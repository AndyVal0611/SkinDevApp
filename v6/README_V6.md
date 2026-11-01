# LUMYVUE V6 — Three-Detector YOLOv8-Guided MobileNetV2 with Trainable Fusion

This folder contains the **complete, unexecuted** V6 training-and-evaluation notebook and everything it needs. **You run the training; nothing here has been trained or evaluated, and no result in this folder is experimental.**

| File | Purpose |
|---|---|
| `LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training.ipynb` | the notebook (142 cells; no stored outputs) |
| `v6_dataset_config.example.json` | dataset / run configuration (copy, edit, point `LUMYVUE_V6_CONFIG` at it) |
| `requirements_v6.txt` | dependency versions of the training environment that was inspected |
| `annotations/*.csv`, `annotations/ANNOTATION_PROTOCOL.md` | annotation, provenance and **dermatologist-review** templates (headers only; no labels invented) |
| `docs/` | architecture, checklist, export spec, deployment guide, evaluation methodology, **`REVIEW_CONTRACT.md`** (mandatory dermatologist review, sealed AI output, separate findings), readiness and validation reports |
| `notebook_src/`, `tools/` | the notebook sources (one file per section group), the builder, the static validator and the gated dry-run script |

## 1. Required inputs (all configurable; nothing is hard-coded to another computer)

| Input | Default (WSL) | Needed for |
|---|---|---|
| V5 split manifest `split_manifest.csv` | `~/PrecisionSkinV1/metadata/metadata/` | everything |
| Processed 224×224 images `<split>/<class>/<file>` | `~/PrecisionSkinV1/dataset_processed/processed/` | global CNN, V5 inference |
| **Original** full-size images `<Class>/<file>` | `/mnt/c/Users/*/Downloads/PrecisionSkin/Classes/` (auto-discovered) | YOLO and ROI crops at their real resolution (strongly recommended) |
| V5 ONNX (+ Keras for experiments B/C) | `~/PrecisionSkinV1/runs/facial_only/models/final/` | baseline, parity, fine-tuning |
| YOLO ONNX models | `/mnt/c/Users/*/source/repos/SkinDevApp/SkinDevApp/Model/` (auto-discovered) | three-detector branch |
| **Eczema detector** | installed in the WPF `Model/` folder (4 files, in the csproj, copied to `bin/Debug/Model`) | Eczema localisation, three-detector gates | Eczema localisation, three-detector gates |
| ImageNet MobileNetV2 `no_top` weights | `~/.keras/models/` (already cached) | backbones (`OFFLINE_MODE` blocks downloads) |
| Verified multilabel annotations | `~/PrecisionSkinV1/v6/annotations/multilabel_annotations.csv` | **all supervised stages** |
| Verified region annotations | `…/region_annotations.csv` | ROI classifier, localisation metrics |
| Dataset provenance | `…/dataset_provenance.csv` | training gate |
| Optional: app capture folder, OOD reference images, `--diagnose` folders | see config | domain audit, OOD AUROC, C# parity |

Override anything with an environment variable `LUMYVUE_<NAME>` (JSON for lists/dicts) or a JSON file in `LUMYVUE_V6_CONFIG`.

## 2. How to run (WSL, RTX 4060 8 GB)

```bash
cd ~/PrecisionSkinV1            # or wherever you keep the project
source .venv/bin/activate       # Python 3.13, TensorFlow 2.21, Keras 3.15 (inspected versions: requirements_v6.txt)
jupyter lab                     # open LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training.ipynb  (VS Code works too)
```
Run **top to bottom**, one section at a time. The notebook is safe to interrupt and re-run with the same `RUN_ID` (training resumes from checkpoints). Use a new `RUN_ID` for a new experiment.

**Stage flow and what blocks what**

1. *Parts I–III (no annotations needed):* environment, dataset audit, leakage groups, **frozen 4-way split**, annotation templates, V5 parity and baseline error analysis, hard-example candidates, review queue. Run these first.
2. **Annotate** (see `annotations/ANNOTATION_PROTOCOL.md`): validation, fusion, test first (the template is ordered by a random `review_rank`), then train. Fill `dataset_provenance.csv`.
3. *Part IV* global experiments B, C, D, D2 → selection on validation.
4. *Part V* YOLO inference (resumable cache), diagnostics. *Part VI* ROI tables + ROI MobileNetV2.
5. *Part VII* fusion (cross-fitted), calibration, thresholds. *Part VIII* Stage 1/2 rules, OOD.
6. *Part IX* evaluation — **opens the test split once**. *Part X* export, tests, report.

Every blocked stage prints the exact shortfall (for example `Acne present 12 < 30 in validation`) and does **not** create data.

## 3. Eczema detector (verified, adjustable, not assumed)
The Eczema files are now in the WPF `Model/` folder, listed in the csproj and copied to `bin/Debug/Model` by the build: `lesion_detector_eczema.onnx`, `_m2.onnx`, `_m3.onnx`, `lesion_detector_eczema.json`. Loaded and checked with ONNX Runtime: 3 distinct models, input `[1,3,640,640]`, output `[1,5,8400]`, class map `{0: eczema}`, NMS 0.6, threshold 0.25, JSON hash matches the primary file; the notebook's detector engine ran all three on real images. The notebook registers Eczema from, in order: explicit `LUMYVUE_ECZEMA_ONNX_FILES`/`ECZEMA_META_JSON`/`ECZEMA_THRESHOLD`; `lesion_detector_eczema*` in the WPF `Model/` folder; the folders in `ECZEMA_SEARCH_DIRS` (default: Downloads, read-only — the notebook prints where it found them); then the 3-class v1 file. Still missing for reproducibility: the Eczema training notebook/dataset fingerprint; its JSON records heterogeneous small-set results (mAP@0.5 0.52 on 39 photos, 0.12, 0.181) from one annotator and a fusion IoU of 0.55 where the app fuses at 0.5 — none of this is treated as validated.

## 3b. Dermatologist review (mandatory)
Every AI assessment is issued as `PENDING_DERMATOLOGIST_REVIEW`, sealed (SHA-256) and never edited; the dermatologist's findings are a separate record (`annotations/dermatologist_reviews.csv`, schema in `docs/REVIEW_CONTRACT.md`). Stage 2 is **AI verification, not clinical validation**.

## 4. What the notebook does **not** do
No training or evaluation has been run by the author. It does not modify V5/YOLO models, datasets or the WPF app; it does not deploy; it does not upload anything; it makes no regulatory claim. Production security controls (disk encryption, access control, retention, tamper detection, update channel) must be verified in the C# application.
