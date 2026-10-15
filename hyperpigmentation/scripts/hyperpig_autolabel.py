#!/usr/bin/env python
"""
hyperpig_autolabel.py - hyperpigmentation-only box detector + automatic pseudo-labelling of unannotated photos.

What it does
  1. build_dataset   : a ONE-class dataset (class 0 = hyperpigmentation) from your existing annotated photos
                       (v1 hyperpigmentation boxes + your hand-boxed photos). Validation and test stay exactly the v1 sets.
  2. index_unlabeled : scans the photos WITHOUT boxes; excludes unreadable files, exact/near duplicates, photos that overlap
                       validation/test, and photos that are already annotated (training copies).
  3. train           : trains the hyperpigmentation-only YOLOv8 model (can resume).
  4. evaluate        : precision / recall / mAP on validation and test.
  5. annotate        : batch inference with RESUME. Writes pseudo-labels (YOLO, class 0), confidences, previews, a per-image summary.
  6. summarize       : counts of processed images, boxes, uncertain predictions, images with no detection.
  7. make_review_folder : puts the uncertain / no-detection photos in a folder you can open with lesion_box_tool.py.

Never changed: your original photos, the v1 dataset, your hand-boxed folder. Everything generated goes to  out_root/<run_name>/.
No detection does NOT mean healthy skin: those photos are only flagged "no_detection" and are never used as "no lesion" examples.

Command line (from the SkinDevApp folder, kernel/venv with torch + ultralytics, e.g. ~/PrecisionSkinV1/.venv_det):
    python hyperpig_autolabel.py build
    python hyperpig_autolabel.py index
    python hyperpig_autolabel.py audit               # public vs hand boxes
    python hyperpig_autolabel.py train               # all variants (n_640, s_800, m_800)
    python hyperpig_autolabel.py compare             # v1 vs variants on val / test / hand-boxed test
    python hyperpig_autolabel.py evaluate
    python hyperpig_autolabel.py annotate            # resumes automatically; add --limit 100 for a trial
    python hyperpig_autolabel.py summary
    python hyperpig_autolabel.py review
Settings can be overridden with  --set key=value  (for example  --set conf_accept=0.5 --set epochs=60).
"""
import argparse
import csv
import datetime
import hashlib
import json
import os
import random
import shutil
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import cv2
import numpy as np
import pandas as pd
import yaml

IMG_EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
CLASS_NAME = "hyperpigmentation"


class Cfg(dict):
    __getattr__ = dict.get
    __setattr__ = dict.__setitem__


def default_config():
    home = Path.home() / "PrecisionSkinV1"
    c = Cfg(
        # ---- inputs (read only) ----
        v1_dataset=home / "detector_work" / "dataset_v1",                                    # v1 train/val/test + dataset_manifest.csv
        hand_boxed_dir=Path("/mnt/c/Users/Bautista/Downloads/own_hyperpigmentation.yolov8"),   # your hand-boxed photos (images/ + labels/, class 0); None to skip
        replace_public_twins=True,                                                           # your boxes replace the public boxes of the same photo
        hand_repeat=2,                                                                       # times your hand-boxed photos are used per epoch
        unlabeled_dir=Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Hyperpigmentation"),   # photos WITHOUT boxes (folder, searched recursively)
        # ---- outputs ----
        out_root=home / "hyperpig_autolabel",
        run_name="hyperpig_only_v1",
        # ---- training ----
        model_start="yolov8n.pt", imgsz=640, epochs=100, patience=40, batch=16, workers=2, seed=42,
        # model variants that are trained and compared (one class each). 8 GB GPU: lower batch for bigger models / resolution
        variants=[dict(name="n_640", model="yolov8n.pt", imgsz=640, batch=16),
                  dict(name="s_800", model="yolov8s.pt", imgsz=800, batch=8),
                  dict(name="m_800", model="yolov8m.pt", imgsz=800, batch=4)],
        active_variant="s_800",                 # the variant used by annotate (change after you see the comparison)
        v1_model=home / "detector_work" / "runs" / "detector_v1_yolov8n_seed42" / "weights" / "best.pt",   # 3-class model in the app (for the comparison)
        v1_app_conf=0.25,                       # hyperpigmentation threshold used in the app
        # ---- consistency of the boxes ----
        hand_holdout=100,                       # hand-boxed photos kept OUT of training and used as a hand-boxed test set (0 = none)
        public_max_box_area=None,               # e.g. 0.5: drop public training photos that contain a box over 50% of the photo (see box_audit)
        # ---- pseudo-labelling thresholds ----
        conf_min=0.15,        # below this a prediction is ignored completely
        conf_accept=0.40,     # >= this: the box is written to the pseudo-label file
        conf_high=0.55,       # an image whose BEST accepted box is below this is flagged "weak"
        nms_iou=0.45, max_det=100, tta=False, annotate_batch=32,
        many_boxes_flag=25, huge_box_frac=0.6, tiny_box_frac=0.01,
        process_duplicates=False,                                                            # also annotate duplicate copies (normally False)
        preview_mode="all",                                                                  # "all" | "uncertain" | "none"
        preview_max_side=900, review_max=500,
        near_dup_corr=0.97,   # building the dataset (public twin of a hand-boxed photo)
        corr_holdout=0.90,    # checking the unannotated photos against val / test / hand-test (strict: also catches augmented copies)
        corr_dup=0.93,        # duplicates among the unannotated photos and against annotated training photos
        expect_val=(164, 448), expect_test=(164, 380),                                       # photos, boxes you expect (only a warning if different)
    )
    return c


