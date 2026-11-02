# LUMYVUE V6 Astra manual Jupyter guide

Open `v6/LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb`. Cell references below are **one-based positions**, not Jupyter execution counters. The notebook has 166 cells. Keep the `astra_training_src` directory beside it; the code is inlined, and source hashes bind the run to the maintained implementation.

Do not use Run All. Each expensive research action requires a one-use token set in a separate scratch cell. For example:

```python
ASTRA_ACTION = 'global_A'
```

Then run only the corresponding action cell. The token is consumed, even if readiness blocks the operation. It never overrides annotation, source-use, calibration, or test protections. Every phase loops over the configured experiments/seeds; these loops can take hours. No research training has been performed during preparation.

## 1. Start WSL Ubuntu

In Windows PowerShell:

```powershell
wsl -d Ubuntu
```

## 2. Activate the actual Python environment

```bash
source ~/PrecisionSkinV1/.venv/bin/activate
python -c 'import sys; print(sys.executable)'
cd /mnt/c/Users/Bautista/source/repos/SkinDevApp/v6
```

The verified interpreter is `/home/bautista/PrecisionSkinV1/.venv/bin/python`. Do not select Windows Anaconda for GPU training. Existing runtime versions are TensorFlow 2.21.0 and Keras 3.15.1.

## 3. CUDA environment

The supplied `astra_training_src/launch_jupyter.sh` uses the same process-local CUDA library discovery as the passing GPU smoke checks: `/usr/lib/wsl/lib` plus the installed venv `nvidia/*/lib` directories. It sets bounded CPU thread counts. It does not edit `.bashrc`, install CUDA, or change system configuration. The library paths must be set **before** starting the kernel; importing TensorFlow first is too late.

## 4. Launch Jupyter

Inspection found `ipykernel` available, but **JupyterLab, Notebook server, and nbformat absent from this WSL venv**. They are not required to execute cells through an existing WSL notebook editor. If you want browser JupyterLab, explicitly run this installation yourself:

```bash
python -m pip install jupyterlab nbformat
```

Then start a unique experiment (choose your own ID; record it for recovery):

```bash
bash astra_training_src/launch_jupyter.sh astra_run01
```

Open the localhost URL printed by Jupyter in your browser. Keep the terminal running. The launcher stops with an explanation if the server is missing. No packages were installed by the preparation work. An alternative is your existing VS Code WSL notebook editor with the same interpreter; launch it from the CUDA-configured environment or ensure its kernel receives the same `LD_LIBRARY_PATH`.

## 5. Select and verify the kernel

Select the Python kernel backed by `/home/bautista/PrecisionSkinV1/.venv/bin/python`. The notebook's display name is a label, not proof of interpreter selection. Check `sys.executable` in cell 10 (`3.1 Package check (set INSTALL_MISSING=True only if you want this cell to pip-install what is missing).`). If using VS Code, set `os.environ['LUMYVUE_RUN_ID']='astra_run01'` before running configuration.

## 6. Open the new notebook

Open **LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb**, not the original Codex notebook. Start from a fresh kernel. Execute the bootstrap, overview and setup cells in order through cell 20 (`Pin the entire run before any research data cache is reused.`). Stop on errors.

## 7. Confirm RTX 4060 detection

cell 12 (`3.2 / 4.1 / 6.2  Imports, seeds, GPU setup (memory growth, optional cap, mixed precision), hardware + software metadata, experiment folders.`) must show a GPU, mixed-float16 policy, and the configured 4096 MB cap. Confirm with `nvidia-smi` in WSL. If CPU fallback appears, stop before training and correct the kernel/library environment. If the GPU was already initialized, restart the kernel; do not silently ignore a failed memory cap.

## 8. Review configuration

Edit configuration before executing cell 11 (`6.1 Experiment configuration. Override order (later wins): defaults < JSON file ($LUMYVUE_V6_CONFIG) < environment LUMYVUE_<NAME>.`), or provide a JSON file through `LUMYVUE_V6_CONFIG`. Defaults: global batch 8, ROI batch 8, fusion batch 2, TensorFlow GPU cap 4096 MB; these are starting settings, not full-epoch memory guarantees. Class order is `[Acne, Hyperpigmentation, Eczema]`; ROI adds `Background`. The 128/21/12×83 feature contract is preserved.

