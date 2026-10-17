#!/usr/bin/env python
"""build_evidence_notebook.py - writes shared/notebooks/PrecisionSkin_Training_Evidence_Acne_and_Hyperpigmentation.ipynb
A READ-ONLY documentation notebook: it reads the records that the real training runs left behind (run folders, results.csv, args.yaml, logs, labeling logs, locks) and re-evaluates the
finished models. It contains NO training code and writes nothing except its own output. Run in WSL:  python build_evidence_notebook.py"""
import sys
from pathlib import Path
import nbformat as nbf
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/shared"]
import build_report_notebooks as B

OUT = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/shared/notebooks/PrecisionSkin_Training_Evidence_Acne_and_Hyperpigmentation.ipynb")
C = []
md = lambda s: C.append(nbf.v4.new_markdown_cell(s.strip("\n")))
code = lambda s: C.append(nbf.v4.new_code_cell(s.strip("\n")))

md("""# PrecisionSkin: training and evaluation record of the Acne and Hyperpigmentation lesion detectors

**What this notebook is.** A documentation and evidence notebook for the thesis panel. It collects, in one place, the records that the real training runs left on the researcher's computer (run folders, per-epoch `results.csv`, `args.yaml`, experiment registry, logs, box-tool labeling logs, stored test results) and re-evaluates the finished models.

**What this notebook is not.**
- It is **not** the notebook in which the models were trained, and it was **created after training** (the date it was generated and run is printed below). It does not claim otherwise.
- It contains **no training code**. It does not retrain, fine-tune, overwrite, modify or replace any model, label or dataset. Everything it shows is read from files, or re-computed from the finished models on existing photos.
- Where a record is missing, it says **MISSING** instead of filling the gap.

**How to read it.** Section 1 states who did what. Sections 2-7 are the evidence, one block per condition. Section 8 lists everything that is missing or limited. Section 9 gives commands that let anyone check the files themselves.

*Dates in this notebook are file-system timestamps and log timestamps. They are evidence, but they are not signed or tamper-proof records.*""")

md("## 0. Environment of this run and of the original training")
code(r'''
import os, sys, json, glob, re, time, datetime, hashlib, platform, warnings, shutil
from pathlib import Path
import numpy as np, pandas as pd, matplotlib.pyplot as plt
from PIL import Image
warnings.filterwarnings("ignore")
pd.set_option("display.max_rows", None); pd.set_option("display.width", 230); pd.set_option("display.max_colwidth", 100)
HOME = Path.home() / "PrecisionSkinV1"; REPO = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp"); DL = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin")
EXP = HOME / "lesion_experiments"; RUNS = EXP / "runs"; ACNE_RUN = HOME / "runs" / "lesion_acne_boxed_only"; ACNE_DS = DL / "Acne_Boxing" / "acne_dataset_v4"; MODEL_DIR = REPO / "SkinDevApp" / "Model"
MISSING = []
def missing(what): MISSING.append(what); print("MISSING:", what)
def fdate(p):
    try: return datetime.datetime.fromtimestamp(Path(p).stat().st_mtime).strftime("%Y-%m-%d %H:%M")
    except Exception: return "n/a"
def sha256(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()
import torch, ultralytics
print("This notebook was generated and run on:", datetime.datetime.now().strftime("%Y-%m-%d %H:%M"))
print("Python", platform.python_version(), "|", platform.platform()[:60]); print("torch", torch.__version__, "| ultralytics", ultralytics.__version__, "| GPU:", torch.cuda.get_device_name(0) if torch.cuda.is_available() else "none")
print("\nLines recorded by the ORIGINAL training runs in the experiment queue logs (library and hardware at training time):")
seen = {}
for lg in sorted(EXP.glob("queue*.log")):
    try:
        for line in open(lg, encoding="utf-8", errors="ignore"):
            m = re.search(r"Ultralytics [\d.]+ .*?CUDA:0 \(.*?\)", line.replace("\r", "\n"))
            if m: seen.setdefault(m.group(0), []).append(lg.name); break
    except Exception: pass
for k, v in seen.items(): print("  ", k, "  <-", ", ".join(sorted(set(v)))[:140])
if not seen: missing("no training-time library line found in the queue logs")
''')

md("""## 1. Who did what (read this first)

This table states, as plainly as the records allow, which part of the work was done by whom. It was written from the project working records and **the researcher should confirm each row before presenting it**.

| Work | Done by | Evidence in this notebook |
|---|---|---|
| Collecting and boxing the photos: the acne dataset (training, validation, test, independent and fresh test sets), the hyperpigmentation labels under the first rule and under rule v2, the blind re-box passes, the clear-skin photos | **The researcher** (annotator "Elaiza"), with the project's box tool; model drafts were sometimes pre-drawn and then corrected by the researcher | the box tool's `labeling_log.csv` files (section 2) |
| Final acne 3-seed training (the installed acne models) | **The researcher**, by running the notebook "Pipeline V5 - Lesion Detector" in VS Code on 10 Oct 2026 | the run folder `runs/lesion_acne_boxed_only` and its timestamps (section 4), and the **executed notebook saved with its full console output** (section 4.3) |
| Acne experiments A0-A7 and C1-C3, hyperpigmentation experiments H0-H25 (including the final hyperpigmentation training) | An **AI coding assistant (Claude Code)** working in the researcher's repository, executing the scripts and notebooks on the researcher's computer, at the researcher's request | `EXPERIMENTS.csv`, queue logs, run folders (sections 4-5) |
| Acne Phase 1 experiments (E1-E4) and the scoring of the fresh acne test set | **The researcher**, running the notebooks in VS Code | run folders, lock files (sections 5, 6) |
| Acne Phase 2 candidate (960 px, 3 seeds) | The AI assistant, executing the notebook headlessly | run folders |
| Design of the pipeline, scripts, notebooks, analyses and write-ups | Built **with the help of an AI coding assistant** | git history of the repository |

The researcher is responsible for the data, the decisions and the claims; the assistant wrote and ran much of the code. Panel members should be told this in the same words.""")

