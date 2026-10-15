#!/usr/bin/env python
"""build_report_notebooks.py - writes the two report notebooks (training + results) for acne and hyperpigmentation into notebooks/.
Run in WSL:  python build_report_notebooks.py"""
import nbformat as nbf
from pathlib import Path

OUT = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp")

TOOLBOX = r'''
import json, csv, os, sys, time, shutil, random, warnings
from pathlib import Path
import numpy as np, pandas as pd
import matplotlib.pyplot as plt
from PIL import Image, ImageDraw
warnings.filterwarnings("ignore")
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}

def rd(p):
    p = Path(p); return [tuple(map(float, l.split()[1:5])) for l in p.read_text().splitlines() if len(l.split()) >= 5] if p.exists() else []
def xyxy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2
def iou(a, b):
    a, b = xyxy(a), xyxy(b); iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    return i / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i + 1e-12)
def side(b): return (b[2] * b[3]) ** 0.5
def images_of(d): return sorted(p for p in (Path(d) / "images").glob("*") if p.suffix.lower() in EXT)

def raw_preds(weights, d, imgsz=800):
    """all boxes down to confidence 0.001 for every photo of folder d (normalised x-centre, y-centre, w, h)"""
    from ultralytics import YOLO
    import torch
    m = YOLO(str(weights)); dev = 0 if torch.cuda.is_available() else "cpu"; out = {}; ims = images_of(d)
    for s in range(0, len(ims), 8):
        ch = ims[s:s + 8]
        for p, r in zip(ch, m.predict([str(x) for x in ch], imgsz=imgsz, conf=0.001, iou=0.6, max_det=300, verbose=False, device=dev)):
            out[p.name] = [(tuple(map(float, b)), float(c)) for b, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy())] if len(r.boxes) else []
    return out

def fuse(lists, thr=0.5):
    """weighted box fusion: boxes of the models that overlap (IoU >= thr) become one box; confidence = sum of confidences / number of models"""
    n = len(lists); allb = sorted([b for l in lists for b in l], key=lambda x: -x[1]); clusters = []
    for b, c in allb:
        for cl in clusters:
            if iou(cl["box"], b) >= thr:
                cl["m"].append((b, c)); w = np.array([x[1] for x in cl["m"]]); cl["box"] = tuple(np.average(np.array([x[0] for x in cl["m"]]), axis=0, weights=w)); break
        else: clusters.append({"box": b, "m": [(b, c)]})
    return [(cl["box"], min(1.0, sum(x[1] for x in cl["m"]) / n)) for cl in clusters]

def _ap(conf, tp, n_gt):
    if n_gt == 0 or len(conf) == 0: return 0.0
    o = np.argsort(-conf); tp = tp[o]; tpc = np.cumsum(tp); fpc = np.cumsum(1 - tp); rec = tpc / n_gt; prec = tpc / (tpc + fpc)
    mrec = np.concatenate([[0], rec, [1]]); mpre = np.concatenate([[1], prec, [0]]); mpre = np.flip(np.maximum.accumulate(np.flip(mpre))); x = np.linspace(0, 1, 101)
    return float(np.trapezoid(np.interp(x, mrec, mpre), x)) if hasattr(np, "trapezoid") else float(np.trapz(np.interp(x, mrec, mpre), x))

def match_flags(gt, pr, thr):
    """greedy by confidence: returns (confidences, 1/0 true-positive flags) of every predicted box of one photo"""
    used = set(); cs, fl = [], []
    for b, c in sorted(pr, key=lambda x: -x[1]):
        best, bj = 0, -1
        for j, g in enumerate(gt):
            if j in used: continue
            v = iou(g, b)
            if v > best: best, bj = v, j
        ok = bj >= 0 and best >= thr
        if ok: used.add(bj)
        cs.append(c); fl.append(1.0 if ok else 0.0)
    return cs, fl

def score(preds, d):
    """mAP@0.5, mAP@0.5:0.95 and the raw arrays for precision / recall / F1 at any confidence (ground truth = labels of folder d)"""
    d = Path(d); gts = {k: rd(d / "labels" / (Path(k).stem + ".txt")) for k in preds}; n_gt = sum(len(g) for g in gts.values()); aps = []; conf50 = tp50 = None
    for t in np.arange(0.5, 0.96, 0.05):
        cs, fl = [], []
        for k, pr in preds.items():
            c, f = match_flags(gts[k], pr, t); cs += c; fl += f
        cs, fl = np.array(cs), np.array(fl); aps.append(_ap(cs, fl, n_gt))
        if abs(t - 0.5) < 1e-9: conf50, tp50 = cs, fl
    return dict(AP50=aps[0], AP50_95=float(np.mean(aps)), conf=conf50, tp=tp50, n_gt=n_gt)

def at_conf(s, c):
    m = s["conf"] >= c; tp = s["tp"][m].sum(); p = tp / max(1, m.sum()); r = tp / max(1, s["n_gt"]); return p, r, 2 * p * r / max(1e-9, p + r)

def best_conf(s):
    cs = np.arange(0.05, 0.9, 0.025); return float(max(cs, key=lambda c: at_conf(s, c)[2]))

def rows_at(preds, d, thr):
    """per photo: true positives, false positives, false negatives, zones of your boxes, zones of the model boxes, n your boxes, n model boxes"""
    rows = []
    for k, pr in preds.items():
        gt = rd(Path(d) / "labels" / (Path(k).stem + ".txt")); pc = [b for b, c in pr if c >= thr]; _, fl = match_flags(gt, [(b, 1.0) for b in pc], 0.5); tp = int(sum(fl))
        cell = lambda b: min(2, int(b[1] * 3)) * 3 + min(2, int(b[0] * 3))
        rows.append((tp, len(pc) - tp, len(gt) - tp, {cell(g) for g in gt}, {cell(b) for b in pc}, len(gt), len(pc)))
    return rows

def prf(rows):
    tp, fp, fn = sum(r[0] for r in rows), sum(r[1] for r in rows), sum(r[2] for r in rows); p, r = tp / max(1, tp + fp), tp / max(1, tp + fn); return p, r, 2 * p * r / max(1e-9, p + r)

def zone_f1(rows):
    tp = sum(len(r[3] & r[4]) for r in rows); fp = sum(len(r[4] - r[3]) for r in rows); fn = sum(len(r[3] - r[4]) for r in rows); return 2 * tp / max(1, 2 * tp + fp + fn)

def f1_by_iou(preds, d, thr, ious=(0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8)):
    out = {}
    for t in ious:
        tp = fp = fn = 0
        for k, pr in preds.items():
            gt = rd(Path(d) / "labels" / (Path(k).stem + ".txt")); pc = [(b, 1.0) for b, c in pr if c >= thr]; _, fl = match_flags(gt, pc, t); x = int(sum(fl)); tp += x; fp += len(pc) - x; fn += len(gt) - x
        p, r = tp / max(1, tp + fp), tp / max(1, tp + fn); out[t] = 2 * p * r / max(1e-9, p + r)
    return out

def bootstrap_f1(rows, n=1000, seed=0):
    rng = np.random.default_rng(seed); idx = np.arange(len(rows)); b = [prf([rows[i] for i in rng.choice(idx, len(idx))])[2] for _ in range(n)]; return float(np.percentile(b, 2.5)), float(np.percentile(b, 97.5))

def show_examples(preds, d, thr, names, cols=4, title=""):
    d = Path(d); fig, ax = plt.subplots(-(-len(names) // cols), cols, figsize=(4 * cols, 4 * -(-len(names) // cols))); ax = np.array(ax).reshape(-1)
    for a in ax: a.axis("off")
    for a, n in zip(ax, names):
        p = next(iter((d / "images").glob(Path(n).stem + ".*"))); im = Image.open(p).convert("RGB"); W, H = im.size; dr = ImageDraw.Draw(im); lw = max(2, W // 250)
        for b in rd(d / "labels" / (Path(n).stem + ".txt")): x0, y0, x1, y1 = xyxy(b); dr.rectangle([x0 * W, y0 * H, x1 * W, y1 * H], outline=(40, 220, 60), width=lw)
        for b, c in preds[n]:
            if c >= thr: x0, y0, x1, y1 = xyxy(b); dr.rectangle([x0 * W, y0 * H, x1 * W, y1 * H], outline=(255, 40, 40), width=lw)
        a.imshow(im); a.set_title(n[:28], fontsize=8)
    fig.suptitle(title + "  (green = your boxes, red = model boxes above the threshold)"); plt.tight_layout(); plt.show()

def curves(run_dirs, labels):
    fig, ax = plt.subplots(1, 3, figsize=(15, 4))
    for rd_, lb in zip(run_dirs, labels):
        f = Path(rd_) / "results.csv"
        if not f.exists(): continue
        r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]
        ax[0].plot(r["epoch"], r["train/box_loss"], label=lb + " train"); ax[0].plot(r["epoch"], r["val/box_loss"], "--", label=lb + " val")
        ax[1].plot(r["epoch"], r["metrics/mAP50(B)"], label=lb); ax[2].plot(r["epoch"], r["metrics/mAP50-95(B)"], label=lb)
    for a, t in zip(ax, ("box loss (training = solid, validation = dashed)", "validation mAP@0.5", "validation mAP@0.5:0.95")): a.set_title(t, fontsize=10); a.set_xlabel("epoch"); a.legend(fontsize=7); a.grid(alpha=.3)
    plt.tight_layout(); plt.show()
'''


