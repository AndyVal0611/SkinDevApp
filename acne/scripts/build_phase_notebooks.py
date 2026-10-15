#!/usr/bin/env python
"""build_phase_notebooks.py - writes the three notebooks YOU run for the acne improvement plan:
  Phase 1  PrecisionSkin_Acne_Phase1_Experiments.ipynb     E1-E4, validation only, seed 42, same rule as before
  Phase 2  PrecisionSkin_Acne_Phase2_FinalTraining.ipynb   best configuration x 3 seeds, 3-model average, threshold on validation, development test once
  Phase 3  PrecisionSkin_Acne_Phase3_FreshTest.ipynb       the fresh untouched test set, scored once (positives + clear-skin negatives)
Run in WSL:  python build_phase_notebooks.py"""
import sys
from pathlib import Path
import nbformat as nbf
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts")]
import build_report_notebooks as B

OUT = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/acne/notebooks")
SETUP = r'''
import os, sys, json, time, shutil
os.environ["YOLO_VERBOSE"] = "False"          # no progress bars: one clean line per finished epoch is printed instead
from pathlib import Path
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
HOME = Path.home() / "PrecisionSkinV1"
'''

TRAIN_FN = r'''
def train_run(run_id, recipe, over, note, seed, args_base=None, quick=False):
    """one training run with a clean per-epoch line; resumable; returns the run folder"""
    import yaml, torch
    from ultralytics import YOLO
    sp = LE.spec(run_id, "acne", recipe, note, seed=seed, **over); train, vals, _ = LE.recipe(recipe); args = dict(sp["args"]); model = args.pop("model")
    if quick: args["epochs"] = 1
    run = LE.RUNS / run_id; run.mkdir(parents=True, exist_ok=True); lst = run / "train_list.txt"; lst.write_text("\n".join(str(p) for p in train) + "\n")
    first_val = next(iter(vals.values())); yml = run / "data.yaml"
    yaml.safe_dump({"path": str(run), "train": str(lst), "val": str(first_val / "images"), "names": {0: "acne"}}, open(yml, "w")); (run / "spec.json").write_text(json.dumps(sp, indent=1))
    best = {"v": 0.0}
    def on_epoch(trainer):
        m = trainer.metrics; ls = trainer.label_loss_items(trainer.tloss, prefix="train"); mp = m.get("metrics/mAP50(B)", 0.0); star = "*" if mp > best["v"] else " "; best["v"] = max(best["v"], mp)
        print("%s epoch %3d/%d | box %.3f cls %.3f dfl %.3f | P %.3f R %.3f mAP50 %.3f mAP50-95 %.3f" % (star, trainer.epoch + 1, trainer.epochs, ls["train/box_loss"], ls["train/cls_loss"], ls["train/dfl_loss"],
              m.get("metrics/precision(B)", 0), m.get("metrics/recall(B)", 0), mp, m.get("metrics/mAP50-95(B)", 0)), flush=True)
    t0 = time.time(); last = run / "weights" / "last.pt"; print("=" * 110); print(run_id, "|", note, "| %d training images | seed %d" % (len(train), seed), flush=True)
    if (run / "DONE").exists(): print("already trained: using the saved run"); return run
    mdl = YOLO(str(last)) if last.exists() else YOLO(model); mdl.add_callback("on_fit_epoch_end", on_epoch)
    if last.exists(): mdl.train(resume=True)
    else: mdl.train(data=str(yml), seed=seed, device=0 if torch.cuda.is_available() else "cpu", project=str(LE.RUNS), name=run_id, exist_ok=True, plots=False, **args)
    (run / "DONE").write_text("ok"); print("trained in %.1f minutes" % ((time.time() - t0) / 60), flush=True); return run
'''

STOP_NOTE = """**Before you start (important):**
- Make sure no other training is running (`ps aux | grep -E "nbconvert|lesion_exp"` in WSL). WSL has only about 6.7 GB of memory; two trainings at once can freeze or crash both.
- Kernel: choose the WSL environment `.venv_det` (Python 3.11).
- Run the cells in order. Every finished epoch prints one line; `*` marks a new best mAP@0.5.
- Close other heavy programs (browsers with many tabs, other notebooks) while training."""