def paths(cfg):
    base = Path(cfg.out_root) / cfg.run_name
    p = Cfg(base=base, dataset=base / "dataset", runs=base / "runs", pseudo=base / "pseudo_labels", previews=base / "previews",
            reports=base / "reports", review=base / "review_folder")
    for k in ("dataset", "runs", "pseudo", "reports"):
        p[k].mkdir(parents=True, exist_ok=True)
    return p


def _assert_outputs_safe(cfg):
    base = (Path(cfg.out_root) / cfg.run_name).resolve()
    for src in (cfg.v1_dataset, cfg.hand_boxed_dir, cfg.unlabeled_dir):
        if src and str(base).startswith(str(Path(src).resolve())):
            raise RuntimeError("out_root must not be inside an input folder: %s" % src)


# ------------------------------------------------------------------------------------------------ small helpers
def md5_of(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for blk in iter(lambda: f.read(1 << 20), b""):
            h.update(blk)
    return h.hexdigest()


def thumb(path):
    try:
        im = cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_GRAYSCALE)
        return None if im is None else cv2.resize(im, (32, 32), interpolation=cv2.INTER_AREA).astype(np.float32)
    except Exception:
        return None


def zscore(x):
    x = x.reshape(len(x), -1).astype(np.float32)
    x = x - x.mean(1, keepdims=True)
    return (x / (x.std(1, keepdims=True) + 1e-6) / np.sqrt(x.shape[1])).astype(np.float32)


def best_corr(A, B):
    """A: (n,32,32) thumbnails, B: (m,32,32). Returns (n, m) best correlation over the 8 rotations/flips of A."""
    Bz = zscore(B)
    best = np.full((len(A), len(B)), -1.0, np.float32)
    for k in range(4):
        for flip in (False, True):
            v = np.rot90(A, k, axes=(1, 2))
            v = v[:, :, ::-1] if flip else v
            best = np.maximum(best, zscore(np.ascontiguousarray(v)) @ Bz.T)
    return best


def read_yolo(path, only_class=None):
    out = []
    if Path(path).exists():
        for ln in Path(path).read_text().splitlines():
            t = ln.split()
            if len(t) >= 5:
                k = int(float(t[0]))
                if only_class is not None and k != only_class:
                    continue
                nums = [float(v) for v in t[1:]]
                if len(nums) > 4:                                        # polygon -> enclosing box
                    xs, ys = nums[0::2], nums[1::2]
                    cx, cy, w, h = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, max(xs) - min(xs), max(ys) - min(ys)
                else:
                    cx, cy, w, h = nums
                if w > 0.003 and h > 0.003:
                    out.append((cx, cy, w, h))
    return out


def list_images(folder):
    return sorted(p for p in Path(folder).rglob("*") if p.is_file() and p.suffix.lower() in IMG_EXT)