Inputs default to the existing WSL project, frozen split directory and annotations. Outputs are fixed to this repository's `v6/astra_runs/<RUN_ID>/`. IDs accept letters, digits, underscore and hyphen. No path traversal or output-root symlinks are allowed. `CFG.REVIEW_QUEUE_CSV` is redirected into the new run. Existing annotation files and pseudonym key remain read-only.

Changing configuration, annotations, split content, detector weights or source code requires a **new RUN_ID**. Do not reuse an old ID to evade a mismatch error. B/C remain four-class comparison experiments; the hybrid's selected global model must come from a three-logit multilabel experiment D/D2. This requirement is an explicit correction to original selection behavior.

## 9. Dataset readiness checks

Continue the Part II cells in order. They inventory files, check duplicates and quality, reload the **existing** frozen manifest, verify source-use records, and load annotations. They do not create/rewrite frozen splits. The notebook keeps Normal entries in every partition. Unknown labels remain masked, not negative. The inherited optional weak-positive policy is explicitly marked and cannot satisfy verified annotation gates; Normal-folder membership never supplies negative labels.

## 10. Recognize blockers

Current counts: 9,789 frozen entries; Acne 3,000, Hyperpigmentation 3,000, Eczema 789, Normal 3,000. All 9,789 annotation rows remain unverified; accepted fully decided rows = 0; region rows = 0; all 12 source-use entries are blank. Global minimums are 300 train / 150 validation fully decided images, with 40/20 Present **and** Absent per condition. Later fusion/test minimums are 250 each and 30 per polarity/condition. Regions require 60 per class in train, 20 in each later split, plus all four classes and ≥100 matched ROI training rows. Reviewer IDs/timestamps must be recorded; evaluation image references must be blinded dermatologist reviews. These checks establish record eligibility, not proof that a review was authentic.

Stop when these prerequisites fail. Genuine reviewers and responsible source owners must resolve them. The notebook cannot manufacture annotations or grant rights. Use the separate annotation workflow; Astra's inherited annotation-writing helper is intentionally disabled. No broad provenance research is repeated.

## 11. Run Global Phase A

First complete the V5 comparison/hard-example development cells. cell 48 (`V5_PROBS_PATH = os.path.join(CFG.DIRS["predictions"], "v5_probs_all.csv")`) requires token `baseline_development` and source-use clearance. It processes development partitions only. Continue Global definitions/data pipeline cells through the phase controls. Set token `global_A`, then run cell 66 (`astra_require_action('global_A')`). This trains frozen-backbone heads for the configured experiments/seeds.

## 12. Inspect learning curves

Run cell 142 (`44.1 Training and validation curves (loss; precision / recall / F1) for every trained global experiment and the ROI model.`) directly after Phase A; it defines/calls `plot_histories()` and does not open the test set. Inspect `loss`, `val_loss`, `val_macro_ap`/`val_macro_f1`, `val_sel`, and the saved histories under `checkpoints/<experiment>/seed_<seed>/phaseA_history.csv`. No figures are fabricated if histories are absent.

## 13. Run Global Phase B

Set token `global_B`, run cell 68 (`astra_require_action('global_B')`). It loads the selected Phase A model, partially unfreezes the backbone, keeps BatchNorm frozen, and recompiles with a fresh phase optimizer and the configured lower LR. It does not carry incompatible Phase A optimizer state into Phase B.

## 14. Find selected global checkpoint

Each seed gets `phase_selection.json`; validation `val_sel` chooses between A and B, with B winning exact ties. The actual winning full checkpoint is restored before `final_best.keras` is saved. Continue Section 22 to choose the multilabel experiment/seed using validation AP and record regression warnings. The result is `metrics/global_selection.json`. Never infer selection from the last completed phase.

## 15. Prepare YOLO evidence

Run detector registry/contract definitions, then token `yolo_evidence` for cell 74 (`24.2 Run the detectors over the dataset images that matter (annotated images + region-annotated images), with append-only caching and resume.`). It uses the existing ensembles read-only, per-detector sizes/metadata, RGB letterbox/NCHW scaling, per-class NMS and confidence-weighted ensemble aggregation. Default scope is annotated **development** images; no automatic unannotated preview or early test inference. Continue flattening/diagnostics cells. Inspect the recorded model hashes, availability and JSON-hash mismatch warnings. A predicted box is only a proposal, not a reviewed reference.

