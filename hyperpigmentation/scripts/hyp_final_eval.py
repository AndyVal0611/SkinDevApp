#!/usr/bin/env python
"""
hyp_final_eval.py - FROZEN evaluation of the hyperpigmentation detector on the hand-boxed TEST photos (read-only, nothing is changed).

  models: H10 recipe with seeds 42 / 7 / 123 (chosen recipe), and the model that is installed in the app today (s_800) as the comparison.
  threshold per model: best F1 on the hand-boxed VALIDATION photos (never the test photos)
  per model on the test photos: mAP@0.5, mAP@0.5:0.95, precision, recall, F1 (IoU 0.5) at that threshold
  best seed (by validation) in more detail: bootstrap 95 % range over photos, 3x3 face-zone F1 (patch-centre based), photo-level detection rate,
  count agreement (Spearman, MAE), and the F1 at the other thresholds (0.15 / 0.20 / 0.25 / 0.30)
Output: ~/PrecisionSkinV1/hyp_final_eval/HYP_FINAL_EVAL.md   Run in WSL.
"""
import json, sys
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts", "hyperpigmentation/scripts", "eczema/scripts", "skintone", "gradcam")]
import hyperpig_autolabel as H
from ultralytics import YOLO

P = Path.home() / "PrecisionSkinV1"; E = P / "lesion_experiments"; OUT = P / "hyp_final_eval"; OUT.mkdir(exist_ok=True)
VAL = E / "data" / "hyp_hand_val"; TEST = P / "hyperpig_autolabel" / "hyperpig_only_v1" / "dataset" / "hand_test"
MODELS = [("H10 seed 42", E / "runs/H10_v2_lowlr/weights/best.pt", 800), ("H10 seed 7", E / "runs/H10_s7/weights/best.pt", 800), ("H10 seed 123", E / "runs/H10_s123/weights/best.pt", 800),
          ("installed today (s_800)", P / "hyperpig_autolabel/hyperpig_only_v1/runs/s_800/weights/best.pt", 800)]
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def rd(p): return [tuple(map(float, l.split()[1:5])) for l in Path(p).read_text().splitlines() if len(l.split()) >= 5] if Path(p).exists() else []
def xyxy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2
def iou(a, b):
    a, b = xyxy(a), xyxy(b); iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    return i / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i + 1e-12)
def cell(b): return min(2, int(b[1] * 3)) * 3 + min(2, int(b[0] * 3))


def photo_preds(model, imgsz, d):
    out = {}
    imgs = sorted(p for p in (d / "images").glob("*") if p.suffix.lower() in EXT)
    for s in range(0, len(imgs), 16):
        ch = imgs[s:s + 16]
        for p, r in zip(ch, model.predict([str(x) for x in ch], imgsz=imgsz, conf=0.05, iou=0.6, max_det=300, verbose=False, device=0)):
            pr = [(tuple(map(float, b)), float(c)) for b, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy())] if len(r.boxes) else []
            out[p.name] = (rd(d / "labels" / (p.stem + ".txt")), pr)
    return out


def counts(data, conf):
    """per photo: (tp, fp, fn, gt cells, predicted cells, n_gt, n_pred)"""
    rows = []
    for gt, pr in data.values():
        pc = [b for b, c in pr if c >= conf]; used = set(); tp = 0
        for b in sorted(pc, key=lambda x: 0):
            best, bj = 0, -1
            for j, g in enumerate(gt):
                if j in used: continue
                v = iou(g, b)
                if v > best: best, bj = v, j
            if bj >= 0 and best >= 0.5: used.add(bj); tp += 1
        rows.append((tp, len(pc) - tp, len(gt) - tp, {cell(g) for g in gt}, {cell(b) for b in pc}, len(gt), len(pc)))
    return rows


def prf(rows):
    tp, fp, fn = sum(r[0] for r in rows), sum(r[1] for r in rows), sum(r[2] for r in rows)
    p, r_ = tp / max(1, tp + fp), tp / max(1, tp + fn); return p, r_, 2 * p * r_ / max(1e-9, p + r_)


