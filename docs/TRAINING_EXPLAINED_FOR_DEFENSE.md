# How the detectors were trained: explained for the defense

## The 60-second answer
We trained one-class **YOLOv8s** detectors, one for acne and one for hyperpigmentation. YOLOv8s is a pretrained object-detection network; we fine-tuned it on face photos that the researcher boxed by hand (one box per spot or patch).
Training ran on a personal computer (NVIDIA RTX 4060, 8 GB) with the Ultralytics library, 800 px input, up to 100 epochs, three random seeds per detector.
Models and confidence thresholds were chosen on a separate **validation** set. The result was then measured **once** on a **test** set that was never used for training or choosing. The final detector averages the three seeds (weighted box fusion).

## The pipeline in five steps
1. **Data.** Photos with box files (one text file per photo; one line per box).
   - Acne: 1,145 train / 154 validation / 167 development-test photos, plus a 41-photo independent set boxed blind and scored once.
   - Hyperpigmentation: 808 training photos corrected under annotation rule v2 (each shown 3 times per epoch) + 1,260 de-duplicated public photos; 60 validation and 100 test photos boxed under rule v2.
2. **Training.** For every image the network predicts boxes and confidences, the loss compares them with the researcher's boxes, and the weights are updated. After each epoch the validation photos are scored; the best epoch is kept (`best.pt`).
   Settings: YOLOv8s, 800 px, batch 8, AdamW (lr 0.0005) for hyperpigmentation, cosine learning-rate decay, mosaic mixing off for the last 10 epochs, flips / small rotations / scale / colour jitter, early stopping after 30 epochs without improvement.
3. **Choosing.** One change at a time against a baseline (data, image size, model size, learning rate, starting weights, labels). Choices and the confidence threshold used validation photos only. Every run is in `lesion_experiments/EXPERIMENTS.csv` with its settings and data fingerprint.
4. **Testing.** Test photos scored once, with a 95 % range (resampling photos). Precision, recall, F1 at IoU 0.5, mAP@0.5 and mAP@0.5:0.95, plus face-zone (3x3), photo-level and count results.
5. **Deployment.** The weights are exported to ONNX (opset 12, 800 px) and run inside the C# app (ONNX Runtime). Each detector uses three models and averages their boxes.

## Key numbers
| | Test set | mAP@0.5 | F1 |
|---|---|---|---|
| Acne (3-seed average) | 167 development-test photos | 0.439 | 0.507 |
| Acne, single model, independent set | 41 photos, scored once | 0.358 | 0.443 |
| Hyperpigmentation, installed model (rule v2) | 100 photos | 0.341 | 0.409 |
Hyperpigmentation scores under rule v2 are not comparable with scores under the first rule (the target changed: larger, whole-patch boxes). Update this table when the final hyperpigmentation model is installed.

## Likely questions and honest answers
- **Why is hyperpigmentation lower than acne?** Patches have soft, diffuse edges. A blind re-box of 30 photos agreed with the original boxes at F1 0.28 (IoU 0.5), so strict box-level scores are capped by the labels. A stricter rule (v2) raised agreement to 0.67 (two passes minutes apart, an upper bound; a one-day re-check is pending).
- **Is the test set clean?** Never used for training, model choice or thresholds; near-duplicate and look-alike checks were done. The acne development test did guide some earlier decisions, which is why an independent set was added.
- **Why average three models?** Different random starts miss different spots; boxes that all three agree on are more reliable. The gain was +0.02 to +0.05 mAP@0.5 and costs about three times the inference time.
- **Is it a diagnosis?** No. The detectors propose candidate lesions. Grad-CAM++ shows where the classifier looked; it is not a localization map.
- **What are the limits?** One annotator; tiny spots are often missed (acne); box-level agreement limited by label ambiguity (hyperpigmentation); few acne-negative photos in the independent set; no skin-tone breakdown yet; no real kiosk scans evaluated; speed on the mini PC not measured.
- **Why can't old and new hyperpigmentation numbers be compared?** The box definition changed, so the two sets of numbers measure different targets.
- **Where are the results and code?** `docs/RESULTS_FOR_PAPER.md`, `hyperpigmentation/docs/HYPERPIGMENTATION_FULL_RESULTS.md`, the two `*_Training_and_Results.ipynb` notebooks, `shared/lesion_exp.py`, `lesion_experiments/EXPERIMENTS.csv`.

## Be ready for
- Stating how the pipeline was built, including use of an AI coding assistant, in the way your school's policy requires. Be able to explain every step above in your own words.
- Knowing the headline numbers and the limits by heart.
