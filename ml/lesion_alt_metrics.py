#!/usr/bin/env python
"""
lesion_alt_metrics.py - metrics that match how the app is USED (a screening aid), for the acne detector. mAP at IoU 0.5-0.95 punishes a 5-pixel edge shift on a
30-pixel spot; these ask the questions a user cares about. Test photos only for the positives; nothing here changes any model or label.

  1. photo level: does the detector flag a photo with acne, and stay quiet on healthy-skin photos?  (AUC, sensitivity, specificity at the app threshold)
  2. count level: does the number of boxes follow the number you drew?  (Spearman correlation, mean absolute error, bias)
  3. zone level: on a 3x3 grid of the photo, does it light the same zones you boxed?  (precision / recall / F1 per zone)
Healthy photos come from Classes/Normal (never used to train the acne models); they were not checked one by one, so a few may contain faint acne (that makes specificity look worse, not better).
"""
import random, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyperpig_autolabel as H
from ultralytics import YOLO

P = Path.home() / "PrecisionSkinV1"; TEST = P / "acne_detector_v4/dataset/test"; NORMAL = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Normal")
SEEDS = [42, 7, 123]; THR = 0.35; N_NEG = 200
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def labels(p): return [tuple(map(float, l.split()[1:5])) for l in (TEST / "labels" / (p.stem + ".txt")).read_text().splitlines() if len(l.split()) >= 5]


def zones(boxes):
    z = np.zeros((3, 3), bool)
    for cx, cy, w, h in boxes: z[min(2, int(cy * 3)), min(2, int(cx * 3))] = True
    return z


def auc(pos, neg):
    s = np.concatenate([pos, neg]); y = np.concatenate([np.ones(len(pos)), np.zeros(len(neg))]); o = np.argsort(s); r = np.empty(len(s)); r[o] = np.arange(1, len(s) + 1)
    for v in np.unique(s):                                           # average ranks for ties
        m = s == v; r[m] = r[m].mean()
    return (r[y == 1].sum() - len(pos) * (len(pos) + 1) / 2) / (len(pos) * len(neg))


def main():
    pos = sorted(p for p in (TEST / "images").glob("*") if p.suffix.lower() in EXT)
    cand = sorted(p for p in NORMAL.rglob("*") if p.suffix.lower() in EXT); random.Random(11).shuffle(cand)
    neg = []
    import cv2
    for p in cand:
        im = cv2.imdecode(np.fromfile(str(p), np.uint8), cv2.IMREAD_COLOR)
        if im is not None and min(im.shape[:2]) >= 400: neg.append(p)
        if len(neg) >= N_NEG: break
    print("positives (test acne photos): %d | healthy photos: %d" % (len(pos), len(neg)), flush=True)
    res = []
    for s in SEEDS:
        m = YOLO(str(P / ("acne_detector_v4/runs/acne_f100_s%d/weights/best.pt" % s)))
        def run(p):
            try: r = m.predict(str(p), imgsz=800, conf=0.05, iou=0.5, verbose=False, device=0)
            except Exception: return None                              # an unreadable file (flaky Windows drive): skipped, never counted
            if not r: return None
            r = r[0]
            return (r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy()) if len(r.boxes) else (np.zeros((0, 4)), np.zeros(0))
        P_ = [run(p) for p in pos]; keep = [i for i, x in enumerate(P_) if x is not None]; P_ = [P_[i] for i in keep]; posk = [pos[i] for i in keep]
        N_ = [x for x in (run(p) for p in neg) if x is not None]
        mx = lambda L: np.array([c.max() if len(c) else 0.0 for _, c in L])
        a = auc(mx(P_), mx(N_)); sens = float((mx(P_) >= THR).mean()); spec = float((mx(N_) < THR).mean())
        npred = np.array([int((c >= THR).sum()) for _, c in P_]); ngt = np.array([len(labels(p)) for p in posk])
        rho = float(np.corrcoef(np.argsort(np.argsort(npred)), np.argsort(np.argsort(ngt)))[0, 1])
        mae = float(np.abs(npred - ngt).mean()); bias = float((npred - ngt).mean())
        tp = fp = fn = 0
        for p, (b, c) in zip(posk, P_):
            zp = zones([x for x, cc in zip(b, c) if cc >= THR]); zg = zones(labels(p)); tp += int((zp & zg).sum()); fp += int((zp & ~zg).sum()); fn += int((~zp & zg).sum())
        zp_, zr_ = tp / max(1, tp + fp), tp / max(1, tp + fn)
        res.append(dict(auc=a, sens=sens, spec=spec, rho=rho, mae=mae, bias=bias, zP=zp_, zR=zr_, zF1=2 * zp_ * zr_ / max(1e-9, zp_ + zr_)))
        print("seed %d: %s" % (s, {k: round(v, 3) for k, v in res[-1].items()}), flush=True)
    print("\nMEAN of 3 seeds at the app threshold %.2f:" % THR)
    for k, lab in (("auc", "photo level: AUC acne vs healthy"), ("sens", "photo level: acne photos flagged (sensitivity)"), ("spec", "photo level: healthy photos left alone (specificity)"),
                   ("rho", "count level: Spearman correlation, boxes vs your boxes"), ("mae", "count level: mean abs. error (boxes per photo)"), ("bias", "count level: bias (predicted minus yours)"),
                   ("zP", "3x3 zones: precision"), ("zR", "3x3 zones: recall"), ("zF1", "3x3 zones: F1")):
        print("  %-58s %.3f +/- %.3f" % (lab, np.mean([r[k] for r in res]), np.std([r[k] for r in res], ddof=1)))


if __name__ == "__main__":
    main()
