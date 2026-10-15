#!/usr/bin/env python
"""
acne_annotation_audit.py - READ-ONLY audit of the acne box annotations (nothing in the dataset, labels or models is changed).

  A. dataset statistics per split (boxes per photo, box size in % of the photo and in pixels, aspect ratio, tiny / large boxes)
  B. annotation-style flags per photo: oversized boxes (cluster-like), boxes that swallow other boxes, duplicates / heavy overlaps
  C. possible MISSING lesions: confident model boxes with no ground-truth box near them (train + val only, never the test sets)
  D. why is mAP@0.5:0.95 low: localisation analysis of the final model on the VALIDATION photos only
       - precision / recall / F1 at IoU 0.5 ... 0.9
       - IoU of the matched boxes, "found but not tight enough" share, duplicates, size/centre bias
       - recall by lesion size and by photo density
       - label-noise ceiling: what IoU a tiny box gets if a second annotator is off by 1-3 pixels
  E. outputs: review_queue.csv (for YOU to check by eye), overlay pictures of the top photos, AUDIT_REPORT.md, tables

Output folder: Acne_Boxing/_annotation_audit/   Run in WSL:  python acne_annotation_audit.py
Test and independent sets are described (counts, sizes) but never used to pick anything.
"""
import csv, json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

DS = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/acne_dataset_v4")
INDEP = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/Independent_Final_Set/boxed_independent")
W = Path.home() / "PrecisionSkinV1" / "runs" / "lesion_acne_boxed_only" / "models" / "seed_123" / "weights" / "best.pt"
OUT = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/_annotation_audit"); (OUT / "overlays").mkdir(parents=True, exist_ok=True)
CONF = 0.242
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def read(p):
    return [tuple(map(float, l.split()[1:5])) for l in Path(p).read_text().splitlines() if len(l.split()) >= 5] if Path(p).exists() else []

def xyxy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2

def iou(a, b):
    a, b = xyxy(a), xyxy(b); iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    return i / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i + 1e-12)

def inside(c, b):
    x1, y1, x2, y2 = xyxy(b); return x1 <= c[0] <= x2 and y1 <= c[1] <= y2

def side(b): return (b[2] * b[3]) ** 0.5

def photos(d):
    return sorted(p for p in (Path(d) / "images").glob("*") if p.suffix.lower() in EXT)


# ----------------------------------------------------------------------------- A. statistics
def stats_table(split, d):
    rows, sides, ar, px, per, tiny, big = [], [], [], [], [], 0, 0
    for p in photos(d):
        bx = read(Path(d) / "labels" / (p.stem + ".txt")); w, h = Image.open(p).size
        per.append(len(bx))
        for b in bx:
            s = side(b); sides.append(s); ar.append(b[2] * w / max(1e-9, b[3] * h)); px.append(s * min(w, h))
    sides, ar, px, per = map(np.array, (sides, ar, px, per))
    return dict(split=split, photos=len(per), boxes=int(per.sum()), boxes_per_photo=round(per.mean(), 1), median_boxes=int(np.median(per)), max_boxes=int(per.max()),
                side_p10=round(float(np.percentile(sides, 10)), 3), side_median=round(float(np.median(sides)), 3), side_p90=round(float(np.percentile(sides, 90)), 3), side_max=round(float(sides.max()), 3),
                median_px=round(float(np.median(px)), 1), px_p10=round(float(np.percentile(px, 10)), 1), under_8px_pct=round(100 * float((px < 8).mean()), 1),
                over_0p10_pct=round(100 * float((sides > 0.10).mean()), 1), aspect_median=round(float(np.median(ar)), 2), aspect_gt2_or_lt05_pct=round(100 * float(((ar > 2) | (ar < 0.5)).mean()), 1))


