# How we improved the hyperpigmentation detector: step by step (a guide for the eczema detector)

Result of the whole process, on a test set that was never used for training or choosing: **mAP@0.5 0.18 -> 0.51, F1 0.24 -> 0.52** (the numbers are under a redefined box rule, see step 3).
Eczema patches are diffuse and have soft edges, like hyperpigmentation, so the same problems and the same fixes probably apply. Use this as a checklist, not as a promise.

## The one idea behind everything
**Before tuning the model, find out whether your own boxes are reproducible.** If the same person boxing the same photos twice agrees only about 30 %, no model can score much higher than that on strict overlap. Fixing the labels gave the biggest gain; model tweaks gave almost nothing.

---
## Step 0. Set up the rules of the experiment (do this first, it protects your honesty)
1. Three separate photo groups that never mix: **training**, **validation** (choose models and thresholds here), **test** (score once, at the very end).
2. Check that no photo (or near-duplicate: same photo rotated / flipped / re-saved) is in two groups. We compared 32x32 thumbnails with correlation >= 0.90 over 8 rotations/flips (`hyperpig_autolabel.thumb` / `best_corr`).
3. Fix a decision rule **before** looking at results: "a change counts only if validation mAP@0.5 rises by at least +0.02 and neither precision nor recall drops by more than 0.02". Use the same scorer every time.
4. Keep a record of every run (settings, data fingerprint, validation scores): `lesion_experiments/EXPERIMENTS.csv`, written by `shared/lesion_exp.py`.
5. Train in notebooks so the epoch output stays saved in the file (`..._WITH_OUTPUTS.ipynb`).

## Step 1. Diagnose before retraining (about half a day)
Questions that found our real problems (answer them with numbers from your files, not guesses):
- **Box style per source.** Compare box size (as a share of the photo) and how much of the photo the boxes cover, per data source. Ours: public photos' boxes covered 18 % of a photo vs 9 % for our own boxes.
- **Does the validation set match what you will deploy on?** Ours was in the public style, which flattered the old model.
- **Near-duplicates inside training.** 2,524 of 3,031 public training photos had a look-alike, which inflates scores.
- **Did your boxing style drift?** Split your boxed photos in the order you boxed them into 4 groups and compare boxes per photo and median box size. Ours drifted by about 50 % within one session (0.080 -> 0.125).
- **Were earlier comparisons valid?** Ours had two runs that stopped after 7 and 9 epochs and were wrongly compared.
Scripts: `shared/lesion_diagnose_data.py`, `shared/lesion_diagnose_models.py`, `hyperpigmentation/scripts/hyp_v2_train_consistency.py` (the drift check).

## Step 2. Measure how well YOU agree with yourself (30 photos, 20 minutes)
1. Take 30 validation photos, copy them **without their boxes**, shuffle and rename them (`make_hyp_reliability.py` pattern; see `hyperpigmentation/scripts/make_hyp_reliability_v2.py`).
2. Box them again from scratch without looking at the old boxes.
3. Compare the two sets: F1 at IoU 0.5 and 0.3 (`hyp_reliability_v2.py`).
**Ours: F1 0.28 at IoU 0.5** (3.0 vs 6.4 boxes per photo for the same photos). That told us the labels, not the model, were the ceiling.

## Step 3. Write a boxing rule that removes the ambiguity (rule v2)
Our rule, in plain words (`hyperpigmentation/docs/HYPERPIGMENTATION_ANNOTATION_RULE_V2.md`):
- Look at the whole face at normal size; do not zoom in to find more.
- One box per **connected** patch; patches that touch, or are closer than the smaller one's width, are one box.
- Skip marks smaller than about 7 % of the photo's short side.
- Speckled areas: one box only if the area is darker as a whole.
- A box stays inside one facial region; the edge goes where the darkening has faded to about half.
- Usually 1 to 5 boxes per photo; at 8, merge neighbours.
- No model drafts and no old boxes while boxing.
For eczema, write the equivalent (what counts as one patch, how big a patch must be, what to do with scratching marks, where the edge is, which regions) and put a short version into the box tool's `--rule` text.
**Test the rule on a day gap:** box the same 30 photos twice (pass A and pass B) and a third time at least 12 hours later. Compare. **Ours: 0.67 minutes apart (memory inflates it), 0.49 to 0.55 a day later** (vs 0.28 before). Decision rule we set beforehand: >= 0.50 reproducible; 0.40-0.50 adjust the rule once; < 0.40 report the ceiling.

