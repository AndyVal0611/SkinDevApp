#!/usr/bin/env python
"""
acne_compare_report.py - after the acne v3 training: compares v1 (app), the first new model (v2 run) and the v3 run on the SAME 167 hand-boxed test photos
and writes ACNE_V3_COMPARISON.md (+ csv) into Acne_Boxing. Nothing is tuned on the test photos: the confidence threshold is chosen on validation.
Run in WSL:  python acne_compare_report.py            (use --wait to sleep until the v3 100% run has finished)
"""
import argparse, sys, time
from pathlib import Path
import numpy as np, pandas as pd

sys.path.insert(0, "/mnt/c/Users/Bautista/Downloads")
import hyperpig_autolabel as H                                   # the scoring functions (own AP50 / AP50-95 scorer)

HOME = Path.home() / "PrecisionSkinV1"
O2, O3 = HOME / "acne_detector_v2", HOME / "acne_detector_v3"
V1 = HOME / "detector_work/runs/detector_v1_yolov8n_seed42/weights/best.pt"
REPORT = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing")
IMGSZ = 800


def n_train(O, name):
    f = O / ("train_%s.txt" % name)
    return len(f.read_text().split()) if f.exists() else None


def xyxy(b): return np.array([b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2])


def iou(a, b):
    a, b = xyxy(a), xyxy(b)
    iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    u = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i
    return i / u if u > 0 else 0


