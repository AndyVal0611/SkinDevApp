#!/usr/bin/env python
"""
hyp_v2_final_eval.py - FROZEN evaluation under annotation rule v2 (read-only).
  validation = the 30 photos you boxed under rule v2 (pass A);  test = the 100 photos of V2_Test boxed from scratch by you, scored once here.
  models: H20 (trained on rule-v2 converted boxes) seeds 42 / 7 / 123 and their average; for comparison the old-style H10 seeds, their average, and the model installed before this work.
  threshold per model / average: best F1 on the validation photos only.
Output: ~/PrecisionSkinV1/hyp_final_eval/HYP_V2_FINAL_EVAL.md
"""
import json, shutil, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
import hyp_ensemble_tta as T
import hyperpig_autolabel as H
from ultralytics import YOLO

B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); R = F.E / "runs"
VA = F.E / "data" / "hyp_v2_valA"; TV = F.E / "data" / "hyp_v2_test"


def build_test():
    D = B / "V2_Test"; mp = json.load(open(D / "name_map.json")); shutil.rmtree(TV, ignore_errors=True); (TV / "images").mkdir(parents=True); (TV / "labels").mkdir(parents=True)
    for orig, new in mp.items():
        img = next(iter((D / "Elaiza__hyperpigmentation_v2/images").glob(new + ".*"))); shutil.copy2(img, TV / "images" / (Path(orig).stem + img.suffix.lower()))
        shutil.copy2(D / "Elaiza__hyperpigmentation_v2/labels" / (new + ".txt"), TV / "labels" / (Path(orig).stem + ".txt"))


def zone(rows):
    tp = sum(len(r[3] & r[4]) for r in rows); fp = sum(len(r[4] - r[3]) for r in rows); fn = sum(len(r[3] - r[4]) for r in rows)
    return 2 * tp / max(1, 2 * tp + fp + fn)


def main():
    build_test(); L = []; w = L.append
    nt = len(list((TV / "images").glob("*"))); nbx = sum(len(F.rd(f)) for f in (TV / "labels").glob("*.txt"))
    w("# Hyperpigmentation under rule v2: frozen evaluation\n"); w("Validation: %d photos (your pass A). Test: %d photos, %d boxes (V2_Test, boxed from scratch). Threshold = best F1 on validation.\n" % (len(list((VA / "images").glob("*"))), nt, nbx))
    sets = {"H20 (rule-v2 conversion)": [R / "H20_v2auto/weights/best.pt", R / "H20_s7/weights/best.pt", R / "H20_s123/weights/best.pt"],
            "H10 (old-style boxes)": [R / "H10_v2_lowlr/weights/best.pt", R / "H10_s7/weights/best.pt", R / "H10_s123/weights/best.pt"]}
    old = F.P / "hyperpig_autolabel/hyperpig_only_v1/runs/s_800/weights/best.pt"
    w("| model | threshold (val) | test mAP@0.5 | test mAP@0.5-0.95 | precision | recall | F1 | zone F1 |"); w("|---|---|---|---|---|---|---|---|")
    cache = {}
    def rows_for(data, thr): return F.counts({k: (F.rd(TV / "labels" / (Path(k).stem + ".txt")), v) for k, v in data.items()}, thr)
    for gname, paths in sets.items():
        singles = []
        for i, wt in enumerate(paths):
            sv = H.score_model(wt, 800, VA); c = H.best_f1_conf(sv); st = H.score_model(wt, 800, TV); p, r, f = H.at_conf(st, c)
            m = YOLO(str(wt)); data = {n: pr for n, (g, pr) in F.photo_preds(m, 800, TV).items()}
            zr = zone(rows_for(data, c)); singles.append(st["AP50"])
            w("| %s, seed %d | %.3f | %.3f | %.3f | %.3f | %.3f | %.3f | %.3f |" % (gname, [42, 7, 123][i], c, st["AP50"], st["AP50_95"], p, r, f, zr))
        w("| **%s, mean of 3 seeds** | | **%.3f** (std %.3f) | | | | | |" % (gname, np.mean(singles), np.std(singles, ddof=1)))
        PV = [{n: pr for n, pr in T.raw_preds(YOLO(str(wt)), VA, False).items()} for wt in paths]; PT = [T.raw_preds(YOLO(str(wt)), TV, False) for wt in paths]
        fv = {k: T.fuse([p[k] for p in PV]) for k in PV[0]}; ap_v, (f_v, c, _, _) = T.evaluate(fv, VA)
        ft = {k: T.fuse([p[k] for p in PT]) for k in PT[0]}; ap_t, _ = T.evaluate(ft, TV)
        rows = rows_for(ft, c); p, r, f = F.prf(rows); rng = np.random.default_rng(0); idx = np.arange(len(rows)); bs = [F.prf([rows[i] for i in rng.choice(idx, len(idx))])[2] for _ in range(1000)]
        w("| **%s, 3-model average** | %.3f | **%.3f** | n/a | %.3f | %.3f | **%.3f** (95%% range %.2f-%.2f) | %.3f |" % (gname, c, ap_t, p, r, f, np.percentile(bs, 2.5), np.percentile(bs, 97.5), zone(rows)))
    sv = H.score_model(old, 800, VA); c = H.best_f1_conf(sv); st = H.score_model(old, 800, TV); p, r, f = H.at_conf(st, c)
    data = {n: pr for n, (g, pr) in F.photo_preds(YOLO(str(old)), 800, TV).items()}
    w("| model installed before this work (s_800) | %.3f | %.3f | %.3f | %.3f | %.3f | %.3f | %.3f |" % (c, st["AP50"], st["AP50_95"], p, r, f, zone(rows_for(data, c))))
    (F.OUT / "HYP_V2_FINAL_EVAL.md").write_text("\n".join(L), encoding="utf-8"); print("\n".join(L))


if __name__ == "__main__":
    main()
