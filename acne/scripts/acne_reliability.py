#!/usr/bin/env python
"""acne_reliability.py - how much do you agree with yourself on acne? Compares the blind re-box of 30 validation photos with the original boxes of the same photos
(and the final model's boxes against both). Read-only.   Run in WSL:  python acne_reliability.py"""
import json, sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_final_eval as F
from ultralytics import YOLO

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing"); DS = R / "acne_dataset_v4" / "val"; D = R / "Reliability_Rebox_Acne"; S = D / "Elaiza__acne_rebox"
ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]; mp = json.load(open(D / "name_map.json"))
side = lambda b: (b[2] * b[3]) ** 0.5
keys, old, new = [], [], []
for orig, nm in mp.items():
    f = S / "labels" / (nm + ".txt")
    if f.exists() and f.stat().st_mtime > ref + 1: keys.append(orig); old.append(F.rd(DS / "labels" / (Path(orig).stem + ".txt"))); new.append(F.rd(f))
print("re-boxed and saved: %d of %d photos" % (len(keys), len(mp)))
if not keys: sys.exit()

def f1(A, B, thr):
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

print("boxes per photo: original %.1f | re-box %.1f | median box side: original %.3f, re-box %.3f" % (np.mean(list(map(len, old))), np.mean(list(map(len, new))),
      np.median([side(b) for L in old for b in L]), np.median([side(b) for L in new for b in L])))
W = Path.home() / "PrecisionSkinV1/runs/lesion_acne_boxed_only/models/seed_123/weights/best.pt"; m = YOLO(str(W)); mod = []
for k in keys:
    r = m.predict(str(next(iter((DS / "images").glob(Path(k).stem + ".*")))), imgsz=800, conf=0.25, iou=0.6, verbose=False, device=0)[0]
    mod.append([tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else [])
print("\nF1 at IoU                      0.2    0.3    0.5    0.7")
for name, a, b in (("you now vs you before", new, old), ("model vs original boxes", mod, old), ("model vs re-box", mod, new)):
    print("%-28s %s" % (name, " ".join("%6.3f" % f1(a, b, t)[2] for t in (0.2, 0.3, 0.5, 0.7))))
p, r, f = f1(new, old, 0.5); print("\nyou now vs you before at IoU 0.5: precision %.3f recall %.3f F1 %.3f" % (p, r, f))
ious = [max([F.iou(x, y) for y in b], default=0) for a, b in zip(new, old) for x in a]; ious = [v for v in ious if v > 0.1]
print("IoU of boxes that overlap: median %.2f | 25%% below %.2f | 75%% below %.2f" % (np.median(ious), np.percentile(ious, 25), np.percentile(ious, 75)))
ng = np.array([len(x) for x in old]); nn = np.array([len(x) for x in new]); print("count per photo: same %d of %d, within 2: %d | correlation %.2f" % ((ng == nn).sum(), len(ng), (abs(ng - nn) <= 2).sum(), np.corrcoef(ng, nn)[0, 1]))
