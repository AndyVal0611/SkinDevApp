#!/usr/bin/env python
"""
acne_predraft.py - pre-draw acne boxes so you only DELETE wrong ones and ADD missed ones (class 0 = acne, YOLO format).

  step 1  train  : learns YOUR box style from the acne photos you boxed (training photos only; the hand-boxed TEST photos are never used).
                   --tight keeps only photos whose boxes are not loose (median box size <= --tight-max), so drift in later sessions is not learned.
                   It reports recall/precision on 12 % of your photos that were held out, at several confidence levels.
  step 2  draft  : writes drafts for your UNREVIEWED training photos into  Draft_Acne/  (a copy; your own labels are never touched).
                   Uses a LOW confidence (default 0.10) on purpose: it over-draws, so deleting is the main job and missed lesions are rare.
                   The 239-photo TEST set gets NO drafts: you box those from scratch.
  step 3  review : open Draft_Acne/START_REVIEW_DRAFTS.bat. Every photo you save lands in Draft_Acne/boxed_Acne_drafts (images + labels).
  step 4  stats  : counts, for the photos you reviewed, how many drafted boxes you kept / deleted / added (the real reliability of the drafts).

Run in WSL (.venv_det):   python acne_predraft.py train   |   python acne_predraft.py draft   |   python acne_predraft.py stats
Options: --tight-max 0.065  --conf 0.10  --epochs 60  --imgsz 800  --model yolov8s.pt  --limit N (draft only N photos)  --root <Acne_Boxing folder>
"""
import argparse, csv, json, os, random, shutil, sys, time
from pathlib import Path
import numpy as np
import cv2
import pandas as pd

IMG_EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def cfg_from_args(a):
    root = Path(a.root)
    return dict(root=root, imgs=root / "Elaiza__acne" / "images", labs=root / "Elaiza__acne" / "labels",
                work=Path(a.work), draft=root / "Draft_Acne", tool=Path(a.tool))


def read_boxes(p):
    out = []
    if Path(p).exists():
        for ln in Path(p).read_text().splitlines():
            t = ln.split()
            if len(t) >= 5:
                cx, cy, w, h = map(float, t[1:5])
                if w > 0.003 and h > 0.003:
                    out.append((cx, cy, w, h))
    return out


def iou(a, b):
    ax0, ay0, ax1, ay1 = a[0] - a[2] / 2, a[1] - a[3] / 2, a[0] + a[2] / 2, a[1] + a[3] / 2
    bx0, by0, bx1, by1 = b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2
    iw, ih = max(0, min(ax1, bx1) - max(ax0, bx0)), max(0, min(ay1, by1) - max(ay0, by0))
    i = iw * ih
    u = a[2] * a[3] + b[2] * b[3] - i
    return i / u if u > 0 else 0.0


def match(pred, gt, thr=0.5):
    """greedy one-to-one matching, pred sorted by confidence already; returns (tp, fp, fn)."""
    used, tp = set(), 0
    for p in pred:
        best, bj = 0.0, None
        for j, g in enumerate(gt):
            if j in used:
                continue
            v = iou(p, g)
            if v > best:
                best, bj = v, j
        if bj is not None and best >= thr:
            used.add(bj); tp += 1
    return tp, len(pred) - tp, len(gt) - tp


def clean_draft(boxes, confs, iou_max=0.30, contain_max=0.55, max_side=0.12, dense_cap=30):
    """Make a draft readable: highest confidence first; drop a box that overlaps a kept box (IoU) or sits mostly inside one;
    drop huge boxes (a spot is small: those are redness patches); a photo that still has more than dense_cap boxes is too dense
    to pre-draw (returns [], True: box it yourself)."""
    order = sorted(range(len(boxes)), key=lambda i: -confs[i])
    kept = []
    for i in order:
        b = boxes[i]
        if (b[2] * b[3]) ** 0.5 > max_side:
            continue
        ok = True
        for k in kept:
            if iou(b, k) > iou_max:
                ok = False; break
            ix = max(0, min(b[0] + b[2] / 2, k[0] + k[2] / 2) - max(b[0] - b[2] / 2, k[0] - k[2] / 2))
            iy = max(0, min(b[1] + b[3] / 2, k[1] + k[3] / 2) - max(b[1] - b[3] / 2, k[1] - k[3] / 2))
            if ix * iy / max(1e-9, min(b[2] * b[3], k[2] * k[3])) > contain_max:
                ok = False; break
        if ok:
            kept.append(b)
    if len(kept) > dense_cap:
        return [], True
    return kept, False