# ------------------------------------------------------------------------------------------------ toolbox
md("## Helper functions\nThe same box-matching and scoring functions as the project's other notebooks (nothing here trains).")
code(B.TOOLBOX)

# ------------------------------------------------------------------------------------------------ 2 data and annotation
md("""## 2. Dataset preparation and annotation process

**Annotation process (from the project documents, `docs` folders of the repository).**
- *Acne:* individual spots are boxed one box per visible spot with the project's box tool. Many photos were first pre-drawn by a helper model and then corrected photo by photo by the researcher. The annotation audit (`acne/docs/ACNE_IMPROVEMENT_AUDIT_AND_PLAN.md`) found individual lesions (not grouped regions) and measured the researcher's agreement with their own earlier boxes.
- *Hyperpigmentation:* first boxed under a loose rule (agreement of the researcher with their own boxes F1 0.28 at IoU 0.5), then a stricter written rule v2 (`hyperpigmentation/docs/HYPERPIGMENTATION_ANNOTATION_RULE_V2.md`) with blind re-box passes and hand correction of the training photos.
The numbers below are **re-computed from the label files now**.""")
code(r'''
def split_stats(name, d):
    d = Path(d)
    if not (d / "images").exists(): missing("dataset folder " + str(d)); return None
    ims = images_of(d); nb = [len(rd(d / "labels" / (p.stem + ".txt"))) for p in ims]; sd = [side(b) for p in ims for b in rd(d / "labels" / (p.stem + ".txt"))]
    return dict(set=name, photos=len(ims), photos_without_boxes=sum(1 for x in nb if x == 0), boxes=sum(nb), boxes_per_photo=round(float(np.mean(nb)), 1), median_box_side=round(float(np.median(sd)), 3) if sd else None, folder=str(d))
print("ACNE dataset v4 and the later sets"); A = [split_stats("train", ACNE_DS / "train"), split_stats("validation", ACNE_DS / "val"), split_stats("development test", ACNE_DS / "test"),
     split_stats("independent set (41 photos)", DL / "Acne_Boxing" / "Independent_Final_Set" / "boxed_independent"), split_stats("fresh test (boxed from scratch)", EXP / "data" / "acne_fresh_test")]
display(pd.DataFrame([a for a in A if a]))
print("\nHYPERPIGMENTATION (annotation rule v2)")
tl = RUNS / "H25_v2human_all_pub" / "train_list.txt"
if tl.exists():
    lst = [l.strip() for l in tl.read_text().splitlines() if l.strip()]; own = [p for p in lst if "hyp_v2human_only" in p]
    print("training list of the final run: %d images = %d images from the researcher's corrected photos (each photo listed 3 times: original and 2 copies, i.e. about %d photos) + %d public photos" % (len(lst), len(own), len(set(Path(p).name.split("_rep")[0] for p in own)), len(lst) - len(own)))
else: missing("training list of the final hyperpigmentation run")
H = [split_stats("validation (60 photos, rule v2)", EXP / "data" / "hyp_v2_val60"), split_stats("test (100 photos, rule v2, boxed from scratch)", EXP / "data" / "hyp_v2_test")]
display(pd.DataFrame([h for h in H if h]))
''')
md("""### 2.1 Manual boxing activity recorded by the box tool
The box tool writes a `labeling_log.csv` next to every labelling folder: one row per saved photo with the time it was saved, the annotator name, the number of boxes and the seconds spent. This is the strongest available evidence that the boxes were drawn by hand. (`seconds` is the active time the tool measured for that photo.)""")
code(r'''
rows = []
for cond, base in (("acne", DL / "Acne_Boxing"), ("hyperpigmentation", DL / "Hyperpigmentation_Boxing")):
    for f in sorted(base.rglob("labeling_log.csv")):
        try:
            d = pd.read_csv(f); d["timestamp"] = pd.to_datetime(d["timestamp"]); s = d[d["action"].isin(["save", "none", "x", "empty"])] if "action" in d else d
            rows.append(dict(condition=cond, folder=str(f.parent.parent.relative_to(base)), photos_logged=d["image"].nunique(), saved_actions=len(d), boxes_saved=int(d["n_boxes"].sum()), active_minutes=round(float(d["seconds"].sum()) / 60, 1),
                             first=d["timestamp"].min().strftime("%Y-%m-%d %H:%M"), last=d["timestamp"].max().strftime("%Y-%m-%d %H:%M"), annotator=",".join(sorted(d["annotator"].astype(str).unique()))))
        except Exception as e: missing("could not read " + str(f) + " (" + str(e)[:50] + ")")
LOG = pd.DataFrame(rows); display(LOG)
for cond in ("acne", "hyperpigmentation"):
    s = LOG[LOG.condition == cond]
    if len(s): print("%-18s %d labelling folders | %d photo records | %d box-count total over all saves (includes boxes pre-drawn by a helper model and kept by the researcher; a photo saved twice counts twice) | about %.1f hours of active tool time | between %s and %s" % (cond, len(s), s.photos_logged.sum(), s.boxes_saved.sum(), s.active_minutes.sum() / 60, s["first"].min(), s["last"].max()))
''')

