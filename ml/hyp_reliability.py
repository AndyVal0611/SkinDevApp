#!/usr/bin/env python
"""hyp_reliability.py - how much do you agree with yourself? Compares the blind re-box of 30 validation photos with the original boxes of the same photos
(and the model's boxes against both). Read-only."""
import json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
from ultralytics import YOLO
D = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Reliability_Rebox"); S = D / "Elaiza__hyperpigmentation_rebox"
ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]
saved = [f for f in sorted((S / "labels").glob("*.txt")) if f.stat().st_mtime > ref]
print("re-boxed and saved: %d of 30 photos" % len(saved))
def pr_f(A, B, thr):
    """precision/recall/F1 of boxes A against reference B"""
    tp = fp = fn = 0
    for a, b in zip(A, B):
        used = set(); t = 0
        for x in a:
            best, bj = 0, -1
            for j, y in enumerate(b):
                if j in used: continue
                v = F.iou(x, y)
                if v > best: best, bj = v, j
            if bj >= 0 and best >= thr: used.add(bj); t += 1
        tp += t; fp += len(a) - t; fn += len(b) - t
    p, r = tp / max(1, tp + fp), tp / max(1, tp + fn); return p, r, 2 * p * r / max(1e-9, p + r)
old = [F.rd(F.VAL / "labels" / f.name) for f in saved]; new = [F.rd(f) for f in saved]
m = YOLO(str(F.E / "runs/H10_v2_lowlr/weights/best.pt")); names = [next(iter((S / "images").glob(f.stem + ".*"))) for f in saved]
mod = []
for p, r in zip(names, m.predict([str(x) for x in names], imgsz=800, conf=0.20, iou=0.6, verbose=False, device=0)):
    mod.append([tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else [])
print("boxes: original %d (%.1f per photo) | re-box %d (%.1f per photo) | model %d" % (sum(map(len, old)), np.mean(list(map(len, old))), sum(map(len, new)), np.mean(list(map(len, new))), sum(map(len, mod))))
print("\n%-34s %8s %8s %8s %8s" % ("F1 at IoU", "0.2", "0.3", "0.5", "0.7"))
for name, a, b in (("you now vs you before", new, old), ("model vs original boxes", mod, old), ("model vs re-box", mod, new)):
    print("%-34s %s" % (name, " ".join("%8.3f" % pr_f(a, b, t)[2] for t in (0.2, 0.3, 0.5, 0.7))))
p, r, f = pr_f(new, old, 0.5); print("\nyou now vs you before at IoU 0.5: precision %.3f recall %.3f F1 %.3f" % (p, r, f))
ious = []
for a, b in zip(new, old):
    for x in a:
        v = max([F.iou(x, y) for y in b], default=0)
        if v > 0.1: ious.append(v)
print("IoU of boxes that overlap: median %.2f | 25%% below %.2f | 75%% below %.2f" % (np.median(ious), np.percentile(ious, 25), np.percentile(ious, 75)))
