# PrecisionSkin – Methods & Limitations Notes (working draft)

> Written from the work done so far. **Numbers marked ✅ were measured**; items marked ⏳ are still pending – fill them in, don't guess.
> Edit freely; this is a starting point for the paper, not final text.

---

## 1. System in one paragraph

PrecisionSkin classifies a facial photo into four classes (Acne, Hyperpigmentation, Eczema, Normal) with a MobileNetV2 classifier (`facial_only` run).
Two **independent** pieces of visual evidence are shown to the researcher for the same captured frame:

1. **Grad-CAM++** – which image regions influenced the classifier's score for each class (explanation of the classifier).
2. **Lesion detector** (YOLOv8) – candidate lesion locations (acne, hyperpigmentation, eczema boxes), a *separate model* trained on box-annotated data.

The two are never combined into one claim: the heatmap is not a lesion detector, and the detector is not an explanation of the classifier.

---

## 2. Grad-CAM++: what was checked ✅

**Question raised:** the four class maps in the Comparison Gallery looked almost identical.

**What was verified (service `gradcam_service.py`, model `precisionskin_best.keras`):**
- Each class map uses the gradient of **its own** pre-softmax logit (Acne 0, Hyperpigmentation 1, Eczema 2, Normal 3). The all-class path was compared with four separate single-class computations: **maximum difference = 0**.
- C# side (`Gradcamservice.cs`, `ClassHeatmapRenderer.cs`): each map is decoded by class index and coloured with its own colour; colour does not decide where a map appears.
- Startup parity check added: the Grad-CAM forward path must reproduce `model(x)` (fails fast otherwise).

**Why the maps still look alike (measured on 12 captures):** raw (un-normalised) class CAMs, mean pairwise Pearson correlation:

| Target layer | Grid | Mean correlation between classes |
|---|---|---|
| `Conv_1` (used) | 7×7 | 0.947 |
| `out_relu` | 7×7 | 0.954 |
| `block_13_expand_relu` | 14×14 | 0.984 |
| `block_6_expand_relu` | 28×28 | 0.989 |
| `block_3_expand_relu` | 56×56 | 0.992 |

Earlier layers give finer maps but make the classes **more** alike (they respond to image structure, not class evidence).
The head is global-average-pool + three Dense blocks, so every class map is a re-weighting of the same activation maps (cosine similarity of the Acne/Hyperpigmentation/Eczema output weights: 0.29–0.53).

**Conclusion to state:** the maps are correctly computed class-specific attributions; their similarity is a property of the trained classifier (it relies on shared facial features for the three skin conditions), **not** a bug. Grad-CAM++ is not lesion segmentation, and a highlighted region is not proof of affected skin.
Each map is normalised separately (compare shape, not brightness).

---

## 3. Classifier data (context) ✅

- 12,000 images, 3,000 per class; split 8,793 train / 1,603 validation / 1,604 test (`master_dataset.csv`).
- All images pass the project's facial QA (`facial_primary` 10,367, `facial_aux_train` 1,633), but "facial" includes tight lesion-region crops:
  full face 1,346 · partial face 3,324 · close-up facial 2,221 · lesion-region facial 4,266 · unknown 830 · non-facial 13.
- ⏳ Classifier test accuracy / confusion matrix: add from the `facial_only` run (not reproduced here).
- Live webcam frames are mostly full faces, a different mix from the training set (domain gap) – see limitations.

---

## 4. Detector data

### 4.1 Sources (all Roboflow Universe exports, YOLO format, licence CC BY 4.0 as stated in each `data.yaml`) ✅

