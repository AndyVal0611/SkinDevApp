# Notebook validation report

Notebook: `LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training.ipynb` — 137 cells (85 code, 52 Markdown), **no stored outputs**, nbformat 4 (validated), built from `notebook_src/*.cellsrc` by `tools/build_notebook.py`.

**Nothing was trained and nothing was evaluated.** The checks below are static analysis, a *gated dry run* (every code cell executed once in a throw-away scratch workspace while all supervised stages were blocked by their gates), and *forward-only* smoke checks (no `fit`, no `train_on_batch`).

## 1. Static checks (`tools/validate_notebook.py`, `tools/check_globals.py`)
| Check | Result |
|---|---|
| JSON structure / `nbformat.validate` | passed |
| Python syntax of every code cell (`ast.parse`) | passed (0 errors) |
| Undefined names in **execution order** (pyflakes over the concatenated cells; module-level order, function bodies resolved at call time) | **0 undefined names** |
| Key definitions appear in a valid order (`CFG`, `safe_path`, `DF`, groups, split, annotation loader, preprocessing, V5 session, hard mining, loss, `run_phase`, detectors, ROI, fusion, `verification_cycle`, `decide`, `assess_image`, test lock, metadata, tests) | passed |
| Section coverage (all 56 requested section topics found by keyword) | passed |
| No `TODO` / `NotImplementedError` / pseudocode / bare `except: pass` | passed |
| No hard-coded machine paths (`/home/<user>`, `C:\Users\…`) in code | passed (all paths come from `CFG`; defaults are discovered from `~` and `/mnt/c/Users/*`) |
| Writes go through guarded helpers (`safe_path`, `write_*`, `export_path`) | passed; 12 *warnings* reviewed: helper internals, the pseudonymisation key (intentionally outside the experiment folders), the ROI-cache purge (inside the workspace), and the unit tests' own temp files |
| Markdown contains no V6 result claims | 1 flagged phrase ("improves accuracy") is the *negated* disclaimer in the header |
| Cross-cell clobbering of globals read by functions (`check_globals.py`) | 8 names reviewed, all benign (`DF`, `HARD_USABLE`, `TEST`, `yt`, …); the check was added after the dry run exposed a real clobbering bug (below) |
| Stored outputs / execution counts | none |

## 2. Gated dry run (`tools/dry_run.py`)
Environment: the project's `.venv` (Python 3.13.15, TensorFlow 2.21.0, Keras 3.15.1, ONNX Runtime 1.30.0), scratch workspace under `/tmp`, **read-only** access to the real manifest, processed images, originals, V5 models, YOLO models and application captures. Result: **all 85 code cells executed without an uncaught exception** in the no-annotation state, in about 9 minutes the first time and 2.5 minutes with caches.

