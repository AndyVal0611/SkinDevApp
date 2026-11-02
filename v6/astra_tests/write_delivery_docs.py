"""Generate traceability and exact-cell guide from delivered files and saved test results."""
from pathlib import Path
import json,hashlib,difflib
root=Path(__file__).resolve().parents[1];docs=root/'astra_docs';repo=root.parent
nb=json.loads((root/'LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb').read_text(encoding='utf-8'))
cells=[''.join(c['source']) for c in nb['cells']]
def cell(marker):
    n=next(i+1 for i,s in enumerate(cells) if marker in s)
    return f"cell {n} (`{cells[n-1].splitlines()[0].lstrip('# ')}`)"
def action(name):return cell("astra_require_action('"+name+"')")
def write(name,text):(docs/name).write_text(text,encoding='utf-8')
def load(name):return json.loads((root/'astra_tests'/name).read_text(encoding='utf-8'))
env=load('environment_inventory.json');static=load('static_results.json');contracts=load('contracts_results.json');gpu=load('synthetic_training_gpu.json');cpu=load('synthetic_training_cpu.json');exports=load('synthetic_export_results.json');setup=load('setup_results.json')
inherited=load('inherited_results.json');v5=load('v5_initialization_results.json')
guide=f'''# LUMYVUE V6 Astra manual Jupyter guide

Open `v6/LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb`. Cell references below are **one-based positions**, not Jupyter execution counters. The notebook has {len(cells)} cells. Keep the `astra_training_src` directory beside it; the code is inlined, and source hashes bind the run to the maintained implementation.

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

The verified interpreter is `/home/bautista/PrecisionSkinV1/.venv/bin/python`. Do not select Windows Anaconda for GPU training. Existing runtime versions are TensorFlow {gpu['tensorflow']} and Keras {gpu['keras']}.

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

Select the Python kernel backed by `/home/bautista/PrecisionSkinV1/.venv/bin/python`. The notebook's display name is a label, not proof of interpreter selection. Check `sys.executable` in {cell('# 3.1 Package check')}. If using VS Code, set `os.environ['LUMYVUE_RUN_ID']='astra_run01'` before running configuration.

## 6. Open the new notebook

Open **LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb**, not the original Codex notebook. Start from a fresh kernel. Execute the bootstrap, overview and setup cells in order through {cell('# Pin the entire run')}. Stop on errors.

## 7. Confirm RTX 4060 detection

{cell('# 3.2 / 4.1 / 6.2')} must show a GPU, mixed-float16 policy, and the configured 4096 MB cap. Confirm with `nvidia-smi` in WSL. If CPU fallback appears, stop before training and correct the kernel/library environment. If the GPU was already initialized, restart the kernel; do not silently ignore a failed memory cap.

## 8. Review configuration

Edit configuration before executing {cell('# 6.1 Experiment configuration')}, or provide a JSON file through `LUMYVUE_V6_CONFIG`. Defaults: global batch 8, ROI batch 8, fusion batch 2, TensorFlow GPU cap 4096 MB; these are starting settings, not full-epoch memory guarantees. Class order is `[Acne, Hyperpigmentation, Eczema]`; ROI adds `Background`. The 128/21/12×83 feature contract is preserved.

Inputs default to the existing WSL project, frozen split directory and annotations. Outputs are fixed to this repository's `v6/astra_runs/<RUN_ID>/`. IDs accept letters, digits, underscore and hyphen. No path traversal or output-root symlinks are allowed. `CFG.REVIEW_QUEUE_CSV` is redirected into the new run. Existing annotation files and pseudonym key remain read-only.

Changing configuration, annotations, split content, detector weights or source code requires a **new RUN_ID**. Do not reuse an old ID to evade a mismatch error. B/C remain four-class comparison experiments; the hybrid's selected global model must come from a three-logit multilabel experiment D/D2. This requirement is an explicit correction to original selection behavior.

## 9. Dataset readiness checks

Continue the Part II cells in order. They inventory files, check duplicates and quality, reload the **existing** frozen manifest, verify source-use records, and load annotations. They do not create/rewrite frozen splits. The notebook keeps Normal entries in every partition. Unknown labels remain masked, not negative. The inherited optional weak-positive policy is explicitly marked and cannot satisfy verified annotation gates; Normal-folder membership never supplies negative labels.

## 10. Recognize blockers

Current counts: 9,789 frozen entries; Acne 3,000, Hyperpigmentation 3,000, Eczema 789, Normal 3,000. All 9,789 annotation rows remain unverified; accepted fully decided rows = 0; region rows = 0; all 12 source-use entries are blank. Global minimums are 300 train / 150 validation fully decided images, with 40/20 Present **and** Absent per condition. Later fusion/test minimums are 250 each and 30 per polarity/condition. Regions require 60 per class in train, 20 in each later split, plus all four classes and ≥100 matched ROI training rows. Reviewer IDs/timestamps must be recorded; evaluation image references must be blinded dermatologist reviews. These checks establish record eligibility, not proof that a review was authentic.

Stop when these prerequisites fail. Genuine reviewers and responsible source owners must resolve them. The notebook cannot manufacture annotations or grant rights. Use the separate annotation workflow; Astra's inherited annotation-writing helper is intentionally disabled. No broad provenance research is repeated.

## 11. Run Global Phase A

First complete the V5 comparison/hard-example development cells. {action('baseline_development')} requires token `baseline_development` and source-use clearance. It processes development partitions only. Continue Global definitions/data pipeline cells through the phase controls. Set token `global_A`, then run {action('global_A')}. This trains frozen-backbone heads for the configured experiments/seeds.

## 12. Inspect learning curves

Run {cell('# 44.1 Training and validation curves')} directly after Phase A; it defines/calls `plot_histories()` and does not open the test set. Inspect `loss`, `val_loss`, `val_macro_ap`/`val_macro_f1`, `val_sel`, and the saved histories under `checkpoints/<experiment>/seed_<seed>/phaseA_history.csv`. No figures are fabricated if histories are absent.

## 13. Run Global Phase B

Set token `global_B`, run {action('global_B')}. It loads the selected Phase A model, partially unfreezes the backbone, keeps BatchNorm frozen, and recompiles with a fresh phase optimizer and the configured lower LR. It does not carry incompatible Phase A optimizer state into Phase B.

## 14. Find selected global checkpoint

Each seed gets `phase_selection.json`; validation `val_sel` chooses between A and B, with B winning exact ties. The actual winning full checkpoint is restored before `final_best.keras` is saved. Continue Section 22 to choose the multilabel experiment/seed using validation AP and record regression warnings. The result is `metrics/global_selection.json`. Never infer selection from the last completed phase.

## 15. Prepare YOLO evidence

Run detector registry/contract definitions, then token `yolo_evidence` for {action('yolo_evidence')}. It uses the existing ensembles read-only, per-detector sizes/metadata, RGB letterbox/NCHW scaling, per-class NMS and confidence-weighted ensemble aggregation. Default scope is annotated **development** images; no automatic unannotated preview or early test inference. Continue flattening/diagnostics cells. Inspect the recorded model hashes, availability and JSON-hash mismatch warnings. A predicted box is only a proposal, not a reviewed reference.

## 16. Run ROI Phase A

Execute crop/matching/data-table/model definitions in order. Review proposal matches and four-class counts. Set token `roi_A`, run {action('roi_A')}. ROI crops stream in batches instead of keeping all image tensors in RAM.

## 17. Run ROI Phase B

Set token `roi_B`, run {action('roi_B')}. It uses a new optimizer after partial unfreezing and selects the actual better phase. Inspect the same history-plot cell and `metrics/roi_training_summary.json`.

## 18. Restore selected ROI

Phase B's control cell automatically restores the winning phase and writes `checkpoints/ROI/seed_<seed>/final_best.keras`. `phase_selection.json` records the source checkpoint/hash. Missing or changed checkpoints stop explicitly; no Phase B substitution is allowed when A wins.

## 19. Prepare fusion features

Continue ROI set and fusion builder definitions. Input order is global embedding, global logits, global extras, detector evidence, ROI features, ROI mask. Per-condition evidence has seven fields (21 total); each of up to 12 regions has 64 embedding + 4 ROI scores + 15 geometry/evidence features. Empty ROI sets are legal and masked. Cache identity includes model hash, tiers and configuration.

## 20. Grouped fusion training

Set token `fusion_train`, run {action('fusion_train')}. This is expensive: every configured variant, seed and grouped fold runs. Outer held-out groups never enter that fold's training/inner validation. Final per-seed models retain a grouped inner holdout for checkpoint selection; the original claim that they train on every fusion image was inaccurate. Proxy group separation does not prove patient independence.

## 21. Resume after interruption

Restart the same environment/kernel and replay prerequisites using the **same RUN_ID and unchanged inputs/source**. Re-arm only the needed action. Completed phases/folds restore selected checkpoints without fitting again. Global/ROI restore latest committed epoch, optimizer and callback controller state. Interrupted fusion folds restart only that incomplete job in `attempt_0002`, etc.; completed folds/seeds are reused. Mid-epoch work is lost. Shuffling/RNG/device state is not bitwise-exact across kernel restarts. A phase boundary always gets a new optimizer; an epoch resume within a phase restores its optimizer. Keep both current/latest and selected-best artifacts.

## 22. Calibration

Set token `calibration`, run {action('calibration')}. Only fusion out-of-fold logits and known development labels fit Platt scaling and thresholds. Then token `stage_rules` for {action('stage_rules')} selects reliability bands on development data. Missing per-condition calibration prevents an issued supported assessment; unavailable reliability selection forces abstention rather than arbitrary Normal.

## 23. Stage 1 inference

Continue the remaining stage/assessment definitions. The actual preserved Stage 1 decision uses `global_yolo` fusion and calibrated global-only disagreement evidence. It is not global-CNN-only and is not full ROI fusion. For a permitted development image, instantiate `InferenceBundle()` and call `assess_image(bundle, path)`. The live reference currently precomputes ROI features even if Stage 1 ultimately accepts; report this runtime limitation honestly.

## 24. Stage 2 verification

`assess_image` automatically enters Stage 2 when the reliability rule requires it, integrates ROI full-fusion evidence in tier 1, adds candidate regions in tier 2, then wider context crops in tier 3. It stops for resolution, no new evidence, convergence, bounds, recapture or instability. There are at most three **Stage 2 evidence views**, in addition to initial Stage 1; these are not three new camera captures. Use `astra_profile_runtime(path)` after its definition for actual timed model calls. Its extra Stage 2 fusion time excludes eager ROI precomputation; it is not a complete deployment overhead benchmark.

## 25. Normal evaluation

Run token `development_evaluation` at {action('development_evaluation')}. Derived Normal metrics use only fully reviewed all-negative reference rows and `NO_FINDINGS_SUPPORTED` states, with abstentions reported separately. Outputs include false Normal, missed-condition cases called Normal, confusion matrices, accepted-reference recall and all-reference recall. ROI Background and no detector boxes never imply Normal.

## 26. Development metrics

The same development cell writes OOF per-condition/multilabel metrics, stage transitions, corrected/introduced errors and Normal analysis. These data also selected calibration/rules, so these are **development estimates**. Token `roi_validation` at {action('roi_validation')} writes four-class ROI metrics, confusion matrix and ROC/PR curves. Global validation metrics and all learning curves are already generated by earlier cells. Undefined AUCs require both reference classes and remain unavailable.

## 27. Unlock final evaluation

Do this only after all models, calibration, rules and the evaluation protocol are frozen, references are eligible, and your adviser has addressed historical model exposure. Set `ASTRA_EVALUATION_NOTE` to the actual approved scope/limitations and token `final_test`, then run {action('final_test')}. The original cohort is not automatically independent of V5/YOLO development. For an approved independent cohort, prepare a separately approved manifest/annotation workspace and a new run/configuration; Astra does not reshuffle or import one automatically. Test labels never select thresholds/checkpoints.

The cell explicitly prepares test baseline/detector features, checks prerequisites, then records `metrics/test_evaluated.lock.json` immediately before metrics. A second evaluation is refused unless the existing explicit reproduction flag is enabled. Record the reason; never use repeat evaluation for tuning.

## 28. Figures and error cases

After the locked evaluation, continue the remaining evaluation cells sequentially: systems A–G, group-bootstrap intervals, reviewed exhaustive localization AP/IoU, ablations, risk/coverage, perturbation sensitivity, Grad-CAM, PR/ROC, calibration and galleries. These are expensive. `figures/`, `metrics/`, and `predictions/` contain actual generated outputs. Localization AP uses the preserved confidence-floor protocol, not an assertion of Ultralytics evaluation equivalence. Region recall and precision need exhaustive human annotation.

## 29. Export

Execute export definitions, then token `export` at {action('export')}. This writes selected Keras models, feature models, and ONNX components under this Astra run only. Every seed of runtime fusion variants is exported; deployment must average per-seed logits **before** calibration. No original YOLO/V5 file is re-exported or overwritten.

## 30. Verify ONNX parity

Continue Section 51. Identical development tensors are sent to Keras and ONNX Runtime, with per-output errors and hashes in `v6_keras_vs_onnx_verification.json`. Tolerance is 1e-4; failures are recorded, not waived. Synthetic float32 parity passed during preparation; real trained mixed-precision components still require these checks. JSON/native fusion weights are a reference for a future port, not a validated C# fallback. No single graph combines all detectors and both stages.

## 31. Locate outputs

All research artifacts: `C:\\Users\\Bautista\\source\\repos\\SkinDevApp\\v6\\astra_runs\\<RUN_ID>\\`. Subdirectories include config, checkpoints, logs, metrics, figures, yolo, roi, fusion, stages, export and report. `v6_run_manifest.json` hashes export artifacts and records class/feature order, masks, calibration and parity. Synthetic test outputs are separately under `v6/astra_tests/`, with clearly labeled temporary/checkpoint artifacts.

## 32. GPU/RAM problems

Stop training on OOM, keep committed checkpoints and restart the kernel to release TensorFlow memory. Avoid running multiple training kernels. Never terminate another kernel without saving its work and choosing to do so. If changing batch/cap/hyperparameters, use a new run ID; do not silently change the experiment while reusing checkpoints. A passing bounded batch does not prove full-epoch memory safety.

## 33. Checkpoint restoration problems

Read the failing contract/hash/path. Do not delete or edit contract JSON to force reuse. Restore a known backup for corrupted/missing artifacts, or choose a new run. Only an incomplete fusion fold is restarted automatically. The original notebook's checkpoints are not imported into Astra experiments.

## 34. Report errors

Send the notebook filename, exact cell heading/one-based position, traceback, run ID, relevant config/selection/contract records, Python/TensorFlow/Keras versions, and whether the run was resumed. Redact private paths/identifiers. Do not send patient images, permission keys or confidential review records by default. Include `astra_tests/*.json` for engineering failures.

## Rebuilding and validation

Maintain `astra_training_src/*.cellsrc` and `astra_evaluation.cellpy`, then run `python astra_training_src/build_astra.py` in an environment with nbformat (Windows Anaconda currently has it). This only assembles/validates syntax; it never executes training. Rebuilds change code fingerprints and may require a new research run. The full cell index is `astra_docs/cell_index.json`.
'''
write('LUMYVUE_V6_ASTRA_MANUAL_JUPYTER_GUIDE.md',guide)
changes=[];diffs=[]
for p in sorted((root/'astra_training_src').glob('*.cellsrc')):
    old=root/'notebook_src'/p.name
    before=old.read_text(encoding='utf-8');after=p.read_text(encoding='utf-8')
    diff=list(difflib.unified_diff(before.splitlines(True),after.splitlines(True),fromfile='Codex/'+p.name,tofile='Astra/'+p.name))
    diffs.extend(diff)
    changes.append(dict(source=p.name,original_sha256=hashlib.sha256(old.read_bytes()).hexdigest(),astra_sha256=hashlib.sha256(p.read_bytes()).hexdigest(),unchanged=not diff,added_lines=sum(x.startswith('+') and not x.startswith('+++') for x in diff),removed_lines=sum(x.startswith('-') and not x.startswith('---') for x in diff)))