# ------------------------------------------------------------------------------------------------ 1. dataset
def build_dataset(cfg, log=print):
    """One-class dataset from the existing annotated photos. Returns a dict of counts."""
    _assert_outputs_safe(cfg)
    P = paths(cfg)
    DS = P.dataset
    if DS.exists():
        shutil.rmtree(DS)
    for sp in ("train", "val", "test"):
        (DS / sp / "images").mkdir(parents=True)
        (DS / sp / "labels").mkdir(parents=True)
    v1 = Path(cfg.v1_dataset)
    man = pd.read_csv(v1 / "dataset_manifest.csv")
    hyp = man[man.condition.str.lower() == "hyperpigmentation"].reset_index(drop=True)

    # --- your hand-boxed photos: which public photos do they replace? which must be skipped (public copy in val/test)? ---
    hand = []
    removed_groups, skipped_leak, n_twin = set(), set(), 0
    if cfg.hand_boxed_dir and Path(cfg.hand_boxed_dir).exists():
        hd = Path(cfg.hand_boxed_dir)
        for img in list_images(hd / "images"):
            lab = hd / "labels" / (img.stem + ".txt")
            if read_yolo(lab):
                hand.append(img)
        log("hand-boxed photos with boxes: %d" % len(hand))
        if cfg.replace_public_twins and hand:
            pub_t = [thumb(v1 / r.split / "images" / r.file) for r in hyp.itertuples()]
            ok = [i for i, t in enumerate(pub_t) if t is not None]
            own_t = [thumb(p) for p in hand]
            keep = [i for i, t in enumerate(own_t) if t is not None]
            best = best_corr(np.stack([own_t[i] for i in keep]), np.stack([pub_t[i] for i in ok]))
            for row, i in enumerate(keep):
                hit = np.where(best[row] >= cfg.near_dup_corr)[0]
                if len(hit) == 0:
                    continue
                rows = hyp.iloc[[ok[j] for j in hit]]
                n_twin += 1
                if rows.split.isin(["val", "test"]).any():
                    skipped_leak.add(hand[i])
                else:
                    removed_groups.update(rows.duplicate_group.tolist())
            log("hand-boxed photos with a public copy: %d | replaced public groups: %d | skipped (public copy in val/test): %d"
                % (n_twin, len(removed_groups), len(skipped_leak)))

    counts = {}
    n_big_dropped = 0
    for sp in ("train", "val", "test"):
        rows = hyp[hyp.split == sp]
        if sp == "train" and removed_groups:
            rows = rows[~rows.duplicate_group.isin(removed_groups)]
        n_ph = n_bx = 0
        for r in rows.itertuples():
            src_img = v1 / sp / "images" / r.file
            boxes = read_yolo(v1 / sp / "labels" / (Path(r.file).stem + ".txt"), only_class=1)       # v1 class 1 = hyperpigmentation
            if not boxes or not src_img.exists():
                continue
            if sp == "train" and cfg.public_max_box_area and any(b[2] * b[3] > cfg.public_max_box_area for b in boxes):
                n_big_dropped += 1
                continue
            os.symlink(src_img, DS / sp / "images" / r.file)
            (DS / sp / "labels" / (Path(r.file).stem + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes))
            n_ph += 1; n_bx += len(boxes)
        counts[sp] = [n_ph, n_bx]
    n_hand = n_hand_boxes = 0
    usable = [i for i in hand if i not in skipped_leak]
    hold = set(random.Random(cfg.seed).sample(usable, min(int(cfg.hand_holdout or 0), len(usable))))
    (DS / "hand_test" / "images").mkdir(parents=True); (DS / "hand_test" / "labels").mkdir(parents=True)
    n_ht = n_htb = 0
    for img in usable:
        boxes = read_yolo(Path(cfg.hand_boxed_dir) / "labels" / (img.stem + ".txt"))
        if img in hold:                                                    # hand-boxed TEST photos: never trained on
            shutil.copy2(img, DS / "hand_test" / "images" / (img.stem + img.suffix.lower()))
            (DS / "hand_test" / "labels" / (img.stem + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes))
            n_ht += 1; n_htb += len(boxes)
            continue
        for rep in range(int(cfg.hand_repeat)):
            nm = "hand_%s%s" % (img.stem, "" if rep == 0 else "_rep%d" % rep)
            shutil.copy2(img, DS / "train" / "images" / (nm + img.suffix.lower()))
            (DS / "train" / "labels" / (nm + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes))
        n_hand += 1; n_hand_boxes += len(boxes)
    counts["train"][0] += n_hand; counts["train"][1] += n_hand_boxes
    yaml.safe_dump({"path": str(DS), "train": "train/images", "val": "val/images", "test": "test/images", "names": {0: CLASS_NAME}}, open(DS / "data.yaml", "w"))
    rep = dict(train_photos=counts["train"][0], train_boxes=counts["train"][1], val_photos=counts["val"][0], val_boxes=counts["val"][1],
               test_photos=counts["test"][0], test_boxes=counts["test"][1], hand_boxed_used=n_hand, hand_boxed_skipped_leak=len(skipped_leak),
               public_groups_replaced=len(removed_groups), hand_test_photos=n_ht, hand_test_boxes=n_htb, public_dropped_big_box=n_big_dropped)
    json.dump(rep, open(P.reports / "dataset_report.json", "w"), indent=2)
    log("DATASET (one class: %s): train %d photos / %d boxes | val %d / %d | test %d / %d" % (
        CLASS_NAME, rep["train_photos"], rep["train_boxes"], rep["val_photos"], rep["val_boxes"], rep["test_photos"], rep["test_boxes"]))
    log("hand-boxed TEST set (held out of training): %d photos / %d boxes | public photos dropped for a huge box: %d" % (n_ht, n_htb, n_big_dropped))
    if (rep["val_photos"], rep["val_boxes"]) != tuple(cfg.expect_val):
        log("WARNING: validation is %s, you expected %s" % ((rep["val_photos"], rep["val_boxes"]), tuple(cfg.expect_val)))
    if (rep["test_photos"], rep["test_boxes"]) != tuple(cfg.expect_test):
        log("WARNING: test is %s, you expected %s" % ((rep["test_photos"], rep["test_boxes"]), tuple(cfg.expect_test)))
    return rep


