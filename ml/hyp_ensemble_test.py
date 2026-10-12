#!/usr/bin/env python
"""hyp_ensemble_test.py - the plain 3-seed average (chosen on VALIDATION: threshold 0.225) scored ONCE on the 100 hand-boxed test photos, next to the single chosen model and the installed one."""
import sys
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import numpy as np
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

THR = 0.225
models = [YOLO(str(p)) for p in T.PATHS]
P = [T.raw_preds(m, F.TEST, False) for m in models]
fused = {n: T.fuse([p[n] for p in P]) for n in P[0]}
ap, _ = T.evaluate(fused, F.TEST)
singles = [T.evaluate(p, F.TEST)[0] for p in P]
data = {n: (F.rd(F.TEST / "labels" / (n.rsplit(".", 1)[0] + ".txt")), fused[n]) for n in fused}
rows = F.counts(data, THR); p, r, f = F.prf(rows)
rng = np.random.default_rng(0); idx = np.arange(len(rows)); bs = [F.prf([rows[i] for i in rng.choice(idx, len(idx))])[2] for _ in range(1000)]
zp = sum(len(x[3] & x[4]) for x in rows); zfp = sum(len(x[4] - x[3]) for x in rows); zfn = sum(len(x[3] - x[4]) for x in rows)
zf = 2 * zp / max(1, 2 * zp + zfp + zfn)
print("TEST (100 photos), 3-seed average at threshold %.3f: mAP@0.5 %.3f | precision %.3f recall %.3f F1 %.3f (95%% range %.3f-%.3f) | zone F1 %.3f | photos with a patch found %.1f%%" %
      (THR, ap, p, r, f, np.percentile(bs, 2.5), np.percentile(bs, 97.5), zf, 100 * np.mean([x[6] > 0 for x in rows])))
print("single seeds on the same photos (AP50 over all boxes): %s" % ", ".join("%.3f" % s for s in singles))
