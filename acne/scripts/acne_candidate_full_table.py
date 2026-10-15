#!/usr/bin/env python
"""acne_candidate_full_table.py - the full results table of the 960 px candidate (3-model average) next to the installed baseline average. Read-only; nothing is trained or installed.
Validation (154 photos) and the development test (167 photos, already scored for both; this is a descriptive breakdown, no decision is based on it).
Run in WSL, with no training running.  Output: acne/docs/ACNE_CANDIDATE_960_FULL_RESULTS.md"""
import sys, json
from pathlib import Path
import numpy as np
sys.path[:0] = ["/mnt/c/Users/Bautista/source/repos/SkinDevApp/" + _d for _d in ("shared", "acne/scripts")]
import build_report_notebooks as B
ns = {}; exec(B.TOOLBOX, ns)
rd, iou, side, images_of, raw_preds, fuse, score, at_conf, best_conf, rows_at, prf, zone_f1, f1_by_iou, bootstrap_f1 = (ns[k] for k in (
    "rd", "iou", "side", "images_of", "raw_preds", "fuse", "score", "at_conf", "best_conf", "rows_at", "prf", "zone_f1", "f1_by_iou", "bootstrap_f1"))
import pandas as pd

HOME = Path.home() / "PrecisionSkinV1"; DS = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/acne_dataset_v4"); VAL, TEST = DS / "val", DS / "test"
RUNS = HOME / "lesion_experiments" / "runs"
CAND = ([RUNS / ("acne_final_960_lowlr_seed%d" % s) / "weights/best.pt" for s in (42, 7, 123)], 960)
BASE = ([HOME / "runs/lesion_acne_boxed_only/models" / ("seed_%d" % s) / "weights/best.pt" for s in (123, 42, 7)], 800)
OUT = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/acne/docs/ACNE_CANDIDATE_960_FULL_RESULTS.md"); L = []; w = L.append

def evaluate(name, paths, imgsz):
    pv = [raw_preds(p, VAL, imgsz) for p in paths]; pt = [raw_preds(p, TEST, imgsz) for p in paths]
    fv = {k: fuse([p[k] for p in pv]) for k in pv[0]}; ft = {k: fuse([p[k] for p in pt]) for k in pt[0]}
    sv = score(fv, VAL); thr = best_conf(sv); st = score(ft, TEST); rows = rows_at(ft, TEST, thr); p, r, f = prf(rows); lo, hi = bootstrap_f1(rows)
    ng = np.array([x[5] for x in rows]); npd = np.array([x[6] for x in rows]); ra = np.argsort(np.argsort(ng + 1e-9 * np.arange(len(ng)))); rb = np.argsort(np.argsort(npd + 1e-9 * np.arange(len(npd))))
    sizes = np.array([side(b) for q in images_of(TEST) for b in rd(TEST / "labels" / (q.stem + ".txt"))]); q1, q2 = np.percentile(sizes, 33), np.percentile(sizes, 67)
    rec = {"small": [0, 0], "medium": [0, 0], "large": [0, 0]}; den = {"sparse (<=6)": [0, 0], "medium (7-14)": [0, 0], "dense (15+)": [0, 0]}
    for k, pr in ft.items():
        gt = rd(TEST / "labels" / (Path(k).stem + ".txt")); pc = [b for b, c in pr if c >= thr]
        for g in gt:
            hit = any(iou(g, b) >= 0.5 for b in pc); z = "small" if side(g) <= q1 else "medium" if side(g) <= q2 else "large"; rec[z][1] += 1; rec[z][0] += hit
            z2 = "sparse (<=6)" if len(gt) <= 6 else "medium (7-14)" if len(gt) <= 14 else "dense (15+)"; den[z2][1] += 1; den[z2][0] += hit
    pv_f = at_conf(sv, thr)
    singles = [(score(a, TEST)["AP50"], score(a, TEST)["AP50_95"]) for a in pt]
    return dict(name=name, thr=thr, val_ap=sv["AP50"], val_ap95=sv["AP50_95"], val_prf=pv_f, ap=st["AP50"], ap95=st["AP50_95"], prf=(p, r, f), ci=(lo, hi), zone=zone_f1(rows),
                with_box=float(np.mean(npd > 0)), mae=float(np.abs(ng - npd).mean()), within2=float(np.mean(np.abs(ng - npd) <= 2)), sp=float(np.corrcoef(ra, rb)[0, 1]), by_iou=f1_by_iou(ft, TEST, thr, (0.3, 0.5, 0.7)),
                rec=rec, den=den, ft=ft, singles=singles)

