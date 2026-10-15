#!/usr/bin/env python
"""
make_more_boxing.py - review folders for the next round of boxing (copies only; nothing existing is changed):
  V2_Public      300 de-duplicated public photos, preloaded with their public boxes converted to rule v2 by code -> correct under rule v2
  V2_New         200 NEW photos nobody has boxed, drafted by the H25 model (rule-v2 style)                        -> correct under rule v2
  V2_Independent the 60 independent photos (blank), to be boxed from scratch under rule v2                       -> a final independent hyperpigmentation score
  Negatives_100  100 healthy-skin photos (blank); press X when the photo has no lesion at all                     -> specificity for BOTH detectors
Run in WSL:  python make_more_boxing.py
"""
import json, os, random, shutil, sys, time
from pathlib import Path
import numpy as np, pandas as pd
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyp_final_eval as F
import hyp_v2_transform as TR
import hyperpig_predraft as HP
from ultralytics import YOLO

P = Path.home() / "PrecisionSkinV1"; E = P / "lesion_experiments"; HYP = P / "hyperpig_autolabel/hyperpig_only_v1/dataset"
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); TOOL = B / "Independent_Final_Set/lesion_box_tool.py"
RULE = ("One box per CONNECTED dark patch (touching or closer than the smaller one's width = ONE box). Skip marks smaller than 7% of the photo. "
        "Speckles = one box only if the area is darker as a whole. Stay inside one facial region. Edge where the darkening fades to half. Usually 1-5 boxes. "
        "Do not zoom in to find more. X = nothing qualifies.")
NEG_RULE = "HEALTHY-SKIN CHECK: if you see NO acne, NO dark patch and NO other lesion, press X. If you do see a lesion, draw nothing and press N (skip)."
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def folder(name, port, items, preload, note, rule=RULE, label="hyperpigmentation"):
    D = B / name
    if D.exists(): print("exists, skipped:", D); return
    S = D / "Elaiza__hyperpigmentation_v2"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir(); mp = {}
    for i, (src, boxes) in enumerate(items):
        new = "%s_%04d" % (name[3:].lower() if name.startswith("V2_") else name.lower(), i); shutil.copy2(src, S / "images" / (new + src.suffix.lower())); mp[str(src)] = new
        (S / "labels" / (new + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes) if preload else "")
    json.dump(mp, open(D / "name_map.json", "w")); (S / "classes.txt").write_text(label + "\n"); time.sleep(1.5)
    ref = time.time() + 2; json.dump({"drafted_at": ref, "note": note}, open(D / "_drafts" / "meta.json", "w"))
    for f in (S / "labels").glob("*.txt"): os.utime(f, (ref - 5, ref - 5))
    shutil.copy2(TOOL, D / "lesion_box_tool.py")
    (D / ("START_" + name.upper() + ".bat")).write_text('@echo off\r\ncd /d "%~dp0"\r\necho ' + note + '\r\n'
        'py -3.13 lesion_box_tool.py "Elaiza__hyperpigmentation_v2" --label ' + label + ' --annotator Elaiza --port %d --rule "%s" --export "boxed_%s"\r\npause\r\n' % (port, rule, name.lower()))
    print(name, len(items), "photos ->", D)


rng = random.Random(21)
# ---- V2_Public: public photos with converted boxes
split = json.load(open(E / "data/hyp_split.json")); pub = list(split["public_dedup"]); rng.shuffle(pub); items = []
for n in pub:
    img = HYP / "train/images" / n; lab = HYP / "train/labels" / (Path(n).stem + ".txt"); bx = F.rd(lab)
    if img.exists() and bx: items.append((img, TR.to_v2(bx, 1.0, 0.09)))
    if len(items) >= 300: break
folder("V2_Public", 8831, items, True, "RULE V2 on PUBLIC photos: boxes are an automatic start. Fix them under rule v2 and press S. Only saved photos are used.")

# ---- V2_New: new photos drafted by the H25 model
idx = pd.read_csv(HYP.parent / "reports" / "unlabeled_index.csv"); idx = idx[(idx.eligible == True) & (idx.status == "ok") & (idx[["width", "height"]].min(axis=1) >= 500)]
used = set()
for sub in ("Draft_Hyperpig/Elaiza__hyperpigmentation_draft/images", "Fix_Hyperpig_Boxes/Elaiza__hyperpigmentation_fix/images", "Independent_Final_Set/Elaiza__hyperpigmentation_independent/images"):
    if (B / sub).exists(): used |= {p.name for p in (B / sub).glob("*")}
pool = [p for p in idx.path.tolist() if Path(p).name not in used]; rng.shuffle(pool)
m = YOLO(str(E / "runs/H25_v2human_all_pub/weights/best.pt")); new = []
for p in pool:
    r = m.predict(p, imgsz=800, conf=0.20, iou=0.6, max_det=30, verbose=False, device=0)[0]
    bx = [tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else []
    if 1 <= len(bx) <= 8: new.append((Path(p), bx))
    if len(new) >= 200: break
folder("V2_New", 8832, new, True, "RULE V2 on NEW photos: boxes drawn by the H25 model. Delete wrong ones, add missed patches under rule v2, press S. X = nothing qualifies.")

# ---- V2_Independent: the 60 independent photos, blank
ind = sorted((B / "Independent_Final_Set/Elaiza__hyperpigmentation_independent/images").glob("*"))
folder("V2_Independent", 8833, [(p, []) for p in ind if p.suffix.lower() in EXT], False, "RULE V2 INDEPENDENT SET: box from scratch, no helper, never look at model boxes. S = save, X = nothing qualifies.")

# ---- Negatives: healthy-skin photos
NORM = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Normal"); fs = [p for p in NORM.rglob("*") if p.suffix.lower() in EXT]; rng.shuffle(fs); neg = []
from PIL import Image
for p in fs:
    try:
        if min(Image.open(p).size) >= 300: neg.append((p, []))
    except Exception: continue
    if len(neg) >= 100: break
folder("Negatives_100", 8834, neg, False, NEG_RULE, rule=NEG_RULE, label="none")
