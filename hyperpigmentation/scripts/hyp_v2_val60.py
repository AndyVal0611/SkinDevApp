#!/usr/bin/env python
"""hyp_v2_val60.py - validation set of 60 photos under rule v2 (pass A + the 30 photos of V2_Val); picks the confidence threshold of the 3-model average on VALIDATION only.
The test photos are not read."""
import json, shutil, sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); R = F.E / "runs"; V60 = F.E / "data" / "hyp_v2_val60"
shutil.rmtree(V60, ignore_errors=True); (V60 / "images").mkdir(parents=True); (V60 / "labels").mkdir(parents=True)
for f in (F.E / "data/hyp_v2_valA/images").glob("*"): shutil.copy2(f, V60 / "images" / f.name)
for f in (F.E / "data/hyp_v2_valA/labels").glob("*"): shutil.copy2(f, V60 / "labels" / f.name)
D = B / "V2_Val"; mp = json.load(open(D / "name_map.json"))
for orig, new in mp.items():
    img = next(iter((D / "Elaiza__hyperpigmentation_v2/images").glob(new + ".*"))); shutil.copy2(img, V60 / "images" / (Path(orig).stem + img.suffix.lower()))
    shutil.copy2(D / "Elaiza__hyperpigmentation_v2/labels" / (new + ".txt"), V60 / "labels" / (Path(orig).stem + ".txt"))
paths = [R / "H20_v2auto/weights/best.pt", R / "H20_s7/weights/best.pt", R / "H20_s123/weights/best.pt"]
P = [T.raw_preds(YOLO(str(p)), V60, False) for p in paths]; fused = {k: T.fuse([p[k] for p in P]) for k in P[0]}
ap, best = T.evaluate(fused, V60)
print("validation: %d photos | 3-model average mAP@0.5 %.3f | best F1 %.3f at threshold %.3f (precision %.3f recall %.3f)" % (len(fused), ap, best[0], best[1], best[2], best[3]))
for c in (0.10, 0.125, 0.15, 0.175, 0.20, 0.225, 0.25, 0.30):
    p, r, f = F.prf(F.counts({k: (F.rd(V60 / "labels" / (Path(k).stem + ".txt")), v) for k, v in fused.items()}, c))
    print("  threshold %.3f: precision %.3f recall %.3f F1 %.3f" % (c, p, r, f))