# ------------------------------------------------------------------------------------------------ 3 architecture
md("""## 3. Model architecture and training configuration (read from `args.yaml` of the real runs)
Both detectors are the **official Ultralytics YOLOv8s** (small) model, starting from the COCO-pretrained `yolov8s.pt`, one class each, trained with three random seeds. The configuration below is read from each run's own `args.yaml` (the settings Ultralytics itself recorded when the run started).""")
code(r'''
import yaml
KEYS = ["model", "imgsz", "epochs", "patience", "batch", "optimizer", "lr0", "cos_lr", "close_mosaic", "seed", "deterministic", "amp", "workers", "fliplr", "degrees", "scale", "mosaic", "hsv_h", "hsv_s", "hsv_v", "data"]
def read_args(run):
    f = Path(run) / "args.yaml"
    if not f.exists(): missing("args.yaml of " + str(run)); return None
    a = yaml.safe_load(open(f)); r = {k: a.get(k) for k in KEYS}; r["run"] = Path(run).name; r["args.yaml written"] = fdate(f); r["data"] = Path(str(r["data"])).name; return r
ACNE_SEEDS = {s: ACNE_RUN / "models" / ("seed_%d" % s) for s in (123, 42, 7)}
HYP_RUNS = {42: RUNS / "H25_v2human_all_pub", 7: RUNS / "H25_s7", 123: RUNS / "H25_s123"}
print("ACNE final runs (the models installed in the app):"); display(pd.DataFrame([r for r in (read_args(p) for p in ACNE_SEEDS.values()) if r]).set_index("run").T)
print("\nHYPERPIGMENTATION final runs (H25, the models installed in the app):"); display(pd.DataFrame([r for r in (read_args(p) for p in HYP_RUNS.values()) if r]).set_index("run").T)
from ultralytics import YOLO
for name, w in (("acne seed 123", ACNE_SEEDS[123] / "weights" / "best.pt"), ("hyperpigmentation seed 42", HYP_RUNS[42] / "weights" / "best.pt")):
    if Path(w).exists():
        mm = YOLO(str(w)).model; npar = sum(q.numel() for q in mm.parameters())
        try:
            from ultralytics.utils.torch_utils import get_flops; gf = "%.1f" % get_flops(mm, 800)
        except Exception: gf = "n/a"
        print("%-26s architecture %s | trainable parameters %s | GFLOPs at 800 px %s | modules %d" % (name, type(mm).__name__, "{:,}".format(npar), gf, len(list(mm.modules()))))
    else: missing("weights of " + name)
''')

