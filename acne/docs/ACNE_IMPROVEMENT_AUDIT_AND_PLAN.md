# Acne detector: audit against the improvement brief, and the plan to approve (2026-10-13)

Status words: **CONFIRMED** = measured on project files. **HYPOTHESIS** = reasoned, not tested. Nothing in this document has been run since the last experiment batch (A0-A7, C1-C3); no existing weights, labels, datasets or app files were changed by it.

## 0. A correction to the numbers in the brief
The brief lists "Validation 0.413 / Independent 0.358". In the project files, **0.413 is the 3-seed mean on the 167-photo DEVELOPMENT TEST**, not the validation set. Validation (154 photos) scores 0.425-0.440 per seed. The independent set (41 photos, 437 boxes) was scored once with the single installed model: mAP@0.5 0.358, mAP@0.5:0.95 0.112, precision 0.384, recall 0.524, F1 0.443. The installed detector is now the 3-seed average: development test mAP@0.5 0.439, F1 0.507 (the independent set has not been re-scored with the average).

## 1. The implementation (CONFIRMED)
Official Ultralytics 8.4.171; **YOLOv8s**, pretrained `yolov8s.pt` (COCO), 800 px, up to 100 epochs, early stopping after 30 epochs without improvement, batch 8, optimizer auto (AdamW lr 0.002), cosine LR, `close_mosaic` 10, fliplr 0.5, degrees 10, scale 0.5, hsv 0.015/0.5/0.35, seeds 42/7/123. Records: `lesion_experiments/EXPERIMENTS.csv` (settings, data fingerprint, scores), run folders, `acne/notebooks/PrecisionSkin_Acne_Training_and_Results.ipynb`.
**Model size (CONFIRMED):** the earlier 3-class detector used YOLOv8n at 640 px (acne mAP@0.5 0.22). YOLOv8m at 800 px (A6) scored 0.426 vs 0.425 for YOLOv8s: higher precision (0.52), lower recall (0.45), 2x training time, slower inference. So a larger model is not beneficial here.

## 2. Dataset and annotation audit (CONFIRMED unless marked)
- Acne dataset v4: 1,145 train / 154 val / 167 test photos; 11,621 / 1,562 / 1,369 boxes; one class; YOLO format. Median box side 0.064 of the photo (about 28 px); 90 % of boxes below 0.104.
- **Individual lesions, not grouped regions:** only 0.9 % of train+val photos have a box more than 2.5x the photo's median, 0.2 % have a box over other boxes, no duplicate boxes, aspect ratio about 1.0.
- Possible missing labels: 142 photos (10.9 %) where the model sees confident spots with no box (111 train, 31 val). A 60-photo relabel (A7) raised boxes 893 -> 1,430.
- Leakage: exact and near-duplicate checks done when the sets were built (leak_check, 8 rotations/flips correlation). **Patient-level leakage cannot be excluded** (no patient IDs in the sources) = HYPOTHESIS-level risk to state in the thesis.
- Test sets: development test photos median 22 px boxes; the independent set photos are small (median 306 px short side) and denser (10.7 boxes/photo); only 2 negative photos.
- Annotation strategy was not changed.

## 3. Why mAP@0.5 is limited (evidence)
| Cause | Evidence |
|---|---|
| Tiny lesions | recall at IoU 0.5: small 0.31, medium 0.57, large 0.57 (validation) |
| Crowded photos | recall 0.39 on photos with 15+ boxes vs 0.61-0.65 on sparser ones; 57 % of boxes are in such photos |
| Strict IoU on tiny boxes | F1 0.49 at IoU 0.5, 0.36 at 0.6, 0.20 at 0.7; a 28 px box shifted 2 / 3 px scores IoU 0.87 / 0.81 |
| Label agreement | the same annotator agrees with their earlier boxes at F1 0.30 (IoU 0.5), 0.56 (IoU 0.3); the model agrees with the original boxes at 0.48. Scaling the boxes explains little (best 0.35 at 0.8-0.9x) |
| Missing labels | 10.9 % of photos flagged; relabel test +0.013 mAP, recall +0.05, but photo AUC 0.966 -> 0.951 |
| Negatives | 21 % of 96 clear-skin photos get a false box at the app threshold (0.275); 8 % at 0.40 |
| Not acne-specific | 54 % of eczema, 29 % of hyperpigmentation, 15 % of healthy photos get boxes (0.35) |
HYPOTHESES not yet tested: tiling / overlapping crops, disabling mosaic, smaller scale augmentation, close-up vs full-face split (photos are of mixed resolution; no close-up flag exists).

