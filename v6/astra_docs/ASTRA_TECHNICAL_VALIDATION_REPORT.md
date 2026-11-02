# Astra technical validation report

Recorded during engineering preparation on 2026-11-01 (Asia/Manila). All training tests used synthetic tensors. No research-data model fitting, calibration or final-test metrics were executed.

- Notebook static checks: **10/10 passed**, 0 failures, 0 errors. Schema, all code-cell syntax, cell IDs/order, action guards, source parity and protected-original hashes checked. 166 total cells.
- Synthetic regression suite: **24/24 passed**, 0 failures, 0 errors, 0 skips. See `astra_tests/contracts_results.json`.
- Retained detector/ROI/stage/review logic suite on synthetic fixtures: **36/37 passed**, 0 failures, 0 errors, 1 skip. The skipped end-to-end case requires trained research components and an evaluated test split. Its split check uses synthetic fixtures and is not presented as a new real-dataset independence audit. See `inherited_results.json`.
- Existing V5 initialization: **PASS**. Read-only loading, 128-feature embedding, four-way comparison output and a synthetic in-memory head update verified; original hash unchanged = True.
- CPU actual global/ROI/fusion synthetic training: **3 passed, 0 failed**; CNN batch 1.
- RTX 4060 actual global/ROI/fusion synthetic training: **3 passed, 0 failed**; CNN batch 8, fusion batch 2, mixed-float16 CNNs/float32 fusion, 4096 MB cap. Tracked allocator peak: 1010684672 bytes. This is not total device memory or full-epoch safety.
- Actual setup cells: **PASS**, 10 code cells executed with run output redirected to a synthetic workspace, stopping before dataset processing.
- Original specialist ONNX interfaces: **9 inspected**, black-tensor inference finite for 9; model-specific interfaces/metadata are in `environment_inventory.json`. This checks loading/execution, not clinical detection or C# numerical parity.
- Synthetic ONNX component parity: **5/5 passed** at absolute tolerance 1e-4 (global classifier, global features, ROI classifier, ROI features, fusion). These models received only bounded synthetic updates and used float32 CPU export validation. Real trained mixed-precision exports remain pending.

The regression suite checks empty/partial/full ROI masks, branch sensitivity, unknown-target masking, all-negative targets, phase A/B/tie/missing selection, saved predictions, optimizer restoration, epoch interruption, completed-fold reuse, incomplete/uncommitted fold restart, contract mismatch/corruption, output traversal/symlinks/protected input refusal, one-use action tokens, Normal-vs-abstention, zero F1, source-policy refusal and ONNX conversion. Branch sensitivity is a mathematical check, not evidence of clinical usefulness. Additional grouping/wrapper tests are named in the saved suite log.

## Environment and unresolved tests

TensorFlow 2.21.0; Keras 3.15.1; NVIDIA GeForce RTX 4060 Laptop GPU, 8188 MiB, 1795 MiB. Required notebook runtime imports passed. The WSL environment lacks **nbformat/JupyterLab/Notebook server**; nbformat schema validation ran successfully with Windows Anaconda. `ipykernel` is available. The manual guide provides an explicit user-run browser-server setup; no package or CUDA installation was performed.

Not executed: full real-data notebook run; real Phase A/B epochs; real grouped cross-fitting/calibration; final test; actual dermatologist agreement; final trained-model ONNX/mixed-precision parity; target-device deployment latency; C# integration; clinical or patient-independent generalization. These are pending, not skipped tests counted as successes.

The first attempt to run the copied Bash harness failed on CRLF line endings; the delivered harness uses LF and passed. An initial export attempt could not reuse temporary GPU checkpoint paths across tool invocations; CPU synthetic checkpoints were retained in a separate test directory and the completed export checks used those. These were harness issues, not research training failures. Keras emitted a custom-attention build warning during reload; prediction restoration and synthetic export parity passed, but full trained exports still require their own validation.

Evidence files are under `v6/astra_tests/`: static_results.json, contracts_results.json, synthetic_training_cpu.json, synthetic_training_gpu.json, setup_results.json, environment_inventory.json, synthetic_export_results.json and corresponding logs. Original integrity is documented separately.
