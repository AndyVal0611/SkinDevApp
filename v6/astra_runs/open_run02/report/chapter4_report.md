# LUMYVUE V6 - Chapter IV material (generated 2026-11-02T16:43:42, run `open_run02`)

> Every table is read from a saved experiment artifact. `NOT RUN` means the artifact does not exist; no value is estimated or substituted. Results are **experimental**, obtained on the data and annotations supplied by the researcher, and carry the limitations listed at the end.


## 1. Dataset characteristics and integrity

**Dataset policy log** - source `audit/dataset_policy_log.csv` - status: **RESULT**

| step | rows |
|---|---|
| manifest rows | 12000 |
| after excluding non-facial auxiliary training images (INCLUDE_AUX_NONFACIAL=False) | 10367 |
| after excluding facial_qa_status in ['reject', 'manual_review'] | 10063 |

**Inventory: class by V5 split** - source `audit/inventory_class_by_v5split.csv` - status: **RESULT**

| src_label | test | train | validation |
|---|---|---|---|
| Acne | 465 | 2071 | 464 |
| Hyperpigmentation | 454 | 2092 | 454 |
| Eczema | 179 | 698 | 186 |
| Normal | 458 | 2084 | 458 |

**Image type per class** - source `audit/inventory_image_type.csv` - status: **RESULT**

| src_label | close_up_facial | full_face | lesion_region_facial | partial_face |
|---|---|---|---|---|
| Acne | 1049 | 111 | 1249 | 591 |
| Hyperpigmentation | 359 | 220 | 1801 | 620 |
| Eczema | 201 | 105 | 414 | 343 |
| Normal | 230 | 910 | 90 | 1770 |

**Leakage summary (V5 split)** - source `audit/v5_leakage_summary.json` - status: **RESULT**

```json
{
 "groups": 6515,
 "groups_crossing_v5_splits": 885,
 "v5_test_images": 1556,
 "v5_test_images_with_sibling_elsewhere": 696,
 "by_class": {
  "Hyperpigmentation": 296,
  "Normal": 249,
  "Acne": 119,
  "Eczema": 32
 },
 "subject_ids_available": false,
 "group_stats": {
  "md5_links": 274,
  "stem_links": 3062,
  "near_links": 1635,
  "bucket_overflow_skips": 0
 }
}
```

**Annotation gates** - source `audit/annotation_gate_table.csv` - status: **RESULT**

| split | images | verified_full | present_Acne | present_Hyperpigmentation | present_Eczema | absent_Acne | absent_Hyperpigmentation | absent_Eczema | gate_ok | missing |
|---|---|---|---|---|---|---|---|---|---|---|
| train | 5873 | 889 | 176 | 261 | 243 | 715 | 632 | 651 | True | nan |
| validation | 979 | 591 | 173 | 233 | 55 | 421 | 363 | 537 | True | nan |
| fusion | 1469 | 347 | 111 | 124 | 33 | 242 | 228 | 324 | True | nan |
| test | 1468 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | False | verified photos 0 < 250; Acne present 0 < 30; Acne absent 0 < 30; Hyperpigmentation present 0 < 30; Hyperpigmentation absent 0 < 30; Eczema present 0 < 30; Eczema absent 0 < 30 |

**Region annotation gates** - source `audit/region_gate_table.csv` - status: **RESULT**

| split | acne | hyperpigmentation | eczema | background_false_positive |
|---|---|---|---|---|
| train | 5244 | 993 | 1058 | 134 |
| validation | 850 | 148 | 117 | 93 |
| fusion | 0 | 0 | 0 | 0 |
| test | 0 | 0 | 0 | 0 |

**Quality/domain audit (medians)** - source `audit/quality_domain_audit.csv` - status: **RESULT**

| index | app_auto | app_manual | app_upload | source_dataset |
|---|---|---|---|---|
| laplacian_var | 79.9159 | 32.3726 | 124.8027 | 356.0212 |
| mean_brightness | 126.0185 | 137.2121 | 104.3890 | 126.0148 |
| under_frac | 0.0429 | 0.0331 | 0.1436 | 0.0412 |
| over_frac | 0.0004 | 0.0000 | 0.0001 | 0.0000 |
| uneven_lr | 0.1373 | 0.2594 | 0.1579 | 0.1499 |
| glare_frac | 0.0003 | 0.0000 | 0.0000 | 0.0000 |
| gray_world_dev | 0.1246 | 0.0518 | 0.4902 | 0.3957 |
| jpeg_blockiness | 1.1047 | 1.1909 | 1.3716 | 1.1539 |
| overlay_edge_frac | 0.0109 | 0.0078 | 0.0354 | 0.0288 |
| skin_frac | 0.3529 | 0.3387 | 0.7547 | 0.7791 |
| ita_proxy | 26.2985 | 56.7322 | 10.4094 | 22.8767 |
| width | 480.0000 | 480.0000 | 640.0000 | 640.0000 |


