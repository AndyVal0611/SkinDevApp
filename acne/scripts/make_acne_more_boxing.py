#!/usr/bin/env python
"""
make_acne_more_boxing.py - two review folders for acne (copies only; nothing existing is changed):
  Acne_Review_Missing  the 111 TRAINING photos where the final model saw confident spots (conf >= 0.5) that have no box of yours near them.
                       Your boxes are preloaded; the model's extra spots are added as extra boxes. Delete the ones that are not acne, keep real spots, press S.
  Acne_New_Photos      up to 250 NEW acne photos (not near-duplicates of any photo already in the acne dataset, validation, test or independent set),
                       drafted by the final model. Delete wrong boxes, add missed spots, press S; X = no acne on a face.
Run in WSL:  python make_acne_more_boxing.py
"""
import csv, json, os, random, shutil, sys, time
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyperpig_autolabel as H
import hyp_final_eval as F
from ultralytics import YOLO

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing"); DS = R / "acne_dataset_v4"; AUD = R / "_annotation_audit"
TOOL = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Independent_Final_Set/lesion_box_tool.py")
INDEP = R / "Independent_Final_Set" / "boxed_independent"
W = Path.home() / "PrecisionSkinV1/runs/lesion_acne_boxed_only/models/seed_123/weights/best.pt"
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def folder(name, port, items, note):
    D = R / name
    if D.exists(): print("exists, skipped:", D); return
    S = D / "Elaiza__acne_review"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir(); mp = {}
    for i, (src, boxes) in enumerate(items):
        new = "%s_%04d" % (name.lower(), i); shutil.copy2(src, S / "images" / (new + Path(src).suffix.lower())); mp[str(src)] = new
        (S / "labels" / (new + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes))
    json.dump(mp, open(D / "name_map.json", "w")); (S / "classes.txt").write_text("acne\n"); time.sleep(1.5)
    ref = time.time() + 2; json.dump({"drafted_at": ref, "note": note}, open(D / "_drafts" / "meta.json", "w"))
    for f in (S / "labels").glob("*.txt"): os.utime(f, (ref - 5, ref - 5))
    shutil.copy2(TOOL, D / "lesion_box_tool.py")
    (D / ("START_" + name.upper() + ".bat")).write_text('@echo off\r\ncd /d "%~dp0"\r\necho ' + note + '\r\n'
        'py -3.13 lesion_box_tool.py "Elaiza__acne_review" --label acne --annotator Elaiza --port %d --export "boxed_%s"\r\npause\r\n' % (port, name.lower()))
    print(name, len(items), "photos ->", D)


m = YOLO(str(W))
def predict(p, conf, max_det=80):
    r = m.predict(str(p), imgsz=800, conf=conf, iou=0.6, max_det=max_det, verbose=False, device=0)[0]
    return [tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else []

# ---- 1. review of possibly missing spots (training photos only)
q = [r for r in csv.DictReader(open(AUD / "review_queue.csv", encoding="utf-8")) if r["split"] == "train" and r["issue"] == "POSSIBLE_MISSING"]
items = []
for r in q:
    img = DS / "train/images" / r["image"]; gt = F.rd(DS / "train/labels" / (Path(r["image"]).stem + ".txt")); pr = predict(img, 0.5)
    extra = [b for b in pr if not gt or max(F.iou(b, g) for g in gt) < 0.10]
    items.append((img, gt + extra))
folder("Acne_Review_Missing", 8841, items, "ACNE REVIEW: your boxes plus extra spots the model is confident about. Delete extras that are NOT acne, keep real ones, press S.")

# ---- 2. new photos, not near-duplicates of anything already used
ref_imgs = [p for s in ("train", "val", "test") for p in (DS / s / "images").glob("*")] + [p for p in (INDEP / "images").glob("*")]
T = [t for t in (H.thumb(p) for p in ref_imgs) if t is not None]; A = np.stack(T)
pool = [p for p in (Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Acne")).rglob("*") if p.suffix.lower() in EXT]
random.Random(31).shuffle(pool); new = []; looked = 0
from PIL import Image
for p in pool:
    if len(new) >= 250 or looked > 2500: break
    looked += 1
    try:
        if min(Image.open(p).size) < 400: continue
    except Exception: continue
    t = H.thumb(p)
    if t is None or H.best_corr(np.stack([t]), A)[0].max() >= 0.90: continue
    bx = predict(p, 0.25)
    if 3 <= len(bx) <= 40: new.append((p, bx))
folder("Acne_New_Photos", 8842, new, "ACNE NEW PHOTOS: boxes drawn by the model. Delete wrong ones, add missed spots, press S. X = no acne on a face (or not a face).")
print("looked at %d candidate photos" % looked)
