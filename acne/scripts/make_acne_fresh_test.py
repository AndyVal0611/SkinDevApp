#!/usr/bin/env python
"""
make_acne_fresh_test.py - the FRESH acne test set (Phase 3): 70 acne-pool photos + 30 clear-skin photos, shuffled, renamed, BLANK (no model drafts), for you to box from scratch.
Photos that are near-duplicates (correlation >= 0.90, 8 rotations/flips) of ANY photo already used anywhere (acne dataset train/val/test, independent set, the new / reviewed / more-faces
folders, Negatives_100, the re-box set) are excluded, so the set is untouched. Press S with boxes on acne, X when the photo has no acne (it then counts as a clear-skin negative).
Run in WSL, with no training running:  python make_acne_fresh_test.py
"""
import json, os, random, shutil, sys, time
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyperpig_autolabel as H
from PIL import Image

B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin"); R = B / "Acne_Boxing"; DS = R / "acne_dataset_v4"
TOOL = B / "Hyperpigmentation_Boxing/Independent_Final_Set/lesion_box_tool.py"; D = R / "Acne_Fresh_Test"
if D.exists(): raise SystemExit("exists already, not overwriting: %s" % D)
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
refs = []
for s in ("train", "val", "test"): refs += list((DS / s / "images").glob("*"))
for sub in ("Independent_Final_Set/boxed_independent/images", "Acne_New_Photos/Elaiza__acne_review/images", "Acne_Review_Missing/Elaiza__acne_review/images", "More_Faces/Elaiza__acne_more/images",
            "Reliability_Rebox_Acne/Elaiza__acne_rebox/images", "Fix_Acne_Missed_Spots/Elaiza__acne_fix/images"):
    refs += list((R / sub).glob("*"))
refs += list((B / "Hyperpigmentation_Boxing/Negatives_100/Elaiza__hyperpigmentation_v2/images").glob("*"))
T = [t for t in (H.thumb(p) for p in refs if p.suffix.lower() in EXT) for t in [t] if t is not None]; A = np.stack(T); print("reference photos checked against:", len(T), flush=True)
rng = random.Random(77)

def pick(pool_dir, n, min_side):
    pool = [p for p in pool_dir.rglob("*") if p.suffix.lower() in EXT]; rng.shuffle(pool); out = []; extra = []
    for p in pool:
        if len(out) >= n: break
        try:
            if min(Image.open(p).size) < min_side: continue
        except Exception: continue
        t = H.thumb(p)
        if t is None or H.best_corr(np.stack([t]), A)[0].max() >= 0.90: continue
        if extra and H.best_corr(np.stack([t]), np.stack(extra))[0].max() >= 0.90: continue
        out.append(p); extra.append(t)
    return out

pos = pick(B / "Classes" / "Acne", 70, 400); neg = pick(B / "Classes" / "Normal", 30, 300); items = [(p, "pool_acne") for p in pos] + [(p, "pool_normal") for p in neg]; rng.shuffle(items)
S = D / "Elaiza__acne_review"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir(); mp = {}; src = {}
for i, (p, kind) in enumerate(items):
    new = "acne_fresh_%03d" % i; shutil.copy2(p, S / "images" / (new + p.suffix.lower())); (S / "labels" / (new + ".txt")).write_text(""); mp[str(p)] = new; src[new] = kind
json.dump(mp, open(D / "name_map.json", "w")); json.dump(src, open(D / "SECRET_source_of_each_photo.json", "w")); (S / "classes.txt").write_text("acne\n"); time.sleep(1.5)
ref = time.time() + 2; json.dump({"drafted_at": ref, "note": "fresh acne test set: box from scratch, no helper"}, open(D / "_drafts" / "meta.json", "w"))
for f in (S / "labels").glob("*.txt"): os.utime(f, (ref - 5, ref - 5))
shutil.copy2(TOOL, D / "lesion_box_tool.py")
(D / "START_ACNE_FRESH_TEST.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho FRESH ACNE TEST SET: box every visible acne spot from scratch, one box per spot. NO helper, never look at model boxes. S = save. X = no acne in the photo.\r\n'
    'py -3.13 lesion_box_tool.py "Elaiza__acne_review" --label acne --annotator Elaiza --port 8851 --export "boxed_acne_fresh_test"\r\npause\r\n')
print("fresh test set: %d photos (%d from the acne pool, %d clear-skin) -> %s" % (len(items), len(pos), len(neg), D / "START_ACNE_FRESH_TEST.bat"))
