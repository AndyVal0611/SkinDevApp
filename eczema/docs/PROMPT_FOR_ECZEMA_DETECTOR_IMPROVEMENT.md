# Prompt to give to an AI assistant: improve the eczema lesion detector

Copy everything below the line into the assistant (Claude Code or similar) while it can see the PrecisionSkin repository and your eczema data. Fill in the two places marked [ ].

---

I am working on **PrecisionSkinV1**, an AI-assisted facial skin health monitoring system for our undergraduate thesis. My part is the **eczema lesion detector (YOLOv8)**. It proposes bounding boxes around eczema patches on face photos and will be installed in a C# WPF app next to a MobileNetV2 classifier and the acne and hyperpigmentation detectors. My current results are too low: [paste your current mAP@0.5, mAP@0.5:0.95, precision, recall, F1, and which set they are on]. My goal is **the best honest improvement that I can defend in Chapter 4**. Do not promise a number, do not inflate metrics, and do not tune on the test set.

## 0. How to work with me
- **Inspect first, change nothing.** Read my eczema notebooks, dataset folders and labels, training settings and results, and report what you found before proposing anything. Separate **confirmed findings** (measured on my files) from **hypotheses**.
- Ask me before any long training run (more than about 30 minutes) or any change to labels, datasets, existing weights or the app. Never overwrite my datasets, labels, checkpoints or experiment records. Never touch validation or test labels.
- Training goes through a Jupyter notebook that is executed so the epoch output stays saved in the file (`..._WITH_OUTPUTS.ipynb`). Training runs in WSL on a single GPU (about 8 GB) and about 6-7 GB of RAM: never run two trainings at once.
- Participant photos stay on my machine. Nothing is uploaded or committed.
- Use plain language in your explanations. Keep records of every run (settings, data fingerprint, validation scores, seeds).

## 1. What my team already did for two other detectors (use it as the playbook)
The repository has the files: `eczema/docs/HOW_WE_IMPROVED_HYPERPIGMENTATION_STEP_BY_STEP.md`, `hyperpigmentation/docs/HYPERPIGMENTATION_ANNOTATION_RULE_V2.md`, `acne/docs/ACNE_IMPROVEMENT_AUDIT_AND_PLAN.md`, `docs/LUMYVUE_COMPLETE_TECHNICAL_RECORD.pdf`, the experiment runner `shared/lesion_exp.py`, the box tool `shared/lesion_box_tool.py`, the installer `shared/install_specialist_detector.py`, and the notebooks `hyperpigmentation/notebooks/PrecisionSkin_Hyperpigmentation_Training_and_Results.ipynb`, `acne/notebooks/PrecisionSkin_Acne_Training_and_Results.ipynb`, `acne/notebooks/PrecisionSkin_Acne_Phase1_Experiments.ipynb` ... `Phase3_FreshTest.ipynb`. Read them.

