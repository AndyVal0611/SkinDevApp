#!/usr/bin/env python
"""hyp_ensemble_sets.py - which model sets make the best box average? (VALIDATION photos only; four sets fixed in advance, so the choice is not a search)
  S3  = the three H10 seeds                      (reference: 0.357)
  S4  = S3 + H8  (same data, default learning rate)
  S5  = S3 + H9 (960 px) + H11 (YOLOv8m)         (more different models)
  S6  = S3 + H8 + H9 + H11
Output: ~/PrecisionSkinV1/hyp_final_eval/HYP_ENSEMBLE_SETS.md"""
import sys
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

R = F.E / "runs"
M = {"H10_s42": (R / "H10_v2_lowlr/weights/best.pt", 800), "H10_s7": (R / "H10_s7/weights/best.pt", 800), "H10_s123": (R / "H10_s123/weights/best.pt", 800),
     "H8": (R / "H8_drafts_v2/weights/best.pt", 800), "H9_960": (R / "H9_v2_960/weights/best.pt", 960), "H11_m800": (R / "H11_v2_m800/weights/best.pt", 800)}
SETS = {"S3 (3 x H10)": ["H10_s42", "H10_s7", "H10_s123"], "S4 (S3 + H8)": ["H10_s42", "H10_s7", "H10_s123", "H8"],
        "S5 (S3 + 960 px + YOLOv8m)": ["H10_s42", "H10_s7", "H10_s123", "H9_960", "H11_m800"], "S6 (all six)": list(M)}


def preds(name, d):
    wt, sz = M[name]; m = YOLO(str(wt)); out = {}
    imgs = sorted(p for p in (d / "images").glob("*") if p.suffix.lower() in F.EXT)
    for s in range(0, len(imgs), 8):
        ch = imgs[s:s + 8]
        for p, r in zip(ch, m.predict([str(x) for x in ch], imgsz=sz, conf=0.001, iou=0.6, max_det=300, verbose=False, device=0)):
            out[p.name] = [(tuple(map(float, b)), float(c)) for b, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy())] if len(r.boxes) else []
    return out


if __name__ == "__main__":
    P = {n: preds(n, F.VAL) for n in M}; L = ["# Hyperpigmentation: which models to average (validation photos)\n", "| set | models | mAP@0.5 | best F1 | threshold | precision | recall |", "|---|---|---|---|---|---|---|"]
    for name, ms in SETS.items():
        fused = {k: T.fuse([P[m][k] for m in ms]) for k in P[ms[0]]}; ap, (f, c, p, r) = T.evaluate(fused, F.VAL)
        L.append("| %s | %d | **%.3f** | %.3f | %.3f | %.3f | %.3f |" % (name, len(ms), ap, f, c, p, r))
    for n in M: L.append("| single: %s | 1 | %.3f | | | | |" % (n, T.evaluate(P[n], F.VAL)[0]))
    (F.OUT / "HYP_ENSEMBLE_SETS.md").write_text("\n".join(L), encoding="utf-8"); print("\n".join(L))
