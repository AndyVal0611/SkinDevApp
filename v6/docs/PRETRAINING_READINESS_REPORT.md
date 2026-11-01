# Pre-training readiness report

*Prepared before any V6 training. Nothing below is an experimental result of V6. Items marked **MEASURED (audit code, real files)** come from the notebook's dataset/parity audit cells run in a throw-away scratch workspace; **READ** items come from existing project files; everything else is a design statement.*

## 1. Verdict
| Stage | Ready? | What is needed |
|---|---|---|
| Parts I–III (environment, dataset audit, frozen split, templates, V5 parity & baseline error analysis, review queue) | **Yes — run now** | nothing |
| Annotation | **Required human work** | verified multilabel annotations (≥ the gate minimums), region boxes, provenance — see §5 |
| Part IV global-CNN experiments B, C, D, D2 | **Blocked until annotations exist** | `annotations_train`, `annotations_validation`, `provenance_defined` gates |
| Part V YOLO inference/diagnostics | **All three detectors load and run** (Eczema from the app's `Model/` folder) | see §4 |
| Parts VI–IX (ROI, fusion, stages, evaluation) | **Blocked** behind annotations, region labels and trained components | gates print the exact shortfall |
| Export / tests / report | Tests run now; export waits for trained models | — |

## 2. Sources inspected
| Requested source | Status |
|---|---|
| `Pipeline V5 - With Facial Eczema(2).ipynb` | **not found under that name**; the V5.2.1 notebook `Pipeline  V5 - With Facial Eczema.ipynb` (Downloads and `Training Essentials (AI models)/`, with saved outputs) was read cell by cell (config, data policy, augmentation, architecture, phases A/B, callbacks, evaluation, error analysis, Grad-CAM++, ONNX export). A `…With_Auxiliary_Eczema` variant also exists and was **not** analysed |
| `PrecisionSkin_Acne_Training_and_Results(.ipynb / _WITH_OUTPUTS)` | READ: configuration (`TRAIN_ARGS`, seeds, dataset paths), recorded-args comparison cell, the installed-model JSON, the acne verdict and diagnosis/plan documents. The 8 MB `WITH_OUTPUTS` file was searched for configuration, **not** read output by output |
| `PrecisionSkin_Hyperpigmentation_Training_and_Results(.ipynb / _WITH_OUTPUTS)` | READ: configuration cell, installed-model JSON, `HYPERPIGMENTATION_FULL_RESULTS.md`, annotation rule v2. Same caveat for the 9.7 MB outputs file. **Discrepancy found:** test mAP@0.5 appears as 0.341 (full-results document, installed 3-model average, 100 rule-v2 photos) and 0.508 (installed JSON note, "single run"); not reconciled — re-run the scorer before quoting |
| Eczema YOLOv8 training notebook / weights | **training notebook not available** (`eczema/notebooks/` holds data-preparation notebooks only). **Weights/ONNX now installed in the app's `Model/` folder** (2026-11-01): see §4 |
| Existing artifacts | V5 logs (`seed_results`, `calibration_report`, `onnx_keras_consistency`, `deployment_summary`, error analysis with leakage flag), master/split manifests, C# `OnnxInference.cs`, `LesionDetector.cs`, `ScanSettings.cs`, `ScanPipeline.cs`, `CaptureArchive.cs`, csproj, `install_specialist_detector.py` |

## 3. Baseline facts used by the design (READ)
* **V5 reported:** accuracy 0.929 [0.916, 0.941], macro-F1 0.917 [0.902, 0.931] (5 seeds, bootstrap), validation ECE 0.0118, Keras-vs-ONNX max |Δp| 0.0052 (argmax agreement 0.9994), deploy-path vs training-path agreement 0.9919 on the V5 test set. These are single-label results on a split with sibling leakage (below).
* **Deployment target:** Windows 11 mini-PC, Intel i5 6th gen, 8 GB RAM, **CPU inference**, Logitech Brio. The V6 pipeline (three detector ensembles + ROI CNN + fusion) is far heavier than V5; the acne verdict measured ≈ 291 ms per 800 px frame for the acne ensemble alone on a laptop CPU. **Latency on the mini-PC is unmeasured and may force ROI/ensemble limits.**
* **V5 preprocessing parity (MEASURED, real files):** static contract of `OnnxInference.cs` matches; for 60 saved application captures (record + working frame) Python ONNX Runtime reproduces the C# app's stored probabilities to **≤ 0.0005 percentage points** (the record's rounding), top class identical in 60/60, model hash identical in 60/60. **Not established by this check:** camera acquisition, face handling, stabiliser, and identical-tensor parity for V6.

## 4. Eczema detector status (updated after the files were located)
* **Location:** `lesion_detector_eczema.onnx`, `_m2.onnx`, `_m3.onnx` (12 773 204 bytes each) and `lesion_detector_eczema.json` are installed in `SkinDevApp/Model/`, listed in the csproj (entries added 2026-11-01) and copied to `bin/Debug/Model` by a Debug build (exit 0, no errors).
* **Loadability and compatibility with V6 (verified with ONNX Runtime and the notebook's engine):** 3 distinct ONNX models (three different SHA-256 hashes; the JSON hash matches the primary file); opset 12; input `images [1,3,640,640]`; output `output0 [1,5,8400]` = `cx,cy,w,h` + one class score (no objectness) — the layout the notebook decodes; embedded names `{0: eczema}` = JSON classes `["eczema"]` → class index 0; NMS IoU 0.6; deployed threshold 0.25 (JSON `default_conf` = app `ScanSettings`). The notebook registry reports `available = True`, 3 members, imgsz 640, location "outside the WPF Model folder". On a 60-image *unlabelled* preview the engine ran without error (detections on 28 images; **not a performance figure**).
* **Differences to be aware of:** (a) imgsz 640 and YOLOv8n versus 800 / YOLOv8s for acne and hyperpigmentation; (b) the JSON note describes weighted box fusion at IoU **0.55**, the C# app fuses every ensemble at **0.5** — the notebook keeps the deployed 0.5 (override `ECZEMA_FUSION_IOU`) and does not change it silently; (c) its recorded metrics are heterogeneous and small (mAP@0.5 0.52 on 39 "fresh" photos, 0.12 on `r3_test_50`, 0.181 on `dev2_39`), from one annotator (same-annotator agreement F1 0.38 at IoU 0.5) — **not validated**; (d) a 6-model run (NA23) is mentioned but not installed.
* **Also found while checking:** the hyperpigmentation JSON's `onnx_sha256` does **not** match the installed primary ONNX file (acne's does). The detector loads and works; reconcile the JSON before quoting its metrics.
* **Still missing for reproducibility:** the Eczema training notebook, dataset fingerprint and splits, threshold-selection record, evaluation protocol, and verified eczema boxes for ROI training and localisation metrics.
* **Review contract:** every AI assessment must be reviewed by a dermatologist; see `REVIEW_CONTRACT.md` for the sealed-AI / separate-findings design and the WPF gaps.

## 5. Annotations still required (counts are configurable, provisional, power-based choices)
| Item | Minimum per the gates | Notes |
|---|---|---|
| Verified fully-labelled photographs | train 300 · validation 150 · fusion 250 · test 250 | any prefix of the `review_rank`-ordered template is a random sample; do validation/fusion/test first |
| Verified *Present* per condition | 40 · 20 · 30 · 30 | Eczema is scarce in the V6 split (see §6): check feasibility early |
| Verified *Absent* per condition | 40 · 20 · 30 · 30 | needed so negatives are real, not assumed |
| Verified regions per class (acne, hyperpigmentation, eczema, background_false_positive) | 60 · 20 · 20 · 20 | `exhaustive = yes` marks complete box sets |
| Dataset provenance | one row per source key (12 found) | blocks training until `permissible_use` is filled |
| Optional | camera-capture annotations (`image_origin = camera_capture`), double-read 10 % for κ, skin-tone codes, OOD reference images | strengthen Chapter IV |

## 6. Dataset findings (MEASURED, audit code on the real manifest and images)
* 12 000 manifest rows → **10 367** after excluding non-facial auxiliary training images → **10 063** after excluding `reject`/`manual_review` facial-QA rows → **9 789** after dropping 274 exact duplicates (all Eczema). Processed images: 10 063/10 063 present, readable, 224×224; **all 10 063 originals found** in `Downloads/PrecisionSkin/Classes`.
* **Leakage groups:** 6 515 groups; 1 665 groups hold more than one image (5 213 images); largest group 18; 1 group spans two source classes. **885 groups cross V5 splits; 696 of 1 556 V5 test images (45 %) share a group with another split** (Hyperpigmentation 296, Normal 249, Acne 119, Eczema 32) — the V5 test result is optimistic. No subject/session ids exist.
* **V6 frozen split (train / validation / fusion / test):** Acne 1800/300/450/450, Hyperpigmentation 1800/300/450/450, Normal 1800/300/450/450, **Eczema 473/79/119/118** — the verified eczema counts are bounded by these; large eczema annotation counts need either more eczema images or relaxed gates.
* **Image-type confound** (inventory): Normal is mostly partial/full faces; disease classes are mostly lesion-region crops and close-ups. **Domain shift (quality audit, medians):** application captures show Laplacian variance 32–125 vs 356 for dataset images, skin fraction 0.34–0.35 (camera) vs 0.78 (dataset), and 19/60 captures without a detected face, 25/60 uneven lighting, 15/60 blurry.
* **Label-noise/multi-condition hints (review queue seeds):** 418 Acne-folder files whose names mention pigmentation, 386 disease-class full-face images (possible co-existing conditions), 90 Normal images that are lesion-region crops, 24 Acne files naming eczema/dermatitis, 5 images in groups with conflicting class labels. **These are reasons to look, not labels.**

## 7. Known limitations that remain after delivery
Group proxies cannot prove patient independence; reference-standard quality depends on reviewers; provisional quality/OOD/stage parameters; Eczema detector status; ROI crops from 224×224 images if originals are absent; no C# parity for V6 components; mini-PC latency unknown; hyperpigmentation detector metric discrepancy; WITH_OUTPUTS notebooks not exhaustively read; the dry-run machine's TensorFlow saw **no GPU** (CUDA library path of a non-login shell) — verify GPU visibility in your normal kernel.