def sources(c, pool_role="train"):
    """Reviewed photos you boxed (training role only) and the unreviewed pool (role 'train' = the facial photos; 'non_facial' = the rest of the 4,073, many are really faces the face check missed)."""
    R = c["root"]
    meta = json.load(open(R / "_drafts" / "meta.json"))["drafted_at"]
    sel = pd.read_csv(R / "sprint_photos.csv").set_index("name")
    reviewed_boxed, reviewed_any = [], set()
    for lp in c["labs"].glob("*.txt"):
        if lp.stat().st_mtime > meta + 1:
            reviewed_any.add(lp.stem)
            img = next(iter(c["imgs"].glob(lp.stem + ".*")), None)
            if img is not None and sel.role.get(img.name) == "train" and lp.stat().st_size > 0:
                reviewed_boxed.append(img.name)
    claude = {p.stem for p in (R / "claude_boxed_Acne" / "labels").glob("*.txt")} if (R / "claude_boxed_Acne" / "labels").exists() else set()
    pool = [n for n in sorted(sel.index) if sel.role[n] == pool_role and Path(n).stem not in reviewed_any and Path(n).stem not in claude]
    return sel, sorted(reviewed_boxed), pool


# ---------------------------------------------------------------------------------------------------- train
def train(a):
    from ultralytics import YOLO
    import torch
    c = cfg_from_args(a)
    sel, boxed, pool = sources(c)
    keep = []
    for n in boxed:
        bx = read_boxes(c["labs"] / (Path(n).stem + ".txt"))
        if not bx:
            continue
        side = float(np.median([np.sqrt(w * h) for _, _, w, h in bx]))
        keep.append((n, side))
    print("your boxed TRAINING photos: %d (test photos excluded)" % len(keep))
    loc = {n: (c["imgs"] / n, c["labs"] / (Path(n).stem + ".txt")) for n, _ in keep}      # name -> (image, label)
    if a.include_drafts:
        dd = c["draft"] / "boxed_Acne_drafts"
        have = {Path(n).stem for n in loc}
        added = skipped = 0
        for lp in sorted((dd / "labels").glob("*.txt")) if (dd / "labels").exists() else []:
            if lp.stem in have:
                continue
            img = next(iter((dd / "images").glob(lp.stem + ".*")), None)
            bx = read_boxes(lp)
            if img is None or not bx:
                continue
            _, _ = clean_draft(bx, [1.0 - 0.001 * i for i in range(len(bx))], dense_cap=10 ** 6)
            kept_n = len(clean_draft(bx, [1.0 - 0.001 * i for i in range(len(bx))], dense_cap=10 ** 6)[0])
            if len(bx) - kept_n >= 3:                      # photos with several doubled / nested boxes would teach doubles
                skipped += 1
                continue
            side = float(np.median([np.sqrt(w * h) for _, _, w, h in bx]))
            keep.append((img.name, side)); loc[img.name] = (img, lp); added += 1
        print("+ reviewed draft photos added to training: %d (skipped %d with several doubled boxes)" % (added, skipped))
    if a.tight:
        before = len(keep)
        keep = [(n, s) for n, s in keep if s <= a.tight_max]
        print("--tight: kept %d of %d photos with median box size <= %.3f (looser photos left out)" % (len(keep), before, a.tight_max))
    names = [n for n, _ in keep]
    rng = random.Random(42); rng.shuffle(names)
    nval = max(5, int(0.12 * len(names)))
    val, tr = names[:nval], names[nval:]
    W = c["work"]
    if W.exists():
        shutil.rmtree(W / "dataset", ignore_errors=True)
    for sp, lst in (("train", tr), ("val", val)):
        (W / "dataset" / sp / "images").mkdir(parents=True); (W / "dataset" / sp / "labels").mkdir(parents=True)
        for n in lst:
            shutil.copy2(loc[n][0], W / "dataset" / sp / "images" / n)      # a local copy: reading many files over the Windows drive can fail at random
            shutil.copy2(loc[n][1], W / "dataset" / sp / "labels" / (Path(n).stem + ".txt"))
    import yaml
    yaml.safe_dump({"path": str(W / "dataset"), "train": "train/images", "val": "val/images", "names": {0: "acne"}}, open(W / "data.yaml", "w"))
    print("train %d photos | held-out check %d photos" % (len(tr), len(val)))
    run = W / "runs" / "acne_predraft"
    best = run / "weights" / "best.pt"
    if not best.exists() or a.retrain:
        m = YOLO(a.model)
        m.train(data=str(W / "data.yaml"), imgsz=a.imgsz, epochs=a.epochs, patience=25, batch=a.batch, device=0 if torch.cuda.is_available() else "cpu",
                workers=2, seed=42, cos_lr=True, close_mosaic=10, project=str(W / "runs"), name="acne_predraft", exist_ok=True,
                fliplr=0.5, flipud=0.0, degrees=10, scale=0.5, hsv_h=0.015, hsv_s=0.5, hsv_v=0.35)
    # honest check on the held-out photos of YOUR boxes
    model = YOLO(str(best))
    rows = []
    for conf in (0.05, 0.10, 0.15, 0.25, 0.40):
        TP = FP = FN = 0
        for n in val:
            r = model.predict(str(W / "dataset" / "val" / "images" / n), imgsz=a.imgsz, conf=conf, iou=0.5, max_det=150, verbose=False, device=0 if torch.cuda.is_available() else "cpu")[0]
            pred = [tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if r.boxes is not None and len(r.boxes) else []
            tp, fp, fn = match(pred, read_boxes(W / "dataset" / "val" / "labels" / (Path(n).stem + ".txt")))
            TP += tp; FP += fp; FN += fn
        rows.append(dict(conf=conf, recall=round(TP / max(1, TP + FN), 3), precision=round(TP / max(1, TP + FP), 3), drafted_per_photo=round((TP + FP) / len(val), 1)))
    df = pd.DataFrame(rows)
    df.to_csv(W / "heldout_check.csv", index=False)
    print("\nHELD-OUT CHECK on %d of your photos (IoU >= 0.5). recall = share of YOUR boxes the draft already has; precision = share of drafted boxes you would keep:" % len(val))
    print(df.to_string(index=False))
    print("\nweights:", best)


# ---------------------------------------------------------------------------------------------------- draft
def draft(a):
    from ultralytics import YOLO
    import torch
    c = cfg_from_args(a)
    sel, boxed, pool = sources(c, a.pool_role)
    D = c["draft"]
    imgsrc = c["imgs"]
    alias = {}
    if a.extra_pool:                                            # more photos: a csv of public photos (column "file") that are NOT in the project yet
        D = c["root"] / a.out_name
        imgsrc = Path(a.extra_images)
        files = pd.read_csv(a.extra_pool).file.tolist()
        pool = ["pub_" + f for f in files]
        alias = {"pub_" + f: f for f in files}
    best = c["work"] / "runs" / "acne_predraft" / "weights" / "best.pt"
    if not best.exists():
        sys.exit("Train first:  python acne_predraft.py train")
    reviewed_here = set()
    if (D / "_drafts" / "meta.json").exists() and (D / "Elaiza__acne_draft" / "labels").exists():
        meta0 = json.load(open(D / "_drafts" / "meta.json"))["drafted_at"]
        reviewed_here = {p.stem for p in (D / "Elaiza__acne_draft" / "labels").glob("*.txt") if p.stat().st_mtime > meta0 + 1}
        if reviewed_here:
            print("keeping %d draft photos you already reviewed (not redrawn)" % len(reviewed_here))
    for d in (D / "Elaiza__acne_draft" / "images", D / "Elaiza__acne_draft" / "labels", D / "drafts_original", D / "_drafts"):
        d.mkdir(parents=True, exist_ok=True)
    if not a.extra_pool:
        pool = pool[::-1]                               # from the END of the list: you box from the front, so the two never overlap
    todo = pool if a.min_boxes else (pool[: a.limit] if a.limit else pool)
    todo = [n for n in todo if Path(n).stem not in reviewed_here]
    model = YOLO(str(best))
    rows = []
    for i, n in enumerate(todo):
        if a.min_boxes and a.limit and len(rows) >= a.limit:
            break                                                   # enough photos that really show several spots
        src_img = imgsrc / alias.get(n, n)
        r = model.predict(str(src_img), imgsz=a.imgsz, conf=a.conf, iou=0.5, max_det=150, verbose=False, device=0 if torch.cuda.is_available() else "cpu")[0]
        boxes, confs = [], []
        if r.boxes is not None and len(r.boxes):
            for b, cf in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy()):
                if b[2] > 0.003 and b[3] > 0.003:
                    boxes.append(tuple(map(float, b))); confs.append(float(cf))
        raw_n = len(boxes)
        boxes, dense = clean_draft(boxes, confs)
        if dense:
            confs = []
        if a.min_boxes and (dense or len(boxes) < a.min_boxes):
            continue                                                # skin patch / no clear acne: leave it out of the review set
        shutil.copy2(src_img, D / "Elaiza__acne_draft" / "images" / n)
        text = "".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes)
        (D / "Elaiza__acne_draft" / "labels" / (Path(n).stem + ".txt")).write_text(text)
        (D / "drafts_original" / (Path(n).stem + ".txt")).write_text(text)
        rows.append((n, len(boxes), "%.2f" % (max(confs) if confs else 0), "%.2f" % (min(confs) if confs else 0), raw_n, "dense: box it yourself" if dense else ""))
        if (i + 1) % 50 == 0:
            print("  drafted %d / %d" % (i + 1, len(todo)), flush=True)
    (D / "Elaiza__acne_draft" / "classes.txt").write_text("acne\n")
    with open(D / "draft_summary.csv", "w", newline="") as fh:
        w = csv.writer(fh); w.writerow(["image", "n_drafted", "max_conf", "min_conf", "n_before_cleaning", "note"]); w.writerows(rows)
    time.sleep(1.5)
    mf = D / "_drafts" / "meta.json"
    ref = json.load(open(mf))["drafted_at"] if mf.exists() else time.time() - 10         # keep the original reference time: photos you reviewed stay "reviewed"
    for n in {Path(r[0]).stem for r in rows}:                                           # photos redrawn now stay "unreviewed": label time just before the reference time
        for f in ((D / "Elaiza__acne_draft" / "labels" / (n + ".txt")), (D / "drafts_original" / (n + ".txt"))):
            os.utime(f, (ref - 5, ref - 5))
    if not mf.exists():
        json.dump({"drafted_at": ref, "note": "acne pre-drawn drafts; a photo counts as reviewed once you save it"}, open(mf, "w"))
    shutil.copy2(c["tool"], D / "lesion_box_tool.py")
    (D / "START_REVIEW_DRAFTS.bat").write_text(
        '@echo off\r\ncd /d "%~dp0"\r\n'
        'echo Drafts are already drawn. DELETE wrong boxes, ADD missed spots, press S. X = no acne visible.\r\n'
        'echo Every photo you save is copied to boxed_Acne_drafts.\r\n'
        'py -3.13 lesion_box_tool.py "Elaiza__acne_draft" --label acne --annotator Elaiza --port ' + str(a.port) + ' --export "boxed_Acne_drafts"\r\npause\r\n')
    n_dense = sum(1 for r in rows if r[5])
    print("%d photos were too dense to pre-draw (box those yourself); duplicate / nested / huge boxes were removed" % n_dense)
    n_with = sum(1 for r in rows if r[1] > 0)
    print("\nDRAFTED %d photos (%d with at least one box, %d with none) | mean %.1f boxes per photo | conf >= %.2f" %
          (len(rows), n_with, len(rows) - n_with, np.mean([r[1] for r in rows]) if rows else 0, a.conf))
    print("skipped on purpose: hand-boxed TEST photos (box them yourself) and photos you already reviewed")
    print("Open:", D / "START_REVIEW_DRAFTS.bat")


