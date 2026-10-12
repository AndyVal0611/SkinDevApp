#!/usr/bin/env python
"""hyp_fix_check.py - how far the hyperpigmentation consistency fix has got, and whether the fixed photos now follow one style (read-only)."""
import json
from pathlib import Path
import numpy as np

SPR = Path("/mnt/c/Users/Bautista/Documents/PrecisionSkin_DetectorOwn/hyperpigmentation_sprint")
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing")
def rd(p): return [tuple(map(float, l.split()[1:5])) for l in Path(p).read_text().splitlines() if len(l.split()) >= 5] if Path(p).exists() else []
def st(bx):
    s = [(w * h) ** 0.5 for _, _, w, h in bx]; return (len(bx), float(np.median(s)) if s else 0.0, max(s) if s else 0.0)

D = B / "Fix_Hyperpig_Consistency"; ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]
fs = sorted((D / "Elaiza__hyperpigmentation_fix/labels").glob("*.txt")); done = [f for f in fs if f.stat().st_mtime > ref + 1]
print("consistency fix: %d photos | saved %d | still to do %d" % (len(fs), len(done), len(fs) - len(done)))
if done:
    b = np.array([st(rd(SPR / "Elaiza__hyperpigmentation/labels" / f.name)) for f in done]); a = np.array([st(rd(f)) for f in done])
    print("boxes per photo: before %.1f -> after %.1f | median box side: before %.3f -> after %.3f | largest box: before %.2f -> after %.2f" % (b[:, 0].mean(), a[:, 0].mean(), np.median(b[:, 1]), np.median(a[:, 1]), np.median(b[:, 2]), np.median(a[:, 2])))
    print("still over-split (10+ boxes): %d | still over-merged (a box over 40%% of the side): %d | marked X (no patch): %d" % (int((a[:, 0] >= 10).sum()), int((a[:, 2] > 0.40).sum()), int((a[:, 0] == 0).sum())))
    print("target style (the middle of your sprint, quartiles 2-3): about 3.5-3.9 boxes per photo, median side 0.09-0.10")
dr = B / "Draft_Hyperpig/Elaiza__hyperpigmentation_draft/labels"; dref = json.load(open(B / "Draft_Hyperpig/_drafts/meta.json"))["drafted_at"]
rev = [f for f in dr.glob("*.txt") if f.stat().st_mtime > dref + 1]
print("\ndrafts reviewed so far: %d of 300" % len(rev))
if rev:
    sd = np.array([st(rd(f)) for f in rev if rd(f)])
    print("reviewed drafts: %.1f boxes per photo | median box side %.3f | photos with a box over 40%%: %d" % (sd[:, 0].mean(), np.median(sd[:, 1]), int((sd[:, 2] > 0.40).sum())))
