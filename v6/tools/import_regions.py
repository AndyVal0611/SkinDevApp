"""Import the researchers' existing YOLO boxes into V6's region_annotations.csv.

  python import_regions.py            dry run: counts and a report only, nothing in the annotation folder is touched
  python import_regions.py --apply    backs up region_annotations.csv, then appends the importable boxes

What is imported (each box image must be byte-identical (MD5) to the V6 image, so normalised coordinates carry over exactly):
  acne               audit_results/train_validation_box_review_candidates.csv (acne dataset)      -> reviewer R01
  hyperpigmentation  same list; the rule-v2 folder wins when an image is in both folders            -> reviewer R01
  eczema             teammate package boxes/Andrea__eczema_R3_{train200,b2..b7} (RULE 3 labels)     -> reviewer R02
Only V6 `train` and `validation` images are imported (ROI training uses nothing else). Boxes are skipped when the V6 image label for
that condition is `absent`, when they fall outside the photo, or when the same image-condition was already imported from a higher-priority
folder. Not imported: compiled_*/train_ready_* (old dot labels), machine_only_add_for_runB (machine drafts), Andrea__eczema_rebox and
*_reliability (second passes of the same photos), the _stale_b7 folder, patch_v1_labels.
Every imported row is status `verified`, role `researcher`, source `manual_box`, with a note that the boxes were drawn by the reviewer with
AI-assisted consistency and reviewed. The `background_false_positive` class is NOT created here: it needs a human review of detector false alarms.
"""
import argparse, collections, csv, datetime, hashlib, os, shutil, sys

ap = argparse.ArgumentParser(); ap.add_argument("--apply", action="store_true"); args = ap.parse_args()
WSL = r"\\wsl.localhost\Ubuntu\home\bautista\PrecisionSkinV1"
ANN_DIR = WSL + r"\v6\annotations"
REGION_CSV = ANN_DIR + r"\region_annotations.csv"
AUDIT = r"C:\Users\Bautista\source\repos\SkinDevApp\v6\audit_results\train_validation_box_review_candidates.csv"
PKG = r"C:\Users\Bautista\Downloads\PrecisionSkin\Eczema_Boxing\Eczema_for_V6\Eczema_for_V6\boxes"
REPORT = r"C:\Users\Bautista\source\repos\SkinDevApp\v6\annotations\region_import_report.csv"
ECZEMA_ORDER = ["Andrea__eczema_R3_train200"] + ["Andrea__eczema_R3_b%d" % i for i in range(2, 8)]
HYPER_PRIORITY = ["hyperpigmentation_candidate_v2_train", "hyperpigmentation.v2i.yolov8"]
EXT = (".jpg", ".jpeg", ".png", ".webp", ".bmp", ".JPG", ".JPEG", ".PNG")
COLS = ["region_id", "image_id", "x0", "y0", "x1", "y1", "label", "exhaustive", "review_status", "reviewer_id", "reviewer_role", "annotation_source", "reviewed_at", "notes"]
md5 = lambda p: hashlib.md5(open(p, "rb").read()).hexdigest()
win = lambda p: p.replace("/mnt/c/", "C:\\").replace("/", "\\") if p.startswith("/mnt/c/") else p

man = {r["image_id"]: r for r in csv.DictReader(open(WSL + r"\metadata\metadata\split_manifest.csv", encoding="utf-8"))}
ann = {r["image_id"]: r for r in csv.DictReader(open(ANN_DIR + r"\multilabel_annotations.csv", encoding="utf-8"))}
by_md5 = {man[i]["md5"]: i for i in ann if i in man}
stat = collections.Counter(); skipped = collections.Counter(); picked = {}  # (image_id, condition) -> (priority, source, [boxes], label_file)


def boxes_of(path):
    out = []
    for line in open(path, encoding="utf-8", errors="replace"):
        t = line.split()
        if len(t) < 5: continue
        cx, cy, w, h = map(float, t[1:5]); x0, y0, x1, y1 = cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2
        if x0 < -0.002 or y0 < -0.002 or x1 > 1.002 or y1 > 1.002 or x1 <= x0 or y1 <= y0:
            stat["box outside the photo (skipped)"] += 1; continue
        out.append((max(0.0, x0), max(0.0, y0), min(1.0, x1), min(1.0, y1)))
    return out


def consider(image_id, cond, prio, source, label_file, reviewer):
    a = ann.get(image_id)
    if not a or a["split"] not in ("train", "validation"):
        stat["image not in V6 train/validation"] += 1; return
    if a[cond] == "absent":
        skipped[(a["split"], cond)] += 1; stat["conflicts with the researcher's image label (absent)"] += 1; return
    bx = boxes_of(label_file)
    if not bx: stat["label file without boxes"] += 1; return
    k = (image_id, cond)
    if k in picked and picked[k][0] <= prio:
        stat["duplicate image-condition from a lower-priority folder"] += 1; return
    picked[k] = (prio, source, bx, label_file, reviewer)


