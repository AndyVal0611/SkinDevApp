# Panel study guide: the skin disease classifier and the YOLOv8 lesion detectors

Everything here comes from your own files (the executed classifier notebook `Pipeline V5 - With Facial Eczema`, the acne and hyperpigmentation records, `docs/RESULTS_FOR_PAPER.md`). Items marked **(check)** are things to confirm yourself before the defense.

---
## 1. The whole system in 60 seconds
PrecisionSkin is a research prototype: a kiosk app (WPF, .NET Framework 4.7.2, CPU-only mini PC, Logitech BRIO camera) that screens a face photo. Three parts work together:
1. **Classifier (MobileNetV2):** decides which of 4 classes the photo looks like: Acne, Hyperpigmentation, Eczema, Normal.
2. **Grad-CAM++ service:** shows which image regions influenced the classifier (explanation of the classifier, not a lesion map).
3. **YOLOv8 detectors:** draw candidate boxes where acne spots or hyperpigmentation patches are (eczema detector is trained by a teammate).

Results are stored in a local SQLite study database (participants, scans, researcher and dermatologist verification). It is a screening aid, **not a diagnosis and not a medical device.** The Fitzpatrick AI module and the mimicker logic were removed from the system and the objectives.

---
## 2. The classifier (MobileNetV2)

### 2.1 Facts
| Item | Value |
|---|---|
| Task | single-image, 4-class classification: Acne, Hyperpigmentation, Eczema, Normal |
| Data | 12,000 images, **3,000 per class**; split **8,793 train / 1,603 validation / 1,604 test** (split by duplicate group, so look-alike photos stay in the same split) |
| Run used | `facial_only` (non-facial-origin eczema photos, 1,633 images, kept out of training; eczema trains on 913 facial photos); natural class counts with **class weights** (eczema 1.96, others about 0.86); no oversampling |
| Model | **MobileNetV2**, ImageNet-pretrained, 224 x 224 input, then global average pooling, BatchNorm, Dropout 0.4, Dense 256 (L2 1e-4), BatchNorm, Dropout 0.4, Dense 128, BatchNorm, Dropout 0.4, Dense 4 softmax (float32) |
| Training | **Phase A:** backbone frozen, 30 epochs, learning rate 1e-3. **Phase B:** fine-tune from layer 100 on, 15 epochs, learning rate 1e-5. Batch 16. Early stopping / LR reduction on validation loss. **5 seeds (0 to 4)** |
| Augmentation | conservative: horizontal flip, brightness/contrast, small noise, coarse dropout, resize (training only) |
| Model choice | the seed with the best **validation** macro-F1 = **seed 1 (0.9272)**; the test set was touched once |
| Export | Keras -> **ONNX** (5.4 MB, 224 x 224, RGB, normalization `(pixel / 127.5) - 1`); Keras and ONNX agree on 48 test images (largest probability difference 0.0044) |
| Speed (GPU, measured) | ONNX inference about 20 ms; Grad-CAM++ about 300 ms. **CPU mini PC not measured for the classifier (check)** |

### 2.2 Results (test set, 1,604 photos, never used for choosing)
- **5 seeds:** accuracy **0.926 +/- 0.005**, balanced accuracy 0.924, macro-F1 **0.914 +/- 0.005**, kappa 0.900.
- **Selected seed (1):** accuracy 0.929, macro-F1 0.917. 95 % ranges: accuracy 0.916 to 0.941, macro-F1 0.902 to 0.931.
- **Per class (precision / recall / F1):** Acne 0.96 / 0.85 / 0.90; Hyperpigmentation 0.97 / 0.95 / 0.96; Eczema **0.74** / 0.93 / 0.82; Normal 0.98 / 0.98 / 0.98.
- **Calibration:** validation expected calibration error 0.012 (the confidence numbers are meaningful).
- **Leak-free check:** 488 test photos had a same-source sibling in training. On the other 1,116 photos: accuracy **0.909**, macro-F1 **0.912** (so the headline is slightly optimistic; quote both).

### 2.3 The main weakness (say it before they ask)
**Acne is often called eczema:** 59 to 63 of 465 acne photos (about 13.5 %) were predicted as eczema, which is why eczema precision is only 0.74 and acne recall 0.85. Many of these errors are confident (median confidence 0.77). The kiosk only shows a result above a confidence gate of **0.65**, which already filters about 25 % of those acne/eczema errors. A simulated "ambiguous" guard would catch only about 14 % of them and was not applied.