## 16. Run ROI Phase A

Execute crop/matching/data-table/model definitions in order. Review proposal matches and four-class counts. Set token `roi_A`, run cell 91 (`astra_require_action('roi_A')`). ROI crops stream in batches instead of keeping all image tensors in RAM.

## 17. Run ROI Phase B

Set token `roi_B`, run cell 93 (`astra_require_action('roi_B')`). It uses a new optimizer after partial unfreezing and selects the actual better phase. Inspect the same history-plot cell and `metrics/roi_training_summary.json`.

## 18. Restore selected ROI

Phase B's control cell automatically restores the winning phase and writes `checkpoints/ROI/seed_<seed>/final_best.keras`. `phase_selection.json` records the source checkpoint/hash. Missing or changed checkpoints stop explicitly; no Phase B substitution is allowed when A wins.

## 19. Prepare fusion features

Continue ROI set and fusion builder definitions. Input order is global embedding, global logits, global extras, detector evidence, ROI features, ROI mask. Per-condition evidence has seven fields (21 total); each of up to 12 regions has 64 embedding + 4 ROI scores + 15 geometry/evidence features. Empty ROI sets are legal and masked. Cache identity includes model hash, tiers and configuration.

## 20. Grouped fusion training

Set token `fusion_train`, run cell 101 (`astra_require_action('fusion_train')`). This is expensive: every configured variant, seed and grouped fold runs. Outer held-out groups never enter that fold's training/inner validation. Final per-seed models retain a grouped inner holdout for checkpoint selection; the original claim that they train on every fusion image was inaccurate. Proxy group separation does not prove patient independence.

## 21. Resume after interruption

Restart the same environment/kernel and replay prerequisites using the **same RUN_ID and unchanged inputs/source**. Re-arm only the needed action. Completed phases/folds restore selected checkpoints without fitting again. Global/ROI restore latest committed epoch, optimizer and callback controller state. Interrupted fusion folds restart only that incomplete job in `attempt_0002`, etc.; completed folds/seeds are reused. Mid-epoch work is lost. Shuffling/RNG/device state is not bitwise-exact across kernel restarts. A phase boundary always gets a new optimizer; an epoch resume within a phase restores its optimizer. Keep both current/latest and selected-best artifacts.

## 22. Calibration

Set token `calibration`, run cell 103 (`from scipy.optimize import minimize`). Only fusion out-of-fold logits and known development labels fit Platt scaling and thresholds. Then token `stage_rules` for cell 110 (`33.2 Stage-1 reliability rule: flags (conflict / near-threshold / quality / OOD) and a band selected on OUT-OF-FOLD fusion data to meet TARGET_ACCEPTED_ERROR.`) selects reliability bands on development data. Missing per-condition calibration prevents an issued supported assessment; unavailable reliability selection forces abstention rather than arbitrary Normal.

## 23. Stage 1 inference

Continue the remaining stage/assessment definitions. The actual preserved Stage 1 decision uses `global_yolo` fusion and calibrated global-only disagreement evidence. It is not global-CNN-only and is not full ROI fusion. For a permitted development image, instantiate `InferenceBundle()` and call `assess_image(bundle, path)`. The live reference currently precomputes ROI features even if Stage 1 ultimately accepts; report this runtime limitation honestly.

## 24. Stage 2 verification

`assess_image` automatically enters Stage 2 when the reliability rule requires it, integrates ROI full-fusion evidence in tier 1, adds candidate regions in tier 2, then wider context crops in tier 3. It stops for resolution, no new evidence, convergence, bounds, recapture or instability. There are at most three **Stage 2 evidence views**, in addition to initial Stage 1; these are not three new camera captures. Use `astra_profile_runtime(path)` after its definition for actual timed model calls. Its extra Stage 2 fusion time excludes eager ROI precomputation; it is not a complete deployment overhead benchmark.

## 25. Normal evaluation

Run token `development_evaluation` at cell 122 (`astra_require_action('development_evaluation')`). Derived Normal metrics use only fully reviewed all-negative reference rows and `NO_FINDINGS_SUPPORTED` states, with abstentions reported separately. Outputs include false Normal, missed-condition cases called Normal, confusion matrices, accepted-reference recall and all-reference recall. ROI Background and no detector boxes never imply Normal.

## 26. Development metrics