# ------------------------------------------------------------------------------------------------ 2. index the unlabeled photos
def index_unlabeled(cfg, log=print, workers=8):
    """Checks every photo without boxes. Writes reports/unlabeled_index.csv (+ overlap_report.csv). Returns the DataFrame."""
    _assert_outputs_safe(cfg)
    P = paths(cfg)
    files = list_images(cfg.unlabeled_dir)
    log("photos found in %s: %d" % (cfg.unlabeled_dir, len(files)))

    def feat(p):
        try:
            return md5_of(p), thumb(p)
        except Exception:
            return None, None
    with ThreadPoolExecutor(workers) as ex:
        res = list(ex.map(feat, files))
    df = pd.DataFrame(dict(path=[str(f) for f in files], md5=[r[0] for r in res]))
    th = [r[1] for r in res]
    df["readable"] = [t is not None for t in th]
    sizes = []
    for f, t in zip(files, th):
        try:
            im = cv2.imdecode(np.fromfile(str(f), np.uint8), cv2.IMREAD_UNCHANGED)
            sizes.append((im.shape[1], im.shape[0]) if im is not None else (0, 0))
        except Exception:
            sizes.append((0, 0))
    df["width"], df["height"] = [s[0] for s in sizes], [s[1] for s in sizes]
    df["status"] = np.where(df.readable, "ok", "unreadable")
    df["matched_with"] = ""
    ok_idx = np.where(df.readable)[0]
    U = np.stack([th[i] for i in ok_idx])

    # reference photos: every hyperpigmentation photo that already has annotations (v1 train/val/test + hand-boxed)
    v1 = Path(cfg.v1_dataset)
    man = pd.read_csv(v1 / "dataset_manifest.csv")
    hyp = man[man.condition.str.lower() == "hyperpigmentation"].reset_index(drop=True)
    ref_paths, ref_tag = [], []
    for r in hyp.itertuples():
        ref_paths.append(v1 / r.split / "images" / r.file); ref_tag.append(("overlap_with_" + r.split) if r.split in ("val", "test") else "already_annotated")
    held = {p.stem for p in list_images(P.dataset / "hand_test" / "images")} if (P.dataset / "hand_test" / "images").exists() else set()
    if cfg.hand_boxed_dir and Path(cfg.hand_boxed_dir).exists():
        for p in list_images(Path(cfg.hand_boxed_dir) / "images"):
            ref_paths.append(p); ref_tag.append("overlap_with_hand_test" if p.stem in held else "already_annotated")
    rt = [thumb(p) for p in ref_paths]
    rok = [i for i, t in enumerate(rt) if t is not None]
    R = np.stack([rt[i] for i in rok])
    best = best_corr(U, R)
    prio = {"overlap_with_val": 0, "overlap_with_test": 0, "overlap_with_hand_test": 0, "already_annotated": 1}
    # augmented copies (crop / colour / rotation) of a val/test photo have a lower correlation than exact copies, so val/test/hand-test use a stricter (lower) threshold
    thr_ref = np.array([cfg.corr_holdout if ref_tag[i].startswith("overlap") else cfg.corr_dup for i in rok], np.float32)
    for row, i in enumerate(ok_idx):
        hits = np.where(best[row] >= thr_ref)[0]
        if len(hits):
            tags = [ref_tag[rok[j]] for j in hits]
            tag = sorted(tags, key=lambda t: prio[t])[0]                  # val/test overlap wins over "already annotated"
            df.at[i, "status"] = tag
            df.at[i, "matched_with"] = str(ref_paths[rok[hits[int(np.argmax(best[row][hits]))]]])

    # duplicates inside the unlabeled set itself: keep the first of each group
    still = [i for i in ok_idx if df.at[i, "status"] == "ok"]
    if still:
        S = np.stack([th[i] for i in still])
        sc = best_corr(S, S)
        parent = list(range(len(still)))

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]; x = parent[x]
            return x
        md5s = df.md5.values
        for a in range(len(still)):
            for b in np.where(sc[a] >= cfg.corr_dup)[0]:
                if b > a:
                    ra, rb = find(a), find(int(b))
                    if ra != rb:
                        parent[max(ra, rb)] = min(ra, rb)
        for a, i in enumerate(still):
            g = find(a)
            if g != a:
                df.at[i, "status"] = "duplicate_of_other_unlabeled"
                df.at[i, "matched_with"] = df.at[still[g], "path"]
    df["eligible"] = df.status == "ok"
    df.to_csv(P.reports / "unlabeled_index.csv", index=False)
    df[~df.eligible & (df.status != "ok")].to_csv(P.reports / "overlap_report.csv", index=False)
    vc = df.status.value_counts()
    log("INDEX: " + " | ".join("%s: %d" % (k, v) for k, v in vc.items()))
    log("eligible for pseudo-labelling: %d of %d" % (int(df.eligible.sum()), len(df)))
    return df


# ------------------------------------------------------------------------------------------------ 3. audit / train / compare
def get_variant(cfg, name=None):
    name = name or cfg.active_variant
    for v in cfg.variants:
        if v["name"] == name:
            return v
    raise KeyError("unknown variant %s" % name)


def weights_path(cfg, name=None):
    return paths(cfg).runs / get_variant(cfg, name)["name"] / "weights" / "best.pt"


def box_audit(cfg):
    """Public boxes (v1 train) versus your hand boxes: do they follow the same rule? Look at this BEFORE training."""
    P = paths(cfg)
    def stats(label_files, name):
        n_ph = n_bx = big = tiny = 0; areas = []
        for f in label_files:
            bx = read_yolo(f)
            if not bx:
                continue
            n_ph += 1; n_bx += len(bx)
            a = [b[2] * b[3] for b in bx]; areas += a
            big += any(x > 0.5 for x in a); tiny += any(min(b[2], b[3]) < 0.02 for b in bx)
        areas = np.array(areas or [0])
        return {"source": name, "photos": n_ph, "boxes": n_bx, "boxes_per_photo": round(n_bx / max(1, n_ph), 2),
                "median_box_area_%": round(100 * float(np.median(areas)), 2), "p90_box_area_%": round(100 * float(np.percentile(areas, 90)), 2),
                "photos_with_box_over_50%": big, "photos_with_tiny_box": tiny}
    rows = [stats([p for p in (P.dataset / "train" / "labels").glob("*.txt") if not p.name.startswith("hand_")], "public (training)"),
            stats([p for p in (P.dataset / "train" / "labels").glob("hand_*.txt") if "_rep" not in p.name], "your hand boxes (training)"),
            stats(sorted((P.dataset / "hand_test" / "labels").glob("*.txt")), "your hand boxes (hand test)")]
    df = pd.DataFrame(rows)
    df.to_csv(P.reports / "box_audit.csv", index=False)
    return df


