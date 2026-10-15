# PrecisionSkin / LUMYVUE: project map

One folder per condition. Big image data stays in `Downloads\PrecisionSkin\...` (participant photos are never committed). Model files (`*.onnx`, `*.pt`) are on disk but intentionally not in git.

```
SkinDevApp/                 the WPF app (C#). Model\ holds the ONNX models the app uses: classifier + acne (3 models) + hyperpigmentation (3 models)
gradcam_service.py          Grad-CAM++ service the app starts (must stay in this root folder: the app looks for it here)
precisionskin_best.keras, labels.json, requirements_gradcam.txt, start_gradcam_service.bat   belong to the Grad-CAM service

acne/                       everything about the acne detector
  notebooks/                training and results notebooks (final: PrecisionSkin_Acne_Training_and_Results.ipynb), data comparison, boxing sprint
  scripts/                  dataset building, drafts, audits, ensemble checks, launchers (.sh)
  docs/                     verdict, annotation audit and plan
  data_lists/               candidate lists for boxing
hyperpigmentation/          everything about the hyperpigmentation detector
  notebooks/                PrecisionSkin_Hyperpigmentation_Training_and_Results.ipynb (final)
  scripts/                  evaluation, rule-v2 tools, reliability checks, drafts, launchers
  docs/                     annotation rule v2, full results
  data_lists/
eczema/                     eczema notebooks, review tool, team guide (a teammate trains the eczema detector)
skintone/                   Monk Skin Tone model: notebook, mst_skintone.py, download_scin.py, paper section
gradcam/                    Grad-CAM studies and the tool that upgrades existing scans
shared/                     tools used by several conditions
  lesion_exp.py             experiment runner and registry (lesion_experiments/EXPERIMENTS.csv)
  lesion_box_tool.py        the box-drawing tool used for all boxing
  install_specialist_detector.py, build_report_notebooks.py, benchmark_pc.py, lesion_* diagnostics
  notebooks/                the general lesion-detector pipelines (Pipeline V5 etc.)
  docs/                     lesion detector diagnosis and plan
docs/                       general documents: results for the paper, methods and limits, training explained for the defense, progress summary, setup guide
```

## Start here (docs/)
| File | What it is |
|---|---|
| `docs/RESULTS_FOR_PAPER.md` | final numbers and thesis wording, acne and hyperpigmentation |
| `docs/TRAINING_EXPLAINED_FOR_DEFENSE.md` | how the detectors were trained, in plain language, with likely panel questions |
| `docs/METHODS_AND_LIMITATIONS_NOTES.md` | methods text and limits |
| `docs/PROJECT_PROGRESS_SUMMARY.md` | whole-project status |
| `acne/docs/ACNE_DETECTOR_VERDICT.md` | strict review of the acne detector and the frozen evaluation protocol |
| `hyperpigmentation/docs/HYPERPIGMENTATION_FULL_RESULTS.md`, `..._ANNOTATION_RULE_V2.md` | full results and the boxing rule |

## Rules for training
Final or reported trainings run through a notebook that is executed headlessly and saved as `..._WITH_OUTPUTS.ipynb` (one line per finished epoch stays in the file). Training happens in WSL (`~/PrecisionSkinV1`, environment `.venv_det`).

## Running things (WSL)
Python tools find each other through a path line at the top of each script (`shared`, `acne/scripts`, `hyperpigmentation/scripts`, `eczema/scripts`, `skintone`, `gradcam`). They read data from `Downloads\PrecisionSkin\...` and `~/PrecisionSkinV1`.

## Not in the repo
Training photos and WSL runs (`~/PrecisionSkinV1`), a training backup in `Documents\PrecisionSkin_Training_Backup`, and an archive of obsolete files in `Downloads\_ARCHIVE_to_delete_later`.