| Source folder | Images | Unique photos* | Boxes | Original labels |
|---|---|---|---|---|
| ACNE Detection v2i (BEAUTYAI, `acne-detection-mph4m` v2) | 6,257 | ~1,800 | 80,950 | `0` |
| acne-detection v2i (Sarah Kiyani) | 4,859 | ~2,350 | 31,409 | `0,1,2,acne,pimple,spot` |
| hyperpigmentation yolov10 v3 | 2,250 | ~770 | 5,271 | `hyperpigmentation`, `redness` (1 box) |
| Pigmentation v1i (Tinnaluris Workspace) | 1,634 | ~960 | 5,293 polygons | `0` |
| hyperpigmentation v2i (Goat) | 1,169 | ~300 | 2,850 | `Hyperpigmentation` |
| Hyperpigmentation v1 | 500 | ~380 | 2,014 | `Hyperpigmentation`, `3` |
| Eczema v1 facial | 348 | ~332 | 714 | `Eczema` |
| **Total audited** | **17,017** | **5,731 duplicate groups (after merging copies across sources)** | 123,208 boxes + 5,293 polygons | |

\*per-source unique-photo counts are from name grouping; the authoritative number is the duplicate-group count after cross-source merging.
⏳ Fill in exact project names/URLs/citations for every source (Roboflow shows them on each project page).

All boxes in these sources were **drawn by the original dataset annotators** (human). Polygon labels (Pigmentation v1i) were converted to their enclosing boxes.

### 4.2 Label decisions (each checked by eye on sample images) ✅

| Source · original label | Decision | Evidence |
|---|---|---|
| ACNE Detection · `0` | Acne | pimples/blemishes (many grayscale/noisy augmented copies) |
| acne-detection v2i · `0`,`1`,`2` | Acne | small pimple-like spots on faces |
| acne-detection v2i · `spot` | Acne (**least certain**) | small blemish-like boxes; a few may be pigmentation |
| Hyperpigmentation v1 · `3` | Hyperpigmentation | genuine melasma / sun-spot patches (not junk) |
| Pigmentation v1i · `0` | Hyperpigmentation | brown cheek patches |
| yolov10 · `redness` | dropped | 1 box in the whole dataset |

Original label, source and mapping history are kept for every annotation (`annotations_long.csv`).

### 4.3 Audit (notebook `PrecisionSkin_Localization_Dataset_Audit`, originals read-only) ✅

- 0 corrupted images; 34 images with no annotation; 0 annotation files without an image.
- **Duplicate/near-duplicate groups.** Augmented copies (flips, rotations, grayscale, noise) and the same photo in several datasets are common (95% of images are in a multi-image group).
  A first method (64-bit perceptual hash, Hamming ≤ 6) both missed real copies (same-photo pairs differed by up to 32 bits) and chained unrelated photos into **one 6,364-image group**.
  It was replaced after calibration on the data: **32×32 grayscale thumbnail correlation ≥ 0.97 against all 8 rotations/flips**, plus identical-file and same-Roboflow-name links, with a safety cap of 150 images per group.
  Result: **5,731 groups, largest 51 images, 642 groups spanning several sources, 23 groups (295 images) containing the same photo under two conditions** (excluded from training and evaluation).
- **Anatomical site.** A face detector (YuNet) found a face in 68% of images (11,645/17,017). The rest are mostly cheek/nose close-ups. After browsing ~16 random images of each, **six sources were declared facial by the researchers** (basis recorded as "declared by you for this source").
  Roughly 1 in 6 sampled images of the two acne sources and Pigmentation v1i looked unclear (could be neck/back). → state this as a documented assumption.
- Flagged for review: 504 images (295 same photo under two conditions, 115 dark-vignette/dermoscopy-like, 34 very large box, 34 empty label file, 22 low resolution, 8 zero-area box). **16,513 kept.**

---

## 5. Detector training protocol

### 5.1 Dataset construction (notebook `PrecisionSkin_Detector_Export_and_Train`) ✅

