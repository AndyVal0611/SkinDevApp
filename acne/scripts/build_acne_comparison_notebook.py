#!/usr/bin/env python
"""build_acne_comparison_notebook.py - writes acne/notebooks/PrecisionSkin_Acne_Data_Comparison.ipynb: trains the acne data variants (one seed, same recipe) INSIDE the notebook,
prints one clean line per finished epoch, and shows the comparison with the baseline. Run in WSL:  python build_acne_comparison_notebook.py"""
import nbformat as nbf
from pathlib import Path

OUT = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/acne/acne/notebooks/PrecisionSkin_Acne_Data_Comparison.ipynb")
C = []
md = lambda s: C.append(nbf.v4.new_markdown_cell(s.strip("\n")))
code = lambda s: C.append(nbf.v4.new_code_cell(s.strip("\n")))

md("""# Acne: do the new photos and the reviewed labels help? (training inside the notebook)

**Question.** After you boxed 250 new acne photos and reviewed 111 training photos, does adding them improve the acne detector?
**Method.** The same recipe and the same seed (42) as the baseline, one change at a time, always scored on the same 154 validation photos. The development test and independent sets are not used here.

| variant | training data |
|---|---|
| baseline A0 (already trained) | acne dataset v4, 1,145 photos |
| **C1** | v4 + 250 new photos you boxed |
| **C2** | C1 + the 111 training photos you reviewed (missed spots added) |
| **C3** | C2 + 115 healthy-skin negatives (no boxes) |

**Decision rule (fixed before looking):** a variant is adopted only if validation mAP@0.5 rises by at least 0.02 over the baseline and neither precision nor recall falls by more than 0.02. Photo-level AUC is checked afterwards for the winner.

**How to read the output.** Each finished epoch prints one line: epoch, the three training losses, and precision / recall / mAP on the validation photos. `*` marks a new best mAP@0.5.""")
code(r'''
import os, sys, json, time, shutil
os.environ["YOLO_VERBOSE"] = "False"          # no progress bars: one clean line per epoch is printed below
from pathlib import Path
import numpy as np, pandas as pd
import matplotlib.pyplot as plt
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import lesion_exp as LE
from ultralytics import YOLO
import torch, yaml
VARIANTS = ["C1_acne_new", "C2_acne_new_review", "C3_acne_new_review_neg"]
RUN_VARIANTS = VARIANTS                   # remove names here to train fewer variants
print("runs are saved in", LE.RUNS)
''')
md("## 1. The data of each variant")
code(r'''
for v in VARIANTS:
    sp = next(s for s in LE.QUEUE if s["id"] == v); train, vals, note = LE.recipe(sp["recipe"])
    nb = sum(len([l for l in (Path(str(p).replace("/images/", "/labels/")).with_suffix(".txt")).read_text().splitlines() if l.strip()]) if Path(str(p).replace("/images/", "/labels/")).with_suffix(".txt").exists() else 0 for p in train)
    print("%-26s %5d training images | %6d boxes | %s" % (v, len(train), nb, note))
print("validation photos:", len(list((LE.ACNE / "val" / "images").glob("*"))), "(same for every variant)")
''')
md("## 2. Training (one variant after the other)\nSame settings as every earlier acne run: YOLOv8s, 800 px, up to 100 epochs, early stopping after 30 epochs without improvement, seed 42.")
code(r'''
def train_variant(vid):
    sp = next(s for s in LE.QUEUE if s["id"] == vid); train, vals, note = LE.recipe(sp["recipe"]); args = dict(sp["args"]); model = args.pop("model")
    run = LE.RUNS / vid; run.mkdir(parents=True, exist_ok=True)
    lst = run / "train_list.txt"; lst.write_text("\n".join(str(p) for p in train) + "\n"); first_val = next(iter(vals.values()))
    yml = run / "data.yaml"; yaml.safe_dump({"path": str(run), "train": str(lst), "val": str(first_val / "images"), "names": {0: sp["condition"]}}, open(yml, "w"))
    (run / "spec.json").write_text(json.dumps(sp, indent=1)); best = {"v": 0.0}
    def on_epoch(trainer):
        m = trainer.metrics; ls = trainer.label_loss_items(trainer.tloss, prefix="train"); mp = m.get("metrics/mAP50(B)", 0.0); star = "*" if mp > best["v"] else " "
        best["v"] = max(best["v"], mp)
        print("%s epoch %3d/%d | box %.3f cls %.3f dfl %.3f | P %.3f R %.3f mAP50 %.3f mAP50-95 %.3f" % (star, trainer.epoch + 1, trainer.epochs, ls["train/box_loss"], ls["train/cls_loss"], ls["train/dfl_loss"],
              m.get("metrics/precision(B)", 0), m.get("metrics/recall(B)", 0), mp, m.get("metrics/mAP50-95(B)", 0)), flush=True)
    t0 = time.time(); last = run / "weights" / "last.pt"; print("=" * 110); print(vid, "|", note, "| %d training images" % len(train), flush=True)
    if (run / "weights" / "best.pt").exists() and (run / "DONE").exists(): print("already trained: using the saved run"); return
    mdl = YOLO(str(last)) if last.exists() else YOLO(model)
    mdl.add_callback("on_fit_epoch_end", on_epoch)
    if last.exists(): mdl.train(resume=True)
    else: mdl.train(data=str(yml), seed=sp["seed"], device=0 if torch.cuda.is_available() else "cpu", project=str(LE.RUNS), name=vid, exist_ok=True, plots=False, **args)
    (run / "DONE").write_text("ok"); print("trained in %.1f minutes" % ((time.time() - t0) / 60), flush=True)

for v in RUN_VARIANTS: train_variant(v)
''')
md("## 3. Training curves\nValidation mAP should rise and flatten; the best epoch is kept.")
code(r'''
fig, ax = plt.subplots(1, 3, figsize=(15, 4))
for v in VARIANTS:
    f = LE.RUNS / v / "results.csv"
    if not f.exists(): continue
    r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]
    ax[0].plot(r["epoch"], r["train/box_loss"], label=v + " train"); ax[0].plot(r["epoch"], r["val/box_loss"], "--", label=v + " val"); ax[1].plot(r["epoch"], r["metrics/mAP50(B)"], label=v); ax[2].plot(r["epoch"], r["metrics/mAP50-95(B)"], label=v)
for a, t in zip(ax, ("box loss (training solid, validation dashed)", "validation mAP@0.5", "validation mAP@0.5:0.95")): a.set_title(t, fontsize=10); a.set_xlabel("epoch"); a.legend(fontsize=7); a.grid(alpha=.3)
plt.tight_layout(); plt.show()
''')
md("## 4. Every finished epoch of every variant\nSaved per-epoch records (a `*` marks a new best mAP@0.5). Scroll the tables; they stay in this notebook.")
code(r'''
pd.set_option("display.max_rows", None); pd.set_option("display.width", 220)
cols = {"epoch": "epoch", "train/box_loss": "box_loss", "train/cls_loss": "cls_loss", "train/dfl_loss": "dfl_loss", "metrics/precision(B)": "P", "metrics/recall(B)": "R", "metrics/mAP50(B)": "mAP50", "metrics/mAP50-95(B)": "mAP50-95", "val/box_loss": "val_box_loss"}
for v in VARIANTS:
    f = LE.RUNS / v / "results.csv"
    if not f.exists(): print(v, ": no results yet"); continue
    r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]; t = r[list(cols)].rename(columns=cols); t["epoch"] = t["epoch"].astype(int); t["best so far"] = np.where(t["mAP50"] == t["mAP50"].cummax(), "*", "")
    print("=" * 100); print("%s | %d finished epochs | best mAP@0.5 %.3f at epoch %d" % (v, len(t), t["mAP50"].max(), int(t.loc[t["mAP50"].idxmax(), "epoch"]))); display(t.round(3).set_index("epoch"))
''')
md("## 5. Comparison on the same validation photos\nEvery model (best epoch) scored with the same scorer; the confidence is the one with the best F1 on the validation photos. Baseline = the earlier A0 run of the same recipe on the v4 data.")
code(r'''
import hyperpig_autolabel as H
rows = []
base = pd.read_csv(LE.REG) if hasattr(LE, "REG") else pd.read_csv(Path.home() / "PrecisionSkinV1/lesion_experiments/EXPERIMENTS.csv")
b = base[(base.id == "A0_base")].iloc[0]; rows.append(dict(run="A0 baseline (v4)", mAP50=b.val_mAP50, mAP50_95=b.val_mAP50_95, precision=b.val_P, recall=b.val_R, F1=b.val_F1, dmAP50=0.0, adopt=""))
for v in VARIANTS:
    w = LE.RUNS / v / "weights" / "best.pt"
    if not w.exists(): continue
    sv = H.score_model(w, 800, LE.ACNE / "val"); c = H.best_f1_conf(sv); p, r, f = H.at_conf(sv, c); d = sv["AP50"] - b.val_mAP50
    ok = d >= 0.02 and p >= b.val_P - 0.02 and r >= b.val_R - 0.02
    rows.append(dict(run=v, mAP50=sv["AP50"], mAP50_95=sv["AP50_95"], precision=p, recall=r, F1=f, dmAP50=d, adopt="YES (passes the rule)" if ok else "no"))
CMP = pd.DataFrame(rows); display(CMP.round(3))
CMP.to_csv(Path.home() / "PrecisionSkinV1/lesion_experiments/acne_data_comparison.csv", index=False)
print("rule: adopt only if mAP@0.5 gains at least +0.02 and neither precision nor recall drops by more than 0.02 (single seed: seed noise is about +/-0.006)")
''')
md("## 6. Summary\nRead the table above: the `dmAP50` column is the gain over the baseline on the same 154 validation photos; `adopt` applies the pre-set rule. If a variant passes, the next step is to train it with three seeds (also in a notebook), choose the threshold on validation, and score the development test and the independent set once.")
nb = nbf.v4.new_notebook(); nb["cells"] = C; nb["metadata"] = {"kernelspec": {"display_name": "Python 3", "language": "python", "name": "python3"}}
nbf.write(nb, str(OUT)); print("wrote", OUT)