# ----------------------------------------------------------------------------- B. style flags
def photo_flags(gt):
    """returns list of (issue, box_index, detail)"""
    out = []
    if not gt: return out
    m = float(np.median([side(b) for b in gt]))
    for i, b in enumerate(gt):
        s = side(b)
        if s > max(2.5 * m, 0.08): out.append(("OVERSIZED", i, "side %.3f vs photo median %.3f" % (s, m)))
        sw = [j for j, c in enumerate(gt) if j != i and inside(c[:2], b) and c[2] * c[3] < 0.5 * b[2] * b[3]]
        if len(sw) >= 2: out.append(("SWALLOWS_BOXES", i, "contains the centre of %d smaller boxes (cluster box on top of individual boxes?)" % len(sw)))
        for j in range(i + 1, len(gt)):
            v = iou(b, gt[j])
            if v > 0.6: out.append(("DUPLICATE", i, "overlaps box %d with IoU %.2f" % (j, v)))
            elif v > 0.3: out.append(("HEAVY_OVERLAP", i, "overlaps box %d with IoU %.2f" % (j, v)))
    return out


# ----------------------------------------------------------------------------- model predictions
def load_model():
    from ultralytics import YOLO
    return YOLO(str(W))

def predict(m, img, conf=0.05):
    r = m.predict(str(img), imgsz=800, conf=conf, iou=0.45, max_det=300, verbose=False, device=0)[0]
    if not len(r.boxes): return []
    return [(tuple(map(float, b)), float(c)) for b, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy())]

def match(gt, pr, thr):
    """greedy by confidence; returns tp pairs [(gi, pi, iou)], fp idx, fn idx"""
    used, tp = set(), []
    for pi in sorted(range(len(pr)), key=lambda k: -pr[k][1]):
        best, bj = 0.0, -1
        for gi, g in enumerate(gt):
            if gi in used: continue
            v = iou(g, pr[pi][0])
            if v > best: best, bj = v, gi
        if bj >= 0 and best >= thr: used.add(bj); tp.append((bj, pi, best))
    fp = [pi for pi in range(len(pr)) if pi not in {t[1] for t in tp}]; fn = [gi for gi in range(len(gt)) if gi not in used]
    return tp, fp, fn


def draw(img_path, gt, pr, flagged, dest, title):
    im = Image.open(img_path).convert("RGB"); w0, h0 = im.size; sc = 900 / max(w0, h0); im = im.resize((int(w0 * sc), int(h0 * sc)), Image.LANCZOS); w, h = im.size
    d = ImageDraw.Draw(im)
    for i, b in enumerate(gt):
        x1, y1, x2, y2 = xyxy(b); col = (255, 40, 40) if i in flagged else (40, 220, 60)
        d.rectangle([x1 * w, y1 * h, x2 * w, y2 * h], outline=col, width=3 if i in flagged else 2)
    for b, c in pr:
        x1, y1, x2, y2 = xyxy(b); d.rectangle([x1 * w, y1 * h, x2 * w, y2 * h], outline=(60, 120, 255), width=1)
    d.rectangle([0, 0, w, 18], fill=(0, 0, 0)); d.text((4, 3), title + "   green = your box | red = flagged | blue = model", fill=(255, 255, 255))
    im.save(dest, quality=88)