## 2. Deployment parity and V5 baseline

**Parity summary** - source `audit/parity_summary.json` - status: **RESULT**

```json
{
 "level1_static": true,
 "level2": {
  "n": 60,
  "max_abs_diff_pp": 0.000499990463256772,
  "per_class_max_pp": {
   "Acne": 0.0004954137951135862,
   "Hyperpigmentation": 0.000499990463256772,
   "Eczema": 0.0004996342658998287,
   "Normal": 0.00048422503471368117
  }
 },
 "level3_folders": 0,
 "claim": "Equivalence is established only for the levels with data; camera, face detection and stabiliser are not covered."
}
```

**Python vs C# app records** - source `audit/parity_python_vs_app_records.csv` - status: **RESULT**

| capture | trigger | same_model_hash | max_abs_diff_pp | diff_Acne | diff_Hyperpigmentation | diff_Eczema | diff_Normal | same_top_class |
|---|---|---|---|---|---|---|---|---|
| cap_813417360d3c7c85 | manual | True | 0.0004 | 0.0004 | 0.0003 | 0.0004 | 0.0003 | True |
| cap_9f465baf1feb6bb5 | upload | True | 0.0004 | 0.0001 | 0.0004 | 0.0002 | 0.0002 | True |
| cap_9e4aa324ed41d5a6 | upload | True | 0.0004 | 0.0002 | 0.0001 | 0.0004 | 0.0002 | True |
| cap_2cf0cfe62d04a918 | upload | True | 0.0005 | 0.0003 | 0.0005 | 0.0003 | 0.0005 | True |
| cap_5721ebddb1f3c8f8 | upload | True | 0.0002 | 0.0000 | 0.0002 | 0.0002 | 0.0001 | True |
| cap_de10686b20ce3ae0 | upload | True | 0.0005 | 0.0005 | 0.0001 | 0.0002 | 0.0001 | True |
| cap_b6d81f0153b9a87f | upload | True | 0.0004 | 0.0004 | 0.0001 | 0.0004 | 0.0002 | True |
| cap_a908c98fab642b88 | upload | True | 0.0005 | 0.0002 | 0.0001 | 0.0005 | 0.0001 | True |
| cap_7b810b73f5dca83f | auto | True | 0.0004 | 0.0004 | 0.0001 | 0.0004 | 0.0001 | True |
| cap_e931e1b27c055c7d | auto | True | 0.0003 | 0.0001 | 0.0003 | 0.0001 | 0.0001 | True |
| cap_b6e2a35d7ef67673 | manual | True | 0.0005 | 0.0004 | 0.0005 | 0.0004 | 0.0004 | True |
| cap_0025f9374757d43d | auto | True | 0.0003 | 0.0003 | 0.0003 | 0.0002 | 0.0002 | True |
| cap_50b59f0999081633 | auto | True | 0.0005 | 0.0005 | 0.0003 | 0.0003 | 0.0001 | True |
| cap_049f418994c99457 | auto | True | 0.0005 | 0.0005 | 0.0002 | 0.0001 | 0.0003 | True |
| cap_4c2de5ca384427b0 | auto | True | 0.0003 | 0.0002 | 0.0003 | 0.0001 | 0.0000 | True |
| cap_9e5ddc68fc95213a | auto | True | 0.0003 | 0.0003 | 0.0003 | 0.0002 | 0.0002 | True |
| cap_4e1e01120f1406b3 | auto | True | 0.0003 | 0.0001 | 0.0003 | 0.0002 | 0.0001 | True |
| cap_82d957a4e90a066e | auto | True | 0.0003 | 0.0000 | 0.0003 | 0.0002 | 0.0001 | True |
| cap_1a0b7d9923dbd511 | auto | True | 0.0004 | 0.0001 | 0.0004 | 0.0001 | 0.0004 | True |
| cap_c7218aed343a9da0 | auto | True | 0.0003 | 0.0003 | 0.0000 | 0.0003 | 0.0003 | True |
| cap_a0584b9fc499ee45 | auto | True | 0.0005 | 0.0002 | 0.0002 | 0.0005 | 0.0002 | True |
| cap_1ff166028b99bccd | auto | True | 0.0004 | 0.0002 | 0.0003 | 0.0004 | 0.0002 | True |
| cap_91331c28b50d05c4 | auto | True | 0.0004 | 0.0001 | 0.0004 | 0.0001 | 0.0003 | True |
| cap_90bbe521a09e10d4 | auto | True | 0.0002 | 0.0001 | 0.0002 | 0.0002 | 0.0001 | True |
| cap_35d2bab7ab89c243 | auto | True | 0.0004 | 0.0003 | 0.0004 | 0.0001 | 0.0001 | True |
| cap_c7b16549adbeafce | manual | True | 0.0005 | 0.0003 | 0.0005 | 0.0003 | 0.0002 | True |
| cap_d237afd1466ea35f | manual | True | 0.0005 | 0.0004 | 0.0005 | 0.0002 | 0.0003 | True |
| cap_c0ccef6fe63a4f61 | manual | True | 0.0005 | 0.0004 | 0.0005 | 0.0001 | 0.0002 | True |
| cap_1441a0bbc8859dc8 | auto | True | 0.0004 | 0.0002 | 0.0001 | 0.0001 | 0.0004 | True |
| cap_2e79cad98d56aa0e | auto | True | 0.0005 | 0.0001 | 0.0005 | 0.0004 | 0.0000 | True |
| cap_00404b7e378be890 | auto | True | 0.0004 | 0.0001 | 0.0004 | 0.0003 | 0.0001 | True |
| cap_273ac6d059969651 | auto | True | 0.0002 | 0.0001 | 0.0001 | 0.0002 | 0.0002 | True |
| cap_b86556f36f02489f | auto | True | 0.0002 | 0.0002 | 0.0001 | 0.0002 | 0.0001 | True |
| cap_38bfc4b1adadeb2a | auto | True | 0.0004 | 0.0003 | 0.0001 | 0.0004 | 0.0002 | True |
| cap_ca2dad75221d7632 | auto | True | 0.0005 | 0.0005 | 0.0003 | 0.0004 | 0.0001 | True |
| cap_2dcaf0c4aafe6842 | manual | True | 0.0004 | 0.0004 | 0.0001 | 0.0002 | 0.0001 | True |
| cap_a3e75aacd15f90a9 | manual | True | 0.0003 | 0.0001 | 0.0003 | 0.0003 | 0.0001 | True |
| cap_7bbc04ec1f541833 | manual | True | 0.0005 | 0.0001 | 0.0002 | 0.0002 | 0.0005 | True |
| cap_341149a3b2b70544 | manual | True | 0.0003 | 0.0003 | 0.0001 | 0.0001 | 0.0003 | True |
| cap_1590cc778c62f0f5 | manual | True | 0.0005 | 0.0005 | 0.0004 | 0.0001 | 0.0000 | True |
| cap_9013cbdf6c2d95ee | manual | True | 0.0004 | 0.0004 | 0.0004 | 0.0002 | 0.0000 | True |
| cap_f0b5121e007b39d2 | manual | True | 0.0004 | 0.0004 | 0.0003 | 0.0004 | 0.0001 | True |
| cap_cefaf233c968118e | manual | True | 0.0004 | 0.0004 | 0.0003 | 0.0004 | 0.0004 | True |
| cap_0827023cf02cea19 | manual | True | 0.0004 | 0.0002 | 0.0004 | 0.0000 | 0.0004 | True |
| cap_893719db6f0a0f62 | manual | True | 0.0002 | 0.0000 | 0.0002 | 0.0002 | 0.0000 | True |
| cap_1bf99828bd04ead9 | auto | True | 0.0004 | 0.0003 | 0.0004 | 0.0002 | 0.0002 | True |
| cap_2d028fe5db7b560a | manual | True | 0.0003 | 0.0002 | 0.0003 | 0.0003 | 0.0002 | True |
| cap_acda26e4fb09ff61 | manual | True | 0.0004 | 0.0002 | 0.0003 | 0.0004 | 0.0001 | True |
| cap_963bf8f26f237bf8 | auto | True | 0.0004 | 0.0003 | 0.0003 | 0.0001 | 0.0004 | True |
| cap_d4eb8abc9f48d44b | manual | True | 0.0004 | 0.0004 | 0.0004 | 0.0002 | 0.0002 | True |
| cap_4992f210ff126c10 | manual | True | 0.0004 | 0.0002 | 0.0003 | 0.0002 | 0.0004 | True |
| cap_a3dff2c33c8d2af8 | manual | True | 0.0003 | 0.0002 | 0.0003 | 0.0003 | 0.0002 | True |
| cap_c4d017dde34af488 | manual | True | 0.0005 | 0.0005 | 0.0002 | 0.0000 | 0.0002 | True |
| cap_55c7a4bfc5ed1860 | auto | True | 0.0005 | 0.0005 | 0.0005 | 0.0003 | 0.0003 | True |
| cap_9a9c2199ca0681a8 | manual | True | 0.0005 | 0.0001 | 0.0005 | 0.0002 | 0.0005 | True |
| cap_fbdaa5c8d87d62b0 | manual | True | 0.0004 | 0.0002 | 0.0003 | 0.0002 | 0.0004 | True |
| cap_5ade85bce6153ae5 | manual | True | 0.0004 | 0.0004 | 0.0001 | 0.0000 | 0.0002 | True |
| cap_f7143866f8a1fc86 | upload | True | 0.0003 | 0.0002 | 0.0001 | 0.0002 | 0.0003 | True |
| cap_ebd236435d5b726d | upload | True | 0.0004 | 0.0004 | 0.0001 | 0.0003 | 0.0002 | True |
| cap_6da0a8cba261e900 | upload | True | 0.0004 | 0.0004 | 0.0001 | 0.0001 | 0.0002 | True |