write('source_comparison.json',json.dumps(changes,indent=2));write('CODEX_TO_ASTRA.patch',''.join(diffs))
comparison='''# Codex V6 vs Astra pipeline comparison

The reference was the actual existing `v6/notebook_src` tree, including its unfinished recovery helper, not a reimplementation from a prose architecture. Original notebook/source files were not regenerated. `source_comparison.json` records every corresponding source hash; `CODEX_TO_ASTRA.patch` provides the exact line-level changes. The self-contained notebook embeds the Astra sources.

## Reused implementation

Global MobileNetV2 builders (ImageNet and V5 comparison initialization), 128-dimensional embedding, masked BCE, input preprocessing, augmentation, BatchNorm freezing, existing optimizer/LR/epoch choices; the three ONNX specialist ensemble interfaces, letterbox/coordinate/NMS/aggregation logic; ROI matching/cropping/classes and 64-dimensional embedding; trainable fusion layers, modality dropout and stable masked attention; grouped outer cross-fitting and grouped inner selection; Platt calibration, thresholds, quality/OOD/reliability functions, bounded verification, immutable/separate dermatologist review contracts, localization and most thesis metrics/plots are retained.

Verified feature contract: `[Acne, Hyperpigmentation, Eczema]`; ROI `[Acne, Hyperpigmentation, Eczema, Background]`; global embedding 128; detector evidence 3×7=21; ROI representation 64+4+15=83, max 12. Normal remains a dataset category and derived no-target-condition outcome.

## Corrections and improvements

- **Output isolation/control:** run-only writing, existing annotation/split/key read-only, unique validated run IDs, one-use researcher action tokens. Setup tested in isolation; no real training or final evaluation executed. Does not change model design.
- **Global phase selection:** original current loop saved Phase B automatically. Astra compares validation-best A/B and restores the selected full checkpoint; B wins ties. Tested A/B/tie/missing file/reload. Changes which checkpoint can represent an experiment, as required.
- **ROI phase selection:** the current original already restored A/B correctly. Astra retains its tie rule and uses the shared full-checkpoint selector; this is not reported as an unfixed original bug.
- **Recovery:** the original helper existed but training functions did not call it. Astra connects it to global/ROI and per-fold fusion; hashes config/data/annotations/splits/source/model contracts; retains optimizer and controller state at committed epochs. It saves the optimizer from the actual selected epoch, avoiding best weights paired with the last epoch's optimizer. Completed jobs reuse; only incomplete fusion jobs restart. Synthetic recovery and mismatch tests passed. No exact stochastic continuation claim.
- **Memory:** requested batches 8/8/2, 4096 MB GPU cap, bounded pipeline concurrency/prefetch, streaming ROI crops. Changes batch-size experiment settings explicitly; architecture unchanged. Full epochs remain untested.
- **Model choice:** B/C four-way classifiers remain comparison experiments; only three-logit multilabel D/D2 are eligible as the selected hybrid global model. This is an explicit scientific selection-policy correction required by independent condition outputs.
- **Frozen split/labels:** no new split creation; regenerated proxy group IDs must match the frozen manifest. Normal is retained. Accepted image reviews require reviewer ID/timestamp; evaluation requires recorded blinding in addition to the original role/status checks. This tightens eligibility, not clinical evidence. Source policy remains fail-closed. Missing fields do not become labels or permissions.
- **Evidence caches:** run identity prevents stale cross-run reuse; ROI keys include model hash and tiers. Detector JSONL repair discards a truncated tail before append. Baseline/test detector inference is deferred until explicit test action. Detector proposals never become reference boxes.
- **Evaluation:** Normal/abstention reporting, actual final Stage 2 probabilities (rather than full-tier scores unrelated to the stopped pass), paired accepted-case corrections/errors, ROI validation outputs, runtime scope and error rows. No clinical metric was calculated on the research dataset.
- **Calibration fallback:** no valid reliability band means no supported decision; the original 0.5 fallback could still be satisfied at asymmetric thresholds. Missing class calibration is explicit. The fallback is not a tuned threshold.
- **Export:** concrete `tf.function` conversion supports the installed Keras 3/tf2onnx path; every fusion seed is exported instead of just the first; JSON weight names remain unique; Keras feature models and run/feature contracts are saved. Five synthetic float32 component exports passed ONNX parity. Real trained mixed-precision parity and native C# fallback remain pending.

## Architecture discrepancies resolved in documentation

The actual Stage 1 decision is global-plus-detector learned fusion, with global-only disagreement evidence. Full ROI fusion is integrated in Stage 2, with up to three meaningful ROI evidence views. The live reference eagerly computes ROI crops/features before its Stage 1 branch. Thus Stage 2 adds evidence to the decision, but does not capture new photographs and does not imply all ROI compute is deferred. This implementation was preserved, rather than silently replacing it with a different stage design. Three Stage 2 views are counted separately from initial Stage 1.

Final fusion models use a grouped inner validation holdout; they are not fitted on every fusion row. Historical V5/YOLO exposure persists even though new V6 optimizers respect frozen groups. Subject identity remains unknown; proxy groups are not patient IDs.

## Verification limits

Read the technical validation report for actual counts. Static compilation, synthetic weight updates and inference/export checks do not establish real-data training success, full-epoch safety, clinical validity, independent generalization or WPF deployment. No frozen split, curated image, original model or original annotation was altered. Dataset/source owners and reviewers must resolve real-data prerequisites.
'''
write('CODEX_V6_VS_ASTRA_PIPELINE_COMPARISON.md',comparison)
write('ASTRA_TRAINING_PIPELINE_CHANGELOG.md','''# Astra training pipeline changelog

2026-11-01 (Asia/Manila): first independent Astra delivery.

1. Snapshotted hashes of the pre-existing working files, preserving uncommitted changes.
2. Derived independent source copies and a standalone notebook; no original regeneration.
3. Added researcher-controlled phase cells and run-only output paths.
4. Connected fingerprinted recovery; corrected global phase choice and selected-epoch optimizer pairing.
5. Added fusion fold/seed reuse and explicit incomplete-fold restart.
6. Streamed ROI crops and applied requested conservative batch/memory defaults.
7. Tightened annotation record eligibility and preserved frozen/source-use safeguards.
8. Added Normal/paired-stage/development reporting and complete ensemble export contracts.
9. Validated actual setup, CPU/GPU synthetic mechanics, recovery, masks, grouping, ONNX interfaces and synthetic exports.
10. Generated the exact-cell Jupyter guide, per-source hash/diff report, readiness/validation and original-integrity evidence.

See CODEX_V6_VS_ASTRA_PIPELINE_COMPARISON.md for scientific effects and limitations. No research training, real calibration, locked final evaluation or WPF change occurred.
''')
failed_exports=[x['component'] for x in exports['components'] if not x.get('parity_pass')]
report=f'''# Astra technical validation report

Recorded during engineering preparation on 2026-11-01 (Asia/Manila). All training tests used synthetic tensors. No research-data model fitting, calibration or final-test metrics were executed.

- Notebook static checks: **{static['passed']}/{static['tests']} passed**, {len(static['failures'])} failures, {len(static['errors'])} errors. Schema, all code-cell syntax, cell IDs/order, action guards, source parity and protected-original hashes checked. {len(cells)} total cells.
- Synthetic regression suite: **{contracts['passed']}/{contracts['run']} passed**, {len(contracts['failures'])} failures, {len(contracts['errors'])} errors, {len(contracts['skipped'])} skips. See `astra_tests/contracts_results.json`.
- Retained detector/ROI/stage/review logic suite on synthetic fixtures: **{inherited['passed']}/{inherited['run']} passed**, {len(inherited['failures'])} failures, {len(inherited['errors'])} errors, {len(inherited['skipped'])} skip. The skipped end-to-end case requires trained research components and an evaluated test split. Its split check uses synthetic fixtures and is not presented as a new real-dataset independence audit. See `inherited_results.json`.
- Existing V5 initialization: **{v5['status']}**. Read-only loading, 128-feature embedding, four-way comparison output and a synthetic in-memory head update verified; original hash unchanged = {v5['original_unchanged']}.
- CPU actual global/ROI/fusion synthetic training: **{cpu['passed']} passed, {cpu['failed']} failed**; CNN batch {cpu['cnn_batch_size']}.
- RTX 4060 actual global/ROI/fusion synthetic training: **{gpu['passed']} passed, {gpu['failed']} failed**; CNN batch {gpu['cnn_batch_size']}, fusion batch 2, mixed-float16 CNNs/float32 fusion, 4096 MB cap. Tracked allocator peak: {gpu.get('gpu_memory',{}).get('peak','unavailable')} bytes. This is not total device memory or full-epoch safety.
- Actual setup cells: **{setup['status']}**, {len(setup['cells'])} code cells executed with run output redirected to a synthetic workspace, stopping before dataset processing.
- Original specialist ONNX interfaces: **{len(env['detectors'])} inspected**, black-tensor inference finite for {sum(d.get('synthetic_inference_finite',False) for d in env['detectors'])}; model-specific interfaces/metadata are in `environment_inventory.json`. This checks loading/execution, not clinical detection or C# numerical parity.
- Synthetic ONNX component parity: **{len(exports['components'])-len(failed_exports)}/{len(exports['components'])} passed** at absolute tolerance 1e-4 (global classifier, global features, ROI classifier, ROI features, fusion). These models received only bounded synthetic updates and used float32 CPU export validation. Real trained mixed-precision exports remain pending.

The regression suite checks empty/partial/full ROI masks, branch sensitivity, unknown-target masking, all-negative targets, phase A/B/tie/missing selection, saved predictions, optimizer restoration, epoch interruption, completed-fold reuse, incomplete/uncommitted fold restart, contract mismatch/corruption, output traversal/symlinks/protected input refusal, one-use action tokens, Normal-vs-abstention, zero F1, source-policy refusal and ONNX conversion. Branch sensitivity is a mathematical check, not evidence of clinical usefulness. Additional grouping/wrapper tests are named in the saved suite log.

## Environment and unresolved tests

TensorFlow {gpu['tensorflow']}; Keras {gpu['keras']}; {env['nvidia_smi']}. Required notebook runtime imports passed. The WSL environment lacks **nbformat/JupyterLab/Notebook server**; nbformat schema validation ran successfully with Windows Anaconda. `ipykernel` is available. The manual guide provides an explicit user-run browser-server setup; no package or CUDA installation was performed.

Not executed: full real-data notebook run; real Phase A/B epochs; real grouped cross-fitting/calibration; final test; actual dermatologist agreement; final trained-model ONNX/mixed-precision parity; target-device deployment latency; C# integration; clinical or patient-independent generalization. These are pending, not skipped tests counted as successes.

The first attempt to run the copied Bash harness failed on CRLF line endings; the delivered harness uses LF and passed. An initial export attempt could not reuse temporary GPU checkpoint paths across tool invocations; CPU synthetic checkpoints were retained in a separate test directory and the completed export checks used those. These were harness issues, not research training failures. Keras emitted a custom-attention build warning during reload; prediction restoration and synthetic export parity passed, but full trained exports still require their own validation.

Evidence files are under `v6/astra_tests/`: static_results.json, contracts_results.json, synthetic_training_cpu.json, synthetic_training_gpu.json, setup_results.json, environment_inventory.json, synthetic_export_results.json and corresponding logs. Original integrity is documented separately.
'''
write('ASTRA_TECHNICAL_VALIDATION_REPORT.md',report)
write('ASTRA_TRAINING_READINESS.md',f'''# Astra training readiness

**Engineering artifact implemented and bounded-tested; real supervised training remains blocked.**

- Notebook structure/syntax: passed; {len(cells)} cells, unexecuted research outputs.
- CPU/GPU synthetic training: passed for global CNN, ROI CNN and learned fusion.
- Recovery/masking/selection/isolation: see the actual regression results and limits in ASTRA_TECHNICAL_VALIDATION_REPORT.md.
- Real-data prerequisites: blocked. Current frozen catalog {env['frozen_rows']} entries: {env['category_counts']}. Normal remains in train (1,800), validation (300), fusion (450), test (450).
- Image annotations: {env['annotation_rows']} rows, all unverified; {env['accepted_fully_decided']} accepted fully decided rows.
- Region annotations: {env['region_rows']} rows.
- Source-use evidence: {env['source_records']} registry rows, all permissible_use values blank. No permission was fabricated and no broad source research restarted.
- Global training: code complete; real training pending. Needs 300 train/150 validation fully decided eligible images and 40/20 Present and Absent per condition, source-use clearance and documented reviews.
- ROI training: code complete; real training pending. Needs verified matched regions, all four classes, existing per-split region counts, at least 100 matched train proposals and validation references.
- Fusion: code complete; real training pending, including eligible grouped fusion references (minimum 250 fully decided, 30 positive/negative per condition).
- Calibration/reliability rules: implemented; real fitting pending, using fusion OOF data only.
- Final evaluation: locked/pending. Needs eligible test references and adviser-approved scope addressing historical model exposure. No automatic reshuffle/exclusion or independent cohort fabrication.
- ONNX: five synthetic float32 components passed parity; actual trained component export/parity pending.
- WPF: unchanged; integration, native fallback, C# parity, latency and mandatory-review workflow verification pending.
- Browser Jupyter: WSL kernel exists; JupyterLab/Notebook server is missing. Follow the guide's explicit setup or use the existing WSL notebook editor.

The minimum next human work is genuine source-use evidence plus documented train/validation annotation. Region annotation is not required to begin the global stage. Training code will not bypass these prerequisites. Existing historical-exposure findings in `v6/docs/PRETRAINING_READINESS_REPORT.md` remain relevant: V5 and specialist training exposure, unknown subjects and metadata lineage are unresolved. This task rechecked current manifests; it did not rerun the broad historical exposure investigation or claim the frozen test is independently clean.

The preserved Stage 1/Stage 2 implementation and its eager ROI precomputation are documented in the comparison report. Normal means supported absence of the three targets, not a diagnosis of universally healthy skin. Every assessment remains pending dermatologist review.
''')
before=json.loads((docs/'original_integrity_before.json').read_text());after={name:hashlib.sha256((repo/name).read_bytes()).hexdigest() for name in before}
changed=[n for n in before if before[n]!=after[n]]
write('original_integrity_after.json',json.dumps(after,indent=2))
write('ORIGINAL_FILE_INTEGRITY_REPORT.md',f'''# Original file integrity

Compared before/after SHA-256 values for **{len(before)} pre-existing files** in the reference V6 tree and WPF project, including original notebook, all notebook_src files, existing models/metadata, tools, documentation, logs/reports and backups present at snapshot time.

Changed original files: **{len(changed)}**. Details: {changed or 'none'}.

The exact list and hashes are in original_integrity_before.json / original_integrity_after.json. The original working tree already contained uncommitted modifications; those were preserved rather than reset or mistaken for Astra changes.

Read-only WSL inspection separately hashed **{len(env['original_wsl_files_before'])} original annotation/split/V5 artifact files** before/after its check; unchanged = {env['original_wsl_files_unchanged']}. Synthetic model tests also recorded original model hashes unchanged. No operation wrote curated images or WPF code. The complete image collection was not independently hashed byte by byte, so this report does not claim a full image-binary snapshot.

All delivered additions are the Astra notebook, astra_training_src, astra_docs and astra_tests. No real astra_runs research training output was created. Existing V5/Codex V6 checkpoints and logs were never used as destinations for synthetic or Astra outputs.
''')
print('Wrote delivery guide/reports and source diff. Original changes:',changed)
