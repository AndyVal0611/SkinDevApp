# Lesion detector results: ready-to-use summary (acne and hyperpigmentation)

All numbers: IoU 0.5 matching, confidence chosen on validation photos, test photos never used for selection. "Hand-boxed" = boxed by the researcher from scratch with one rule.
Eczema is trained separately by a teammate and is not covered here.

## Final results
| Detector | Test set | mAP@0.5 | mAP@0.5-0.95 | Precision | Recall | F1 |
|---|---|---|---|---|---|---|
| **Acne** (YOLOv8s, 800 px, clean dataset v4: 1,145 train / 154 val photos), mean of 3 seeds | 167 hand-boxed photos (a DEVELOPMENT test set: it guided some decisions, see ACNE_DETECTOR_VERDICT.md) | **0.413** (std 0.006; 95 % range 0.37-0.46) | 0.122 | 0.43 | 0.54 | 0.48 |
| Earlier 3-class detector (acne class) | same | 0.220 | 0.066 | 0.36 | 0.23 | 0.28 |
| **Hyperpigmentation, final** (YOLOv8s, 800 px, one class; own boxes + 518 reviewed drafts, lr0 0.0005), mean of 3 seeds | 100 hand-boxed photos | **0.229** (std 0.020; seeds 0.207 / 0.246 / 0.233) | 0.065 | 0.31 | 0.30 | 0.30 |
| **Hyperpigmentation, 3-seed box average (installed in the app)** | same | **0.278** | n/a | 0.37 | 0.34 | **0.354** (95 % range 0.30-0.41) |
| Hyperpigmentation, detector installed before this work (s_800) | same | 0.178 | 0.051 | 0.29 | 0.21 | 0.24 |
| Earlier 3-class detector (hyperpigmentation class) | same | 0.053 | 0.019 | 0.19 | 0.05 | 0.08 |

Gain over the earlier detector for acne: +0.19 mAP@0.5 (95 % range +0.15 to +0.24), not explained by noise. Published acne detectors report about 0.5 mAP on their own datasets (different data and settings; context only, not a ranking).

## Reliability at the level the app is used (acne, 3 seeds, app threshold 0.35, `lesion_alt_metrics.py`)
| Question | Result |
|---|---|
| Photo level: acne photo vs healthy photo (167 test photos vs 200 healthy photos from the Normal class) | AUC 0.971 +/- 0.004; sensitivity 95.0 % (acne photos flagged); specificity 85.0 % +/- 3.0 (healthy photos left alone) |
| Count level: does the number of boxes follow the number the researcher drew? | Spearman 0.70 +/- 0.02; mean absolute error 3.9 boxes per photo (about 8 true); bias -0.15 |
| Zone level (3x3 grid): does it light the same facial zones that were boxed? | precision 0.83, recall 0.79, F1 0.81 |
The healthy photos were not individually verified (a few may show faint acne, which would lower specificity, not raise it). These measures do not replace mAP; box-level mAP stays about 0.41 because acne spots are tiny and hand-boxed edges differ by a few pixels.

## Published context for acne detection (checked against the PDF of Huynh et al. 2022, Diagnostics 12(8):1879, doi 10.3390/diagnostics12081879)
| Study | What was detected | Data | Model | mAP as reported |
|---|---|---|---|---|
| Huynh et al. 2022 (AcneDet) | 4 types: blackheads/whiteheads, papules/pustules, nodules/cysts, acne scars | 1,572 smartphone images (about 720x1280), 41,859 lesions, labelled by 4 dermatologists (junior label + senior review), 70:30 split | Faster R-CNN ResNet50 | 0.54 overall; per type 0.40 / 0.64 / 0.68 / 0.44 |
| Min et al. (ACNet), as cited in that paper | **general acne, no types** (same task as our single "acne" class) | 18,983 acne objects (ACNE04) | ACNet | 0.205 |
| Kuladech et al., as cited in that paper | 4 types | 15,917 | Faster R-CNN / R-FCN | 0.233 / 0.283 |
| **This study** | general acne, one class | 1,145 train photos, 11,621 boxes, one annotator | YOLOv8s, 800 px | 0.41 (mAP@0.5; hand-boxed test) |
Reading it: the paper's lowest APs are the small, low-contrast types (blackheads/whiteheads 0.40, scars 0.44) and the highest the large, red ones (nodules 0.68, only 0.67 % of lesions). A single "acne" class mixes all types and is dominated by the small ones. Weighting the paper's non-scar types by their lesion counts gives about 0.44 (my own back-of-envelope calculation, not a figure from the paper), close to our 0.41-0.45. Caveats: the paper does not state the IoU threshold of its mAP, does not say whether the 70:30 split keeps one person's three selfies together, and the datasets, annotators and image sizes differ from ours, so this is context, not a ranking.