**V5 dev error pairs (source labels)** - source `v5_comparison/v5_dev_source_label_error_pairs.csv` - status: **RESULT**

| source_label | v5_predicted | n | rate_of_source_class | median_conf | median_margin |
|---|---|---|---|---|---|
| Normal | Hyperpigmentation | 8 | 0.0031 | 0.7142 | 0.4333 |
| Hyperpigmentation | Normal | 15 | 0.0059 | 0.7120 | 0.4729 |
| Hyperpigmentation | Eczema | 36 | 0.0141 | 0.6619 | 0.3737 |
| Eczema | Hyperpigmentation | 4 | 0.0060 | 0.7093 | 0.5007 |
| Acne | Eczema | 173 | 0.0678 | 0.7025 | 0.4322 |
| Eczema | Acne | 14 | 0.0209 | 0.6008 | 0.2353 |

**V5 dev error pairs (verified labels)** - source `v5_comparison/v5_dev_verified_error_pairs.csv` - status: **RESULT**

| verified_truth | v5_predicted | n | of_truth_class |
|---|---|---|---|
| none | Hyperpigmentation | 18 | 514 |
| Hyperpigmentation | Normal | 43 | 540 |
| Hyperpigmentation | Eczema | 17 | 540 |
| Eczema | Hyperpigmentation | 8 | 315 |
| Acne | Eczema | 40 | 386 |
| Eczema | Acne | 20 | 315 |
| mixed (>=2 conditions) | one single class | 72 | 72 |
| mixed: V5 single class is one of the true conditions | (partial credit) | 68 | 72 |


