# LUMYVUE V6 - machine learning results summary

Source: `v6/astra_runs/open_run02/metrics/v5_vs_v6_fair_set.csv` and `v5_vs_v6_fair_set_paired.csv` (run open_run02, finished 2026-11-02).
The locked test split was never opened (it has 0 labels). Nothing below is an independent test or a clinical validation.

## Fair set
132 fully verified validation photographs that V5 had not seen: 47 hyperpigmentation-positive, 26 with no condition at all, 18 eczema-positive.
Small set: every figure below carries wide uncertainty.

## Systems compared

| System | Hyper. missed (of 47) | Hyper. recall | Clean photos flagged hyper. (of 26) | F1 acne | F1 hyper. | F1 eczema | Macro-F1 |
|---|---|---|---|---|---|---|---|
| V5 argmax (current baseline) | 9 | 51% | 1 | 0.835 | 0.658 | 0.718 | 0.737 |
| V5 with tuned thresholds | 7 | 62% | 2 | 0.836 | 0.725 | 0.667 | 0.743 |
| Global only | 10 | 57% | 3 | 0.852 | 0.701 | 0.647 | 0.733 |
| **Global + YOLO (deployed `global_yolo`)** | 10 | 57% | 2 | 0.870 | 0.711 | 0.632 | 0.737 |
| Full fusion (with ROI branch) | 8 | 70% | 2 | 0.862 | 0.759 | 0.645 | 0.755 |
| ROI only | 9 | 62% | 1 | 0.830 | 0.734 | 0.629 | 0.731 |

## Paired comparison against V5 (95% bootstrap CI)

| System | Metric | Difference | 95% CI | Excludes 0? |
|---|---|---|---|---|
| Global + YOLO | macro-F1 | +0.001 | -0.084 to +0.074 | no |
| Global + YOLO | hyper. missed rate | +0.021 (worse) | 0.000 to +0.076 | no |
| Global + YOLO | false-alarm rate | +0.038 (worse) | -0.081 to +0.185 | no |
| Full fusion | macro-F1 | +0.019 | -0.086 to +0.112 | no |
| Full fusion | hyper. missed rate | -0.021 (better) | -0.093 to +0.053 | no |
| Full fusion | false-alarm rate | +0.038 (worse) | 0.000 to +0.136 | no |

## Verdict
- The pre-registered criterion was **not met**: no clear improvement of V6 over V5 on the V5-unseen set. Every paired interval includes zero.
- False alarms were not better than V5, and eczema F1 was below V5.
- V5 with thresholds tuned on the fusion split already matches most of the gain (macro-F1 0.743 vs 0.737).
- Eczema: V6 ranks better (AP 0.75 vs 0.66) but loses at the calibrated cutoff, on only 18 positives.
- The ROI branch adds nothing over Global + YOLO.

## Deployment checks (software, not accuracy)
- C# vs Python reference detector boxes: 270 boxes, 0.0001 px difference.
- Fusion model, CPU ONNX vs Keras: max difference 1.9e-6, 0 of 1200 decisions flipped.
- Detector-only reading on the same fair set: 91% recall but 27% of clean photos flagged, which is why V6 shows "Possible" for a detector mark the fusion did not confirm.

## What can honestly be claimed
V6 delivers one unified, auditable result with accuracy comparable to V5 on a small fair set. It does not show a statistically clear accuracy gain. Independent test, dermatologist review and consent are pending.
