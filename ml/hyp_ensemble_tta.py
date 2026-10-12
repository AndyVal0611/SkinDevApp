#!/usr/bin/env python
"""
hyp_ensemble_tta.py - do free inference tricks help the hyperpigmentation detector? (VALIDATION photos only; nothing is trained or changed)
  A  single model, plain                (the three H10 seeds, mean)
  B  single model + test-time augmentation (flips / scales built into YOLO, augment=True)
  C  the 3 seeds averaged: boxes from the 3 models are merged (weighted box fusion, IoU 0.5), confidence = mean over models
  D  C + test-time augmentation
Reports mAP@0.5 and F1 at the best validation threshold. Run in WSL.  Output: ~/PrecisionSkinV1/hyp_final_eval/HYP_ENSEMBLE_TTA.md
"""
import sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
import hyperpig_autolabel as H
from ultralytics import YOLO

PATHS = [F.E / "runs/H10_v2_lowlr/weights/best.pt", F.E / "runs/H10_s7/weights/best.pt", F.E / "runs/H10_s123/weights/best.pt"]


def raw_preds(model, d, aug):
    out = {}
    imgs = sorted(p for p in (d / "images").glob("*") if p.suffix.lower() in F.EXT)
    for s in range(0, len(imgs), 8):
        ch = imgs[s:s + 8]
        for p, r in zip(ch, model.predict([str(x) for x in ch], imgsz=800, conf=0.001, iou=0.6, max_det=300, augment=aug, verbose=False, device=0)):
            out[p.name] = [(tuple(map(float, b)), float(c)) for b, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy())] if len(r.boxes) else []
    return out


def fuse(lists, thr=0.5):
    """weighted box fusion of several models' boxes for one photo (xywhn); confidence = sum of confidences / number of models"""
    n = len(lists); allb = sorted([b for l in lists for b in l], key=lambda x: -x[1]); clusters = []
    for b, c in allb:
        for cl in clusters:
            if F.iou(cl["box"], b) >= thr: cl["m"].append((b, c)); w = np.array([x[1] for x in cl["m"]]); cl["box"] = tuple(np.average(np.array([x[0] for x in cl["m"]]), axis=0, weights=w)); break
        else: clusters.append({"box": b, "m": [(b, c)]})
    return [(cl["box"], min(1.0, sum(x[1] for x in cl["m"]) / n)) for cl in clusters]


def evaluate(preds, d):
    confs, tps, n_gt = [], [], 0
    for name, pr in preds.items():
        gt = F.rd(d / "labels" / (Path(name).stem + ".txt")); n_gt += len(gt); used = set()
        for b, c in sorted(pr, key=lambda x: -x[1]):
            best, bj = 0, -1
            for j, g in enumerate(gt):
                if j in used: continue
                v = F.iou(g, b)
                if v > best: best, bj = v, j
            ok = bj >= 0 and best >= 0.5
            if ok: used.add(bj)
            confs.append(c); tps.append(1.0 if ok else 0.0)
    confs, tps = np.array(confs), np.array(tps); o = np.argsort(-confs); confs, tps = confs[o], tps[o]
    ap = H._ap(confs, tps, n_gt); best = (0, 0, 0, 0)
    for c in np.arange(0.05, 0.9, 0.025):
        m = confs >= c; t = tps[m].sum(); p = t / max(1, m.sum()); r = t / max(1, n_gt); f = 2 * p * r / max(1e-9, p + r)
        if f > best[0]: best = (f, c, p, r)
    return ap, best


def main():
    L = []; w = L.append
    w("# Hyperpigmentation: seed averaging and test-time augmentation (validation photos only)\n"); w("| variant | mAP@0.5 | best F1 | at threshold | precision | recall |"); w("|---|---|---|---|---|---|")
    models = [YOLO(str(p)) for p in PATHS]; res = {}
    for aug in (False, True):
        P = [raw_preds(m, F.VAL, aug) for m in models]
        singles = [evaluate(p, F.VAL) for p in P]
        ap = np.mean([s[0] for s in singles]); fb = np.mean([s[1][0] for s in singles])
        w("| %s, single model (mean of 3 seeds) | %.3f | %.3f | | | |" % ("TTA" if aug else "plain", ap, fb))
        fused = {n: fuse([p[n] for p in P]) for n in P[0]}; a, (f, c, pp, rr) = evaluate(fused, F.VAL)
        w("| %s, 3 seeds averaged | **%.3f** | %.3f | %.3f | %.3f | %.3f |" % ("TTA" if aug else "plain", a, f, c, pp, rr))
    w("\nReference: the three single H10 seeds scored 0.324, 0.294 and 0.283 on the same photos (mean 0.300).")
    (F.OUT / "HYP_ENSEMBLE_TTA.md").write_text("\n".join(L), encoding="utf-8"); print("\n".join(L))


if __name__ == "__main__":
    main()