**Hyperpigmentation (diffuse patches with soft edges, the closest case to eczema): test mAP@0.5 went from 0.18 to 0.51 (F1 0.24 -> 0.52). What actually worked, in order of gain:**
1. **Measured label reproducibility first.** Boxing 30 validation photos a second time from scratch, blind, gave an agreement of only F1 0.28 at IoU 0.5 with the first boxing: the labels, not the model, were the ceiling.
2. **Wrote a boxing rule** (one box per connected patch; patches that touch or are closer than the smaller one's width are one box; skip marks under about 7 % of the photo; one box stays inside one facial region; edge where the darkening fades to half; usually 1-5 boxes; no zooming to find more) and tested it on a day gap: agreement rose to 0.49-0.55. (Two passes a few minutes apart gave 0.67, which was inflated by memory.)
3. **Relabelled under the rule:** validation and test boxed from scratch with no helper; training boxes converted by code (merge touching boxes, drop tiny ones) and then corrected by hand. The code conversion alone helped a lot; the first ~200 hand-corrected photos gave most of the extra gain; correcting the other 600 added nothing measurable.
4. **Added de-duplicated public photos** (+0.07 mAP@0.5 on top of the corrected photos).
5. **Three seeds averaged** (weighted box fusion): +0.05 mAP@0.5 on test.
6. Threshold chosen on validation only; test scored once; false alarms on clear skin measured (32 %).
What did **not** help: larger input (960 px) and larger model for hyperpigmentation, starting from other weights, a lower learning rate (looked like +0.028 on one seed but was luck across three seeds), a second round of model-drafted boxes on the same photos.
Diagnosis findings that mattered: public-dataset boxes were about 2x coarser than ours and dominated training; the validation set was in the public style; most public training photos had near-duplicates; our own boxing style drifted by about 50 % within one session.

**Acne (small discrete spots): test mAP@0.5 about 0.44 with 3 seeds averaged (0.36 on an independent set).** Lessons: one change at a time on the same validation photos with a pre-set rule (+0.02 mAP@0.5, precision and recall not lower by more than 0.02); more photos and reviewed labels gave only +0.01; 960 px gave about +0.015 on validation but did **not** hold on a fresh test set; a fresh untouched test set exposed that a development-test gain (+0.042) was not real; same-annotator agreement on tiny boxes was only F1 0.30 (IoU 0.5), so strict-overlap scores are capped; both detectors put boxes on a large share of clear-skin photos.

**Rules we followed:** training / validation / test never mix and near-duplicates are checked (32x32 thumbnails, correlation >= 0.90, 8 rotations and flips); decide the adoption rule before looking at results; confirm any gain with at least 3 seeds; report Ultralytics' own mAP separately from any custom scorer; never score the test more than once; state that scores under a changed box rule are not comparable with scores under the old rule.

## 2. What is probably different and important for eczema (check each one with data, do not assume)
- **Eczema patches are diffuse with soft, irregular borders** (redness, dryness, scaling, weeping), so box-level agreement is probably low, as it was for hyperpigmentation. **Hypothesis 1: the labels are not reproducible.** Test this first with a 30-photo blind re-box before changing the model.
- **Where the photo comes from:** many eczema photos in public sets are not facial (arms, hands, legs). The detector is meant for face photos, and the app keeps only boxes inside the detected face. Check how many training, validation and test photos are facial, and whether the validation and test sets match facial use. A facial-only dataset (and facial-only evaluation) may be the single biggest fix.
- **Box style per source:** compare box size, boxes per photo and how much of the photo the boxes cover for every data source (own boxing, teammates, Roboflow or other public data). Mixed styles hurt.
- **Skin tone:** redness is weaker on darker skin. Check whether the test photos cover several tones, avoid hue and saturation augmentation that removes the redness signal (acne colour-jitter test: milder jitter did not help, but check for eczema), and report results by tone group if tone labels exist.
- **Look-alikes and negatives:** the thesis objectives do not include a separate mimicker class, so do not add one. Do measure false alarms on clear-skin faces and on other conditions (acne, hyperpigmentation) so the false-positive limit is stated.
- **Diffuse patches may be better described as regions than as boxes.** If agreement stays low even under a clear rule, report zone-level (3x3 face zones) and photo-level results as the main claim and box-level as secondary, as we did for hyperpigmentation.
- **Data amount:** eczema may have far fewer facial photos than acne. Check class counts, duplicates and the size of validation and test sets; with few photos, confidence ranges are wide and the claim must be modest.
- **Model settings:** compare YOLOv8n/s at 640 vs 800 px only after the labels are checked. Our experience: label and data fixes gave most of the gain; size, learning rate and augmentation gave little.

## 3. The plan I want from you (give it to me first, then wait for my approval)
1. **Audit** (read-only): dataset structure and counts per split, image resolution, facial vs non-facial, duplicates and look-alikes across splits, label format and class IDs, box statistics per source and in the order of boxing (drift), current training settings, seeds, baseline results with a confidence range. Use `shared/lesion_diagnose_data.py` and `shared/lesion_diagnose_models.py` as templates.
2. **Reliability test:** prepare a folder with 30 validation photos without boxes (shuffled, renamed, blank) for a blind re-box; compare with F1 at IoU 0.3/0.5/0.7 (`hyperpigmentation/scripts/make_hyp_reliability_v2.py` and `hyp_reliability_v2.py` are templates). If agreement is below about 0.4, propose an eczema boxing rule (what counts as one patch, minimum size, how to treat scratch marks and eyelid/lip areas, where the edge goes) and test it on a day gap with the same 30 photos.
3. **Relabel plan:** evaluation labels from scratch and blind (new validation and test), training labels converted by code and corrected by hand in small steps (measure after the first 200 corrected photos before asking me to do more). Draft the folders for the box tool (`shared/lesion_box_tool.py`, `--rule` text).
4. **Experiment matrix** (validation only, one change per run, the same seed, the pre-set rule): facial-only data; own boxes only vs plus public photos; image size; then confirm winners with 3 seeds in a notebook.
5. **Final steps:** average 3 seeds (box fusion), choose the confidence threshold on validation, score a fresh untouched test set **once**, measure false alarms on clear-skin photos, then decide about installing only if it clearly beats the current model.
6. **Deliverables:** an audit report, the written boxing rule, a prioritized plan with time and compute cost for each step, comparison tables with seeds (individual scores, mean, standard deviation), the executed notebooks, and a plain-language explanation of what is confirmed and what is still a hypothesis.

## 4. Compatibility
The final detector must export to ONNX (opset 12, fixed image size) and install as `SkinDevApp/Model/lesion_detector_eczema.onnx` plus a `.json` (classes: ["eczema"], imgsz, nms_iou, default_conf) using `shared/install_specialist_detector.py`. The app's detector code, box format and threshold settings must not change without my approval. Do not modify the MobileNetV2 classifier, the acne or hyperpigmentation detectors, or any existing model weights.

Start with step 1 (the audit) and show me the findings before doing anything else.
