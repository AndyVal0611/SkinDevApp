# Lesion detector: diagnosis, findings and experiment plan (part 1 of the final report)

Scope: acne, hyperpigmentation and eczema YOLOv8 detectors. Read-only diagnosis first; nothing was retrained or changed before the findings below.
The disease classifier (MobileNetV2) and the Grad-CAM++ service were not touched. Grad-CAM++ is attribution; localisation here means the detector's boxes only.

## 0. Integrity and reproducibility
- Originals preserved: datasets are read-only; baseline weights, results.csv and args of the current models are copied to `lesion_experiments/baselines/` (314 MB).
- Experiments run from file lists in `lesion_experiments/runs/<id>/` (spec.json, train_list.txt, data.yaml, weights, curves). Registry: `lesion_experiments/EXPERIMENTS.csv`.
- Selection uses VALIDATION photos only. The test sets are opened once, at the end, by `lesion_final_eval.py` for the configuration chosen on validation.
- Disclosure: the acne test photos and the hyperpigmentation hand-boxed test photos were scored in earlier iterations (baseline reporting). The new experiments do not use them for any decision.
- Code: `lesion_diagnose_data.py`, `lesion_diagnose_models.py`, `lesion_exp.py`, notebook `Pipeline V5 - Lesion Detector.ipynb` (same stages as the classifier pipeline; smoke-tested).

## 1. Baseline (current models)
| Detector | Test set | mAP@0.5 | mAP@0.5-0.95 | P | R | F1 |
|---|---|---|---|---|---|---|
| acne v4, 3 seeds (mean) | 167 hand-boxed photos | 0.413 (std 0.006, 95% range 0.37-0.46) | 0.122 | 0.43 | 0.54 | 0.48 |
| acne v1 (3-class, in the app before) | same | 0.220 | 0.066 | 0.36 | 0.23 | 0.28 |
| hyperpigmentation s_800 | 100 hand-boxed photos | 0.178 | 0.051 | 0.30 | 0.18 | 0.22 |
| hyperpigmentation v1 | same | 0.053 | 0.019 | 0.19 | 0.05 | 0.08 |
| eczema v1 (only detector that exists) | public test, 32 photos | not re-measured | | | | |

## 2. Findings (causes, from the actual files)

### Hyperpigmentation (largest problems)
1. **Annotation-style mismatch.** Public training boxes: median side 12.6 % of the photo, 17 % of boxes larger than 30 % of the photo, boxes cover 18 % of the photo. Your boxes: median 9.6 %, 3.4 % larger than 30 %, coverage 9 %. Your hand-boxed test set matches your style (coverage 7.7 %). Public boxes are about 2x coarser and make up 2,277 of 3,031 training photos.
2. **Validation set is in the public style, the target is your style.** The confidence threshold and "best model" were chosen on public validation photos (coverage 17 %, 20 % huge boxes), not on photos boxed with your rule. Public val mAP@0.5 0.34 versus hand-boxed test 0.18 is this mismatch, not generalisation to real scans.
3. **Heavy duplication.** 2,524 of 3,031 training photos have a near-duplicate (corr >= 0.95; 1,734 pairs) (Roboflow augmentations plus your photos repeated x2). Effective diversity is far below the photo count, and duplicates make training loss look better than it generalises.
4. **Look-alikes between splits:** public val 15/164, public test 16/164, hand-boxed test 6/100 photos have a look-alike (corr >= 0.90) in training. Scores on those are optimistic; a leak-free re-evaluation is part of the final evaluation.
5. **The model-size comparison was invalid.** The earlier n_640 and m_800 hyperpigmentation runs stopped after 7 and 9 epochs (results.csv); only s_800 trained 100 epochs. "Medium is worse" was never tested. Re-run in the queue (H4, H5).
6. **Low-resolution source photos.** 41 % of your hyperpigmentation photos and 51 % of the hand-boxed test photos are under 400 px (median 224 px for the test set) versus 768 px for the public photos.
7. Overfitting signal: validation loss rises 0.33 after its minimum (epoch 65) while training loss keeps falling; val/train loss ratio 1.6.

