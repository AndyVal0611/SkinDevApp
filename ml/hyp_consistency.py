#!/usr/bin/env python
"""hyp_consistency.py - is the hyperpigmentation boxing consistent? (read-only)
Compares box style across: your original sprint boxes (by time quartile = drift), the drafts you reviewed, the second-look photos, the independent set, and the hand-boxed test photos."""
import json, os, sys
from pathlib import Path
import numpy as np

SPR = Path("/mnt/c/Users/Bautista/Documents/PrecisionSkin_DetectorOwn/hyperpigmentation_sprint")
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing")
HT = Path.home() / "PrecisionSkinV1/hyperpig_autolabel/hyperpig_only_v1/dataset/hand_test/labels"


def rd(p):
    return [tuple(map(float, l.split()[1:5])) for l in Path(p).read_text().splitlines() if len(l.split()) >= 5] if Path(p).exists() else []


def iou(a, b):
    def xy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2
    a, b = xy(a), xy(b); iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    return i / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i + 1e-9)


def stats(files):
    nb, sides, asp, cover, ov, border, per = 0, [], [], [], 0, 0, []
    for f in files:
        bx = rd(f); per.append(len(bx)); nb += len(bx); m = np.zeros((96, 96), np.uint8)
        for cx, cy, w, h in bx:
            sides.append((w * h) ** 0.5); asp.append(w / h if h else 1)
            x0, x1, y0, y1 = cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2
            border += (x0 <= 0.005 or y0 <= 0.005 or x1 >= 0.995 or y1 >= 0.995); m[int(max(0, y0) * 96):int(min(1, y1) * 96) + 1, int(max(0, x0) * 96):int(min(1, x1) * 96) + 1] = 1
        ov += sum(1 for i in range(len(bx)) for j in range(i + 1, len(bx)) if iou(bx[i], bx[j]) > 0.3); cover.append(m.mean())
    s = np.array(sides) if sides else np.zeros(1)
    return dict(photos=len(files), boxes=nb, per_photo=round(nb / max(1, len(files)), 1), side_p10=round(float(np.percentile(s, 10)), 3), side_med=round(float(np.median(s)), 3), side_p90=round(float(np.percentile(s, 90)), 3),
                cover_pct=round(100 * float(np.mean(cover)) if cover else 0, 1), tiny_pct=round(100 * float((s < 0.02).mean()), 1), huge_pct=round(100 * float((s > 0.30).mean()), 1),
                asp_med=round(float(np.median(asp)) if asp else 0, 2), overlap_pct=round(100 * ov / max(1, nb), 1), border_pct=round(100 * border / max(1, nb), 1))


rows = {}
meta = json.load(open(SPR / "_drafts/meta.json"))["drafted_at"]
sp = sorted(((f.stat().st_mtime, f) for f in (SPR / "Elaiza__hyperpigmentation/labels").glob("*.txt") if f.stat().st_mtime > meta + 1 and rd(f)))
q = np.array_split(np.arange(len(sp)), 4)
for k, idx in enumerate(q): rows["sprint boxes, time quartile %d (oldest=1)" % (k + 1)] = stats([sp[i][1] for i in idx])
rows["sprint boxes, all"] = stats([f for _, f in sp])
rows["hand-boxed TEST photos"] = stats(sorted(HT.glob("*.txt")))
extra = {}
for name, sub in (("drafts you reviewed (final)", "Draft_Hyperpig/Elaiza__hyperpigmentation_draft/labels"), ("second-look photos (final)", "Fix_Hyperpig_Boxes/Elaiza__hyperpigmentation_fix/labels"), ("independent set (final)", "Independent_Final_Set/Elaiza__hyperpigmentation_independent/labels")):
    d = B / sub
    if not d.exists(): continue
    ref = json.load(open(d.parent.parent / "_drafts/meta.json"))["drafted_at"]; rev = [f for f in d.glob("*.txt") if f.stat().st_mtime > ref + 1 and rd(f)]
    if rev: rows[name] = stats(rev); extra[name] = rev
print("%-44s %6s %6s %5s %6s %6s %6s %6s %5s %5s %5s %5s %5s" % ("group", "photos", "boxes", "b/ph", "p10", "median", "p90", "cover%", "tiny%", "huge%", "asp", "ovl%", "bord%"))
for k, v in rows.items(): print("%-44s %6d %6d %5.1f %6.3f %6.3f %6.3f %6.1f %5.1f %5.1f %5.2f %5.1f %5.1f" % (k, v["photos"], v["boxes"], v["per_photo"], v["side_p10"], v["side_med"], v["side_p90"], v["cover_pct"], v["tiny_pct"], v["huge_pct"], v["asp_med"], v["overlap_pct"], v["border_pct"]))
# how the reviews changed the drafts / the originals
for name, drafts_dir in (("drafts", B / "Draft_Hyperpig/drafts_original"),):
    if name in ("drafts",) and "drafts you reviewed (final)" in extra:
        kept = dele = add = same = 0
        for f in extra["drafts you reviewed (final)"]:
            o = rd(drafts_dir / f.name); fin = rd(f); k = sum(1 for b in o if any(iou(b, g) > 0.5 for g in fin)); kept += k; dele += len(o) - k; add += sum(1 for g in fin if not any(iou(g, b) > 0.5 for b in o)); same += (len(o) == k and len(fin) == k)
        print("\ndrafts reviewed: drafted boxes %d | kept %d (%.0f%%) | deleted %d | you added %d | photos left exactly as drafted %d of %d" % (kept + dele, kept, 100 * kept / max(1, kept + dele), dele, add, same, len(extra["drafts you reviewed (final)"])))
if "second-look photos (final)" in extra:
    orig = SPR / "Elaiza__hyperpigmentation/labels"; add = rem = 0
    for f in extra["second-look photos (final)"]:
        o = rd(orig / f.name); fin = rd(f); add += sum(1 for g in fin if not any(iou(g, b) > 0.5 for b in o)); rem += sum(1 for b in o if not any(iou(g, b) > 0.5 for g in fin))
    print("second look: boxes you added %d | your original boxes you removed %d (over %d photos)" % (add, rem, len(extra["second-look photos (final)"])))