- Removed: zero-area boxes, corrupted images, low-resolution images, photos under conflicting conditions (324 images); boxes thinner than 3 px (52).
- **Split by duplicate group** (all copies of a photo in one split; asserted in code). Seed 42.
- **Validation and test contain facial, kept images only, one image per photo** (highest-resolution copy) so augmented copies do not inflate scores.
- Training: at most 3 copies per photo; **eczema training images used 3× (oversampling of the weakest class), training split only.**
- Test set: **566 photos – acne 370, hyperpigmentation 164, eczema 32.** Boxes are the source annotators' (human) boxes.
- Every image's box origin is recorded in `dataset_manifest.csv` (`human-drawn (source dataset annotators)`; later `machine-drafted, human-corrected` for the eczema extension).

### 5.2 Training ✅ (first run)

YOLOv8n, COCO-pretrained, 640 px, batch 16, 2 workers, up to 100 epochs with early stopping (patience 25), cosine LR, mosaic closed for the last 10 epochs, left-right flip 0.5, rotation ±10°, scale 0.5, HSV jitter (0.015/0.5/0.35), seed 42.
Hardware: RTX 4060 Laptop GPU (8 GB). Software: ultralytics 8.4.172, PyTorch 2.14.1 (CUDA 13.0). Stopped at epoch 82 (best epoch 66), ≈ 1 h 25 min.

### 5.3 Results – first detector (640 px), test split ✅

| Class | Test photos | Precision | Recall | mAP@0.5 | mAP@0.5–0.95 |
|---|---|---|---|---|---|
| Acne | 370 | 0.50 | 0.35 | 0.36 | 0.11 |
| Hyperpigmentation | 164 | 0.42 | 0.37 | 0.32 | 0.13 |
| Eczema | 32 | 0.35 | 0.46 | 0.37 | 0.13 |
| All | 566 | 0.42 | 0.39 | 0.35 | 0.12 |

Best F1 over confidence: 0.39 at 0.175 (acne peaks ≈ 0.16–0.2; hyperpigmentation/eczema hold up to ≈ 0.35). **CPU latency (ONNX, 4 threads, this laptop): median 31.5 ms (p95 41.3 ms).**
Visual check: boxes land on real acne spots, an eczema patch and pigmented patches; many lesions are missed and confidences are low (0.25–0.5).

### 5.4 Larger images and longer training (runs B and C) ✅

Same data and split as 5.3. A 960-px run (B, 79 epochs, best epoch 54) reached validation mAP@0.5 = 0.319 / mAP@0.5–0.95 = 0.114, and a full 100-epoch 640-px run (C, no early stopping) reached 0.333 / 0.116 at its best epoch (66) and 0.299 / 0.109 at the last epoch (overfitting after epoch 66). **Neither helped; run A (5.3) is the deployed model.** Test scores for B and C were not computed.

### 5.5 Eczema extension (dataset v2) ✅ – result: **no improvement; v1 kept**

