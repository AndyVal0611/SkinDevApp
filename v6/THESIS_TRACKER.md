# LUMYVUE V6 - thesis tracker (started 2026-11-02, Monday)

Defense: **Monday 2026-11-16**. Pipeline target: **Monday 2026-11-09** (assumed; confirm). Hardware: "next Friday" (**2026-11-06 or 2026-11-13: confirm**).
Rule: every row below gets a status next time we talk (done / not done / blocked + why). "Working on it" is not a status.

## Decisions to approve now (PROPOSED, not yet agreed)
1. **Success criterion, fixed before looking at final results.** On validation images V5 did not train on, V6 (Global + YOLO fusion) counts as an improvement only if (a) Hyperpigmentation missed as no-target AND Normal falsely flagged are each not worse than V5, and at least one is better with a 95% interval that excludes zero, and (b) Acne and Eczema F1 are not more than 0.05 below V5. Anything else is reported honestly as "no clear improvement".
2. **Scope cut line.** Minimum thesis result = V5 vs Global multilabel (E) vs Global + YOLO fusion (Stage 1). ROI + Stage 2 are an extension: if the ROI model is not training by **Tue 11-03 evening**, Stage 2 is dropped and listed as future work.
3. **Evaluation honesty.** The locked test split is not independent of V5 (about 85% of it was in V5's training) and has no expert labels. It is NOT opened for the defense unless an independent labelled set exists. The reported comparison is development-stage, on V5-unseen validation images, with the limits stated.
4. **App integration.** The hardware demo runs the existing V5 pipeline. V6 is presented as an offline evaluation unless C# integration is finished and parity-tested (not planned this week).

## Plan
| Date | Deliverable | Status |
|---|---|---|
| Mon 11-02 | Box import (acne, hyperpigmentation `R01`; eczema RULE 3 batches `R02`), background-box review (about 80), `open_run02` started | |
| Tue 11-03 | ROI model trained (or Stage 2 cut); YOLO evidence reused/rebuilt for run02 | |
| Wed 11-04 | Fusion trained, calibration, Stage 1 rules | |
| Thu 11-05 | V5 vs V6 comparison on V5-unseen images with intervals; error analysis (eczema, hyperpigmentation vs Normal) | |
| Fri 11-06 | Models frozen; figures and tables exported | |
| Sat-Sun 11-07/08 | Chapters III-IV drafted from saved results | |
| Mon 11-09 | Pipeline DONE. Nothing new is trained after this date | |
| 11-10 to 11-15 | Hardware, slides, defense practice | |

## Open risks (do not hide these in the thesis)
- Evaluation not independent of V5; no expert reference labels (researcher + AI-assisted labels; fusion and half of train are AI-proposed, reviewed by researcher, recorded as `blinded_to_ai = no`).
- No inter-rater check of the labels. TODO: re-read 100 validation images blind and report Cohen's kappa per condition.
- Image sources unverified (DermNet / stock-photo watermarks seen). Deferred by the researcher; MUST appear as a limitation / ethics statement. Not claimed as verified.
- Single seed; eczema is the weakest condition (54 validation positives).
- Training images (web/stock) differ from the prototype camera.
- Eczema detector is weak by its own record (AP50 0.12 to 0.55 across sets).

## Log of decisions
- 2026-11-02: researcher asked for strict guidance; asked to defer image-source verification (recorded as an open risk, not waived).
- 2026-11-02: imported 8,410 reviewed boxes (acne and hyperpigmentation by R01, eczema RULE 3 batches by R02, all AI-assisted then reviewed) into region_annotations.csv; backup region_annotations.csv.bak_20261102_003008.
- 2026-11-02: reviewed detector false alarms: 227 background_false_positive boxes saved (train 134, validation 93). 22 boxes on 18 photos were flagged as real lesions on photos labelled "no condition"; all 18 re-judged, 7 labels changed (acne 3, hyperpigmentation 2, eczema 2), 11 kept as no condition. These 18 rows are now blinded_to_ai=no. LABEL-QUALITY FINDING: report it in the thesis (13 of the 22 flags came from the hyperpigmentation detector).
- 2026-11-02: next = open_run02 (new RUN_ID required because region and annotation files changed).