The same development cell writes OOF per-condition/multilabel metrics, stage transitions, corrected/introduced errors and Normal analysis. These data also selected calibration/rules, so these are **development estimates**. Token `roi_validation` at cell 126 (`def astra_roi_validation():`) writes four-class ROI metrics, confusion matrix and ROC/PR curves. Global validation metrics and all learning curves are already generated by earlier cells. Undefined AUCs require both reference classes and remain unavailable.

## 27. Unlock final evaluation

Do this only after all models, calibration, rules and the evaluation protocol are frozen, references are eligible, and your adviser has addressed historical model exposure. Set `ASTRA_EVALUATION_NOTE` to the actual approved scope/limitations and token `final_test`, then run cell 128 (`astra_require_action('final_test')`). The original cohort is not automatically independent of V5/YOLO development. For an approved independent cohort, prepare a separately approved manifest/annotation workspace and a new run/configuration; Astra does not reshuffle or import one automatically. Test labels never select thresholds/checkpoints.

The cell explicitly prepares test baseline/detector features, checks prerequisites, then records `metrics/test_evaluated.lock.json` immediately before metrics. A second evaluation is refused unless the existing explicit reproduction flag is enabled. Record the reason; never use repeat evaluation for tuning.

## 28. Figures and error cases

After the locked evaluation, continue the remaining evaluation cells sequentially: systems A–G, group-bootstrap intervals, reviewed exhaustive localization AP/IoU, ablations, risk/coverage, perturbation sensitivity, Grad-CAM, PR/ROC, calibration and galleries. These are expensive. `figures/`, `metrics/`, and `predictions/` contain actual generated outputs. Localization AP uses the preserved confidence-floor protocol, not an assertion of Ultralytics evaluation equivalence. Region recall and precision need exhaustive human annotation.

## 29. Export

Execute export definitions, then token `export` at cell 150 (`astra_require_action('export')`). This writes selected Keras models, feature models, and ONNX components under this Astra run only. Every seed of runtime fusion variants is exported; deployment must average per-seed logits **before** calibration. No original YOLO/V5 file is re-exported or overwritten.

## 30. Verify ONNX parity

Continue Section 51. Identical development tensors are sent to Keras and ONNX Runtime, with per-output errors and hashes in `v6_keras_vs_onnx_verification.json`. Tolerance is 1e-4; failures are recorded, not waived. Synthetic float32 parity passed during preparation; real trained mixed-precision components still require these checks. JSON/native fusion weights are a reference for a future port, not a validated C# fallback. No single graph combines all detectors and both stages.

## 31. Locate outputs

All research artifacts: `C:\Users\Bautista\source\repos\SkinDevApp\v6\astra_runs\<RUN_ID>\`. Subdirectories include config, checkpoints, logs, metrics, figures, yolo, roi, fusion, stages, export and report. `v6_run_manifest.json` hashes export artifacts and records class/feature order, masks, calibration and parity. Synthetic test outputs are separately under `v6/astra_tests/`, with clearly labeled temporary/checkpoint artifacts.

## 32. GPU/RAM problems

Stop training on OOM, keep committed checkpoints and restart the kernel to release TensorFlow memory. Avoid running multiple training kernels. Never terminate another kernel without saving its work and choosing to do so. If changing batch/cap/hyperparameters, use a new run ID; do not silently change the experiment while reusing checkpoints. A passing bounded batch does not prove full-epoch memory safety.

## 33. Checkpoint restoration problems

Read the failing contract/hash/path. Do not delete or edit contract JSON to force reuse. Restore a known backup for corrupted/missing artifacts, or choose a new run. Only an incomplete fusion fold is restarted automatically. The original notebook's checkpoints are not imported into Astra experiments.

## 34. Report errors

Send the notebook filename, exact cell heading/one-based position, traceback, run ID, relevant config/selection/contract records, Python/TensorFlow/Keras versions, and whether the run was resumed. Redact private paths/identifiers. Do not send patient images, permission keys or confidential review records by default. Include `astra_tests/*.json` for engineering failures.

## Rebuilding and validation

Maintain `astra_training_src/*.cellsrc` and `astra_evaluation.cellpy`, then run `python astra_training_src/build_astra.py` in an environment with nbformat (Windows Anaconda currently has it). This only assembles/validates syntax; it never executes training. Rebuilds change code fingerprints and may require a new research run. The full cell index is `astra_docs/cell_index.json`.