def train(cfg, variant=None, log=print, resume=False):    # resume is kept for compatibility: an unfinished run is always resumed
    from ultralytics import YOLO
    import torch
    P = paths(cfg)
    v = get_variant(cfg, variant)
    best = weights_path(cfg, v["name"])
    last = best.parent / "last.pt"
    done = best.parent.parent / "DONE"                              # written only when training really finished
    if done.exists():
        log("[%s] already trained -> skipped: %s (delete the run folder to train again)" % (v["name"], best))
        return best
    if last.exists():                                               # interrupted run: continue where it stopped
        log("[%s] unfinished run found -> resuming from %s" % (v["name"], last))
        try:
            YOLO(str(last)).train(resume=True)
        except Exception as e:                                      # runs that finished before the DONE marker existed
            if "finished" not in str(e).lower() and "nothing to resume" not in str(e).lower():
                raise
            log("[%s] was already finished" % v["name"])
    else:
        m = YOLO(v["model"])
        m.train(data=str(P.dataset / "data.yaml"), imgsz=v["imgsz"], epochs=cfg.epochs, patience=cfg.patience, batch=v["batch"],
                device=0 if torch.cuda.is_available() else "cpu", workers=cfg.workers, seed=cfg.seed, cos_lr=True, close_mosaic=10,
                project=str(P.runs), name=v["name"], exist_ok=True, fliplr=0.5, flipud=0.0, degrees=10, scale=0.5, hsv_h=0.015, hsv_s=0.5, hsv_v=0.35)
    assert best.exists(), "training did not produce best.pt"
    done.write_text(time.strftime("%Y-%m-%d %H:%M:%S"))
    log("[%s] best weights: %s" % (v["name"], best))
    return best


def _iou_matrix(a, b):
    """a: (n,4), b: (m,4) in xyxy."""
    if len(a) == 0 or len(b) == 0:
        return np.zeros((len(a), len(b)), np.float32)
    ix0 = np.maximum(a[:, None, 0], b[None, :, 0]); iy0 = np.maximum(a[:, None, 1], b[None, :, 1])
    ix1 = np.minimum(a[:, None, 2], b[None, :, 2]); iy1 = np.minimum(a[:, None, 3], b[None, :, 3])
    inter = np.clip(ix1 - ix0, 0, None) * np.clip(iy1 - iy0, 0, None)
    aa = (a[:, 2] - a[:, 0]) * (a[:, 3] - a[:, 1]); ab = (b[:, 2] - b[:, 0]) * (b[:, 3] - b[:, 1])
    return inter / (aa[:, None] + ab[None, :] - inter + 1e-9)


def _ap(conf, tp, n_gt):
    if n_gt == 0 or len(conf) == 0:
        return 0.0
    o = np.argsort(-conf); tp = tp[o]
    tpc = np.cumsum(tp); fpc = np.cumsum(1 - tp)
    rec = tpc / n_gt; prec = tpc / (tpc + fpc)
    mrec = np.concatenate([[0], rec, [1]]); mpre = np.concatenate([[1], prec, [0]])
    mpre = np.flip(np.maximum.accumulate(np.flip(mpre)))
    x = np.linspace(0, 1, 101)
    return float(np.trapezoid(np.interp(x, mrec, mpre), x)) if hasattr(np, "trapezoid") else float(np.trapz(np.interp(x, mrec, mpre), x))


def score_model(weights, imgsz, split_dir, class_id=None, batch=16):
    """Same scoring for every model: one-class ground truth, predictions at conf 0.001 (class_id keeps only that class of a multi-class model).
    Returns dict with AP50, AP50-95 and the raw arrays needed for precision/recall at any confidence."""
    from ultralytics import YOLO
    import torch
    model = YOLO(str(weights))
    imgs = list_images(Path(split_dir) / "images")
    thr = np.arange(0.5, 0.96, 0.05)
    confs, tps, n_gt = [], [[] for _ in thr], 0
    dev = 0 if torch.cuda.is_available() else "cpu"
    for s in range(0, len(imgs), batch):
        chunk = imgs[s:s + batch]
        res = model.predict([str(p) for p in chunk], imgsz=imgsz, conf=0.001, iou=0.6, max_det=300, verbose=False, device=dev)
        for p, r in zip(chunk, res):
            h, w = r.orig_shape
            g = np.array([[(cx - bw / 2) * w, (cy - bh / 2) * h, (cx + bw / 2) * w, (cy + bh / 2) * h] for cx, cy, bw, bh in read_yolo(Path(split_dir) / "labels" / (p.stem + ".txt"))], np.float32).reshape(-1, 4)
            n_gt += len(g)
            if r.boxes is None or len(r.boxes) == 0:
                continue
            b = r.boxes.xyxy.cpu().numpy(); c = r.boxes.conf.cpu().numpy(); k = r.boxes.cls.cpu().numpy().astype(int)
            if class_id is not None:
                keep = k == class_id; b, c = b[keep], c[keep]
            o = np.argsort(-c); b, c = b[o], c[o]
            iou = _iou_matrix(b, g)
            confs.append(c)
            for ti, t in enumerate(thr):
                used = set(); flag = np.zeros(len(b))
                for i in range(len(b)):
                    if iou.shape[1] == 0:
                        break
                    cand = [(iou[i, j], j) for j in range(iou.shape[1]) if j not in used and iou[i, j] >= t]
                    if cand:
                        j = max(cand)[1]; used.add(j); flag[i] = 1
                tps[ti].append(flag)
    conf = np.concatenate(confs) if confs else np.zeros(0)
    tp = [np.concatenate(t) if t else np.zeros(0) for t in tps]
    aps = [_ap(conf, t, n_gt) for t in tp]
    return dict(AP50=aps[0], AP50_95=float(np.mean(aps)), conf=conf, tp50=tp[0], n_gt=n_gt, n_photos=len(imgs))


