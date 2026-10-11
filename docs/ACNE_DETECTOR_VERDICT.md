# Acne lesion detector: strict review and verdict

Reviewer stance: adversarial. Nothing here is called reliable or thesis-ready unless the evidence supports it. Written 10 Oct 2026, 19:55.
Where I say "verified" I ran the check myself in this session (listed in section 11). Where I say "unverified" I did not or could not.

---------------------------------------------------------------------------------------------------------------------------------------------

## 0. VERDICT

**KEEP BUT IMPROVE** - confidence about 70 %.

"Keep" because the model clearly does something real (see section 3). "Improve" because, as of today, the evidence does NOT support calling it reliable for lesion-level claims, the test set is no longer a clean final evaluation, the labels are incomplete, and the detector fires on non-acne photos. The improvement I recommend is mostly **evaluation and gating work, not more training** (section 9).

What it is NOT: a precise lesion locator or counter. What it can defensibly be: a screening aid that flags acne-affected photos and facial zones and marks candidate lesions.

---------------------------------------------------------------------------------------------------------------------------------------------

## 1. What the task should be (decided before judging the metrics)

| Possible objective | Supported by the model? | Needed by the thesis objectives? |
|---|---|---|
| Individual lesion detection and counting | Weak: precision 0.43-0.48, recall 0.48-0.54, tiny-spot recall about 20 %, count error 3.9 boxes on about 8 | No: the objectives name classification of acne/eczema/hyperpigmentation; lesion counting or severity grading is not an objective |
| Acne cluster / affected-region localisation | Yes: 3x3 zone F1 0.81 (vs the researcher's boxes) | Fits "detecting affected skin regions" |
| Photo-level "is acne present" | Yes: AUC 0.971, 95 % sensitivity (negatives unverified) | Supports the classifier, does not replace it |

**Declared objective (fixed now, not tuned to the numbers):** *candidate acne-lesion marking and affected-region localisation as a research screening aid.* Lesion-level metrics are still reported in full and are described as exploratory. This is a statement of purpose the app already implements (it shows boxes as "candidate lesions"), not a change made to flatter the scores.

YOLO bounding boxes are appropriate for this objective. Segmentation is NOT justified: there are no mask labels, tiny lesions (median box about 6 % of the photo side) gain little from masks, the annotation workload would be several times larger, and the region objective is already met at zone level.

---------------------------------------------------------------------------------------------------------------------------------------------

## 2. Evidence table

| Claim | Value | How it was checked | Status |
|---|---|---|---|
| Test mAP@0.5, 3 seeds | 0.412 / 0.408 / 0.419 (mean 0.413) with our scorer; **0.423 / 0.421 / 0.419 (mean 0.421) with Ultralytics' own validator** | I re-scored all three seeds with a second implementation (V2) | **Verified**: our scorer is, if anything, slightly conservative (about 0.008 lower) |
| Test mAP@0.5-0.95 | 0.121 / 0.120 / 0.126 (ours); 0.124 / 0.123 / 0.124 (Ultralytics) | same | Verified |
| Precision / recall / F1 at best-validation conf | 0.43 / 0.54 / 0.48 | our scorer | Verified by recomputation, single scorer |
| Gain over the old detector | +0.193 mAP@0.5, 95 % range +0.147 to +0.238 (bootstrap over 167 photos) | earlier run | Reproducible; same-photo comparison |
| Photo-level AUC / sensitivity / specificity | 0.971 / 95 % / 85 % at 0.35 | earlier run; **healthy photos not individually verified** | Unverified negatives |
| Zone F1 | 0.81 | earlier run | Verified against the researcher's boxes only |
| Count correlation | Spearman 0.70, MAE 3.9 boxes | earlier run | Verified; MAE is about half the true count |
| **False boxes on other conditions (V3, new)** | acne test photos 95 % flagged (8.05 boxes/photo); healthy 15 % flagged (0.22/photo); **eczema photos 54 % flagged (1.95 boxes/photo, n = 341)**; hyperpigmentation photos 29 % (0.77/photo, n = 164); hand-boxed hyperpigmentation test 20 % (0.29/photo, n = 100) | I ran the three seeds at 0.35 on those sets | **Verified as counts; the photo labels are NOT verified** (some eczema or hyperpigmentation photos may truly show acne) |
| Latency / memory (V7) | 291 ms median per 800 px frame, 2 CPU threads, this laptop; peak RSS 1.9 GB (includes Python libraries, an upper bound) | onnxruntime run of the installed file | **This machine, not the mini PC** |

---------------------------------------------------------------------------------------------------------------------------------------------

## 3. What the model demonstrably does

- It separates acne photos from healthy photos far better than chance and far better than the old detector, and it lights the right facial zones. Both are measured against the researcher's own labels and are stable across three seeds (std 0.006).
- It is not good at exact lesion-level localisation: a random visual audit of nine test photos (V4, AI-assisted visual inspection only, NOT dermatologist adjudication) showed that many "false" boxes are (a) plausible real lesions the labels do not contain, (b) near-misses of a labelled lesion with a different box size or offset (IoU below 0.5), and (c) a minority on freckles or texture. I cannot quantify the split from nine photos; it is an impression, not a measurement.

**Image-level capability is stronger than lesion-level capability. Yes. State it that way.** It is sufficient for the declared objective, not for lesion counting.

---------------------------------------------------------------------------------------------------------------------------------------------

## 4. Ground-truth reliability

- **Incomplete labels (verified in the 60 re-reviewed photos):** boxes rose from 893 to 1,430 (+60 %), only 2 of the original boxes were removed. Those 60 photos were chosen because the model was most confident there, so +60 % is an upper bound for the whole set, but it proves incompleteness exists. The test labels were made by the same one annotator under the same rule and are probably incomplete too. Consequences: measured precision is biased DOWN; recall is measured only against the boxes that exist; strict-IoU scores are depressed by near-miss boxes.
- **One annotator, no adjudication, no inter-rater data.** There is no measurement of human-human agreement, so nobody can say what a perfect detector would score. This is the biggest unknown behind every metric.
- **Annotation style differs by source:** drafted photos have far fewer tiny boxes (0.3-0.4 %) than from-scratch photos (2.5 %); 28-46 % of photos are under 400 px.
- **Rule vs claim:** the rule ("one tight box per spot; no scars, freckles, moles or dark marks") matches the claim "candidate acne lesions". The model's boxes on freckles are therefore genuine errors under the rule.
- **Near-duplicates:** none inside training (0 pairs at correlation 0.95); 5 validation and 2 test photos still have a look-alike (corr 0.90) in training. **Participant-level leakage cannot be assessed**: the photos are public images without participant IDs, and several come from series of the same patient. Unverified.

---------------------------------------------------------------------------------------------------------------------------------------------

## 5. Is the 167-photo test set still an unbiased final evaluation? No.

It guided development. The decisions it informed:
1. Comparing v2 / v3 / v3a on the test photos led to dropping the 400 drafted non-facial photos (the composition of dataset v4).
2. The test/validation mismatch it revealed led to the scratch-only validation design.
3. **The app default threshold 0.35 was chosen from a precision-recall sweep computed on the test photos.** It should have come from validation (validation-best F1 confidences were 0.225-0.25).
4. About 90 test photos marked X were re-inspected and 6 were re-boxed.
Not test-guided: the seed (chosen on validation, 123) and all experiments A0-A6 (validation only).

Consequence: the test numbers are best treated as **development-test** numbers, likely optimistic by an unknown, probably small amount (the decisions were coarse, not fine-tuned). The size of the bias is unmeasured.

**How to get a genuinely independent evaluation:** collect at least 100 new facial photos that have never been viewed or scored (the unreviewed non-facial pool, new kiosk captures, new public photos), have them boxed blind by the researcher and, for at least 30 of them, also by a second reviewer, fix the threshold (0.35 or the validation value) and the protocol in writing beforehand, and score once. Dermatologist adjudication of disagreements is the strongest option and is unavailable until someone arranges it.

---------------------------------------------------------------------------------------------------------------------------------------------

## 6. Acceptance criteria (for the declared objective)

I do not use universal mAP thresholds. These are set from the purpose, the measurement uncertainty, and the verified literature (Huynh et al. 2022: mAP 0.54 over 4 types, 0.40-0.44 for small lesions; general-acne baseline 0.205 as cited there; their IoU is not stated).

| Criterion | Pass condition | Why | Today |
|---|---|---|---|
| Photo-level | AUC >= 0.90 and sensitivity >= 90 % at the app threshold on an independent set with **verified** healthy photos | the screening purpose | 0.97 / 95 % but negatives unverified: **provisional pass** |
| Other conditions | no more than 20 % of independently verified non-acne photos (healthy, eczema, hyperpigmentation) receive an acne box | acne boxes on eczema or healthy skin mislead the reader | healthy 15 %, eczema 54 %, hyperpigmentation 29 % (labels unverified): **fail until verified or gated** |
| Lesion-level | precision and recall each >= 0.50 at IoU 0.5 against labels made by two reviewers | candidate marking should be right about half the time at least, and comparable literature sits around 0.4-0.55 | 0.48 / 0.48 vs one annotator: **borderline, not provable** |
| mAP@0.5 | >= 0.40 on the independent set | context only | 0.41-0.42 on the dev-test: **pass, not independent** |
| mAP@0.5-0.95 | no threshold, reported only | strict-IoU is dominated by 30 px boxes and edge noise | 0.12 |
| Small lesions | report recall per size; no claim about tiny spots | | 20 % under 2 % of the side: **disclosed** |
| Zone level | F1 >= 0.75 | region objective | 0.81 on dev-test: **pass, not independent** |
| Counting | none claimed | MAE 3.9 on about 8 | not claimed |
| Front / left / right consistency | not scored (no ground truth for the 42 captures); future: counts per view within a stated tolerance | | **not measured** |
| Skin-tone groups | recall difference between independently rated MST bands <= 0.10 | fairness | **not measured** (no tone labels, MST model not trained) |
| Agreement with independent ground truth | model F1 against reviewer A at least 80 % of reviewer B against reviewer A | a model cannot be asked to beat human agreement | **unknown** (no second reviewer) |
| Latency | <= 2 s per three-view scan on the mini PC | kiosk usability | 0.29 s per view on this laptop; mini PC **unmeasured**; the app currently runs the old detector AND both specialists per view |

---------------------------------------------------------------------------------------------------------------------------------------------

## 7. The four options

| Option | Scientific validity | Expected reliability | Feasibility | Thesis suitability | Rank |
|---|---|---|---|---|---|
| **B. Keep but improve** (evaluation hardening, label completion on a budget, a display rule for non-acne photos) | high once an independent set exists | highest honest reliability gain per hour | high (about 5 working days) | best | **1** |
| A. Keep as is, with disclosures | moderate: dev-test contaminated, labels unadjudicated, FP on other conditions unaddressed | as measured | highest | acceptable if every limit is disclosed | 2 |
| C. Redesign (region / cluster objective or segmentation) | region objective is already supported by the existing model and mostly means re-defining and re-evaluating, which Option B includes; segmentation unjustified | no measured gain expected | segmentation low (new masks, 3-5x workload) | segmentation poor | 3 |
| D. Replace or remove | not justified: the model beats the old detector by +0.19 with a clear range, and photo/zone capability is real | removing loses the "where" the app shows | high | removing a working module weakens the thesis | 4 |

---------------------------------------------------------------------------------------------------------------------------------------------

## 8. Challenge to "three specialist detectors"

The evidence does not show that specialists beat one multiclass detector. The acne specialist beats the old 3-class detector (0.41 vs 0.22) but they differ in data, labels, size and resolution, so "separate" and "better data" are confounded. A fair test (a 3-class model trained on the same improved data) has not been run. **Do not claim specialists are superior.** Defensible reason to keep them: the acne and hyperpigmentation models exist, were measured against the researcher's own labels, and beat the old detector on those labels; the old detector stays as the fallback for eczema.
Cost to disclose: the app now runs the old detector plus two specialists per view.

---------------------------------------------------------------------------------------------------------------------------------------------

## 9. The smallest set of actions, budget and stop conditions

Budget: **5 working days, decision on day 5.**
1. **Fix the objective and criteria in writing (this document).** Zero cost.
2. **Resolve the non-acne false boxes.** Verify by eye 50 healthy, 50 eczema, 50 hyperpigmentation photos (2-3 hours), then test one display rule chosen on validation only (for example show acne boxes only when at least 2 boxes pass the threshold or the top confidence is above a higher value). Pass: criterion in section 6 met without losing more than 0.05 photo-level sensitivity. Stop if the rule costs more than that.
3. **Build the independent set and a second review** (blind, at least 100 photos, 30 double-boxed). This gives the human-agreement ceiling, the true precision, and an unbiased final score. About 2 working days of boxing.
4. **Only if the double boxing shows the labels are incomplete,** finish the second-look relabelling of the highest-priority training photos and retrain once with 3 seeds, scored once on the independent set. **Stop condition: if the independent-set gain is under 0.02 in mAP@0.5 and under 0.02 in F1, stop all acne training for good.**
Do NOT: add more epochs, a larger model, or thousands of photos. The 960 px result (+0.017 on validation, one seed) does not meet our own +0.02 rule and costs about 44 % more compute; drop it unless 5 seeds show at least +0.02. A6 (yolov8m) is still running; A7 (relabelled photos) is queued; read them, do not chase them.

**Stop improving this detector when** the section-6 criteria are met on the independent set, or after day 5, whichever comes first, and move to the rest of the thesis.

---------------------------------------------------------------------------------------------------------------------------------------------

## 10. Panel critique (what they will ask and what you may say)

Likely questions and honest answers:
- *"Your boxes are only half right. Why use it?"* It is a screening aid; photo-level AUC 0.97 and zone F1 0.81 are the claims, the lesion-level score is exploratory.
- *"How do you know the labels are right?"* One annotator; labels were shown incomplete in a re-review (+60 % in 60 photos); no adjudication yet. Say so.
- *"Is the test set independent?"* It guided decisions; it is a development test; an independent set is planned or done (say which).
- *"What happens on eczema or healthy skin?"* Acne boxes appear on 54 % of eczema photos and 15 % of healthy photos at the app threshold (labels unverified); a display rule is planned or tested.
- *"Why YOLO and not segmentation?"* The objective is candidate marking and region localisation; no masks exist; tiny lesions.
- *"Why three detectors?"* Not proven better than one; kept because they exist and beat the old one on your labels.
- *"Real scans?"* Not evaluated yet. Do not claim it.
- *"Skin tone?"* Not evaluated.

Claims you CAN defend: acne mAP@0.5 about 0.41 (0.42 with Ultralytics' scorer) on a 167-photo development test set, +0.19 over the old detector; photo-level AUC 0.97 with unverified negatives; zone F1 0.81; Grad-CAM is attribution, not localisation; negative experiments (five training changes gave no gain beyond noise).
Claims you MUST NOT make: that the detector is clinically reliable; that it counts lesions; that it detects every pimple; that specialists beat a multiclass detector; that the test result is an unbiased final evaluation; that it works across skin tones or on kiosk scans; that the 85 % specificity is verified.

---------------------------------------------------------------------------------------------------------------------------------------------

## 11. Provenance (what I actually ran or read)

Executed in this session: `acne_verdict_checks.py` (V2 independent scorer, V3 false boxes on non-acne photo sets, V7 latency, V9 note); a random nine-photo visual audit of test predictions (seed 123, threshold 0.35); `lesion_alt_metrics.py` (photo / count / zone levels); `lesion_diagnose_data.py`, `lesion_diagnose_models.py`; experiments A0-A5 (validation only) via `lesion_exp.py`; the second-look statistics. Read: the Huynh et al. PDF; `EXPERIMENTS.csv`.
NOT done: any second-annotator or dermatologist review; evaluation on kiosk scans; verification of the healthy / eczema / hyperpigmentation photo labels; skin-tone analysis; mini-PC latency; A6 and A7 (A6 was at epoch 42 of 100 at 19:52, A7 queued); a true 3-class versus specialists comparison.
Nothing was overwritten or deleted; weights, datasets, annotations and experiment history are unchanged. The checks wrote only `lesion_experiments/diagnosis/verdict_checks.json` and a Ultralytics label cache in the WSL copy of the dataset.

---------------------------------------------------------------------------------------------------------------------------------------------

## 12. Tonight's reduced plan and the pre-registered independent evaluation (frozen BEFORE the independent photos exist)

Constraint: the researcher has one evening. The five-day plan in section 9 is cut to what can be finished tonight.

**Findings added tonight (validation only unless stated)**
- Display-rule test (`acne_gate_rule.py`, acne VALIDATION photos as positives, class-folder photos as unverified negatives, seed 123): requiring at least N boxes does cut false displays but costs sensitivity: at threshold 0.35, N = 3 shows boxes on 70 % of acne validation photos (vs 94 % with N = 1) while eczema photos drop from 52 % to 24 % and healthy from 16 % to 1 %. It fails the section-9 stop condition (lose no more than 0.05 sensitivity), so **no count gate is adopted**.
- Visual check of nine eczema-class photos with 3+ acne boxes (AI-assisted, not adjudication): the set contains non-facial skin (trunk, neck, limbs), papular eruptions that look acneiform, and crusted eczema patches. The detector behaves as a generic spot/papule detector, not an acne-specific one. **Disclose it that way; do not claim acne specificity.**
- Threshold: on validation, best F1 is at confidence 0.25 and precision equals recall (0.494 / 0.497) at 0.26. The earlier default 0.35 was chosen from a test-set sweep and is therefore test-informed. **The validation-based default is 0.26.** 0.35 is kept only as a documented precision-leaning option.

**Frozen protocol for the independent evaluation (no changes after this line)**
- Model: the installed acne detector (seed 123 of the v4 run), unchanged.
- Thresholds: 0.26 (validation-chosen, the primary result) and 0.35 (secondary); no other threshold is tried.
- Photos: 43 photos (the whole eligible pool; 60 were planned) drawn from the never-reviewed pool (small real-face photos, 250-399 px, no look-alike of any training/validation/test photo at correlation 0.90), boxed by the researcher WITHOUT drafts, with X for photos showing no acne. The model is not run on these photos until boxing is finished.
- Metrics: precision, recall, F1 at IoU 0.5, mAP@0.5 and mAP@0.5-0.95 with a bootstrap range over photos, and photo-level behaviour using the researcher's X photos as verified negatives (small n, reported with its uncertainty).
- It is scored once. Whatever the result is, it is reported.
- Limits that stay: one annotator, small n, one source style (small low-resolution photos), no second reviewer, no dermatologist, no kiosk scans.

---------------------------------------------------------------------------------------------------------------------------------------------

## 13. RESULT of the independent evaluation (scored once, frozen protocol, 10 Oct 2026 evening)

Final run: notebook `Pipeline V5 - Lesion Detector` (acne, dataset v4, YOLOv8s 800 px, seeds 42/7/123, `USE_FIXED_LABELS = False`, `USE_NEGATIVES = False`). Training reproduced the earlier run exactly (same dev-test numbers). Best validation seed: **123** (validation mAP@0.5 0.440).

| Evaluation | mAP@0.5 | mAP@0.5-0.95 | Precision | Recall | F1 |
|---|---|---|---|---|---|
| Development test (167 photos), 3-seed mean | 0.413 (95 % range 0.369-0.463) | 0.123 | 0.427 | 0.543 | 0.478 |
| **Independent set (41 photos, 437 boxes), seed 123, threshold 0.26** | **0.358 (95 % range 0.306-0.435)** | 0.112 | 0.384 | 0.524 | **0.443 (range 0.383-0.502)** |
| Independent set, threshold 0.35 | same model | | 0.410 | 0.467 | 0.436 (range 0.374-0.499) |

- Photo level on the independent set: all 41 boxed photos got at least one box (sensitivity 100 %); the 2 photos marked X (no acne) both received a box at both thresholds (n = 2: specificity cannot be estimated from this set).
- The independent mAP@0.5 is 0.055 lower than the development test. Two explanations that cannot be separated with these data: (a) the development test is mildly optimistic because it guided decisions (section 5); (b) the independent photos are a different source style (small, 250-399 px). The independent set is small (41 photos); its own range still contains the development value.
- Development-test usage-level metrics at 0.26 / 0.35 (3 seeds, healthy photos unverified): photo AUC 0.962; sensitivity 0.97 / 0.95; specificity 0.69 / 0.83; count Spearman 0.73 / 0.70; zone F1 0.82 / 0.81.
- Verdict unchanged: **KEEP BUT IMPROVE**. The independent set supports "moderate lesion-level performance, F1 about 0.44, stable photo-level behaviour" and weakens any claim that the 0.41 mAP is the unbiased figure; report both and call the independent one the more conservative estimate.
