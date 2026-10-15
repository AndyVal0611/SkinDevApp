# Acne: full results of the 960 px candidate vs the installed model (generated)

Descriptive breakdown. The threshold of each model set is the best F1 on the **validation** photos; the development test (167 photos) was already scored for both and is not used to decide anything here. The decisive test is the fresh test set (Phase 3).

## 1. Headline table

| measure | 960 px candidate (3-model average) | installed baseline (3-model average, 800 px) |
|---|---|---|
| image size (px) | 960 | 800 |
| confidence threshold (from validation) | 0.225 | 0.275 |
| VALIDATION mAP@0.5 | 0.471 | 0.460 |
| VALIDATION mAP@0.5-0.95 | 0.148 | 0.144 |
| VALIDATION precision / recall / F1 | 0.487 / 0.542 / 0.513 | 0.514 / 0.509 / 0.511 |
| DEV TEST mAP@0.5 | **0.481** | **0.439** |
| DEV TEST mAP@0.5-0.95 | 0.138 | 0.127 |
| DEV TEST precision | 0.454 | 0.465 |
| DEV TEST recall | 0.601 | 0.556 |
| DEV TEST F1 (95 % range) | 0.517 (0.48-0.55) | 0.507 (0.47-0.54) |
| DEV TEST 3x3 face-zone F1 | 0.831 | 0.832 |
| photos with at least one box | 96 % | 98 % |
| F1 at IoU 0.3 / 0.5 / 0.7 | 0.638 / 0.517 / 0.194 | 0.639 / 0.507 / 0.181 |
| lesion count: mean error / within 2 / Spearman | 4.52 / 50 % / 0.77 | 4.14 / 51 % / 0.74 |

## 2. The three single seeds on the development test (mAP@0.5 / mAP@0.5-0.95, each at its own scoring)

| seed order | 960 px candidate (3-model average) | installed baseline (3-model average, 800 px) |
|---|---|---|
| model 1 | 0.453 / 0.137 | 0.419 / 0.126 |
| model 2 | 0.430 / 0.127 | 0.412 / 0.121 |
| model 3 | 0.436 / 0.130 | 0.408 / 0.120 |

## 3. Recall at IoU 0.5 on the development test (what share of your boxes the model finds)

| group | 960 px candidate (3-model average) | installed baseline (3-model average, 800 px) |
|---|---|---|
| spot size: small | 51 % (232 of 452) | 41 % (187 of 452) |
| spot size: medium | 65 % (304 of 465) | 60 % (280 of 465) |
| spot size: large | 63 % (287 of 452) | 65 % (294 of 452) |
| photo density: sparse (<=6) | 71 % (179 of 251) | 69 % (173 of 251) |
| photo density: medium (7-14) | 61 % (348 of 570) | 56 % (318 of 570) |
| photo density: dense (15+) | 54 % (296 of 548) | 49 % (270 of 548) |

## 4. Threshold table, development test (candidate, then baseline)

**960 px candidate (3-model average)**

| threshold | precision | recall | F1 | zone F1 | photos with a box |
|---|---|---|---|---|---|
| 0.150 | 0.397 | 0.657 | 0.495 | 0.816 | 100 % |
| 0.200 | 0.436 | 0.622 | 0.512 | 0.826 | 97 % |
| 0.225 | 0.454 | 0.601 | 0.517 | 0.831 | 96 % |
| 0.250 | 0.469 | 0.581 | 0.519 | 0.831 | 96 % |
| 0.275 | 0.481 | 0.562 | 0.518 | 0.829 | 95 % |
| 0.300 | 0.497 | 0.546 | 0.520 | 0.827 | 94 % |
| 0.350 | 0.528 | 0.514 | 0.521 | 0.817 | 93 % |
| 0.400 | 0.556 | 0.476 | 0.513 | 0.807 | 93 % |

**installed baseline (3-model average, 800 px)**

| threshold | precision | recall | F1 | zone F1 | photos with a box |
|---|---|---|---|---|---|
| 0.150 | 0.380 | 0.620 | 0.471 | 0.820 | 100 % |
| 0.200 | 0.410 | 0.584 | 0.482 | 0.820 | 99 % |
| 0.225 | 0.430 | 0.575 | 0.492 | 0.827 | 99 % |
| 0.250 | 0.449 | 0.567 | 0.501 | 0.827 | 99 % |
| 0.275 | 0.465 | 0.556 | 0.507 | 0.832 | 98 % |
| 0.300 | 0.475 | 0.539 | 0.505 | 0.828 | 97 % |
| 0.350 | 0.504 | 0.514 | 0.509 | 0.835 | 95 % |
| 0.400 | 0.526 | 0.476 | 0.500 | 0.821 | 95 % |

## 5. Cost
The candidate uses 960 px input: about 1.4 times the time per model of the 800 px baseline (the app runs three models of each condition).