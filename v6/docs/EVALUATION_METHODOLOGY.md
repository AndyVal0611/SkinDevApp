# Evaluation methodology (suitable for Chapters III and IV)

## 1. Reference standard
Independent, dermatologist-verified labels in three states per condition (*Present / Absent / Unknown*) for acne, eczema and hyperpigmentation; *Normal* = all three Absent. Labels are recorded blind to AI output where possible (`blinded_to_ai`). Weak labels (the single V5 class name) are never an evaluation reference and are used only as weak *positives* in training. Region labels (boxes on the original image, normalised) support ROI training and localisation metrics; `exhaustive = yes` marks complete box sets.

## 2. Data roles and leakage control
No subject/session identifiers exist, so groups are built from exact duplicates, mirror-aware perceptual hashes (Hamming ≤ 3) and shared source stems, merged by union-find. Splits are made **by group**, stratified by source class, and **frozen before annotation** (hash-checked): `train` (CNN + ROI training), `validation` (early stopping, model selection), `fusion` (fusion training, calibration, thresholds, Stage-1/2 rules, OOD threshold — all via grouped K-fold cross-fitting), `test` (single evaluation). ROI crops are derived after the split; fusion features for `fusion`/`test` come from CNNs that never saw those images. The limitation that proxies cannot prove patient-level independence is reported.

## 3. Systems compared (same test images, same labels)
A V5 · B improved global CNN · C YOLO-only (three detectors; Eczema *not evaluable* when no detector is registered) · D YOLO-guided ROI MobileNetV2 · E global + YOLO evidence fusion · F global + ROI + three-detector fusion · G full two-stage adaptive verification with abstention. Global-model experiments A–F are compared on validation with a per-condition regression check.

## 4. Metrics
Per condition: precision, recall/sensitivity, specificity, F1, ROC-AUC, PR-AUC (only with ≥ `MIN_POS_FOR_METRIC` positives **and** negatives). Set-level: micro/macro-F1, Hamming loss, exact match (fully-known images only). Calibration: reliability, ECE, Brier. Selective prediction: coverage, accepted-case error, risk–coverage, AURC, abstention/recapture/out-of-scope rates. Localisation: precision, recall, F1 at the deployed threshold, AP50, AP50–95 (computed down to confidence 0.05, so not numerically identical to Ultralytics' 0.001-based mAP), localisation error, FP/FN counts, duplicate/nested analysis. Verification: escalation rate, passes, stop reasons, resolved/unresolved CNN–YOLO disagreements judged against verified labels, false-confidence rate, redundancy, latency. 95 % intervals by **cluster bootstrap over leakage groups**; paired differences by the same resampling.

## 5. Selection discipline
Thresholds, Platt calibration, fusion hyper-parameters, Stage-1/2 bands (`TARGET_ACCEPTED_ERROR`, `MIN_ACCEPT_COVERAGE`) and the OOD threshold are fitted on out-of-fold `fusion` predictions. The test split is opened once (lock file); nothing computed on it feeds back. Detection thresholds are the deployed values and are **not** tuned.

## 6. Ablations and robustness
Remove each detector's evidence (and its ROIs), the ROI branch, the global branch, trainable fusion (fixed OR rule baseline) and adaptive verification; each fusion ablation is **re-trained and re-calibrated** with the same procedure. Robustness: brightness, blur, JPEG, rotation, crop, white balance, down-scaling → score change and decision flips of the global CNN (sensitivity, not accuracy); optional system-level latency.

## 7. V5 comparison — what is valid
V5's published single-label test metrics are **not** comparable to V6's multilabel metrics. V5 vs V6 is compared on the **V6 verified test split** (same images/labels); a **V5-unseen** subset (leakage groups absent from V5's training split) removes V5's advantage from sibling leakage. V5 cannot output more than one condition: its exact-match on mixed images is structurally 0 and is reported as such.

## 8. Reporting rules
Report intervals with every headline number; separate *verified misclassification* from *source-label disagreement* and from *CNN–YOLO disagreement*; state coverage with every selective result; never present agreement between models as correctness; list provisional parameters; distinguish `RESULT` from `NOT RUN` (the report generator does this automatically).

## 9. Threats to validity
Proxy grouping, reviewer variability (measure κ on a double-read subset), small verified sets, domain shift between dataset crops and kiosk captures, unvalidated quality/OOD heuristics, a single test split, the absence of an Eczema detector (if unregistered), and low-resolution ROI crops when originals are unavailable.
