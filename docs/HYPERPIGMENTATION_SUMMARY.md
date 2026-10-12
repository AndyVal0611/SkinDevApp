# Hyperpigmentation detector: what we did (as of 2026-10-11)

## 1. Goal
A one-class YOLOv8 detector that boxes hyperpigmentation patches (melasma, PIH, dark spots) on face photos, to replace the
class-1 boxes of the old 3-class v1 detector. Held to the same honest protocol as the acne detector.

## 2. Where we started
- Installed model: `s_800` (YOLOv8s, 800 px) trained on public photos plus your boxes. Scored **0.178 mAP@0.5** on your hand-boxed test photos.
- The old validation set was in the public-dataset style, so its scores looked better than the model really was.

## 3. Diagnosis (before any retraining)
- **Public boxes are about 2x coarser than yours** (coverage 18 % vs 9 % of the photo), and they dominated training.
- **2,524 of 3,031 public training photos have a near-duplicate**, which inflates scores.
- **Your boxing drifted inside one ~2 hour session**: median box side 0.080 -> 0.125 (close-ups 0.108 -> 0.184).
- **42 sprint photos** were over-split (37) or over-merged (5) and were queued for a second look.
- An earlier size comparison (n_640 / m_800) was invalid: those runs stopped after 7 and 9 epochs.

## 4. How the boxes were made
1. **Sprint (by hand).** You boxed photos yourself with the box tool (`lesion_box_tool.py`): one box per patch.
   This is the "own" set, 614 training photos, used twice in each epoch (x2).
2. **Validation and test (by hand, never touched).** 60 validation photos (217 boxes), the real validation set,
   and a separate hand-boxed test set (about 100 photos). Their labels were never edited.
3. **Automatic drafts, round 1.** A helper YOLOv8s trained only on your boxes drew drafts on 300 unboxed photos
   (clean, distinct pool, at least 500 px on the short side). You reviewed them: delete wrong boxes, add missed patches,
   S to save, X for no patch. 288 were saved (281 boxed, 7 X). You kept about 99 % of the drafted boxes.
4. **Automatic drafts, round 2.** The better H6 model drew 300 more (confidence 0.20, mean 2.4 boxes per photo).
   You saved 230 of the 312 open drafts (518 reviewed in total: 501 boxed, 17 X). 82 are unsaved and unused.
5. **Consistency fix.** The 42 photos were reopened with your boxes preloaded and saved (23 of them unchanged).
   Result: 24 photos still have 10 or more boxes, so many of them probably have genuinely many small spots.
6. **Not done yet.** 60 independent hyperpigmentation photos for a one-time final score (0 boxed).

Boxing rule used: one box per patch; don't split one patch into pieces, don't merge separate patches.

## 5. Experiments (seed 42, validation = your 60 hand-boxed photos)
| Run | Training data / change | mAP@0.5 | mAP@0.5-0.95 | P | R | F1 |
|---|---|---|---|---|---|---|
| H0 | public boxes + yours (old recipe) | 0.232 | 0.067 | 0.25 | 0.33 | 0.28 |
| H1 | your boxes only | 0.267 | 0.084 | 0.30 | 0.39 | 0.34 |
| H6 | + 281 reviewed drafts | 0.298 | 0.092 | 0.33 | 0.37 | 0.35 |
| H7 | + only drafts in your test style | 0.282 | 0.085 | 0.38 | 0.32 | 0.35 |
| H8 | + 518 reviewed drafts (round 2) | 0.296 | 0.093 | 0.33 | 0.37 | 0.35 |
| H9 | H8 + 960 px | 0.308 | 0.091 | 0.44 | 0.32 | 0.37 |
| **H10** | **H8 + lower learning rate (lr0 0.0005, AdamW)** | **0.324** | 0.094 | 0.36 | 0.37 | 0.37 |
| H11 | H8 + YOLOv8m | running | | | | |

Rule for accepting a change: mAP@0.5 up by at least 0.02, and neither precision nor recall down by more than 0.02.

## 6. What we learned
- The public photos hurt on your boxing style (H0 scores 0.335 on public-style validation but 0.232 on yours).
- Your own boxes plus reviewed drafts beat the old recipe by about +0.07 mAP@0.5.
- A second round of drafts added no gain (H8 = H6). More data of the same kind did not help.
- A lower learning rate gave +0.028 and passes the rule. 960 px does not.
- Everything is single-seed, with a 60-photo validation set. A difference of 0.02 is close to noise.

## 7. Honest status
- Best so far about 0.32 mAP@0.5. The 0.40 target has not been reached and no lever found so far is big enough.
- Wording to defend: box-level mAP about 0.32, with region-level and photo-level results as the main reliability claim.
- The 0.92 mAP in the ETASR 2026 paper (HAM10000, dermoscopy) is not comparable: no stated box protocol, split or leak check.

## 8. Next
1. H11 (bigger model) finishes.
2. Run the winner with 3 seeds (42, 7, 123), pick the seed on validation only.
3. Photo-level and region-level metrics, the same as for acne.
4. Install the new hyperpigmentation ONNX only if it beats the current model, with a validation-chosen threshold.
5. Optional: box the 60 independent photos for a once-only final score.

## 9. Rules we kept
Validation and test labels are never edited. Test sets are scored once with a frozen protocol. Nothing is deleted without
approval (archive folders instead). Participant photos are not uploaded or committed.
