#!/usr/bin/env python
"""acne_fresh_negatives_by_source.py - AFTER the fresh test has been scored: which kind of negative photo gets false boxes?
The 64 photos you marked X came from two pools (the secret file says which): photos from the ACNE pool where you saw no acne (or no face), and the CLEAR-SKIN pool. Descriptive only; the scored result is not changed."""
import json, sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts")]
import build_report_notebooks as B
ns = {}; exec(B.TOOLBOX, ns); rd, raw_preds, fuse = ns["rd"], ns["raw_preds"], ns["fuse"]
HOME = Path.home() / "PrecisionSkinV1"; D = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/Acne_Fresh_Test"); FRESH = HOME / "lesion_experiments/data/acne_fresh_test"
src = json.load(open(D / "SECRET_source_of_each_photo.json")); RUNS = HOME / "lesion_experiments" / "runs"
M = {"960 px candidate": ([RUNS / ("acne_final_960_lowlr_seed%d" % s) / "weights/best.pt" for s in (42, 7, 123)], 960, 0.225),
     "installed baseline": ([HOME / "runs/lesion_acne_boxed_only/models" / ("seed_%d" % s) / "weights/best.pt" for s in (123, 42, 7)], 800, 0.275)}
neg = [p.name for p in sorted((FRESH / "images").glob("*")) if not rd(FRESH / "labels" / (p.stem + ".txt"))]
pos = [p.name for p in sorted((FRESH / "images").glob("*")) if rd(FRESH / "labels" / (p.stem + ".txt"))]
print("photos: %d with acne boxes | %d without (X)" % (len(pos), len(neg)))
print("where the photos came from:  positives: %s | negatives: %s" % ({k: sum(1 for n in pos if src.get(Path(n).stem) == k) for k in ("pool_acne", "pool_normal")}, {k: sum(1 for n in neg if src.get(Path(n).stem) == k) for k in ("pool_acne", "pool_normal")}))
for name, (paths, imgsz, thr) in M.items():
    P = [raw_preds(w, FRESH, imgsz) for w in paths]; F = {k: fuse([p[k] for p in P]) for k in P[0]}
    for kind in ("pool_acne", "pool_normal"):
        ks = [n for n in neg if src.get(Path(n).stem) == kind]
        if not ks: continue
        cnt = np.array([sum(1 for b, c in F[k] if c >= thr) for k in ks])
        print("%-20s negatives from %-12s: %2d photos | %2d get a false box (%3.0f %%) | %.2f false boxes per photo" % (name, kind, len(ks), (cnt > 0).sum(), 100 * (cnt > 0).mean(), cnt.mean()))
