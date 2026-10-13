#!/usr/bin/env python
"""acne_ensemble_val.py - would averaging the three acne seeds help? VALIDATION photos only (154); the test and independent sets are not read."""
import sys
from pathlib import Path
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_ensemble_tta as T
from ultralytics import YOLO

VAL = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/acne_dataset_v4/val")
R = Path.home() / "PrecisionSkinV1/runs/lesion_acne_boxed_only/models"
paths = [R / ("seed_%d/weights/best.pt" % s) for s in (42, 7, 123)]
P = [T.raw_preds(YOLO(str(p)), VAL, False) for p in paths]
for s, p in zip((42, 7, 123), P):
    ap, (f, c, pp, rr) = T.evaluate(p, VAL); print("single seed %3d: val mAP@0.5 %.3f | best F1 %.3f at %.3f (P %.3f R %.3f)" % (s, ap, f, c, pp, rr))
fused = {k: T.fuse([p[k] for p in P]) for k in P[0]}; ap, (f, c, pp, rr) = T.evaluate(fused, VAL)
print("3-seed average : val mAP@0.5 %.3f | best F1 %.3f at %.3f (P %.3f R %.3f)" % (ap, f, c, pp, rr))