def by_size(w, test_dir, conf):
    from ultralytics import YOLO
    m = YOLO(str(w)); bins = [(0, 0.02, "tiny (<2% of photo side)"), (0.02, 0.04, "small (2-4%)"), (0.04, 0.08, "medium (4-8%)"), (0.08, 9, "large (>8%)")]
    gt_n = [0] * 4; hit = [0] * 4; fp = 0; n_pred = 0; n_gt = 0
    for img in sorted((test_dir / "images").iterdir()):
        lab = test_dir / "labels" / (img.stem + ".txt")
        gt = [tuple(map(float, l.split()[1:5])) for l in lab.read_text().splitlines() if len(l.split()) >= 5] if lab.exists() else []
        r = m.predict(str(img), imgsz=IMGSZ, conf=conf, iou=0.5, verbose=False, device=0)[0]
        pr = [tuple(map(float, x)) for x in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else []
        used = set(); n_pred += len(pr); n_gt += len(gt)
        for g in gt:
            k = [i for i, (lo, hi, _) in enumerate(bins) if lo <= (g[2] * g[3]) ** 0.5 < hi][0]; gt_n[k] += 1
            best, bj = 0, -1
            for j, p in enumerate(pr):
                if j not in used and iou(g, p) > best: best, bj = iou(g, p), j
            if best >= 0.5: used.add(bj); hit[k] += 1
        fp += len(pr) - len(used)
    return [(bins[i][2], hit[i], gt_n[i]) for i in range(4)], fp, n_pred, n_gt


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--wait", action="store_true"); a = ap.parse_args()
    if a.wait:
        while not (O3 / "runs/acne_f100_s42/DONE").exists():
            time.sleep(30)
        time.sleep(60)
    L = O3 / "dataset"                                           # v3 copy: val has MORE photos than v2's, test is the same 167 photos
    rows, weights = [], []
    t2 = {p.name for p in (O2 / "dataset/test/images").iterdir()}; t3 = {p.name for p in (L / "test/images").iterdir()}
    print("test photos identical between the two runs:", t2 == t3, "(%d vs %d)" % (len(t2), len(t3)))
    def add(label, w, sz, cid, fixed, n, run, val_dir=None):
        if not Path(w).exists() or (run != "app" and not (Path(w).parent.parent / "DONE").exists()):
            return                                               # unfinished runs are not scored
        vdir = val_dir or (O2 / "dataset" if run == "v2 run" else L)          # each model picks its threshold on ITS OWN validation photos
        sv = H.score_model(w, sz, vdir / "val", cid); st = H.score_model(w, sz, L / "test", cid)
        conf = fixed if fixed is not None else H.best_f1_conf(sv)
        p, r, f = H.at_conf(st, conf)
        rows.append(dict(model=label, run=run, train_photos=n, test_mAP50=round(st["AP50"], 3), test_mAP50_95=round(st["AP50_95"], 3), conf=round(conf, 2),
                         test_precision=round(p, 3), test_recall=round(r, 3), test_F1=round(f, 3)))
        weights.append((label, Path(w), conf))
    add("v1 (in the app now)", V1, 640, 0, 0.20, None, "app")
    for frac in (0.25, 0.5, 1.0):
        nm = "acne_f%d_s42" % int(round(frac * 100)); add("first new model, %d%% of data" % int(round(frac * 100)), O2 / "runs" / nm / "weights/best.pt", IMGSZ, None, None, n_train(O2, nm), "v2 run")
    for frac in (0.5, 1.0):
        nm = "acne_f%d_s42" % int(round(frac * 100)); add("second new model, %d%% of data" % int(round(frac * 100)), O3 / "runs" / nm / "weights/best.pt", IMGSZ, None, None, n_train(O3, nm), "v3 run")
    O3a = HOME / "acne_detector_v3a"                             # experiment: v3 data WITHOUT the 400 non-facial photos
    add("v3a: same as v3 but facial photos only, 100%", O3a / "runs/acne_f100_s42/weights/best.pt", IMGSZ, None, None, n_train(O3a, "acne_f100_s42"), "v3a run", val_dir=O3a / "dataset")
    df = pd.DataFrame(rows)
    df.to_csv(REPORT / "ACNE_V3_COMPARISON.csv", index=False)

    new = df[df.run == "v3 run"].sort_values("train_photos"); old = df[df.run == "v2 run"].sort_values("train_photos")
    best_old = old.iloc[-1] if len(old) else None
    verdict = []
    if len(new) >= 1 and best_old is not None:
        top = new.iloc[-1]
        d = top.test_mAP50 - best_old.test_mAP50
        verdict.append("The v3 model with all data (%s photos) scores mAP@0.5 **%.3f** on the 167 hand-boxed test photos, against **%.3f** for the first new model (%s photos) and **%.3f** for v1 in the app." % (
            top.train_photos, top.test_mAP50, best_old.test_mAP50, best_old.train_photos, df.iloc[0].test_mAP50))
        verdict.append("Change from adding the last ~%d training photos: **%+.3f** mAP@0.5, F1 %+.3f." % (int(top.train_photos - best_old.train_photos), d, top.test_F1 - best_old.test_F1))
        if d >= 0.02:
            more = "YES, still worth adding photos: the score climbed by 0.02 or more when the data grew by about half. Box another 300-500 photos (the helper drafts make it about 14 s per photo)."
        elif d >= 0.01:
            more = "MAYBE: the gain is small (0.01-0.02). Re-box doubtful photos and check consistency first; add more photos only if you have time."
        elif d <= -0.02:
            more = "NOT YET: the model with more data scored LOWER. The new photos (drafted / non-facial ones) may differ from your test photos or carry inconsistent boxes. Review the false boxes before adding anything."
        else:
            more = "NO, not for the score: the last extra photos changed it by less than 0.01, which is inside the noise of one run on 167 test photos. Put effort into consistent boxes (the false-box review) and into the final multi-seed run instead."
        verdict.append("**Do we need more acne photos?** " + more)
        verdict.append("Caution: one training run (seed 42) per size; differences below about 0.02 are not reliable on 167 test photos. The final reported number should be the mean of 3 seeds.")
    bs = None
    if len(new):
        w, conf = [(l, p, c) for l, p, c in weights if l.startswith("second new model")][-1][1:]
        bs = by_size(w, L / "test", conf)
    out = ["# Acne detector: v1 vs the new models (v3 comparison)", "", "Same **167 hand-boxed test photos** for every row (never trained on). Confidence threshold chosen on validation (v1 uses its app threshold 0.20). One seed per run.", "",
           "| " + " | ".join(df.columns) + " |", "|" + "---|" * len(df.columns)] + ["| " + " | ".join("" if pd.isna(v) else str(v) for v in r) + " |" for r in df.itertuples(index=False)] + ["", "## Verdict", ""] + ["- " + v for v in verdict]
    if bs:
        rows_s, fp, n_pred, n_gt = bs
        out += ["", "## Where the best model fails (test photos)", "", "| true box size | found | of | recall |", "|---|---|---|---|"]
        out += ["| %s | %d | %d | %.0f%% |" % (n, h, t, 100 * h / max(1, t)) for n, h, t in rows_s]
        out += ["", "It drew %d boxes in total; %d of them did not match any of your boxes (IoU 0.5)." % (n_pred, fp)]
    (REPORT / "ACNE_V3_COMPARISON.md").write_text("\n".join(out), encoding="utf-8")
    print("\n".join(out))


if __name__ == "__main__":
    main()
