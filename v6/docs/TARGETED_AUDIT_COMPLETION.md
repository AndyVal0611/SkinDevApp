# Targeted V6 maintenance and audit — 2026-11-01

## Implemented

Both notebook versions were copied, byte-for-byte, into `v6/backups/20261101_095515/`. The manifest contains their original paths and SHA-256 hashes. The executed Downloads notebook remains unchanged. Only cell 82 differed in source; its float32 masked attention, finite sentinel, and divide-no-nan normalization were reconciled into `notebook_src/08_fusion.cellsrc`. The repository notebook was rebuilt with all 142 cells, including the review contract. It has no stored results.

Source permissions now fail closed at annotation/region gates, ROI table creation and cached ROI feature loading, fusion calibration, localization evaluation, and the final test lock. The shared policy is embedded in the portable notebook. It reloads the provenance CSV at boundaries instead of trusting stale cached permission flags. Train, validation and fusion splits require `research_training`; test allows `research_training` or `evaluation_only`. Duplicate source records, missing required evidence fields, unresolved identifiable-face status, missing keys, and invalid permissions block use. No override of REQUIRE_PROVENANCE_FOR_TRAINING can waive this policy.

The conservative policy blocks an entire requested split when any included source lacks permission; it does not silently filter samples or rewrite the frozen split. A documented exclusion/split plan is a separate decision. Presence of text fields is not verification of legal rights: a responsible human must substantiate them. No permission values, annotations, images, model binaries, or split records were changed.

## Human source verification required

The previously inspected live registry is `/home/bautista/PrecisionSkinV1/v6/annotations/dataset_provenance.csv`. All 12 keys (Acne, Eczema, Hyperpigmentation, Normal crossed with aug, other, roboflow_export) have blank permission/evidence fields and unknown identifiable-face status.

For each `aug` key, identify parent photographs, original dataset and permission for derivatives and research use. For each `roboflow_export` key, identify the original project/version, upstream owners, original license/terms and any restrictions; an export is not permission. For each `other` key, identify the actual providers or collections and trace every image to an evidenced source. All keys need actual terms, consent basis, identifiable-face determination, retention policy and a responsible person. A collection with mixed rights must not receive a blanket permission merely to pass the gate. The current keys are preserved.

## Historical exposure and limitations

`tools/historical_exposure.py` joins the frozen V6 manifest to V5's historical manifest and tests direct image membership, exact MD5 and V6 proxy-group overlap with V5 train. It writes per-image flags without predicting labels or evaluating models. It also searches candidate YOLO historical run argument files. WSL initially worked for notebook preservation and editing, but subsequently stopped responding; a second connection returned Wsl/Service/0x8007274c. Therefore the new WSL exposure results are **not established** in this maintenance run unless their output artifacts exist and are reviewed.

Earlier documentation reports 885 groups crossing V5 splits and 696/1556 historical V5 test images sharing groups with another split. These are historical findings, not newly computed V6 evaluation exposure counts.

The Windows fallback `tools/windows_exposure_candidates.py` compares exact original-image bytes and filenames against the acne dataset named by installed metadata and a candidate hyperpigmentation V2 training folder. Results concern the **original image pool**, not confirmed frozen V6 evaluation membership. Filenames alone are unconfirmed; current dataset contents do not establish historical contents. Acne metadata names acne_dataset_v4 and a dataset fingerprint; this should be recomputed using the original fingerprint algorithm. Hyperpigmentation's installed metadata hash mismatch must be reconciled before assigning training lineage. Eczema training manifests and patient/session identities remain unverified. No claim of patient independence is justified by these checks.

## Validation

The completed Windows scan inspected 1,145 acne training images and 794 candidate hyperpigmentation images. It found 1,218 acne exact match pairs (1,218 unique original-pool files) and 796 hyperpigmentation pairs (796 unique original-pool files). Several original files share identical bytes; pair counts are not independent people or independent test cases. Detailed pairs are in `audit_results/yolo_original_pool_candidates.csv`.

Validation is treated as model development, so the final boundary policy requires research_training for train, validation and fusion. Evaluation-only permission is sufficient only for the final test role. No evaluation-only sources may be used for early stopping or model selection under this policy.

Windows Anaconda Python: notebook nbformat validation passed; all code cells parse. Seven bounded tests ran: six passed, one skipped. Tests cover missing/duplicate records, each missing evidence field, the training/evaluation permission matrix, source-boundary presence and runtime permission reload despite an already-open annotation gate. Fixtures are explicitly synthetic and are not saved as research permissions.

The skipped check exercises actual mixed-float16 attention with empty and partially masked ROI sets, finite output, zero empty output and masked-feature invariance. Windows lacks TensorFlow. The WSL TensorFlow process did not return; the modified attention test and original 37-test notebook suite must not be reported as newly passed. No full dry run, model fit, final test evaluation or training smoke run was performed. `tools/bounded_audit_checks.py` is safe to rerun in the functioning training environment. Do not run the full notebook simply to rerun tests.

## Remaining decisions and blockers

Dataset owners must supply source permissions and derivative-use evidence. Dermatologists must supply real blinded multilabel annotations and verified/exhaustive region annotations; all recorded counts remain zero. Historical train manifests/snapshots must be recovered and linked to deployed weight hashes. Any exclusion of sources, frozen split revision, acquisition of new data, or environment restart requires a separate user decision; none was done automatically. No further code-fix approval is needed within the authorized scope.

Before legitimate training: restore working WSL validation, pass the attention test, complete provenance evidence, resolve exposure/lineage, satisfy unchanged annotation counts, verify supervised training readiness, and freeze the approved run configuration. WPF integration remains out of scope. The two-stage learned CNN–YOLO fusion architecture and mandatory dermatologist review remain intact.
