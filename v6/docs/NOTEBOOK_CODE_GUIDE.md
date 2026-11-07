# LUMYVUE V6 final notebook: code guide

Written from the notebook's code structure, comments and saved outputs. The notebook is `LUMYVUE_V6_Final_Model_Complete.ipynb`. Numbers in brackets such as [cell 199] are notebook cell indices (0-based).

## The big picture in one paragraph

An image goes through three streams of evidence. (1) A **global CNN** (MobileNetV2) looks at the whole face. (2) **Three YOLOv8 detectors** (acne, hyperpigmentation, eczema) draw boxes. (3) An optional **ROI CNN** classifies the boxed crops. A small **fusion network** combines the evidence into one probability per condition. **Platt calibration** makes the probabilities honest, and **thresholds** (0.42 / 0.37 / 0.27) turn them into yes/no. A **trust layer** (quality, out-of-distribution, conflicts, stage rules) can overrule with "Needs review". The deployed model uses Global + YOLO only. The full model with the ROI stream was not selected.

```
photo -> Global MobileNetV2 --------\
      -> 3x YOLOv8 (boxes -> evidence)--> Fusion NN -> Platt -> thresholds -> trust rules -> result
      -> (ROI crops -> ROI MobileNetV2)--/                                    (+ "pending dermatologist")
```

## Part by part

### Setup and safety, cells 1-21 (Parts I-II start)
| Cells | Code | What it does |
|---|---|---|
| 1-4 | bootstrap, package check, `CFG` config, imports | Defines every setting in one `CFG` object, sets random seeds (42), configures the GPU. `require()` and `GateError` stop the run when a condition is not met (fail closed). |
| 5 | `safe_path`, `write_text`, `sha256_file`, redaction | Writes only inside the experiment folder, and refuses to touch protected original data. Hashes files so changes are detectable. |
| 6-7 | preflight, `draw_architecture` | Checks which inputs exist, and draws the architecture diagram. |
| 8-10 | `atomic_record`, `recovery_fit`, `astra_require_action` | Checkpoint and resume. `astra_require_action` is a one-use token: **Run All cannot start an expensive training by accident**. |

### Data, cells 11-37
| Cells | Code | What it does |
|---|---|---|
| 11-13 | load manifest, inventory, `_check_one` | Lists the images, counts classes, and checks each is a readable 224x224 RGB image (not blank). |
| 14-15 | `dhash64`, `UnionFind`, `build_groups`, `assign_splits` | **Leakage control.** Exact duplicates (MD5) and near-duplicates (perceptual hash) are merged into groups. Whole groups go to train, validation, fusion or test, so the same face never sits on both sides. |
| 16 | `source_issues`, `require_source_permission` | Refuses to train on data without a recorded permission or provenance. |
| 18-19 | `load_annotations`, `build_train_labels` | Labels are Present=1, Absent=0, **Unknown=-1**. Unknown is masked out of the loss and never used for evaluation. |
| 21-22 | `load_regions`, `annotation_helper` | Loads box annotations, with a widget for reviewers to add them. |
| 23-25 | `preprocess_global`, quality metrics, `static_review_reasons` | The exact preprocessing the C# app uses (BGR->RGB, resize, (p/127.5)-1), blur and exposure checks, and a domain-shift audit against real app captures. |

### V5 baseline, cells 26-34 (Part III)
Runs the deployed V5 ONNX model unchanged (`v5_predict_bgr`), checks Python against the C# app at three levels (cells 26-28), and analyses where V5 errs (cells 30-32). `mine_hard_examples` finds hard training images (train split only), and `build_review_queue` makes the annotation to-do list.

### Global CNN, cells 35-45 (Part IV)
| Code | What it does |
|---|---|
| `cond_metrics`, `cluster_bootstrap` [35] | Shared metrics (precision, recall, F1, AP, AUC). The bootstrap resamples **leakage groups**, not images, which gives honest confidence intervals. |
| `make_dataset`, `augment_batch` [36] | tf.data pipeline with skin-safe augmentation. |
| `MaskedBCE`, `build_global` [37] | Multilabel loss that ignores Unknown labels, and the MobileNetV2 with a 3-output head. |
| `EXPERIMENTS` D vs E [38] | **D** starts from ImageNet weights. **E** starts from V5's backbone. |
| `ValMetrics`, `run_phase` [39] | Training with validation callbacks, checkpoints and resume. Phase A trains the head, and Phase B fine-tunes. |
| `transfer_v5_backbone` [40] | Copies V5's backbone and verifies the copy. |
| cells 42-43 | The actual training calls, each guarded by `astra_require_action`. |
| `v5_exposure_flags` [45] | Splits validation images by whether V5 had seen them, so the D vs E comparison is not biased. **E was selected.** |

