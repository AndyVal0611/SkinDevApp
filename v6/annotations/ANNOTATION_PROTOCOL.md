# V6 annotation protocol (give this page to the dermatologist / reviewers)

The CSV files in this folder are **templates with headers only**. The notebook creates the real, fully populated templates (every image listed, all labels blank = *Unknown*) in `~/PrecisionSkinV1/v6/annotations/` the first time you run Section 11. **Existing annotation files are never overwritten.** Nothing in the notebook fills a label from a model.

## 1. Image-level labels — `multilabel_annotations.csv`

| Column | Allowed values | Meaning |
|---|---|---|
| `image_id` | as supplied | join key (not personally identifying) |
| `split`, `review_rank` | supplied | frozen split; a random order inside each split — **any prefix of the list is an unbiased random sample**. Annotate `validation`, `fusion`, `test` first, then `train` |
| `acne`, `hyperpigmentation`, `eczema` | `present` / `absent` / `unknown` | decide each condition **independently** |
| `review_status` | `verified` · `partial` · `unverified` · `disputed` | `verified` = all three decided; `partial` = at least one decided and at least one `unknown`; `unverified` = not reviewed; `disputed` = reviewers disagree |
| `reviewer_id` | a pseudonymous code | never a name |
| `reviewer_role` | `dermatologist` / `researcher` | evaluation labels are accepted from **dermatologists only** (configurable) |
| `blinded_to_ai` | `yes` / `no` | `yes` = the reviewer did not see any model output (required for a defensible reference standard) |
| `reviewed_at`, `image_origin`, `skin_tone_group`, `notes` | free / `source_dataset` or `camera_capture` / Fitzpatrick or Monk code if known | `image_origin` keeps camera and source-dataset performance apart |

**Normal is not a label.** A photograph is *Normal* when all three conditions are `absent` and the image is adequate. If the image is not adequate to judge a condition, use `unknown` — **never** `absent`. *Unknown* is not negative anywhere in the notebook. The V5 class name is *not* a label: it is used only as a weak positive during training and never for evaluation.

## 2. Region labels — `region_annotations.csv`
Boxes on the **original** photograph in **normalised coordinates** (0–1 of the original width/height): `x0,y0,x1,y1` with `x1>x0`, `y1>y0`. `label`: `acne` · `hyperpigmentation` · `eczema` · `background_false_positive` (a *reviewed* candidate box that is **not** a lesion — this is how verified hard negatives enter ROI training). `exhaustive = yes` when the reviewer boxed **all** regions of that label in that image (needed for precision/recall/mAP); leave blank otherwise. `annotation_source`: `manual_box` or `yolo_proposal_reviewed`. Region classes are **never** inferred from YOLO's predicted class.

## 3. Hard-example review queue — `hard_example_review_queue.csv`
The notebook appends suspicious images (label/filename conflicts, image-type mismatches, confident model disagreement, strong second condition, YOLO flags) with `reason_codes`. The reviewer fills `reviewer_decision` (`label_ok`, `label_wrong`, `mixed_conditions`, `unusable`, …), `reviewer_id`, `reviewed_at`. **A queue entry never changes a label by itself**; transfer decisions into the annotation file manually.

## 4. Provenance — `dataset_provenance.csv`
One row per `source_key` (`<class>/<origin>`): description, licence/terms, consent basis, `permissible_use` (`research_training` · `evaluation_only` · `not_permitted`), whether faces are identifiable, retention policy, responsible person. Training is blocked until every source is described.

## 5. Quality of the reference standard (recommended for Chapter III)
Re-read a random 10 % blind and report Cohen's κ per condition; record disagreements as `disputed`. Annotate images from the real camera with `image_origin = camera_capture`. Minimum counts per split are configurable (`MIN_VERIFIED`, `MIN_POSITIVES`, `MIN_NEGATIVES`, `MIN_REGION_PER_CLASS`) and **are provisional power-based choices, not guarantees** — larger verified sets give narrower confidence intervals.
