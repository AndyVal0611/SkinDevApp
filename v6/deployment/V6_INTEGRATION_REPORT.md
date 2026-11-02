# LUMYVUE V6 integration report (Phases 1 to 5)

Date: 2026-11-02. Branch: `v6-integration` (nothing committed). Status: **EXPERIMENTAL, development-stage.** The V6 service is implemented and verified by tests; the screen has been compiled but not yet run. The V6 feature flag is **off by default**.

## Architecture as built (unchanged from the agreed design)
Global MobileNetV2 (embedding + logits) and three YOLOv8 ensembles (candidate pool down to 0.05, no face filter) feed the trained `global_yolo` fusion; Platt calibration and per-condition thresholds follow; Normal is derived only when all three conditions are absent with working detectors, no flags and assessed image quality. No voting, no averaging, no forced agreement. The ROI branch and Stage 2 are not used. V5 files are untouched.

## Verification
| Requirement | Evidence | Result |
|---|---|---|
| Python vs ONNX parity | CPU Keras vs CPU ONNX, `global_yolo` fusion: max logit difference 1.9e-6, 0 of 1,200 decisions flipped; Global features: 4e-5. The earlier "outside tolerance" flag was GPU-vs-CPU noise (`v6_cpu_parity_verification.json`) | Pass |
| Artifacts and hashes | Every export, V5 and all 9 detector files match the run metadata; the service re-checks hashes at load and refuses a mismatch | Pass |
| Reference faithful to training | Python detector replica vs the training-time detection cache: same box count on 13/13 photographs, max difference 0.000000 | Pass |
| C# vs Python on real photographs (13) | 270 candidate boxes: worst coordinate difference 0.0001 px, confidence 1e-5; evidence vector, fusion logits, calibrated probabilities, flags, states, per-condition decisions and supporting boxes all agree | Pass |
| Image-coordinate mapping | Square and non-square frames (216x312, 720x482, 605x436, 640x640, 224x224); boxes inside the frame | Pass |
| No detections, low-confidence, overlapping, multiple conditions, missing detector | 8 fixtures + unit tests; real photos with 0 and up to 19 deployed boxes | Pass |
| Missing detector is not "no detection" | seven zeros incl. `available = 0`; absence unsupported; never Normal | Pass |
| Normal safety | needs all absent + all detectors + no flags + assessed quality; failures give INFERENCE_ERROR | Pass |
| V5 behaviour unchanged | V5 ONNX output on real photos equals Python (1e-4); the 40 existing tests pass; only 5 existing files edited (list below) | Pass |
| Flag off restores the original workflow | scan pipeline and Results page are gated by one flag; loader returns null when off and for scans saved without V6 | Pass (code and loader tests; not run on screen) |
| Locked test split | not opened, not used for debugging or selection | Respected |

Test totals: **74 passed, 0 failed** (40 existing + 34 V6). App compiles (Release, built into a scratch folder; the existing `.exe` was not touched).

## Existing files edited (all additive)
`ScanPipeline.cs` (+15), `CaptureRecord.cs` (+2), `Views/ResultsPage.xaml.cs` (+18, 2 lines changed), `SkinDevApp.csproj` (+16), `SkinDevApp.Tests.csproj` (+6). New: `SkinDevApp/V6/`, `SkinDevApp/V6App/`, `SkinDevApp/Model/V6/` (config, OOD, three ONNX files, about 10 MB), `v6/deployment/`, `v6/tools/` (generators), tests.

## Measured and observed
- **Time:** one V6 assessment on CPU (9 detector members + Global + 2 fusions) took a median of **2.1 s** per photograph, maximum 7.1 s (includes warm-up). V6 runs the detectors a second time for its own evidence, so a scan with the flag on takes about this much longer.
- **Review rate:** on these 13 validation photographs, 10 came out `UNCERTAIN_REVIEW_REQUIRED`, 1 `FINDINGS_SUPPORTED`, 2 `NO_FINDINGS_SUPPORTED`. This follows the design (a classifier-versus-detector conflict raises a review flag, and there is no Stage 2); the fusion split showed 40.8 % of scans passing Stage 1. Expect most scans to read "review recommended".
- These tests prove the C# service reproduces the Python reference. They are **not** an accuracy evaluation; V6 was not shown to be more accurate than V5 (see the run02 comparison).

## Phase 4b: unified surfaces (flag on + a saved V6 assessment; otherwise every screen is unchanged)
One shared wording layer (`V6Text`, `V6SessionSummary`) now feeds: the Results page, **thermal receipts** (preliminary and final; the history label becomes "V6: ..."), the **A4 report** (summary page, findings, a V6 marked-areas page; the Grad-CAM++ attribution pages are dropped), the **dermatologist review** summary, the **researcher review** (AI column and the suggested agreement, now computed against the V6 findings), and the **patient record** (selected-scan card and "Latest scan" card). Three-photograph scans are reported per photograph and never merged. 82 of 82 tests pass.

