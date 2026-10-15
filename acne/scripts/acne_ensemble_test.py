#!/usr/bin/env python
"""acne_ensemble_test.py - the 3-seed acne average scored ONCE on the 167-photo development test set; threshold = best F1 on the 154 validation photos (0.275, chosen before this run).
The independent set is not touched. The installed single model (seed 123) is scored on the same photos at its own validation threshold for comparison."""
import sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

DS = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/acne_dataset_v4"); VAL, TEST = DS / "val", DS / "test"
R = Path.home() / "PrecisionSkinV1/runs/lesion_acne_boxed_only/models"; paths = [R / ("seed_%d/weights/best.pt" % s) for s in (42, 7, 123)]
THR = 0.275


def rows_for(preds, thr):
    return F.counts({k: (F.rd(TEST / "labels" / (Path(k).stem + ".txt")), v) for k, v in preds.items()}, thr)


def boot(rows):
    rng = np.random.default_rng(0); idx = np.arange(len(rows)); b = [F.prf([rows[i] for i in rng.choice(idx, len(idx))])[2] for _ in range(1000)]; return np.percentile(b, 2.5), np.percentile(b, 97.5)


PT = [T.raw_preds(YOLO(str(p)), TEST, False) for p in paths]
ft = {k: T.fuse([p[k] for p in PT]) for k in PT[0]}; ap, _ = T.evaluate(ft, TEST); rows = rows_for(ft, THR); p, r, f = F.prf(rows); lo, hi = boot(rows)
print("TEST (167 photos), 3-seed average at validation threshold %.3f: mAP@0.5 %.3f | precision %.3f recall %.3f F1 %.3f (95%% range %.3f-%.3f)" % (THR, ap, p, r, f, lo, hi))
# installed single model (seed 123) at its own validation threshold
pv = T.raw_preds(YOLO(str(paths[2])), VAL, False); _, (fv, cv, _, _) = T.evaluate(pv, VAL)
aps, _ = T.evaluate(PT[2], TEST); rs = rows_for(PT[2], cv); ps, rrs, fs = F.prf(rs); lo2, hi2 = boot(rs)
print("installed seed 123 at its validation threshold %.3f: mAP@0.5 %.3f | precision %.3f recall %.3f F1 %.3f (95%% range %.3f-%.3f)" % (cv, aps, ps, rrs, fs, lo2, hi2))
for i, s in enumerate((42, 7)):
    pv_ = T.raw_preds(YOLO(str(paths[i])), VAL, False); _, (_, cvi, _, _) = T.evaluate(pv_, VAL); a_, _ = T.evaluate(PT[i], TEST); x = F.prf(rows_for(PT[i], cvi))
    print("seed %3d at its validation threshold %.3f: mAP@0.5 %.3f | precision %.3f recall %.3f F1 %.3f" % (s, cvi, a_, x[0], x[1], x[2]))
tp = sum(len(x[3] & x[4]) for x in rows); fp = sum(len(x[4] - x[3]) for x in rows); fn = sum(len(x[3] - x[4]) for x in rows)
print("3x3 zone F1 of the average: %.3f | photos with at least one box: %.1f%%" % (2 * tp / max(1, 2 * tp + fp + fn), 100 * np.mean([x[6] > 0 for x in rows])))
