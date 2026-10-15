#!/usr/bin/env python
"""acne_box_margin_check.py - what changes if the acne box CONVENTION is larger (a margin around each spot)? VALIDATION photos only; read-only.
For several box scales s (every box, yours and the model's, grown around its centre by s):
  - mAP@0.5 of the 3-seed acne average on the 154 validation photos
  - how well you agree with yourself (original boxes vs the blind re-box of 30 photos), F1 at IoU 0.5
A larger convention makes strict overlap measures more forgiving; it must be applied to ALL labels (train, val, test), documented, and chosen by human agreement, never by a test score.
Run in WSL."""
import json, shutil, sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing"); VAL = R / "acne_dataset_v4" / "val"
A = Path.home() / "PrecisionSkinV1/runs/lesion_acne_boxed_only/models"; paths = [A / ("seed_%d/weights/best.pt" % s) for s in (123, 42, 7)]
def grow(b, s):
    w, h = min(1.0, b[2] * s), min(1.0, b[3] * s); return (b[0], b[1], w, h)
P = [T.raw_preds(YOLO(str(p)), VAL, False) for p in paths]; fused = {k: T.fuse([p[k] for p in P]) for k in P[0]}
# self agreement
D = R / "Reliability_Rebox_Acne"; S = D / "Elaiza__acne_rebox"; ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]; mp = json.load(open(D / "name_map.json")); old, new = [], []
for orig, nm in mp.items():
    f = S / "labels" / (nm + ".txt")
    if f.exists() and f.stat().st_mtime > ref + 1: old.append(F.rd(VAL / "labels" / (Path(orig).stem + ".txt"))); new.append(F.rd(f))
def f1(X, Y, thr):
    tp = fp = fn = 0
    for a, b in zip(X, Y):
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
tmp = Path.home() / "PrecisionSkinV1" / "acne_margin_tmp"
print("box scale | validation mAP@0.5 of the 3-model average | your agreement with yourself (F1@0.5, 30 photos)")
for s in (1.0, 1.25, 1.5, 1.75, 2.0, 2.5):
    shutil.rmtree(tmp, ignore_errors=True); (tmp / "labels").mkdir(parents=True)
    for f in (VAL / "labels").glob("*.txt"): (tmp / "labels" / f.name).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % grow(b, s) for b in F.rd(f)))
    pr = {k: [(grow(b, s), c) for b, c in v] for k, v in fused.items()}; ap, (f, c, p_, r_) = T.evaluate(pr, tmp)
    sa = f1([[grow(b, s) for b in L] for L in old], [[grow(b, s) for b in L] for L in new], 0.5)
    print("  %.2f    |  mAP@0.5 %.3f  (best F1 %.3f)            |  %.3f" % (s, ap, f, sa))