### YOLO detectors, cells 46-54 (Part V)
`discover_detectors` finds the models. `detect_condition` reproduces the C# pipeline: letterbox, decode, per-class NMS and ensemble fusion. `run_yolo_cache` runs the detectors once and caches the results (about 1 second per image on CPU). Cell 49 turns boxes into **evidence** per image and condition: detected or not, count, max confidence, area, and `available=0` if the detector failed. Cells 50-52 compute duplicates and nesting, false-positive rates, and **localisation precision, recall and F1** against verified boxes.

### ROI CNN, cells 55-62 (Part VI)
`square_crop_box` and `build_roi_candidates` crop boxes with context. `label_proposal` labels each crop (acne, hyperpigmentation, eczema or background false positive). `build_roi_model` is a 4-class MobileNetV2, and cell 62 aggregates its outputs into features for fusion.

### Fusion, cells 63-69 (Part VII)
| Code | What it does |
|---|---|
| `assemble` [63] | Builds the feature vector: global logits, detector evidence, quality, ROI features. |
| `build_fusion`, `ModalityDropout`, `MaskedAttentionPool` [64] | A small NN. Modality dropout randomly hides a stream during training so the model copes when one is missing. |
| `crossfit_fusion` [65] | **Grouped 5-fold cross-fitting**: every image is scored by a model that never saw its group (out-of-fold). This is the honest basis for calibration and thresholds. |
| cell 66 | Trains **7 variants** (full, global_yolo, global_only, roi_only and three ablations). |
| `fit_platt`, `brier_ece` [69] | Platt calibration, and calibration quality (Brier score, ECE). |

### Trust layer, cells 70-78 (Part VIII)
`FinalAssessment` is one result object, and **every** output is marked *pending dermatologist review*. `validate_input` and `assess_quality` never turn a bad photo into "Normal". `OODModel` uses Mahalanobis distance to the training embedding to flag unfamiliar images. `stage1_flags` flags conflicts, near-threshold cases and quality or OOD problems. `verification_cycle` is a bounded second look. `decide` is a pure function that gives per-condition decisions plus one overall state. `assess_image` is the single-image path used at inference, and it verifies SHA-256 hashes of the models when loading.

### Evaluation, cells 79-97 (Part IX)
Cell 79 contains `open_test_once`, a **lock file so the test split can only be opened one time**. In this run it was **never opened**, because there were no expert labels. That is why cells 85-91 print notes instead of results. The remaining cells produce the development-set evaluation, ablations, robustness (`perturb`), Grad-CAM++ explanations, learning curves, ROC/PR curves, calibration plots and galleries.

### Export and tests, cells 98-111 (Part X)
`to_onnx` and `load_onnx_verified` export the models, and the Keras-vs-ONNX check compares outputs. `build_deployment_metadata` writes the config the C# app reads. `log_assessment` writes a hash-chained audit log. Cell 104 holds the unit tests (35 passed, 2 skipped by design). Cells 108-111 are the **post-hoc analyses** that produced the fair-set comparison, the eczema diagnostic, the ONNX gap check and the `no_eczema_evidence` candidate.

### Run 02 evidence, cells 112-125 (Part XI)
This part **only reads saved CSV and JSON files** and shows them. The key results are in 122-125: the fair-set table, the pre-registered criterion check, the eczema diagnostic and ONNX faithfulness.

### Deployed model, cells 126-132 (Part XII)
Cell 127 checks the installed model hash against the recorded one. Cell 128 shows each variant's thresholds. Cell 129 repeats the fair-set comparison for the deployed variant. Cell 130 shows the C#-vs-Python parity evidence. Cell 131 is a Python copy of the app's reading rules (`read_photo`, `certainty`). In it, **hyperpigmentation is detector-led**: a detector mark makes it "Detected". Cells 132-133 apply those rules to 13 reference photos.

## Terms that keep coming up
- **Out-of-fold (OOF):** a prediction from a model that never trained on that image.
- **Platt calibration:** a sigmoid fitted to turn raw scores into probabilities that match real frequencies.
- **Masked loss:** Unknown labels contribute zero to the loss, so the model doesn't learn from guesses.
- **Leakage group:** images that are duplicates or near-duplicates, which are always kept in the same split.
- **Pre-registered criterion:** the pass/fail rule fixed before looking at the results (it is in `v6/THESIS_TRACKER.md`).
- **astra_*:** the "researcher-initiated" guards and entry points for the expensive steps.

## What this guide did not verify
I read the structure, comments and saved outputs. I did not re-run any cell or audit the code line by line for bugs.