def main():
    L = []; w = L.append
    nt = len(list((TEST / "images").glob("*"))); nv = len(list((VAL / "images").glob("*")))
    w("# Hyperpigmentation: frozen evaluation on the hand-boxed TEST photos\n")
    w("Test photos: %d (hand-boxed, never used for training or selection). Validation photos used to pick each threshold: %d.\n" % (nt, nv))
    w("| model | threshold (val) | test mAP@0.5 | test mAP@0.5-0.95 | precision | recall | F1 | val mAP@0.5 |"); w("|---|---|---|---|---|---|---|---|")
    keep = {}
    for name, wt, sz in MODELS:
        sv = H.score_model(wt, sz, VAL); conf = H.best_f1_conf(sv); st = H.score_model(wt, sz, TEST); p, r_, f = H.at_conf(st, conf)
        w("| %s | %.3f | **%.3f** | %.3f | %.3f | %.3f | %.3f | %.3f |" % (name, conf, st["AP50"], st["AP50_95"], p, r_, f, sv["AP50"])); keep[name] = (wt, sz, conf, st["AP50"])
        print(name, round(st["AP50"], 3), flush=True)
    seeds = [keep[n][3] for n in ("H10 seed 42", "H10 seed 7", "H10 seed 123")]
    w("\nH10 recipe, 3-seed mean on the test photos: mAP@0.5 **%.3f** (std %.3f; seeds %s)\n" % (np.mean(seeds), np.std(seeds, ddof=1), ", ".join("%.3f" % s for s in seeds)))
    best_name = "H10 seed 42"; wt, sz, conf, _ = keep[best_name]; m = YOLO(str(wt)); data = photo_preds(m, sz, TEST)
    old_name = "installed today (s_800)"; wo, so, co, _ = keep[old_name]; mo = YOLO(str(wo)); dato = photo_preds(mo, so, TEST)
    w("## Chosen model: %s at threshold %.3f (best validation score)\n" % (best_name, conf))
    rows = counts(data, conf); p, r_, f = prf(rows); rng = np.random.default_rng(0); idx = np.arange(len(rows)); bs = []
    for _ in range(1000):
        s = rng.choice(idx, len(idx)); bs.append(prf([rows[i] for i in s])[2])
    w("- Box level at IoU 0.5: precision %.3f, recall %.3f, F1 **%.3f** (95 %% range over photos %.3f - %.3f)" % (p, r_, f, np.percentile(bs, 2.5), np.percentile(bs, 97.5)))
    ro = counts(dato, co); po, rr, fo = prf(ro); w("- Installed model today, same photos and its own validation threshold %.3f: precision %.3f, recall %.3f, F1 %.3f" % (co, po, rr, fo))
    # zones
    def zone(rows_):
        tp = fp = fn = 0
        for r in rows_: g, pz = r[3], r[4]; tp += len(g & pz); fp += len(pz - g); fn += len(g - pz)
        pp, rr_ = tp / max(1, tp + fp), tp / max(1, tp + fn); return 2 * pp * rr_ / max(1e-9, pp + rr_), pp, rr_
    zf, zp, zr = zone(rows); zo = zone(ro)
    w("- 3x3 face-zone F1 (a zone counts when a patch centre falls in it): **%.3f** (precision %.3f, recall %.3f); installed model %.3f" % (zf, zp, zr, zo[0]))
    # photo level (all test photos contain at least one patch)
    det = np.mean([r[6] > 0 for r in rows]); deto = np.mean([r[6] > 0 for r in ro])
    w("- Photos where at least one patch is found: **%.1f %%** (installed model %.1f %%). All test photos contain a patch, so specificity cannot be computed from this set." % (100 * det, 100 * deto))
    def spearmanr(a, b):
        ra = np.argsort(np.argsort(a + 1e-9 * np.arange(len(a)))); rb = np.argsort(np.argsort(b + 1e-9 * np.arange(len(b)))); return (np.corrcoef(ra, rb)[0, 1],)
    ng = np.array([r[5] for r in rows]); npd = np.array([r[6] for r in rows])
    w("- Patch count: Spearman %.2f, mean absolute error %.2f patches per photo (your mean %.1f, model mean %.1f)" % (spearmanr(ng, npd)[0], np.abs(ng - npd).mean(), ng.mean(), npd.mean()))
    w("\nF1 at other thresholds (box level, test photos):\n"); w("| threshold | precision | recall | F1 |"); w("|---|---|---|---|")
    for c in (0.15, 0.20, 0.25, 0.30, 0.35):
        pp, rr_, ff = prf(counts(data, c)); w("| %.2f | %.3f | %.3f | %.3f |" % (c, pp, rr_, ff))
    (OUT / "HYP_FINAL_EVAL.md").write_text("\n".join(L), encoding="utf-8"); print("\n".join(L))


if __name__ == "__main__":
    main()
