# V6 dataset preparation action plan

Prepared 2026-11-01, Asia/Manila. This investigation reads existing files and produces documentation only. No training, performance evaluation, image edits, annotation edits, model replacement, source-permission edits or frozen-split changes were made. The two-stage region-guided CNN–YOLO fusion and mandatory dermatologist review remain unchanged.

## 1. Dataset inventory — confirmed findings

The original-image working location is `C:/Users/Bautista/Downloads/PrecisionSkin/Classes/<condition>/`. A recursive file inventory found **21,893 image files**, not 21,893 independent patients:

- Acne: **5,725** on disk; **3,000** in the facial catalog and frozen V6 dataset.
- Eczema: **3,566** on disk; **1,063** in the facial catalog; **789** in frozen V6 after duplicate removal.
- Hyperpigmentation: **6,422** on disk; **3,000** cataloged/frozen.
- Normal: **6,180** on disk; **3,000** cataloged/frozen.

The historical manifest contains 12,000 rows (3,000 per source-folder class). Facial-use/QA filtering leaves 10,063; excluding 274 exact duplicate rows leaves **9,789 frozen images**. The extra files on disk have not automatically been accepted, deduplicated, licensed or shown independent of previous model development. Do not add them to a split without a documented audit and approval.

Processed images are under `/home/bautista/PrecisionSkinV1/dataset_processed/processed/`; the historical manifest is `metadata/metadata/split_manifest.csv`; frozen V6 IDs/groups are in `v6/splits/v6_split_manifest.csv` under that project root. Image-level annotation CSVs use those frozen IDs, not arbitrary file names.

Other inspected holdings: `FINAL DATASET/Acne` has 6,697 images, and `Final Dataset V1/Acne` has 2,845. These are separate holdings, not additional unique usable cases. `PrecisionSkin_Raw/{Acne,Eczema,Hyperpigmentation}` contains Fitzpatrick17k-named directories but **zero supported image files** in this scan. A folder name is not evidence that the corresponding clinical dataset supplied V6 images. No original provider was confirmed for Normal.

The dataset-engineering notebook `Downloads/dataset-v3.ipynb` documents downsampling to at most 3,000 images per folder class. Its Kaggle mount matches the historical manifest. It was inspected as text and was not executed.

`Downloads/PrecisionSkin.zip` was inspected by reading its central directory only: 57,327 entries and no filenames identifying a README, licence, consent or provenance record. No files were extracted. This does not prove that source evidence cannot exist elsewhere. The checked Windows download-origin stream did not supply a usable source URL.

## 2. Source investigation — confirmed evidence and missing information

All 12,000 historical manifest rows say `source=local` and point through `/kaggle/input/datasets/elaizaclaire/precisionskin/PrecisionSkin/Classes/`. This confirms an intermediate Kaggle collection, not its original image owners or rights. The original source URL is inferable as https://www.kaggle.com/datasets/elaizaclaire/precisionskin ; the page could not be verified through the web tool. No authoritative image-linked Kaggle licence or consent record was established.

The live registry remains `/home/bautista/PrecisionSkinV1/v6/annotations/dataset_provenance.csv`. Its 12 rows still have blank permission/evidence fields and unknown identifiable-face status. **No source group is currently cleared for training or evaluation by the strengthened policy.**

The source keys are filename categories, not actual providers. `aug_` suggests a derivative; `.rf.` suggests Roboflow processing. Neither proves the original provider, the transformation recipe, medical correctness or permission.

### All 12 groups

