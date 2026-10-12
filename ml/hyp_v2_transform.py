#!/usr/bin/env python
"""
hyp_v2_transform.py - can the old boxes be turned into rule-v2 boxes automatically?  (read-only check on the 30 photos you boxed under rule v2)
rule v2 in code: boxes that touch or are closer than (gap factor x the smaller box's short side) become ONE box (their union); boxes smaller than a minimum side are dropped.
The two parameters are chosen on pass A and checked on pass B (a second look), so the check is not fitted to itself.
Also shows how well the current single model and the 3-model average agree with passes A and B.   Run in WSL.
"""
import json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
import hyp_ensemble_tta as T
from ultralytics import YOLO

B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing")


def xy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2


def to_v2(boxes, gap_f, min_side):
    bx = [list(xy(b)) for b in boxes]
    changed = True
    while changed:
        changed = False
        for i in range(len(bx)):
            for j in range(i + 1, len(bx)):
                a, c = bx[i], bx[j]
                dx = max(0, c[0] - a[2], a[0] - c[2]); dy = max(0, c[1] - a[3], a[1] - c[3]); gap = max(dx, dy)
                sm = min(min(a[2] - a[0], a[3] - a[1]), min(c[2] - c[0], c[3] - c[1]))
                if gap <= gap_f * sm:
                    bx[i] = [min(a[0], c[0]), min(a[1], c[1]), max(a[2], c[2]), max(a[3], c[3])]; del bx[j]; changed = True; break
            if changed: break
    out = [((x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0) for x0, y0, x1, y1 in bx]
    return [b for b in out if (b[2] * b[3]) ** 0.5 >= min_side]


def load(tag):
    D = B / ("Rebox_V2_Pass" + tag); ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]; mp = json.load(open(D / "name_map.json")); out = {}
    for orig, new in mp.items():
        f = D / "Elaiza__hyperpigmentation_v2/labels" / (new + ".txt")
        if f.exists() and f.stat().st_mtime > ref: out[Path(orig).stem] = F.rd(f)
    return out


def f1(A, Bx, thr=0.5):
    tp = fp = fn = 0
    for a, b in zip(A, Bx):
        used = set(); t = 0
        for x in a:
            best, bj = 0, -1
            for j, y in enumerate(b):
                if j in used: continue
                v = F.iou(x, y)
                if v > best: best, bj = v, j
            if bj >= 0 and best >= thr: used.add(bj); t += 1
        tp += t; fp += len(a) - t; fn += len(b) - t
    p, r = tp / max(1, tp + fp), tp / max(1, tp + fn); return 2 * p * r / max(1e-9, p + r)


def main():
    a, b = load("A"), load("B"); keys = sorted(set(a) & set(b)); A = [a[k] for k in keys]; Bb = [b[k] for k in keys]
    orig = [F.rd(F.VAL / "labels" / (k + ".txt")) for k in keys]
    print("photos with both v2 passes: %d | A vs B F1@0.5 %.3f (upper bound: same session)" % (len(keys), f1(A, Bb)))
    print("\nold boxes as they are            vs A %.3f | vs B %.3f" % (f1(orig, A), f1(orig, Bb)))
    best = None
    print("\nautomatic v2 conversion of the old boxes (gap factor, minimum side) -> F1@0.5 vs pass A / vs pass B / boxes per photo")
    for g in (0.5, 1.0, 1.5, 2.0, 3.0):
        for ms in (0.0, 0.05, 0.07, 0.09):
            conv = [to_v2(o, g, ms) for o in orig]; fa, fb = f1(conv, A), f1(conv, Bb)
            print("  gap %.1f  min %.2f -> %.3f / %.3f / %.1f" % (g, ms, fa, fb, np.mean([len(c) for c in conv])))
            if best is None or fa > best[0]: best = (fa, fb, g, ms)
    print("\nchosen on pass A: gap %.1f, min side %.2f -> pass A %.3f, pass B (not used for choosing) %.3f" % (best[2], best[3], best[0], best[1]))
    # models
    R = F.E / "runs"; paths = [R / "H10_v2_lowlr/weights/best.pt", R / "H10_s7/weights/best.pt", R / "H10_s123/weights/best.pt"]
    ms_ = [YOLO(str(p)) for p in paths]; img = {k: next(iter((F.VAL / "images").glob(k + ".*"))) for k in keys}
    P = []
    for m in ms_:
        d = {}
        for k in keys:
            r = m.predict(str(img[k]), imgsz=800, conf=0.001, iou=0.6, max_det=300, verbose=False, device=0)[0]
            d[k] = [(tuple(map(float, bb)), float(c)) for bb, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy())] if len(r.boxes) else []
        P.append(d)
    ens = {k: T.fuse([p[k] for p in P]) for k in keys}
    print("\nthe current model against the rule-v2 boxes (F1@0.5):")
    for name, d, thr in (("single model (seed 42), conf 0.20", P[0], 0.20), ("3-model average, conf 0.225", ens, 0.225)):
        pr = [[bb for bb, c in d[k] if c >= thr] for k in keys]
        print("  %-36s vs A %.3f | vs B %.3f | vs old boxes %.3f" % (name, f1(pr, A), f1(pr, Bb), f1(pr, orig)))


if __name__ == "__main__":
    main()