LOGCODE = r'''
pd.set_option("display.max_rows", None); pd.set_option("display.width", 220)
cols = {"epoch": "epoch", "train/box_loss": "box_loss", "train/cls_loss": "cls_loss", "train/dfl_loss": "dfl_loss", "metrics/precision(B)": "P", "metrics/recall(B)": "R",
        "metrics/mAP50(B)": "mAP50", "metrics/mAP50-95(B)": "mAP50-95", "val/box_loss": "val_box_loss"}
for s in SEEDS:
    f = FINAL_RUNS[s] / "results.csv"
    if not f.exists(): print("seed", s, ": no results.csv yet"); continue
    r = pd.read_csv(f); r.columns = [c.strip() for c in r.columns]; t = r[list(cols)].rename(columns=cols)
    t["epoch"] = t["epoch"].astype(int); t["best so far"] = np.where(t["mAP50"] == t["mAP50"].cummax(), "*", "")
    print("=" * 100); print("seed %d  |  %d finished epochs  |  best mAP@0.5 %.3f at epoch %d  |  P, R, mAP are on the validation photos" % (s, len(t), t["mAP50"].max(), int(t.loc[t["mAP50"].idxmax(), "epoch"])))
    display(t.round(3).set_index("epoch"))
'''


def build(cfg):
    nb = nbf.v4.new_notebook(); C = []
    md = lambda s: C.append(nbf.v4.new_markdown_cell(s.strip("\n")))
    code = lambda s: C.append(nbf.v4.new_code_cell(s.strip("\n")))
    md(cfg["intro"])
    md("## 0. How to run\n" + cfg["howto"])
    code(cfg["config"])
    md("## 1. Toolbox\nSmall helper functions: reading boxes, matching predicted boxes to yours, precision / recall / F1, averaging models, plotting. Nothing here trains or changes a model.")
    code(TOOLBOX)
    md(cfg["data_md"]); code(cfg["data_code"])
    md(cfg["train_md"]); code(cfg["train_code"])
    if cfg.get("screenshot"):
        import base64
        cell = nbf.v4.new_markdown_cell(cfg["screenshot_md"] + "\n\n![training terminal](attachment:training_terminal.png)")
        cell["attachments"] = {"training_terminal.png": {"image/png": base64.b64encode(open(cfg["screenshot"], "rb").read()).decode()}}
        C.append(cell)
    md("### Training log (from the saved per-epoch records)\nThe same information as the terminal shows, kept in each run's `results.csv`: one row per epoch. **Every finished epoch** of every seed (a `*` marks an epoch that set a new best mAP@0.5). Scroll the table; it is saved in the notebook, so you can look at it any time without running anything.")
    code(LOGCODE)
    md("## 4. Training curves\nLoss should go down; the validation mAP rises, then flattens. The best epoch is kept (`best.pt`).")
    code(cfg["curves_code"])
    md("## 5. Validation results per model\nEach model scored on the validation photos (used to choose models and thresholds, never the test photos). One row per seed.")
    code(cfg["val_code"])
    md("## 6. Averaging the three models and choosing the threshold on validation\nBoxes that several models agree on are merged (weighted box fusion). The confidence threshold with the best F1 on the **validation** photos is chosen here, before the test photos are looked at.")
    code(cfg["ens_code"])
    md(cfg["test_md"]); code(cfg["test_code"])
    md("## 8. Where does it work and where does it fail?")
    code(cfg["fail_code"])
    md("## 9. Example predictions on test photos")
    code(cfg["examples_code"])
    md("## 10. Summary")
    code(cfg["summary_code"])
    md(cfg["export_md"]); code(cfg["export_code"])
    nb["cells"] = C; nb["metadata"] = {"kernelspec": {"display_name": "Python 3", "language": "python", "name": "python3"}}
    return nb


