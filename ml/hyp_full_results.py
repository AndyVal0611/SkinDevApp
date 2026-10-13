#!/usr/bin/env python
"""hyp_full_results.py - the complete hyperpigmentation results in one markdown file (read-only):
  A. every experiment run from EXPERIMENTS.csv with precision / recall / F1 (validation sets only)
  B. the installed 3-model rule-v2 detector on the 100 rule-v2 TEST photos: F1 at several IoU levels, threshold table, face zones, photo level, patch counts, box quality
Output: ~/PrecisionSkinV1/hyp_final_eval/HYP_FULL_RESULTS.md (copied to the repo docs folder by hand)"""
import sys
from pathlib import Path
import numpy as np
import pandas as pd
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

R = F.E / "runs"; TV = F.E / "data" / "hyp_v2_test"; VAL60 = F.E / "data" / "hyp_v2_val60"
paths = [R / "H20_v2auto/weights/best.pt", R / "H20_s7/weights/best.pt", R / "H20_s123/weights/best.pt"]
side = lambda b: (b[2] * b[3]) ** 0.5


def f1_at(rows_pairs, thr):
    tp = fp = fn = 0
    for gt, pc in rows_pairs:
        used = set(); t = 0
        for b in pc:
            best, bj = 0, -1
            for j, g in enumerate(gt):
                if j in used: continue
                v = F.iou(g, b)
                if v > best: best, bj = v, j
            if bj >= 0 and best >= thr: used.add(bj); t += 1
        tp += t; fp += len(pc) - t; fn += len(gt) - t
    p, r = tp / max(1, tp + fp), tp / max(1, tp + fn); return p, r, 2 * p * r / max(1e-9, p + r)


