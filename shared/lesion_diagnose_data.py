#!/usr/bin/env python
"""
lesion_diagnose_data.py - READ-ONLY data diagnosis of the lesion-detector datasets (nothing is modified, nothing is deleted).

For every dataset / group it measures what could limit a YOLO detector:
  photos, boxes, boxes per photo, box size (sqrt of the area as a share of the photo side), tiny / huge box share, aspect ratio, boxes touching the photo border,
  overlapping boxes (IoU > 0.30), how much of the photo the boxes cover, photos without boxes, photo resolution,
  near-duplicates inside the training set and look-alikes between train/val and the test sets (8 rotations/flips, correlation of 32x32 thumbnails).

    python lesion_diagnose_data.py            writes  <PROJECT>/lesion_experiments/diagnosis/data_diagnosis.json + .md
"""
import json, sys
from pathlib import Path
import numpy as np
import pandas as pd
from PIL import Image

sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp")
import hyperpig_autolabel as H

P = Path.home() / "PrecisionSkinV1"
OUT = P / "lesion_experiments" / "diagnosis"; OUT.mkdir(parents=True, exist_ok=True)
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def imgs(d):
    return sorted(p for p in Path(d).glob("*") if p.suffix.lower() in EXT)


def labels(p, lab_dir, cls=None):
    out = []
    f = Path(lab_dir) / (p.stem + ".txt")
    if f.exists():
        for ln in f.read_text().splitlines():
            t = ln.split()
            if len(t) >= 5 and (cls is None or int(float(t[0])) == cls):
                out.append(tuple(map(float, t[1:5])))
    return out


def iou(a, b):
    def xy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2
    a, b = xy(a), xy(b); iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    return i / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i + 1e-9)


def stats(files, lab_dir, cls=None):
    n_ph = len(files); box_n = []; sides = []; asp = []; border = 0; ov = 0; nb = 0; cover = []; empty = 0; sizes = []
    for p in files:
        bx = labels(p, lab_dir, cls)
        try: sizes.append(Image.open(p).size)
        except Exception: pass
        if not bx: empty += 1
        box_n.append(len(bx)); nb += len(bx)
        m = np.zeros((128, 128), np.uint8)
        for (cx, cy, w, h) in bx:
            sides.append((w * h) ** 0.5); asp.append(w / h if h else 1.0)
            x0, x1, y0, y1 = cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2
            border += (x0 <= 0.005 or y0 <= 0.005 or x1 >= 0.995 or y1 >= 0.995)
            m[int(max(0, y0) * 128):int(min(1, y1) * 128) + 1, int(max(0, x0) * 128):int(min(1, x1) * 128) + 1] = 1
        ov += sum(1 for i in range(len(bx)) for j in range(i + 1, len(bx)) if iou(bx[i], bx[j]) > 0.30)
        cover.append(float(m.mean()))
    s = np.array(sides) if sides else np.zeros(1)
    wh = np.array(sizes) if sizes else np.zeros((1, 2))
    return dict(photos=n_ph, boxes=nb, boxes_per_photo_mean=round(float(np.mean(box_n)) if box_n else 0, 1), boxes_per_photo_median=int(np.median(box_n)) if box_n else 0,
                boxes_per_photo_p90=int(np.percentile(box_n, 90)) if box_n else 0, photos_without_boxes=empty,
                side_p10=round(float(np.percentile(s, 10)), 3), side_median=round(float(np.median(s)), 3), side_p90=round(float(np.percentile(s, 90)), 3),
                pct_tiny_lt2pct=round(100 * float((s < 0.02).mean()), 1), pct_huge_gt30pct=round(100 * float((s > 0.30).mean()), 1),
                aspect_median=round(float(np.median(asp)) if asp else 0, 2), pct_touch_border=round(100 * border / max(1, nb), 1),
                pct_overlapping_boxes=round(100 * ov / max(1, nb), 1), mean_coverage_pct=round(100 * float(np.mean(cover)) if cover else 0, 1),
                photo_w_median=int(np.median(wh[:, 0])), photo_h_median=int(np.median(wh[:, 1])), pct_photos_lt400px=round(100 * float((wh.min(axis=1) < 400).mean()), 1))


def thumbs(files):
    out = []
    for p in files:
        t = H.thumb(p)
        if t is not None: out.append((p.name, t))
    return out