- **Acne/aug**: 555 catalog / 555 frozen. No exact association established with inspected source exports. Recover parent-image mapping and augmentation history; verify derivative rights. Human verification required. Do not grant blanket research_training permission.
- **Acne/other**: 857 catalog / 857 frozen. No exact association established with inspected source exports. Identify original provider and per-image collection history; do not infer provider from filenames. Human verification required. Do not grant blanket research_training permission.
- **Acne/roboflow_export**: 1588 catalog / 1588 frozen. No exact association established with inspected source exports. Recover original project/version and upstream ownership; match per-image export records. Human verification required. Do not grant blanket research_training permission.
- **Eczema/aug**: 68 catalog / 68 frozen. 15 byte-identical images associated with eczema_corrected.yolov8. Recover parent-image mapping and augmentation history; verify derivative rights. Human verification required. Do not grant blanket research_training permission.
- **Eczema/other**: 911 catalog / 639 frozen. 234 byte-identical images associated with eczema_corrected.yolov8. Identify original provider and per-image collection history; do not infer provider from filenames. Human verification required. Do not grant blanket research_training permission.
- **Eczema/roboflow_export**: 84 catalog / 82 frozen. 37 byte-identical images associated with eczema_corrected.yolov8. Recover original project/version and upstream ownership; match per-image export records. Human verification required. Do not grant blanket research_training permission.
- **Hyperpigmentation/aug**: 278 catalog / 278 frozen. No exact association established with inspected source exports. Recover parent-image mapping and augmentation history; verify derivative rights. Human verification required. Do not grant blanket research_training permission.
- **Hyperpigmentation/other**: 121 catalog / 121 frozen. No exact association established with inspected source exports. Identify original provider and per-image collection history; do not infer provider from filenames. Human verification required. Do not grant blanket research_training permission.
- **Hyperpigmentation/roboflow_export**: 2601 catalog / 2601 frozen. 304 byte-identical images associated with hyperpigmentation.v2i.yolov8. Recover original project/version and upstream ownership; match per-image export records. Human verification required. Do not grant blanket research_training permission.
- **Normal/aug**: 107 catalog / 107 frozen. No exact association established with inspected source exports. Recover parent-image mapping and augmentation history; verify derivative rights. Human verification required. Do not grant blanket research_training permission.
- **Normal/other**: 478 catalog / 478 frozen. No exact association established with inspected source exports. Identify original provider and per-image collection history; do not infer provider from filenames. Human verification required. Do not grant blanket research_training permission.
- **Normal/roboflow_export**: 2415 catalog / 2415 frozen. No exact association established with inspected source exports. Recover original project/version and upstream ownership; match per-image export records. Human verification required. Do not grant blanket research_training permission.

The structured companion file `v6/audit_results/provenance_investigation.csv` records each group's counts, evidence, permission status, consent status, facial-image uncertainty, missing requirements and action. No entries were copied into the live permission registry.

### Source export records recovered

Seven archived README pairs are preserved under `/home/bautista/PrecisionSkinV1/Localization_Documentation/03_data_sources/`; their source image exports are under `Downloads/Localization Training Datasets/`. All seven local dataset README files state **CC BY 4.0**. This is confirmed documentary evidence of the export's stated licence; it is not confirmation that the uploader owned every underlying image or obtained participant consent. Live project pages could not be verified through the web tool.

- `beautyai/acne-detection-mph4m`, v2, exported 2024-09-12: README says 6,280 images; current folder has **6,257** with label files. It documents grayscale, resizing, contrast changes and three augmented versions. Investigate the **23-image discrepancy**.
- `sarah-kiyani-l30y8/acne-detection-gpqbh`, v2, exported 2024-03-15: **4,859** images; resize/auto-orientation, no augmentation declared.
- `2023-2-02500-lpunetwork-edu-ph/eczema-bgyew-jnsj1-lwh7o`, v1, exported 2026-10-03: **348** images; no augmentation declared. Its exported class name is an opaque Stageok identifier and needs clinically verified mapping.
- `2023-2-02500-lpunetwork-edu-ph/hyperpigmentation-dm0kf-pcmtc`, v1, exported 2026-10-03: **500** images; no augmentation declared.
- `goat-pf9lx/hyperpigmentation-ctmqm`, v2, exported 2025-12-03: **1,169** images; resize to 512×512, no augmentation declared.
- `rizals-workspace-njmfs/hyperpigmentation_yolov10`, v3, exported 2026-05-09: **2,250** images; resize to 640×640, no augmentation declared.
- `tinnaluris-workspace/pigmentation-ptdgs`, v1, exported 2026-04-29: **1,634** images; resize to 768×768, two versions using flips/brightness augmentation.