## 3. Model training summary

**Global experiment status** - source `metrics/global_training_status.json` - status: **RESULT**

```json
{
 "D_multilabel3": {
  "status": "trained",
  "info": {
   "experiment": "D_multilabel3",
   "pos_weight": [
    1.0,
    1.0,
    1.0
   ],
   "known_labels_train": [
    891,
    893,
    894
   ],
   "positives_train": [
    176,
    261,
    243
   ],
   "negatives_train": [
    715,
    632,
    651
   ],
   "n_train": 896,
   "n_val": 596
  }
 },
 "E_v5_backbone_multilabel3": {
  "status": "trained",
  "info": {
   "experiment": "E_v5_backbone_multilabel3",
   "pos_weight": [
    1.0,
    1.0,
    1.0
   ],
   "known_labels_train": [
    891,
    893,
    894
   ],
   "positives_train": [
    176,
    261,
    243
   ],
   "negatives_train": [
    715,
    632,
    651
   ],
   "n_train": 896,
   "n_val": 596
  }
 }
}
```

**Global experiments on validation (per condition)** - source `metrics/global_experiments_validation_per_condition.csv` - status: **RESULT**

| condition | n_known | pos | neg | threshold | precision | recall | sensitivity | specificity | f1 | tp | fp | fn | tn | ap | roc_auc | experiment | seed |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Acne | 594 | 173 | 421 | 0.5000 | 0.8655 | 0.8555 | 0.8555 | 0.9454 | 0.8605 | 148 | 23 | 25 | 398 | 0.8750 | 0.9446 | A_v5_baseline | -1 |
| Hyperpigmentation | 596 | 233 | 363 | 0.5000 | 0.9235 | 0.6738 | 0.6738 | 0.9642 | 0.7792 | 157 | 13 | 76 | 350 | 0.8742 | 0.8927 | A_v5_baseline | -1 |
| Eczema | 592 | 55 | 537 | 0.5000 | 0.6406 | 0.7455 | 0.7455 | 0.9572 | 0.6891 | 41 | 23 | 14 | 514 | 0.6511 | 0.9354 | A_v5_baseline | -1 |
| Acne | 594 | 173 | 421 | 0.5000 | 0.8923 | 0.6705 | 0.6705 | 0.9667 | 0.7657 | 116 | 14 | 57 | 407 | 0.8887 | 0.9319 | D_multilabel3 | 42 |
| Hyperpigmentation | 596 | 233 | 363 | 0.5000 | 0.8125 | 0.7253 | 0.7253 | 0.8926 | 0.7664 | 169 | 39 | 64 | 324 | 0.8709 | 0.8824 | D_multilabel3 | 42 |
| Eczema | 592 | 55 | 537 | 0.5000 | 0.6364 | 0.6364 | 0.6364 | 0.9628 | 0.6364 | 35 | 20 | 20 | 517 | 0.6295 | 0.9233 | D_multilabel3 | 42 |
| Acne | 594 | 173 | 421 | 0.5000 | 0.8757 | 0.8555 | 0.8555 | 0.9501 | 0.8655 | 148 | 21 | 25 | 400 | 0.9249 | 0.9518 | E_v5_backbone_multilabel3 | 42 |
| Hyperpigmentation | 596 | 233 | 363 | 0.5000 | 0.9016 | 0.7082 | 0.7082 | 0.9504 | 0.7933 | 165 | 18 | 68 | 345 | 0.9119 | 0.9148 | E_v5_backbone_multilabel3 | 42 |
| Eczema | 592 | 55 | 537 | 0.5000 | 0.6724 | 0.7091 | 0.7091 | 0.9646 | 0.6903 | 39 | 19 | 16 | 518 | 0.7535 | 0.9506 | E_v5_backbone_multilabel3 | 42 |