# acne + hyperpigmentation from the audit list
for r in csv.DictReader(open(AUDIT, encoding="utf-8")):
    src = r["source"]; cond = "acne" if "acne" in src else "hyperpigmentation" if "hyper" in src else None
    if cond is None or r["image_id"] not in man: continue
    lf = win(r["box_file"]); stem = os.path.splitext(os.path.basename(lf))[0]; imgdir = os.path.join(os.path.dirname(os.path.dirname(lf)), "images")
    img = next((os.path.join(imgdir, stem + e) for e in EXT if os.path.exists(os.path.join(imgdir, stem + e))), None)
    if not os.path.exists(lf) or img is None: stat["box file or image missing"] += 1; continue
    if md5(img) != man[r["image_id"]]["md5"]: stat["box image differs from the V6 image (skipped)"] += 1; continue
    prio = HYPER_PRIORITY.index(src) if cond == "hyperpigmentation" and src in HYPER_PRIORITY else 0
    consider(r["image_id"], cond, prio, src, lf, "R01")

# eczema from the teammate package
for prio, ds in enumerate(ECZEMA_ORDER):
    root = os.path.join(PKG, ds)
    for rt, dirs, fs in os.walk(root):
        dirs[:] = [d for d in dirs if not d.startswith("_stale")]
        if os.path.basename(rt) != "labels": continue
        imgdir = os.path.join(os.path.dirname(rt), "images")
        for f in fs:
            if not f.endswith(".txt"): continue
            stem = os.path.splitext(f)[0]
            img = next((os.path.join(imgdir, stem + e) for e in EXT if os.path.exists(os.path.join(imgdir, stem + e))), None)
            if img is None: stat["eczema label without image"] += 1; continue
            iid = by_md5.get(md5(img))
            if iid is None: stat["eczema image not in the 9,789 frozen V6 images"] += 1; continue
            consider(iid, "eczema", prio, ds, os.path.join(rt, f), "R02")

rows = []; per = collections.Counter(); per_src = collections.Counter(); n = collections.Counter()
for (iid, cond), (prio, src, bx, lf, rev) in sorted(picked.items()):
    when = datetime.datetime.fromtimestamp(os.path.getmtime(lf)).isoformat(timespec="seconds")
    for b in bx:
        n[rev + cond] += 1
        rows.append(dict(region_id="%s-%s-%06d" % (rev, cond[:4], n[rev + cond]), image_id=iid, x0="%.5f" % b[0], y0="%.5f" % b[1], x1="%.5f" % b[2], y1="%.5f" % b[3],
                         label=cond, exhaustive="", review_status="verified", reviewer_id=rev, reviewer_role="researcher", annotation_source="manual_box", reviewed_at=when,
                         notes="boxed by the reviewer with AI-assisted consistency, then reviewed; source folder %s; imported 2026-11-02; reviewed_at = last edit time of the label file" % src))
        per[(ann[iid]["split"], cond)] += 1; per_src[src] += 1
print("== skipped / notes"); [print("  ", k, v) for k, v in stat.items()]
print("== conflicts skipped by split/condition:", dict(skipped))
print("== boxes to import by split/condition:"); [print("  ", k, v) for k, v in sorted(per.items())]
print("== by source folder:", dict(per_src)); print("== images with boxes:", len({r["image_id"] for r in rows}), "| boxes:", len(rows))
with open(REPORT, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["item", "key", "count"])
    for k, v in stat.items(): w.writerow(["skipped", k, v])
    for k, v in sorted(per.items()): w.writerow(["boxes_by_split_condition", "|".join(k), v])
    for k, v in per_src.items(): w.writerow(["boxes_by_source", k, v])
if not args.apply:
    print("DRY RUN: nothing written to", REGION_CSV); sys.exit(0)
existing = list(csv.DictReader(open(REGION_CSV, encoding="utf-8")))
if existing: sys.exit("region_annotations.csv already has %d rows: refusing to append (review it first)" % len(existing))
bak = REGION_CSV + ".bak_" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S"); shutil.copy2(REGION_CSV, bak)
tmp = REGION_CSV + ".tmp"
with open(tmp, "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=COLS); w.writeheader(); w.writerows(rows)
os.replace(tmp, REGION_CSV)
chk = list(csv.DictReader(open(REGION_CSV, encoding="utf-8")))
ok = (len(chk) == len(rows) and len({r["region_id"] for r in chk}) == len(chk) and all(0 <= float(r["x0"]) < float(r["x1"]) <= 1 and 0 <= float(r["y0"]) < float(r["y1"]) <= 1 for r in chk)
      and all(r["image_id"] in man for r in chk) and {r["label"] for r in chk} <= {"acne", "hyperpigmentation", "eczema"})
print("APPLIED: backup", bak, "| rows written", len(chk), "| validation checks pass:", ok)