# ------------------------------------------------------------------------------------------------ 4 training history
md("## 4. Training history of the final runs (per-epoch records written by Ultralytics during the real training)")
code(r'''
def history(title, runs):
    print("=" * 110); print(title); out = []
    for s, run in runs.items():
        f = Path(run) / "results.csv"
        if not f.exists(): missing("results.csv of %s seed %s" % (title, s)); continue
        r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]; b = r["metrics/mAP50(B)"].idxmax(); w = Path(run) / "weights" / "best.pt"
        out.append(dict(seed=s, epochs_trained=len(r), best_epoch_by_mAP50=int(r.loc[b, "epoch"]), best_val_mAP50=round(r.loc[b, "metrics/mAP50(B)"], 3), best_val_mAP50_95=round(r.loc[b, "metrics/mAP50-95(B)"], 3),
                        P_at_best=round(r.loc[b, "metrics/precision(B)"], 3), R_at_best=round(r.loc[b, "metrics/recall(B)"], 3), training_minutes=round(float(r["time"].iloc[-1]) / 60, 1),
                        results_csv_dated=fdate(f), best_pt_dated=fdate(w) if w.exists() else "weights not found", best_pt_bytes=w.stat().st_size if w.exists() else None))
    display(pd.DataFrame(out))
history("ACNE: three seeds of the final training (800 px, YOLOv8s)", ACNE_SEEDS); history("HYPERPIGMENTATION: three seeds of the final training (800 px, YOLOv8s, rule-v2 labels)", HYP_RUNS)
''')
code(r'''
def curves_for(title, runs):
    fig, ax = plt.subplots(1, 4, figsize=(18, 3.8))
    for s, run in runs.items():
        f = Path(run) / "results.csv"
        if not f.exists(): continue
        r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]
        ax[0].plot(r["epoch"], r["train/box_loss"], label="seed %s train" % s); ax[0].plot(r["epoch"], r["val/box_loss"], "--", label="seed %s val" % s)
        ax[1].plot(r["epoch"], r["metrics/mAP50(B)"], label="seed %s" % s); ax[2].plot(r["epoch"], r["metrics/mAP50-95(B)"], label="seed %s" % s)
        ax[3].plot(r["epoch"], r["metrics/precision(B)"], label="P seed %s" % s); ax[3].plot(r["epoch"], r["metrics/recall(B)"], "--", label="R seed %s" % s)
    for a, t in zip(ax, ("box loss (training solid, validation dashed)", "validation mAP@0.5", "validation mAP@0.5:0.95", "validation precision (solid) and recall (dashed)")): a.set_title(t, fontsize=9); a.set_xlabel("epoch"); a.legend(fontsize=6); a.grid(alpha=.3)
    fig.suptitle(title); plt.tight_layout(); plt.show()
curves_for("ACNE: training curves (all three seeds)", ACNE_SEEDS); curves_for("HYPERPIGMENTATION: training curves (all three seeds)", HYP_RUNS)
''')
md("### 4.1 Plots and confusion matrices saved by Ultralytics during the real runs\nThese images were written by the training library itself at the time of the runs. If an image was not saved, it says MISSING.")
code(r'''
def show_pngs(title, run, names):
    fig, ax = plt.subplots(1, len(names), figsize=(5.2 * len(names), 4.6))
    for a, n in zip(np.atleast_1d(ax), names):
        a.axis("off"); p = Path(run) / n
        if p.exists(): a.imshow(Image.open(p)); a.set_title("%s\n(saved %s)" % (n, fdate(p)), fontsize=8)
        else: a.text(0.5, 0.5, "MISSING\n" + n, ha="center"); missing("%s: %s" % (title, n))
    fig.suptitle(title, fontsize=10); plt.tight_layout(); plt.show()
NAMES = ["results.png", "confusion_matrix.png", "BoxPR_curve.png", "BoxF1_curve.png"]
show_pngs("ACNE seed 123 (the main installed model)", ACNE_SEEDS[123], NAMES); show_pngs("HYPERPIGMENTATION seed 42 (the main installed model)", HYP_RUNS[42], NAMES)
print("Note: Ultralytics' confusion matrix for a one-class detector has the classes 'acne' / 'hyperpigmentation' and 'background'; the detection counts (found / false / missed) at the chosen threshold are computed in section 7.")
''')
md("### 4.2 Every finished epoch of every final seed (the saved `results.csv`)")
code(r'''
cols = {"epoch": "epoch", "time": "elapsed_s", "train/box_loss": "box_loss", "train/cls_loss": "cls_loss", "train/dfl_loss": "dfl_loss", "metrics/precision(B)": "P", "metrics/recall(B)": "R", "metrics/mAP50(B)": "mAP50", "metrics/mAP50-95(B)": "mAP50-95", "val/box_loss": "val_box_loss"}
for title, runs in (("ACNE", ACNE_SEEDS), ("HYPERPIGMENTATION", HYP_RUNS)):
    for s, run in runs.items():
        f = Path(run) / "results.csv"
        if not f.exists(): continue
        r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]; t = r[list(cols)].rename(columns=cols); t["epoch"] = t["epoch"].astype(int); t["new best mAP50"] = np.where(t["mAP50"] == t["mAP50"].cummax(), "*", "")
        print("=" * 100); print("%s seed %s | %d epochs | file dated %s" % (title, s, len(t), fdate(f))); display(t.round(3).set_index("epoch"))
''')