**Global model selection** - source `metrics/global_selection.json` - status: **RESULT**

```json
{
 "candidates": {
  "D_multilabel3": {
   "best_seed": 42,
   "val_macro_ap": 0.7963939929356001,
   "delta_f1_vs_A": {
    "Acne": -0.0948,
    "Hyperpigmentation": -0.0127,
    "Eczema": -0.0527
   },
   "regression": [
    "Acne",
    "Eczema"
   ]
  },
  "E_v5_backbone_multilabel3": {
   "best_seed": 42,
   "val_macro_ap": 0.8634127577818869,
   "delta_f1_vs_A": {
    "Acne": 0.005,
    "Hyperpigmentation": 0.0141,
    "Eczema": 0.0012
   },
   "regression": []
  }
 },
 "selected": "E_v5_backbone_multilabel3",
 "selected_seed": 42,
 "selected_has_regression": false,
 "d_vs_e": {
  "all_validation": {
   "D_multilabel3": {
    "macro_ap": 0.7963939929356001,
    "macro_f1": 0.7228267044391514,
    "hp_pos": 230,
    "hp_missed_as_no_target": 33,
    "all_neg": 168,
    "all_neg_flagged_hp": 19
   },
   "E_v5_backbone_multilabel3": {
    "macro_ap": 0.8634127577818869,
    "macro_f1": 0.7830105978394287,
    "hp_pos": 230,
    "hp_missed_as_no_target": 32,
    "all_neg": 168,
    "all_neg_flagged_hp": 10
   },
   "e_minus_d_macro_ap_ci95": [
    0.04549775424567642,
    0.0910810146309694
   ]
  },
  "not_trained_by_v5": {
   "D_multilabel3": {
    "macro_ap": 0.810124517371129,
    "macro_f1": 0.6918049270990448,
    "hp_pos": 47,
    "hp_missed_as_no_target": 9,
    "all_neg": 26,
    "all_neg_flagged_hp": 2
   },
   "E_v5_backbone_multilabel3": {
    "macro_ap": 0.8494774756161151,
    "macro_f1": 0.7320384757676656,
    "hp_pos": 47,
    "hp_missed_as_no_target": 10,
    "all_neg": 26,
    "all_neg_flagged_hp": 3
   },
   "e_minus_d_macro_ap_ci95": [
    0.0016820990309305507,
    0.07620585524358933
   ]
  },
  "v5_test_split_only": {
   "D_multilabel3": {
    "macro_ap": 0.6991614918421467,
    "macro_f1": 0.5891405342624855,
    "hp_pos": 23,
    "hp_missed_as_no_target": 3,
    "all_neg": 11,
    "all_neg_flagged_hp": 0
   },
   "E_v5_backbone_multilabel3": {
    "macro_ap": 0.7676475338639683,
    "macro_f1": 0.7097409483411604,
    "hp_pos": 23,
    "hp_missed_as_no_target": 3,
    "all_neg": 11,
    "all_neg_flagged_hp": 1
   },
   "e_minus_d_macro_ap_ci95": [
    -0.005743277197732433,
    0.20350505486474962
   ]
  }
 },
 "d_vs_e_selection_basis": "macro AP on validation images not trained on by V5, no per-condition regression preferred"
}
```