def at_conf(res, c):
    m = res["conf"] >= c
    tp = float(res["tp50"][m].sum()); n = int(m.sum())
    P_ = tp / n if n else 0.0; R_ = tp / res["n_gt"] if res["n_gt"] else 0.0
    return P_, R_, (2 * P_ * R_ / (P_ + R_) if P_ + R_ else 0.0)


def best_f1_conf(res):
    cs = np.arange(0.05, 0.9, 0.025)
    return float(max(cs, key=lambda c: at_conf(res, c)[2]))


def compare(cfg, variants=None, log=print):
    """v1 (3-class, in the app) vs the one-class variants on the SAME photos: v1 val, v1 test and your hand-boxed test set.
    The operating confidence of each new model is the best-F1 value on VALIDATION; v1 uses the app's 0.25."""
    P = paths(cfg)
    names = variants or [v["name"] for v in cfg.variants]
    sets = [("v1 validation", P.dataset / "val"), ("v1 test", P.dataset / "test")]
    if (P.dataset / "hand_test" / "images").exists() and list_images(P.dataset / "hand_test" / "images"):
        sets.append(("hand-boxed test", P.dataset / "hand_test"))
    models = []
    if cfg.v1_model and Path(cfg.v1_model).exists():
        models.append(("v1 (3-class, in the app)", cfg.v1_model, 640, 1, float(cfg.v1_app_conf)))
    for n in names:
        w = weights_path(cfg, n)
        if w.exists():
            models.append((n, w, get_variant(cfg, n)["imgsz"], None, None))
    rows = []
    for name, w, sz, cid, conf in models:
        scored = {s: score_model(w, sz, d, cid) for s, d in sets}
        if conf is None:
            conf = best_f1_conf(scored["v1 validation"])
        for s, _ in sets:
            r = scored[s]; p_, r_, f_ = at_conf(r, conf)
            rows.append({"model": name, "set": s, "photos": r["n_photos"], "boxes": r["n_gt"], "mAP@0.5": round(r["AP50"], 3), "mAP@0.5-0.95": round(r["AP50_95"], 3),
                         "conf used": round(conf, 3), "precision": round(p_, 3), "recall": round(r_, 3), "F1": round(f_, 3)})
        log("scored %s" % name)
    df = pd.DataFrame(rows)
    df.to_csv(P.reports / "model_comparison.csv", index=False)
    return df


def evaluate(cfg, log=print):
    return compare(cfg, variants=[cfg.active_variant], log=log)


# ------------------------------------------------------------------------------------------------ 4. annotate (batch, resume)
SUMMARY_COLS = ["image", "status", "n_accepted", "n_uncertain", "max_conf", "min_accepted_conf", "warnings", "use_for_training", "preview"]


def _draw(img, boxes, cfg):
    h, w = img.shape[:2]
    for (cx, cy, bw, bh, cf, tier) in boxes:
        x0, y0, x1, y1 = int((cx - bw / 2) * w), int((cy - bh / 2) * h), int((cx + bw / 2) * w), int((cy + bh / 2) * h)
        col = (60, 200, 60) if tier == "accepted" else (0, 160, 255)
        th = max(2, int(round(max(h, w) / 400)))
        if tier == "accepted":
            cv2.rectangle(img, (x0, y0), (x1, y1), col, th)
        else:                                                              # uncertain boxes are dashed
            seg = 8
            for xs in range(x0, x1, seg * 2):
                cv2.line(img, (xs, y0), (min(xs + seg, x1), y0), col, th); cv2.line(img, (xs, y1), (min(xs + seg, x1), y1), col, th)
            for ys in range(y0, y1, seg * 2):
                cv2.line(img, (x0, ys), (x0, min(ys + seg, y1)), col, th); cv2.line(img, (x1, ys), (x1, min(ys + seg, y1)), col, th)
        cv2.putText(img, "%.2f" % cf, (x0 + 2, max(12, y0 - 3)), cv2.FONT_HERSHEY_SIMPLEX, max(0.4, h / 1200), col, 1, cv2.LINE_AA)
    return img