# ------------------------------------------------------------------------------------------------ 5 experiments
md("""### 4.3 The executed notebook of the final acne training (saved with its console output)
The notebook **"Pipeline V5 - Lesion Detector"** was run top to bottom on the researcher's computer on 10 Oct 2026 and saved with all outputs. The saved file (copied unchanged into `acne/evidence/`; the original stays in `Downloads`) contains the complete Ultralytics console output of the three final acne trainings. Below it is opened read-only: its kernel, execution counts, configuration, and the number of epochs in its console output, which is then compared with the per-epoch `results.csv` files above.""")
code(r'''
EVID = REPO / "acne" / "evidence" / "Pipeline V5 - Lesion Detector__EXECUTED_by_researcher_2026-10-10.ipynb"; ORIG = Path("/mnt/c/Users/Bautista/Downloads/Pipeline V5 - Lesion Detector.ipynb")
if EVID.exists():
    nbx = json.load(open(EVID, encoding="utf-8")); cells = nbx["cells"]; ks = nbx["metadata"].get("kernelspec", {})
    print("file: %s" % EVID.name); print("   saved %s | %.0f KB | SHA-256 %s" % (fdate(ORIG if ORIG.exists() else EVID), EVID.stat().st_size / 1024, sha256(EVID)))
    print("   original in Downloads: %s (dated %s)" % (ORIG, fdate(ORIG)) if ORIG.exists() else "   original in Downloads: not found now")
    print("kernel: %s | Python %s" % (ks.get("display_name"), nbx["metadata"].get("language_info", {}).get("version")))
    codec = [c for c in cells if c["cell_type"] == "code"]; ex = [c.get("execution_count") for c in codec]
    print("code cells: %d | execution counts: %s | executed in order from 1: %s | cells with saved output: %d" % (len(codec), ex[:25], ex == sorted(ex) and ex[0] == 1, sum(1 for c in codec if c.get("outputs"))))
    def outtext(c): return "".join("".join(o.get("text", "")) for o in c.get("outputs", []) if o.get("output_type") == "stream").replace("\r", "\n")
    cfg = [outtext(c) for c in codec if "condition:" in outtext(c)]; print("\nconfiguration printed by the notebook:\n" + (cfg[0].strip() if cfg else "not found"))
    tr = max(codec, key=lambda c: len(outtext(c))); txt = outtext(tr); lines = [l for l in txt.splitlines() if l.strip()]
    print("\nthe training cell's saved console output has %d lines" % len(lines))
    ban = [l for l in lines if l.startswith("Ultralytics")]; print("library line:", re.sub(r"[^\x20-\x7e]", "?", ban[0]) if ban else "not found")
    blocks = re.split(r"=+ (seed_\d+) \| training photos: (\d+)", txt); rows = []
    for i in range(1, len(blocks), 3):
        seed, nphotos, body = blocks[i], blocks[i + 1], blocks[i + 2]; allv = re.findall(r"^\s+all\s+(\d+)\s+(\d+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)", body, re.M)
        res = ACNE_RUN / "models" / seed / "results.csv"; n_csv = len(pd.read_csv(res)) if res.exists() else None
        rows.append(dict(seed=seed, training_photos=int(nphotos), validation_lines_in_console_output=len(allv), epochs_in_results_csv=n_csv, last_validation_line=("images %s instances %s P %s R %s mAP50 %s mAP50-95 %s" % allv[-1]) if allv else None))
    display(pd.DataFrame(rows))
    print("(the validation line after the last epoch of a run is Ultralytics' final re-validation of best.pt, so the console output can show one line more than results.csv has epochs)")
    summ = [outtext(c) for c in codec if "deployment summary" in outtext(c)]; print("\nthe notebook's own final summary cell:\n" + (summ[-1].strip() if summ else "not found"))
else: missing("the executed acne final-training notebook (acne/evidence copy)")
''')

md("""## 5. The controlled experiments (the record of everything that was tried)
`EXPERIMENTS.csv` is the experiment registry written automatically by the experiment runner: one row per run per validation set, with the run's settings, a fingerprint of the training data, epochs, best epoch, minutes and the validation scores. Runs that were launched from notebooks (E1-E4, the final acne runs) are in their own run folders and logs. **The weights of superseded experiment runs were deleted on 15 Oct 2026 to free disk space; their `results.csv`, settings and the registry rows were kept** (weights of the hyperpigmentation and A-series runs also exist in the backup folder `Documents/PrecisionSkin_Training_Backup`).""")
code(r'''
reg = pd.read_csv(EXP / "EXPERIMENTS.csv");
def results_date(rid):
    return fdate(RUNS / rid / "results.csv") if (RUNS / rid / "results.csv").exists() else "n/a"
cols_show = ["id", "note", "train_photos", "val_set", "val_mAP50", "val_mAP50_95", "val_P", "val_R", "val_F1", "epochs_run", "best_epoch", "train_minutes"]
for cond, title in (("acne", "ACNE experiments (validation = the 154 validation photos, seed 42 unless stated)"), ("hyperpigmentation", "HYPERPIGMENTATION experiments (validation sets differ by run: see val_set; scores of different sets are not comparable)")):
    t = reg[reg.condition == cond][cols_show].copy(); t["record_dated"] = t["id"].map(results_date); t["note"] = t["note"].astype(str).str.slice(0, 95)
    print("=" * 100); print(title); display(t.round(3).reset_index(drop=True))
extra = EXP / "acne_data_comparison.csv"
if extra.exists(): print("\nAcne data-comparison runs C1-C3 (recorded by the data-comparison notebook; file dated %s):" % fdate(extra)); display(pd.read_csv(extra).round(3))
else: missing("acne_data_comparison.csv (C1-C3 comparison table)")
phase1 = [r for r in ("E1_960_lowlr", "E2_960_lowlr_newdata", "E3_mosaic_off", "E4_scale025") if (RUNS / r / "results.csv").exists()]
print("\nAcne Phase 1 runs with saved per-epoch records:", phase1)
''')

