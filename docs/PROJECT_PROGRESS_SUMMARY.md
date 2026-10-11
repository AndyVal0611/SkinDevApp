# PrecisionSkin / LUMYVUE - full progress summary (from the start of our work until 10 Oct 2026, evening)

Written for you to keep. Numbers are the ones we measured; where something is a judgment or not verified, it says so.

---------------------------------------------------------------------------------------------------------------------------------------------

## 1. The system in one paragraph
A WPF (.NET Framework 4.7.2) kiosk app running on a CPU-only Windows mini PC. It has (a) a **MobileNetV2 classifier** (Acne / Eczema / Hyperpigmentation / Normal), (b) a **Grad-CAM++ service** (Python/TensorFlow on 127.0.0.1:8765) that explains the classifier, and (c) **YOLOv8 lesion detectors** that draw candidate boxes. Study data live in SQLite (participants, scans, researcher and dermatologist verification). Objectives (current wording): CNN classification of acne, eczema, hyperpigmentation; standardised capture hardware; OpenCV preprocessing; evaluation with accuracy / precision / recall / F1; ISO/IEC 25010 + TAM. The mimicker logic was removed from the objectives; the Fitzpatrick AI module was removed from the system.

---------------------------------------------------------------------------------------------------------------------------------------------

## 2. Timeline of what we did

### Phase A - Hyperpigmentation auto-boxing and model comparison
- Built `hyperpig_autolabel.py` (one-class dataset build, duplicate/overlap checks, training of variants n_640 / s_800 / m_800, pseudo-labelling with resume, scoring code used by every later notebook) and the notebooks `PrecisionSkin_Hyperpigmentation_AutoBox.ipynb` and `..._Boxing_Sprint.ipynb`.
- You hand-boxed hyperpigmentation: **646 photos opened = 504 boxed (2,117 boxes) + 142 marked X**; 100 hand-boxed photos are held out as the test set.
- Boxing-consistency tracking for acne and hyperpigmentation (box-size and overlap checks) and a worklist (`BOXING_WORKLIST.md`).

### Phase B - Baseline, own labels, acne boxing sprint
- Compared the app's detector (v1) with published results; agreed the public Roboflow boxes were rough, so you box your own with one rule per condition.
- Built the **box tool** `lesion_box_tool.py` (local web tool, X = "no lesion", export of saved photos) and the **acne sprint**: all 4,073 distinct acne photos from your folder (de-duplicated at 0.93, test photos reserved, train/test roles).
- Built the **pre-draw helper** (`acne_predraft.py`): a model trained on your boxes draws first boxes, you correct them. Reliability measured on your reviews: **87 % recall, 98 % precision** of drafted boxes; later More_Faces: 79 % recall, 99.5 % precision.
- Merged everything into YOLO datasets with `merge_acne_dataset.py` (leak checks, scratch-only validation option, leak-free test).

### Phase C - Fitzpatrick removed, forms updated
- Diagnosed the Fitzpatrick model: folders encoded apparent ethnicity (Types I/II AUC 0.48, 78 % of both predicted Type II); it learned faces, not tone. **The AI Fitzpatrick module and its UI were removed** (manual and dermatologist skin-type records kept). Documented as a negative finding (`METHODS_AND_LIMITATIONS_NOTES.md` 6.1).
- Updated the study forms/summary documents to the current system (MobileNetV2, detector, Visual Studio app).

### Phase D - Big app update (pushed)
- **Dermatologist Review page**, participant search by ID or name (picker and records), camera auto-detect with a frame test, portable Grad-CAM++ launcher (log-lock, process-tree, GPU registry fixes found by testing), validation-status wording, additive DB upgrade. A **PC benchmark script** (`benchmark_pc.py`) for the mini PC (not run on it yet).

### Phase E - Acne dataset to v4 and the final acne run (this part of the work)
- Fixed my own "doubled box" rule (it flagged touching neighbours and close-up boxes): only 5 of 46 photos were truly doubled; rule loosened; you fixed the 46 (`Fix_46_Doubled_Boxes`).
- Reviewed 400 helper-drafted non-facial photos (v3): **this made the test score drop** (0.387). Ablation v3a (facial only) recovered it (0.423). Conclusion: those drafted non-facial photos hurt; they were removed.
- You then boxed **More_Faces** (580 pre-filtered photos, 448 boxed, 130 X, 4,662 boxes) -> **dataset v4: 1,145 train / 154 validation / 167 test photos** (validation = photos you boxed from scratch only; test unchanged).
- Final acne training: 3 seeds (42, 7, 123), YOLOv8s, 800 px (see section 4).

