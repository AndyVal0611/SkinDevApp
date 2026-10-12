#!/usr/bin/env python
"""
hyp_error_analysis.py - where does the hyperpigmentation detector go wrong? (read-only; VALIDATION photos only, never the test photos)
  recall by patch size and by patches per photo; IoU of the matched boxes; split / merged patches; false boxes by size and confidence;
  size and position bias; how much the result would change if every box were judged at IoU 0.3 instead of 0.5.
Run in WSL:  python hyp_error_analysis.py      Output: ~/PrecisionSkinV1/hyp_final_eval/HYP_ERROR_ANALYSIS.md
"""
import sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
from ultralytics import YOLO

W = F.E / "runs/H10_v2_lowlr/weights/best.pt"; CONF = 0.20


def side(b): return (b[2] * b[3]) ** 0.5

def main():
    data = F.photo_preds(YOLO(str(W)), 800, F.VAL); L = []; w = L.append
    w("# Hyperpigmentation error analysis (validation photos, H10 seed 42, threshold %.2f)\n" % CONF)
    ng = sum(len(g) for g, _ in data.values()); w("Photos: %d | your boxes: %d | boxes per photo: %.1f\n" % (len(data), ng, ng / len(data)))
    sizes = np.array([side(b) for g, _ in data.values() for b in g]); q1, q2 = np.percentile(sizes, 33), np.percentile(sizes, 67)
    rec = {"small": [0, 0], "medium": [0, 0], "large": [0, 0]}; recn = {"1-2 patches": [0, 0], "3-5": [0, 0], "6+": [0, 0]}
    ious, split, merged, fp_conf, fp_size, ratio, off, fp_n = [], 0, 0, [], [], [], [], 0
    for g, pr in data.values():
        pc = [(b, c) for b, c in pr if c >= CONF]
        k = "1-2 patches" if len(g) <= 2 else "3-5" if len(g) <= 5 else "6+"
        for gi, gb in enumerate(g):
            s = side(gb); sz = "small" if s <= q1 else "medium" if s <= q2 else "large"; rec[sz][1] += 1; recn[k][1] += 1
            hits = [(F.iou(gb, b), b) for b, _ in pc]; best = max(hits, default=(0, None), key=lambda x: x[0])
            if best[0] >= 0.5: rec[sz][0] += 1; recn[k][0] += 1; ious.append(best[0]); ratio.append(side(best[1]) / s); off.append((((best[1][0] - gb[0]) ** 2 + (best[1][1] - gb[1]) ** 2) ** 0.5) / s)
            # split: two or more predictions each covering a good part of one patch of yours
            cover = [b for b, _ in pc if F.iou(gb, b) > 0.1 and b[2] * b[3] < gb[2] * gb[3]]
            if len(cover) >= 2: split += 1
        for b, c in pc:
            inside = [gb for gb in g if F.iou(gb, b) >= 0.1]
            if sum(1 for gb in g if (min(xyxy_ for xyxy_ in [0]) == 0) and F.iou(gb, b) > 0.05 and gb[2] * gb[3] < b[2] * b[3]) >= 2: merged += 1
            if not any(F.iou(gb, b) >= 0.5 for gb in g):
                fp_n += 1; fp_conf.append(c); fp_size.append(side(b))
    w("## Recall at IoU 0.5\n"); w("| patch size (terciles: small <= %.3f, large > %.3f of the photo) | boxes | found | recall |" % (q1, q2)); w("|---|---|---|---|")
    for k, (a, b) in rec.items(): w("| %s | %d | %d | %.3f |" % (k, b, a, a / max(1, b)))
    w("\n| patches in the photo | boxes | found | recall |"); w("|---|---|---|---|")
    for k, (a, b) in recn.items(): w("| %s | %d | %d | %.3f |" % (k, b, a, a / max(1, b)))
    ious = np.array(ious)
    w("\n## Box quality\n")
    w("- Median IoU of found patches: **%.2f** (25 %% below %.2f, 75 %% below %.2f)" % (np.median(ious), np.percentile(ious, 25), np.percentile(ious, 75)))
    w("- Model box side / your box side: median **%.2f**; centre offset: median **%.2f** of the box side" % (np.median(ratio), np.median(off)))
    w("- Your patches covered by 2+ smaller model boxes (split): **%d** of %d; model boxes that sit on 2+ of your smaller patches (merged): **%d**" % (split, ng, merged))
    fs = np.array(fp_size); fc = np.array(fp_conf)
    w("- False boxes (no box of yours at IoU 0.5): **%d**, median size %.3f vs your median %.3f, median confidence %.2f" % (fp_n, np.median(fs) if len(fs) else 0, np.median(sizes), np.median(fc) if len(fc) else 0))
    w("\n## What if matching were looser?\n"); w("| IoU needed | precision | recall | F1 |"); w("|---|---|---|---|")
    for thr in (0.2, 0.3, 0.4, 0.5, 0.6):
        tp = fp = fn = 0
        for g, pr in data.values():
            pc = [b for b, c in pr if c >= CONF]; used = set(); t = 0
            for b in pc:
                best, bj = 0, -1
                for j, gb in enumerate(g):
                    if j in used: continue
                    v = F.iou(gb, b)
                    if v > best: best, bj = v, j
                if bj >= 0 and best >= thr: used.add(bj); t += 1
            tp += t; fp += len(pc) - t; fn += len(g) - t
        p, r = tp / max(1, tp + fp), tp / max(1, tp + fn); w("| %.1f | %.3f | %.3f | %.3f |" % (thr, p, r, 2 * p * r / max(1e-9, p + r)))
    out = F.OUT / "HYP_ERROR_ANALYSIS.md"; out.write_text("\n".join(L), encoding="utf-8"); print("\n".join(L))

if __name__ == "__main__":
    main()
