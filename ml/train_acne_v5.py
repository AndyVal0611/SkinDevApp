#!/usr/bin/env python
"""
train_acne_v5.py - the acne detector WITH healthy-skin negatives (background images), 3 seeds, everything else identical to the v4 run.

Why: the v4 model draws about 1.2x too many boxes (precision 0.43). Facial photos with NO acne, given with empty labels, teach it that pores, shadows and
healthy texture are background. Ultralytics recommends roughly 0-10 % background images.

  * negatives come from Classes/Normal (healthy facial skin): readable, >= 400 px, no rotated-crop black corners, no smeared borders; any photo on which the
    v4 model fires with confidence >= 0.50 is dropped (it may really show acne); about 10 % of the training set; training only.
  * validation and test are EXACTLY the v4 photos, so the numbers compare directly with the v4 result.
  * same settings as the v4 notebook: yolov8s, 800 px, 100 epochs, patience 30, batch 8, seeds 42 / 7 / 123.

Run in WSL (venv with ultralytics):   python train_acne_v5.py            (resumable: finished seeds are skipped)
"""
import os, random, shutil, sys, yaml
from pathlib import Path
import cv2, numpy as np

HOME = Path.home() / "PrecisionSkinV1"
V4 = HOME / "acne_detector_v4"; OUT = HOME / "acne_detector_v5"; LOCAL = OUT / "dataset"
NORMAL = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Normal")
SEEDS = [42, 7, 123]; N_NEG = 115
IMG_EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def junk(path, min_side=400):
    im = cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)
    if im is None: return "unreadable"
    h, w = im.shape[:2]
    if min(h, w) < min_side: return "small"
    g = cv2.cvtColor(cv2.resize(im, (128, 128), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2GRAY).astype(np.float32)
    if (g <= 8).mean() > 0.02: return "black corners"
    for strip, ax in ((g[:, :12], 1), (g[:, -12:], 1), (g[:12, :], 0), (g[-12:, :], 0)):
        if np.abs(np.diff(strip, axis=ax)).mean() < 0.6: return "smeared border"
    return None


def build_dataset():
    if (LOCAL / "NEGATIVES_DONE").exists():
        print("dataset v5 already built"); return
    shutil.rmtree(LOCAL, ignore_errors=True); OUT.mkdir(parents=True, exist_ok=True)
    shutil.copytree(V4 / "dataset", LOCAL)                                      # train / val / test exactly as v4
    from ultralytics import YOLO
    probe = YOLO(str(V4 / "runs/acne_f100_s123/weights/best.pt"))
    files = sorted(p for p in NORMAL.rglob("*") if p.suffix.lower() in IMG_EXT); random.Random(5).shuffle(files)
    print("Normal photos available:", len(files), flush=True)
    kept, why = [], {}
    for p in files:
        r = junk(p)
        if r: why[r] = why.get(r, 0) + 1; continue
        det = probe.predict(str(p), imgsz=800, conf=0.50, verbose=False, device=0)[0]
        if det.boxes is not None and len(det.boxes): why["acne-like (conf>=0.5)"] = why.get("acne-like (conf>=0.5)", 0) + 1; continue
        kept.append(p)
        if len(kept) >= N_NEG: break
    print("negatives kept %d | rejected: %s" % (len(kept), why), flush=True)
    for i, p in enumerate(kept):
        nm = "neg_%03d%s" % (i, p.suffix.lower()); shutil.copy2(p, LOCAL / "train/images" / nm); (LOCAL / "train/labels" / ("neg_%03d.txt" % i)).write_text("")
    (LOCAL / "NEGATIVES_DONE").write_text(str(len(kept)))


def main():
    build_dataset()
    from ultralytics import YOLO
    train = sorted((LOCAL / "train/images").glob("*"))
    print("train photos (incl. negatives):", len(train), "| val", len(list((LOCAL / "val/images").glob("*"))), "| test", len(list((LOCAL / "test/images").glob("*"))), flush=True)
    for seed in SEEDS:
        name = "acne_f100_s%d" % seed; run = OUT / "runs" / name; done = run / "DONE"
        if done.exists(): print(name, "already done"); continue
        lst = OUT / ("train_%s.txt" % name); lst.write_text("\n".join(str(p) for p in train) + "\n")
        yml = OUT / ("data_%s.yaml" % name)
        yaml.safe_dump({"path": str(LOCAL), "train": str(lst), "val": "val/images", "test": "test/images", "names": {0: "acne"}}, open(yml, "w"))
        last = run / "weights" / "last.pt"
        if last.exists():
            YOLO(str(last)).train(resume=True)
        else:
            YOLO("yolov8s.pt").train(data=str(yml), imgsz=800, epochs=100, patience=30, batch=8, device=0, workers=2, seed=seed, cos_lr=True, close_mosaic=10,
                                     project=str(OUT / "runs"), name=name, exist_ok=True, fliplr=0.5, flipud=0.0, degrees=10, scale=0.5, hsv_h=0.015, hsv_s=0.5, hsv_v=0.35)
        assert (run / "weights" / "best.pt").exists(); done.write_text("ok"); print("DONE", name, flush=True)
    print("ALL SEEDS DONE", flush=True)


if __name__ == "__main__":
    main()