### Phase F - Grad-CAM++ and the app display
- Measured that the four class heatmaps are **~87 % alike** (raw Pearson; shared component ~90 %): one image-level classifier scores all classes from the same features. A "this class minus the others" map did not help on real captures (a 1 %-score class got the strongest map), so it is opt-in only. Layer study: earlier layers are *more* alike (0.98-0.99).
- Enrichment test on your 167 hand-boxed photos: the acne Grad-CAM map put **1.30x** chance of its heat inside your boxes versus **1.18x** for a fixed face-centre blob: it carries almost no lesion-specific information. Grad-CAM is attribution, not localisation.
- App changes: **one Grad-CAM++ picture** (the detected class) in the record, gallery and print views (all four still saved); overlay opacity follows the class score; shorter on-screen notes; the service's startup parity check relaxed (1e-4 -> 5e-3, it would have refused to start on other CPUs); `gradcam_enrichment.py` added.
- **38 of 42 existing scans re-rendered** to the new look (backups in `_before_display_update`; originals, heatmaps, records untouched; 4 old scans without heatmap files skipped).

### Phase G - Detector integration in the app
- **Specialist detectors**: the app now loads optional one-class models (`lesion_detector_<class>.onnx/.json`) that replace the 3-class model's boxes for that class; broken specialist = automatic fallback. Installed: **acne** (seed 123, best validation of 3 seeds) and **hyperpigmentation** (s_800); eczema stays on v1 (teammate trains it).
- Thinner, crisper box drawing (white halo, strongest on top, count legend); boxes outside the detected face are dropped; Settings page shows "Active detectors"; defaults acne **0.35**, hyperpigmentation **0.175** (saved settings must be edited once).
- `install_specialist_detector.py` installs a trained model in one command.

### Phase H - Skin tone (Monk Skin Tone) set-up
- App: optional **Monk Skin Tone (1-10) + who rated it** in registration, shown on the patient record, changes audited; additive DB columns.
- `mst_skintone.py` + notebook `PrecisionSkin_SkinTone_MST_Training.ipynb`: separate MobileNetV2 model on MST-E + SCIN with person/case-grouped splits, no hue augmentation, within-1-shade and 3-band metrics with a people-resampled range, brightness stress test. Smoke-tested on fake data only. **Datasets not downloaded yet**; licences not verified.
- Paper text: `MST_PAPER_SECTION.md`, notes section 6.2. Verified by search: MST-E = 19 people, 1,515 images + 31 videos, made for annotator training; SCIN tone labels are layperson estimates.

### Phase I - Diagnosis and controlled experiments (detector)
- Read-only diagnosis of all datasets and models (`lesion_diagnose_data.py`, `lesion_diagnose_models.py`, `LESION_DETECTOR_DIAGNOSIS_AND_PLAN.md`); experiment runner `lesion_exp.py` (one factor per run, registry `EXPERIMENTS.csv`, baselines backed up); a new notebook `Pipeline V5 - Lesion Detector.ipynb` that mirrors your classifier pipeline stage by stage (smoke-tested, all sanity checks pass).
- Second-look relabel of 60 acne training photos (`Fix_Acne_Missed_Spots`): you took them from **893 to 1,430 boxes (+60 %)**.
- Usage-level metrics (`lesion_alt_metrics.py`) and ready-to-paste results (`RESULTS_FOR_PAPER.md`).
- Clean-up: obsolete datasets/notebooks moved to `_ARCHIVE_to_delete_later` folders (Windows ~500 MB, WSL ~1.2 GB); only my own temporary files were deleted.

---------------------------------------------------------------------------------------------------------------------------------------------

## 3. Results

### 3.1 Acne detector - history on the same 167 hand-boxed test photos
| Model | train photos | mAP@0.5 | mAP@0.5-0.95 | P | R | F1 |
|---|---|---|---|---|---|---|
| v1 (3-class, in the app before) | - | 0.220 | 0.066 | 0.36 | 0.23 | 0.28 |
| first new model (one seed) | 708 | 0.448 | 0.135 | 0.45 | 0.56 | 0.50 |
| v3 (+400 drafted non-facial photos) | 1,099 | 0.387 | 0.116 | 0.43 | 0.53 | 0.47 |
| v3a (facial only) | 748 | 0.423 | 0.127 | 0.42 | 0.55 | 0.48 |
| **v4, 3 seeds (final)** | 1,145 | **0.413** (std 0.006; 95 % range 0.37-0.46) | 0.122 | 0.43 | 0.54 | 0.48 |
Gain over v1: **+0.193** mAP@0.5 (95 % range +0.147 to +0.238). The 0.448 was one lucky seed; **0.413 is the number to report**. Threshold sweep (3-seed mean): conf 0.25 -> P 0.43 / R 0.54; **0.35 -> 0.48 / 0.48 (app default)**; 0.40 -> 0.50 / 0.44; F1 stays ~0.47-0.48 from 0.20 to 0.40.