## Wording you can use
"Separate condition-specific YOLOv8 detectors were trained for acne and hyperpigmentation on researcher-boxed photos and evaluated on independently hand-boxed test photos that were never used for training or model selection. The acne detector reached mAP@0.5 = 0.41 (mean of three seeds, F1 = 0.48), compared with 0.22 for the earlier combined three-class detector. The hyperpigmentation detector reached mAP@0.5 = 0.23 (mean of three seeds, F1 about 0.30; 0.18 for the detector it replaced) and is reported with its limits: box-level scores are capped by low agreement of the annotator with themselves on diffuse patches (F1 0.28 at IoU 0.5 on a 30-photo blind re-box), so zone-level results (3x3 face-zone F1 0.68) are the more reliable claim. The detectors propose candidate lesion boxes; they are not a diagnosis, and Grad-CAM++ is reported separately as model attribution, not localisation."

## What limits the results (from the diagnosis)
- Acne: spots are tiny (median box about 6 % of the photo side), so strict-IoU metrics are low (mAP@0.5-0.95 about 0.12) and recall for spots under 2 % of the side is about 20 %. Training labels were incomplete: in 60 photos re-reviewed with model help, boxes rose from 893 to 1,430. Test labels have the same incompleteness, so precision is probably underestimated.
- Acne experiments that did NOT help beyond noise (validation, seed 42; baseline mAP@0.5 0.425): healthy-skin negatives 0.431, milder colour jitter 0.424, tighter box loss 0.431, lower learning rate 0.433. More photos (708 to 1,145) did not help either.
- Hyperpigmentation: public training boxes are about twice as large as the researcher's (18 % vs 9 % of the photo covered) and dominate training; the earlier validation set was in the public style; 2,524 of 3,031 training photos have a near-duplicate; the earlier n/m model comparison was invalid (7 and 9 epochs). Corrected experiments are queued (hand-boxed validation, de-duplication, balanced mixes, fully trained n/m).

