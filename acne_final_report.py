#!/usr/bin/env python
"""
acne_final_report.py - the FINAL acne detector numbers: 3 seeds on the clean dataset (v4), v1 for comparison, all on the same hand-boxed test photos.

  * every seed is scored separately (mAP@0.5, mAP@0.5-0.95, precision / recall / F1 at the confidence chosen on VALIDATION photos)
  * mean and spread (standard deviation across seeds) are reported
  * a bootstrap (1,000 resamples of the test PHOTOS) gives a 95% range for the mean mAP@0.5 and for the gain over v1, so a difference that is only noise is not reported as an improvement
Writes FINAL_ACNE_RESULTS.md and .csv into Acne_Boxing.     python acne_final_report.py [--seeds 42 7 123]
"""
import argparse, sys
from pathlib import Path
import numpy as np, pandas as pd

sys.path.insert(0, "/mnt/c/Users/Bautista/Downloads")
import hyperpig_autolabel as H

HOME = Path.home() / "PrecisionSkinV1"
O4 = HOME / "acne_detector_v4"
V1 = HOME / "detector_work/runs/detector_v1_yolov8n_seed42/weights/best.pt"
REPORT = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing")
IMGSZ = 800


def per_photo(weights, imgsz, test_dir, class_id=None):
    """for every test photo: (confidences, tp flags at IoU 0.5, number of true boxes)."""
    from ultralytics import YOLO
    import torch
    m = YOLO(str(weights)); out = []
    imgs = H.list_images(Path(test_dir) / "images")
    for s in range(0, len(imgs), 16):
        chunk = imgs[s:s + 16]
        res = m.predict([str(p) for p in chunk], imgsz=imgsz, conf=0.001, iou=0.6, max_det=300, verbose=False, device=0 if torch.cuda.is_available() else "cpu")
        for p, r in zip(chunk, res):
            h, w = r.orig_shape
            g = np.array([[(cx - bw / 2) * w, (cy - bh / 2) * h, (cx + bw / 2) * w, (cy + bh / 2) * h] for cx, cy, bw, bh in H.read_yolo(Path(test_dir) / "labels" / (p.stem + ".txt"))], np.float32).reshape(-1, 4)
            if r.boxes is None or len(r.boxes) == 0:
                out.append((np.zeros(0), np.zeros(0), len(g))); continue
            b = r.boxes.xyxy.cpu().numpy(); c = r.boxes.conf.cpu().numpy()
            if class_id is not None:
                keep = r.boxes.cls.cpu().numpy().astype(int) == class_id; b, c = b[keep], c[keep]
            o = np.argsort(-c); b, c = b[o], c[o]
            iou = H._iou_matrix(b, g); used = set(); flag = np.zeros(len(b))
            for i in range(len(b)):
                cand = [(iou[i, j], j) for j in range(iou.shape[1]) if j not in used and iou[i, j] >= 0.5] if iou.shape[1] else []
                if cand:
                    j = max(cand)[1]; used.add(j); flag[i] = 1
            out.append((c, flag, len(g)))
    return out


def ap_pr(photos, idx, conf):
    c = np.concatenate([photos[i][0] for i in idx]); t = np.concatenate([photos[i][1] for i in idx]); n = sum(photos[i][2] for i in idx)
    ap = H._ap(c, t, n)
    m = c >= conf; tp = t[m].sum(); P = tp / m.sum() if m.sum() else 0.0; R = tp / n if n else 0.0
    return ap, P, R, (2 * P * R / (P + R) if P + R else 0.0)


def main():
    ap_ = argparse.ArgumentParser(); ap_.add_argument("--seeds", type=int, nargs="+", default=[42, 7, 123]); ap_.add_argument("--boot", type=int, default=1000)
    a = ap_.parse_args()
    L = O4 / "dataset"; test = L / "test"
    models = [("v1 (in the app now)", V1, 640, 0, 0.20)]
    for s in a.seeds:
        w = O4 / "runs" / ("acne_f100_s%d" % s) / "weights/best.pt"
        if w.exists() and (w.parent.parent / "DONE").exists():
            models.append(("v4 seed %d" % s, w, IMGSZ, None, None))
        else:
            print("seed %d not finished, skipped" % s)
    rows, pp, confs = [], {}, {}
    for name, w, sz, cid, fixed in models:
        sv = H.score_model(w, sz, L / "val", cid); st = H.score_model(w, sz, test, cid)
        conf = fixed if fixed is not None else H.best_f1_conf(sv)
        p, r, f = H.at_conf(st, conf)
        rows.append(dict(model=name, val_mAP50=round(sv["AP50"], 3), test_mAP50=round(st["AP50"], 3), test_mAP50_95=round(st["AP50_95"], 3), conf=round(conf, 2), precision=round(p, 3), recall=round(r, 3), F1=round(f, 3)))
        pp[name] = per_photo(w, sz, test, cid); confs[name] = conf
        print(rows[-1], flush=True)
    df = pd.DataFrame(rows); df.to_csv(REPORT / "FINAL_ACNE_RESULTS.csv", index=False)
    seeds = df[df.model.str.startswith("v4")]
    out = ["# Acne detector: final results (clean dataset v4, three seeds)", "",
           "Test = the same %d hand-boxed photos for every row, never trained on. Confidence chosen on validation photos boxed from scratch (v1: its app threshold 0.20)." % len(pp[models[0][0]]), "",
           "| " + " | ".join(df.columns) + " |", "|" + "---|" * len(df.columns)] + ["| " + " | ".join(str(v) for v in r) + " |" for r in df.itertuples(index=False)]
    if len(seeds) >= 2:
        out += ["", "## Mean and spread over %d seeds" % len(seeds), "", "| metric | mean | std |", "|---|---|---|"]
        for col in ("test_mAP50", "test_mAP50_95", "precision", "recall", "F1"):
            out.append("| %s | %.3f | %.3f |" % (col, seeds[col].mean(), seeds[col].std(ddof=1)))
        n = len(pp[seeds.model.iloc[0]]); rng = np.random.default_rng(0); boots = []
        v1n = models[0][0]; names = list(seeds.model)
        for _ in range(a.boot):
            idx = rng.integers(0, n, n)
            v4 = np.mean([ap_pr(pp[m], idx, confs[m])[0] for m in names]); v1 = ap_pr(pp[v1n], idx, confs[v1n])[0]
            boots.append((v4, v1, v4 - v1))
        b = np.array(boots); lo, hi = np.percentile(b[:, 0], [2.5, 97.5]); dl, dh = np.percentile(b[:, 2], [2.5, 97.5])
        out += ["", "## How sure are we? (bootstrap over the test photos, 1,000 resamples)", "",
                "- mean mAP@0.5 of the v4 seeds: **%.3f** (95%% range %.3f to %.3f)" % (seeds.test_mAP50.mean(), lo, hi),
                "- gain over v1: **%+.3f** mAP@0.5 (95%% range %+.3f to %+.3f)%s" % (seeds.test_mAP50.mean() - float(df.test_mAP50.iloc[0]), dl, dh, "  -> the gain is real, not noise" if dl > 0 else "  -> the range includes 0: not proven"),
                "", "The range reflects the small test set (resampling photos). It does not include differences in how photos were boxed; report both the mean and this range."]
    (REPORT / "FINAL_ACNE_RESULTS.md").write_text("\n".join(out), encoding="utf-8")
    print("\n".join(out))


if __name__ == "__main__":
    main()