# =================================================================================== PHASE 1
def phase1():
    nb = nbf.v4.new_notebook(); C = []
    md = lambda s: C.append(nbf.v4.new_markdown_cell(s.strip("\n"))); code = lambda s: C.append(nbf.v4.new_code_cell(s.strip("\n")))
    md("""# Acne improvement, Phase 1: controlled experiments (validation only)

**Goal.** Find which changes help small-acne detection, one change at a time, always on the same 154 **validation** photos with the same seed (42). The development test and the independent set are NOT used here.

| ID | change against the baseline A0 | why |
|---|---|---|
| E1 | 960 px input + learning rate 0.0005 (AdamW) | the two best single changes earlier (+0.017 and +0.008) together |
| E2 | E1 + the new and reviewed labels (the C2 data: 250 new photos + 111 reviewed) | label completeness |
| E3 | mosaic augmentation off | mosaic shrinks tiny spots; may hurt |
| E4 | scale augmentation 0.25 instead of 0.5 | scale augmentation shrinks tiny spots further |

**Baseline A0** (already trained, same recipe on the v4 data): validation mAP@0.5 0.4254, F1 0.494.
**Decision rule (fixed before looking):** a change counts only if validation mAP@0.5 rises by at least **0.02** over A0 and neither precision nor recall falls by more than 0.02. Photo-level AUC is checked later for winners. Only winners are combined in Phase 2.

**Time:** about 40 to 70 minutes per experiment (E1 and E2 are the slowest because of 960 px): roughly 3 to 4 hours for all four. You can set `RUN_IDS` below to run fewer.

""" + STOP_NOTE)
    code(SETUP + r'''
import numpy as np, pandas as pd
import matplotlib.pyplot as plt
import lesion_exp as LE
import hyperpig_autolabel as H
QUICK_TEST = False        # True = 1 epoch per experiment, to check that everything works (a few minutes)
EXP = {
    "E1_960_lowlr":          dict(recipe="acne_base", over=dict(imgsz=960, batch=6, lr0=0.0005, optimizer="AdamW"), note="960 px + lr 0.0005 AdamW"),
    "E2_960_lowlr_newdata":  dict(recipe="acne_c2",   over=dict(imgsz=960, batch=6, lr0=0.0005, optimizer="AdamW"), note="E1 + new photos and reviewed labels"),
    "E3_mosaic_off":         dict(recipe="acne_base", over=dict(mosaic=0.0), note="mosaic off"),
    "E4_scale025":           dict(recipe="acne_base", over=dict(scale=0.25), note="scale augmentation 0.25"),
}
RUN_IDS = list(EXP)       # remove names to run fewer experiments
if QUICK_TEST: RUN_IDS = RUN_IDS[:1]
print("experiments to run:", RUN_IDS, "| runs are saved in", LE.RUNS)
''')
    md("## 1. The training function\nOne function used for every experiment: builds the training list, trains with the same settings as the baseline plus the one change, prints a line per epoch, and can resume if interrupted.")
    code(TRAIN_FN)
    md("## 2. Train the experiments (one after the other)")
    code(r'''
for rid in RUN_IDS:
    e = EXP[rid]; train_run(rid, e["recipe"], e["over"], e["note"], seed=42, quick=QUICK_TEST)
''')
    md("## 3. Training curves")
    code(r'''
fig, ax = plt.subplots(1, 3, figsize=(15, 4))
for rid in RUN_IDS:
    f = LE.RUNS / rid / "results.csv"
    if not f.exists(): continue
    r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]
    ax[0].plot(r["epoch"], r["train/box_loss"], label=rid + " train"); ax[0].plot(r["epoch"], r["val/box_loss"], "--", label=rid + " val"); ax[1].plot(r["epoch"], r["metrics/mAP50(B)"], label=rid); ax[2].plot(r["epoch"], r["metrics/mAP50-95(B)"], label=rid)
for a, t in zip(ax, ("box loss", "validation mAP@0.5", "validation mAP@0.5:0.95")): a.set_title(t, fontsize=10); a.set_xlabel("epoch"); a.legend(fontsize=7); a.grid(alpha=.3)
plt.tight_layout(); plt.show()
''')
    md("## 4. Every finished epoch of every experiment (scroll; a `*` marks a new best mAP@0.5)")
    code(r'''
pd.set_option("display.max_rows", None); pd.set_option("display.width", 220)
cols = {"epoch": "epoch", "train/box_loss": "box_loss", "train/cls_loss": "cls_loss", "train/dfl_loss": "dfl_loss", "metrics/precision(B)": "P", "metrics/recall(B)": "R", "metrics/mAP50(B)": "mAP50", "metrics/mAP50-95(B)": "mAP50-95"}
for rid in RUN_IDS:
    f = LE.RUNS / rid / "results.csv"
    if not f.exists(): print(rid, ": no results yet"); continue
    r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]; t = r[list(cols)].rename(columns=cols); t["epoch"] = t["epoch"].astype(int); t["best so far"] = np.where(t["mAP50"] == t["mAP50"].cummax(), "*", "")
    print("=" * 100); print("%s | %d finished epochs | best mAP@0.5 %.3f at epoch %d" % (rid, len(t), t["mAP50"].max(), int(t.loc[t["mAP50"].idxmax(), "epoch"]))); display(t.round(3).set_index("epoch"))
''')
    md("## 5. Comparison with the baseline and the decision rule\nEvery model (best epoch) is scored with the same scorer on the same validation photos, at the confidence with the best F1. The result is also appended to `EXPERIMENTS.csv` (the project's record of every run).")
    code(r'''
import hashlib
reg = pd.read_csv(LE.REG); b = reg[reg.id == "A0_base"].iloc[0]
rows = [dict(run="A0 baseline", mAP50=b.val_mAP50, mAP50_95=b.val_mAP50_95, precision=b.val_P, recall=b.val_R, F1=b.val_F1, dmAP50=0.0, passes_rule="")]; log = []
for rid in RUN_IDS:
    w = LE.RUNS / rid / "weights" / "best.pt"
    if not w.exists(): continue
    sp = json.loads((LE.RUNS / rid / "spec.json").read_text()); imgsz = sp["args"]["imgsz"]; sv = H.score_model(w, imgsz, LE.ACNE / "val"); c = H.best_f1_conf(sv); p, r, f = H.at_conf(sv, c); d = sv["AP50"] - b.val_mAP50
    ok = d >= 0.02 and p >= b.val_P - 0.02 and r >= b.val_R - 0.02
    rows.append(dict(run=rid, mAP50=sv["AP50"], mAP50_95=sv["AP50_95"], precision=p, recall=r, F1=f, dmAP50=d, passes_rule="YES" if ok else "no"))
    if QUICK_TEST: continue
    tl = [l for l in (LE.RUNS / rid / "train_list.txt").read_text().splitlines() if l]; res = pd.read_csv(LE.RUNS / rid / "results.csv"); res.columns = [c.strip() for c in res.columns]
    log.append(dict(id=rid, condition="acne", recipe=sp["recipe"], seed=sp["seed"], model="yolov8s.pt", imgsz=imgsz, overrides=json.dumps({k: v for k, v in sp["args"].items() if LE.BASE_ARGS.get(k) != v and k != "model"}), note=sp["note"],
                    train_photos=len(tl), dataset_fingerprint=hashlib.sha256("\n".join(sorted(tl)).encode()).hexdigest()[:16], epochs_run=len(res), best_epoch=int(res["metrics/mAP50(B)"].idxmax()) + 1, train_minutes=round(float(res["time"].iloc[-1]) / 60, 1),
                    val_set="val", val_mAP50=round(sv["AP50"], 4), val_mAP50_95=round(sv["AP50_95"], 4), val_conf=round(c, 3), val_P=round(p, 3), val_R=round(r, 3), val_F1=round(f, 3), status="ok"))
CMP = pd.DataFrame(rows); display(CMP.round(3))
if log and not any(reg.id.isin([l["id"] for l in log])): LE.log_rows(log); print("results appended to", LE.REG)
print("rule: adopt only if validation mAP@0.5 gains at least +0.02 and neither precision nor recall drops by more than 0.02 (single seed; seed noise about +/-0.006)")
''')
    md("""## 6. What to do with the result
- Write down which experiments passed the rule (`passes_rule` = YES). Only those go into Phase 2.
- If none passes, say so: Phase 2 then uses the baseline settings (or the best single change if you choose, stating that it did not pass the rule).
- Do not look at the test photos to decide anything. Next: `PrecisionSkin_Acne_Phase2_FinalTraining.ipynb`.""")
    nb["cells"] = C; nb["metadata"] = {"kernelspec": {"display_name": "Python 3", "language": "python", "name": "python3"}}; return nb