# ---------------------------------------------------------------------------------------------------- stats
def stats(a):
    c = cfg_from_args(a)
    D = c["draft"]
    meta = json.load(open(D / "_drafts" / "meta.json"))["drafted_at"]
    rows = []
    for orig in sorted((D / "drafts_original").glob("*.txt")):
        cur = D / "Elaiza__acne_draft" / "labels" / orig.name
        if not cur.exists() or cur.stat().st_mtime <= meta + 1:
            continue                                         # not reviewed yet
        d, f = read_boxes(orig), read_boxes(cur)
        kept = 0; used = set()
        for p in d:
            best, bj = 0.0, None
            for j, g in enumerate(f):
                if j in used:
                    continue
                v = iou(p, g)
                if v > best:
                    best, bj = v, j
            if bj is not None and best >= 0.5:
                used.add(bj); kept += 1
        rows.append(dict(image=orig.stem, drafted=len(d), final=len(f), kept=kept, deleted=len(d) - kept, added=len(f) - kept))
    df = pd.DataFrame(rows)
    if df.empty:
        print("No reviewed draft photos yet (save some in the box tool first)."); return
    df.to_csv(D / "draft_review_stats.csv", index=False)
    T = df[["drafted", "final", "kept", "deleted", "added"]].sum()
    print("REVIEWED %d draft photos" % len(df))
    print("drafted boxes %d | kept as drawn %d (%.0f %%) | deleted %d | you added %d" % (T.drafted, T.kept, 100 * T.kept / max(1, T.drafted), T.deleted, T.added))
    print("draft PRECISION (share of drafted boxes you kept): %.0f %%" % (100 * T.kept / max(1, T.drafted)))
    print("draft RECALL (share of your final boxes that were already drafted): %.0f %%" % (100 * T.kept / max(1, T.final)))
    print("photos left exactly as drafted: %d of %d" % (int(((df.deleted == 0) & (df.added == 0)).sum()), len(df)))
    print("saved:", D / "draft_review_stats.csv")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("step", choices=["train", "draft", "stats"])
    ap.add_argument("--root", default="/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing")
    ap.add_argument("--work", default=str(Path.home() / "PrecisionSkinV1" / "acne_predraft_v2"))
    ap.add_argument("--tool", default="/mnt/c/Users/Bautista/Downloads/lesion_box_tool.py")
    ap.add_argument("--model", default="yolov8s.pt")
    ap.add_argument("--imgsz", type=int, default=800)
    ap.add_argument("--epochs", type=int, default=60)
    ap.add_argument("--batch", type=int, default=8)
    ap.add_argument("--conf", type=float, default=0.15)
    ap.add_argument("--tight", action=argparse.BooleanOptionalAction, default=False)   # all-photos model was clearly better than the tight-only one
    ap.add_argument("--tight-max", type=float, default=0.065)
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--pool-role", default="train", choices=["train", "non_facial"], help="which unreviewed photos to pre-draw; the facial pool is used up, 'non_facial' holds the remaining ~2,650")
    ap.add_argument("--min-boxes", type=int, default=0, help="only keep photos where the helper finds at least this many clean boxes (skips skin patches); --limit then counts kept photos")
    ap.add_argument("--retrain", action="store_true")
    ap.add_argument("--extra-pool", default=None, help="csv of NEW public photos (column 'file') to pre-draw instead of the project's own photos")
    ap.add_argument("--extra-images", default=None, help="folder that holds those photos")
    ap.add_argument("--out-name", default="Draft_Acne2", help="output folder name inside Acne_Boxing (extra-pool mode)")
    ap.add_argument("--port", type=int, default=8802, help="port written into the review .bat")
    ap.add_argument("--include-drafts", action=argparse.BooleanOptionalAction, default=True)   # also learn from the draft photos you reviewed
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()
    {"train": train, "draft": draft, "stats": stats}[a.step](a)


if __name__ == "__main__":
    main()
