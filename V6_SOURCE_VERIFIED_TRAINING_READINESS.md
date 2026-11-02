# V6 source-verified training preparation

Reviewed 2026-11-01. **The curated dataset is preserved, but no image is yet cleared by substantiated, image-linked AI-training permission.** This is an evidence gap, not a determination that every image is unusable. No data, annotation drafts, V5 models, architecture or frozen split was changed. No training or performance evaluation ran.

## Current inventory

Within the unchanged 9,789-image frozen V6 dataset:

- **Acne:** 3,000 images; **0 permission-verified**, **3,000 unresolved**.
- **Eczema:** 789 images; **0 permission-verified**, **789 unresolved**.
- **Hyperpigmentation:** 3,000 images; **0 permission-verified**, **3,000 unresolved**.
- **Normal:** 3,000 images; **0 permission-verified**, **3,000 unresolved**.

The broader facial catalog has 10,063 rows including 274 duplicate rows excluded from frozen V6. The working image folders contain 21,893 files; extras are not automatically eligible or independent. Counts above refer to frozen images and do not double-count copies or claim unique patients. No individual image has sufficiently established origin to be conclusively assigned a restricted-rights category in this investigation; that count of zero does **not** mean that no restrictions exist.

## Evidence organized for maximum legitimate reuse

The new `v6/audit_results/rights_review/` folder separates **manifest references**, without copying or moving photographs:

- `image_rights_inventory.csv`: all 10,063 catalog rows, with frozen membership, source key, paths, export associations, rights status and hold reason.
- `unresolved_frozen_images.csv`: all 9,789 frozen images awaiting evidence.
- `verified_training_images.csv`: header-only, because no substantiated approvals were found.
- `confirmed_restricted_images.csv`: header-only; suspected restrictions remain review holds until origin is established.
- `possible_dermnet_review.csv`: possible DermNet catalog images, with filename/hash/group clues.
- `condition_rights_counts.csv`: per-condition rights and suspected DermNet counts.
- `permission_evidence_template.csv`: an empty evidence form for actual owner/source records; no grants or consent values invented.
- `input_integrity.json`: matching before/after SHA-256 fingerprints for the historical manifest, frozen split, live provenance registry, and both live annotation files.

The existing exact-image source mappings remain available in `original_export_links.csv` and `frozen_export_annotation_candidates.csv` under `v6/audit_results/`. They associate **304 frozen hyperpigmentation images** with a documented export and **286 frozen eczema images** with a corrected annotation collection. These are the strongest starting points for recovering rights and retaining annotation work. Shared files establish an association, not original ownership or the direction of copying. Local export READMEs declaring CC BY 4.0 have been retained as evidence; no blanket approval was inferred from them.

All 12 live provenance rows still have blank permission fields. The Kaggle collection is an intermediary, and `.rf.` is an export clue. Neither establishes rights over an upstream medical website's photographs. The inspected engineering manual mentions DermNet via a Kaggle compilation, but it is a planning document rather than proof of acquisition for a particular image.

## DermNet verification hold

The frozen inventory has **three Eczema images with explicit DermNet filename clues**; the complete working folders contain **four** such files. Hash/group propagation did not add further frozen candidates. The filenames, image IDs and paths are recorded in `possible_dermnet_review.csv`. This is a conservative discovery list, not a complete detector: renamed, cropped or augmented website images may have no DermNet text. Zero flags for the other conditions must not be interpreted as clearance. `all_disk_dermnet_filename_candidates.csv` separately records explicit filename clues across the full working folders.

DermNet's current [image licence, clause 25](https://dermnetnz.org/image-licence) prohibits AI training and testing with freely available website images, including validation; a separately licensed AI dataset is offered. Educational display permission does not establish AI-use permission. For a suspected image, recover its original page and acquisition history and check for a separate applicable AI licence or written grant. A downstream Kaggle or Roboflow declaration does not, by itself, resolve that issue. Do not remove watermarks, infer permission, or delete the held images as part of this audit. Terms checked 2026-11-01; no claim is made about an image's historical contractual status.

## Can the verified subset train V6?

**No.** There are zero cleared images in every split, and zero accepted clinical annotations. Current gates require 300 fully decided train images, with at least 40 Present and 40 Absent per condition; 60 verified regions per each of four ROI classes; and valid proposal matches producing all four classes and at least 100 ROI training rows. Validation requires 150 decided images, fusion 250, and test 250, with their existing per-condition/region minima. Training permission is needed for validation selection and fusion/calibration too. Test-only permission cannot be used for those learning steps.

Resolving rights alone does not establish dermatologist labels or independent evaluation. Previously identified historical exposure remains relevant: only 192 frozen test candidates avoid the inspected V5/YOLO groups, below the 250-image test minimum and still not proven patient-independent.

## Proposed retention, exclusion and replacement plan — not implemented

1. **Recover grants before discarding curation.** Ask the assembler for upstream owner/project/version records and original terms, starting with the exact-linked hyperpigmentation and corrected-eczema batches. Record image IDs, parent/augmentation links, derivative use, AI training/validation/testing scope, consent basis, face handling, retention and responsible review. A valid applicable existing licence can be evidence; a new letter is needed only where existing evidence does not establish the required scope.
2. **Clear only the evidenced scope.** Preserve current source keys. If only some images in a mixed group are cleared, do not mark the whole group `research_training`. Prepare a per-image eligibility proposal for review; the present pipeline blocks an affected split rather than silently selecting a subset. No gate change or per-image filtering was implemented here.
3. **Keep unresolved/restricted candidates on a manifest hold.** Do not use them for fitting, calibration, validation or final evaluation. Preserve images and annotation drafts so they can be reused if adequate rights are established. If a restriction is confirmed and no applicable permission is obtained, propose exclusion of the image and derivative family from the approved run; do not physically delete it or modify frozen membership automatically.
4. **Audit existing extras before collecting replacements.** Check the extra curated files for rights, duplicates, patient/source grouping and prior model exposure. If these cannot fill the quotas, propose a small rights-cleared acquisition or consented collection. Submit exact proposed IDs, counts, exposure effects and a new versioned split/eligibility plan for approval before implementation. Protect independent test cases from model selection.
5. **Begin dermatologist work with a cleared pilot batch.** Supply only images covered by the approved evidence, blinded to AI outputs. Reuse drafts only after independent image-level findings, recording their origin and review. Retain Present/Absent/Unknown, simultaneous conditions, genuine negatives and separate dermatologist records. No absent labels are inferred from missing boxes.

## Single next action

**Have the dataset assembler and responsible investigator substantiate AI-use rights for one exact-ID pilot batch—starting with the documented hyperpigmentation export—and record the applicable owner/licence, upstream provenance and consent basis in `permission_evidence_template.csv`.** Once that batch is approved for the intended research use, it can enter blinded dermatologist annotation; the rest of the curated collection stays preserved on hold.