### 2.4 Likely questions and answers
- **Why MobileNetV2?** Small (5 MB ONNX), fast on a CPU-only kiosk, strong pretrained features, and enough accuracy. Larger models would be slower on the mini PC.
- **Why 4 classes and no mimickers?** The approved objectives cover acne, eczema, hyperpigmentation and a normal class; mimicker logic was removed from the objectives.
- **How do you know it isn't memorizing duplicates?** The split is by duplicate group; a leak-free re-evaluation (no same-source sibling in training) still gives accuracy 0.909 and macro-F1 0.912.
- **Why train on facial photos only?** The kiosk takes face photos. Adding non-facial eczema photos changes the domain; the `facial_only` run is the deployed one.
- **Why 5 seeds and why is the test used once?** To show the result is not one lucky run, and to avoid choosing the model on the test set. The seed was chosen on validation.
- **Why is eczema precision low?** Acne photos get mislabeled as eczema (shared redness features); class weights raise eczema's recall (0.93) at the cost of precision.
- **What does Grad-CAM++ show?** The regions that influenced the classifier's score. It is **attribution, not localization**: the classifier makes a global image-level decision, so highlighted regions need not be the diseased skin. The per-class maps are very similar (correlation 0.95 to 0.99).
- **Skin tones?** The classifier was **not** evaluated per skin tone (no Fitzpatrick labels in the data). A Fitzpatrick AI model was tried and discontinued because it learned facial/ethnic appearance, not skin phototype. A Monk Skin Tone model is set up but not trained (data not collected). Do **not** claim validation across skin tones.
- **Does it work on live camera frames?** Only informal checks. The training photos are a mix of close-ups, crops and full faces from public data; kiosk frames are mostly full faces (a domain gap). A participant pilot with dermatologist verification is the real test.

---
## 3. The YOLOv8 lesion detectors

### 3.1 What they are
Two **one-class YOLOv8s** detectors (acne and hyperpigmentation), official Ultralytics implementation, COCO-pretrained `yolov8s.pt`, 800 px input, up to 100 epochs, batch 8, AdamW, seeds 42 / 7 / 123, early stopping after 30 epochs without improvement. They draw **candidate boxes**; a box is not a diagnosis and not a lesion count. In the app each condition runs **three models** (one per seed) and their boxes are averaged (weighted box fusion); boxes outside the detected face are dropped.

### 3.2 Numbers to know
| | Acne | Hyperpigmentation |
|---|---|---|
| Training data | 1,145 photos, 11,621 boxes (hand-boxed, one box per spot) | 808 photos corrected by hand under box rule v2 (x3) + 1,260 public photos |
| Validation | 154 photos | 60 photos |
| Test | development test 167 photos; independent set 41 photos; fresh test 100 photos (36 with acne, 64 without) | 100 photos boxed from scratch under rule v2 |
| **mAP@0.5** | dev test **0.439** (3-model average); independent set 0.358 (single model); **fresh test 0.336** | **0.508** |
| mAP@0.5:0.95 | 0.127 (dev test) | 0.209 |
| F1 | 0.507 (dev test); 0.431 (fresh test) | 0.520 (range 0.44 to 0.59) |
| Other | face-zone F1 0.83, photo AUC 0.96, count error about 4 lesions | face-zone F1 0.61 |
| False alarms on clear skin | about 39 % of no-acne photos get a box | about 32 % |
| Installed threshold | 0.275 | 0.225 |
Previous detector (the old 3-class one): acne mAP@0.5 0.22.