# =================================================================================== PHASE 2
def phase2():
    nb = nbf.v4.new_notebook(); C = []
    md = lambda s: C.append(nbf.v4.new_markdown_cell(s.strip("\n"))); code = lambda s: C.append(nbf.v4.new_code_cell(s.strip("\n")))
    md("""# Acne improvement, Phase 2: final training (3 seeds), average, threshold, development test once

**What it does.** Trains the chosen configuration with three seeds (42, 7, 123), averages the three models' boxes, picks the confidence threshold on the 154 **validation** photos, and scores the 167-photo development test **once** (stored in a lock file).
**Before you run it:** open Phase 1's result table and set `CONFIG` below to the winning settings (only experiments that passed the +0.02 rule). The default is the baseline recipe (no change).

**Time:** about 2 to 3.5 hours (3 seeds). """ + "\n\n" + STOP_NOTE)
    code(SETUP + r'''
import numpy as np, pandas as pd
import matplotlib.pyplot as plt
import lesion_exp as LE
QUICK_TEST = False                    # True = 1 epoch of seed 42 only, throw-away folder: checks that everything works
TRAIN = True                          # False = only show the results of runs that already finished
ALLOW_RESCORE = False                 # the development test is scored once, then the stored result is shown
# ---- set this after Phase 1 (only changes that passed the rule) ----
CONFIG = dict(name="baseline", recipe="acne_base", over=dict())        # e.g. dict(name="960_lowlr", recipe="acne_base", over=dict(imgsz=960, batch=6, lr0=0.0005, optimizer="AdamW"))
SEEDS = [42, 7, 123]
IMGSZ = CONFIG["over"].get("imgsz", 800)
TAG = "acne_final_" + CONFIG["name"]
if QUICK_TEST: SEEDS = [42]; TAG += "_QUICKTEST"
VAL_DIR, TEST_DIR = LE.ACNE / "val", LE.ACNE / "test"
LOCK = HOME / "runs" / (TAG + "_devtest_scored.json")
print("configuration:", CONFIG, "| image size", IMGSZ, "| seeds", SEEDS, "| run names start with", TAG)
''')
    md("## 1. Toolbox\nHelper functions for boxes, matching, precision / recall / F1, model averaging and plots (nothing here trains).")
    code(B.TOOLBOX)
    md("## 2. Training function")
    code(TRAIN_FN)
    md("## 3. Train the three seeds")
    code(r'''
RUNS = {}
for s in SEEDS:
    rid = "%s_seed%d" % (TAG, s)
    if TRAIN: train_run(rid, CONFIG["recipe"], CONFIG["over"], "final acne model, configuration %s, seed %d" % (CONFIG["name"], s), seed=s, quick=QUICK_TEST)
    RUNS[s] = LE.RUNS / rid
WEIGHTS = {s: RUNS[s] / "weights" / "best.pt" for s in SEEDS}; print({s: p.exists() for s, p in WEIGHTS.items()})
''')
    md("## 4. Training curves and every finished epoch")
    code(r'''curves([RUNS[s] for s in SEEDS], ["seed %d" % s for s in SEEDS])''')
    code(r'''
pd.set_option("display.max_rows", None); pd.set_option("display.width", 220)
cols = {"epoch": "epoch", "train/box_loss": "box_loss", "train/cls_loss": "cls_loss", "train/dfl_loss": "dfl_loss", "metrics/precision(B)": "P", "metrics/recall(B)": "R", "metrics/mAP50(B)": "mAP50", "metrics/mAP50-95(B)": "mAP50-95"}
for s in SEEDS:
    f = RUNS[s] / "results.csv"
    if not f.exists(): print("seed", s, ": no results yet"); continue
    r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]; t = r[list(cols)].rename(columns=cols); t["epoch"] = t["epoch"].astype(int); t["best so far"] = np.where(t["mAP50"] == t["mAP50"].cummax(), "*", "")
    print("=" * 100); print("seed %d | %d finished epochs | best mAP@0.5 %.3f at epoch %d" % (s, len(t), t["mAP50"].max(), int(t.loc[t["mAP50"].idxmax(), "epoch"]))); display(t.round(3).set_index("epoch"))
''')
    md("## 5. Validation results per seed, the 3-model average, and the threshold (chosen on validation only)")
    code(r'''
VAL_PRED = {s: raw_preds(WEIGHTS[s], VAL_DIR, IMGSZ) for s in SEEDS}; rows = []
for s in SEEDS:
    sc = score(VAL_PRED[s], VAL_DIR); c = best_conf(sc); p, r, f = at_conf(sc, c); rows.append(dict(seed=s, mAP50=sc["AP50"], mAP50_95=sc["AP50_95"], threshold=c, precision=p, recall=r, F1=f))
vt = pd.DataFrame(rows); display(vt.round(3)); print("mean mAP@0.5 %.3f (std %.3f)" % (vt.mAP50.mean(), vt.mAP50.std() if len(vt) > 1 else 0))
FUSED_VAL = {k: fuse([VAL_PRED[s][k] for s in SEEDS]) for k in VAL_PRED[SEEDS[0]]}; sv = score(FUSED_VAL, VAL_DIR); THR = best_conf(sv); p, r, f = at_conf(sv, THR)
print("3-model average on validation: mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | best F1 %.3f at threshold %.3f (precision %.3f, recall %.3f)" % (sv["AP50"], sv["AP50_95"], f, THR, p, r))
cs = np.arange(0.05, 0.6, 0.025); pr_ = [at_conf(sv, c) for c in cs]
plt.figure(figsize=(6, 3.5)); plt.plot(cs, [x[0] for x in pr_], label="precision"); plt.plot(cs, [x[1] for x in pr_], label="recall"); plt.plot(cs, [x[2] for x in pr_], label="F1", lw=2)
plt.axvline(THR, color="k", ls=":"); plt.xlabel("confidence threshold"); plt.legend(); plt.grid(alpha=.3); plt.title("validation: threshold choice"); plt.show()
''')
    md("## 6. Development test (167 photos), scored once\nThis set influenced earlier decisions, so treat it as a development number. The independent claim is made in Phase 3 on the fresh test set.")
    code(r'''
if LOCK.exists() and not ALLOW_RESCORE:
    R = json.loads(LOCK.read_text()); print("stored result from", R["scored_at"])
else:
    TEST_PRED = {s: raw_preds(WEIGHTS[s], TEST_DIR, IMGSZ) for s in SEEDS}; FUSED_TEST = {k: fuse([TEST_PRED[s][k] for s in SEEDS]) for k in TEST_PRED[SEEDS[0]]}
    st = score(FUSED_TEST, TEST_DIR); rows_t = rows_at(FUSED_TEST, TEST_DIR, THR); p, r, f = prf(rows_t); lo, hi = bootstrap_f1(rows_t); singles = [score(TEST_PRED[s], TEST_DIR)["AP50"] for s in SEEDS]
    R = dict(scored_at=time.strftime("%Y-%m-%d %H:%M"), config=CONFIG["name"], threshold=THR, mAP50=st["AP50"], mAP50_95=st["AP50_95"], precision=p, recall=r, F1=f, F1_ci=[lo, hi], zone_F1=zone_f1(rows_t), single_seed_mAP50=singles,
             n_photos=len(FUSED_TEST), val_mAP50_avg=sv["AP50"])
    LOCK.parent.mkdir(parents=True, exist_ok=True); LOCK.write_text(json.dumps(R, indent=1)); print("scored and stored in", LOCK)
print("DEVELOPMENT TEST (%d photos), 3-model average at threshold %.3f" % (R["n_photos"], R["threshold"]))
print("  mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | precision %.3f | recall %.3f | F1 %.3f (95%% range %.2f-%.2f) | zone F1 %.3f" % (R["mAP50"], R["mAP50_95"], R["precision"], R["recall"], R["F1"], *R["F1_ci"], R["zone_F1"]))
print("  single seeds, mAP@0.5:", [round(x, 3) for x in R["single_seed_mAP50"]], "| installed model before: mAP@0.5 0.439, F1 0.507 (3-seed average, baseline recipe)")
''')
    md("""## 7. Export for the app (optional, only after you decide to install)
Off by default. If you turn it on, the files go to `SkinDevApp/Model` and **replace the installed acne models**. The image size must match (960 px models need `imgsz` 960 in the json and the same size in the export). Tell Claude before installing so the app's threshold and the check through the C# code are done too.""")
    code(r'''
EXPORT = False
if EXPORT:
    from ultralytics import YOLO
    MODEL_DIR = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/SkinDevApp/Model")
    for suffix, s in (("", SEEDS[0]), ("_m2", SEEDS[1]), ("_m3", SEEDS[2])):
        o = Path(YOLO(str(WEIGHTS[s])).export(format="onnx", imgsz=IMGSZ, opset=12, simplify=True, dynamic=False)); shutil.copy2(o, MODEL_DIR / ("lesion_detector_acne%s.onnx" % suffix)); print("exported seed", s)
    print("now set default_conf = %.3f, imgsz = %d and nms_iou = 0.6 in lesion_detector_acne.json" % (THR, IMGSZ))
else: print("EXPORT = False: nothing written")
''')
    nb["cells"] = C; nb["metadata"] = {"kernelspec": {"display_name": "Python 3", "language": "python", "name": "python3"}}; return nb