## Deployment
- App uses an acne specialist (threshold 0.26, chosen on validation photos where precision = recall; 0.35 gave precision 0.48 / recall 0.48 on the development test but was test-informed) and a hyperpigmentation specialist (average of three seeds by weighted box fusion, threshold 0.225, chosen on validation; the app's own detector code reproduces the offline result on the test photos: F1 0.357, zone F1 0.683). The earlier three-class detector was removed from the app; eczema gets no boxes until the teammate's model is installed. Boxes are filtered to the detected face region. CPU latency on the mini PC was not measured yet (run `benchmark_pc.py` there).
- Reproducibility: `lesion_experiments/EXPERIMENTS.csv`, `lesion_exp.py`, `Pipeline V5 - Lesion Detector.ipynb`, `LESION_DETECTOR_DIAGNOSIS_AND_PLAN.md`.

## Still to state honestly
Single hand-boxed test set per condition; one annotator; hyperpigmentation test photos are low resolution (median 224 px); no evaluation on real kiosk scans yet; skin-tone subgroup performance not evaluated.

## Added after the strict review (read ACNE_DETECTOR_VERDICT.md before quoting any number)
- The 167-photo test set guided development decisions (dataset composition, validation design, an earlier threshold), so call it a **development test set**; an independent 43-photo set is being boxed blind and will be scored once under a frozen protocol.
- An independent second implementation (Ultralytics' validator) gives mAP@0.5 of 0.421 (mean of 3 seeds) on the same photos, versus 0.413 with our scorer.
- **The acne detector is not acne-specific:** at the app threshold it puts boxes on 54 % of eczema-class photos (n = 341, many non-facial or papular), 29 % of hyperpigmentation-class photos and 15 % of healthy photos (labels not verified). Describe it as a candidate-lesion (spot / papule) marker, not an acne diagnosis tool.
- Latency: about 0.29 s per 800 px frame on the development laptop (2 CPU threads); not measured on the mini PC.

## Independent evaluation (acne; scored once with a frozen protocol; see ACNE_DETECTOR_VERDICT.md section 13)
41 blind-boxed photos (437 boxes), best-validation seed, threshold 0.26: **mAP@0.5 0.358 (95 % range 0.31-0.44), precision 0.38, recall 0.52, F1 0.44 (range 0.38-0.50)**; at threshold 0.35: precision 0.41, recall 0.47, F1 0.44. All boxed photos were flagged. This is lower than the development-test mAP@0.5 of 0.413 (0.055 lower): report the independent result as the more conservative figure, and note its limits (41 photos, one annotator, small low-resolution photos, only 2 verified no-acne photos).


## Hyperpigmentation: final evaluation and what limits it (added 2026-10-12)
- **Chosen setting:** your boxes plus 518 reviewed model drafts, YOLOv8s 800 px, lr0 0.0005 (AdamW). Seed 42 was chosen on validation (hand-boxed, 60 photos, mAP@0.5 0.324; 3-seed validation mean 0.300 +/- 0.022). The seed-42 model is the one installed in the app, threshold 0.20 (best validation F1).
- **Frozen test (100 hand-boxed photos, never used for training or selection):** seed 42 mAP@0.5 0.207, F1 0.29 (95 % range 0.24-0.34); 3-seed mean 0.229. The old installed model: 0.178, F1 0.24. 3x3 face-zone F1 0.68 (old 0.62). A patch is found in 97 % of photos (old 94 %). Patch-count agreement is weak (Spearman 0.40, mean error 2.3 patches). Specificity cannot be computed: all test photos contain a patch.
- **3-seed box averaging (weighted box fusion), installed in the app:** validation mAP@0.5 0.357 vs 0.300 for one model; on the test photos 0.278 and F1 0.354 (through the app's own C# code: F1 0.357). A five-model average (adding 960 px and YOLOv8m) reached 0.374 on validation, a gain within noise for five times the cost, so three models were used. Cost: about 0.85 s per model on the development PC with 2 threads (acne + three hyperpigmentation models: 3.4 s per photo); mini-PC latency not measured.
- **What did not help (validation, seed 42):** a second round of 230 reviewed drafts (0.296 vs 0.298), 960 px (0.308), YOLOv8m (0.305), starting from the acne weights (0.278) or from the public-photo model (0.312). The lower learning rate looked like +0.028 on one seed but the 3-seed mean (0.300) equals the earlier runs.
- **Where it fails (validation):** patches found at IoU 0.5: small 24 %, medium 37 %, large 51 %. Matching looser raises F1 from 0.37 (IoU 0.5) to 0.47 (IoU 0.3) and 0.51 (IoU 0.2): many missed patches are found but not tightly, which is edge ambiguity of diffuse patches.
- **Annotator reliability (30 validation photos boxed again blind, same annotator):** F1 0.28 at IoU 0.5 (0.38 at 0.3), with 3.0 vs 6.4 boxes per photo. The detector agrees with the original boxes better (F1 0.41 on the same photos, validation-tuned) than the annotator agrees with themselves. Box-level mAP above this level cannot be demonstrated with these labels.
- **Rule v2 (`HYPERPIGMENTATION_ANNOTATION_RULE_V2.md`):** one box per connected patch, minimum size, one facial region per box. Two passes on the same 30 photos agreed at F1 0.67 (IoU 0.5), but they were done minutes apart, so this is an upper bound; a third pass at least 12 hours later (against pass A) is prepared but not done. Rule v2 boxes are about twice as large (median side 0.22 vs 0.125), so adopting it means relabelling and retraining; this is future work.
- **Wording:** "The hyperpigmentation detector proposes candidate patches (mAP@0.5 0.23 on 100 hand-boxed photos); box-level agreement is limited by annotator consistency on diffuse patches, so results are also reported by face zone. It is not a diagnosis."


## Hyperpigmentation under annotation rule v2 (added 2026-10-12; this replaces the old-style numbers above as the main result)
- **Why:** a blind re-box of 30 photos showed the original rule was not reproducible (F1 0.28 at IoU 0.5). Rule v2 (`HYPERPIGMENTATION_ANNOTATION_RULE_V2.md`: one box per connected patch, minimum size, one facial region per box; boxes about twice as large) was agreed with itself at F1 0.67 on the same 30 photos (two passes minutes apart, so an upper bound).
- **Labels:** test = 100 photos boxed from scratch by the researcher under rule v2 (226 boxes, never used for training or selection); validation = 60 other photos boxed under rule v2. Training boxes = the old boxes converted to rule v2 by code (touching or closer than the smaller box's side = one box; boxes under 0.09 dropped), 1,115 training images; no new training annotation.
- **Result (frozen, test = 100 rule-v2 photos), threshold from validation:** the 3-model average (three seeds) mAP@0.5 **0.341**, precision 0.45, recall 0.37, F1 **0.409** (95 % range 0.35-0.47); at the more sensitive threshold 0.125 (test-informed, F1 equal): precision 0.36, recall 0.46, F1 0.405, zone F1 0.57, patch found in 97 % of photos. Single seeds: mAP@0.5 0.209 / 0.282 / 0.244 (mean 0.245).
- **Comparison on the same test photos:** the same recipe trained on the old-style boxes: 3-model average mAP@0.5 0.077 (F1 0.18); the model installed before this work (trained with public photos, whose boxes are also coarse): 0.207 (F1 0.30). On the old test labels the old-style models score 0.23-0.28, so these results are not comparable with the old test set: the target changed.
- **Not shown:** an independent set under rule v2; a rule-v2 re-check one day later (pass C is prepared, not done); training photos corrected by hand under rule v2 (808 prepared, none corrected). Rule-v2 boxes are whole patches, so counts of patches are not comparable with acne lesion counts.


## Acne: 3-seed average (added 2026-10-13; installed in the app)
- The three final seeds (123, 42, 7) are averaged by weighted box fusion (IoU 0.5, confidence = mean over the models). Threshold 0.275 = best F1 on the 154 validation photos, fixed before the test was scored.
- **Development test (167 photos), scored once for this change:** mAP@0.5 **0.439**, precision 0.465, recall 0.556, F1 **0.507** (95 % range 0.470-0.544), 3x3 zone F1 0.832, a box in 98 % of photos. The single installed seed (123) on the same photos: mAP@0.5 0.419, F1 0.482 (0.446-0.521); seeds 42 and 7: 0.412 and 0.408. Gain +0.02 mAP@0.5 and +0.025 F1; the F1 ranges overlap, so the gain is small. The development test has now been scored twice (the third scoring of the set overall, see ACNE_DETECTOR_VERDICT.md); the independent set was not touched and keeps the single-model result (mAP@0.5 0.358).
- The app's own detector code reproduces the offline score (F1 0.506, zone F1 0.829). Cost: about 0.85 s per model per photo on the development PC (three models: about 2.5 s); mini-PC latency not measured.


## Hyperpigmentation: final model H25 (added 2026-10-13; installed in the app; replaces the rule-v2 conversion model above)
- **Training data:** 808 photos corrected by hand under annotation rule v2 (each shown 3 times per epoch) + 1,260 de-duplicated public photos (3,684 training images); YOLOv8s, 800 px, AdamW lr 0.0005, up to 100 epochs, three seeds (42, 7, 123). Validation: 60 rule-v2 photos. Threshold 0.225 = best F1 on validation.
- **Validation (60 photos):** single seeds mAP@0.5 0.449 / 0.480 / 0.436 (mean 0.455, std 0.023); 3-model average mAP@0.5 **0.532**, mAP@0.5-0.95 0.194, F1 0.563.
- **Test (100 rule-v2 photos boxed from scratch, never used for training or choosing; scored once, stored by the notebook):** 3-model average mAP@0.5 **0.508**, mAP@0.5-0.95 **0.209**, precision 0.590, recall 0.465, F1 **0.520** (95 % range 0.44-0.59); 3x3 face-zone F1 0.608; a patch found in 93 % of photos; patch count: mean error 0.94 patches, within 1 in 82 % of photos, Spearman 0.54. F1 by IoU needed: 0.2: 0.67, 0.3: 0.65, 0.5: 0.52, 0.7: 0.28, 0.8: 0.13. Single seeds on the test photos: mAP@0.5 0.440 / 0.450 / 0.410.
- **Gain over the previous installed model (rule-v2 conversion, same test photos):** mAP@0.5 0.341 -> 0.508, F1 0.409 -> 0.520.
- **The app's own detector code reproduces it:** precision 0.608, recall 0.460, F1 0.524, zone F1 0.614; about 3.1 s per photo for all six models (three acne, three hyperpigmentation) on the development PC; mini-PC latency not measured.
- **What helped (validation, single seed):** hand-corrected rule-v2 labels (H23/H24 vs converted H20: +0.09 mAP@0.5), adding the public photos (H25 vs H24: +0.07), averaging three seeds (+0.05 on test). What did not: correcting more than the first 200 photos (H24 = H23), larger input, larger model, other starting weights.
- **Limits:** one annotator; rule v2 boxes are whole patches about twice the size of the first rule's (scores are not comparable with the old-rule results); the rule was re-checked only minutes apart (pass C pending); no independent set under rule v2 and no clear-skin photos scored yet, so specificity is not measured; boxes are candidate patches, not a diagnosis.


## Acne: fresh untouched test set, candidate vs installed model (added 2026-10-15)
- **Set:** 100 photos boxed from scratch by the researcher (no model drafts), never used for training, validation, tuning or threshold choice, and checked against every photo used elsewhere for near-duplicates: **36 photos with acne (203 boxes) and 64 photos with no acne** (marked X; 37 came from the acne pool, 27 from the clear-skin pool). Scored once for the two models below, each at its own validation threshold. (A first run scored only the 36 positive photos because of a bug in the notebook's folder selection; it was archived and the corrected run on all 100 photos is the result reported here. Nothing was tuned between the two runs.)
- **Models:** the installed 3-seed average (800 px, threshold 0.275) and the 960 px candidate (3 seeds, threshold 0.225; validation mAP@0.5 0.471 vs 0.460, development test 0.481 vs 0.439).
- **Result (all 100 photos):** installed mAP@0.5 **0.336**, mAP@0.5:0.95 0.088, precision 0.350, recall 0.562, F1 0.431 (95 % range 0.30-0.55), face-zone F1 0.695; candidate mAP@0.5 0.300, mAP@0.5:0.95 0.077, precision 0.301, recall 0.571, F1 0.395 (0.28-0.50), zone F1 0.667. Single seeds: installed 0.292 / 0.285 / 0.320, candidate 0.279 / 0.256 / 0.268.
- **Positive photos only (36 photos, first run):** installed mAP@0.5 0.418, F1 0.498; candidate 0.389, F1 0.471.
- **False alarms on the 64 no-acne photos:** installed 39 % get at least one box, candidate 50 %. By source: installed 57 % on the 37 acne-pool negatives (many are not clear facial skin) and 15 % on the 27 clear-skin photos; candidate 59 % and 37 %.
- **Pre-set decision rule (candidate must be +0.02 mAP@0.5, precision and recall not more than 0.02 lower, false alarms not more than 5 points higher):** failed (mAP -0.036, precision -0.048, false alarms +10.9 points). **The installed model stays.** The development-test gain of the candidate did not hold on fresh photos.
- **0.50 mAP@0.5 on the fresh test was not reached** (best 0.336 on all 100 photos, 0.418 on the 36 photos with acne). The fresh set is small (203 boxes), so the ranges are wide: differences of about 0.03 are within noise.
- **Limits:** one annotator; same-annotator box agreement F1 0.30 at IoU 0.5; few acne photos in the fresh set; many negatives are not clear facial skin; the development test had influenced earlier decisions, which is why this set was made.