def annotate(cfg, limit=None, log=print):
    """Batch pseudo-labelling with resume. Re-running continues where it stopped (images already in images_summary.csv are skipped)."""
    from ultralytics import YOLO
    import torch
    _assert_outputs_safe(cfg)
    P = paths(cfg)
    idx_path = P.reports / "unlabeled_index.csv"
    if not idx_path.exists():
        raise RuntimeError("run index_unlabeled first")
    df = pd.read_csv(idx_path)
    todo = df[df.eligible | ((df.status == "duplicate_of_other_unlabeled") & bool(cfg.process_duplicates))]
    best = weights_path(cfg)
    model = YOLO(str(best))
    (P.pseudo / "labels").mkdir(parents=True, exist_ok=True)
    (P.pseudo / "confidences").mkdir(parents=True, exist_ok=True)
    sum_csv, box_csv = P.pseudo / "images_summary.csv", P.pseudo / "boxes.csv"
    done = set(pd.read_csv(sum_csv).image) if sum_csv.exists() else set()
    paths_todo = [p for p in todo.path if Path(p).name not in done]
    if limit:
        paths_todo = paths_todo[:int(limit)]
    log("eligible: %d | already processed: %d | to do now: %d" % (len(todo), len(done), len(paths_todo)))
    new_sum, new_box = not sum_csv.exists(), not box_csv.exists()
    fs, fb = open(sum_csv, "a", newline="", encoding="utf-8"), open(box_csv, "a", newline="", encoding="utf-8")
    ws, wb = csv.writer(fs), csv.writer(fb)
    if new_sum: ws.writerow(SUMMARY_COLS)
    if new_box: wb.writerow(["image", "box", "cx", "cy", "w", "h", "conf", "tier"])
    names_seen = set()
    t0 = time.time()
    for s in range(0, len(paths_todo), int(cfg.annotate_batch)):
        batch = paths_todo[s:s + int(cfg.annotate_batch)]
        res = model.predict(batch, imgsz=get_variant(cfg)["imgsz"], conf=cfg.conf_min, iou=cfg.nms_iou, max_det=cfg.max_det, augment=bool(cfg.tta), verbose=False,
                            device=0 if torch.cuda.is_available() else "cpu")
        for pth, r in zip(batch, res):
            name = Path(pth).name
            if name in names_seen:                                       # two files with the same name in different folders: add the folder name
                name = Path(pth).parent.name + "__" + name
            names_seen.add(name)
            boxes = []
            if r.boxes is not None and len(r.boxes):
                for (cx, cy, w, h), cf in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy()):
                    boxes.append((float(cx), float(cy), float(w), float(h), float(cf), "accepted" if cf >= cfg.conf_accept else "uncertain"))
            acc = [b for b in boxes if b[5] == "accepted"]
            unc = [b for b in boxes if b[5] == "uncertain"]
            flags = []
            if len(boxes) >= cfg.many_boxes_flag: flags.append("many_boxes")
            if any(b[2] > cfg.huge_box_frac and b[3] > cfg.huge_box_frac for b in boxes): flags.append("huge_box")
            if any(min(b[2], b[3]) < cfg.tiny_box_frac for b in boxes): flags.append("tiny_box")
            if acc and max(b[4] for b in acc) < cfg.conf_high: flags.append("weak")
            if not boxes:
                status = "no_detection"                       # NOT healthy skin: the model simply found nothing
            elif unc or flags or not acc:
                status = "uncertain"
            else:
                status = "accepted"
            stem = Path(name).stem
            if acc:
                (P.pseudo / "labels" / (stem + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b[:4] for b in acc))
            if boxes:
                (P.pseudo / "confidences" / (stem + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f %.4f %s\n" % (*b[:5], b[5]) for b in boxes))
            for k, b in enumerate(boxes):
                wb.writerow([name, k, "%.6f" % b[0], "%.6f" % b[1], "%.6f" % b[2], "%.6f" % b[3], "%.4f" % b[4], b[5]])
            prev = ""
            if cfg.preview_mode == "all" or (cfg.preview_mode == "uncertain" and status != "accepted"):
                img = cv2.imdecode(np.fromfile(str(pth), np.uint8), cv2.IMREAD_COLOR)
                if img is not None:
                    sc = cfg.preview_max_side / max(img.shape[:2])
                    if sc < 1:
                        img = cv2.resize(img, (int(img.shape[1] * sc), int(img.shape[0] * sc)), interpolation=cv2.INTER_AREA)
                    img = _draw(img, boxes, cfg)
                    d = P.previews / status; d.mkdir(parents=True, exist_ok=True)
                    cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, 82])[1].tofile(str(d / (stem + ".jpg")))
                    prev = "%s/%s.jpg" % (status, stem)
            ws.writerow([name, status, len(acc), len(unc), "%.4f" % (max([b[4] for b in boxes]) if boxes else 0), "%.4f" % (min([b[4] for b in acc]) if acc else 0),
                         ";".join(flags), status == "accepted", prev])
        fs.flush(); fb.flush()                                          # a batch is saved before the next one starts: safe to stop at any time
        log("  processed %d / %d  (%.0fs)" % (min(s + int(cfg.annotate_batch), len(paths_todo)), len(paths_todo), time.time() - t0))
    fs.close(); fb.close()
    return summarize(cfg, log=log)