# ------------------------------------------------------------------------------------------------ 6 final selection
md("""## 6. Final model selection and the files installed in the app
Selection rule used throughout: models, thresholds and settings were chosen on the **validation** photos only; a change was adopted only if it passed a rule fixed beforehand (validation mAP@0.5 at least +0.02, precision and recall not lower by more than 0.02); test sets were scored once and stored in lock files. The decisions are documented in `acne/docs/ACNE_DETECTOR_VERDICT.md`, `acne/docs/ACNE_IMPROVEMENT_AUDIT_AND_PLAN.md` and `docs/RESULTS_FOR_PAPER.md`.
- **Acne:** the installed detector is the average of the three seeds 123 / 42 / 7 (800 px). A 960 px candidate was trained and compared on a fresh test set; it did not pass the pre-set rule, so it was **not** installed.
- **Hyperpigmentation:** the installed detector is the average of the three H25 seeds (800 px), trained on rule-v2 labels.""")
code(r'''
rows = []
for f in sorted(MODEL_DIR.glob("lesion_detector_*.onnx")):
    rows.append(dict(file=f.name, dated=fdate(f), size_MB=round(f.stat().st_size / 1e6, 1), sha256=sha256(f)))
display(pd.DataFrame(rows))
for cond in ("acne", "hyperpigmentation"):
    j = MODEL_DIR / ("lesion_detector_%s.json" % cond)
    if j.exists():
        d = json.load(open(j)); print("%-18s settings file (dated %s): image size %s | confidence threshold %s | NMS IoU %s | classes %s" % (cond, fdate(j), d.get("imgsz"), d.get("default_conf"), d.get("nms_iou"), d.get("classes")))
        print("%-18s note in the file: %s" % ("", str(d.get("note", ""))[:230])); print("%-18s ensemble listed in the file: %s" % ("", d.get("ensemble", "not listed")))
    else: missing("settings file " + str(j))
md_ = ACNE_RUN / "logs" / "model_metadata.json"
if md_.exists():
    d = json.load(open(md_)); main = MODEL_DIR / "lesion_detector_acne.onnx"
    print("\nAcne main model: SHA-256 recorded by the training notebook at export time =", d["onnx_sha256"]); print("                 SHA-256 of the file now in the app                    =", sha256(main) if main.exists() else "n/a")
    print("                 match:", (sha256(main) == d["onnx_sha256"]) if main.exists() else "n/a", "(the other two acne files and the hyperpigmentation files have no SHA-256 recorded at export time: MISSING record, hashes above are as of today)")
    print("                 dataset fingerprint recorded:", d.get("dataset_fingerprint", "")[:32] + "...", "| best seed:", d.get("best_seed"), "| seeds:", d.get("seeds"))
else: missing("model_metadata.json of the acne final run")
''')