# ============================================================================================================== hyperpigmentation
HYP = dict(
    intro="""# Hyperpigmentation detector: training and results

PrecisionSkin / LUMYVUE. One-class YOLOv8s detector that boxes patches of hyperpigmentation (melasma, post-inflammatory marks, dark spots) on face photos.
This notebook shows **how the final model is trained**, **how its settings are chosen**, and **how it scores** on photos that were never used for training or selection.

**Annotation rule v2 (important).** One box = one *connected* dark patch. Patches that touch, or are closer than the smaller one's width, are one box; marks smaller than about 7 % of the photo are skipped; a box stays in one facial region. Boxes are therefore about twice as large as under the first rule.
Why: the first rule was not reproducible (the same annotator agreed with their earlier boxes at F1 0.28 at IoU 0.5); rule v2 is (F1 0.67 between two passes minutes apart, an upper bound).""",
    howto="""- Run it in WSL with the `.venv_det` environment (ultralytics, torch, pandas, matplotlib), e.g. VS Code -> *Select Kernel* -> that environment.
- `TRAIN = False` (default) uses the finished runs and takes a few minutes. `TRAIN = True` retrains the three seeds (about 1.5 to 2 hours on an RTX 4060) into a new folder; finished runs are never overwritten.
- The test photos are scored **once**: the result is stored in a lock file and shown from there afterwards (`ALLOW_RESCORE = False`).""",
    config=r'''
import os, json, time, shutil
from pathlib import Path
TRAIN = False                      # True = retrain the three seeds (slow); False = use the finished runs
QUICK_TEST = False                 # True = 1 epoch of seed 42 only, into a throw-away folder: checks that training works (about 3 minutes)
ALLOW_RESCORE = False              # the test photos are scored once; afterwards the stored result is shown
SMOKE = os.environ.get("NB_SMOKE") == "1"          # used only to check that the notebook runs end to end
HOME = Path.home() / "PrecisionSkinV1"
EXP = HOME / "lesion_experiments"; RUNS = EXP / "runs"; DATA = EXP / "data"
VAL_DIR = DATA / "hyp_v2_val60"        # 60 photos boxed under rule v2 (choose threshold here)
TEST_DIR = DATA / "hyp_v2_test"        # 100 photos boxed from scratch under rule v2 (scored once)
SEEDS = [42, 7, 123]
FINAL_RUNS = {42: RUNS / "H25_v2human_all_pub", 7: RUNS / "H25_s7", 123: RUNS / "H25_s123"}
if SMOKE: FINAL_RUNS = {42: RUNS / "H20_v2auto", 7: RUNS / "H20_s7", 123: RUNS / "H20_s123"}
REPORT_RUNS = HOME / "runs" / "report_hyperpigmentation"      # where TRAIN = True writes (the final runs are never touched)
LOCK = (HOME / "runs" / ("report_hyperpigmentation_SMOKE_test.json" if SMOKE else "report_hyperpigmentation_test_scored.json"))
TRAIN_ARGS = dict(imgsz=800, epochs=100, patience=30, batch=8, workers=2, cos_lr=True, close_mosaic=10, fliplr=0.5, flipud=0.0, degrees=10, scale=0.5,
                  hsv_h=0.015, hsv_s=0.5, hsv_v=0.35, lr0=0.0005, optimizer="AdamW")
BASE_MODEL = str(HOME / "yolov8s.pt") if (HOME / "yolov8s.pt").exists() else "yolov8s.pt"      # pretrained starting weights
if os.environ.get("NB_QUICK") == "1": TRAIN, QUICK_TEST = True, True
if QUICK_TEST:
    TRAIN = True; SEEDS = [42]; TRAIN_ARGS = dict(TRAIN_ARGS, epochs=1); REPORT_RUNS = HOME / "runs" / "report_hyperpigmentation_QUICKTEST"
    LOCK = HOME / "runs" / "report_hyperpigmentation_QUICKTEST_test.json"
print("mode:", "QUICK TEST (1 epoch, throw-away folder)" if QUICK_TEST else "TRAIN" if TRAIN else "use finished runs", "| smoke check" if SMOKE else "")
''',
    data_md="""## 2. The data
Training = **808 photos you corrected under rule v2**, each shown to the network 3 times (original + 2 copies), plus **1,260 de-duplicated public photos** (their boxes are also coarse, close to rule v2): 3,684 training images.
Validation = 60 photos for choosing models and thresholds. Test = 100 other photos, boxed from scratch under rule v2, scored once.""",
    data_code=r'''
tl = FINAL_RUNS[42] / "train_list.txt"
lst = [l.strip() for l in tl.read_text().splitlines() if l.strip()] if tl.exists() else []
print("training images in the list: %d" % len(lst))
for name, d in (("validation", VAL_DIR), ("test", TEST_DIR)):
    ims = images_of(d); nb = [len(rd(d / "labels" / (p.stem + ".txt"))) for p in ims]
    print("%-10s %3d photos | %3d boxes | %.1f boxes per photo | %d photos without a patch" % (name, len(ims), sum(nb), np.mean(nb), sum(1 for x in nb if x == 0)))
sd = [side(b) for p in images_of(TEST_DIR) for b in rd(TEST_DIR / "labels" / (p.stem + ".txt"))]
print("test boxes: median side %.3f of the photo (the first rule gave 0.106)" % np.median(sd))
fig, ax = plt.subplots(1, 2, figsize=(10, 3.5)); ax[0].hist([len(rd(TEST_DIR / "labels" / (p.stem + ".txt"))) for p in images_of(TEST_DIR)], bins=range(0, 10), color="#4c78a8"); ax[0].set_title("boxes per test photo")
ax[1].hist(sd, bins=20, color="#f58518"); ax[1].set_title("test box size (share of the photo)"); plt.tight_layout(); plt.show()
show_examples({p.name: [] for p in images_of(TEST_DIR)[:8]}, TEST_DIR, 1.0, [p.name for p in images_of(TEST_DIR)[:8]], title="test photos with your rule-v2 boxes")
''',
    train_md="""## 3. Training
YOLOv8s (small), 800 px input, up to 100 epochs, AdamW with a low learning rate (0.0005), cosine learning-rate decay, mosaic mixing switched off for the last 10 epochs, flips and small rotations / scale / colour changes as augmentation, early stopping after 30 epochs without improvement. Three seeds (42, 7, 123) are trained; seed changes the random start and the order of the images.
With `TRAIN = False` this cell only prints the settings the finished runs used.

**Where the finished models come from:** the three seeds (H25 runs) were trained with `shared/lesion_exp.py` using exactly these settings and this data list (808 corrected photos x3 + 1,260 public photos, `train_list.txt` in each run folder); their per-epoch records (`results.csv`) and weights are loaded below. Set `TRAIN = True` to reproduce the training inside this notebook.""",
    screenshot="/mnt/c/Users/Bautista/source/repos/SkinDevApp/docs/figures/hyperpigmentation_training_terminal.png",
    screenshot_md="""**What training looks like in the terminal** (screenshot, seed 123, epoch 46 of 100). Each line is a batch of 8 images: the three losses (box, class, distribution) go down as the network learns. At the end of each epoch the 60 validation photos are scored: `all 60 142` = 60 photos with 142 boxes; `Box(P` precision 0.491, `R` recall 0.451, `mAP50` 0.419, `mAP50-95` 0.142 for that epoch.""",
    train_code=r'''
if TRAIN:
    from ultralytics import YOLO
    import torch, yaml
    REPORT_RUNS.mkdir(parents=True, exist_ok=True)
    yml = REPORT_RUNS / "data.yaml"; shutil.copy2(FINAL_RUNS[42] / "train_list.txt", REPORT_RUNS / "train_list.txt")
    yaml.safe_dump({"path": str(REPORT_RUNS), "train": str(REPORT_RUNS / "train_list.txt"), "val": str(VAL_DIR / "images"), "names": {0: "hyperpigmentation"}}, open(yml, "w"))
    for s in SEEDS:
        last = REPORT_RUNS / ("seed_%d" % s) / "weights" / "last.pt"
        if last.exists(): YOLO(str(last)).train(resume=True)
        else: YOLO(BASE_MODEL).train(data=str(yml), seed=s, device=0 if torch.cuda.is_available() else "cpu", project=str(REPORT_RUNS), name="seed_%d" % s, exist_ok=True, **TRAIN_ARGS)
    FINAL_RUNS = {s: REPORT_RUNS / ("seed_%d" % s) for s in SEEDS}
else:
    spec = FINAL_RUNS[42] / "spec.json"
    if spec.exists(): print(json.dumps(json.loads(spec.read_text())["args"], indent=1))
WEIGHTS = {s: FINAL_RUNS[s] / "weights" / "best.pt" for s in SEEDS}
print({s: p.exists() for s, p in WEIGHTS.items()})
''',
    curves_code=r'''curves([FINAL_RUNS[s] for s in SEEDS], ["seed %d" % s for s in SEEDS])''',
    val_code=r'''
VAL_PRED = {s: raw_preds(WEIGHTS[s], VAL_DIR) for s in SEEDS}
rows = []
for s in SEEDS:
    sc = score(VAL_PRED[s], VAL_DIR); c = best_conf(sc); p, r, f = at_conf(sc, c)
    rows.append(dict(seed=s, mAP50=sc["AP50"], mAP50_95=sc["AP50_95"], threshold=c, precision=p, recall=r, F1=f))
vt = pd.DataFrame(rows); display(vt.round(3)); print("mean mAP@0.5 %.3f (std %.3f)" % (vt.mAP50.mean(), vt.mAP50.std()))
''',
    ens_code=r'''
FUSED_VAL = {k: fuse([VAL_PRED[s][k] for s in SEEDS]) for k in VAL_PRED[SEEDS[0]]}
sv = score(FUSED_VAL, VAL_DIR); THR = best_conf(sv); p, r, f = at_conf(sv, THR)
print("3-model average on validation: mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | best F1 %.3f at threshold %.3f (precision %.3f, recall %.3f)" % (sv["AP50"], sv["AP50_95"], f, THR, p, r))
cs = np.arange(0.05, 0.6, 0.025); pr_ = [at_conf(sv, c) for c in cs]
plt.figure(figsize=(6, 3.5)); plt.plot(cs, [x[0] for x in pr_], label="precision"); plt.plot(cs, [x[1] for x in pr_], label="recall"); plt.plot(cs, [x[2] for x in pr_], label="F1", lw=2)
plt.axvline(THR, color="k", ls=":"); plt.xlabel("confidence threshold"); plt.legend(); plt.grid(alpha=.3); plt.title("validation: threshold choice"); plt.show()
''',
    test_md="""## 7. The test photos (scored once)
These 100 photos were boxed from scratch by you under rule v2 and were never used for training, model choice or the threshold. The threshold above (from validation) is applied; nothing is tuned here.
If the lock file already exists, the stored result is shown instead of scoring again.""",
    test_code=r'''
if LOCK.exists() and not ALLOW_RESCORE:
    R = json.loads(LOCK.read_text()); print("stored result from", R["scored_at"])
    TEST_PRED = None
else:
    TEST_PRED = {s: raw_preds(WEIGHTS[s], TEST_DIR) for s in SEEDS}
    FUSED_TEST = {k: fuse([TEST_PRED[s][k] for s in SEEDS]) for k in TEST_PRED[SEEDS[0]]}
    st = score(FUSED_TEST, TEST_DIR); rows_t = rows_at(FUSED_TEST, TEST_DIR, THR); p, r, f = prf(rows_t); lo, hi = bootstrap_f1(rows_t)
    singles = [score(TEST_PRED[s], TEST_DIR)["AP50"] for s in SEEDS]
    R = dict(scored_at=time.strftime("%Y-%m-%d %H:%M"), threshold=THR, mAP50=st["AP50"], mAP50_95=st["AP50_95"], precision=p, recall=r, F1=f, F1_ci=[lo, hi], zone_F1=zone_f1(rows_t),
             photos_with_patch=float(np.mean([x[6] > 0 for x in rows_t])), single_seed_mAP50=singles, n_photos=len(FUSED_TEST),
             F1_by_iou=f1_by_iou(FUSED_TEST, TEST_DIR, THR), count_mae=float(np.mean([abs(x[5] - x[6]) for x in rows_t])),
             count_within1=float(np.mean([abs(x[5] - x[6]) <= 1 for x in rows_t])), count_spearman=float(np.corrcoef(np.argsort(np.argsort([x[5] + 1e-9 * i for i, x in enumerate(rows_t)])), np.argsort(np.argsort([x[6] + 1e-9 * i for i, x in enumerate(rows_t)])))[0, 1]))
    LOCK.parent.mkdir(parents=True, exist_ok=True); LOCK.write_text(json.dumps(R, indent=1)); print("scored and stored in", LOCK)
print("TEST (%d photos), 3-model average at threshold %.3f" % (R["n_photos"], R["threshold"]))
print("  mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | precision %.3f | recall %.3f | F1 %.3f (95%% range %.2f-%.2f)" % (R["mAP50"], R["mAP50_95"], R["precision"], R["recall"], R["F1"], *R["F1_ci"]))
print("  3x3 face-zone F1 %.3f | photos with a patch found %.0f %% | patch count: mean error %.2f, within 1 in %.0f %% of photos, Spearman %.2f" % (R["zone_F1"], 100 * R["photos_with_patch"], R["count_mae"], 100 * R["count_within1"], R["count_spearman"]))
print("  single seeds, mAP@0.5:", [round(x, 3) for x in R["single_seed_mAP50"]])
ious = list(map(float, R["F1_by_iou"])); plt.figure(figsize=(5.5, 3.2)); plt.plot(ious, [R["F1_by_iou"][str(i)] if str(i) in R["F1_by_iou"] else R["F1_by_iou"][i] for i in ious], "o-"); plt.xlabel("IoU needed to count a patch as found"); plt.ylabel("F1"); plt.grid(alpha=.3)
plt.title("test: F1 gets lower as the overlap needed rises"); plt.show()
''',
    fail_code=r'''
if TEST_PRED is None:
    TEST_PRED = {s: raw_preds(WEIGHTS[s], TEST_DIR) for s in SEEDS}
FUSED_TEST = {k: fuse([TEST_PRED[s][k] for s in SEEDS]) for k in TEST_PRED[SEEDS[0]]}
sizes = np.array([side(b) for p in images_of(TEST_DIR) for b in rd(TEST_DIR / "labels" / (p.stem + ".txt"))]); q1, q2 = np.percentile(sizes, 33), np.percentile(sizes, 67)
rec = {"small": [0, 0], "medium": [0, 0], "large": [0, 0]}; den = {"1 patch": [0, 0], "2-3 patches": [0, 0], "4+ patches": [0, 0]}
for k, pr in FUSED_TEST.items():
    gt = rd(TEST_DIR / "labels" / (Path(k).stem + ".txt")); pc = [b for b, c in pr if c >= THR]
    for g in gt:
        hit = any(iou(g, b) >= 0.5 for b in pc); z = "small" if side(g) <= q1 else "medium" if side(g) <= q2 else "large"; rec[z][1] += 1; rec[z][0] += hit
        z2 = "1 patch" if len(gt) <= 1 else "2-3 patches" if len(gt) <= 3 else "4+ patches"; den[z2][1] += 1; den[z2][0] += hit
fig, ax = plt.subplots(1, 2, figsize=(9, 3.2))
for a, dct, t in ((ax[0], rec, "recall by patch size"), (ax[1], den, "recall by patches in the photo")):
    a.bar(list(dct), [v[0] / max(1, v[1]) for v in dct.values()], color="#4c78a8"); a.set_ylim(0, 1); a.set_title(t); [a.text(i, v[0] / max(1, v[1]) + .02, "%d/%d" % (v[0], v[1]), ha="center", fontsize=8) for i, v in enumerate(dct.values())]
plt.tight_layout(); plt.show()
tt = []
for thr in (0.10, 0.125, 0.15, 0.175, 0.20, 0.25, 0.30):
    rr = rows_at(FUSED_TEST, TEST_DIR, thr); p, r, f = prf(rr); tt.append(dict(threshold=thr, precision=p, recall=r, F1=f, zone_F1=zone_f1(rr), photos_with_patch=np.mean([x[6] > 0 for x in rr])))
display(pd.DataFrame(tt).round(3)); print("a higher threshold gives fewer, more certain boxes (higher precision, lower recall)")
''',
    examples_code=r'''
names = [p.name for p in images_of(TEST_DIR)][8:16]
show_examples(FUSED_TEST, TEST_DIR, THR, names, title="test photos")
''',
    summary_code=r'''
ref = pd.DataFrame([("first recipe, old rule, public + own boxes (installed before)", "old-rule test", 0.178), ("same recipe on old boxes, scored on rule-v2 test", "rule-v2 test", 0.077),
                    ("model installed before this work, scored on rule-v2 test", "rule-v2 test", 0.207), ("rule-v2 conversion, 3-model average", "rule-v2 test", 0.341)], columns=["model", "test set", "mAP@0.5"])
ref.loc[len(ref)] = ["THIS NOTEBOOK: 3-model average (corrected rule-v2 boxes + public photos)", "rule-v2 test", R["mAP50"]]
display(ref.round(3))
print("Final: mAP@0.5 %.3f, F1 %.3f at threshold %.3f on %d test photos. Scores under rule v2 are not comparable with scores under the old rule (the target changed: larger, whole-patch boxes)." % (R["mAP50"], R["F1"], R["threshold"], R["n_photos"]))
print("Limits: one annotator; boxes are candidate patches, not a diagnosis; no independent set under rule v2 yet; pass C (reproducibility one day later) not done.")
''',
    export_md="""## 11. Export for the app (optional)
Turns the three models into ONNX files for the C# app: `lesion_detector_hyperpigmentation.onnx` (seed 42) and `..._m2.onnx`, `..._m3.onnx` (the extra models, averaged inside the app). Off by default; it overwrites the files in `SkinDevApp/Model`.""",
    export_code=r'''
EXPORT = False
if EXPORT:
    from ultralytics import YOLO
    MODEL_DIR = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/SkinDevApp/Model")
    for suffix, s in (("", SEEDS[0]), ("_m2", SEEDS[1]), ("_m3", SEEDS[2])):
        o = Path(YOLO(str(WEIGHTS[s])).export(format="onnx", imgsz=800, opset=12, simplify=True, dynamic=False)); shutil.copy2(o, MODEL_DIR / ("lesion_detector_hyperpigmentation%s.onnx" % suffix)); print("exported seed", s)
    print("also set default_conf in lesion_detector_hyperpigmentation.json to %.3f and nms_iou to 0.6" % THR)
else: print("EXPORT = False: nothing written")
''',
)

