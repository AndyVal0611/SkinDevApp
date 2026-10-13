# Hyperpigmentation: full results (generated)

## A. Every experiment run (validation sets only; seed shown; precision/recall/F1 at IoU 0.5 at the F1-best confidence)

| run | what changed | train photos | validation set | mAP@0.5 | mAP@0.5-0.95 | precision | recall | F1 | confidence | epochs (best) | minutes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| H1_own (seed 42) | FACTOR: training data = only your boxes | 614 | hand_val | 0.267 | 0.084 | 0.30 | 0.39 | 0.34 | 0.125 | 63 (37) | 10 |
| H1_own (seed 42) | FACTOR: training data = only your boxes | 614 | public_val | 0.070 | 0.021 | 0.15 | 0.17 | 0.16 | 0.150 | 63 (37) | 10 |
| H6_own_drafts (seed 42) | FACTOR: your boxes + all reviewed drafts (two box styles mixed) | 895 | hand_val | 0.298 | 0.092 | 0.33 | 0.37 | 0.35 | 0.150 | 56 (47) | 14 |
| H6_own_drafts (seed 42) | FACTOR: your boxes + all reviewed drafts (two box styles mixed) | 895 | public_val | 0.058 | 0.016 | 0.14 | 0.15 | 0.15 | 0.175 | 56 (47) | 14 |
| H7_own_drafts_cov (seed 42) | FACTOR: your boxes + only the reviewed drafts in the test-photo style (coverage <= 18 %, n | 796 | hand_val | 0.282 | 0.085 | 0.38 | 0.32 | 0.35 | 0.125 | 54 (40) | 12 |
| H7_own_drafts_cov (seed 42) | FACTOR: your boxes + only the reviewed drafts in the test-photo style (coverage <= 18 %, n | 796 | public_val | 0.071 | 0.020 | 0.17 | 0.14 | 0.15 | 0.175 | 54 (40) | 12 |
| H0_mix (seed 42) | baseline of the new protocol (same recipe as before, hand-boxed validation) | 2891 | hand_val | 0.232 | 0.067 | 0.25 | 0.33 | 0.28 | 0.075 | 80 (59) | 48 |
| H0_mix (seed 42) | baseline of the new protocol (same recipe as before, hand-boxed validation) | 2891 | public_val | 0.336 | 0.112 | 0.40 | 0.39 | 0.40 | 0.125 | 80 (59) | 48 |
| H8_drafts_v2 (seed 42) | FACTOR: your boxes + all 500+ reviewed drafts (round 2) | 1115 | hand_val | 0.296 | 0.093 | 0.33 | 0.37 | 0.35 | 0.150 | 72 (50) | 22 |
| H8_drafts_v2 (seed 42) | FACTOR: your boxes + all 500+ reviewed drafts (round 2) | 1115 | public_val | 0.105 | 0.026 | 0.24 | 0.16 | 0.19 | 0.250 | 72 (50) | 22 |
| H9_v2_960 (seed 42) | FACTOR (vs H8): input 960 px | 1115 | hand_val | 0.308 | 0.090 | 0.44 | 0.32 | 0.37 | 0.175 | 62 (32) | 26 |
| H9_v2_960 (seed 42) | FACTOR (vs H8): input 960 px | 1115 | public_val | 0.084 | 0.025 | 0.17 | 0.17 | 0.17 | 0.175 | 62 (32) | 26 |
| H10_v2_lowlr (seed 42) | FACTOR (vs H8): lr0 0.0005 AdamW | 1115 | hand_val | 0.324 | 0.094 | 0.36 | 0.37 | 0.37 | 0.200 | 60 (30) | 18 |
| H10_v2_lowlr (seed 42) | FACTOR (vs H8): lr0 0.0005 AdamW | 1115 | public_val | 0.091 | 0.025 | 0.26 | 0.12 | 0.17 | 0.375 | 60 (30) | 18 |
| H11_v2_m800 (seed 42) | FACTOR (vs H8): yolov8m | 1115 | hand_val | 0.305 | 0.098 | 0.29 | 0.46 | 0.36 | 0.125 | 73 (43) | 52 |
| H11_v2_m800 (seed 42) | FACTOR (vs H8): yolov8m | 1115 | public_val | 0.108 | 0.029 | 0.24 | 0.17 | 0.20 | 0.250 | 73 (43) | 52 |
| H10_s7 (seed 7) | CONFIRM H10 with seed 7 | 1115 | hand_val | 0.293 | 0.095 | 0.34 | 0.39 | 0.37 | 0.150 | 37 (8) | 12 |
| H10_s7 (seed 7) | CONFIRM H10 with seed 7 | 1115 | public_val | 0.064 | 0.019 | 0.19 | 0.10 | 0.13 | 0.250 | 37 (8) | 12 |
| H10_s123 (seed 123) | CONFIRM H10 with seed 123 | 1115 | hand_val | 0.282 | 0.087 | 0.39 | 0.28 | 0.33 | 0.200 | 56 (26) | 18 |
| H10_s123 (seed 123) | CONFIRM H10 with seed 123 | 1115 | public_val | 0.087 | 0.027 | 0.13 | 0.22 | 0.17 | 0.125 | 56 (26) | 19 |
| H12_init_acne (seed 42) | FACTOR (vs H10): start from the final acne detector's weights (same skin domain) instead o | 1115 | hand_val | 0.278 | 0.097 | 0.41 | 0.27 | 0.33 | 0.200 | 45 (29) | 14 |
| H12_init_acne (seed 42) | FACTOR (vs H10): start from the final acne detector's weights (same skin domain) instead o | 1115 | public_val | 0.096 | 0.025 | 0.18 | 0.19 | 0.19 | 0.175 | 45 (29) | 14 |
| H13_init_mix (seed 42) | FACTOR (vs H10): start from the public+own model (H0), then fine-tune on your boxes only | 1115 | hand_val | 0.312 | 0.096 | 0.33 | 0.37 | 0.35 | 0.100 | 33 (3) | 10 |
| H13_init_mix (seed 42) | FACTOR (vs H10): start from the public+own model (H0), then fine-tune on your boxes only | 1115 | public_val | 0.223 | 0.066 | 0.45 | 0.23 | 0.31 | 0.200 | 33 (3) | 10 |
| H20_v2auto (seed 42) | FACTOR: train on old boxes converted to rule v2 by code; validated on your rule-v2 pass A | 1115 | v2_valA | 0.323 | 0.111 | 0.34 | 0.36 | 0.35 | 0.125 | 54 (53) | 17 |
| H20_s7 (seed 7) | CONFIRM H20 with seed 7 | 1115 | v2_valA | 0.318 | 0.111 | 0.40 | 0.42 | 0.41 | 0.125 | 35 (32) | 11 |
| H20_s123 (seed 123) | CONFIRM H20 with seed 123 | 1115 | v2_valA | 0.285 | 0.111 | 0.41 | 0.30 | 0.35 | 0.125 | 42 (12) | 14 |
| H21_v2auto_pub (seed 42) | FACTOR (vs H20): add the de-duplicated public photos (coarse boxes, close to rule v2) | 2375 | v2_valA | 0.392 | 0.146 | 0.45 | 0.48 | 0.46 | 0.075 | 69 (39) | 48 |
| H22_v2human_only (seed 42) | FACTOR: only the training photos you corrected under rule v2 (x3); validation = 60 rule-v2 | 600 | v2_val60 | 0.307 | 0.115 | 0.38 | 0.39 | 0.39 | 0.150 | 70 (32) | 15 |
| H23_v2human_plus (seed 42) | FACTOR: corrected photos (x3) + code-converted rest; validation = 60 rule-v2 photos | 1434 | v2_val60 | 0.383 | 0.132 | 0.49 | 0.41 | 0.45 | 0.150 | 59 (29) | 23 |
| H24_v2human_all (seed 42) | FACTOR: ALL 808 training photos corrected by you under rule v2 (x3); validation = 60 rule- | 2424 | v2_val60 | 0.381 | 0.141 | 0.47 | 0.43 | 0.45 | 0.150 | 51 (16) | 33 |

Validation sets: `hand_val` = 60 photos, old rule; `public_val` = public-style photos; `v2_valA` = 30 photos, rule v2; `v2_val60` = 60 photos, rule v2. Scores of different validation sets are not comparable.

## B. Installed detector (3-model average, rule v2) on the 100 rule-v2 test photos (226 boxes), scored once, threshold from validation

mAP@0.5 **0.341**. Threshold table (box level, IoU 0.5):

| threshold | precision | recall | F1 | zone F1 | photos with a patch | boxes per photo |
|---|---|---|---|---|---|---|
| 0.100 | 0.308 | 0.513 | 0.385 | 0.581 | 99 % | 3.8 |
| 0.125 | 0.361 | 0.460 | 0.405 | 0.568 | 97 % | 2.9 |
| 0.150 | 0.414 | 0.416 | 0.415 | 0.553 | 93 % | 2.3 |
| 0.175 (app) | 0.454 | 0.372 | 0.409 | 0.522 | 89 % | 1.9 |
| 0.200 | 0.476 | 0.350 | 0.403 | 0.520 | 85 % | 1.7 |
| 0.250 | 0.517 | 0.270 | 0.355 | 0.462 | 74 % | 1.2 |
| 0.300 | 0.567 | 0.226 | 0.323 | 0.408 | 64 % | 0.9 |

F1 by IoU needed (threshold 0.175): IoU 0.2: 0.594 | IoU 0.3: 0.550 | IoU 0.4: 0.496 | IoU 0.5: 0.409 | IoU 0.6: 0.311 | IoU 0.7: 0.165 | IoU 0.8: 0.054

- Patch counts per photo: your mean 2.26, model mean 1.85, Spearman 0.47, mean absolute error 1.19, exactly right in 31 % of photos, within 1 in 73 %
- Box quality of the patches found at IoU >= 0.3: median IoU 0.63 (25 % below 0.50, 75 % below 0.72); model box side / your box side: median 0.98
- Recall at IoU 0.5 by patch size: small 23 % (17 of 75), medium 33 % (25 of 76), large 56 % (42 of 75)
- Recall at IoU 0.5 by patches in the photo: 1 patch 53 % (19 of 36), 2-3 35 % (41 of 116), 4+ 32 % (24 of 74)
- The same photos with no patch in your labels: 2 photos (marked X), of which the model puts a box on 1