## Step 4. Re-label under the rule (this is where the gain came from)
1. **Evaluation labels first, from scratch, blind:** 100 test photos and 60 validation photos boxed under the rule with no helper (`make_hyp_v2_relabel.py` builds the folders: `V2_Test`, `V2_Val`). About 10 seconds per photo.
2. **Training labels:** convert your old boxes to the new rule **by code** as a starting point (merge boxes that touch or are closer than 1.0 x the smaller box's side; drop boxes under 0.09) = `hyp_v2_transform.py`. Then **correct them by hand** under the rule (`V2_Train`, 808 photos; about 10-15 seconds each).
   - The code conversion alone already helped a lot (H20: mAP@0.5 0.07 -> 0.28 on the rule-v2 test, with no new boxing).
   - **Your first ~200 hand-corrected photos gave most of the gain.** Correcting the other 600 added nothing measurable (H23 0.383 vs H24 0.381). Do 200 first, measure, then decide whether to continue.
3. Check consistency of your new boxes in the order you made them (boxes per photo and median size should not drift): ours stayed at 2.0 boxes per photo, median 0.26.

## Step 5. Add more photos in the same style
- **Public photos helped even though their boxes were coarse** (H25 vs H24: mAP@0.5 +0.07). Use the de-duplicated public photos together with your corrected ones (after converting or leaving them in their own style).
- Model-drafted boxes for **new** photos that you correct are fast. We found that correcting the same photos again adds little; new, different photos add information.

## Step 6. Train (what worked, settings)
- Official Ultralytics YOLOv8s, pretrained COCO weights, 800 px, up to 100 epochs, early stopping 30, batch 8, AdamW lr 0.0005, cosine LR, mosaic off for the last 10 epochs, flips / small rotation / scale 0.5 / colour jitter. Your own corrected photos repeated 3 times per epoch, public photos once.
- **Three seeds (42, 7, 123).** Report each seed, the mean and the standard deviation.
- Run it through `hyperpigmentation/notebooks/PrecisionSkin_Hyperpigmentation_Training_and_Results.ipynb` (set `TRAIN = True`); for eczema copy it and change the condition, the data folders and the names. Takes about 2-3 hours for three seeds on an RTX 4060.

## Step 7. Average the three models and choose the threshold on validation
- Merge the boxes of the three seeds (weighted box fusion, IoU 0.5; confidence = mean over the models). **+0.05 mAP@0.5 on test** for us (code in the notebook toolbox: `fuse`).
- Pick the confidence threshold with the best F1 on the **validation** photos (ours 0.225).

## Step 8. Score the test set once
- Run the notebook's test cell once; it stores the result in a lock file so the number cannot change.
- Report mAP@0.5, mAP@0.5:0.95, precision, recall, F1 with a 95 % range (resampling photos), zone and count results.
- **Ours: mAP@0.5 0.508, mAP@0.5:0.95 0.209, F1 0.520 (range 0.44-0.59), 100 test photos.** The validation score was higher (0.53); test is the honest number.

## Step 9. Measure false alarms on clear skin
- 100 healthy-skin photos, press **X** on those with no lesion at all; count how many get a box. **Ours: 32 % of clear-skin photos got a false box** (68 % specificity). Report it as a limit. `acne/scripts/negatives_eval.py` pattern.

## Step 10. Install in the app (only if it clearly beats the old model)
Export the three seeds to ONNX (opset 12, same image size), install as `lesion_detector_<class>.onnx`, `_m2.onnx`, `_m3.onnx`; set the threshold from validation in the `.json` and the app settings; check that the **app's own C# detector** reproduces the offline score (ours: F1 0.524 vs 0.520). Scan time is about 0.85 s per model.

---
## What did NOT help (so you can skip it)
- A larger input (960 px) and a larger model (YOLOv8m) for hyperpigmentation: +0.01, below the rule.
- Starting from other weights (acne model, the public-photo model): no gain or worse.
- A lower learning rate: looked like +0.028 on one seed, but the 3-seed mean was the same (luck). **Always confirm a gain with more seeds.**
- A second round of drafts on the same photos, and correcting more than ~200 training photos: no measurable gain.

## Pitfalls we hit
- **Scoring on the wrong validation set** (public style) and believing it.
- **Comparing runs that stopped early** or were scored with different scorers (Ultralytics' own mAP reads higher than a custom scorer: say which one you use).
- **Re-boxing minutes apart** gives a falsely high agreement (memory). Wait a day.
- **Changing the label rule changes the target.** Scores before and after are not comparable; say so in the thesis.
- Running two trainings at once on a 6-8 GB machine can crash both.
- Do not tune the threshold or choose a model on the test set; do not score the test more than once.

## What to give the panel
The reliability numbers (before and after the rule), the decision rule, the table of experiments with plain names, the test result with its 95 % range, the false-alarm rate on clear skin, the limits (one annotator, rule-dependent target), and the `_WITH_OUTPUTS` notebooks as proof of the training.

## Files to reuse
`shared/lesion_exp.py` (experiment runner and registry), `shared/lesion_box_tool.py` (box tool with `--rule`), `hyperpigmentation/scripts/` (`hyp_v2_transform.py`, `make_hyp_v2_relabel.py`, `make_hyp_reliability_v2.py`, `hyp_reliability_v2.py`, `hyp_v2_train_consistency.py`, `make_more_boxing.py`), `hyperpigmentation/notebooks/` (final training and results notebook), `docs/LUMYVUE_COMPLETE_TECHNICAL_RECORD.pdf`.
Participant photos stay on your own machines and are never committed or uploaded.
