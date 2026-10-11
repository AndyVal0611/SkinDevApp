#!/usr/bin/env python
"""
acne_gate_rule.py - choose (on VALIDATION photos only) a display rule that stops acne boxes appearing on photos without acne:
"show acne boxes only if the detector finds at least N boxes above the threshold".
  * positives: the 154 acne VALIDATION photos (never the test photos)
  * negatives: healthy / eczema / hyperpigmentation photos from the class folders (labels NOT verified: some may show acne)
  * also finds the validation confidence where precision equals recall (a validation-based justification for the app threshold)
Uses the installed acne model (seed 123). Read-only.
"""
import random, sys
from pathlib import Path
import numpy as np, pandas as pd
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyperpig_autolabel as H
from ultralytics import YOLO

P = Path.home() / "PrecisionSkinV1"; D = P / "acne_detector_v4" / "dataset"; EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
W = P / "acne_detector_v4/runs/acne_f100_s123/weights/best.pt"; m = YOLO(str(W))
man = pd.read_csv(P / "detector_work/dataset_v1/dataset_manifest.csv"); V1 = P / "detector_work/dataset_v1"
def v1f(cond, sp): return [V1 / r.split / "images" / r.file for r in man[(man.condition == cond) & (man.split.isin(sp))].itertuples() if (V1 / r.split / "images" / r.file).exists()]
normal = [p for p in Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Normal").rglob("*") if p.suffix.lower() in EXT]; random.Random(11).shuffle(normal)
sets = {"acne VALIDATION photos (positives)": sorted((D / "val/images").glob("*")), "healthy (200)": normal[:200], "eczema (341)": v1f("Eczema", ["train", "val", "test"]),
        "hyperpigmentation (164)": v1f("Hyperpigmentation", ["test"])}
confs = {}
for name, files in sets.items():
    c = []
    for p in files:
        try: r = m.predict(str(p), imgsz=800, conf=0.20, iou=0.5, verbose=False, device=0)
        except Exception: continue
        c.append(r[0].boxes.conf.cpu().numpy() if r and len(r[0].boxes) else np.zeros(0))
    confs[name] = c
print("rule: acne boxes are shown only if at least N boxes have confidence >= T\n")
rows = []
for T in (0.30, 0.35, 0.40):
    for N in (1, 2, 3, 4, 5):
        row = {"T": T, "N": N}
        for name, c in confs.items(): row[name.split(" (")[0].replace(" VALIDATION photos", "")] = round(100 * float(np.mean([(x >= T).sum() >= N for x in c])), 1)
        rows.append(row)
df = pd.DataFrame(rows); print("% of photos that would show acne boxes:\n", df.to_string(index=False))
sv = H.score_model(W, 800, D / "val")
cs = np.arange(0.10, 0.70, 0.01); pr = [H.at_conf(sv, c) for c in cs]; gap = [abs(a[0] - a[1]) for a in pr]; i = int(np.argmin(gap))
print("\nvalidation: precision = recall at confidence %.2f (P %.3f, R %.3f); best-F1 confidence %.3f" % (cs[i], pr[i][0], pr[i][1], H.best_f1_conf(sv)))
