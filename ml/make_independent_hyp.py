#!/usr/bin/env python
"""
make_independent_hyp.py - an INDEPENDENT hyperpigmentation evaluation set for blind boxing (no drafts, no model run on it).
60 random photos from the distinct, never-annotated pool (unlabeled_index.csv: eligible), excluding every photo in Draft_Hyperpig / Fix_Hyperpig_Boxes, and any photo with a
look-alike (correlation >= 0.90, 8 rotations/flips) in the hyperpigmentation training, validation or test photos. Copies only.
"""
import json, random, shutil, sys, time
from pathlib import Path
import numpy as np, pandas as pd
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyperpig_autolabel as H

P = Path.home() / "PrecisionSkinV1"; W1 = P / "hyperpig_autolabel/hyperpig_only_v1"
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); D = B / "Independent_Final_Set"
if D.exists(): raise SystemExit("exists already, not overwriting: %s" % D)
idx = pd.read_csv(W1 / "reports/unlabeled_index.csv"); idx = idx[(idx.eligible == True) & (idx.status == "ok")]
drafted = set()
for sub in ("Draft_Hyperpig/Elaiza__hyperpigmentation_draft/images", "Fix_Hyperpig_Boxes/Elaiza__hyperpigmentation_fix/images"):
    if (B / sub).exists(): drafted |= {p.name for p in (B / sub).glob("*")}
cand = [p for p in idx.path.tolist() if Path(p).name not in drafted]; random.Random(2026).shuffle(cand); cand = cand[:250]
used = []
for s in ("train", "val", "test", "hand_test"): used += sorted((W1 / "dataset" / s / "images").glob("*"))
A = np.stack([t for t in (H.thumb(p) for p in used) if t is not None])
keep = []
for p in cand:
    t = H.thumb(p)
    if t is None: continue
    if H.best_corr(np.stack([t]), A)[0].max() >= 0.90: continue
    keep.append(Path(p))
    if len(keep) == 60: break
S = D / "Elaiza__hyperpigmentation_independent"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir()
for p in keep:
    shutil.copy2(p, S / "images" / p.name); (S / "labels" / (p.stem + ".txt")).write_text("")
(S / "classes.txt").write_text("hyperpigmentation\n"); time.sleep(1.5)
json.dump({"drafted_at": time.time() + 2, "note": "independent evaluation set: box from scratch, no drafts; X = no patch"}, open(D / "_drafts" / "meta.json", "w"))
shutil.copy2("/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_box_tool.py", D / "lesion_box_tool.py")
(D / "START_BOXING_INDEPENDENT_HYPERPIG.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho INDEPENDENT SET: one box per PATCH of darker skin (your rule), from scratch. X = no patch. NO helper. S = save, N = next.\r\n'
    'py -3.13 lesion_box_tool.py "Elaiza__hyperpigmentation_independent" --label hyperpigmentation --annotator Elaiza --port 8821 --export "boxed_independent_hyperpig"\r\npause\r\n')
print("independent hyperpigmentation set: %d photos" % len(keep), "->", D / "START_BOXING_INDEPENDENT_HYPERPIG.bat")
