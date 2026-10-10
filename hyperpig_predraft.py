#!/usr/bin/env python
"""
hyperpig_predraft.py - the same helper idea as acne_predraft.py, for hyperpigmentation.

  step 1  train  : trains a hyperpigmentation-only helper on YOUR boxes (public boxes are NOT used), without the 100 hand-boxed TEST photos.
                   It also prints how the helper does on those 100 test photos (just to know how good the drafts will be; nothing is tuned on them).
  step 2  draft  : pre-draws boxes on photos nobody has boxed (the clean, distinct pool), keeps only photos where the helper finds something,
                   and writes a review folder for lesion_box_tool.py:      Hyperpigmentation_Boxing/Draft_Hyperpig/START_REVIEW_DRAFTS.bat
  step 3  fix    : "second look" at YOUR old boxes: photos where the helper confidently sees patches you did not box. It writes a copy of those
                   photos with YOUR boxes + the extra suggested boxes, so you only delete the wrong ones. Your originals are never touched,
                   and the 100 hand-boxed test photos are never included.
  step 4  stats  : after you reviewed the drafts, how many drafted boxes you kept / deleted / added (the real reliability of the helper).

Run (WSL, venv with ultralytics):   python hyperpig_predraft.py train | draft --limit 300 | fix --limit 60 | stats
"""
import argparse, csv, json, os, random, shutil, sys, time
from pathlib import Path
import numpy as np, pandas as pd

HOME = Path.home() / "PrecisionSkinV1"
OWN = Path("/mnt/c/Users/Bautista/Downloads/own_hyperpigmentation.yolov8")                      # your boxed photos (images/ + labels/)
W1 = HOME / "hyperpig_autolabel" / "hyperpig_only_v1"                                           # earlier pipeline: hand_test list + unlabeled index
OUT = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing")
TOOL = Path("/mnt/c/Users/Bautista/Downloads/lesion_box_tool.py")
WORK = HOME / "hyperpig_predraft"
IMG_EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def read_boxes(p):
    out = []
    if Path(p).exists():
        for ln in Path(p).read_text().splitlines():
            t = ln.split()
            if len(t) >= 5:
                out.append(tuple(map(float, t[-4:])))
    return out


def xyxy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2


def rel(a, b):
    a, b = xyxy(a), xyxy(b)
    iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    aa, bb = (a[2] - a[0]) * (a[3] - a[1]), (b[2] - b[0]) * (b[3] - b[1])
    return i / (aa + bb - i + 1e-9), i / (min(aa, bb) + 1e-9)


def clean(boxes, confs, dense_cap=25):
    """highest confidence first; drop a box that doubles or sits inside an already kept one, and near-whole-photo boxes."""
    kept = []
    for b, c in sorted(zip(boxes, confs), key=lambda x: -x[1]):
        if b[2] > 0.6 or b[3] > 0.6:
            continue
        if any(rel(b, k)[0] > 0.5 or rel(b, k)[1] > 0.8 for k, _ in kept):
            continue
        kept.append((b, c))
    return kept, len(kept) > dense_cap


def hand_test_stems():
    d = W1 / "dataset" / "hand_test" / "images"
    return {p.stem for p in d.glob("*")} if d.exists() else set()


def own_photos(exclude_test=True):
    ex = hand_test_stems() if exclude_test else set()
    out = []
    for img in sorted((OWN / "images").glob("*")):
        if img.suffix.lower() in IMG_EXT and img.stem not in ex and read_boxes(OWN / "labels" / (img.stem + ".txt")):
            out.append(img)
    return out


def weights():
    return WORK / "runs" / "hyp_predraft" / "weights" / "best.pt"


# ------------------------------------------------------------------------------------------------ train
def train(a):
    from ultralytics import YOLO
    import yaml
    rng = random.Random(42)
    photos = own_photos()
    extra = []
    if a.include_drafts:                                            # drafts you already reviewed and saved with boxes
        D = OUT / "Draft_Hyperpig"
        meta = D / "_drafts" / "meta.json"
        if meta.exists():
            ref = json.load(open(meta))["drafted_at"]
            for lp in (D / "Elaiza__hyperpigmentation_draft" / "labels").glob("*.txt"):
                if lp.stat().st_mtime > ref + 1 and read_boxes(lp):
                    img = next(iter((D / "Elaiza__hyperpigmentation_draft" / "images").glob(lp.stem + ".*")), None)
                    if img is not None:
                        extra.append((img, lp))
    items = [(p, OWN / "labels" / (p.stem + ".txt")) for p in photos] + extra
    rng.shuffle(items)
    nval = max(20, int(0.15 * len(items)))
    DS = WORK / "dataset"
    if DS.exists():
        shutil.rmtree(DS)
    for sp in ("train", "val"):
        (DS / sp / "images").mkdir(parents=True); (DS / sp / "labels").mkdir(parents=True)
    for k, (img, lab) in enumerate(items):
        sp = "val" if k < nval else "train"
        shutil.copy2(img, DS / sp / "images" / img.name)
        shutil.copy2(lab, DS / sp / "labels" / (img.stem + ".txt"))
    yaml.safe_dump({"path": str(DS), "train": "train/images", "val": "val/images", "names": {0: "hyperpigmentation"}}, open(DS / "data.yaml", "w"))
    print("helper data: %d own photos + %d reviewed drafts | train %d / val %d (the 100 hand-boxed test photos are NOT used)" % (len(photos), len(extra), len(items) - nval, nval), flush=True)
    if weights().exists() and not a.retrain:
        print("helper already trained (use --retrain to redo)")
    else:
        m = YOLO(a.model)
        m.train(data=str(DS / "data.yaml"), imgsz=a.imgsz, epochs=a.epochs, patience=25, batch=a.batch, workers=2, seed=42, device=0,
                project=str(WORK / "runs"), name="hyp_predraft", exist_ok=True, plots=False, verbose=False)
    held_out_check(a)


