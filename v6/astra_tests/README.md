# Astra isolated validation

No script here runs research-dataset training, calibration or final-test evaluation. Synthetic tensor/checkpoint artifacts are labeled and kept separate from `astra_runs`.

From the repository root, with the existing WSL Python environment:

```bash
ASTRA_CUDA_DEVICE=-1 bash v6/astra_tests/run_bounded.sh test_contracts.py
ASTRA_CUDA_DEVICE=-1 bash v6/astra_tests/run_bounded.sh run_inherited_suite.py
LUMYVUE_SYNTHETIC_BATCH=8 bash v6/astra_tests/run_bounded.sh
ASTRA_CUDA_DEVICE=-1 LUMYVUE_SYNTHETIC_BATCH=1 bash v6/astra_tests/run_bounded.sh
ASTRA_CUDA_DEVICE=-1 bash v6/astra_tests/run_bounded.sh export_synthetic.py
bash v6/astra_tests/run_bounded.sh check_setup.py
ASTRA_CUDA_DEVICE=-1 bash v6/astra_tests/run_bounded.sh check_v5_initialization.py
```

Run model tests sequentially to limit memory pressure. The wrapper imposes a 420-second timeout, process-local CUDA library discovery and one CPU thread per relevant library. It installs nothing. Export checks consume the CPU synthetic checkpoint report; they do not use original V5 checkpoints as output destinations.

`inspect_environment.py` reads manifest/annotation metadata and uses black synthetic tensors to inspect the nine installed ONNX models. It never uses patient images for inference. `check_setup.py` executes actual setup cells only, redirecting artifacts into a synthetic workspace before configuration. `run_inherited_suite.py` AST-loads definitions and explicitly selected constants; it does not execute notebook stages. Its split-disjointness case uses synthetic fixtures, not an independent real-dataset audit.

Windows Anaconda currently provides nbformat for:

```powershell
& C:\Users\Bautista\anaconda3\python.exe -X utf8 v6/astra_tests/static_validation.py
```

`write_delivery_docs.py` regenerates exact-cell documentation, comparison diffs and integrity reports from saved evidence. It does not run tests or training. Do not confuse old reports with freshly executed results; inspect timestamps/logs when rerunning.