# =================================================================================== PHASE 3
def phase3():
    nb = nbf.v4.new_notebook(); C = []
    md = lambda s: C.append(nbf.v4.new_markdown_cell(s.strip("\n"))); code = lambda s: C.append(nbf.v4.new_code_cell(s.strip("\n")))
    md("""# Acne improvement, Phase 3: the fresh untouched test set (scored once, two models)

**What it is.** About 100 photos that no model has ever seen or been tuned on: positives (acne photos) and clear-skin negatives. You boxed them from scratch (no model drafts). The notebook scores **two** models on exactly the same photos, once:
- **candidate_960**: the new 960 px 3-model average from Phase 2 (threshold 0.225, chosen on validation)
- **installed_800**: the 3-model average currently installed in the app (threshold 0.275, chosen on validation)

**Never run this before the 100 photos are boxed and saved.** It scores once and stores the result in a lock file; after that it only shows the stored result.

**The decision rule (fixed before any fresh-test score exists):** install the candidate only if on the fresh test its mAP@0.5 is at least **+0.02** above the installed model, its precision and recall are each no more than 0.02 lower, and its false-alarm rate on the clear-skin photos is no more than 5 points higher. Otherwise the installed model stays. The result is reported either way, including whether it reaches 0.50.

""" + STOP_NOTE)
    code(SETUP + r"""
import numpy as np, pandas as pd
import matplotlib.pyplot as plt
import lesion_exp as LE
ALLOW_RESCORE = False
SMOKE = os.environ.get("NB_FRESH_FOLDER") is not None        # only used to check that the notebook runs end to end
FRESH_FOLDER = Path(os.environ.get("NB_FRESH_FOLDER", "/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/Acne_Fresh_Test"))
FRESH_DIR = HOME / "lesion_experiments" / "data" / ("acne_fresh_test_SMOKE" if SMOKE else "acne_fresh_test")
LOCK = HOME / "runs" / ("acne_FRESHTEST_SMOKE_scored.json" if SMOKE else "acne_FRESHTEST_scored.json")
MODELS = {
    "candidate_960": dict(label="960 px candidate (Phase 2)", imgsz=960, thr=0.225, weights=[LE.RUNS / ("acne_final_960_lowlr_seed%d" % s) / "weights" / "best.pt" for s in (42, 7, 123)]),
    "installed_800": dict(label="installed baseline", imgsz=800, thr=0.275, weights=[HOME / "runs" / "lesion_acne_boxed_only" / "models" / ("seed_%d" % s) / "weights" / "best.pt" for s in (123, 42, 7)]),
}
for k, m in MODELS.items(): print(k, "| image size", m["imgsz"], "| threshold", m["thr"], "| weights found:", [p.exists() for p in m["weights"]])
""")
    md("## 1. Toolbox")
    code(B.TOOLBOX)
    md("## 2. Build the fresh test set from the photos you saved\nOnly photos you saved are used. A saved photo with boxes is a positive; a photo you marked **X** (no acne) is a clear-skin negative.")
    code(r"""
meta = FRESH_FOLDER / "_drafts" / "meta.json"; ref = json.load(open(meta))["drafted_at"]
S = next(p for p in sorted(FRESH_FOLDER.iterdir()) if p.is_dir() and (p / "labels").is_dir() and (p / "images").is_dir() and not p.name.lower().startswith("boxed"))      # the working folder, NOT the export folder "boxed_..." (that one holds only the photos with boxes)
shutil.rmtree(FRESH_DIR, ignore_errors=True); (FRESH_DIR / "images").mkdir(parents=True); (FRESH_DIR / "labels").mkdir(parents=True); npos = nneg = 0
for f in sorted((S / "labels").glob("*.txt")):
    if f.stat().st_mtime <= ref + 1: continue
    img = next(iter((S / "images").glob(f.stem + ".*"))); shutil.copy2(img, FRESH_DIR / "images" / img.name); shutil.copy2(f, FRESH_DIR / "labels" / f.name)
    if rd(f): npos += 1
    else: nneg += 1
print("fresh test set: %d positives (with boxes), %d negatives (X, clear skin), %d photos in total" % (npos, nneg, npos + nneg)); assert npos + nneg > 0
if not SMOKE and npos + nneg < 90: print("WARNING: only %d of about 100 photos are saved. Finish boxing before scoring (the score is taken only once)." % (npos + nneg))
""")
    md("## 3. Score once (both models, same photos)")
    code(r"""
PRED, FUSED = {}, {}
for name, m in MODELS.items():
    PRED[name] = [raw_preds(w, FRESH_DIR, m["imgsz"]) for w in m["weights"]]; FUSED[name] = {k: fuse([p[k] for p in PRED[name]]) for k in PRED[name][0]}
neg = [k for k in FUSED["installed_800"] if not rd(FRESH_DIR / "labels" / (Path(k).stem + ".txt"))]
if LOCK.exists() and not ALLOW_RESCORE:
    R = json.loads(LOCK.read_text()); print("the fresh test was already scored on", R["scored_at"], "- showing the stored result")
else:
    R = dict(scored_at=time.strftime("%Y-%m-%d %H:%M"), n_photos=len(FUSED["installed_800"]), n_negatives=len(neg), models={})
    for name, m in MODELS.items():
        st = score(FUSED[name], FRESH_DIR); rows_f = rows_at(FUSED[name], FRESH_DIR, m["thr"]); p, r, f = prf(rows_f); lo, hi = bootstrap_f1(rows_f)
        fa = float(np.mean([any(c >= m["thr"] for b, c in FUSED[name][k]) for k in neg])) if neg else float("nan")
        R["models"][name] = dict(label=m["label"], threshold=m["thr"], mAP50=st["AP50"], mAP50_95=st["AP50_95"], precision=p, recall=r, F1=f, F1_ci=[lo, hi], zone_F1=zone_f1(rows_f), false_alarm_rate=fa,
                                 single_seed_mAP50=[score(a, FRESH_DIR)["AP50"] for a in PRED[name]])
    LOCK.parent.mkdir(parents=True, exist_ok=True); LOCK.write_text(json.dumps(R, indent=1)); print("scored and stored in", LOCK)
print("FRESH TEST: %d photos, of which %d clear-skin negatives" % (R["n_photos"], R["n_negatives"]))
tbl = pd.DataFrame([dict(model=v["label"], threshold=v["threshold"], mAP50=v["mAP50"], mAP50_95=v["mAP50_95"], precision=v["precision"], recall=v["recall"], F1=v["F1"], F1_range="%.2f-%.2f" % tuple(v["F1_ci"]),
                         zone_F1=v["zone_F1"], false_alarm_on_clear_skin=v["false_alarm_rate"]) for v in R["models"].values()])
display(tbl.round(3))
for k, v in R["models"].items(): print("%-14s single seeds, mAP@0.5: %s" % (k, [round(x, 3) for x in v["single_seed_mAP50"]]))
""")
    md("## 4. The decision (rule fixed in advance, see the top of the notebook)")
    code(r"""
c, i = R["models"]["candidate_960"], R["models"]["installed_800"]
d_map = c["mAP50"] - i["mAP50"]; d_p = c["precision"] - i["precision"]; d_r = c["recall"] - i["recall"]; d_fa = c["false_alarm_rate"] - i["false_alarm_rate"]
checks = [("mAP@0.5 at least +0.02 above the installed model", d_map >= 0.02, "%+.3f" % d_map), ("precision not more than 0.02 lower", d_p >= -0.02, "%+.3f" % d_p),
          ("recall not more than 0.02 lower", d_r >= -0.02, "%+.3f" % d_r), ("false alarms on clear skin not more than 5 points higher", (d_fa != d_fa) or d_fa <= 0.05, "n/a (no clear-skin photos in this set)" if d_fa != d_fa else "%+.1f points" % (100 * d_fa))]
for name, ok, val in checks: print("%-62s %-5s %s" % (name, "PASS" if ok else "FAIL", val))
print("\nVERDICT:", "the candidate passes every check: it may be installed (after you and Claude confirm)" if all(ok for _, ok, _ in checks) else "the candidate does NOT pass every check: the installed model stays")
print("Reaches 0.50 mAP@0.5 on the fresh test?  candidate: %s (%.3f) | installed: %s (%.3f)" % ("YES" if c["mAP50"] >= 0.5 else "no", c["mAP50"], "YES" if i["mAP50"] >= 0.5 else "no", i["mAP50"]))
""")
    md("## 5. Precision-recall curves and true / false / missed boxes")
    code(r"""
fig, ax = plt.subplots(1, 2, figsize=(11, 3.8)); cols_ = {"candidate_960": "#e45756", "installed_800": "#4c78a8"}; cs = np.arange(0.05, 0.9, 0.0125); counts = {}
for name, m in MODELS.items():
    sc = score(FUSED[name], FRESH_DIR); pr_ = [at_conf(sc, c_) for c_ in cs]; ax[0].plot([x[1] for x in pr_], [x[0] for x in pr_], color=cols_[name], label="%s (mAP@0.5 %.3f)" % (m["label"], sc["AP50"]))
    rr = rows_at(FUSED[name], FRESH_DIR, m["thr"]); counts[name] = (sum(x[0] for x in rr), sum(x[1] for x in rr), sum(x[2] for x in rr))
ax[0].set_xlabel("recall"); ax[0].set_ylabel("precision"); ax[0].set_title("precision-recall curve (IoU 0.5)"); ax[0].legend(fontsize=8); ax[0].grid(alpha=.3); ax[0].set_xlim(0, 1); ax[0].set_ylim(0, 1)
x = np.arange(3); wd = 0.38
for j, (name, m) in enumerate(MODELS.items()):
    ax[1].bar(x + (j - 0.5) * wd, counts[name], wd, color=cols_[name], label=m["label"])
    for xi, v in zip(x + (j - 0.5) * wd, counts[name]): ax[1].text(xi, v, str(v), ha="center", va="bottom", fontsize=8)
ax[1].set_xticks(x); ax[1].set_xticklabels(["found (TP)", "false boxes (FP)", "missed (FN)"]); ax[1].legend(fontsize=8); ax[1].set_title("at each model's threshold")
plt.tight_layout(); plt.show()
""")
    md("## 6. Where it works: by photo resolution, by spot density, and on clear skin (both models)")
    code(r"""
from PIL import Image
info = {}
for k in FUSED["installed_800"]:
    p = next(iter((FRESH_DIR / "images").glob(Path(k).stem + ".*"))); w_, h_ = Image.open(p).size; info[k] = min(w_, h_)
def pos(k): return rd(FRESH_DIR / "labels" / (Path(k).stem + ".txt"))
out = []
for name, m in MODELS.items():
    for gname, fn_ in (("low resolution (<500 px)", lambda k: info[k] < 500), ("high resolution (>=500 px)", lambda k: info[k] >= 500), ("sparse (1-6 spots)", lambda k: 1 <= len(pos(k)) <= 6),
                       ("medium (7-14)", lambda k: 7 <= len(pos(k)) <= 14), ("dense (15+)", lambda k: len(pos(k)) >= 15)):
        ks = [k for k in FUSED[name] if pos(k) and fn_(k)]
        if not ks: continue
        p, r, f = prf(rows_at({k: FUSED[name][k] for k in ks}, FRESH_DIR, m["thr"])); out.append(dict(model=m["label"], group=gname, photos=len(ks), precision=p, recall=r, F1=f))
display(pd.DataFrame(out).round(3))
if neg:
    tt = []
    for name, m in MODELS.items():
        for t in (0.2, 0.25, 0.275, 0.3, 0.35, 0.4):
            n = np.array([sum(1 for b, c in FUSED[name][k] if c >= t) for k in neg]); tt.append(dict(model=m["label"], threshold=t, photos_with_a_false_box=int((n > 0).sum()), of=len(n), specificity=1 - (n > 0).mean(), false_boxes_per_photo=n.mean()))
    display(pd.DataFrame(tt).round(3))
""")
    md("## 7. Example predictions of the candidate (green = your boxes, red = model boxes above the threshold)")
    code(r"""
names = [p.name for p in sorted((FRESH_DIR / "images").glob("*"))]
posn = [n for n in names if pos(n)][:6]; negn = [n for n in names if not pos(n)][:2]
show_examples(FUSED["candidate_960"], FRESH_DIR, MODELS["candidate_960"]["thr"], posn + negn, cols=4, title="fresh test photos, 960 px candidate")
""")
    md("## 8. Summary for Chapter 4\nCopy the numbers from section 3 and the verdict from section 4. State: how many fresh photos (positives and negatives), that they were never used for training, validation, tuning or threshold choice, that you boxed them from scratch without model drafts, that the result was computed once, the 95 % range, and the false-alarm rate. Report the result whether or not it reaches 0.50.")
    nb["cells"] = C; nb["metadata"] = {"kernelspec": {"display_name": "Python 3", "language": "python", "name": "python3"}}; return nb


if __name__ == "__main__":
    # writes ONLY the notebooks named on the command line (e.g. "python build_phase_notebooks.py phase3"): rewriting a notebook you have already run would erase its outputs
    which = set(sys.argv[1:]) or {"phase1", "phase2", "phase3"}
    for key, name, fn in (("phase1", "PrecisionSkin_Acne_Phase1_Experiments.ipynb", phase1), ("phase2", "PrecisionSkin_Acne_Phase2_FinalTraining.ipynb", phase2), ("phase3", "PrecisionSkin_Acne_Phase3_FreshTest.ipynb", phase3)):
        if key in which: nbf.write(fn(), str(OUT / name)); print("wrote", OUT / name)