**ROI training summary** - source `metrics/roi_training_summary.json` - status: **RESULT**

```json
{
 "per_seed": {
  "42": {
   "frozen_backbone_val_macro_f1": 0.7270343989942278,
   "partial_finetune_val_macro_f1": 0.7773202615083622,
   "chosen": "B"
  }
 },
 "selected_seed": 42,
 "init": "imagenet"
}
```

**Fusion calibration (out-of-fold)** - source `metrics/fusion_calibration_oof.csv` - status: **RESULT**

| variant | condition | brier_raw | brier_calibrated | ece_raw | ece_calibrated | calibrated | threshold |
|---|---|---|---|---|---|---|---|
| full | Acne | 0.0649 | 0.0609 | 0.0521 | 0.0386 | True | 0.4300 |
| full | Hyperpigmentation | 0.0585 | 0.0587 | 0.0341 | 0.0211 | True | 0.3400 |
| full | Eczema | 0.0651 | 0.0437 | 0.0719 | 0.0180 | True | 0.3300 |
| global_yolo | Acne | 0.0632 | 0.0582 | 0.0358 | 0.0237 | True | 0.4200 |
| global_yolo | Hyperpigmentation | 0.0572 | 0.0562 | 0.0337 | 0.0199 | True | 0.3700 |
| global_yolo | Eczema | 0.0600 | 0.0454 | 0.0712 | 0.0285 | True | 0.2700 |
| global_only | Acne | 0.0653 | 0.0627 | 0.0393 | 0.0206 | True | 0.5100 |
| global_only | Hyperpigmentation | 0.0664 | 0.0651 | 0.0427 | 0.0282 | True | 0.3900 |
| global_only | Eczema | 0.0713 | 0.0450 | 0.0912 | 0.0258 | True | 0.2500 |
| roi_only | Acne | 0.0877 | 0.0822 | 0.0546 | 0.0098 | True | 0.4600 |
| roi_only | Hyperpigmentation | 0.0783 | 0.0756 | 0.0586 | 0.0562 | True | 0.5000 |
| roi_only | Eczema | 0.1135 | 0.0576 | 0.1653 | 0.0279 | True | 0.2400 |
| no_acne_evidence | Acne | 0.0712 | 0.0638 | 0.0456 | 0.0443 | True | 0.4000 |
| no_acne_evidence | Hyperpigmentation | 0.0609 | 0.0624 | 0.0452 | 0.0297 | True | 0.3200 |
| no_acne_evidence | Eczema | 0.0606 | 0.0441 | 0.0854 | 0.0283 | True | 0.1800 |
| no_eczema_evidence | Acne | 0.0514 | 0.0533 | 0.0450 | 0.0473 | True | 0.3000 |
| no_eczema_evidence | Hyperpigmentation | 0.0591 | 0.0620 | 0.0440 | 0.0399 | True | 0.4000 |
| no_eczema_evidence | Eczema | 0.0688 | 0.0425 | 0.0846 | 0.0286 | True | 0.3600 |
| no_hyper_evidence | Acne | 0.0594 | 0.0558 | 0.0450 | 0.0337 | True | 0.3800 |
| no_hyper_evidence | Hyperpigmentation | 0.0652 | 0.0630 | 0.0431 | 0.0172 | True | 0.3900 |
| no_hyper_evidence | Eczema | 0.0859 | 0.0500 | 0.1025 | 0.0260 | True | 0.2600 |