## Live run (2026-11-02, isolated data folder, V6 flag on, real app assembly, real models and detectors, real photographs)
The real `ScanPipeline` ran on 3 dataset photographs (V5 + Grad-CAM + detectors + V6 + archive), then the real Results card, review summary, preliminary receipt and 5-page A4 report were built and rendered with the app's own styles. All steps completed without errors; nothing touched the real database or captures (`LUMYVUE_DATA_DIR` redirected). Scan finalisation took about 5 s (first scan 10-12 s with model loading), of which the V6 assessment was about 2 s.
Findings fixed during the run: the raw "Blocking quality: ..." line was repeated next to the plain advice; the A4 report still carried the V5 "Model Class Score / Grad-CAM++" disclaimer and a V5 predicted-class caption under each photograph; box labels piled up when a photograph had many boxes. **Open finding:** all three dataset photographs ended as `RECAPTURE_REQUIRED` because the application's face detector found no face in these tight close-up crops (and one photograph is below 224 px). See "Decisions" below.

## Decision taken (owner, 2026-11-02): face gate
"Face not found" **blocks a live capture** (retake) but is **only a warning for an uploaded photograph**, because close-up crops never contain a whole face. The warning still raises a review flag, so an upload without a detected face can be assessed per condition but is never called Normal. Everything else (blur, exposure, resolution below 224 px) still blocks uploads. Re-run of the live flow: the hyperpigmentation photograph now reads "Hyperpigmentation: Present" with review recommended; the clean photograph reads "Not found" for all three with review recommended (not Normal); the 216x312 photograph still reads "retake" (resolution). 83 of 83 tests pass.

## V5 feature parity with V6 on (2026-11-02, late)
V6 heat maps now exist in the same form as V5's: one tinted map per condition on the analysed photograph (same palette, opacity rule, gamma), saved with every scan (`v6_overlay_<Condition>.png`, `v6_heatmap_<Condition>.png`, `v6_overlay_all.png`, `v6_combined_<Condition>.png`). They are **detector-evidence maps** (strength = detector confidence, full strength from 0.70; boxes outside the face region are not drawn), not an attribution of the fusion: a Grad-CAM of the fusion was prototyped and rejected because the maps of different conditions were 95-98 % identical (`v6/tools/v6_attribution*.py`).

| V5 feature | With V6 on |
|---|---|
| Results page: result card, per-photograph reading, marked areas, heat map, "Both" | Done. Heat map and "Both" are V6's; a condition chooser (highest ranked / Acne / Hyperpigmentation / Eczema) replaces V5's per-class maps; V5's Grad-CAM stays as "Classifier map (V5)" |
| Photographs-analysed chips, "photographs agree" message | Done |
| Comparison gallery | Done: V6 evidence cards first, V5 class maps follow, labelled V5 |
| Patient record: photographs, heat maps and marked areas | Done (V6 maps) |
| A4 report: heat-map evidence pages | Done (detector evidence maps, one per photograph) |
| Receipts, dermatologist review, researcher review (suggested agreement) | Done |
| Live camera screen: live scores, verdict line and live heat map on the preview | Done (V6 live reading): the full V6 assessment is refreshed about every 2-3 s on the newest frame (first reading about 7 s after start while the models load); the bars show AI support per condition, the verdict line shows the V6 headline, the preview shows the V6 evidence map of the highest-ranked condition (about 13 ms per video frame). V5 still decides when to auto-capture; V5's per-frame Grad-CAM++ requests are paused while V6 is on |
| Session list columns, statistics and research views | Not done: read the stored V5 class |

## Not done
1. A person clicking through the real windows (login, navigation, the print preview dialog, the physical printer). The live run above exercised the same code paths and rendered the real elements, but not the interactive shell.
2. Still V5-based when the flag is on: the session list columns and statistics in the patient record and research views (they read the stored V5 class from the database, kept as the baseline), the "Compare photographs and heat maps" gallery, and the Grad-CAM++ maps saved with each scan.
3. No settings switch for the flag (set `LUMYVUE_V6_UNIFIED=1`).
4. Quality gate covers only what the app measures (resolution, sharpness, exposure, face); glare, white balance, compression and overlays are not checked.
5. No independent labelled test, no dermatologist validation; the detector evidence was learned on dataset photographs, not prototype captures.
6. Nothing committed; the `Model/V6` files (about 10 MB) are untracked.

## How to try it
Set the environment variable `LUMYVUE_V6_UNIFIED=1`, launch the app and run a scan. Scans made with the flag on save `v6_assessment.json` (sealed) and `v6_marked_areas.png` next to `record.json`; the Results page shows the unified card. Without the variable the app is the V5 app.