What the dry run proved (functionally, not scientifically):
* preflight finds the inputs; originals fingerprinted (13 files) and verified **unchanged** at the end; hash-chained audit log intact; all writes stayed in the scratch workspace;
* dataset policy, integrity checks (10 063 images readable, all 224×224), leakage groups, frozen split with hash check **and resume**, provenance and annotation templates (existing files are not overwritten), annotation validation, per-split gates (all supervised gates **closed with the exact shortfall**);
* parity audit on 60 real application captures (Python vs the C# app's stored V5 probabilities: ≤ 0.0005 percentage points), V5 inference on the dataset, V5 error pairs and galleries, hard-example candidates, review queue;
* **YOLO engine on the real ONNX files** (Acne and Hyperpigmentation ensembles, 6 members, 60-image preview): no errors; two runs bit-identical; Eczema correctly `detector_unavailable` (no ONNX registered); letterbox round-trip assertion; duplicate/nested analysis ran;
* every guarded stage (global experiments, ROI tables, ROI training, fusion, OOD, Stage rules, test evaluation, latency, Grad-CAM, export) printed a `BLOCKED/SKIPPED` reason instead of creating data; the test split was **not** opened (no lock file);
* deployment metadata, Chapter IV report skeleton (`NOT RUN` entries only) and final summary were generated.

## 3. Unit and integration tests (inside the notebook, Section 54)
27 tests ran: **26 passed, 1 skipped** (needs trained components — skipped, not "passed"), 0 failed. Covered: invalid images; RGB/BGR and normalisation; shape and label-map contracts; missing, corrupted and hash-mismatched model files; empty/duplicate/nested/overlapping boxes; missing detector vs empty result; detector runtime failure not swallowed; decoding/letterbox coordinates; ROI coordinate errors and square crops; ROI caps; fusion shape mismatch and masked attention; calibration failure; *Unknown ≠ Absent*; every final state incl. RECAPTURE / OUT_OF_SCOPE / INFERENCE_ERROR; technical failure never Normal; conflicting outputs; bounded verification, no-new-evidence stop, time budget, recapture, no confidence inflation; deterministic replay; raw predictions preserved; split leakage; write-protection of originals; pseudonyms/redaction; audit-chain tamper detection.

## 4. Forward-only smoke checks (`tools/smoke_forward.py`) — 12/12 passed
Global multilabel build + masked loss (a masked label cannot change the loss) + embedding model · V5 Keras (loaded read-only) vs V5 ONNX on 6 real images (max |Δp| 9.5e-4) · tf.data pipelines (global, ROI; augmentation leaves colour statistics essentially unchanged) · ROI crop → dataset → ROI model · Grad-CAM++ on a V5-derived model · metric/calibration/bootstrap numerics on **synthetic** arrays · batch Stage logic on real preview rows with **synthetic** logits · tf2onnx export of a tiny model and of a fusion model (ONNX Runtime agrees to 1e-7) · YOLO determinism/availability semantics · result-object round trip · **serialisation round-trips** (compiled model with the custom loss and optimizer, weights-only `.h5`, fusion `.keras` with custom layers, ROI feature model) · validation-metric callback object.

## 5. Defects found by this validation and fixed
| Defect | How it was found | Fix |
|---|---|---|
| A module-level loop variable `S` overwrote the image-size constant `S`, breaking every model builder | dry run / smoke test | renamed to `IMG_PX`; added `check_globals.py` |
| `keras.saving` missing under `tensorflow.keras` in TF 2.21 / Keras 3.15 | dry run | use standalone `import keras` |
| Resuming the notebook raised "images not in the frozen split" because exact duplicates are (correctly) absent from the split | dry run (second invocation) | compare against non-duplicate images only |
| Grad-CAM++ produced an all-zero map on a softmax model (saturated probability; float16 gradients) | smoke test | head recomputed in float32 on the pre-softmax score; guarded gallery |
| Two wrong test expectations (verification stop reason; redaction text) | unit tests | tests corrected, behaviour unchanged |
| Optimizer state was reset on resume; test lock was taken before the last label-free computation | code review | resume keeps the loaded compiled model; lock is now taken immediately before the first metric |
| Per-file `stat()` over `/mnt/c` made inventory take minutes | dry run | one directory listing per folder |

## 6. Not verified (be explicit before you start)
* **Training-time behaviour** — `model.fit` with the real callbacks (`ValMetrics`, checkpoints, early stopping, LR plateau, resume mid-phase), the ROI trainer, and the cross-fitted fusion trainer have **never been executed**; only construction, forward passes, loss values, serialisation and dataset pipelines were. Expect to watch the first epochs.
* **GPU/mixed-precision/VRAM behaviour** — the dry run's TensorFlow saw **no GPU** (non-login shell); batch-size and memory choices for the RTX 4060 are reasoned, not measured.
* **Scientific validity** — thresholds, bands and quality heuristics are provisional; no V6 metric exists.
* **Real annotated data paths** — annotation loading with a *populated* file, ROI-label matching, localisation mAP, and every test-split computation were exercised only on synthetic fixtures (numerics) or not at all.
* **Eczema** — no real Eczema ONNX was available; the introspection/registration code path for one is untested on a real specialist file.
* **C# parity** of any V6 component; mini-PC CPU latency.
* **Interactive pieces** — the `ipywidgets` annotation helper (ipywidgets not installed in the inspected environment).