def dup_report(train_files, other):
    """other: dict name -> files. returns near-duplicate counts inside train and look-alikes between each other split and train."""
    tr = thumbs(train_files); A = np.stack([t for _, t in tr])
    res = {}
    # within train: group pairs with correlation >= 0.95 (upper triangle, best over 8 transforms)
    c = H.best_corr(A, A); np.fill_diagonal(c, -1)
    iu = np.triu_indices(len(tr), 1); res["train_near_duplicate_pairs_ge_0.95"] = int((c[iu] >= 0.95).sum())
    in_dup = set(np.where((c >= 0.95).any(axis=1))[0].tolist()); res["train_photos_with_a_near_duplicate"] = len(in_dup)
    for name, files in other.items():
        T = thumbs(files)
        if not T or not tr: continue
        b = H.best_corr(np.stack([t for _, t in T]), A)
        res["%s_photos_with_lookalike_in_train_ge_0.90" % name] = int((b.max(axis=1) >= 0.90).sum())
    return res


def main():
    report = {}
    # ---------------------------------------------------------------- acne (clean v4 set, with the source of every photo)
    A = P / "acne_detector_v4" / "dataset"
    man = pd.read_csv("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/acne_dataset_v4/manifest.csv")
    src = dict(zip(man.name, man.source))
    rep = {}
    for sp in ("train", "val", "test"):
        files = imgs(A / sp / "images"); rep[sp] = stats(files, A / sp / "labels", 0)
    for g in sorted(set(src.values())):
        files = [p for p in imgs(A / "train" / "images") if src.get(p.name) == g]
        if files: rep["train: source=%s" % g] = stats(files, A / "train" / "labels", 0)
    rep["leakage"] = dup_report(imgs(A / "train" / "images"), {"val": imgs(A / "val" / "images"), "test": imgs(A / "test" / "images")})
    report["acne (dataset v4)"] = rep; print("acne done", flush=True)

    # ---------------------------------------------------------------- hyperpigmentation (public boxes vs your boxes)
    Hd = P / "hyperpig_autolabel" / "hyperpig_only_v1" / "dataset"
    rep = {}
    tr = imgs(Hd / "train" / "images")
    own = [p for p in tr if p.name.startswith("hand_")]; pub = [p for p in tr if not p.name.startswith("hand_")]
    rep["train: public boxes"] = stats(pub, Hd / "train" / "labels", 0); rep["train: your boxes (repeated x2 in training)"] = stats(own, Hd / "train" / "labels", 0)
    for sp in ("val", "test", "hand_test"):
        rep[sp] = stats(imgs(Hd / sp / "images"), Hd / sp / "labels", 0)
    rep["leakage"] = dup_report(tr, {"val": imgs(Hd / "val" / "images"), "test": imgs(Hd / "test" / "images"), "hand_test": imgs(Hd / "hand_test" / "images")})
    report["hyperpigmentation (hyperpig_only_v1)"] = rep; print("hyperpigmentation done", flush=True)

    # ---------------------------------------------------------------- the 3-class dataset behind v1 (acne / hyperpigmentation / eczema)
    V = P / "detector_work" / "dataset_v1"; m = pd.read_csv(V / "dataset_manifest.csv"); rep = {}
    for cond, cls in (("Acne", 0), ("Hyperpigmentation", 1), ("Eczema", 2)):
        for sp in ("train", "val", "test"):
            files = [V / sp / "images" / f for f in m[(m.condition == cond) & (m.split == sp)].file if (V / sp / "images" / f).exists()]
            if files: rep["%s %s" % (cond, sp)] = stats(files, V / sp / "labels", cls)
    report["3-class dataset v1 (what the app detector learned from)"] = rep; print("v1 dataset done", flush=True)

    (OUT / "data_diagnosis.json").write_text(json.dumps(report, indent=2))
    lines = ["# Data diagnosis (read-only)", ""]
    for name, rep in report.items():
        lines += ["## " + name, ""]
        groups = {k: v for k, v in rep.items() if k != "leakage"}
        df = pd.DataFrame(groups).T
        lines += [df.to_string(), ""]
        if "leakage" in rep: lines += ["leakage / duplicates: " + json.dumps(rep["leakage"]), ""]
    (OUT / "data_diagnosis.md").write_text("\n".join(lines))
    print("\n".join(lines))


if __name__ == "__main__":
    main()
