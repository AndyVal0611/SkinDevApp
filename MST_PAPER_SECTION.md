# Paper-ready text: Monk Skin Tone (MST) datasets and the separate skin-tone model

Check every figure and the licence terms against the original sources before submitting (see the notes at the end).

## Datasets

### 1. Monk Skin Tone Examples (MST-E)

The Monk Skin Tone Examples (MST-E) dataset [cite the MST-E paper] will be used as a reference for developing the separate skin-tone classification model. It contains photographs of 19 people spanning the ten shades of the Monk Skin Tone (MST) scale, with the shade of each person reviewed and approved by the scale's creator. It was released to help practitioners understand the scale and to train human annotators, and it will help the researchers apply one consistent basis for labelling facial images. Because the dataset contains multiple images of a small number of individuals, it will serve as a reference and supplementary resource rather than the sole training dataset, and every split will keep all images of one person together.

### 2. Skin Condition Image Network (SCIN)

The Skin Condition Image Network (SCIN) dataset [cite the SCIN paper / dataset release] will be used as a supplementary source of skin images with Monk Skin Tone annotations. SCIN is a crowd-contributed collection of images of skin, nail and hair conditions; its Monk Skin Tone labels are layperson **estimates** rather than clinical measurements. Because some images show body parts rather than faces, the dataset will be filtered (head and neck cases only, one image per case) and quality-checked before any image is used for training, and the estimated labels will be reviewed to reduce labelling inconsistencies.

### 3. Purpose in PrecisionSkin

Both datasets support an independent skin-tone classification model based on the MST scale. The model operates separately from the MobileNetV2 condition classifier (Acne, Eczema, Hyperpigmentation, Normal Skin) and the lesion detector; its predictions do not influence the classification of dermatological conditions or the localization of affected areas. It provides supplementary research information. To evaluate the condition classifier across skin-tone groups, tone labels assigned independently by trained raters or a dermatologist (recorded in the system as a Monk Skin Tone value with its source) will be used, not the model's own predictions, until the model has been validated.

## Evaluation (to be reported with the results)

Skin tone is subjective even for trained raters, so the model will be reported as exact-shade accuracy, accuracy within one shade, and accuracy over three bands (MST 1-3, 4-6, 7-10), with a 95 % range obtained by resampling people (not photographs), results per source dataset, and a lighting stress test (±25 % brightness). A small set of images from the study's own acquisition setup, rated by two raters against the MST card, will be used to check how well the model transfers to the ring-light capture conditions.

## Why Fitzpatrick was not used (methods note)

An earlier Fitzpatrick-type model was discontinued: its six training folders followed apparent ethnicity groups rather than measured skin tone (Types I and II could not be told apart, AUC 0.48), so the model learned facial appearance, not phototype. The MST approach uses labels assigned by raters per image or per person, avoids folder-based proxy labels, and keeps people out of more than one split.

## Notes to verify before citing

- MST-E: 19 subjects, 1,515 images and 31 videos (NeurIPS 2023 Datasets and Benchmarks paper "Consensus and Subjectivity of Skin Tone Annotation for ML Fairness"); read the data card / skintone.google for the licence and permitted uses.
- SCIN: more than 10,000 contributed images; layperson-estimated Monk and dermatologist-estimated Fitzpatrick labels; read the licence in the official repository (a mirror lists CC BY 4.0, unverified); the repository lists a few duplicate images and cases without a condition label.
- Add both datasets and the Monk Skin Tone scale itself to the references (use the official citations, not this text).