def held_out_check(a):
    from ultralytics import YOLO
    d = W1 / "dataset" / "hand_test"
    if not (d / "images").exists():
        return
    m = YOLO(str(weights()))
    for conf in (0.15, 0.25, 0.40):
        tp = fp = fn = 0
        for img in sorted((d / "images").glob("*")):
            gt = read_boxes(d / "labels" / (img.stem + ".txt"))
            r = m.predict(str(img), imgsz=a.imgsz, conf=conf, iou=0.5, verbose=False, device=0)[0]
            pr = [tuple(map(float, x)) for x in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else []
            used = set()
            for g in gt:
                best, bj = 0, -1
                for j, p in enumerate(pr):
                    if j not in used and rel(g, p)[0] > best:
                        best, bj = rel(g, p)[0], j
                if best >= 0.3:
                    used.add(bj); tp += 1
                else:
                    fn += 1
            fp += len(pr) - len(used)
        print("helper on the 100 hand-boxed test photos, conf %.2f (match IoU>=0.3): precision %.2f | recall %.2f" % (conf, tp / max(1, tp + fp), tp / max(1, tp + fn)))


# ------------------------------------------------------------------------------------------------ review folder helpers
def write_review_folder(D, sub, items, name_note, bat_note, port, export):
    """items: list of (image_path, [boxes]) -> D/sub/images, labels, meta (all 'not reviewed yet'), bat."""
    S = D / sub
    for d in (S / "images", S / "labels", D / "_drafts", D / "drafts_original"):
        d.mkdir(parents=True, exist_ok=True)
    for img, boxes in items:
        shutil.copy2(img, S / "images" / img.name)
        text = "".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes)
        (S / "labels" / (img.stem + ".txt")).write_text(text)
        (D / "drafts_original" / (img.stem + ".txt")).write_text(text)              # for the stats step
    (S / "classes.txt").write_text("hyperpigmentation\n")
    time.sleep(1.5)
    mf = D / "_drafts" / "meta.json"
    ref = json.load(open(mf))["drafted_at"] if mf.exists() else time.time()
    for img, _ in items:
        t = ref - 5                                                 # label time just before the reference time = "not reviewed yet"
        os.utime(S / "labels" / (img.stem + ".txt"), (t, t))
    if not mf.exists():
        json.dump({"drafted_at": ref, "note": name_note}, open(mf, "w"))
    shutil.copy2(TOOL, D / "lesion_box_tool.py")
    (D / "START_REVIEW.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho ' + bat_note + '\r\n'
        'py -3.13 lesion_box_tool.py "' + sub + '" --label hyperpigmentation --annotator Elaiza --port ' + str(port) + ' --export "' + export + '"\r\npause\r\n')


