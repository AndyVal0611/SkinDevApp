#!/usr/bin/env python
"""
merge_acne_dataset.py - one acne detection dataset from everything you boxed (class 0 = acne, YOLO format). Nothing is modified:
the output is a NEW folder (acne_dataset_v1) with copies.

Sources (a photo is used only if YOU reviewed it, i.e. its label was saved in the box tool after the folder was created):
  Elaiza__acne            your own boxing (training AND test photos are mixed there)
  Draft_Acne              photos pre-drawn by the helper and reviewed by you           (training photos)
  Test_Acne               the test photos you boxed from scratch                       (test photos)
  Fix_6_Test_Photos       six test photos you boxed again                              (test photos; wins over older versions)
  claude_boxed_Acne       NOT used (machine-drafted by Claude, never reviewed in the tool)

Rules
  * role (train / hand_test) comes from sprint_photos.csv. A test photo never goes to training, whatever folder it was boxed in.
  * a photo with several saved versions uses the newest one.
  * photos marked X (no boxes) are listed but not included: an empty label would claim "healthy skin".
  * training photos with 3+ doubled / nested boxes are left OUT of training (listed; fix them in the box tool and re-run).
    Test photos are never filtered or changed.
  * a validation split (12 % of training photos, fixed seed) is taken from the training photos only.
  * leak check: every test photo is compared with every training photo (32x32 thumbnails, 8 rotations/flips, correlation >= 0.90).
"""
import argparse, json, random, shutil
from pathlib import Path
import numpy as np, pandas as pd, cv2

ROOT = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing")
IMG = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def read_boxes(p):
    out = []
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
    return i / (a[2] * a[3] + b[2] * b[3] - i) if i > 0 else 0.0


def redundant(boxes):
    """number of boxes that duplicate, nest inside or hugely overlap a higher-ranked box (or are huge)."""
    bad = 0
    kept = []
    for b in sorted(boxes, key=lambda x: -(x[2] * x[3])):
        if (b[2] * b[3]) ** 0.5 > 0.40:   # close-up photos legitimately have large boxes; only a near-whole-image box is suspect
            bad += 1; continue
        dup = False
        for k in kept:
            ix = max(0, min(b[0] + b[2] / 2, k[0] + k[2] / 2) - max(b[0] - b[2] / 2, k[0] - k[2] / 2))
            iy = max(0, min(b[1] + b[3] / 2, k[1] + k[3] / 2) - max(b[1] - b[3] / 2, k[1] - k[3] / 2))
            if iou(b, k) > 0.50 or ix * iy / max(1e-9, min(b[2] * b[3], k[2] * k[3])) > 0.80:   # real doubling, not neighbouring spots that touch
                dup = True; break
        if dup: bad += 1
        else: kept.append(b)
    return bad


def thumb(p):
    im = cv2.imdecode(np.fromfile(str(p), np.uint8), cv2.IMREAD_GRAYSCALE)
    return None if im is None else cv2.resize(im, (32, 32), interpolation=cv2.INTER_AREA).astype(np.float32)