## 4. Classification performance on the test split (single evaluation)

**Per condition, by system** - source `metrics/test_per_condition_by_system.csv` - status: **NOT RUN (missing metrics/test_per_condition_by_system.csv)**


**Set metrics by system** - source `metrics/test_set_metrics_by_system.csv` - status: **NOT RUN (missing metrics/test_set_metrics_by_system.csv)**


**Macro-F1 with 95% cluster-bootstrap CI** - source `metrics/test_macro_f1_ci_by_system.csv` - status: **NOT RUN (missing metrics/test_macro_f1_ci_by_system.csv)**


**Paired differences** - source `metrics/test_paired_differences.csv` - status: **NOT RUN (missing metrics/test_paired_differences.csv)**


**Single / mixed / none subsets** - source `metrics/test_subset_metrics.csv` - status: **NOT RUN (missing metrics/test_subset_metrics.csv)**


**Calibration** - source `metrics/test_calibration.csv` - status: **NOT RUN (missing metrics/test_calibration.csv)**



## 5. Localisation evaluation

**YOLO registry** - source `yolo/detector_registry.json` - status: **RESULT**

```json
{
 "config_hash": "ce6ae3186e771bfe",
 "detectors": [
  {
   "condition": "Acne",
   "available": true,
   "kind": "specialist",
   "location": "WPF Model folder",
   "members": 3,
   "imgsz": 800,
   "nms_iou": 0.6,
   "fusion_iou": 0.5,
   "deployed_threshold": 0.275,
   "floor": 0.02,
   "json_sha_matches": true,
   "onnx_input": "[[1, 3, 800, 800]]",
   "onnx_output": "[[1, 5, 13125]]",
   "layout": "rows_first",
   "classes": [
    "acne"
   ],
   "sha256": [
    "2f0f09ebc376",
    "f00c6e6bb333",
    "6dfb0969cf5c"
   ],
   "notes": ""
  },
  {
   "condition": "Hyperpigmentation",
   "available": true,
   "kind": "specialist",
   "location": "WPF Model folder",
   "members": 3,
   "imgsz": 800,
   "nms_iou": 0.6,
   "fusion_iou": 0.5,
   "deployed_threshold": 0.225,
   "floor": 0.02,
   "json_sha_matches": false,
   "onnx_input": "[[1, 3, 800, 800]]",
   "onnx_output": "[[1, 5, 13125]]",
   "layout": "rows_first",
   "classes": [
    "hyperpigmentation"
   ],
   "sha256": [
    "f27affb73061",
    "ed821543c9bc",
    "2a6fa1afa61f"
   ],
   "notes": "JSON onnx_sha256 does NOT match the primary ONNX file (JSON written for another file?) - detector still loaded"
  },
  {
   "condition": "Eczema",
   "available": true,
   "kind": "specialist",
   "location": "WPF Model folder",
   "members": 3,
   "imgsz": 640,
   "nms_iou": 0.6,
   "fusion_iou": 0.5,
   "deployed_threshold": 0.25,
   "floor": 0.02,
   "json_sha_matches": true,
   "onnx_input": "[[1, 3, 640, 640]]",
   "onnx_output": "[[1, 5, 8400]]",
   "layout": "rows_first",
   "classes": [
    "eczema"
   ],
   "sha256": [
    "a93b77460c2f",
    "8fbf857b3169",
    "b5a6b50b9c50"
   ],
   "notes": ""
  }
 ]
}
```

**Duplicate/overlap summary (dev)** - source `yolo/duplicate_overlap_summary.csv` - status: **RESULT**

| condition | images_with_detections | duplicate | nested | overlap |
|---|---|---|---|---|
| Acne | 1525 | 2 | 72 | 411 |
| Hyperpigmentation | 2100 | 4 | 167 | 287 |
| Eczema | 1626 | 2 | 199 | 182 |

**Localisation metrics (dev)** - source `yolo/localisation_metrics_dev.csv` - status: **RESULT**

