#!/usr/bin/env python
"""
lesion_diagnose_models.py - read-only diagnosis of the CURRENT detector models, on VALIDATION photos only (the test sets stay untouched).

Per model: training-curve analysis (best epoch, overfitting gap, late plateau), precision-recall curve + F1-confidence curve (PNG), recall by box size,
and false-box / missed-box counts. Weights are only read.

    python lesion_diagnose_models.py
"""
import json, sys
from pathlib import Path
import numpy as np
import pandas as pd

sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyperpig_autolabel as H
import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt

P = Path.home() / "PrecisionSkinV1"; OUT = P / "lesion_experiments" / "diagnosis"; OUT.mkdir(parents=True, exist_ok=True)
MODELS = [
    ("acne v4 seed 42", P / "acne_detector_v4/runs/acne_f100_s42", 800, P / "acne_detector_v4/dataset/val", None),
    ("acne v4 seed 7", P / "acne_detector_v4/runs/acne_f100_s7", 800, P / "acne_detector_v4/dataset/val", None),
    ("acne v4 seed 123", P / "acne_detector_v4/runs/acne_f100_s123", 800, P / "acne_detector_v4/dataset/val", None),
    ("hyperpigmentation s_800", P / "hyperpig_autolabel/hyperpig_only_v1/runs/s_800", 800, P / "hyperpig_autolabel/hyperpig_only_v1/dataset/val", None),
    ("hyperpigmentation m_800", P / "hyperpig_autolabel/hyperpig_only_v1/runs/m_800", 800, P / "hyperpig_autolabel/hyperpig_only_v1/dataset/val", None),
    ("hyperpigmentation n_640", P / "hyperpig_autolabel/hyperpig_only_v1/runs/n_640", 640, P / "hyperpig_autolabel/hyperpig_only_v1/dataset/val", None),
    ("v1 acne (3-class, class 0)", P / "detector_work/runs/detector_v1_yolov8n_seed42", 640, P / "acne_detector_v4/dataset/val", 0),
]


def curves(run):
    f = Path(run) / "results.csv"
    if not f.exists(): return None
    d = pd.read_csv(f); d.columns = [c.strip() for c in d.columns]
    m = "metrics/mAP50(B)"; tl = [c for c in d.columns if c.startswith("train/")]; vl = [c for c in d.columns if c.startswith("val/")]
    d["train_loss"] = d[tl].sum(axis=1); d["val_loss"] = d[vl].sum(axis=1)
    be = int(d[m].idxmax()); last = d.iloc[-1]; k = min(20, len(d) // 4 or 1)
    try:
        y = d[m].iloc[-k:].values.astype(float)
        slope = float(np.polyfit(np.arange(k, dtype=float), y, 1)[0]) if len(d) >= 8 and np.isfinite(y).all() else float("nan")
    except Exception:
        slope = float("nan")
    return dict(epochs_run=len(d), best_epoch=be + 1, best_val_mAP50=round(float(d[m].max()), 3), final_val_mAP50=round(float(last[m]), 3),
                train_loss_final=round(float(last.train_loss), 3), val_loss_final=round(float(last.val_loss), 3), val_over_train_loss=round(float(last.val_loss / last.train_loss), 2),
                val_loss_min_epoch=int(d.val_loss.idxmin()) + 1, val_loss_rise_after_min=round(float(last.val_loss - d.val_loss.min()), 3),
                late_mAP50_slope_per_epoch=round(slope, 5))


def main():
    rows = []; fig, axs = plt.subplots(1, 2, figsize=(11, 4))
    for name, run, sz, valdir, cid in MODELS:
        w = Path(run) / "weights" / "best.pt"
        if not w.exists(): print("skip (no weights):", name); continue
        sv = H.score_model(w, sz, valdir, cid)
        cs = np.arange(0.05, 0.9, 0.025); pr = [H.at_conf(sv, c) for c in cs]
        bf = cs[int(np.argmax([x[2] for x in pr]))]; p, r, f = H.at_conf(sv, bf)
        order = np.argsort(-sv["conf"]); tp = sv["tp50"][order]; ctp = np.cumsum(tp); cfp = np.cumsum(1 - tp)
        prec = ctp / np.maximum(ctp + cfp, 1); rec = ctp / max(1, sv["n_gt"])
        axs[0].plot(rec, prec, label=name); axs[1].plot(cs, [x[2] for x in pr], label=name)
        row = dict(model=name, val_photos=sv["n_photos"], val_boxes=sv["n_gt"], val_mAP50=round(sv["AP50"], 3), val_mAP50_95=round(sv["AP50_95"], 3),
                   best_F1_conf=round(float(bf), 3), P=round(p, 3), R=round(r, 3), F1=round(f, 3))
        c = curves(run)
        if c: row.update(c)
        rows.append(row); print(name, row, flush=True)
    axs[0].set_xlabel("recall"); axs[0].set_ylabel("precision"); axs[0].set_title("precision-recall (validation)"); axs[0].legend(fontsize=6); axs[0].grid(alpha=.3)
    axs[1].set_xlabel("confidence"); axs[1].set_ylabel("F1"); axs[1].set_title("F1 vs confidence (validation)"); axs[1].grid(alpha=.3)
    plt.tight_layout(); plt.savefig(OUT / "baseline_pr_f1_curves.png", dpi=130)
    df = pd.DataFrame(rows); df.to_csv(OUT / "baseline_model_diagnosis.csv", index=False)
    print(df.T.to_string()); (OUT / "baseline_model_diagnosis.md").write_text(df.T.to_string())


if __name__ == "__main__":
    main()