def main():
    lines = []; P = lines.append
    # ---- A
    st = [stats_table(s, DS / s) for s in ("train", "val", "test")]
    if INDEP.exists(): st.append(stats_table("independent", INDEP))
    with open(OUT / "dataset_statistics.csv", "w", newline="") as fh:
        wr = csv.DictWriter(fh, fieldnames=list(st[0].keys())); wr.writeheader(); wr.writerows(st)
    # ---- B + C on train and val
    m = load_model(); queue = []; per_photo = {}
    for split in ("train", "val"):
        for p in photos(DS / split):
            gt = read(DS / split / "labels" / (p.stem + ".txt")); fl = photo_flags(gt); pr = predict(m, p) if True else []
            conf_pr = [(b, c) for b, c in pr if c >= 0.5]
            miss = [b for b, c in conf_pr if not gt or max(iou(b, g) for g in gt) < 0.10]
            per_photo[(split, p.name)] = (gt, pr, fl, miss)
            by = {}
            for iss, i, det in fl: by.setdefault(iss, []).append((i, det))
            for iss, items in by.items():
                queue.append(dict(split=split, image=p.name, issue=iss, count=len(items), detail=items[0][1], boxes=len(gt), priority=len(items) * (3 if iss in ("DUPLICATE", "SWALLOWS_BOXES") else 2 if iss == "OVERSIZED" else 1)))
            if len(miss) >= 2:
                queue.append(dict(split=split, image=p.name, issue="POSSIBLE_MISSING", count=len(miss), detail="%d confident model boxes (conf >= 0.5) with no box of yours nearby" % len(miss), boxes=len(gt), priority=len(miss)))
    queue.sort(key=lambda r: -r["priority"])
    with open(OUT / "review_queue.csv", "w", newline="", encoding="utf-8") as fh:
        wr = csv.DictWriter(fh, fieldnames=["split", "image", "issue", "count", "detail", "boxes", "priority"]); wr.writeheader(); wr.writerows(queue)
    top, seen = [], set()
    for q in queue:
        k = (q["split"], q["image"])
        if k in seen: continue
        seen.add(k); top.append(q)
        if len(top) >= 40: break
    for q in top:
        gt, pr, fl, miss = per_photo[(q["split"], q["image"])]
        flagged = {i for iss, i, _ in fl if iss in ("OVERSIZED", "SWALLOWS_BOXES", "DUPLICATE")}
        draw(DS / q["split"] / "images" / q["image"], gt, [(b, c) for b, c in pr if c >= CONF], flagged, OUT / "overlays" / ("%s__%s__%s.jpg" % (q["issue"], q["split"], Path(q["image"]).stem[:40])), "%s %s" % (q["issue"], q["image"][:50]))
    cnt = {}
    for q in queue: cnt.setdefault(q["issue"], set()).add((q["split"], q["image"]))
    n_ph = sum(len(photos(DS / s)) for s in ("train", "val"))

    # ---- D localisation analysis on VAL only
    sub = {k: v for k, v in per_photo.items() if k[0] == "val"}
    res = {}
    for thr in (0.5, 0.6, 0.7, 0.75, 0.8, 0.9):
        tp = fp = fn = 0
        for (gt, pr, _, _) in sub.values():
            pc = [(b, c) for b, c in pr if c >= CONF]; t, f, n = match(gt, pc, thr); tp += len(t); fp += len(f); fn += len(n)
        p_, r_ = tp / max(1, tp + fp), tp / max(1, tp + fn); res[thr] = (p_, r_, 2 * p_ * r_ / max(1e-9, p_ + r_))
    ious, near, dup, dsz, dcx = [], 0, 0, [], []
    nsmall = dict(small=[0, 0, []], medium=[0, 0, []], large=[0, 0, []]); dens = {"sparse (<=6 boxes)": [0, 0], "medium (7-14)": [0, 0], "dense (15+)": [0, 0]}
    allgt = [side(b) for (gt, *_) in sub.values() for b in gt]; q1, q2 = np.percentile(allgt, 33), np.percentile(allgt, 67)
    npred = 0
    for (gt, pr, _, _) in sub.values():
        pc = [(b, c) for b, c in pr if c >= CONF]; npred += len(pc); t, f, n = match(gt, pc, 0.5); t3, _, _ = match(gt, pc, 0.1)
        matched = {a: (pi, v) for a, pi, v in t3}
        for gi, g in enumerate(gt):
            s = side(g); k = "small" if s <= q1 else "medium" if s <= q2 else "large"; nsmall[k][1] += 1
            if gi in matched:
                pi, v = matched[gi]; ious.append(v)
                if v >= 0.5: nsmall[k][0] += 1; nsmall[k][2].append(v)
                else: near += 1
                pb = pc[pi][0]; dsz.append(side(pb) / max(1e-9, s)); dcx.append((((pb[0] - g[0]) ** 2 + (pb[1] - g[1]) ** 2) ** 0.5) / max(1e-9, s))
        for i in range(len(pc)):
            for j in range(i + 1, len(pc)):
                if iou(pc[i][0], pc[j][0]) > 0.5: dup += 1
        k = "sparse (<=6 boxes)" if len(gt) <= 6 else "medium (7-14)" if len(gt) <= 14 else "dense (15+)"; dens[k][1] += len(gt); dens[k][0] += len(t)
    ious = np.array(ious)
    # label-noise ceiling: a square box of the median pixel size shifted by 1 / 2 / 3 px
    px_med = st[0]["median_px"]
    ceil = {d_: iou((0.5, 0.5, px_med / 1000, px_med / 1000), (0.5 + d_ / 1000, 0.5, px_med / 1000, px_med / 1000)) for d_ in (1, 2, 3)}

    P("# Acne annotation audit (read-only; nothing in the dataset, labels or models was changed)\n")
    P("Model audited: `%s` at confidence %.3f. Localisation analysis uses the %d VALIDATION photos only; the test and independent sets are only counted.\n" % (W, CONF, len(sub)))
    P("## A. Dataset statistics (confirmed from the files)\n"); P("| " + " | ".join(st[0].keys()) + " |"); P("|" + "---|" * len(st[0]))
    for r in st: P("| " + " | ".join(str(v) for v in r.values()) + " |")
    P("\nBox size is sqrt(width x height) as a share of the photo; px = that share x the photo's short side.\n")
    P("## B. Annotation-style flags on train + val (%d photos)\n" % n_ph)
    P("| issue | photos | share |"); P("|---|---|---|")
    for k, v in sorted(cnt.items(), key=lambda kv: -len(kv[1])): P("| %s | %d | %.1f %% |" % (k, len(v), 100 * len(v) / n_ph))
    P("\nA flag is a REASON TO LOOK, not an error: a big box can be a real cluster. Rules: OVERSIZED = side > 2.5x the photo's median box and > 0.08; SWALLOWS_BOXES = a box whose area covers the centres of 2+ smaller boxes; DUPLICATE = IoU > 0.6 between two of your boxes; HEAVY_OVERLAP = IoU 0.3-0.6; POSSIBLE_MISSING = 2+ model boxes at conf >= 0.5 with no box of yours within IoU 0.10.\n")
    P("Review queue: `review_queue.csv` (%d rows). Pictures of the 40 highest-priority photos: `overlays/` (green = your box, red = flagged, blue = model).\n" % len(queue))
    P("## C. Why is mAP@0.5:0.95 low? (validation photos, final model)\n")
    P("| IoU needed to count as found | precision | recall | F1 |"); P("|---|---|---|---|")
    for thr, (p_, r_, f_) in res.items(): P("| %.2f | %.3f | %.3f | %.3f |" % (thr, p_, r_, f_))
    P("\n- Matched boxes (IoU >= 0.10 with a box of yours): median IoU **%.2f**, 25 %% below %.2f, 75 %% below %.2f." % (np.median(ious), np.percentile(ious, 25), np.percentile(ious, 75)))
    P("- Found but not tight enough (matched at IoU 0.10-0.50): **%d** boxes vs %d found at IoU >= 0.5." % (near, int((ious >= 0.5).sum())))
    P("- Duplicate model boxes (two predictions with IoU > 0.5): %d of %d predictions." % (dup, npred))
    P("- Model box size / your box size: median **%.2f** (below 1 = the model boxes are smaller); centre offset: median **%.2f** of the box side." % (np.median(dsz), np.median(dcx)))
    P("\nRecall at IoU 0.5 by lesion size (terciles of your box sizes, small <= %.3f, large > %.3f of the photo):\n" % (q1, q2)); P("| size | boxes | found | recall | median IoU of the found |"); P("|---|---|---|---|---|")
    for k, (a, b, iv) in nsmall.items(): P("| %s | %d | %d | %.3f | %.2f |" % (k, b, a, a / max(1, b), np.median(iv) if iv else 0))
    P("\nRecall at IoU 0.5 by photo density:\n"); P("| density | boxes | recall |"); P("|---|---|---|")
    for k, (a, b) in dens.items(): P("| %s | %d | %.3f |" % (k, b, a / max(1, b)))
    P("\n**Label-noise ceiling (computed, not a model result).** A box of the median training size (%.0f px) drawn %d / %d / %d px off by a second annotator would score IoU %.2f / %.2f / %.2f against the first. Boxes this small cannot reach high IoU even when both annotators agree on the spot, which caps mAP@0.5:0.95." % (px_med, 1, 2, 3, ceil[1], ceil[2], ceil[3]))
    P("\n## D. What is confirmed and what is still a hypothesis\n")
    P("See the numbers above. Anything about *why* a gap exists (for example 'inconsistent cluster boxes lower the score') stays a hypothesis until the controlled experiment in the plan has been run on the validation set.")
    (OUT / "AUDIT_REPORT.md").write_text("\n".join(lines), encoding="utf-8"); print("\n".join(lines))


if __name__ == "__main__":
    main()
