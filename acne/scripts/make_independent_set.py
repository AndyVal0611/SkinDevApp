#!/usr/bin/env python
"""
make_independent_set.py - an INDEPENDENT acne evaluation set for blind boxing (no drafts, no model run on these photos).
Photos: never reviewed (not in any manifest), role non_facial in the sprint list, small (250-399 px) real-world photos that the earlier filters left out,
no look-alike (8 rotations/flips, correlation >= 0.90) of any train / validation / test photo. 60 random photos, copied (originals untouched).
    python make_independent_set.py
"""
import json, random, shutil, sys, time
from pathlib import Path
import numpy as np, pandas as pd
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyperpig_autolabel as H

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing"); P = Path.home() / "PrecisionSkinV1"
D = R / "Independent_Final_Set"
if D.exists(): raise SystemExit("exists already, not overwriting: %s" % D)
sp = pd.read_csv(R / "sprint_photos.csv")
reviewed = set(pd.read_csv(R / "acne_dataset_v4" / "manifest_all_reviewed.csv").stem)
sp["stem"] = sp.name.str.rsplit(".", n=1).str[0]
pool = sp[(sp.role == "non_facial") & (~sp.stem.isin(reviewed)) & (sp[["width", "height"]].min(axis=1) >= 250) & (sp[["width", "height"]].min(axis=1) < 400)]
print("candidate pool:", len(pool), flush=True)
names = pool.name.tolist(); random.Random(2026).shuffle(names); names = names[:200]
used = []
for s in ("train", "val", "test"): used += sorted((P / "acne_detector_v4/dataset" / s / "images").glob("*"))
A = np.stack([t for t in (H.thumb(p) for p in used) if t is not None])
keep = []
for n in names:
    t = H.thumb(R / "Elaiza__acne/images" / n)
    if t is None: continue
    if H.best_corr(np.stack([t]), A)[0].max() >= 0.90: continue
    keep.append(n)
    if len(keep) == 60: break
S = D / "Elaiza__acne_independent"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir()
for n in keep:
    shutil.copy2(R / "Elaiza__acne/images" / n, S / "images" / n); (S / "labels" / (Path(n).stem + ".txt")).write_text("")
(S / "classes.txt").write_text("acne\n"); time.sleep(1.5)
json.dump({"drafted_at": time.time() + 2, "note": "independent evaluation set: box from scratch, no drafts; X = no acne"}, open(D / "_drafts" / "meta.json", "w"))
shutil.copy2("/mnt/c/Users/Bautista/source/repos/SkinDevApp/shared/lesion_box_tool.py", D / "lesion_box_tool.py")
(D / "START_BOXING_INDEPENDENT.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho INDEPENDENT SET: box every acne spot from scratch. Press X if there is NO acne on the skin. Do NOT use any helper. S = save, N = next.\r\n'
    'py -3.13 lesion_box_tool.py "Elaiza__acne_independent" --label acne --annotator Elaiza --port 8820 --export "boxed_independent"\r\npause\r\n')
print("independent set: %d photos" % len(keep), "->", D / "START_BOXING_INDEPENDENT.bat")