# ============================================================================================================== acne
ACNE = dict(
    intro="""# Acne lesion detector: training and results

PrecisionSkin / LUMYVUE. One-class YOLOv8s detector that proposes **candidate acne spots** (a box per spot) on face photos. It replaces the acne class of the earlier three-class detector.
This notebook shows **how the final model is trained**, **how its threshold is chosen** and **how it scores** on photos that were never used for training or model selection.

Boxes are candidate lesions, not a diagnosis. The detector is also not acne-specific (it can put boxes on other papular conditions), which is why the classifier and Grad-CAM++ are reported separately.""",
    howto="""- Run it in WSL with the `.venv_det` environment (ultralytics, torch, pandas, matplotlib).
- `TRAIN = False` (default) uses the finished runs (a few minutes). `TRAIN = True` retrains the three seeds (about 2 hours on an RTX 4060) into a new folder; the finished runs are never overwritten.
- The development test photos are scored once and stored in a lock file. The separate **independent set** (41 photos) is not used in this notebook; its result is in `RESULTS_FOR_PAPER.md`.""",
    config=r'''
import os, json, time, shutil
from pathlib import Path
TRAIN = False                      # True = retrain the three seeds (slow); False = use the finished runs
QUICK_TEST = False                 # True = 1 epoch of seed 42 only, into a throw-away folder: checks that training works (about 3 minutes)
ALLOW_RESCORE = False
HOME = Path.home() / "PrecisionSkinV1"
DS = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/acne_dataset_v4")
TRAIN_DIR, VAL_DIR, TEST_DIR = DS / "train", DS / "val", DS / "test"
SEEDS = [42, 7, 123]
FINAL_RUNS = {s: HOME / "runs" / "lesion_acne_boxed_only" / "models" / ("seed_%d" % s) for s in SEEDS}
REPORT_RUNS = HOME / "runs" / "report_acne"
LOCK = HOME / "runs" / "report_acne_test_scored.json"
TRAIN_ARGS = dict(imgsz=800, epochs=100, patience=30, batch=8, workers=2, cos_lr=True, close_mosaic=10, fliplr=0.5, flipud=0.0, degrees=10, scale=0.5,
                  hsv_h=0.015, hsv_s=0.5, hsv_v=0.35)
BASE_MODEL = str(HOME / "yolov8s.pt") if (HOME / "yolov8s.pt").exists() else "yolov8s.pt"      # pretrained starting weights
if os.environ.get("NB_QUICK") == "1": TRAIN, QUICK_TEST = True, True
if QUICK_TEST:
    TRAIN = True; SEEDS = [42]; TRAIN_ARGS = dict(TRAIN_ARGS, epochs=1); REPORT_RUNS = HOME / "runs" / "report_acne_QUICKTEST"
    LOCK = HOME / "runs" / "report_acne_QUICKTEST_test.json"
print("mode:", "QUICK TEST (1 epoch, throw-away folder)" if QUICK_TEST else "TRAIN" if TRAIN else "use finished runs")
''',
    data_md="""## 2. The data
Acne dataset v4: photos boxed by hand, one box per spot (one class). Training, validation (for choosing models and the threshold) and a development test set (167 photos, hand-boxed, never used for training).
The development test set did guide some earlier decisions (see `ACNE_DETECTOR_VERDICT.md`), so a separate 41-photo independent set was boxed blind and scored once.""",
    data_code=r'''
for name, d in (("train", TRAIN_DIR), ("validation", VAL_DIR), ("test", TEST_DIR)):
    ims = images_of(d); nb = [len(rd(d / "labels" / (p.stem + ".txt"))) for p in ims]
    print("%-10s %4d photos | %5d boxes | %.1f boxes per photo | median box %.3f of the photo" % (name, len(ims), sum(nb), np.mean(nb), np.median([side(b) for p in ims for b in rd(d / "labels" / (p.stem + ".txt"))])))
fig, ax = plt.subplots(1, 2, figsize=(10, 3.5)); ax[0].hist([len(rd(TEST_DIR / "labels" / (p.stem + ".txt"))) for p in images_of(TEST_DIR)], bins=20, color="#4c78a8"); ax[0].set_title("boxes per test photo")
ax[1].hist([side(b) * 100 for p in images_of(TEST_DIR) for b in rd(TEST_DIR / "labels" / (p.stem + ".txt"))], bins=30, color="#f58518"); ax[1].set_title("test box size (% of the photo)"); plt.tight_layout(); plt.show()
show_examples({p.name: [] for p in images_of(TEST_DIR)[:8]}, TEST_DIR, 1.0, [p.name for p in images_of(TEST_DIR)[:8]], title="test photos with your boxes")
''',
    train_md="""## 3. Training
YOLOv8s (small), 800 px input, up to 100 epochs, cosine learning-rate decay, mosaic mixing off for the last 10 epochs, flips / small rotations / scale / colour changes as augmentation, early stopping after 30 epochs without improvement.
Three seeds (42, 7, 123); the seed changes the random start and image order. With `TRAIN = False` this cell only prints the settings.""",
    train_code=r'''
if TRAIN:
    from ultralytics import YOLO
    import torch, yaml
    REPORT_RUNS.mkdir(parents=True, exist_ok=True)
    yml = REPORT_RUNS / "data.yaml"
    yaml.safe_dump({"path": str(DS), "train": "train/images", "val": "val/images", "names": {0: "acne"}}, open(yml, "w"))
    for s in SEEDS:
        last = REPORT_RUNS / ("seed_%d" % s) / "weights" / "last.pt"
        if last.exists(): YOLO(str(last)).train(resume=True)
        else: YOLO(BASE_MODEL).train(data=str(yml), seed=s, device=0 if torch.cuda.is_available() else "cpu", project=str(REPORT_RUNS), name="seed_%d" % s, exist_ok=True, **TRAIN_ARGS)
    FINAL_RUNS = {s: REPORT_RUNS / ("seed_%d" % s) for s in SEEDS}
else:
    print(json.dumps(TRAIN_ARGS, indent=1))
WEIGHTS = {s: FINAL_RUNS[s] / "weights" / "best.pt" for s in SEEDS}
print({s: p.exists() for s, p in WEIGHTS.items()})
''',
    curves_code=r'''curves([FINAL_RUNS[s] for s in SEEDS], ["seed %d" % s for s in SEEDS])''',
    val_code=r'''
VAL_PRED = {s: raw_preds(WEIGHTS[s], VAL_DIR) for s in SEEDS}
rows = []
for s in SEEDS:
    sc = score(VAL_PRED[s], VAL_DIR); c = best_conf(sc); p, r, f = at_conf(sc, c)
    rows.append(dict(seed=s, mAP50=sc["AP50"], mAP50_95=sc["AP50_95"], threshold=c, precision=p, recall=r, F1=f))
vt = pd.DataFrame(rows); display(vt.round(3)); print("mean mAP@0.5 %.3f (std %.3f)" % (vt.mAP50.mean(), vt.mAP50.std()))
''',
    ens_code=r'''
FUSED_VAL = {k: fuse([VAL_PRED[s][k] for s in SEEDS]) for k in VAL_PRED[SEEDS[0]]}
sv = score(FUSED_VAL, VAL_DIR); THR = best_conf(sv); p, r, f = at_conf(sv, THR)
print("3-model average on validation: mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | best F1 %.3f at threshold %.3f (precision %.3f, recall %.3f)" % (sv["AP50"], sv["AP50_95"], f, THR, p, r))
cs = np.arange(0.05, 0.6, 0.025); pr_ = [at_conf(sv, c) for c in cs]
plt.figure(figsize=(6, 3.5)); plt.plot(cs, [x[0] for x in pr_], label="precision"); plt.plot(cs, [x[1] for x in pr_], label="recall"); plt.plot(cs, [x[2] for x in pr_], label="F1", lw=2)
plt.axvline(THR, color="k", ls=":"); plt.xlabel("confidence threshold"); plt.legend(); plt.grid(alpha=.3); plt.title("validation: threshold choice"); plt.show()
''',
    test_md="""## 7. The development test photos (scored once)
167 hand-boxed photos never used for training. The threshold from validation is applied; nothing is tuned here. Note: this set guided some earlier decisions, so the independent set is the more cautious number.""",
    test_code=r'''
if LOCK.exists() and not ALLOW_RESCORE:
    R = json.loads(LOCK.read_text()); print("stored result from", R["scored_at"]); TEST_PRED = None
else:
    TEST_PRED = {s: raw_preds(WEIGHTS[s], TEST_DIR) for s in SEEDS}
    FUSED_TEST = {k: fuse([TEST_PRED[s][k] for s in SEEDS]) for k in TEST_PRED[SEEDS[0]]}
    st = score(FUSED_TEST, TEST_DIR); rows_t = rows_at(FUSED_TEST, TEST_DIR, THR); p, r, f = prf(rows_t); lo, hi = bootstrap_f1(rows_t)
    singles = [score(TEST_PRED[s], TEST_DIR)["AP50"] for s in SEEDS]
    R = dict(scored_at=time.strftime("%Y-%m-%d %H:%M"), threshold=THR, mAP50=st["AP50"], mAP50_95=st["AP50_95"], precision=p, recall=r, F1=f, F1_ci=[lo, hi], zone_F1=zone_f1(rows_t),
             photos_with_patch=float(np.mean([x[6] > 0 for x in rows_t])), single_seed_mAP50=singles, n_photos=len(FUSED_TEST), F1_by_iou=f1_by_iou(FUSED_TEST, TEST_DIR, THR),
             count_mae=float(np.mean([abs(x[5] - x[6]) for x in rows_t])), count_within1=float(np.mean([abs(x[5] - x[6]) <= 1 for x in rows_t])),
             count_spearman=float(np.corrcoef(np.argsort(np.argsort([x[5] + 1e-9 * i for i, x in enumerate(rows_t)])), np.argsort(np.argsort([x[6] + 1e-9 * i for i, x in enumerate(rows_t)])))[0, 1]))
    LOCK.parent.mkdir(parents=True, exist_ok=True); LOCK.write_text(json.dumps(R, indent=1)); print("scored and stored in", LOCK)
print("DEVELOPMENT TEST (%d photos), 3-model average at threshold %.3f" % (R["n_photos"], R["threshold"]))
print("  mAP@0.5 %.3f | mAP@0.5:0.95 %.3f | precision %.3f | recall %.3f | F1 %.3f (95%% range %.2f-%.2f)" % (R["mAP50"], R["mAP50_95"], R["precision"], R["recall"], R["F1"], *R["F1_ci"]))
print("  3x3 face-zone F1 %.3f | photos with a box %.0f %% | count: mean error %.2f lesions, Spearman %.2f" % (R["zone_F1"], 100 * R["photos_with_patch"], R["count_mae"], R["count_spearman"]))
print("  single seeds, mAP@0.5:", [round(x, 3) for x in R["single_seed_mAP50"]])
ious = list(map(float, R["F1_by_iou"])); plt.figure(figsize=(5.5, 3.2)); plt.plot(ious, [R["F1_by_iou"][str(i)] if str(i) in R["F1_by_iou"] else R["F1_by_iou"][i] for i in ious], "o-"); plt.xlabel("IoU needed to count a spot as found"); plt.ylabel("F1"); plt.grid(alpha=.3)
plt.title("test: F1 falls as the overlap needed rises (spots are small, about 28 px)"); plt.show()
''',
    fail_code=r'''
if TEST_PRED is None:
    TEST_PRED = {s: raw_preds(WEIGHTS[s], TEST_DIR) for s in SEEDS}
FUSED_TEST = {k: fuse([TEST_PRED[s][k] for s in SEEDS]) for k in TEST_PRED[SEEDS[0]]}
sizes = np.array([side(b) for p in images_of(TEST_DIR) for b in rd(TEST_DIR / "labels" / (p.stem + ".txt"))]); q1, q2 = np.percentile(sizes, 33), np.percentile(sizes, 67)
rec = {"small": [0, 0], "medium": [0, 0], "large": [0, 0]}; den = {"sparse (<=6)": [0, 0], "medium (7-14)": [0, 0], "dense (15+)": [0, 0]}
for k, pr in FUSED_TEST.items():
    gt = rd(TEST_DIR / "labels" / (Path(k).stem + ".txt")); pc = [b for b, c in pr if c >= THR]
    for g in gt:
        hit = any(iou(g, b) >= 0.5 for b in pc); z = "small" if side(g) <= q1 else "medium" if side(g) <= q2 else "large"; rec[z][1] += 1; rec[z][0] += hit
        z2 = "sparse (<=6)" if len(gt) <= 6 else "medium (7-14)" if len(gt) <= 14 else "dense (15+)"; den[z2][1] += 1; den[z2][0] += hit
fig, ax = plt.subplots(1, 2, figsize=(9, 3.2))
for a, dct, t in ((ax[0], rec, "recall by spot size"), (ax[1], den, "recall by spots in the photo")):
    a.bar(list(dct), [v[0] / max(1, v[1]) for v in dct.values()], color="#4c78a8"); a.set_ylim(0, 1); a.set_title(t); [a.text(i, v[0] / max(1, v[1]) + .02, "%d/%d" % (v[0], v[1]), ha="center", fontsize=8) for i, v in enumerate(dct.values())]
plt.tight_layout(); plt.show()
tt = []
for thr in (0.15, 0.20, 0.25, 0.275, 0.30, 0.35, 0.40):
    rr = rows_at(FUSED_TEST, TEST_DIR, thr); p, r, f = prf(rr); tt.append(dict(threshold=thr, precision=p, recall=r, F1=f, zone_F1=zone_f1(rr), photos_with_a_box=np.mean([x[6] > 0 for x in rr])))
display(pd.DataFrame(tt).round(3)); print("tiny spots are the main weakness; a higher threshold trades recall for precision")
''',
    examples_code=r'''
names = [p.name for p in images_of(TEST_DIR)][8:16]
show_examples(FUSED_TEST, TEST_DIR, THR, names, title="test photos")
''',
    summary_code=r'''
ref = pd.DataFrame([("earlier three-class detector (acne class)", "development test", 0.220), ("final single model (seed 123)", "development test", 0.419),
                    ("final single model (seed 123)", "independent set (41 photos, scored once)", 0.358)], columns=["model", "test set", "mAP@0.5"])
ref.loc[len(ref)] = ["THIS NOTEBOOK: 3-model average", "development test", R["mAP50"]]
display(ref.round(3))
print("Final: mAP@0.5 %.3f, F1 %.3f at threshold %.3f on the %d development test photos." % (R["mAP50"], R["F1"], R["threshold"], R["n_photos"]))
print("Usage level (earlier single-model results): photo AUC 0.96, zone F1 0.81-0.82, count error about 4 lesions. Limits: tiny spots are missed, the detector is not acne-specific, only two acne-negative photos in the independent set.")
''',
    export_md="""## 11. Export for the app (optional)
Turns the three models into ONNX files for the C# app: `lesion_detector_acne.onnx` (seed 123 is the main file in the app; here the first seed in `SEEDS` is used) and `..._m2.onnx`, `..._m3.onnx`. Off by default; it overwrites the files in `SkinDevApp/Model`.""",
    export_code=r'''
EXPORT = False
if EXPORT:
    from ultralytics import YOLO
    MODEL_DIR = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/SkinDevApp/Model")
    order = [123, 42, 7]
    for suffix, s in (("", order[0]), ("_m2", order[1]), ("_m3", order[2])):
        o = Path(YOLO(str(WEIGHTS[s])).export(format="onnx", imgsz=800, opset=12, simplify=True, dynamic=False)); shutil.copy2(o, MODEL_DIR / ("lesion_detector_acne%s.onnx" % suffix)); print("exported seed", s)
    print("also set default_conf in lesion_detector_acne.json to %.3f and nms_iou to 0.6" % THR)
else: print("EXPORT = False: nothing written")
''',
)

if __name__ == "__main__":
    for name, cfg in (("PrecisionSkin_Acne_Training_and_Results.ipynb", ACNE), ("PrecisionSkin_Hyperpigmentation_Training_and_Results.ipynb", HYP)):
        dest = OUT / ("acne" if "Acne" in name else "hyperpigmentation") / "notebooks" / name; nbf.write(build(cfg), str(dest)); print("wrote", dest)
