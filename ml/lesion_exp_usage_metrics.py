#!/usr/bin/env python
"""
lesion_exp_usage_metrics.py - photo-level (AUC), count-level and zone-level metrics for chosen EXPERIMENT runs, on the VALIDATION photos only (never the test photos).
    python lesion_exp_usage_metrics.py A0_base A7_fixlabels
Healthy photos come from Classes/Normal (not individually verified).
"""
import csv, random, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyperpig_autolabel as H
from ultralytics import YOLO

P = Path.home() / "PrecisionSkinV1"; EXP = P / "lesion_experiments"; VAL = P / "acne_detector_v4/dataset/val"; EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
THR = (0.26, 0.35)


def lab(p): return [tuple(map(float, l.split()[1:5])) for l in (VAL / "labels" / (p.stem + ".txt")).read_text().splitlines() if len(l.split()) >= 5]
def zones(bx):
    z = np.zeros((3, 3), bool)
    for cx, cy, w, h in bx: z[min(2, int(cy * 3)), min(2, int(cx * 3))] = True
    return z
def auc(pos, neg):
    s = np.concatenate([pos, neg]); y = np.concatenate([np.ones(len(pos)), np.zeros(len(neg))]); o = np.argsort(s); r = np.empty(len(s)); r[o] = np.arange(1, len(s) + 1)
    for v in np.unique(s): m = s == v; r[m] = r[m].mean()
    return (r[y == 1].sum() - len(pos) * (len(pos) + 1) / 2) / (len(pos) * len(neg))

pos = sorted((VAL / "images").glob("*"))
normal = [p for p in Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Normal").rglob("*") if p.suffix.lower() in EXT]; random.Random(11).shuffle(normal); neg = normal[:200]
reg = {r["id"]: r for r in csv.DictReader(open(EXP / "EXPERIMENTS.csv")) if r["val_set"] in ("val", "")}
for eid in sys.argv[1:]:
    m = YOLO(str(EXP / "runs" / eid / "weights" / "best.pt"))
    def run(p):
        try: o = m.predict(str(p), imgsz=800, conf=0.05, iou=0.5, verbose=False, device=0)[0]
        except Exception: return None
        return (o.boxes.xywhn.cpu().numpy(), o.boxes.conf.cpu().numpy()) if len(o.boxes) else (np.zeros((0, 4)), np.zeros(0))
    Pp = [(p, run(p)) for p in pos]; Pp = [(p, x) for p, x in Pp if x is not None]; Nn = [x for x in (run(p) for p in neg) if x is not None]
    pm = np.array([c.max() if len(c) else 0.0 for _, (b, c) in Pp]); nm = np.array([c.max() if len(c) else 0.0 for b, c in Nn])
    r = reg.get(eid, {})
    print("\n=== %s | %s ===" % (eid, r.get("note", "")))
    print("box level (validation, confidence %s): mAP@0.5 %s | mAP@0.5-0.95 %s | precision %s | recall %s | F1 %s | best epoch %s of %s" % (r.get("val_conf"), r.get("val_mAP50"), r.get("val_mAP50_95"), r.get("val_P"), r.get("val_R"), r.get("val_F1"), r.get("best_epoch"), r.get("epochs_run")))
    print("photo level: AUC %.3f (%d acne validation photos vs %d healthy photos, healthy NOT verified)" % (auc(pm, nm), len(pm), len(nm)))
    for t in THR:
        npred = np.array([int((c >= t).sum()) for _, (b, c) in Pp]); ngt = np.array([len(lab(p)) for p, _ in Pp])
        tp = fp = fn = 0
        for p, (b, c) in Pp:
            zp = zones([x for x, cc in zip(b, c) if cc >= t]); zg = zones(lab(p)); tp += int((zp & zg).sum()); fp += int((zp & ~zg).sum()); fn += int((~zp & zg).sum())
        zP, zR = tp / max(1, tp + fp), tp / max(1, tp + fn)
        print("  threshold %.2f: sensitivity %.3f | specificity %.3f | count Spearman %.3f | count MAE %.2f (bias %+.2f) | zones P %.3f R %.3f F1 %.3f" % (
            t, float((pm >= t).mean()), float((nm < t).mean()), float(np.corrcoef(np.argsort(np.argsort(npred)), np.argsort(np.argsort(ngt)))[0, 1]), float(np.abs(npred - ngt).mean()), float((npred - ngt).mean()), zP, zR, 2 * zP * zR / max(1e-9, zP + zR)))
