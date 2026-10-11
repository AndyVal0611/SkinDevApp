# PrecisionSkin / LUMYVUE: project map

Everything for the app and the machine-learning work lives in this folder. Big image data stays in `Downloads\PrecisionSkin\...`
(participant photos are never committed). Model files (`*.onnx`) are on disk but intentionally not in git.

```
SkinDevApp/                 the WPF app (C#). Model\ holds the ONNX models the app uses (now: classifier + acne detector only)
gradcam_service.py          Grad-CAM++ service the app starts (must stay in this root folder: the app looks for it here)
precisionskin_best.keras    classifier used by the Grad-CAM service
labels.json, requirements_gradcam.txt, start_gradcam_service.bat   belong to the Grad-CAM service
docs/                       every write-up: results, verdicts, plans, methods, setup guides
notebooks/                  all Jupyter notebooks (training pipelines, boxing sprints, MST skin tone)
ml/                         all Python tools for datasets, boxing helpers, experiments and reports
ml/scripts/                 launcher scripts (.sh for WSL, .ps1 watchdogs)
data_lists/                 CSV candidate lists for boxing
```

## Start here (docs/)
| File | What it is |
|---|---|
| `RESULTS_FOR_PAPER.md` | final acne numbers for the thesis (dev test + independent set) |
| `ACNE_DETECTOR_VERDICT.md` | strict review of the acne detector and the frozen evaluation protocol |
| `ACNE_ANNOTATION_AUDIT_AND_PLAN.md` | box audit, annotation protocol, experiment plan for acne |
| `HYPERPIGMENTATION_SUMMARY.md` | hyperpigmentation: how it was boxed, experiments, status |
| `LESION_DETECTOR_DIAGNOSIS_AND_PLAN.md` | why the old detectors were weak and the experiment design |
| `PROJECT_PROGRESS_SUMMARY.md` | the whole project status |
| `METHODS_AND_LIMITATIONS_NOTES.md`, `MST_PAPER_SECTION.md` | thesis text: methods, limits, skin-tone section |
| `SETUP_GUIDE_RUN_THE_APP.md`, `ROBOFLOW_TEAM_GUIDE.md` | how to run the app / team boxing guide |

## Notebooks
| Notebook | Use |
|---|---|
| `Pipeline V5 - Lesion Detector.ipynb` | **the final lesion-detector pipeline** (set `CONDITION` to acne / hyperpigmentation / eczema) |
| `PrecisionSkin_Acne_Detector_Final_3seeds.ipynb` | the acne final run that produced the installed model |
| `PrecisionSkin_SkinTone_MST_Training.ipynb` | Monk Skin Tone model (data not collected yet) |
| `PrecisionSkin_*Boxing_Sprint*`, `*Eczema*`, `*AutoBox*` | boxing and draft helpers used earlier |
| the other `PrecisionSkin_*` notebooks | earlier versions, kept for the record |

## Python tools (ml/) by job
- **Final training and install:** `install_specialist_detector.py`, `train_acne_v5.py`
- **Experiments:** `lesion_exp.py` (experiment queue and registry), `lesion_alt_metrics.py`, `lesion_exp_usage_metrics.py`, `acne_gate_rule.py`
- **Reports and checks:** `acne_final_report.py`, `acne_compare_report.py`, `acne_verdict_checks.py`, `acne_annotation_audit.py`, `lesion_diagnose_data.py`, `lesion_diagnose_models.py`
- **Build datasets:** `merge_acne_dataset.py`, `make_more_faces.py`, `make_independent_set.py`, `make_independent_hyp.py`
- **Boxing and drafts:** `lesion_box_tool.py` (the box tool), `acne_predraft.py`, `draft_more_faces.py`, `hyperpig_predraft.py`, `hyperpig_autolabel.py`, `hyp_redraft.py`, `acne_second_look.py`, `box_progress.py`, `eczema_review_tool.py`
- **Hyperpigmentation fixes:** `hyp_consistency.py`, `hyp_fix_check.py`, `make_hyp_consistency_fix.py`
- **Grad-CAM studies and old scans:** `gradcam_diagnose.py`, `gradcam_layer_study.py`, `gradcam_enrichment.py`, `upgrade_existing_scans.py`
- **Skin tone:** `mst_skintone.py`;  **speed:** `benchmark_pc.py`

## Running things (WSL)
Python tools run in WSL from the `.venv_det` environment and read their data from `Downloads\PrecisionSkin\...` and `~/PrecisionSkinV1`.
Scripts point to this folder: `/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/`.
Launchers: `ml/scripts/run_hyp_v2.sh` (hyperpigmentation round 2), `run_acne_audit.sh`, `keep_queue_alive_v2.ps1` (restarts WSL if it crashes).

## Not moved
Training data and model runs: `Downloads\PrecisionSkin\*` and WSL `~/PrecisionSkinV1`. Old copies of the scripts above are still in `Downloads`
(a job that was already running uses them); they can be archived after the current training finishes.