### 3.2 Acne detector - reliability at the level the app is used (3 seeds, threshold 0.35)
Photo level (167 acne photos vs 200 healthy photos): **AUC 0.971**, sensitivity **95.0 %**, specificity **85.0 %**. Count level: Spearman **0.70**, mean error 3.9 boxes per photo (about 8 true). 3x3 zones: precision **0.83**, recall **0.79**, F1 **0.81**. (Healthy photos were not individually verified.)

### 3.3 Acne experiments (controlled, validation photos only, seed 42; baseline mAP@0.5 0.425, F1 0.494)
| Exp | Change | mAP@0.5 | mAP@0.5-0.95 | P | R | F1 | Verdict |
|---|---|---|---|---|---|---|---|
| A1 | + 115 healthy-skin negatives | 0.431 | 0.137 | 0.49 | 0.49 | 0.49 | noise |
| A2 | milder colour jitter | 0.424 | 0.132 | 0.48 | 0.50 | 0.49 | noise |
| A3 | box-loss gain 10 | 0.431 | 0.135 | 0.50 | 0.50 | 0.50 | noise |
| A4 | lower learning rate | 0.433 | 0.136 | 0.45 | 0.52 | 0.48 | noise |
| A5 | 960 px input | **0.442** | 0.140 | 0.50 | 0.49 | 0.49 | +0.017: just under the +0.02 rule; worth confirming with seeds |
| A6 | yolov8m | running | | | | | |
| A7 | 60 relabelled photos (missed spots added) | queued | | | | | validation/test labels are still incomplete, so measured precision may even fall |
Rule: adopt a change if mAP@0.5 improves >= 0.02 and neither P nor R drops > 0.02. Seed spread of the baseline on validation is ~0.01. More photos (708 -> 1,145) did not help either.

### 3.4 Hyperpigmentation detector (100 hand-boxed test photos) - limited pilot
| Model | mAP@0.5 | mAP@0.5-0.95 | P | R | F1 |
|---|---|---|---|---|---|
| v1 (in the app before) | 0.053 | 0.019 | 0.19 | 0.05 | 0.08 |
| s_800 (installed) | **0.178** | 0.051 | 0.30 | 0.18 | 0.22 |
| n_640 / m_800 | 0.036 / 0.065 | | | | **invalid: those runs stopped after 7 / 9 epochs** |
Public-style validation/test sets: v1 0.36 / 0.32, s_800 0.34 / 0.28 (v1 learned the public box style). Hyperpigmentation experiments (hand-boxed validation, only your boxes, de-duplication, balanced mix, fully trained n and m) are queued; H0 stopped at epoch 16 and will resume.

### 3.5 Eczema
Teammate trains it. Handover ready: the lesion notebook with `CONDITION = "eczema"` and `install_specialist_detector.py --cls eczema`. v1's eczema data: 277 train photos, 32 val, 32 test; 64 % of photos under 400 px.

---------------------------------------------------------------------------------------------------------------------------------------------

## 4. What the diagnosis found (why scores are where they are)
- **Acne:** data clean (no near-duplicates, no doubled boxes, consistent sizes); the limits are tiny spots (recall 20 % under 2 % of the photo side, 40 % at 2-4 %), **incomplete labels** (+60 % boxes when re-reviewed), strict-IoU sensitivity on ~30 px boxes (mAP@0.5-0.95 ~0.12 whatever the model), and mild overfitting after epoch ~50 (val/train loss 1.7-1.8). Drafted photos have far fewer tiny boxes (0.4 % vs 2.5 %).
- **Hyperpigmentation:** public boxes ~2x coarser than yours (cover 18 % vs 9 % of the photo) and 75 % of training; the old validation set was in the public style; 2,524 of 3,031 training photos have near-duplicates; look-alikes between splits (public val 15, public test 16, hand-boxed test 6); 41 % of your photos and 51 % of the hand-boxed test photos are under 400 px.
- **3-class v1 data:** acne 6,962 photos vs hyperpigmentation 2,629 vs eczema 277 (25:1).

---------------------------------------------------------------------------------------------------------------------------------------------

