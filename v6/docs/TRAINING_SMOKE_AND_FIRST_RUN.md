# V6 bounded training smoke and first real run

2026-11-01. Synthetic test tensors are disposable software fixtures, not clinical annotations or thesis performance results. No dataset training, final evaluation or source-permission investigation was performed in this maintenance task.

## Training stages identified

- **Global CNN:** notebook Sections 20–22; `05_global_cnn.cellsrc`: `build_global`, `run_phase`, `training_tables`. B/C load V5 read-only; D/D2 use a three-logit MobileNetV2. Phase A freezes the backbone, Phase B partially unfreezes it with BatchNorm frozen. Latest compiled `.keras`, best `.weights.h5`, epoch CSV and state JSON support resuming. Selection uses validation, not test.
- **ROI CNN:** Sections 26–28; `07_roi_branch.cellsrc`: actual YOLO proposals matched to verified boxes, `build_roi_model`, `run_roi_phase`. Four classes include reviewed background. Same frozen/partial schedule and checkpoints. A bug was fixed: the final model now restores the validation-selected A/B checkpoint rather than always retaining B weights. Two dedicated regression tests cover A/B/tie selection and missing-checkpoint refusal.
- **Fusion:** Sections 29–31; `08_fusion.cellsrc`: `build_fusion`, `train_one_fusion`, `crossfit_fusion`. Global features, detector evidence and masked ROI sets feed learned fusion. Grouped cross-fitting precedes calibration. Final per-seed `.keras` files are saved; the current controller does not implement mid-fold/epoch fusion resume. The serialization smoke tests optimizer restoration mechanics, not automatic fold resumption.
- **Export:** Sections 50–52; `11_export_tests_report.cellsrc`: global/ROI feature ONNX, fusion ONNX with explicit JSON fallback, metadata and parity checks. Original V5/YOLO models are not re-exported. The present training smoke checks Keras/weights checkpoints; it does not certify full trained-component ONNX or C# parity.

## Smoke test scope and evidence

**Completed results:** CPU software checks **3 passed, 0 failed**. RTX 4060 GPU checks **3 passed, 0 failed**, using mixed_float16 for CNNs (fusion internally float32), batch 8 for global/ROI and batch 2 for fusion. Both CNN phases updated weights; complete optimizer variables/iterations restored exactly, predictions matched after reload, and resumed optimizer updates succeeded. The GPU run took **124.09 seconds**, with **1,010,684,672 bytes (about 964 MiB)** peak tracked TensorFlow GPU allocation under the 4096 MiB cap. This excludes driver/other-process allocations and does not establish batch 32 or whole-epoch memory behavior. All fingerprinted original V5 and specialist model files remained unchanged. The checkpoint-selection regressions passed **2/2 in WSL**; an initial Windows run encountered sandbox temporary-directory cleanup errors, not assertion failures.

`tools/smoke_train_synthetic.py` extracts the actual notebook model/loss definitions without executing dataset-training cells. It uses cached ImageNet initialization, random normalized tensors, synthetic masks/targets, temporary checkpoints and a five-minute external limit. It verifies finite forward/loss/gradients, nonzero gradients, changed trainable weights, partially unfrozen backbone updates, frozen BatchNorm, full-model/optimizer-state restoration, continued optimizer updates, and weights-only restoration. Fusion uses the actual 128-dimensional global embedding, 21 detector features and 12×83 padded ROI tensor shape, with empty/partial masks.

Results are recorded in `audit_results/synthetic_training_smoke.json` and its log; the CPU result is separately preserved as `synthetic_training_smoke_cpu.json`. The first GPU attempt is retained as `synthetic_training_smoke_gpu_initial.json`: one strict assertion saw no backbone update on the first AMP fine-tune step. The harness now permits up to eight dynamic-loss-scale warm-up attempts and still requires an actual update and finite losses; no real training gate was changed. These are bounded numerical checks, not accuracy claims.

The GPU initially failed registration in a non-interactive shell because its installed CUDA libraries were not on the loader path. `tools/run_synthetic_training_smoke.sh` supplies process-local CUDA 12 paths, single-thread CPU limits and a 4 GB TensorFlow GPU cap. It does not install packages, change `.bashrc` or alter system settings.

`v6_first_training_config.example.json` is a conservative, unactivated proposal: CNN/ROI batch 8, GPU cap 4096 MiB, offline weights, unchanged annotation gates, dermatologist requirement and test lock. Batch 32 defaults should not be described as measured safe from a small smoke test. A full epoch, actual data input pipeline, callbacks/early stopping, every V5 softmax variant, and final ONNX export remain outside this smoke's validation scope.

## Existing labels and practical reuse

The current annotation files were rechecked: **9,789 unverified image rows, zero accepted verified multilabel rows, zero V6 region rows**. Folder labels remain weak labels; no absent findings were inferred.

`audit_results/train_validation_box_review_candidates.csv` indexes existing image-matched drafts without altering them:

- Train: acne 509 images; corrected eczema 170; hyperpigmentation export 181; hyperpigmentation rule-v2 candidate folder 332.
- Validation: acne 94; corrected eczema 38; hyperpigmentation export 27; rule-v2 folder 50.

Counts are per source and may overlap; do not sum them as distinct patients. The row's image ID uses the frozen V6 mapping. Draft box paths and source are supplied, with no clinical labels. Permissions remain unresolved as previously documented; this task did not reopen the investigation or grant permissions.

Use the existing live annotation CSV and review ranks. First record independent Acne/Hyperpigmentation/Eczema Present–Absent–Unknown findings without showing AI predictions or box drafts. Record actual reviewer role, pseudonymous ID, time and blinding. Then review the matched box drafts on the identical original image, correcting in the approved V6 annotation file only through a human annotation workflow; identify actual background examples and exhaustive coverage. Empty/missing boxes never establish negative findings. Clinical review remains mandatory for every issued assessment, independently of research annotation eligibility.

## Minimum before the first genuine global-CNN run

1. Satisfy current source-use gates for the sources included in train and validation, with actual evidence and no assumed grants. The present conservative split-level gate is not bypassed by selecting a few annotated images. A restricted eligibility/split change would require a separate approved plan.
2. **Train:** 300 fully decided images, at least 40 accepted Present and 40 Absent labels for each condition. Current policy accepts dermatologist/researcher training roles; reviewer credentials and real verification must be recorded.
3. **Validation:** 150 fully decided dermatologist-reviewed images, at least 20 Present and 20 Absent for each condition. These labels drive model selection; do not use final-test findings.
4. Pass readiness gates, select a new run ID and launch through the verified CUDA environment. Watch the first real epoch for data/callback behavior; no full training has been authorized or started here.

**Region annotations are not needed to begin the global stage.** Before ROI training, additionally provide 60 verified boxes per each of four classes in train and 20 per class in validation (320 total), with actual proposals producing all four training classes and at least 100 rows. Before fusion/calibration, meet the 250-image fusion gate and per-condition 30 Present/30 Absent minima. The final test gate and previously identified exposure/independence problems remain separate; no split was changed.

The fastest next human work is a source-cleared train/validation annotation batch, targeting these 450 fully decided images rather than waiting for all 950 across every stage. This is a staged work plan, not permission to relax any gate or treat inherited labels as ground truth.