R = [evaluate("960 px candidate (3-model average)", *CAND), evaluate("installed baseline (3-model average, 800 px)", *BASE)]
w("# Acne: full results of the 960 px candidate vs the installed model (generated)\n")
w("Descriptive breakdown. The threshold of each model set is the best F1 on the **validation** photos; the development test (167 photos) was already scored for both and is not used to decide anything here. The decisive test is the fresh test set (Phase 3).\n")
w("## 1. Headline table\n"); w("| measure | " + " | ".join(r["name"] for r in R) + " |"); w("|---|---|---|")
rowsdef = [("image size (px)", lambda r: "960" if "960" in r["name"] else "800"), ("confidence threshold (from validation)", lambda r: "%.3f" % r["thr"]),
           ("VALIDATION mAP@0.5", lambda r: "%.3f" % r["val_ap"]), ("VALIDATION mAP@0.5-0.95", lambda r: "%.3f" % r["val_ap95"]),
           ("VALIDATION precision / recall / F1", lambda r: "%.3f / %.3f / %.3f" % r["val_prf"]),
           ("DEV TEST mAP@0.5", lambda r: "**%.3f**" % r["ap"]), ("DEV TEST mAP@0.5-0.95", lambda r: "%.3f" % r["ap95"]),
           ("DEV TEST precision", lambda r: "%.3f" % r["prf"][0]), ("DEV TEST recall", lambda r: "%.3f" % r["prf"][1]), ("DEV TEST F1 (95 % range)", lambda r: "%.3f (%.2f-%.2f)" % (r["prf"][2], *r["ci"])),
           ("DEV TEST 3x3 face-zone F1", lambda r: "%.3f" % r["zone"]), ("photos with at least one box", lambda r: "%.0f %%" % (100 * r["with_box"])),
           ("F1 at IoU 0.3 / 0.5 / 0.7", lambda r: " / ".join("%.3f" % r["by_iou"][t] for t in (0.3, 0.5, 0.7))),
           ("lesion count: mean error / within 2 / Spearman", lambda r: "%.2f / %.0f %% / %.2f" % (r["mae"], 100 * r["within2"], r["sp"]))]
for name, fn in rowsdef: w("| %s | %s |" % (name, " | ".join(fn(r) for r in R)))
w("\n## 2. The three single seeds on the development test (mAP@0.5 / mAP@0.5-0.95, each at its own scoring)\n"); w("| seed order | " + " | ".join(r["name"] for r in R) + " |"); w("|---|---|---|")
for i in range(3): w("| model %d | %s |" % (i + 1, " | ".join("%.3f / %.3f" % r["singles"][i] for r in R)))
w("\n## 3. Recall at IoU 0.5 on the development test (what share of your boxes the model finds)\n"); w("| group | " + " | ".join(r["name"] for r in R) + " |"); w("|---|---|---|")
for g in R[0]["rec"]: w("| spot size: %s | %s |" % (g, " | ".join("%.0f %% (%d of %d)" % (100 * r["rec"][g][0] / max(1, r["rec"][g][1]), r["rec"][g][0], r["rec"][g][1]) for r in R)))
for g in R[0]["den"]: w("| photo density: %s | %s |" % (g, " | ".join("%.0f %% (%d of %d)" % (100 * r["den"][g][0] / max(1, r["den"][g][1]), r["den"][g][0], r["den"][g][1]) for r in R)))
w("\n## 4. Threshold table, development test (candidate, then baseline)\n")
for r in R:
    w("**%s**\n" % r["name"]); w("| threshold | precision | recall | F1 | zone F1 | photos with a box |"); w("|---|---|---|---|---|---|")
    for t in (0.15, 0.20, 0.225, 0.25, 0.275, 0.30, 0.35, 0.40):
        rr = rows_at(r["ft"], TEST, t); p, rc, f = prf(rr); w("| %.3f | %.3f | %.3f | %.3f | %.3f | %.0f %% |" % (t, p, rc, f, zone_f1(rr), 100 * np.mean([x[6] > 0 for x in rr])))
    w("")
w("## 5. Cost\nThe candidate uses 960 px input: about 1.4 times the time per model of the 800 px baseline (the app runs three models of each condition).")
OUT.write_text("\n".join(L), encoding="utf-8"); print("\n".join(L))