These project paths are relative to `https://universe.roboflow.com/`. Historical export counts include augmented/repeated files and must not be interpreted as unique subjects. These detector collections are not automatically the original providers of the classifier collection.

Conservative linkage required the same basename **and identical SHA-256 bytes**. It found 955 matches between the complete local Hyperpigmentation pool and `hyperpigmentation.v2i.yolov8`; **304** belong to frozen V6. It found 326 matches to `eczema_corrected.yolov8`; **286** belong to frozen V6. These establish shared files, not the direction of copying or original ownership. No same-basename/exact-byte link to the other six exports was established by this limited method. Lack of a match does not rule out renamed, cropped or re-encoded copies.

`eczema_corrected.yolov8` has no licence README in the inspected export record. Its relationship to a licensed original must be substantiated separately. Do not transfer the 348-image export's licence to this corrected collection merely because both are eczema folders.

**Identifiability:** frozen metadata includes 1,296 full-face and 3,201 partial-face images. Crops may also be identifiable. These are existing image-type classifications, not a new visual identity assessment. Subject IDs are `unknown` for all 12,000 historical rows. Filenames mentioning DermNet, melasma, pigmentation or Fitzpatrick are clues only. No original clinical-provider or patient-consent linkage was established from them. The thesis consent letters/participant workflow do not establish rights over downloaded third-party images.

## 3. Existing annotation availability

**Image-level:** all 9,789 live V6 annotation rows are `unverified`; zero accepted dermatologist multilabel rows exist. All existing folder classes are inherited weak labels. A folder labelled Acne cannot establish that Eczema or Hyperpigmentation is absent. Normal is not a verified all-negative reference.

**Regions:** the live V6 region CSV has zero rows. Older YOLO boxes exist, but no inspected record establishes them as accepted dermatologist-verified V6 regions.

- Acne `acne_dataset_v4`: train **1,145 images / 11,621 boxes**; validation **154 / 1,562**; test **167 / 1,369**. Class map is one acne class. Every image in those folders has a corresponding label file. The format/coordinate audit found no malformed or out-of-bounds rows there.
- Hyperpigmentation rule-v2 folders: train **794 images / 1,589 boxes**; validation folder **27 / 56**; test **98 / 226**. These local folder counts differ from older summaries such as 808 training photos or 60 validation photos; summaries may refer to combined collections. Do not assume equivalence without the exact run manifest.
- Across **50 inspected image/label folder pairs**, the numeric five-field bounding-box checks found zero malformed/out-of-bounds rows and zero orphan label files. These repeated exports are not 50 independent datasets. Correct format and matching basenames do not establish clinically correct boxes, exhaustive coverage or alignment to the intended original after resizing/cropping.
- `Acne_Boxing/Elaiza__acne` has **4,073 images**, only **927 label files**, and **3,146 images without labels**. Of its label files, 269 are empty. Empty/missing labels must not be converted into dermatologist-confirmed negatives.
- The byte-identical eczema link provides **170 train, 38 validation, 39 fusion, 39 test** image/box review candidates. The linked hyperpigmentation export provides **181, 27, 47, 49**, respectively. They still need class-map review, box validation, permissions, clinical verification and exhaustive/non-exhaustive status.

The 590 linked image candidates have 590 unique label-file paths, containing 1,565 nonempty five-field annotation rows; one label file is empty. This is a format/count observation, not confirmation of clinical correctness or negative status. Shared image bytes establish that the exported image and working original are identical for these links; every box still needs visual alignment and lesion review.

**Potential problems:** `eczema_corrected.yolov8/data.yaml` points train and val at the same `../train/images` directory. Never adopt that split for V6. Existing acne and hyperpigmentation annotation audits describe missing-lesion concerns and reviewer variability; these boxes are useful drafts, not a completed reference standard.

There are **418 Acne-folder filenames containing pigmentation** and **24 mentioning eczema/dermatitis** in the frozen data. These are review priorities, not proof of coexisting conditions. Dermatologists must independently assess each of the three findings.

## 4. Missing annotations and eczema feasibility

All current accepted counts are zero, so the complete current minimums remain outstanding:

- **Train:** 300 fully decided image annotations; for each of Acne, Hyperpigmentation and Eczema, at least 40 Present and 40 Absent labels. At least 60 verified regions for each of acne, hyperpigmentation, eczema and reviewed background: **240 regions**.
- **Validation:** 150 fully decided images; at least 20 Present and 20 Absent per condition; 20 regions per each of four region classes: **80 regions**.
- **Fusion:** 250 fully decided images; at least 30 Present and 30 Absent per condition; 20 regions per class: **80 regions**.
- **Test:** 250 fully decided images; at least 30 Present and 30 Absent per condition; 20 regions per class: **80 regions**.

Total minimum: **950 fully decided photographs and 480 verified regions**. The per-condition counts must also be met; photographs can satisfy multiple Present counts. Unknown is allowed during annotation but does not count as Present/Absent or a fully decided photograph. These minimums are readiness thresholds, not a statistical guarantee.

ROI proposal matching must additionally produce all four training classes and at least 100 training rows; manually drawn box counts alone cannot guarantee this. Exhaustive lesion annotation is required for defensible localization precision/recall metrics.

The frozen source-folder Eczema pool is **473 train, 79 validation, 119 fusion, 118 test**. There are numerically enough folder candidates to attempt the existing positive minima, but actual verified eczema, usable regions, rights and independence are unknown. The candidate group-unexposed subsets have only **19, 28 and 23 Eczema-folder images** in validation/fusion/test. Other folders may contain eczema, but this must be assessed rather than assumed. A genuinely independent eczema evaluation is not established.

## 5. Historical exposure and evaluation feasibility

The frozen groups remain unchanged. No model performance evaluation or final-test lock opening was performed.

- **Potential training pool:** 5,873 frozen train images (1,800 each Acne/Hyperpigmentation/Normal, 473 Eczema). None are currently permission-cleared and clinically ready. Historical exposure need not prohibit training, but reference labels and permitted use remain essential.
- **Development pools:** 979 validation and 1,469 fusion images. Validation influences selection; fusion/calibration learns parameters. Both require research-training permission under the current policy, not evaluation-only permission.
- **Potential final-test pool:** 1,468 frozen test images. This is internally separate in V6, but is not independent of all earlier models.

V5 direct manifest training membership / exact manifest-MD5 exposure is **669 validation, 1,041 fusion, 1,074 test**. Training proxy-group exposure, including those direct matches, is **783, 1,203, 1,246**. Extra group-only cases are **114, 162, 172**; do not add direct and inclusive group counts.

The acne training folder named by installed metadata has exact original-file candidates for **94 validation, 130 fusion, 135 test** images. The inspected hyperpigmentation rule-v2 folder has **50, 87, 87**. Historical H25 train lists provide stronger linkage: each of three runs lists 3,684 readable training files and shares the same **52 validation, 90 fusion, 92 test** original images by SHA-256. Do not multiply these counts by three seeds. Historical file contents and installed hyperpigmentation ONNX linkage still need reconciliation because its metadata hash mismatches the current file. Eczema's exact specialist training membership remains unverified.

Avoiding the identified V5 and inspected YOLO candidate/H25 groups leaves **161 validation, 240 fusion, 192 test** candidates. These are not proven independent patients, permitted images or verified negatives. Candidate final-test source-folder counts are Acne 75, Eczema 23, Hyperpigmentation 38, Normal 56. Every image still has unknown patient identity; missing detector histories can conceal additional exposure.

**Feasibility warning:** 192 candidate test images cannot satisfy the existing 250-image test gate. Likewise, 240 fusion candidates are below its 250-image gate if independence from all historical groups is required. The untouched full split has more images but known historical exposure. This requires a documented evaluation decision, not an automatic split edit, gate reduction or claim that resplitting erases previous exposure. A V5-unseen subset comparison may be reported separately with limitations; a genuinely independent final evaluation may require additional existing rights-cleared, audited images or approved new collection.

## 6. Practical annotation workflow