| condition | status |
|---|---|
| Acne | insufficient verified exhaustive regions (0 boxes in 1378 images) |
| Hyperpigmentation | insufficient verified exhaustive regions (0 boxes in 1223 images) |
| Eczema | insufficient verified exhaustive regions (0 boxes in 1512 images) |

**Localisation metrics (test)** - source `metrics/test_localisation_metrics.csv` - status: **NOT RUN (missing metrics/test_localisation_metrics.csv)**


**Detector vs verified label (test)** - source `metrics/test_detector_vs_verified_label.csv` - status: **NOT RUN (missing metrics/test_detector_vs_verified_label.csv)**



## 6. Fusion system, ablation and verification statistics

**Ablation** - source `metrics/test_ablation.csv` - status: **NOT RUN (missing metrics/test_ablation.csv)**


**Verification statistics** - source `metrics/test_verification_statistics.json` - status: **NOT RUN (missing metrics/test_verification_statistics.json)**


**Risk-coverage (F)** - source `metrics/test_risk_coverage_F.csv` - status: **NOT RUN (missing metrics/test_risk_coverage_F.csv)**


**Robustness (global CNN)** - source `metrics/test_robustness_global_cnn.csv` - status: **NOT RUN (missing metrics/test_robustness_global_cnn.csv)**


**Latency (Python reference)** - source `metrics/latency_reference_pipeline.json` - status: **NOT RUN (missing metrics/latency_reference_pipeline.json)**



## 6b. Agreement with the dermatologist reference (AI-uncertain and AI-no-result reported separately; every assessment remains PENDING_DERMATOLOGIST_REVIEW)

**System G vs reference, per condition** - source `metrics/test_agreement_with_dermatologist_reference_G.csv` - status: **NOT RUN (missing metrics/test_agreement_with_dermatologist_reference_G.csv)**


**Overall** - source `metrics/test_agreement_overall_G.json` - status: **NOT RUN (missing metrics/test_agreement_overall_G.json)**


**Live dermatologist reviews (all completed)** - source `metrics/live_dermatologist_agreement_all.csv` - status: **NOT RUN (missing metrics/live_dermatologist_agreement_all.csv)**


**Live reviews (blinded only)** - source `metrics/live_dermatologist_agreement_blinded.csv` - status: **NOT RUN (missing metrics/live_dermatologist_agreement_blinded.csv)**



## 7. Comparison against V5 and misclassification analysis

**V5 reported (single-label; NOT comparable)** - source `v5_comparison/v5_reported_single_label_test_metrics.csv` - status: **NOT RUN (missing v5_comparison/v5_reported_single_label_test_metrics.csv)**


**V5 vs V6 on the verified test split** - source `v5_comparison/v5_vs_v6_verified_test.csv` - status: **NOT RUN (missing v5_comparison/v5_vs_v6_verified_test.csv)**


**Misclassification pairs (test)** - source `metrics/test_misclassification_pairs.csv` - status: **NOT RUN (missing metrics/test_misclassification_pairs.csv)**


**Hyperpigmentation missed as Normal** - source `v5_comparison/hyperpigmentation_missed_as_normal.json` - status: **NOT RUN (missing v5_comparison/hyperpigmentation_missed_as_normal.json)**



## 8. Limitations (always applicable)
* No subject/session identifiers exist; group separation uses filename-stem, exact-hash and perceptual-hash proxies and cannot prove patient-level independence.
* Reference labels are only as good as the reviewers; inter-rater agreement was not measured here (use `blinded_to_ai` and a double-read subset for Chapter III).
* Quality thresholds, Stage-1/Stage-2 bands and the OOD threshold are provisional and selected on out-of-fold data; they are not validated clinical criteria.
* OOD scores measure distance from the training distribution; they do not identify unknown conditions.
* Eczema localisation depends on the registered Eczema detector; if none was registered, Eczema evidence is `detector_unavailable` and Eczema localisation metrics are not reported.
* ROI crops from 224x224 processed images (when originals are missing) carry little detail; check the per-image `source` columns.
* Python reference timing is not the C# latency; the C# parity of the new components is **untested** until the parity procedure is run.
* A single test split, small verified sets and many comparisons: unadjusted intervals; non-significance is not equivalence.
* Every AI assessment requires dermatologist review. 'Supported', 'accepted' and Stage-2 'verification' describe the strength of AI evidence only; none of them is a clinical confirmation.