def main():
    L = []; w = L.append
    w("# Hyperpigmentation: full results (generated)\n")
    # ---- A
    e = pd.read_csv(F.E / "EXPERIMENTS.csv"); e = e[e.condition == "hyperpigmentation"]
    w("## A. Every experiment run (validation sets only; seed shown; precision/recall/F1 at IoU 0.5 at the F1-best confidence)\n")
    w("| run | what changed | train photos | validation set | mAP@0.5 | mAP@0.5-0.95 | precision | recall | F1 | confidence | epochs (best) | minutes |"); w("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for _, r in e.iterrows():
        w("| %s (seed %d) | %s | %s | %s | %.3f | %.3f | %.2f | %.2f | %.2f | %.3f | %d (%d) | %.0f |" % (r.id, r.seed, str(r.note)[:90].replace("|", "/"), r.train_photos, r.val_set, r.val_mAP50, r.val_mAP50_95, r.val_P, r.val_R, r.val_F1, r.val_conf, r.epochs_run, r.best_epoch, r.train_minutes))
    w("\nValidation sets: `hand_val` = 60 photos, old rule; `public_val` = public-style photos; `v2_valA` = 30 photos, rule v2; `v2_val60` = 60 photos, rule v2. Scores of different validation sets are not comparable.\n")
    # ---- B
    P = [T.raw_preds(YOLO(str(p)), TV, False) for p in paths]; fused = {k: T.fuse([p[k] for p in P]) for k in P[0]}
    gts = {k: F.rd(TV / "labels" / (Path(k).stem + ".txt")) for k in fused}; ap, _ = T.evaluate(fused, TV)
    nbx = sum(len(g) for g in gts.values())
    w("## B. Installed detector (3-model average, rule v2) on the 100 rule-v2 test photos (%d boxes), scored once, threshold from validation\n" % nbx)
    w("mAP@0.5 **%.3f**. Threshold table (box level, IoU 0.5):\n" % ap); w("| threshold | precision | recall | F1 | zone F1 | photos with a patch | boxes per photo |"); w("|---|---|---|---|---|---|---|")
    for thr in (0.10, 0.125, 0.15, 0.175, 0.20, 0.25, 0.30):
        pairs = [(gts[k], [b for b, c in fused[k] if c >= thr]) for k in fused]; p, r, f = f1_at(pairs, 0.5); rows = F.counts({k: (gts[k], fused[k]) for k in fused}, thr)
        w("| %.3f%s | %.3f | %.3f | %.3f | %.3f | %.0f %% | %.1f |" % (thr, " (app)" if thr == 0.175 else "", p, r, f, 2 * sum(len(x[3] & x[4]) for x in rows) / max(1, 2 * sum(len(x[3] & x[4]) for x in rows) + sum(len(x[4] - x[3]) for x in rows) + sum(len(x[3] - x[4]) for x in rows)),
                                        100 * np.mean([x[6] > 0 for x in rows]), np.mean([x[6] for x in rows])))
    thr = 0.175; pairs = [(gts[k], [b for b, c in fused[k] if c >= thr]) for k in fused]
    w("\nF1 by IoU needed (threshold 0.175): " + " | ".join("IoU %.1f: %.3f" % (t, f1_at(pairs, t)[2]) for t in (0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8)))
    rows = F.counts({k: (gts[k], fused[k]) for k in fused}, thr)
    ng = np.array([r[5] for r in rows]); npd = np.array([r[6] for r in rows])
    ra = np.argsort(np.argsort(ng + 1e-9 * np.arange(len(ng)))); rb = np.argsort(np.argsort(npd + 1e-9 * np.arange(len(npd))))
    w("\n- Patch counts per photo: your mean %.2f, model mean %.2f, Spearman %.2f, mean absolute error %.2f, exactly right in %.0f %% of photos, within 1 in %.0f %%" % (
        ng.mean(), npd.mean(), np.corrcoef(ra, rb)[0, 1], np.abs(ng - npd).mean(), 100 * np.mean(ng == npd), 100 * np.mean(np.abs(ng - npd) <= 1)))
    ious, ratio = [], []
    for g, pc in pairs:
        for gb in g:
            best = max(((F.iou(gb, b), b) for b in pc), default=(0, None), key=lambda x: x[0])
            if best[0] >= 0.3: ious.append(best[0]); ratio.append(side(best[1]) / side(gb))
    w("- Box quality of the patches found at IoU >= 0.3: median IoU %.2f (25 %% below %.2f, 75 %% below %.2f); model box side / your box side: median %.2f" % (np.median(ious), np.percentile(ious, 25), np.percentile(ious, 75), np.median(ratio)))
    sizes = np.array([side(b) for g in gts.values() for b in g]); q1, q2 = np.percentile(sizes, 33), np.percentile(sizes, 67); rec = {"small": [0, 0], "medium": [0, 0], "large": [0, 0]}
    for g, pc in pairs:
        for gb in g:
            k = "small" if side(gb) <= q1 else "medium" if side(gb) <= q2 else "large"; rec[k][1] += 1
            if any(F.iou(gb, b) >= 0.5 for b in pc): rec[k][0] += 1
    w("- Recall at IoU 0.5 by patch size: " + ", ".join("%s %.0f %% (%d of %d)" % (k, 100 * a / max(1, b), a, b) for k, (a, b) in rec.items()))
    sparse = {"1 patch": [0, 0], "2-3": [0, 0], "4+": [0, 0]}
    for g, pc in pairs:
        k = "1 patch" if len(g) <= 1 else "2-3" if len(g) <= 3 else "4+"
        for gb in g:
            sparse[k][1] += 1
            if any(F.iou(gb, b) >= 0.5 for b in pc): sparse[k][0] += 1
    w("- Recall at IoU 0.5 by patches in the photo: " + ", ".join("%s %.0f %% (%d of %d)" % (k, 100 * a / max(1, b), a, b) for k, (a, b) in sparse.items()))
    w("- The same photos with no patch in your labels: %d photos (marked X), of which the model puts a box on %d" % (sum(1 for g in gts.values() if not g), sum(1 for g, pc in pairs if not g and pc)))
    out = F.OUT / "HYP_FULL_RESULTS.md"; out.write_text("\n".join(L), encoding="utf-8"); print("\n".join(L))


if __name__ == "__main__":
    main()
