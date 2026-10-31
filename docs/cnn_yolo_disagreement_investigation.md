# CNN vs YOLO disagreement - diagnostic findings

Scope: two saved single-photo scans (same promotional "before" photo family, uploaded): scan `215135_75b50fed`
(CNN Eczema 69.6 / Acne 30.3 / Hyper 0.075 / Normal 0.0; YOLO: two hyperpigmentation boxes) and scan `213246_2a97651a`
(CNN Normal 50.3 / Acne 42.8 / Eczema 6.4 / Hyper 0.5; YOLO: 2 acne + 2 hyperpigmentation boxes).
Nothing was retrained; no threshold, weight or record was changed.

## How to reproduce

```
SkinDevApp.exe --diagnose <image> <outDir> [repeats]        # headless C# replay, no camera / stabiliser / database
python shared/cnn_diagnose.py <outDir>                      # Python ONNX Runtime parity + detector re-run + overlap analysis
dotnet test SkinDevApp.Tests                                # preprocessing, replay and overlap tests
```

## Findings

| Question | Evidence | Verdict |
|---|---|---|
| Same weights in app and Python? | SHA-256 `eea31c7f...0446ac746` in the app, in Python, in the saved record.json and in the notebook-exported model | identical |
| Does C# preprocessing equal the V5.2.1 notebook? | Notebook eval = `A.Resize(224,224)` (bilinear, plain stretch) then `/127.5 - 1`, RGB; ONNX check cell uses the same. C# = BGR->RGB, bilinear 224, `(p/127.5)-1`, NHWC. Tensor rebuilt in Python from the same file differs by at most 1.2e-7 (float rounding) | equivalent |
| Does the deployed ONNX equal Python ORT? | Same C# tensor into Python ORT: max probability difference 4e-10 (scan 1), 3e-8 (scan 2). record.json also stores the Keras service result: Eczema 70.2 vs ONNX 69.6 | equivalent |
| Is replay deterministic? | 5 repeats bitwise identical, detectors identical on repeat; the replay reproduces the saved database scores exactly | deterministic; no stabiliser or camera in the path |
| Do the YOLO boxes come from a coordinate / confidence bug? | Independent Python letterbox + decode + per-class NMS + fusion reproduces the app's boxes exactly (same boxes, same confidences) for acne and hyperpigmentation | no bug found in this logic (see limitation) |
| Are the two hyperpigmentation boxes duplicates? | Fused boxes: IoU 0.32, the smaller lies 89% inside the larger -> **Nested**, not a duplicate by IoU. One ensemble member (m2) itself returns a pair with IoU 0.56 / 100% contained that its own per-class NMS (0.6) keeps | overlapping boxes, mostly one patch boxed at two scales |
| Why do they survive? | Per-member NMS threshold 0.6 is loose; ensemble fusion has no final NMS; IoU-based NMS cannot remove a nested box | explains the overlap; thresholds not changed |

## Sensitivity of the CNN on the Eczema-69.6% photo (informational, not used by the app)

| Variant | Acne | Hyper | Eczema | Normal |
|---|---|---|---|---|
| as deployed | 30.3 | 0.08 | 69.6 | 0.0 |
| from the 1280 px original | 26.8 | 0.09 | 73.1 | 0.0 |
| INTER_AREA | 26.3 | 0.09 | 73.6 | 0.0 |
| mirrored | 22.7 | 0.11 | 77.2 | 0.0 |
| centre 80% crop | 56.6 | 1.39 | 41.9 | 0.0 |
| channels swapped (deliberately wrong) | 6.6 | 84.9 | 8.4 | 0.03 |

The Normal/Acne photo moves from Normal 50 to Normal 72 (mirrored) or Acne 53 (crop). The top class is not stable under
mirroring or a mild crop, and the photo is strongly colour dependent.

## Conclusion

1. **Preprocessing inconsistency: not supported.** Hash, tensor and runtime parity are all exact to float precision.
2. **Duplicate detections: partly.** The two boxes overlap, but they are nested (one inside the other), not true duplicates; this comes from loose per-member NMS and no post-fusion NMS. It does not explain the CNN's low hyperpigmentation score.
3. **Unresolved model disagreement: yes.** The classifier is a single-label softmax trained on images each labelled with exactly one class (3000 per class, no co-occurrence), so a photo with several conditions has to be squeezed into one class. On held-out in-distribution test data it recalls hyperpigmentation at 0.95 and confuses Acne -> Eczema most (59 of 465), so Acne/Eczema confusion on an out-of-distribution promotional photo is consistent with its known weak spot. The photo is a side profile with a colour cast, a text overlay and no detected face, probably unlike the training images. This is a hypothesis; it is not proven here.

## Limitations

- The Python detector re-run mirrors the C# algorithm, so it proves determinism and ONNX-runtime parity, not that the algorithm equals Ultralytics' own NMS. Compare against Ultralytics `predict` on the same image with the same weights before changing any NMS setting.
- Only two scans were examined. The training dataset's preprocessing (`dataset_processed`: crops? resizing?) is not in the notebook; check how those images were made.
- No labelled ground truth exists for these photos, so which model is right is unknown.

## Suggested next steps (not done)

- Evaluate the CNN on a dermatologist-labelled, multi-condition set before deciding on multilabel classification.
- Run the YOLO detectors on the classifier's test set to measure how often hyperpigmentation boxes appear on photos the CNN calls Eczema/Acne.
- Only then consider a post-fusion NMS or containment filter.