1. **Resolve source rights first.** Begin with the matched export subsets because they have recoverable evidence. Ask the dataset assembler to identify the original project/version and image owners. Archive original terms and the applicable consent basis, record identifiable-face status, retention and responsibility. Verify corrected eczema's parent collection. Do not treat any set as already permitted.
2. **Create an evidence-only per-image mapping.** Preserve the 12 current keys and frozen IDs. Link each image to original provider, export/version, parent/augmentation group, evidence document and reviewer. A partially traced source key cannot receive a blanket permission covering its untraced remainder. Any future permission-granularity change must be explicit and reviewed; this investigation makes none.
3. **Agree on evaluation independence before annotation effort is spent.** Review historical membership and recover missing eczema run manifests. Audit the extra on-disk files for rights and previous exposure before proposing collection. Obtain approval for any future split revision or independent evaluation cohort. Patient/session mapping should come from legitimate source records, never face recognition or inferred identity.
4. **Use two review passes to preserve blinding.** First give the dermatologist the image, frozen ID and clinical annotation form without AI scores or YOLO drafts. Record independent Present/Absent/Unknown for all three conditions, image adequacy, reviewer code, date and actual blinding status. All three Absent plus adequate image establishes the three-condition Normal reference; an empty box file does not.
5. **Review regions after the independent findings.** Use existing exact-linked boxes as drafts only where permission allows. Verify class, original dimensions, coordinate frame and lesion coverage; correct boxes in a separate approved annotation artifact. Record manual or proposal-reviewed origin and exhaustive status. Never create a class from its detector score. If the reviewer sees drafts, record that exposure honestly.
6. **Collect genuine background and no-finding cases.** Have a reviewer explicitly reject candidate boxes or verify an adequate all-absent image. Missing labels, unsupported conditions, shadows and failed scans are not automatic negatives. Maintain Unknown for unassessable findings.
7. **Track quotas, not assumed labels.** Start with a randomized validation/fusion/test review order only after the evaluation plan and permissions are settled; then train. Recheck per-condition Present/Absent counts and all four region quotas. Add a blinded double-read subset and adjudicate disputes rather than forcing agreement.
8. **Rerun readiness checks only after real records exist.** Reconcile source/model lineage, confirm permission boundaries and verify annotations against frozen IDs. Keep original AI outputs separate from dermatologist review. Do not use final-test findings to tune the model. No training is authorized by this report.

## 7. Specific human actions

- Dataset assembler/team: provide the original acquisition links, export versions, download history and parent-image mappings for the 12 groups, especially Acne and Normal. A local Kaggle upload and a Roboflow suffix are insufficient.
- Dataset owners/responsible investigator: substantiate licence applicability, consent basis and permitted research uses; resolve rights for recognizable faces and derivative images. Local CC BY declarations are evidence to investigate, not invented approval.
- Research supervisor: approve an honest independent evaluation strategy given the exposed test images and the 192-image candidate remainder. Keep current gates and frozen splits intact until that decision.
- Dermatologist: perform the clinical multilabel and region verification with genuine negatives, blinding records and dispute handling. No accepted clinical labels currently exist.
- Model contributor: recover eczema training manifests and resolve hyperpigmentation model/metadata identity. This affects what can be claimed independent.

## 8. Single next practical step

**Ask the person who assembled the PrecisionSkin Kaggle collection to supply an image-to-original-source map and the supporting licence/consent evidence, starting with the 304 frozen hyperpigmentation images linked to the documented export and the 286 corrected-eczema candidates.** Use the supplied provenance investigation CSV as the checklist. This is the most direct way to establish a legitimate permitted subset before spending dermatologist time or starting any supervised training.

## Evidence and limits

Machine-readable artifacts are in `v6/audit_results/`: `dataset_source_inventory.json`, `preparation_metadata.json`, `export_source_evidence.json`, `original_export_links.csv`, `frozen_export_annotation_candidates.csv`, `provenance_investigation.csv`, and the historical exposure CSV/JSON files. Audit scripts are in `v6/tools/`. Existing Markdown annotation audits and local archived READMEs were inspected; compiled PDFs were not re-audited page by page. The scan did not establish any clinician-approved downloaded labels, verified consent or complete patient independence. Broader archive/history searches may recover further evidence; absence in the inspected files is not proof that evidence never existed.