### Acne
1. **Data is clean and consistent:** no near-duplicates in training (0 pairs), 5 val and 2 test look-alikes, no doubled boxes (0-0.4 % overlapping), box size consistent across sources (median 5.4-6.8 % of the side); drafted photos have fewer tiny boxes (0.3-0.4 % vs 2.5 % for from-scratch photos).
2. **Tiny and small spots are the weak point:** recall 20 % for spots under 2 % of the photo side, 40 % for 2-4 %, 53-62 % above that.
3. **False boxes:** at conf 0.25 the model draws 10.1 boxes per photo for 8.2 true boxes (precision 0.43). F1 is flat at 0.47-0.48 between conf 0.20 and 0.40 (precision 0.40 -> 0.50 while recall 0.57 -> 0.44).
4. **Mild overfitting:** validation/training loss ratio 1.7-1.8; best epochs 32 / 50 / 78 for the three seeds and validation mAP@0.5 at the last epoch 0.03 below its best (0.404 vs 0.432), so selection of best.pt matters and training beyond about epoch 60 does not help.
5. **More photos stopped helping** (708 -> 1,145 photos: no gain), so data quantity is not the limit; resolution, model capacity, loss weights or label noise on tiny spots are candidates.
6. **Dataset diversity:** 28-46 % of photos are under 400 px; the 448 More_Faces photos are higher resolution (720x611 median).

### Eczema (out of scope here: your teammate trains it)
- Handover: use `Pipeline V5 - Lesion Detector.ipynb` with `CONDITION = "eczema"` (same stages and sanity checks) and `install_specialist_detector.py --cls eczema`; the app already accepts an eczema specialist and keeps the 3-class v1 for eczema until then.
- Only the 3-class v1 detector exists. Data: 277 training photos (557 boxes), 32 validation, 32 test; 64 % of photos under 400 px; boxes are large (median 25 % of the side, 39 % larger than 30 %), 14 % touch the border. Class imbalance in the 3-class training set: 6,962 acne vs 2,629 hyperpigmentation vs 277 eczema photos (25:1). Eczema cannot be evaluated or improved honestly until your groupmate's reviewed boxes and a hand-boxed test set exist; the specialist pipeline is ready for it (`CONDITION = "eczema"`).

### Combined versus separate detectors
- The 3-class v1 detector was trained on data dominated 25:1 by acne with public box styles, then compared with specialists trained on your data: the specialist advantage (acne 0.22 -> 0.41, hyperpigmentation 0.05 -> 0.18) mixes "separate" with "better data". A clean test needs a 3-class model trained on the same improved datasets; it is queued after the single-factor experiments if time allows.

## 3. Experiment plan (one factor at a time; seed 42 for screening; 3 seeds to confirm winners)
Decision rule on validation: adopt a change if mAP@0.5 improves by >= 0.02 and neither precision nor recall drops by more than 0.02; confirm with seeds 7 and 123.

| ID | Condition | Factor | Why (finding) |
|---|---|---|---|
| H0_mix | hyperpigmentation | baseline, hand-boxed validation | fair reference in the new protocol |
| H1_own | hyperpigmentation | only your boxes | style mismatch |
| H2_dedup | hyperpigmentation | remove near-duplicate public photos | duplication |
| H3_balanced | hyperpigmentation | public limited to as many photos as yours | style dominance |
| A0_base | acne | baseline | reference |
| A1_neg | acne | + 115 healthy-skin negatives | false boxes |
| A2_colorsafe | acne | milder hue/saturation jitter | redness is the signal |
| A3_box10 | acne | box-loss gain 10 | mAP@0.5-0.95 (localisation) |
| A4_lowlr | acne | lr0 0.0005 | overfitting |
| A5_960 | acne | 960 px | small spots |
| A6_m800 | acne | yolov8m | capacity |
| H4_n640, H5_m800 | hyperpigmentation | fully trained n / m | invalid earlier comparison |

Each run records seed, dataset fingerprint, overrides, epochs run, best epoch and validation metrics (EXPERIMENTS.csv). Final stage: the chosen configuration per condition is evaluated once on its test set (mean +/- std over 3 seeds, bootstrap range, leak-free re-evaluation), with PR and F1-confidence curves, confusion/error sheets, CPU latency and ONNX verification (the notebook cells 9-16 do exactly this).

## 4. Status
Queue launched; results are appended to `lesion_experiments/EXPERIMENTS.csv` as each run finishes (hyperpigmentation first). The final report (part 2) lists original performance, problems, modifications, experiment results, best model, remaining limits and recommendations.