# ------------------------------------------------------------------------------------------------ 7 evaluation
md("""## 7. Evaluation of the finished models (re-computed now, read-only)
Each cell loads the **finished weights** and scores them on existing photos with the project's scorer. Nothing is trained and nothing is written. The values stored by the original runs are printed next to the re-computed ones so that anyone can see they agree. (Re-scoring a test set here only reproduces numbers that were already recorded; no decision depends on it.)""")
code(r'''
def evaluate_condition(label, weights, imgsz, val_dir, test_dir, thr=None):
    pv = [raw_preds(w, val_dir, imgsz) for w in weights]; per_seed = []
    for w, p in zip(weights, pv):
        sc = score(p, val_dir); c = best_conf(sc); pr = at_conf(sc, c); per_seed.append(dict(model=Path(w).parents[1].name, val_mAP50=sc["AP50"], val_mAP50_95=sc["AP50_95"], threshold=c, precision=pr[0], recall=pr[1], F1=pr[2]))
    fv = {k: fuse([p[k] for p in pv]) for k in pv[0]}; sv = score(fv, val_dir); thr = thr or best_conf(sv); pr = at_conf(sv, thr)
    avg_val = dict(model="3-model average", val_mAP50=sv["AP50"], val_mAP50_95=sv["AP50_95"], threshold=thr, precision=pr[0], recall=pr[1], F1=pr[2])
    pt = [raw_preds(w, test_dir, imgsz) for w in weights]; ft = {k: fuse([p[k] for p in pt]) for k in pt[0]}
    return dict(label=label, per_seed=per_seed, avg_val=avg_val, fused_test=ft, test_dir=test_dir, thr=thr, singles_test=[score(p, test_dir)["AP50"] for p in pt])
def report_test(R, stored=None):
    st = score(R["fused_test"], R["test_dir"]); rows = rows_at(R["fused_test"], R["test_dir"], R["thr"]); p, r, f = prf(rows); lo, hi = bootstrap_f1(rows); tp, fp, fn = sum(x[0] for x in rows), sum(x[1] for x in rows), sum(x[2] for x in rows)
    out = dict(set=R["label"], photos=len(R["fused_test"]), threshold=R["thr"], mAP50=st["AP50"], mAP50_95=st["AP50_95"], precision=p, recall=r, F1=f, F1_95pct_range="%.2f-%.2f" % (lo, hi), zone_F1=zone_f1(rows), found_TP=tp, false_boxes_FP=fp, missed_FN=fn)
    return out, st
''')
md("### 7.1 ACNE (installed model: average of seeds 123, 42, 7; image size 800)")
code(r'''
ACNE_W = [ACNE_SEEDS[s] / "weights" / "best.pt" for s in (123, 42, 7)]
if all(Path(w).exists() for w in ACNE_W):
    RA = evaluate_condition("acne development test (167 photos)", ACNE_W, 800, ACNE_DS / "val", ACNE_DS / "test")
    print("Validation (154 photos), per seed and the 3-model average (threshold chosen here):"); display(pd.DataFrame(RA["per_seed"] + [RA["avg_val"]]).round(3))
    out, SA = report_test(RA); display(pd.DataFrame([out]).round(3))
    stored = HOME / "runs" / "report_acne_test_scored.json"
    if stored.exists():
        s = json.load(open(stored)); print("STORED by the earlier run (%s, dated %s): mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | F1 %.3f | threshold %.3f  vs re-computed now: %.3f | %.3f | %.3f | %.3f" % (stored.name, s["scored_at"], s["mAP50"], s["mAP50_95"], s["F1"], s["threshold"], out["mAP50"], out["mAP50_95"], out["F1"], out["threshold"]))
    else: missing("stored acne development-test result")
else: missing("acne weights: " + str([str(w) for w in ACNE_W if not Path(w).exists()]))
''')
code(r'''
# independent set (41 photos, scored once with the single seed-123 model when it was made) and the fresh test set
IND = DL / "Acne_Boxing" / "Independent_Final_Set" / "boxed_independent"
if IND.exists() and Path(ACNE_W[0]).exists():
    pi = raw_preds(ACNE_W[0], IND, 800); si = score(pi, IND); ri = rows_at(pi, IND, 0.26); p, r, f = prf(ri)
    print("INDEPENDENT SET (41 photos), single seed-123 model at threshold 0.26 (the setting recorded when it was scored): mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | precision %.3f | recall %.3f | F1 %.3f" % (si["AP50"], si["AP50_95"], p, r, f))
    sj = ACNE_RUN / "logs" / "independent_result.json"
    if sj.exists(): s = json.load(open(sj)); print("STORED independent_result.json (dated %s): mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | %d photos, %d boxes" % (fdate(sj), s["mAP50"], s["mAP50_95"], s["n_photos"], s["n_boxes"]))
    else: missing("independent_result.json")
else: missing("independent set folder or acne weights")
lockf = HOME / "runs" / "acne_FRESHTEST_scored.json"
if lockf.exists():
    s = json.load(open(lockf)); print("\nFRESH TEST SET (stored lock file, scored once on %s, %d photos of which %d clear-skin):" % (s["scored_at"], s["n_photos"], s["n_negatives"]))
    display(pd.DataFrame([dict(model=v["label"], threshold=v["threshold"], mAP50=v["mAP50"], mAP50_95=v["mAP50_95"], precision=v["precision"], recall=v["recall"], F1=v["F1"], F1_range="%.2f-%.2f" % tuple(v["F1_ci"]), false_alarm_on_clear_skin=v["false_alarm_rate"]) for v in s["models"].values()]).round(3))
else: missing("stored fresh-test result of acne")
''')
md("### 7.2 HYPERPIGMENTATION (installed model: average of the three H25 seeds; image size 800)")
code(r'''
HYP_W = [HYP_RUNS[s] / "weights" / "best.pt" for s in (42, 7, 123)]
if all(Path(w).exists() for w in HYP_W):
    RH = evaluate_condition("hyperpigmentation test (100 photos, rule v2)", HYP_W, 800, EXP / "data" / "hyp_v2_val60", EXP / "data" / "hyp_v2_test")
    print("Validation (60 photos), per seed and the 3-model average (threshold chosen here):"); display(pd.DataFrame(RH["per_seed"] + [RH["avg_val"]]).round(3))
    out, SH = report_test(RH); display(pd.DataFrame([out]).round(3))
    stored = HOME / "runs" / "report_hyperpigmentation_test_scored.json"
    if stored.exists():
        s = json.load(open(stored)); print("STORED by the earlier run (%s, dated %s): mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | F1 %.3f | threshold %.3f  vs re-computed now: %.3f | %.3f | %.3f | %.3f" % (stored.name, s["scored_at"], s["mAP50"], s["mAP50_95"], s["F1"], s["threshold"], out["mAP50"], out["mAP50_95"], out["F1"], out["threshold"]))
    else: missing("stored hyperpigmentation test result")
else: missing("hyperpigmentation weights")
''')
md("### 7.3 Precision-recall curves, F1 by overlap, and found / false / missed boxes")
code(r'''
fig, ax = plt.subplots(2, 3, figsize=(16, 7.4))
for row, (name, Rr, Ss) in enumerate((("ACNE development test", RA if "RA" in globals() else None, SA if "SA" in globals() else None), ("HYPERPIGMENTATION test", RH if "RH" in globals() else None, SH if "SH" in globals() else None))):
    if Rr is None: ax[row, 0].text(0.5, 0.5, "MISSING", ha="center"); continue
    cs = np.arange(0.05, 0.9, 0.0125); pr_ = [at_conf(Ss, c) for c in cs]
    ax[row, 0].plot([x[1] for x in pr_], [x[0] for x in pr_]); ax[row, 0].set_title("%s: precision-recall (IoU 0.5), mAP@0.5 %.3f" % (name, Ss["AP50"]), fontsize=9); ax[row, 0].set_xlabel("recall"); ax[row, 0].set_ylabel("precision"); ax[row, 0].set_xlim(0, 1); ax[row, 0].set_ylim(0, 1); ax[row, 0].grid(alpha=.3)
    ax[row, 1].plot(cs, [x[2] for x in pr_], label="F1"); ax[row, 1].plot(cs, [x[0] for x in pr_], label="precision"); ax[row, 1].plot(cs, [x[1] for x in pr_], label="recall"); ax[row, 1].axvline(Rr["thr"], color="k", ls=":"); ax[row, 1].set_title(name + ": by confidence threshold (dotted = chosen on validation)", fontsize=9); ax[row, 1].legend(fontsize=7); ax[row, 1].grid(alpha=.3)
    iouf = f1_by_iou(Rr["fused_test"], Rr["test_dir"], Rr["thr"]); ax[row, 2].plot(list(iouf), list(iouf.values()), "o-"); ax[row, 2].set_title(name + ": F1 by the overlap (IoU) required", fontsize=9); ax[row, 2].set_xlabel("IoU"); ax[row, 2].grid(alpha=.3)
plt.tight_layout(); plt.show()
print("Detection 'confusion' at the chosen threshold (a detector has no true negatives, so the counts are found boxes (TP), false boxes (FP) and missed boxes (FN)): see the columns found_TP / false_boxes_FP / missed_FN in the tables above.")
''')
md("### 7.4 Sample predictions on test photos (green = the researcher's boxes, red = model boxes above the threshold)")
code(r'''
if "RA" in globals():
    names = [p.name for p in images_of(ACNE_DS / "test")][:8]; show_examples(RA["fused_test"], ACNE_DS / "test", RA["thr"], names, title="ACNE development test photos")
if "RH" in globals():
    names = [p.name for p in images_of(EXP / "data" / "hyp_v2_test")][:8]; show_examples(RH["fused_test"], EXP / "data" / "hyp_v2_test", RH["thr"], names, title="HYPERPIGMENTATION test photos")
''')

