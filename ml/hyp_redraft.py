#!/usr/bin/env python
"""
hyp_redraft.py - a better hyperpigmentation helper trained on the drafts you reviewed, then new DRAFTS for the photos you have not reviewed yet.

  1. helper training data: your sprint boxes (never the 100 test photos; never the 42 photos queued for the consistency fix: they are over-split / over-merged)
     + the drafts you already reviewed and saved, except 30 reviewed photos kept aside to measure the helper honestly
  2. train YOLOv8s 800 px (70 epochs, early stop), seed 42
  3. measure on the 30 kept-aside reviewed photos: how many of YOUR final boxes the helper finds (draft recall) and how many of its boxes you would keep (draft precision)
  4. redraw ONLY the photos you have not saved yet in Draft_Hyperpig (old drafts are backed up first); photos you saved are never touched
Nothing here is final: the new boxes are drafts for you to check and fix.
"""
import json, os, random, shutil, sys, time
from pathlib import Path
import numpy as np

sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyperpig_predraft as HP
from ultralytics import YOLO
import yaml, torch

P = Path.home() / "PrecisionSkinV1"; W = P / "hyperpig_redraft"; W.mkdir(exist_ok=True)
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); DR = B / "Draft_Hyperpig"
S = DR / "Elaiza__hyperpigmentation_draft"; IMG_EXT = HP.IMG_EXT
ref = json.load(open(DR / "_drafts/meta.json"))["drafted_at"]
reviewed = sorted(f for f in (S / "labels").glob("*.txt") if f.stat().st_mtime > ref + 1 and HP.read_boxes(f))
unreviewed = sorted(f for f in (S / "labels").glob("*.txt") if f.stat().st_mtime <= ref + 1)
print("reviewed drafts with boxes: %d | not reviewed yet: %d" % (len(reviewed), len(unreviewed)), flush=True)

# ---- 1. data
test = HP.hand_test_stems()
skip = {p.stem for p in (B / "Fix_Hyperpig_Consistency/Elaiza__hyperpigmentation_fix/images").glob("*")} if (B / "Fix_Hyperpig_Consistency").exists() else set()
own = [p for p in sorted((HP.OWN / "images").glob("*")) if p.suffix.lower() in IMG_EXT and p.stem not in test and p.stem not in skip and HP.read_boxes(HP.OWN / "labels" / (p.stem + ".txt"))]
rng = random.Random(3); rv = reviewed[:]; rng.shuffle(rv); hold = rv[:30]; use = rv[30:]
items = [(p, HP.OWN / "labels" / (p.stem + ".txt")) for p in own]
for f in use:
    img = next(iter((S / "images").glob(f.stem + ".*")), None)
    if img is not None: items.append((img, f))
rng.shuffle(items); nval = 40
DS = W / "dataset"; shutil.rmtree(DS, ignore_errors=True)
for sp in ("train", "val"): (DS / sp / "images").mkdir(parents=True); (DS / sp / "labels").mkdir(parents=True)
for k, (img, lab) in enumerate(items):
    sp = "val" if k < nval else "train"; shutil.copy2(img, DS / sp / "images" / img.name); shutil.copy2(lab, DS / sp / "labels" / (img.stem + ".txt"))
yaml.safe_dump({"path": str(DS), "train": "train/images", "val": "val/images", "names": {0: "hyperpigmentation"}}, open(DS / "data.yaml", "w"))
print("helper data: %d own photos + %d reviewed drafts | train %d / val %d | kept aside to measure: %d reviewed photos" % (len(own), len(use), len(items) - nval, nval, len(hold)), flush=True)

# ---- 2. train
best = W / "runs" / "helper" / "weights" / "best.pt"
if not best.exists():
    YOLO("yolov8s.pt").train(data=str(DS / "data.yaml"), imgsz=800, epochs=70, patience=20, batch=8, workers=2, seed=42, device=0 if torch.cuda.is_available() else "cpu",
                             project=str(W / "runs"), name="helper", exist_ok=True, plots=False, verbose=False)
m = YOLO(str(best))

# ---- 3. honest measurement on reviewed photos the helper never saw
def predict(img, conf):
    r = m.predict(str(img), imgsz=800, conf=conf, iou=0.5, max_det=100, verbose=False, device=0 if torch.cuda.is_available() else "cpu")[0]
    bx = [tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else []; cf = [float(c) for c in r.boxes.conf.cpu().numpy()] if len(r.boxes) else []
    return HP.clean([b for b in bx if b[2] > 0.003 and b[3] > 0.003], cf)[0]
res = {}
for conf in (0.15, 0.20, 0.25, 0.30, 0.40):
    tp = fp = fn = 0
    for f in hold:
        img = next(iter((S / "images").glob(f.stem + ".*"))); gt = HP.read_boxes(f); pr = [b for b, _ in predict(img, conf)]; used = set()
        for g in gt:
            hit = [j for j, q in enumerate(pr) if j not in used and HP.rel(g, q)[0] >= 0.30]
            if hit: used.add(hit[0]); tp += 1
            else: fn += 1
        fp += len(pr) - len(used)
    res[conf] = (tp / max(1, tp + fp), tp / max(1, tp + fn))
lines = ["helper measured on %d reviewed photos it never trained on (a box counts as found when it overlaps yours with IoU >= 0.30):" % len(hold)]
for c, (p_, r_) in res.items(): lines.append("  conf %.2f: draft precision %.2f (share of drafted boxes you would keep) | draft recall %.2f (share of YOUR boxes already drafted) | F1 %.2f" % (c, p_, r_, 2 * p_ * r_ / max(1e-9, p_ + r_)))
conf = max(res, key=lambda c: 2 * res[c][0] * res[c][1] / max(1e-9, res[c][0] + res[c][1])); conf = min(max(conf, 0.15), 0.30)
lines.append("drafting confidence chosen: %.2f (best F1 on those photos, kept between 0.15 and 0.30 so recall stays high)" % conf); print("\n".join(lines), flush=True)

# ---- 4. redraw only the unreviewed photos (old drafts backed up)
bk = DR / ("_old_drafts_backup_" + time.strftime("%Y%m%d_%H%M")); bk.mkdir()
n = nb = 0
for f in unreviewed:
    img = next(iter((S / "images").glob(f.stem + ".*")), None)
    if img is None: continue
    shutil.copy2(f, bk / f.name)
    boxes = [b for b, _ in predict(img, conf)]; text = "".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes)
    for g in (f, DR / "drafts_original" / f.name): g.write_text(text); os.utime(g, (ref - 5, ref - 5))             # stays "not reviewed" until you save it
    n += 1; nb += len(boxes)
lines.append("redrawn: %d photos you have not reviewed yet (%d boxes, %.1f per photo). Old drafts backed up in %s. Photos you saved were not touched." % (n, nb, nb / max(1, n), bk.name))
(DR / "REDRAFT_REPORT.txt").write_text("\n".join(lines)); print(lines[-1], flush=True)
