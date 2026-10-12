#!/usr/bin/env python
"""hyp_reliability_v2.py - rule v2 passes. Usage: python hyp_reliability_v2.py [A|AB]
A  : pass A against the original boxes and against the first re-box (v1 rule); box counts and sizes.
AB : pass A against pass B (the real reliability test of rule v2) - run after pass B."""
import json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing")
side = lambda b: (b[2] * b[3]) ** 0.5

def load(tag):
    D = B / ("Rebox_V2_Pass" + tag); ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]; mp = json.load(open(D / "name_map.json")); L = D / "Elaiza__hyperpigmentation_v2/labels"
    out = {}; saved = 0
    for orig, new in mp.items():
        f = L / (new + ".txt")
        if f.exists() and f.stat().st_mtime > ref: out[Path(orig).stem] = F.rd(f); saved += 1
    return out, saved

def f1(A, Bx, thr):
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

def table(title, A, Bx):
    print("%-40s %s" % (title, " ".join("%6.3f" % f1(A, Bx, t) for t in (0.2, 0.3, 0.5, 0.7))))

mode = sys.argv[1] if len(sys.argv) > 1 else "A"
a, na = load("A"); print("pass A saved: %d of 30" % na)
keys = sorted(a)
orig = [F.rd(F.VAL / "labels" / (k + ".txt")) for k in keys]
v1 = [F.rd(B / "Reliability_Rebox/Elaiza__hyperpigmentation_rebox/labels" / (k + ".txt")) for k in keys]
PA = [a[k] for k in keys]
print("boxes per photo: original %.1f | first re-box (rule v1) %.1f | pass A (rule v2) %.1f" % (np.mean(list(map(len, orig))), np.mean(list(map(len, v1))), np.mean(list(map(len, PA)))))
print("median box side: original %.3f | rule v1 re-box %.3f | pass A %.3f" % tuple(np.median([side(b) for L in X for b in L]) for X in (orig, v1, PA)))
print("\nF1 (precision/recall balanced) at IoU:            0.2    0.3    0.5    0.7")
table("original vs first re-box (rule v1)", v1, orig)
table("pass A vs original", PA, orig)
table("pass A vs first re-box (rule v1)", PA, v1)
if mode == "AC":
    c, nc = load("C"); print("\npass C saved: %d of 30" % nc); ks = sorted(set(a) & set(c)); A2 = [a[k] for k in ks]; C2 = [c[k] for k in ks]
    print("boxes per photo: pass A %.1f | pass C %.1f" % (np.mean(list(map(len, A2))), np.mean(list(map(len, C2)))))
    table("PASS A vs PASS C (a day apart)", C2, A2); print("\nrule v1 reference at IoU 0.5: 0.277")
if mode == "AB":
    b, nb = load("B"); print("\npass B saved: %d of 30" % nb); ks = sorted(set(a) & set(b)); A2 = [a[k] for k in ks]; B2 = [b[k] for k in ks]
    print("boxes per photo: pass A %.1f | pass B %.1f" % (np.mean(list(map(len, A2))), np.mean(list(map(len, B2)))))
    table("PASS A vs PASS B (rule v2 reliability)", B2, A2)
    print("\nrule v1 reference at IoU 0.5: 0.277")
