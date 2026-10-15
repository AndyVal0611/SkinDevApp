#!/usr/bin/env python
"""
make_hyp_v2_relabel.py - three review folders for relabelling hyperpigmentation under annotation rule v2 (copies only; every original label stays untouched):
  V2_Test   : the 100 hand-boxed TEST photos, blank, shuffled and renamed (blind)          -> the new, reproducible test labels
  V2_Val    : the 30 validation photos you have not boxed under v2 yet, blank, renamed      -> with pass A this makes a 60-photo v2 validation set
  V2_Train  : ~800 training photos (your boxes + reviewed drafts) in random order, preloaded with an AUTOMATIC v2 conversion of the old boxes
              (touching or closer than 1.0 x the smaller box's side = one box; boxes under 0.09 dropped). Correct them under rule v2 and save.
              Only photos you SAVE are used for training; stop whenever you are tired.
"""
import json, random, shutil, time
from pathlib import Path
import sys
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_v2_transform as TR
import hyp_final_eval as F

P = Path.home() / "PrecisionSkinV1"; E = P / "lesion_experiments"; HYP = P / "hyperpig_autolabel/hyperpig_only_v1/dataset"
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing")
TOOL = B / "Independent_Final_Set/lesion_box_tool.py"
RULE = ("One box per CONNECTED dark patch (touching or closer than the smaller one's width = ONE box). Skip marks smaller than 7% of the photo. "
        "Speckles = one box only if the area is darker as a whole. Stay inside one facial region. Edge where the darkening fades to half. Usually 1-5 boxes. "
        "Do not zoom in to find more. X = nothing qualifies.")
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def folder(name, port, items, preload, note):
    D = B / name
    if D.exists(): print("exists, skipped:", D); return
    S = D / "Elaiza__hyperpigmentation_v2"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir()
    mp = {}
    for i, (src, boxes) in enumerate(items):
        new = "%s_%04d" % (name[3:].lower(), i); shutil.copy2(src, S / "images" / (new + src.suffix.lower())); mp[src.name] = new
        (S / "labels" / (new + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes) if preload else "")
    json.dump(mp, open(D / "name_map.json", "w")); (S / "classes.txt").write_text("hyperpigmentation\n"); time.sleep(1.5)
    ref = time.time() + 2; json.dump({"drafted_at": ref, "note": note}, open(D / "_drafts" / "meta.json", "w"))
    for f in (S / "labels").glob("*.txt"): import os; os.utime(f, (ref - 5, ref - 5))
    shutil.copy2(TOOL, D / "lesion_box_tool.py")
    (D / ("START_" + name.upper() + ".bat")).write_text('@echo off\r\ncd /d "%~dp0"\r\necho ' + note + '\r\n'
        'py -3.13 lesion_box_tool.py "Elaiza__hyperpigmentation_v2" --label hyperpigmentation --annotator Elaiza --port %d --rule "%s" --export "boxed_%s"\r\npause\r\n' % (port, RULE, name.lower()))
    print(name, len(items), "photos ->", D)


rng = random.Random(5)
# test: blank, blind
test = sorted(p for p in (F.TEST / "images").glob("*") if p.suffix.lower() in EXT); rng.shuffle(test)
folder("V2_Test", 8826, [(p, []) for p in test], False, "RULE V2 TEST SET (100 photos): box from scratch, do not look at old boxes. S = save. X = nothing qualifies.")
# validation: the 30 not yet boxed under v2
done = {Path(k).stem for k in json.load(open(B / "Rebox_V2_PassA/name_map.json"))}
val = sorted(p for p in (F.VAL / "images").glob("*") if p.suffix.lower() in EXT and p.stem not in done); rng.shuffle(val)
folder("V2_Val", 8827, [(p, []) for p in val], False, "RULE V2 VALIDATION (30 photos): box from scratch. S = save. X = nothing qualifies.")
# train: own train photos + reviewed drafts, auto-converted old boxes as the starting point
split = json.load(open(E / "data/hyp_split.json")); items = []
for n in split["own_train"]:
    img = HYP / "train/images" / n; lab = HYP / "train/labels" / (Path(n).stem + ".txt")
    if img.exists() and F.rd(lab): items.append((img, TR.to_v2(F.rd(lab), 1.0, 0.09)))
dd = E / "data/hyp_drafts_v2"
for img in sorted((dd / "images").glob("*")):
    lab = dd / "labels" / (img.stem + ".txt")
    if F.rd(lab): items.append((img, TR.to_v2(F.rd(lab), 1.0, 0.09)))
rng.shuffle(items)
folder("V2_Train", 8828, items, True, "RULE V2 TRAINING PHOTOS: boxes are an automatic starting point. Fix them under rule v2 and press S. Only saved photos are used.")
