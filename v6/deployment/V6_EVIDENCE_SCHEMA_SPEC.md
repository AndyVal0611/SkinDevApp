# LUMYVUE V6 unified assessment: exact runtime specification (Phase 2)

Status: **EXPERIMENTAL, development-stage.** Every assessment is `PENDING_DERMATOLOGIST_REVIEW`; `clinical_validation` is always false. Source of truth: `astra_runs/open_run02/export/v6_model_metadata.json` and the notebook sources in `v6/astra_training_src/`. Machine-readable copy: `v6_deployment_config.json`. Fixtures: `v6_test_vectors.json`.

Architecture (agreed, not changed): Global MobileNetV2 + three YOLOv8 ensembles → **`global_yolo` trained fusion** → Platt calibration → per-condition thresholds → one assessment. No voting, no averaging, no model forced to agree with another. The ROI branch and Stage 2 are **not** used.

## 1. One frame, three consumers
The same frozen frame (`req.Working`, BGR) is given to the Global CNN and to the three detector ensembles. `W,H` below are that frame's width and height.

## 2. Global model (`v6_global_features_mobilenetv2.onnx`)
- Input `image_nhwc_m1p1` float32 `[N,224,224,3]`: BGR→RGB, bilinear stretch to 224×224, `(p/127.5)-1` (identical to `OnnxInference.Preprocess`).
- Outputs: `Identity:0` embedding `[N,128]`; `Identity_1:0` logits `[N,3]` in order Acne, Hyperpigmentation, Eczema.
- Fusion inputs from it: `g_emb` = embedding; `g_logit` = `ln(p/(1-p))` with `p = clip(sigmoid(z), 1e-4, 1-1e-4)`; `g_extra` = `[0, 0]` (no softmax Normal in this model).

## 3. Detectors (3 conditions × 3 members, files already in the WPF Model folder)
Per member (this is what `LesionDetector.DetectSingle` already does): letterbox to `imgsz` (Acne 800, Hyperpigmentation 800, Eczema 640; pad 114, bilinear), RGB, `/255`, NCHW; output `[1,5,N]` = cx,cy,w,h (letterbox px) + one class score (no objectness); keep anchors with score ≥ **0.02**; map back with `(v - pad)/scale`, clamp to the frame, drop boxes narrower or shorter than 2 px; class NMS at IoU **0.6**; at most 60.
Ensemble (this is what `LesionDetector.Detect` already does): cluster boxes in descending confidence by IoU ≥ **0.5** against the cluster's confidence-weighted average box; fused confidence = `min(1, sum(conf)/3)`.
**Differences from today's C# that Phase 3 must add:**
1. Keep the whole candidate pool: every fused box with confidence ≥ **0.05** (max 60 per condition), each flagged `deployed = conf ≥ threshold` (Acne 0.275, Hyperpigmentation 0.225, Eczema 0.25). Today's code discards everything below the deployed threshold, but the fusion needs `max_pool_conf`.
2. **No face filter for the evidence.** Training evidence was computed with `YOLO_FACE_FILTER = False`. The app's face filter may still decide what is *drawn*.
3. Rounding as in the training cache: conf 5 decimals, `rel_area` 6, `cx`/`cy` 4, box coordinates 2.

## 4. Detector evidence `det_ev` (21 floats; per condition in order Acne, Hyperpigmentation, Eczema; 7 numbers each)
| # | name | value |
|---|---|---|
| 0 | available | 1 if the detector ran, 0 if it is missing or failed |
| 1 | detected | 1 if at least one box is deployed |
| 2 | max_deployed_conf | confidence of the best deployed box, else 0 |
| 3 | max_pool_conf | highest confidence in the whole pool (deployed or not), else 0 |
| 4 | sqrt_rel_area | `sqrt((x1-x0)(y1-y0)/(W·H))` of the best deployed box, else 0 |
| 5 | cx | `(x0+x1)/2/W` of the best deployed box, else 0 |
| 6 | cy | `(y0+y1)/2/H` of the best deployed box, else 0 |

"Best" = the deployed box with the highest confidence. **A missing or failed detector gives seven zeros including `available = 0`. It is never encoded as "no detection", and missing detections are never evidence of absence.** No box counts are used.

