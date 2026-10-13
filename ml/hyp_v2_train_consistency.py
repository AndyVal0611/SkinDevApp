#!/usr/bin/env python
"""hyp_v2_train_consistency.py - are the 808 rule-v2 corrected training photos consistent? (read-only)
  1. drift: the photos in the order you saved them, in 4 equal groups: boxes per photo, median box side, share of the photo covered, share of photos with 8+ boxes
  2. how much you changed the code-converted starting boxes (F1 at IoU 0.5 between the two) and what kind of change
  3. the same numbers for the test photos (V2_Test) and pass A, for comparison"""
import json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
import hyp_v2_transform as TR

B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing")
side = lambda b: (b[2] * b[3]) ** 0.5


def cover(bx):
    m = np.zeros((64, 64), np.uint8)
    for cx, cy, w, h in bx: m[int(max(0, cy - h / 2) * 64):int(min(1, cy + h / 2) * 64) + 1, int(max(0, cx - w / 2) * 64):int(min(1, cx + w / 2) * 64) + 1] = 1
    return m.mean()


def stats(rows):
    nb = [len(r) for r in rows]; sd = [side(b) for r in rows for b in r]
    return "%4d photos | %.1f boxes/photo | median side %.3f | covered %.0f%% | 8+ boxes: %.1f%% | no box: %.1f%%" % (
        len(rows), np.mean(nb), np.median(sd), 100 * np.mean([cover(r) for r in rows if r] or [0]), 100 * np.mean([n >= 8 for n in nb]), 100 * np.mean([n == 0 for n in nb]))


def main():
    D = B / "V2_Train"; ref = json.load(open(D / "_drafts/meta.json"))["drafted_at"]; mp = json.load(open(D / "name_map.json")); S = D / "Elaiza__hyperpigmentation_v2/labels"
    items = []
    for orig, new in mp.items():
        f = S / (new + ".txt")
        if f.exists() and f.stat().st_mtime > ref + 1: items.append((f.stat().st_mtime, orig, F.rd(f)))
    items.sort(); rows = [x[2] for x in items]
    print("Corrected training photos in the order you saved them:")
    for k, idx in enumerate(np.array_split(np.arange(len(rows)), 4)): print("  group %d: %s" % (k + 1, stats([rows[i] for i in idx])))
    print("  all:     %s" % stats(rows))
    # original (converted) boxes of the same photos
    HYP = F.P / "hyperpig_autolabel/hyperpig_only_v1/dataset"; split = json.load(open(F.E / "data/hyp_split.json")); own = set(split["own_train"]); conv = []
    dd = F.E / "data/hyp_drafts_v2"
    for t, orig, r in items:
        if orig in own: src = F.rd(HYP / "train/labels" / (Path(orig).stem + ".txt"))
        else: src = F.rd(dd / "labels" / (Path(orig).stem + ".txt"))
        conv.append(TR.to_v2(src, 1.0, 0.09))
    f05 = TR.f1(rows, conv, 0.5); f03 = TR.f1(rows, conv, 0.3)
    same = sum(1 for a, b in zip(rows, conv) if len(a) == len(b) and TR.f1([a], [b], 0.7) > 0.99)
    more = sum(1 for a, b in zip(rows, conv) if len(a) > len(b)); fewer = sum(1 for a, b in zip(rows, conv) if len(a) < len(b))
    print("\nYour corrections against the code-converted starting boxes: F1@0.5 %.3f, F1@0.3 %.3f" % (f05, f03))
    print("  photos left (almost) unchanged: %d | you added boxes: %d | you removed boxes: %d (of %d)" % (same, more, fewer, len(rows)))
    print("  boxes per photo: converted %.1f -> corrected %.1f | median side: converted %.3f -> corrected %.3f" % (np.mean([len(c) for c in conv]), np.mean([len(r) for r in rows]),
          np.median([side(b) for c in conv for b in c]), np.median([side(b) for r in rows for b in r])))
    print("\nFor comparison:")
    tl = sorted((F.E / "data/hyp_v2_test/labels").glob("*.txt")); print("  test photos (V2_Test, from scratch): %s" % stats([F.rd(f) for f in tl]))
    al = sorted((F.E / "data/hyp_v2_valA/labels").glob("*.txt")); print("  pass A (from scratch):               %s" % stats([F.rd(f) for f in al]))
    ol = sorted((F.E / "data/hyp_hand_val/labels").glob("*.txt")); print("  old rule, hand-boxed validation:     %s" % stats([F.rd(f) for f in ol]))


if __name__ == "__main__":
    main()
