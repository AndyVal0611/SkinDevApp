# Deployment integration guide (for a future, separately approved WPF change)

> **Not performed in this task.** The WPF application, the V5 model and all YOLO files were left untouched. This guide describes what the future C# work must do and how to prove equivalence first.

## 1. Shared final-result object
Introduce one C# class mirroring `FinalAssessment` (`lumyvue-final-assessment/1.1`) and make **every** consumer read it: Results screen, localisation overlays, patient record, dermatologist review, thermal receipt, A4 report. Required members: `State` (`FINDINGS_SUPPORTED`, `MULTIPLE_FINDINGS_SUPPORTED`, `NO_FINDINGS_SUPPORTED`, `UNCERTAIN_REVIEW_REQUIRED`, `RECAPTURE_REQUIRED`, `OUT_OF_SCOPE_REVIEW_REQUIRED`, `INFERENCE_ERROR`), predicted conditions, per-condition calibrated probability / threshold / decision / absence-supported / detector availability, regions with provenance, stage reached, verification passes, stop reason, model versions and hashes, fusion configuration, **original CNN scores**, **original YOLO outputs (including `null` for an unavailable detector)**, intermediate pass results, quality and OOD blocks. Persist the object (JSON) with the scan, **sealed** (`ai_content_sha256`), with `review_status = PENDING_DERMATOLOGIST_REVIEW` for **every** assessment. Keep the dermatologist's findings in a **separate** record (per-condition `present/absent/unknown`, blinding flags, hash of the reviewed AI version) and never overwrite the AI object. The full contract, schemas (`export/v6_final_assessment.schema.json`, `v6_dermatologist_review.schema.json`) and the list of WPF gaps are in **`REVIEW_CONTRACT.md`**. Stage 2 must be labelled *AI verification*, never clinical validation; `FINDINGS_SUPPORTED` etc. describe AI evidence strength only.

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

## 5. Eczema detector
The Eczema detector files (`lesion_detector_eczema.onnx`, `_m2.onnx`, `_m3.onnx`, `lesion_detector_eczema.json`) are installed in `SkinDevApp/Model/`, listed in the csproj and copied to `bin/Debug/Model` by the build (done). Then: (a) the C# ensemble fusion IoU is a constant 0.5 whereas the eczema JSON note describes 0.55 for its own evaluation — decide and document one value; (b) threshold 0.25 matches `ScanSettings.DetectorThresholdEczema`; (c) the 3 members are distinct models (hashes differ) and the JSON hash matches the primary file; (d) the hyperpigmentation JSON's `onnx_sha256` does **not** match the installed primary ONNX file (found while checking) — reconcile before quoting its metrics. Until the files are installed the app reports Eczema as *detector unavailable* and the V6 rule `ABSENCE_REQUIRES_ALL_DETECTORS` prevents a "no findings" claim.