## 5. Fusion (`v6_fusion_global_yolo_seed42.onnx`) and calibration
- Inputs (float32): `g_emb[N,128]`, `g_logit[N,3]`, `g_extra[N,2]`, `det_ev[N,21]`, `roi_feat[N,12,83]` = **zeros**, `roi_mask[N,12]` = **zeros**. Output logits `[N,3]`.
- `p = sigmoid(a·z + b)` per condition with the Platt values in `v6_deployment_config.json` (global_yolo a = 0.730, 0.762, 0.536; b = −0.681, −0.547, −1.733).
- Thresholds 0.42, 0.37, 0.27. Stage-1 band **0.0** (selected on out-of-fold fusion data; provisional).

## 6. Decision logic (port of `decide()` / `stage1_flags()`, band 0.0)
Per condition: `present` if `p ≥ t`, `absent` if `p < t`; an `absent` is **supported only if that condition's detector is available**, otherwise it is `uncertain`.
Flags (any flag ⇒ review required, no silent override):
- `cnn_yolo_conflict_<condition>`: detector available and `(global_only calibrated p ≥ global_only threshold) ≠ detected`. This uses `v6_fusion_global_only_seed42.onnx` with `det_ev` = zeros, and is a **flag only**: it never changes the fused score.
- `ood`: Mahalanobis score of the embedding > threshold (`v6_ood_model.json`).
- `quality_<blocking>` / `quality_warning`: from the quality thresholds in the config.
States: `FINDINGS_SUPPORTED`, `MULTIPLE_FINDINGS_SUPPORTED`, `NO_FINDINGS_SUPPORTED`, `UNCERTAIN_REVIEW_REQUIRED`, `RECAPTURE_REQUIRED`, `OUT_OF_SCOPE_REVIEW_REQUIRED`, `INFERENCE_ERROR`. Any exception ⇒ `INFERENCE_ERROR`, never a Normal-looking result.
**Normal** is derived, never a model output: only when all three conditions are `absent` with supported absence (all three detectors available), no flags and no blocking quality problem (`NO_FINDINGS_SUPPORTED`).
Stage 2 / ROI escalation does **not** exist in this deployment: where the notebook would escalate, this deployment returns `UNCERTAIN_REVIEW_REQUIRED` while still showing each condition's fused leaning.

## 7. Parity status (`v6_cpu_parity_verification.json`)
- Global features: Keras CPU vs ONNX CPU max difference 4e-5 (160 real validation photos).
- `global_yolo` fusion: max logit difference **1.9e-6**, **0 of 1,200** decisions flipped (400 inputs, CPU vs CPU).
- The notebook's "outside tolerance" flag compared Keras on the GPU with ONNX on the CPU; the GPU gap (about 0.05) is numeric noise and is not present on CPU.
- **Not yet verified:** C# against Python (Global scores, detector boxes, evidence, final result). `v6_test_vectors.json` is the fixture for it; the vectors pair synthetic box layouts with real embeddings, so their *states* test plumbing, not clinical sense.

## 8. Open items and decisions for the next phases
1. **Review rate.** On the fusion split only **40.8 %** of scans pass Stage 1 without a flag (error 5.4 % on those). With no Stage 2, expect most scans to read `UNCERTAIN_REVIEW_REQUIRED`, mainly from `cnn_yolo_conflict`. Per-condition leanings are still shown. Decide whether the headline wording should stay "review required" or be softened.
2. **Quality metrics.** The notebook's blur/brightness/exposure/glare heuristics (unvalidated) must be mapped onto the app's `FrameQualityAnalyzer`; until then the quality gate is limited to what that analyzer already measures.
3. **Heat maps.** Grad-CAM comes from the V5 Python service; a V5 heat map beside a V6 result would be a competing explanation. Phase 4 must hide it or label it as V5.
4. **Hyperpigmentation detector JSON** `onnx_sha256` does not match the installed primary file (the run records the real hashes). Do not quote its published mAP; the file itself is unchanged.
5. **Domain shift.** Evidence was learned on dataset photos, not prototype captures; no independent test exists. Say so wherever results are shown.
