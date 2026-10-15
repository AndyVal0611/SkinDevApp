# Acne detector: annotation audit, protocol and experiment plan (2026-10-11)

Read-only audit. No label, image, dataset split or model weight was changed. Script: `acne_annotation_audit.py`.
Full tables, `review_queue.csv` and 40 overlay pictures: `Downloads\PrecisionSkin\Acne_Boxing\_annotation_audit\`.
Model audited: final seed 123, confidence 0.242. Localisation analysis = the 154 validation photos only.

## 1. Bottom line
The hypothesis "mixed individual-pimple and cluster boxes are the main reason mAP@0.5:0.95 is low" is **not supported by the files**.
Cluster-style boxes are almost absent. The data points to three other causes, in this order of evidence:
1. **Small lesions and dense photos** are where the model fails (confirmed).
2. **Boxes are only about 28 px wide**, so small errors cost a lot of IoU (confirmed, computed).
3. **Missing labels** in training photos (confirmed as a flag, size of the effect still to be tested).

## 2. Confirmed from the data
| Finding | Evidence |
|---|---|
| Boxes are individual lesions, not clusters | Largest box is 0.274 of the photo; 90 % of boxes are below 0.104. Only 12 of 1,299 train+val photos (0.9 %) have a box more than 2.5x the photo's median. Only 2 photos (0.2 %) have a box sitting on top of 2+ other boxes. 0 exact duplicates; 6 photos with heavy overlap. |
| Box shape is consistent | Median aspect ratio 1.01 to 1.03; only 1.6-2.8 % are very elongated. |
| Boxes are small | Median box 28 px (train), 24 px (val), 22 px (test), 17 px (independent). |
| Strict IoU collapses | On validation, F1 is 0.49 at IoU 0.5, 0.36 at 0.6, 0.20 at 0.7, 0.12 at 0.75, 0.05 at 0.8. Median IoU of matched boxes 0.62. |
| Many are found but loose | 294 boxes match at IoU 0.1-0.5, against 761 at 0.5 or more. |
| Not duplicates, not a size bias | 0 duplicate predictions out of 1,597. Model boxes are 1.12x the size of yours, centre offset 0.13 of the box side. |
| Small lesions are the weak spot | Recall at IoU 0.5: small 0.31, medium 0.57, large 0.57. |
| Dense photos are the weak spot | Recall: sparse (6 or fewer) 0.61, medium 0.65, dense (15 or more) **0.39**. Dense photos hold 57 % of all validation boxes. |
| Possible missing labels | 142 photos (10.9 %) have 2+ confident model boxes with no box of yours near them (111 train, 31 val; 405 boxes in the 111 train photos). |
| Label-noise ceiling | A 28 px box drawn 1 / 2 / 3 px off gives IoU 0.93 / 0.87 / 0.81. So tiny boxes cap mAP@0.5:0.95 even with two careful annotators. |

## 3. Hypotheses (not yet tested)
- Part of the 294 "loose" matches is annotator variation on fuzzy spot edges, not model error. Needs a second annotator on about 30 photos.
- Fixing missing labels in train photos raises recall in dense photos. Earlier test on 60 relabelled photos (A7): mAP@0.5 +0.013 and recall +0.05 on validation, but photo AUC fell 0.966 to 0.951 and specificity dropped. So it is a trade-off, not a free gain.
- Higher resolution helps small lesions. Acne A5 (960 px) gave +0.017 on validation, single seed.
- Cluster boxes are too rare (2 photos) to justify a second class. Keep one class, `acne`.

## 4. Written annotation protocol (acne)
1. **One box per visible lesion** (papule, pustule, nodule, comedone that is clearly raised or discoloured). Box = the lesion plus a thin margin, not the surrounding redness.
2. **Touching lesions:** if you can see two separate centres, draw two boxes. Boxes may overlap but not by more than about 30 % IoU.
3. **Cluster box only when** individual lesions cannot be told apart even when zoomed in. Mark these photos in a list. Do not add a cluster box over lesions that already have individual boxes.
4. **Do not box:** scars, flat marks, freckles, moles, hair, shadows, or anything you would not call a lesion.
5. **Small spots:** box them if a person would count them at normal viewing size. Skip anything under about 8 px.
6. **Dense areas:** go region by region (cheek, forehead, chin) and count each region once. Stop when you cannot separate lesions any more and note the photo as "dense, partial".
7. **Photos without lesions:** save as X (empty). Keep at least 20 percent of the review set as X photos.
8. **Do not edit validation, development-test or independent labels.** Corrections apply to training photos only.

One class is appropriate because clusters are 0.2 % of the photos and the app counts lesions. If dense photos later prove unresolvable, report them as "dense, partial" rather than add a class.

## 5. Review queue (for you, nothing applied automatically)
| Priority | Photos | What to check |
|---|---|---|
| 1 | 10 train photos OVERSIZED + 2 SWALLOWS + 6 HEAVY_OVERLAP | Cluster-style or wrong boxes. Decide split / keep / fix per the protocol. About 18 photos, quick. |
| 2 | 111 train photos POSSIBLE_MISSING | Model sees something you did not box. Add only real lesions. The 40 pictures in `overlays/` start with the worst. Do the 40 with the most flagged boxes first. |
| (excluded) | 31 val + 2 val OVERSIZED photos | Not edited. Validation labels stay fixed. They are only used to measure the effect. |

Original label files stay unchanged. Reviewed copies are saved separately.

## 6. Controlled experiment plan (validation only, seed 42, one change at a time)
| ID | Change | Why |
|---|---|---|
| B0 | Baseline = final recipe (A0: mAP@0.5 0.4254, F1 0.494) | reference |
| B1 | Reviewed training labels (the priority 1+2 corrections) | tests the annotation-consistency hypothesis |
| B2 | B1 + 960 px | small lesions |
| B3 | B1 + lower learning rate | overfitting after best epoch |
| B4 | B1 + box-loss 10 | tighter boxes, targets mAP@0.5:0.95 |
Accept a change if mAP@0.5 rises by 0.02 or more, neither precision nor recall drops by more than 0.02, and photo AUC and zone F1 do not fall. Promising settings get 3 seeds (42, 7, 123): mean, standard deviation and each seed.
The test and independent sets are not used. A final confirmation needs a NEW independent set, including at least 15 acne-negative photos (the old one had only 2, both wrongly flagged).

## 7. Counting and deployment
- Counting: there are no cluster boxes, so counting each box as one lesion stays valid. The count error (MAE about 4) is mostly missed small spots in dense photos, so counts will under-estimate in dense photos. A cluster box, if ever added, must not count as 1 lesion.
- Deployment: any new model is exported as a separate version, checked against PyTorch, and the app keeps the current ONNX until a new one wins on validation.

## 8. Expected size of the gain
mAP@0.5:0.95 is capped by tiny boxes. I expect a few points of mAP@0.5 at best (A7 and A5 each gave about +0.013 to +0.017 alone). I do not expect a jump to 0.5. No guarantee.