# ------------------------------------------------------------------------------------------------ draft
def draft(a):
    from ultralytics import YOLO
    if not weights().exists():
        sys.exit("Train first:  python hyperpig_predraft.py train")
    idx = pd.read_csv(W1 / "reports" / "unlabeled_index.csv")
    idx = idx[(idx.eligible == True) & (idx.status == "ok") & (idx[["width", "height"]].min(axis=1) >= a.min_side)]   # distinct, not overlapping val/test/hand-test, not tiny
    pool = idx.path.tolist(); random.Random(7).shuffle(pool)
    D = OUT / "Draft_Hyperpig"
    sub = "Elaiza__hyperpigmentation_draft"
    done = {p.stem for p in (D / sub / "images").glob("*")} if (D / sub / "images").exists() else set()
    pool = [p for p in pool if Path(p).stem not in done]
    m = YOLO(str(weights()))
    items, rows, seen = [], [], 0
    for p in pool:
        if len(items) >= a.limit:
            break
        seen += 1
        r = m.predict(p, imgsz=a.imgsz, conf=a.conf, iou=0.5, max_det=100, verbose=False, device=0)[0]
        bx = [tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else []
        cf = [float(c) for c in r.boxes.conf.cpu().numpy()] if len(r.boxes) else []
        kept, dense = clean([b for b in bx if b[2] > 0.003 and b[3] > 0.003], cf)
        if dense or len(kept) < a.min_boxes:
            continue
        items.append((Path(p), [b for b, _ in kept])); rows.append((Path(p).name, len(kept), "%.2f" % max(c for _, c in kept)))
        if len(items) % 50 == 0:
            print("  kept %d (looked at %d)" % (len(items), seen), flush=True)
    write_review_folder(D, sub, items, "hyperpigmentation pre-drawn drafts; a photo counts as reviewed once you save it",
                        "Drafts are already drawn. DELETE wrong boxes, ADD missed patches, press S. X = no patch visible. Every saved photo is copied to boxed_Hyperpig_drafts.",
                        a.port, "boxed_Hyperpig_drafts")
    with open(D / "draft_summary.csv", "a", newline="") as fh:
        csv.writer(fh).writerows(rows)
    print("\nDRAFTED %d photos (looked at %d) | mean %.1f boxes per photo | conf >= %.2f" % (len(items), seen, np.mean([r[1] for r in rows]) if rows else 0, a.conf))
    print("Open:", D / "START_REVIEW.bat")


# ------------------------------------------------------------------------------------------------ fix (second look at your own boxes)
def fix(a):
    from ultralytics import YOLO
    if not weights().exists():
        sys.exit("Train first:  python hyperpig_predraft.py train")
    m = YOLO(str(weights()))
    cand = []
    for img in own_photos():                                        # the 100 hand-boxed test photos are excluded
        mine = read_boxes(OWN / "labels" / (img.stem + ".txt"))
        r = m.predict(str(img), imgsz=a.imgsz, conf=a.conf, iou=0.5, max_det=100, verbose=False, device=0)[0]
        bx = [tuple(map(float, b)) for b in r.boxes.xywhn.cpu().numpy()] if len(r.boxes) else []
        cf = [float(c) for c in r.boxes.conf.cpu().numpy()] if len(r.boxes) else []
        kept, _ = clean([b for b in bx if b[2] > 0.003 and b[3] > 0.003], cf)
        extra = [(b, c) for b, c in kept if not any(rel(b, g)[0] > 0.2 or rel(b, g)[1] > 0.5 for g in mine)]
        if len(extra) >= a.min_extra:
            cand.append((sum(c for _, c in extra), img, mine, [b for b, _ in extra]))
    cand.sort(key=lambda t: -t[0])
    cand = cand[: a.limit]
    D = OUT / "Fix_Hyperpig_Boxes"
    if D.exists():
        sys.exit("exists already, not overwriting: %s" % D)
    write_review_folder(D, "Elaiza__hyperpigmentation_fix", [(img, mine + extra) for _, img, mine, extra in cand],
                        "your boxes + suggested extra boxes; a photo counts as fixed once you save it",
                        "These photos have YOUR boxes plus extra boxes the helper suggests. Delete the extra ones that are not patches, keep real ones, press S.",
                        a.port + 1, "boxed_Hyperpig_FIXED")
    print("FIX folder: %d photos with your boxes + suggested extra boxes (%d extra boxes in total)" % (len(cand), sum(len(c[3]) for c in cand)))
    print("Open:", D / "START_REVIEW.bat")


# ------------------------------------------------------------------------------------------------ stats
def stats(a):
    D = OUT / "Draft_Hyperpig"
    ref = json.load(open(D / "_drafts" / "meta.json"))["drafted_at"]
    kept = deleted = added = n = same = 0
    for lp in (D / "Elaiza__hyperpigmentation_draft" / "labels").glob("*.txt"):
        if lp.stat().st_mtime <= ref + 1:
            continue
        orig = D / "drafts_original" / (lp.stem + ".txt")
        if not orig.exists():
            continue
        o, f = read_boxes(orig), read_boxes(lp); n += 1
        k = sum(1 for b in o if any(rel(b, g)[0] > 0.5 for g in f))
        kept += k; deleted += len(o) - k; added += sum(1 for g in f if not any(rel(g, b)[0] > 0.5 for b in o)); same += (len(o) == k and len(f) == k)
    print("REVIEWED %d draft photos | drafted boxes %d | kept %d (%.0f%%) | deleted %d | you added %d | draft recall %.0f%% | photos left exactly as drafted: %d"
          % (n, kept + deleted, kept, 100 * kept / max(1, kept + deleted), deleted, added, 100 * kept / max(1, kept + added), same))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("step", choices=["train", "draft", "fix", "stats"])
    ap.add_argument("--model", default="yolov8s.pt"); ap.add_argument("--imgsz", type=int, default=800)
    ap.add_argument("--epochs", type=int, default=80); ap.add_argument("--batch", type=int, default=8)
    ap.add_argument("--conf", type=float, default=0.25); ap.add_argument("--limit", type=int, default=300)
    ap.add_argument("--min-boxes", type=int, default=1); ap.add_argument("--min-extra", type=int, default=2)
    ap.add_argument("--min-side", type=int, default=500, help="skip low-resolution photos (the pool has many 224 px ones)")
    ap.add_argument("--port", type=int, default=8810)
    ap.add_argument("--retrain", action="store_true")
    ap.add_argument("--include-drafts", action=argparse.BooleanOptionalAction, default=True)
    a = ap.parse_args()
    {"train": train, "draft": draft, "fix": fix, "stats": stats}[a.step](a)


if __name__ == "__main__":
    main()
