#!/usr/bin/env python
"""acne_box_scale_check.py - is the acne disagreement mostly a box-SIZE convention? Shrinks / grows the ORIGINAL boxes of the 30 re-boxed photos around their centres
and measures the agreement with the blind re-box (F1 at IoU 0.5 and 0.3). Read-only.   Run in WSL."""
import json, sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_final_eval as F

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing"); DS = R / "acne_dataset_v4" / "val"; D = R / "Reliability_Rebox_Acne"; S = D / "Elaiza__acne_rebox"
ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]; mp = json.load(open(D / "name_map.json")); old, new = [], []
for orig, nm in mp.items():
    f = S / "labels" / (nm + ".txt")
    if f.exists() and f.stat().st_mtime > ref + 1: old.append(F.rd(DS / "labels" / (Path(orig).stem + ".txt"))); new.append(F.rd(f))

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
    p, r = tp / max(1, tp + fp), tp / max(1, tp + fn); return 2 * p * r / max(1e-9, p + r)

print("original boxes shrunk or grown around their centre, compared with your blind re-box (30 photos)")
print("scale   F1@IoU0.5   F1@IoU0.3   median side")
for s in (1.0, 0.9, 0.8, 0.7, 0.65, 0.6, 0.5):
    sc = [[(b[0], b[1], b[2] * s, b[3] * s) for b in L] for L in old]
    print("%.2f      %.3f       %.3f       %.3f" % (s, f1(sc, new, 0.5), f1(sc, new, 0.3), np.median([(b[2] * b[3]) ** 0.5 for L in sc for b in L])))
