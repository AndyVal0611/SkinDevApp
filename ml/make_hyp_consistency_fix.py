#!/usr/bin/env python
"""
make_hyp_consistency_fix.py - a COPY of your hyperpigmentation photos whose boxes break the region rule, with your boxes preloaded so you only merge or split.
  over-split : 10+ boxes, or 6+ boxes that are mostly tiny (median side < 7 % of the photo)
  over-merged: a box wider than 40 % of the photo side
The 100 hand-boxed TEST photos and the 60 hand-boxed validation photos are NEVER included (their labels stay untouched). Originals are never changed.
"""
import json, os, shutil, time
from pathlib import Path
import numpy as np

SPR = Path("/mnt/c/Users/Bautista/Documents/PrecisionSkin_DetectorOwn/hyperpigmentation_sprint")
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); D = B / "Fix_Hyperpig_Consistency"
P = Path.home() / "PrecisionSkinV1"
if D.exists(): raise SystemExit("exists already, not overwriting: %s" % D)
test = {p.stem for p in (P / "hyperpig_autolabel/hyperpig_only_v1/dataset/hand_test/images").glob("*")}
val = {Path(n).stem.replace("hand_", "", 1) for n in json.load(open(P / "lesion_experiments/data/hyp_split.json"))["hand_val"]} if (P / "lesion_experiments/data/hyp_split.json").exists() else set()
meta = json.load(open(SPR / "_drafts/meta.json"))["drafted_at"]; rows = []
for f in (SPR / "Elaiza__hyperpigmentation/labels").glob("*.txt"):
    if f.stat().st_mtime <= meta + 1 or f.stem in test or f.stem in val: continue
    bx = [tuple(map(float, l.split()[1:5])) for l in f.read_text().splitlines() if len(l.split()) >= 5]
    if not bx: continue
    sides = [(w * h) ** 0.5 for _, _, w, h in bx]
    huge = max(sides) > 0.40; split = len(bx) >= 10 or (len(bx) >= 6 and float(np.median(sides)) < 0.07)
    if huge or split: rows.append((0 if huge else 1, -len(bx), f.stem))
rows.sort(); chosen = [s for _, _, s in rows][:60]
S = D / "Elaiza__hyperpigmentation_fix"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir()
for s in chosen:
    img = next(iter((SPR / "Elaiza__hyperpigmentation/images").glob(s + ".*")))
    shutil.copy2(img, S / "images" / img.name); shutil.copy2(SPR / "Elaiza__hyperpigmentation/labels" / (s + ".txt"), S / "labels" / (s + ".txt"))
(S / "classes.txt").write_text("hyperpigmentation\n"); time.sleep(1.5); ref = time.time()
for s in chosen: os.utime(S / "labels" / (s + ".txt"), (ref - 5, ref - 5))               # "not reviewed" until you save it
json.dump({"drafted_at": ref, "note": "consistency fix: your boxes preloaded; saved = fixed"}, open(D / "_drafts" / "meta.json", "w"))
shutil.copy2("/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_box_tool.py", D / "lesion_box_tool.py")
(D / "START_FIX_CONSISTENCY.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho REGION RULE: one box per visible PATCH or CLUSTER of darker skin. MERGE tiny neighbouring spots into one box. SPLIT a huge box that covers separate patches. Keep real separate patches separate. X = no patch. S = save, N = next.\r\n'
                                             'py -3.13 lesion_box_tool.py "Elaiza__hyperpigmentation_fix" --label hyperpigmentation --annotator Elaiza --port 8822 --export "boxed_Hyperpig_CONSISTENCY"\r\npause\r\n')
print("fix folder: %d photos (%d over-merged, %d over-split) -> %s" % (len(chosen), sum(1 for r in rows[:60] if r[0] == 0), sum(1 for r in rows[:60] if r[0] == 1), D / "START_FIX_CONSISTENCY.bat"))
print("excluded on purpose: %d hand-boxed test photos, %d hand-boxed validation photos" % (len(test), len(val)))