# ------------------------------------------------------------------------------------------------ 8 missing
md("## 8. What is missing or limited (stated, not filled in)")
code(r'''
checks = [
    ("Weights of the superseded experiment runs (deleted on 2026-10-15; per-epoch records and registry rows kept)", None),
    ("SHA-256 of the installed model files recorded at export time (only the acne main file has one)", None),
    ("A signed or tamper-proof timestamp: all dates here are file-system and log timestamps", None),
]
for what, _ in checks: print("-", what)
print()
for desc, p in (("acne final run: results.png of seed 123", ACNE_SEEDS[123] / "results.png"), ("acne final run: confusion matrix of seed 123", ACNE_SEEDS[123] / "confusion_matrix.png"), ("acne final run: DEPLOYMENT_SUMMARY.md", ACNE_RUN / "DEPLOYMENT_SUMMARY.md"),
                ("acne final run: bootstrap.json", ACNE_RUN / "logs" / "bootstrap.json"), ("acne final run: leak_check.csv", ACNE_RUN / "logs" / "leak_check.csv"), ("hyperpigmentation final run: results.png", HYP_RUNS[42] / "results.png"),
                ("experiment registry EXPERIMENTS.csv", EXP / "EXPERIMENTS.csv"), ("training backup folder", Path("/mnt/c/Users/Bautista/Documents/PrecisionSkin_Training_Backup")), ("results table in the repository docs", REPO / "docs" / "RESULTS_FOR_PAPER.md"), ("executed acne final-training notebook with console output (copy)", REPO / "acne" / "evidence" / "Pipeline V5 - Lesion Detector__EXECUTED_by_researcher_2026-10-10.ipynb")):
    print("%-8s %s  (%s)" % ("present" if Path(p).exists() else "MISSING", desc, fdate(p)))
print("\nThings that were recorded by memory of the working session and are NOT in a file: who launched which run (table in section 1 is from the project working records).")
print("Missing records detected automatically while this notebook ran:", len(MISSING), "(the known gaps listed above are stated separately)"); [print("  -", m) for m in MISSING]
''')
md("""**Other limits that apply to the results themselves (also stated in the thesis text):** one annotator; the acne development test set influenced some earlier decisions (which is why an independent set and a fresh test set were made); the hyperpigmentation scores are under the redefined box rule v2 and are not comparable with scores under the first rule; the fresh acne test has only 36 photos with acne; both detectors put boxes on a share of clear-skin photos; the detectors propose candidate lesions and are not a diagnosis.""")

md("""## 9. How anyone can check these records themselves
Open a WSL terminal and run these commands. They read files only.
```
ls -l ~/PrecisionSkinV1/runs/lesion_acne_boxed_only/models/seed_123/        # run folder: args.yaml, results.csv, weights, plots, with dates
head -3 ~/PrecisionSkinV1/runs/lesion_acne_boxed_only/models/seed_123/results.csv
column -s, -t < ~/PrecisionSkinV1/lesion_experiments/EXPERIMENTS.csv | head   # the experiment registry
sha256sum /mnt/c/Users/Bautista/source/repos/SkinDevApp/SkinDevApp/Model/lesion_detector_*.onnx
git -C /mnt/c/Users/Bautista/source/repos/SkinDevApp log --format='%h %ad %s' --date=short | head -20
```
The box tool's `labeling_log.csv` files are in each labelling folder under `Downloads/PrecisionSkin/Acne_Boxing` and `Hyperpigmentation_Boxing`. The panel may ask the researcher to open the box tool on a few photos to show how boxing is done.""")

nb = nbf.v4.new_notebook(); nb["cells"] = C; nb["metadata"] = {"kernelspec": {"display_name": "Python 3", "language": "python", "name": "python3"}}
nbf.write(nb, str(OUT)); print("wrote", OUT)
