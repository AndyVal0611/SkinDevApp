#!/usr/bin/env python
"""make_acne_reliability.py - 30 acne VALIDATION photos copied WITHOUT their boxes (shuffled, renamed) so they can be boxed again from scratch.
Comparing the new boxes with the original ones shows how much the annotator agrees with themselves on acne (the ceiling for box-level scores).
Photos with more than 25 boxes are skipped to keep the task short. Nothing in the dataset is changed."""
import json, random, shutil, sys, time, os
from pathlib import Path

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing"); DS = R / "acne_dataset_v4" / "val"
TOOL = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Independent_Final_Set/lesion_box_tool.py")
D = R / "Reliability_Rebox_Acne"
if D.exists(): raise SystemExit("exists already, not overwriting: %s" % D)
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
cand = []
for p in sorted((DS / "images").glob("*")):
    if p.suffix.lower() not in EXT: continue
    n = len([l for l in (DS / "labels" / (p.stem + ".txt")).read_text().splitlines() if l.strip()])
    if 3 <= n <= 25: cand.append(p)
random.Random(17).shuffle(cand); pick = cand[:30]
S = D / "Elaiza__acne_rebox"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir(); mp = {}
for i, p in enumerate(pick):
    new = "acne_rebox_%02d" % i; shutil.copy2(p, S / "images" / (new + p.suffix.lower())); (S / "labels" / (new + ".txt")).write_text(""); mp[p.name] = new
json.dump(mp, open(D / "name_map.json", "w")); (S / "classes.txt").write_text("acne\n"); time.sleep(1.5)
ref = time.time() + 2; json.dump({"drafted_at": ref, "note": "blind re-box of 30 acne validation photos"}, open(D / "_drafts" / "meta.json", "w"))
for f in (S / "labels").glob("*.txt"): os.utime(f, (ref - 5, ref - 5))
shutil.copy2(TOOL, D / "lesion_box_tool.py")
(D / "START_ACNE_REBOX_30.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho BLIND RE-BOX: 30 acne photos from scratch, your usual rule (one box per visible spot). Do NOT look at old boxes. X = no acne. S = save.\r\n'
    'py -3.13 lesion_box_tool.py "Elaiza__acne_rebox" --label acne --annotator Elaiza --port 8843 --export "boxed_acne_rebox"\r\npause\r\n')
print("acne re-box set: %d photos (3-25 boxes each) -> %s" % (len(pick), D / "START_ACNE_REBOX_30.bat"))
