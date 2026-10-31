# Deployment integration guide (for a future, separately approved WPF change)

> **Not performed in this task.** The WPF application, the V5 model and all YOLO files were left untouched. This guide describes what the future C# work must do and how to prove equivalence first.

## 1. Shared final-result object
Introduce one C# class mirroring `FinalAssessment` (`lumyvue-final-assessment/1.0`) and make **every** consumer read it: Results screen, localisation overlays, patient record, dermatologist review, thermal receipt, A4 report. Required members: `State` (`FINDINGS_SUPPORTED`, `MULTIPLE_FINDINGS_SUPPORTED`, `NO_FINDINGS_SUPPORTED`, `UNCERTAIN_REVIEW_REQUIRED`, `RECAPTURE_REQUIRED`, `OUT_OF_SCOPE_REVIEW_REQUIRED`, `INFERENCE_ERROR`), predicted conditions, per-condition calibrated probability / threshold / decision / absence-supported / detector availability, regions with provenance, stage reached, verification passes, stop reason, model versions and hashes, fusion configuration, **original CNN scores**, **original YOLO outputs (including `null` for an unavailable detector)**, intermediate pass results, quality and OOD blocks. Persist the object (JSON) with the scan; keep the dermatologist's assessment in a **separate** record and never overwrite the AI object (the existing `ScanInterpreter` separation rule already requires this).

The interpretation layer implemented earlier (`ScanInterpreter`) remains the interim display logic until the V6 object replaces it; do not mix the two wordings on one screen.

## 2. Pipeline order in C#
1. Validate the frame (non-empty, 3-channel, 8-bit, minimum size) → quality screening (blocking flags ⇒ `RECAPTURE_REQUIRED`, **never Normal**).
2. Global MobileNetV2 ONNX → embedding + logits (preprocessing identical to `OnnxInference.Preprocess`).
3. OOD score (Mahalanobis on the embedding; parameters from `model_metadata.json`) ⇒ `OUT_OF_SCOPE_REVIEW_REQUIRED` when above the threshold.
4. YOLO detectors **in the order Acne, Hyperpigmentation, Eczema** with the existing C# post-processing, keeping the **candidate pool (fused confidence ≥ 0.05)** and the *deployed* flag per box (the app currently discards pool-only boxes: this must be extended **without changing** which boxes are shown).
5. Evidence features (`det_ev`), ROI candidates (tiers), ROI crops, ROI MobileNetV2.
6. Fusion `global_only` and `global_yolo` → Stage-1 rule; if not accepted → fusion `full` over Tier 1, then bounded passes adding Tier 2 and Tier 3 (never re-scoring identical evidence) → decision logic (`decide`) → `FinalAssessment`.
7. Any exception ⇒ `INFERENCE_ERROR` with the exception type only (no image data in logs).

## 3. Parity testing procedure (required before any model is shipped)
1. In Python run `assess_image` on N saved test photographs; store each `FinalAssessment` JSON.
2. In C# run the same files; export the same JSON.
3. Compare **per stage**: global tensor (`input_224_nhwc_float32.bin` produced by `SkinDevApp.exe --diagnose`), global logits, YOLO pool boxes (IoU > 0.95 and confidence within 1e-3), ROI crops (max pixel difference ≤ 1), ROI scores, fusion logits (≤ 1e-4), calibrated probabilities, decisions, stop reasons.
4. Record maxima in a table; any decision difference blocks deployment until explained.
5. Re-run the V5 parity levels (static contract, app-record parity, identical tensor) for the V6 global model.

## 4. Operational/security items to verify in C# (the notebook cannot)
Model-file hash verification at start-up, label-map and shape checks, no network access, pseudonymous scan identifiers, encrypted/retention-limited photograph storage, ROI-crop cache purge, protected logs without images, access control for patient and review records, secure update path, explicit consent and permissible-use tracking. **No compliance claim is made.**

## 5. Eczema
Install the real Eczema detector as `lesion_detector_eczema.onnx/.json` (classes `["eczema"]`, `imgsz`, `nms_iou`, `default_conf`) via `shared/install_specialist_detector.py`, **then** set its deployed threshold from its own validation record. Until then the app must report Eczema as *detector unavailable*, and the V6 rule `ABSENCE_REQUIRES_ALL_DETECTORS` prevents a "no findings" claim.