### 3.3 Why the numbers are what they are
- **Acne spots are tiny** (about 28 px). A 4-pixel shift already drops the overlap measure a lot, so strict scores (mAP@0.5:0.95) stay low. **Your own boxes agree with themselves only at F1 0.30 (IoU 0.5)**, so the labels cap the score; the model agrees with your original boxes better (0.48).
- **Hyperpigmentation patches have soft edges.** The first rule was not reproducible (F1 0.28). A stricter written rule (rule v2) raised agreement to about 0.49 to 0.55 a day apart, and the model was trained and tested under it. **Scores under rule v2 are not comparable with the old ones** (boxes are about twice as large).
- **What improved the results:** hand-corrected labels under a clear rule, extra public photos, and averaging three seeds (+0.05 on hyperpigmentation, +0.02 on acne). What did **not** help: bigger input (960 px) and bigger model, other starting weights, more draft rounds.
- **Selection was honest:** choices and thresholds on validation only; changes adopted only with a rule fixed in advance (+0.02 mAP@0.5, precision and recall not worse); a **fresh test set** was made because the development test had influenced earlier decisions, and it showed that a 960 px candidate's development-test gain did not hold, so it was not installed.

### 3.4 Likely questions and answers
- **Why YOLOv8 and not Faster R-CNN or others?** One-stage, fast on CPU after ONNX export, simple to train, and the official implementation is well documented.
- **Why the small model (YOLOv8s)?** The kiosk is CPU-only; a medium model gave no better score (acne 0.426 vs 0.425).
- **What does mAP@0.5 mean?** The share of true lesions found with a box that overlaps the true box by at least 50 %, averaged over confidence levels. mAP@0.5:0.95 requires tighter overlaps and is always lower, especially for tiny objects.
- **Why is your acne mAP only 0.44?** Tiny spots, dense photos, and label noise: you agree with your own earlier boxes at F1 0.30. The detector is a candidate marker, not a counter.
- **Why 0.508 for hyperpigmentation but 0.336 for acne?** Different tasks and different labeling rules. Hyperpigmentation uses large whole-patch boxes under a reproducible rule, which are easier to match than tiny acne spots.
- **Why three models per condition?** Different random starts miss different spots; boxes that several agree on are more reliable. It costs about half a second per model per scan.
- **Is the test set independent?** For hyperpigmentation: the 100 test photos were never used for training or choosing and were scored once. For acne: the development test influenced early decisions (say so), the independent set (41 photos) was scored once, and a fresh set (100 photos) was made and scored once.
- **Do the detectors prove disease?** No. They propose candidate locations. The classifier and the dermatologist's verification are separate.
- **What if the classifier and the detector disagree?** The screening result is the classifier's (above the 0.65 gate); the boxes are supplementary. Both are research aids.
- **Where did the data come from?** Your own boxing (box tool logs show about 12 hours for acne and 6 hours for hyperpigmentation), plus public Roboflow Universe sources (CC BY 4.0 as stated in each dataset) for extra training photos.
- **Who did the work?** You boxed the data and ran the final acne training; an AI coding assistant helped write the code and ran many experiments on your computer at your request. Say this plainly (see `shared/notebooks/PrecisionSkin_Training_Evidence_Acne_and_Hyperpigmentation_WITH_OUTPUTS.ipynb`).
- **How fast is it?** A detector takes about 0.5 to 1 s per model on a laptop CPU, so about 3 s for all six models. **Mini PC speed is not measured yet (check).**

---
## 4. Things not to say
- "The system is clinically accurate / diagnoses disease." It is a research prototype.
- "Grad-CAM++ shows where the disease is." It shows what influenced the classifier.
- "The model works on all skin tones." It was not evaluated per tone.
- "0.508 is comparable to published detectors." It is under your own box rule on your own test photos.
- "I trained everything myself." State what you did and what the assistant did.

## 5. Limits to state first (best defense is saying them before they ask)
One annotator; modest detector scores for tiny acne spots; box-rule dependence for hyperpigmentation; the classifier confuses acne with eczema; false alarms on clear skin (about 30 to 40 %); no per-skin-tone evaluation; domain gap between public photos and kiosk frames; mini-PC speed not yet measured; eczema detector still in progress by a teammate.

## 6. Where to point when they ask for proof
- Classifier: `Pipeline  V5 - With Facial Eczema.ipynb` (executed, 47 cells, run in order) and `runs/facial_only/` (checkpoint audit, calibration report, ONNX report).
- Acne final training: `acne/evidence/Pipeline V5 - Lesion Detector__EXECUTED_by_researcher_2026-10-10.ipynb`.
- All detector records: the evidence notebook above, `lesion_experiments/EXPERIMENTS.csv`, `docs/RESULTS_FOR_PAPER.md`, `docs/TRAINING_EXPLAINED_FOR_DEFENSE.md`.