## 4. Experiments already run (validation, seed 42, baseline A0 = 0.4254, F1 0.494)
| Run | Change | mAP@0.5 | P | R |
|---|---|---|---|---|
| A1 | + 115 negatives | 0.431 | | |
| A2 | milder colour jitter | 0.424 | | |
| A3 | box loss 10 | 0.431 | | |
| A4 | lr 0.0005 AdamW | 0.433 | | |
| **A5** | **960 px** | **0.442** | | |
| A6 | YOLOv8m | 0.426 | 0.52 | 0.45 |
| A7 | 60 photos relabelled | 0.439 | | 0.547 |
| C1 | + 250 new photos | 0.433 | 0.51 | 0.48 |
| C2 | C1 + 111 reviewed photos | 0.431 | 0.49 | 0.50 |
| C3 | C2 + negatives | about 0.424 (running) | 0.49 | 0.47 |
No single change passed the +0.02 rule. Largest: 960 px (+0.017), relabel (+0.013). Seed std on validation about 0.006. The 3-seed average gives +0.02 (validation 0.460; development test 0.439 vs 0.419).

## 5. Reproducibility and test protection
- Three seeds with mean/std and individual scores, EXPERIMENTS.csv, fixed splits: DONE.
- **Limitation to state:** the 167-photo development test influenced earlier decisions (threshold 0.35 was test-informed; later 0.26/0.275 from validation) and has been scored several times. The 41-photo independent set was scored once. **A fresh, untouched final test set is recommended** (see plan).

## 6. Proposed plan (needs approval)
**Phase 1, validation only, seed 42 (about 40-70 min each, in notebooks with saved outputs):**
| ID | Change vs A0 | Why | Cost |
|---|---|---|---|
| E1 | 960 px + lr 0.0005 AdamW | the two best single changes together | about 60 min |
| E2 | E1 + reviewed/new labels (C2 data) | label completeness | about 60 min |
| E3 | mosaic off (close_mosaic = all epochs) | mosaic may hurt tiny spots | about 40 min |
| E4 | scale 0.25 instead of 0.5 | scale augmentation shrinks tiny spots further | about 40 min |
| E5 (optional, offline only) | 2x2 overlapping tiles at 640 | spots 2x larger; needs tiling at inference = C# change, not allowed without approval | about 2 h + coding |
Rule: adopt a change if mAP@0.5 +0.02, precision and recall not down by more than 0.02, photo AUC not down. Combine only winners.
**Phase 2:** best configuration with 3 seeds in a notebook; 3-model average; threshold on validation; development test once.
**Phase 3 (fresh test, you box):** 100 new photos never used before, including at least 30 clear-skin negatives, boxed from scratch, scored once. This is the set the 0.50 claim must be made on.
**Expected, honestly:** validation up to about 0.47-0.49 with the average; on a fresh test about 0.40-0.45. Reaching 0.50 on a fresh test with tight boxes is unlikely; it is possible only under a documented larger-box (margin) convention (see ACNE margin check, not yet run), which must be reported next to the tight-box score.

## 7. Compatibility
ONNX opset 12, 800 px (960 px models must be re-exported with imgsz 960 and the app's `imgsz` metadata), box coordinates and the specialist loading are unchanged. The app already runs the acne model as a 3-model average. No change to the classifier, hyperpigmentation or eczema detector, weights or app is proposed without your approval.
