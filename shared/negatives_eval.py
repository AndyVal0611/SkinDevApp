#!/usr/bin/env python
"""negatives_eval.py - how often do the detectors put boxes on clear skin? (Negatives_100: the photos you marked X = no lesion of any kind.)
Acne = 3-seed average (threshold 0.275); hyperpigmentation = H25 3-seed average (threshold 0.225). Nothing is trained or changed."""
import json, sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

D = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Negatives_100"); S = D / "Elaiza__hyperpigmentation_v2"
ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]
neg = [f.stem for f in (S / "labels").glob("*.txt") if f.stat().st_mtime > ref + 1 and not F.rd(f)]
pos = [f.stem for f in (S / "labels").glob("*.txt") if f.stat().st_mtime > ref + 1 and F.rd(f)]
print("clear-skin photos (X): %d | photos where you drew a box (lesion present, excluded): %d" % (len(neg), len(pos)))
tmp = Path.home() / "PrecisionSkinV1" / "negatives_eval_tmp"; import shutil; shutil.rmtree(tmp, ignore_errors=True); (tmp / "images").mkdir(parents=True); (tmp / "labels").mkdir()
for n in neg:
    img = next(iter((S / "images").glob(n + ".*"))); shutil.copy2(img, tmp / "images" / img.name); (tmp / "labels" / (n + ".txt")).write_text("")
R = F.E / "runs"; A = Path.home() / "PrecisionSkinV1/runs/lesion_acne_boxed_only/models"
SETS = {"acne (3-seed average)": ([A / ("seed_%d/weights/best.pt" % s) for s in (123, 42, 7)], (0.20, 0.25, 0.275, 0.30, 0.35, 0.40)),
        "hyperpigmentation (H25 average)": ([R / "H25_v2human_all_pub/weights/best.pt", R / "H25_s7/weights/best.pt", R / "H25_s123/weights/best.pt"], (0.15, 0.20, 0.225, 0.25, 0.30, 0.35))}
for name, (paths, thrs) in SETS.items():
    P = [T.raw_preds(YOLO(str(p)), tmp, False) for p in paths]; fused = {k: T.fuse([p[k] for p in P]) for k in P[0]}
    print("\n%s on %d clear-skin photos" % (name, len(fused))); print("  threshold | photos with at least one box (false alarm) | specificity | boxes per photo")
    for t in thrs:
        n = np.array([sum(1 for b, c in v if c >= t) for v in fused.values()])
        print("  %.3f     | %3d of %d = %4.0f %%                          | %4.0f %%       | %.2f" % (t, (n > 0).sum(), len(n), 100 * (n > 0).mean(), 100 * (n == 0).mean(), n.mean()))