## 5. Mistakes and corrections (so the record is honest)
- I first said drafting was not faster at 119 photos; measured later: 14.2 s/photo (faster).
- I once killed too many python processes and stopped your box tool (labels were safe; I apologised and backed them up).
- My "doubled/nested box" rule was wrong for close-ups and touching neighbours; fixed.
- My plan to "draft 400 new facial photos" was wrong: the facial pool was already used up (all but 12 reviewed); the non-facial pool had real faces but also junk, and the drafted ones hurt the score.
- I mistakenly judged the hyperpigmentation size comparison valid; the diagnosis showed n and m runs had stopped after 7 and 9 epochs.
- The 0.448 acne result was a lucky seed; the honest figure is 0.413.
- WSL crashed several times ("Catastrophic failure"); restarted with your OK; jobs are resumable.
- I started the acne-negatives training once before you wanted it (you interrupted); it ran briefly and was stopped by the next WSL restart; the experiment was later re-run properly (A1).

---------------------------------------------------------------------------------------------------------------------------------------------

## 6. Where things are
- **Repo** `C:\Users\Bautista\source\repos\SkinDevApp`: app code, scripts, notebooks, `RESULTS_FOR_PAPER.md`, `LESION_DETECTOR_DIAGNOSIS_AND_PLAN.md`, `MST_PAPER_SECTION.md`, `METHODS_AND_LIMITATIONS_NOTES.md`, this file.
- **Downloads**: working copies of notebooks/scripts (the notebooks import `hyperpig_autolabel.py` from here), `PrecisionSkin\Acne_Boxing` (originals `Elaiza__acne`, `Draft_Acne`, `More_Faces`, `Test_Acne`, fix folders, `Fix_Acne_Missed_Spots`, `acne_dataset_v4`, `boxed_Acne`), `Hyperpigmentation_Boxing` (300 drafts + 26 second-look photos, not yet reviewed), `_ARCHIVE_to_delete_later`.
- **WSL** `~/PrecisionSkinV1`: models, `acne_detector_v4` (3 seeds), `lesion_experiments` (runs, EXPERIMENTS.csv, baselines), `hyperpig_autolabel`, `detector_work` (v1), classifier folders (untouched).
- **Git:** pushed commits `b6d4283` (one Grad-CAM map, overlays, boxes), `0571702` (detector tooling), `2b5e24d` (MST). **Not committed:** specialist-detector support, face filter, new thresholds/settings, the lesion pipeline notebook (staged), experiment scripts, `RESULTS_FOR_PAPER.md`, this summary. The two 44 MB specialist ONNX files are in `SkinDevApp/Model` and are not meant for git.

---------------------------------------------------------------------------------------------------------------------------------------------

## 7. What is not done / still open
1. Acne experiments A6 and A7 finish tonight; A5 (960 px) is the only candidate near the adoption rule: confirm with seeds 7 and 123 if you want the extra ~0.02, noting inference gets slower on the mini PC.
2. Hyperpigmentation experiments (hand-boxed validation etc.) and then its final test evaluation.
3. Final test evaluation of any new configuration (one time, after selection).
4. A blind re-boxing of ~30 test photos by someone else (human-agreement ceiling; also tells how incomplete the test labels are).
5. Test the app on screen after rebuilding (Settings shows the active detectors; set acne 0.35 and hyperpigmentation 0.175 once).
6. Measure latency on the mini PC (`benchmark_pc.py`); no real-scan evaluation of the detectors yet.
7. Skin-tone (MST) model: download MST-E and SCIN, check licences, run the audit and the notebook.
8. Eczema: teammate's model; hand-boxed eczema test set.
9. UI polish needs your screenshots; study PDFs/forms still need your wording check (mimicker, hardware description, Fitzpatrick claims).
10. Commit and push the open changes when you say so.

---------------------------------------------------------------------------------------------------------------------------------------------

## 8. The numbers to put in the paper
- Acne detector: mAP@0.5 **0.41** (3-seed mean, std 0.006), F1 **0.48**, P 0.43 / R 0.54 (0.48 / 0.48 at the app threshold); photo-level AUC 0.97, zone F1 0.81; +0.19 over the earlier detector (range +0.15 to +0.24).
- Hyperpigmentation: mAP@0.5 **0.18**, F1 0.22 - a limited pilot (3x the earlier detector).
- Negative results worth one paragraph: Grad-CAM maps are not lesion maps (enrichment 1.30x vs 1.18x for a fixed blob); Fitzpatrick model learned ethnicity; five acne training changes gave no gain beyond noise.