**Method.** The v1 detector drafted eczema boxes (confidence ≥ 0.20) on 338 additional unique facial eczema photos from the classifier's own folders (exact duplicates removed). The researchers corrected every image in a local box editor (adjust, add, delete, or mark "no eczema"). **Draft quality: 96.7 % of draft boxes were kept (IoU ≥ 0.5) but the draft found only 36.1 % of the final boxes** (recall); 326 images had at least one box (895 final boxes) and entered a new source, `eczema_corrected.yolov8`, whose boxes are labelled `machine-drafted, human-corrected` in the manifest and used for **training only**.
Dataset v2 = v1 + these images (322 after the audit's duplicate handling): training photos 9,868 → 10,190, eczema training photos 277 → 599 (still oversampled ×3). Everything else, including the recipe, was unchanged (YOLOv8n, 640 px, batch 16, 100 epochs, patience 25, seed 42). **Validation and test sets were pinned to exactly the same 566 + 566 photos as v1** (checked by source and original file name: identical), so the two models are compared on the same images.
Run details: all 100 epochs ran (best epoch 55, validation mAP@0.5 0.308 / mAP@0.5–0.95 0.110). The run was interrupted by a memory stall of the laptop at epoch 78 and **resumed from the saved checkpoint (`last.pt`) with the same settings**.

**Test results (566 photos: acne 370, hyperpigmentation 164, eczema 32):**

| Class | Metric | v1 (deployed) | v2 | Change |
|---|---|---|---|---|
| All | mAP@0.5 | 0.349 | 0.303 | −0.046 |
| All | mAP@0.5–0.95 | 0.122 | 0.107 | −0.015 |
| Acne | mAP@0.5 | 0.357 | 0.358 | 0.000 |
| Hyperpigmentation | mAP@0.5 | 0.323 | 0.312 | −0.011 |
| **Eczema** (n = 32) | mAP@0.5 | 0.368 | 0.241 | **−0.127** |
| Eczema | precision / recall | 0.354 / 0.464 | 0.235 / 0.385 | −0.120 / −0.080 |
| All | CPU latency (median, 4 threads) | 31.5 ms | 35.1 ms | similar |

Full table: `06_training_runs/v1_vs_v2_test_comparison.csv`. **Decision: v1 stays in the app** (`lesion_detector_v1.onnx`). v2 is archived as `lesion_detector_v2_yolov8n_imgsz640.onnx/.json`.
**Interpretation (hypotheses, not tested):** the drafts came from the v1 detector, so lesions it missed (draft recall 36 %) are probably unboxed in these images; training on partly unboxed images teaches the model to treat real lesions as background and lowers precision. The extra eczema images also changed the class balance. With only 32 eczema test photos and one seed, part of the change may be run-to-run noise (not measured; would need seeds 7 and 123). Acne and hyperpigmentation were not touched by the extension and stayed the same, as expected. This is a negative result and should be reported as such: machine-drafted boxes did not improve eczema detection in this study.

### 5.6 Still pending ⏳

- Latency on the CPU-only Mini PC (i5, 4 cores, 8 GB).
- Overlap analysis Grad-CAM++ vs detector boxes (exploratory only).
- Validation of the detector on the team's own captures by a dermatologist (small set).
- Optional: more complete eczema boxing (all lesions in the 326 images) and a 3-seed repeat to separate noise from effect.

---

## 6. Limitations (state these plainly)

1. **Grad-CAM++ is attribution, not localisation.** The classifier makes a global image-level decision (global average pooling); highlighted regions need not be diseased skin.
2. **Class maps are similar** because the classifier uses largely shared facial features for the three skin conditions (measured correlation 0.95–0.99). This is a model property, not a bug.
3. **Detector accuracy is modest** (mAP@0.5 ≈ 0.35; localisation precision mAP@0.5–0.95 ≈ 0.12). It finds a good share of lesions and misses many. Treat boxes as *candidate* locations, not a diagnosis or a count.
4. **Label quality is limited by the public sources.** Annotators boxed acne, pigment patches and eczema inconsistently (very large boxes for pigment patches, tiny boxes for acne). Part of the low score is label noise, not only model error.
5. **Eczema is a pilot class:** 324 unique human-boxed photos, 32 test photos (wide uncertainty). Report it separately. One seed only; the v1 → v2 eczema change (−0.127 mAP@0.5) could partly be noise.
6. **Facial status is partly assumed:** six sources were declared facial from small visual samples, not verified per image.
7. **Domain gap:** training mixes close-ups, crops and full faces from public datasets; live webcam frames are different (lighting, resolution, skin-tone mix). Only informal checks were done on live captures.
8. **Duplicate handling is heuristic** (thumbnail correlation + names). It was calibrated on this data and visually checked, but is not perfect.
9. **Skin-tone coverage is not quantified** for the detector datasets (no Fitzpatrick labels). Performance across skin tones is unknown.
10. **Research prototype; not a medical device.** No claim of clinical accuracy.
11. **Machine-assisted labels:** drafted by the v1 detector and corrected by researchers, they inherit its misses (draft recall 36 %): lesions it did not find may remain unboxed. They were used for **training only** (the test set stays human-drawn) and **did not improve** eczema detection (5.5).

---

### 6.1 Fitzpatrick skin-type model – discontinued (negative finding) ✅

- A MobileNetV2 skin-type model was trained on a public face-photo set with six folders (Types I–VI, 3,000 images per type, split by duplicate group). Validation: exact accuracy 51.9 %, within one type 79.8 %, Type I recall 0.009, Type IV recall 0.173. The frozen test split was never scored.
- Diagnosis (validation and training data only): Types I and II could not be told apart (AUC 0.48); 78 % of both were predicted as Type II. Types II/III and III/IV separated almost perfectly (AUC 0.97/0.96) although median skin colour (ITA) does not support that order. Visual inspection showed the six folders follow apparent ethnicity groups, not measured skin tone. The model therefore learned facial/ethnic appearance, not skin phototype. No train-validation leakage was found.
- Decision: the AI Fitzpatrick module was removed from the system. It was not connected to the disease classifier or the lesion detector, so their results are unaffected. Skin phototype in the app is recorded only as a participant/researcher-entered value (screening, Types III–V) and a dermatologist-assigned value; the software does not estimate it.
- Consequence for claims: the classifier was not evaluated per skin tone (the classifier data has no Fitzpatrick labels). Do not state that it was validated across Fitzpatrick types; stratified results can only come from the dermatologist-assigned types of the participant pilot.

## 7. Ethics / data handling

- Training data are public CC BY 4.0 Roboflow datasets: keep attributions (project, author, URL) in the paper's references.
- Webcam captures are real participants' faces: keep them out of git and online tools; use local annotation tools; follow the consent terms of the study.
- Reports use participant IDs (PS-xxxx), not names.

---

## 8. Reproducibility checklist

- Audit reports: `localization_dataset/01_audit_reports/` (`master_metadata.csv`, `annotations_long.csv`, `all_issues.csv`, `label_mapping_proposal.csv`, `review_decisions.csv`, `duplicate_groups.csv`, `audit_report.json`).
- Datasets: `detector_work/dataset_v1/` (deployed) and `dataset_v2/` (eczema extension), each with `dataset_manifest.csv` (split, source, group, box origin per image). Seed 42; v2 validation/test pinned to v1's photos.
- Runs: `detector_work/runs/…`, exports: `detector_work/export/` (ONNX + JSON with metrics, speed, SHA-256).
- Notebooks (repo root): `PrecisionSkin_Localization_Dataset_Audit.ipynb`, `PrecisionSkin_Detector_Export_and_Train.ipynb`, `PrecisionSkin_Eczema_Draft_and_Review.ipynb`, `PrecisionSkin_Eczema_v2_Pipeline.ipynb` (one-run pipeline for v2), `PrecisionSkin_Eczema_v2_Finish.ipynb` (test evaluation + export after the resumed training); Grad-CAM study: `gradcam_layer_study.py`.

---

## 9. Suggested wording (adapt)

> We trained a separate YOLOv8 lesion detector on public, human-annotated bounding-box datasets (17,017 images, 5,731 unique photo groups after merging augmented and duplicated copies across sources).
> Splits were made by photo group; validation and test images were facial, de-duplicated to one image per photo, and their boxes were drawn by the original dataset annotators.
> The detector reaches mAP@0.5 = 0.35 on 566 held-out photos (acne 0.36, hyperpigmentation 0.32, eczema 0.37; eczema n = 32), and runs in ≈ 32 ms per image on a laptop CPU.
> It is intended to indicate candidate lesion locations alongside, and independently of, the Grad-CAM++ attribution of the classifier, which explains the classifier's score but does not localise lesions.
> An extension with 322 machine-drafted, human-corrected eczema images (training only) did not improve the detector on the unchanged test photos (eczema mAP@0.5 0.37 → 0.24; overall 0.35 → 0.30), so the original detector was kept.