# ------------------------------------------------------------------------------------------------ 5. summary and review
def summarize(cfg, log=print):
    P = paths(cfg)
    sm = pd.read_csv(P.pseudo / "images_summary.csv")
    bx = pd.read_csv(P.pseudo / "boxes.csv") if (P.pseudo / "boxes.csv").exists() else pd.DataFrame(columns=["tier", "conf"])
    out = dict(images_processed=int(len(sm)), images_accepted=int((sm.status == "accepted").sum()), images_uncertain=int((sm.status == "uncertain").sum()),
               images_no_detection=int((sm.status == "no_detection").sum()), boxes_total=int(len(bx)), boxes_accepted=int((bx.tier == "accepted").sum()),
               boxes_uncertain=int((bx.tier == "uncertain").sum()), images_usable_for_training=int(sm.use_for_training.astype(str).str.lower().eq("true").sum()),
               thresholds=dict(conf_min=cfg.conf_min, conf_accept=cfg.conf_accept, conf_high=cfg.conf_high))
    if len(bx):
        out["accepted_box_confidence"] = {k: round(float(v), 3) for k, v in bx[bx.tier == "accepted"].conf.describe(percentiles=[.1, .5, .9]).items() if k in ("mean", "10%", "50%", "90%")}
    out["images_per_flag"] = {f: int(sm["warnings"].fillna("").str.contains(f).sum()) for f in ("many_boxes", "huge_box", "tiny_box", "weak")}
    json.dump(out, open(P.reports / "annotation_summary.json", "w"), indent=2)
    review = sm[sm.status != "accepted"].copy()
    review["reason"] = np.where(review.status == "no_detection", "no detection (NOT necessarily healthy skin)", "uncertain boxes / flags: " + review["warnings"].fillna(""))
    review["priority"] = np.where(review.status == "uncertain", 0, 1)
    review.sort_values(["priority", "max_conf"], ascending=[True, False]).drop(columns=["priority"]).to_csv(P.reports / "review_queue.csv", index=False)
    log("SUMMARY: processed %d | accepted %d | uncertain %d | no detection %d | boxes: %d accepted + %d uncertain | usable for training: %d" % (
        out["images_processed"], out["images_accepted"], out["images_uncertain"], out["images_no_detection"], out["boxes_accepted"], out["boxes_uncertain"],
        out["images_usable_for_training"]))
    return out


def make_review_folder(cfg, log=print):
    """Folder for lesion_box_tool.py: the uncertain / no-detection photos with the model's boxes as a starting point (drafts)."""
    P = paths(cfg)
    q = pd.read_csv(P.reports / "review_queue.csv").head(int(cfg.review_max))
    idx = pd.read_csv(P.reports / "unlabeled_index.csv")
    src = {Path(p).name: p for p in idx.path}
    d = P.review
    if d.exists():
        shutil.rmtree(d)
    (d / "images").mkdir(parents=True); (d / "labels").mkdir(parents=True); (d.parent / "review_drafts").mkdir(exist_ok=True)
    for r in q.itertuples():
        pth = src.get(r.image)
        if not pth:
            continue
        shutil.copy2(pth, d / "images" / r.image)
        conf_file = P.pseudo / "confidences" / (Path(r.image).stem + ".txt")
        lines = []
        if conf_file.exists():
            for ln in conf_file.read_text().splitlines():
                t = ln.split()
                lines.append("0 %s %s %s %s\n" % tuple(t[1:5]))
        (d / "labels" / (Path(r.image).stem + ".txt")).write_text("".join(lines))
    (d.parent / "_drafts").mkdir(exist_ok=True)
    time.sleep(1.2)
    json.dump({"drafted_at": time.time(), "note": "review folder for uncertain / no-detection photos"}, open(d.parent / "_drafts" / "meta.json", "w"))
    (d / "classes.txt").write_text(CLASS_NAME + "\n")
    log("review folder: %s (%d photos). Open with:  py -3.13 lesion_box_tool.py \"<that folder>\" --label hyperpigmentation" % (d, len(q)))
    return d


# ------------------------------------------------------------------------------------------------ command line
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("step", choices=["build", "audit", "index", "train", "compare", "evaluate", "annotate", "summary", "review"])
    ap.add_argument("--limit", type=int, default=None, help="annotate only this many photos (trial run)")
    ap.add_argument("--resume", action="store_true", help="resume an interrupted training run")
    ap.add_argument("--set", action="append", default=[], metavar="key=value", help="override a setting, e.g. --set conf_accept=0.5")
    a = ap.parse_args()
    cfg = default_config()
    for kv in a.set:
        k, v = kv.split("=", 1)
        old = cfg.get(k)
        cfg[k] = type(old)(v) if isinstance(old, (int, float, str)) and old is not None else (Path(v) if isinstance(old, Path) else v)
    if a.step == "train":
        for v in cfg.variants:
            train(cfg, variant=v["name"], resume=a.resume)
    elif a.step == "annotate":
        annotate(cfg, limit=a.limit)
    else:
        {"build": build_dataset, "audit": box_audit, "compare": compare, "index": index_unlabeled, "evaluate": evaluate, "summary": summarize, "review": make_review_folder}[a.step](cfg)


if __name__ == "__main__":
    main()
