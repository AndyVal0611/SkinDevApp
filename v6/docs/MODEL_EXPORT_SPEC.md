# Model export specification (V6 components)

> Status: **specification only.** Nothing has been exported, trained or deployed. Exports are written by Section 50 to `v6/experiments/<RUN_ID>/export/v6_*` and **never** into the WPF `Model/` folder or any V5/YOLO location (guarded by `export_path()`). The authoritative values for *your* run are in `export/v6_model_metadata.json` and `export/v6_keras_vs_onnx_verification.json`.

## 1. Global feature model — `v6_global_features_mobilenetv2.onnx`
| Item | Specification |
|---|---|
| Input | one tensor, float32 NHWC `[N,224,224,3]` (Keras input name `image_nhwc_m1p1` for D experiments; V5-derived B/C keep V5's input name) |
| Preprocessing | BGR→RGB; bilinear (`INTER_LINEAR`) **stretch** to 224×224 (no crop, no letterbox); `(pixel/127.5)−1` — identical to V5 and to `OnnxInference.Preprocess` |
| Outputs (order) | `embedding_128` (ReLU Dense), then the final output: **multilabel experiments: 3 logits** (apply sigmoid), **softmax experiments: 4 probabilities** (Acne, Hyperpigmentation, Eczema, Normal) |
| Condition order | `Acne, Hyperpigmentation, Eczema` (= C# `ClassPalette` indices 0–2). **Normal is derived, never an output** of the multilabel model |
| Fusion feature | `g_logit = clip(z, ±9.2103)` for logits `z` (equal to `logit(clip(sigmoid(z), 1e-4, 1−1e-4))`); for softmax models, the logit of the clipped probability |

## 2. ROI feature model — `v6_roi_features_mobilenetv2.onnx`
Input float32 `[N,224,224,3]`, RGB, `(p/127.5)−1`. Outputs: `embedding_64`, `softmax[4]` in the order `Acne, Hyperpigmentation, Eczema, Background`.

**ROI crop rule (must be reproduced exactly):** take the detector box `(x0,y0,x1,y1)` in pixels of the frame the detector ran on; reject if `max(w,h) < 0.02·min(W,H)` (`too_small`), if it lies more than 1 px outside the frame (`outside_image`), or if `w<2` or `h<2` (`degenerate_box`). Side `s = max(w,h)·(1+2·margin)` with margin 0.25 (0.60 for the *alternative-context* crops), `s = min(s, W, H)`; centre the square on the box centre and **shift it inside the image** (no padding); crop; resize bilinearly to 224×224.

**Candidate tiers.** Tier 1: boxes ≥ the condition's deployed threshold, taken round-robin across detectors in the order Acne, Hyperpigmentation, Eczema, dropping any box whose IoU with an already chosen box exceeds 0.70, at most 6. Tier 2: lower-confidence pool boxes (fused confidence ≥ 0.05), same overlap rule, until the total limit (12) minus the reserved Tier-3 slots. Tier 3: alternative wider-context crops of the first `min(#tier1, 12//4)` Tier-1 boxes. **Per-ROI YOLO feature vector (15 numbers):** one-hot condition (3), confidence, relative area, centre x, centre y, width, height (relative), `log(aspect)`, `deployed` flag, ensemble support fraction, tier-1/2/3 one-hots.

## 3. Fusion models — `v6_fusion_{global_only,global_yolo,full}.onnx` (+ `_weights.json`)
Inputs (float32): `g_emb [N,128]`, `g_logit [N,3]`, `g_extra [N,2]` (= p(Normal) or 0, has-p(Normal) flag), `det_ev [N,21]` (condition-major, 7 numbers per condition: `available, detected, max_deployed_conf, max_pool_conf, sqrt(rel_area), cx, cy`; an unavailable detector is **all zeros including `available`**), `roi_feat [N,12,D]` with `D = 64+4+15 = 83`, `roi_mask [N,12]` (1 = real region). Output: `[N,3]` logits in condition order. **Runtime ensemble:** mean of the per-seed logits. Then **Platt calibration** `p = σ(a·z+b)` per condition and the per-condition threshold from `model_metadata.json`. tf2onnx may fail on the custom attention layer; the notebook then writes the weights JSON and records the failure — a C# port would implement the few layers natively (dense, softmax-masked attention pooling, concatenation).

## 4. YOLO detectors
**Not re-exported.** Existing ONNX files are used unchanged. The notebook records, per detector: file hashes, `imgsz`, layout, number of classes, NMS IoU, deployed threshold, ensemble membership, availability. Reproduce exactly the C# letterbox/decode/NMS/fusion described in Part V.

## 5. Calibration, thresholds, stage rules (configuration, not constants)
`model_metadata.json` → `fusion.calibration` (per variant: Platt `a,b`, thresholds, which conditions could be calibrated), `stages.stage1_rule` (band, selection record, `accepts_nothing`), `stages.stage2_band`, `stages.verification` (max passes 3, 20 s, minimum new ROIs 1, convergence 0.02), `stages.quality_thresholds` (**provisional**), `stages.ood` (Mahalanobis threshold). All provisional values must remain editable configuration in the application.

## 6. Versioning and integrity
Every model file carries its SHA-256 in the metadata. The application must refuse to start a V6 pipeline when a hash, label map or tensor shape differs (`load_onnx_verified`, `assert_contract`, `validate_label_map` are the Python reference checks).