def zs(x):
    x = x.reshape(len(x), -1); x = x - x.mean(1, keepdims=True)
    return (x / (x.std(1, keepdims=True) + 1e-6) / np.sqrt(x.shape[1])).astype(np.float32)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=str(ROOT)); ap.add_argument("--out", default=None)
    ap.add_argument("--facial-only", action="store_true", help="leave out the reviewed 'non_facial' photos (an experiment: do they help or hurt?)")
    ap.add_argument("--no-drafted-nonfacial", action="store_true", help="leave out the 'non_facial' photos that came from machine drafts (they scored lower); non_facial photos you boxed from scratch (More_Faces) stay in")
    ap.add_argument("--scratch-val", action="store_true", help="validation photos are taken ONLY from photos you boxed from scratch (main / fixed / More_Faces), like the test set, never from helper drafts")
    ap.add_argument("--drop-leaks", action=argparse.BooleanOptionalAction, default=True); ap.add_argument("--val-frac", type=float, default=0.12); ap.add_argument("--seed", type=int, default=42)
    a = ap.parse_args()
    R = Path(a.root); OUT = Path(a.out) if a.out else R / "acne_dataset_v1"
    sel = pd.read_csv(R / "sprint_photos.csv").set_index("name")
    stem2name = {Path(n).stem: n for n in sel.index}

    # (images dir, labels dir, reference time, priority) - later in the list wins only when its label is newer
    srcs = [("main", R / "Elaiza__acne/images", R / "Elaiza__acne/labels", R / "_drafts/meta.json"),
            ("drafts", R / "Draft_Acne/Elaiza__acne_draft/images", R / "Draft_Acne/Elaiza__acne_draft/labels", R / "Draft_Acne/_drafts/meta.json"),
            ("test_folder", R / "Test_Acne/Elaiza__acne_TEST/images", R / "Test_Acne/Elaiza__acne_TEST/labels", R / "Test_Acne/_drafts/meta.json"),
            ("fix6", R / "Fix_6_Test_Photos/Elaiza__acne_fix/images", R / "Fix_6_Test_Photos/Elaiza__acne_fix/labels", R / "Fix_6_Test_Photos/_drafts/meta.json"),
            ("fix46", R / "Fix_46_Doubled_Boxes/Elaiza__acne_fix/images", R / "Fix_46_Doubled_Boxes/Elaiza__acne_fix/labels", R / "Fix_46_Doubled_Boxes/_drafts/meta.json"),
            ("more", R / "More_Faces/Elaiza__acne_more/images", R / "More_Faces/Elaiza__acne_more/labels", R / "More_Faces/_drafts/meta.json")]      # real faces from the non_facial pool, boxed from scratch
    versions = {}          # stem -> list of (mtime, source, nboxes, label path, image path)
    for name, idir, ldir, mf in srcs:
        if not ldir.exists() or not mf.exists():
            continue
        ref = json.load(open(mf))["drafted_at"]
        for lp in ldir.glob("*.txt"):
            if lp.stat().st_mtime <= ref + 1 or lp.stem not in stem2name:
                continue                                         # not reviewed by you
            img = next(iter(idir.glob(lp.stem + ".*")), None)
            if img is None:
                continue
            versions.setdefault(lp.stem, []).append((lp.stat().st_mtime, name, len(read_boxes(lp)), lp, img))

    rows = []
    for stem, v in versions.items():
        v.sort(key=lambda x: x[0])
        mt, src, nb, lp, img = v[-1]
        role = sel.role[stem2name[stem]]
        rows.append(dict(stem=stem, name=img.name, role=role, source=src, n_boxes=nb, versions=",".join(x[1] for x in v), label=str(lp), image=str(img),
                         redundant=redundant(read_boxes(lp)) if nb else 0))
    df = pd.DataFrame(rows)
    df["status"] = "included"
    df.loc[df.n_boxes == 0, "status"] = "marked X (no boxes) - not included"
    df.loc[df.role.isin(["train", "non_facial"]) & (df.redundant >= 3) & (df.n_boxes > 0), "status"] = "left out of training: %s+ doubled/nested boxes" % 3
    inc = df[df.status == "included"].copy()
    if a.facial_only:
        inc = inc[inc.role != "non_facial"].copy()
    if a.no_drafted_nonfacial:
        inc = inc[~((inc.role == "non_facial") & (inc.source == "drafts"))].copy()
    tr = inc[inc.role.isin(["train"] if a.facial_only else ["train", "non_facial"])].copy(); te = inc[inc.role == "hand_test"].copy()   # 'non_facial' = photos the face check missed that you reviewed; they are training photos

    rng = random.Random(a.seed)
    names = sorted(tr.stem); rng.shuffle(names)
    nval = max(10, int(a.val_frac * len(names)))
    if a.scratch_val:                                            # validation only from photos you boxed from scratch (same way as the test photos)
        scratch = set(tr[tr.source.isin(["main", "fix46", "more"])].stem)
        names = [s for s in names if s in scratch]
        print("scratch-boxed photos available for validation: %d (need %d)" % (len(names), nval))
    val_set = set(names[:nval])
    split = {s: "val" if s in val_set else "train" for s in tr.stem}
    split.update({s: "test" for s in te.stem})
    inc["split"] = inc.stem.map(split)

    if OUT.exists():
        shutil.rmtree(OUT)
    for sp in ("train", "val", "test"):
        (OUT / sp / "images").mkdir(parents=True); (OUT / sp / "labels").mkdir(parents=True)
    for r in inc.itertuples():
        shutil.copy2(r.image, OUT / r.split / "images" / r.name)
        shutil.copy2(r.label, OUT / r.split / "labels" / (r.stem + ".txt"))
    (OUT / "data.yaml").write_text("path: %s\ntrain: train/images\nval: val/images\ntest: test/images\nnames:\n  0: acne\n" % OUT)

    # leak check: test vs train + val
    leaks = []
    T = [(r.stem, thumb(OUT / "test" / "images" / r.name)) for r in inc[inc.split == "test"].itertuples()]
    O = [(r.stem, thumb(OUT / r.split / "images" / r.name)) for r in inc[inc.split != "test"].itertuples()]
    T = [(s, t) for s, t in T if t is not None]; O = [(s, t) for s, t in O if t is not None]
    if T and O:
        Ta = np.stack([t for _, t in T]); Oz = zs(np.stack([t for _, t in O]))
        best = np.full((len(T), len(O)), -1.0, np.float32)
        for k in range(4):
            for fl in (False, True):
                v = np.rot90(Ta, k, axes=(1, 2)); v = v[:, :, ::-1] if fl else v
                best = np.maximum(best, zs(np.ascontiguousarray(v)) @ Oz.T)
        for i, (s, _) in enumerate(T):
            j = int(best[i].argmax())
            if best[i, j] >= 0.90:
                leaks.append((s, O[j][0], float(best[i, j])))
    pd.DataFrame(leaks, columns=["test_photo", "similar_training_photo", "correlation"]).to_csv(OUT / "leak_check.csv", index=False)
    dropped = []
    if a.drop_leaks:                                  # the TEST photo stays; its look-alike is removed from training / validation
        for _, partner, _ in leaks:
            row = inc[inc.stem == partner]
            if len(row):
                r = row.iloc[0]
                for f in ((OUT / r.split / "images" / r["name"]), (OUT / r.split / "labels" / (r.stem + ".txt"))):
                    if f.exists(): f.unlink()
                dropped.append(partner)
        inc = inc[~inc.stem.isin(dropped)].copy()

    df.to_csv(OUT / "manifest_all_reviewed.csv", index=False)
    inc[["stem", "name", "split", "source", "n_boxes", "versions"]].to_csv(OUT / "manifest.csv", index=False)

    def cnt(sp):
        d = inc[inc.split == sp]; return len(d), int(d.n_boxes.sum())
    allt = sel[sel.role == "hand_test"]
    tests_reviewed = df[df.role == "hand_test"]
    lines = ["# Acne detection dataset v1 - merge report", "",
             "| Split | Photos | Boxes |", "|---|---|---|"]
    for sp in ("train", "val", "test"):
        p, b = cnt(sp); lines.append("| %s | %d | %d |" % (sp, p, b))
    lines += ["", "Class 0 = acne. YOLO format. data.yaml: `%s`" % (OUT / "data.yaml"), "",
              "## Decisions", "",
              "- Reviewed photos found: %d (train-role %d, test-role %d)." % (len(df), (df.role == "train").sum(), len(tests_reviewed)),
              "- Test photos in the project: %d | reviewed %d | with boxes (in the test split) %d | marked X (not included) %d | not reviewed %d." %
              (len(allt), len(tests_reviewed), cnt("test")[0], int(((tests_reviewed.n_boxes == 0)).sum()), len(allt) - len(tests_reviewed)),
              "- Photos marked X (no boxes), all roles: %d. Not included (an empty label would claim healthy skin)." % int((df.n_boxes == 0).sum()),
              "- Training photos left out for 3+ doubled/nested boxes: %d (see manifest_all_reviewed.csv, column status)." % int(df.status.str.startswith("left out").sum()),
              "- Photos that had several saved versions (newest used): %d." % int(df.versions.str.contains(",").sum()),
              "- Machine-drafted photos from `claude_boxed_Acne`: not used (never reviewed in the tool).",
              "- Leak check test vs train/val (correlation >= 0.90): **%d** test photos had a very similar training photo (leak_check.csv). Removed from training/validation: %d (the test photos were kept)." % (len(leaks), len(set(dropped))), "",
              "## Boxes per photo", "",
              "train %.1f | val %.1f | test %.1f" % tuple((inc[inc.split == s].n_boxes.mean() if (inc.split == s).any() else 0) for s in ("train", "val", "test"))]
    (OUT / "MERGE_REPORT.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    print("\nstatus counts:\n", df.status.value_counts().to_string())
    print("\nsource of the included photos:\n", inc.groupby(["split", "source"]).size().to_string())
    if leaks:
        print("\nLEAK CHECK: first pairs:", leaks[:6])


if __name__ == "__main__":
    main()
