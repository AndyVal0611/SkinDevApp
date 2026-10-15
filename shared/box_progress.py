#!/usr/bin/env python
"""
box_progress.py - see how far each annotator is (python standard library only, nothing is uploaded).

Usage (Windows PowerShell):
    py -3.13 box_progress.py "C:\\Users\\Bautista\\Documents\\PrecisionSkin_DetectorOwn"

The folder is the one made by the detector v3 notebook (section 02). It contains folders like  Elaiza__acne,
Francesca__eczema ...  (each with images\\ and labels\\), test_assignments.csv and _drafts\\meta.json.
Put each person's returned folders (or a synced Google Drive / OneDrive folder) there, then run this.

It prints, per person and condition: photos saved / assigned, photos with no lesion (X), boxes per photo,
average seconds per photo (from labeling_log.csv written by lesion_box_tool.py) and simple warnings.
"""
import csv
import json
import os
import statistics as st
import sys
import time


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    root = os.path.abspath(sys.argv[1])
    try:
        t_ref = float(json.load(open(os.path.join(root, "_drafts", "meta.json")))["drafted_at"])
    except Exception:
        t_ref = 0.0
    folders = sorted(d for d in os.listdir(root) if "__" in d and os.path.isdir(os.path.join(root, d, "images")))
    if not folders:
        sys.exit("No <Name>__<condition> folders with an images folder found in " + root)

    rows = []
    for d in folders:
        annot, cond = d.split("__", 1)
        imgs = sorted(f for f in os.listdir(os.path.join(root, d, "images")))
        lab_dir = os.path.join(root, d, "labels")
        saved = empty = boxes = 0
        last = 0.0
        per_photo = []
        for f in imgs:
            lp = os.path.join(lab_dir, os.path.splitext(f)[0] + ".txt")
            if not os.path.exists(lp) or os.path.getmtime(lp) <= t_ref + 1:
                continue
            saved += 1
            last = max(last, os.path.getmtime(lp))
            n = sum(1 for ln in open(lp, encoding="utf-8") if len(ln.split()) == 5)
            if n == 0:
                empty += 1
            else:
                boxes += n
                per_photo.append(n)
        secs = []
        lg = os.path.join(root, d, "labeling_log.csv")
        if os.path.exists(lg):
            seen = {}
            for r in csv.DictReader(open(lg, encoding="utf-8")):
                try:
                    if r.get("action") in ("save", "none"):
                        seen[r["image"]] = float(r["seconds"])      # last time per photo
                except Exception:
                    pass
            secs = list(seen.values())
        rows.append(dict(annotator=annot, cond=cond, assigned=len(imgs), saved=saved, empty=empty, boxes=boxes,
                         bpp=(boxes / max(1, saved - empty)), sec=(st.mean(secs) if secs else None), last=last))

    print("\n%-10s %-18s %-9s %-6s %-12s %-10s %-10s %s" % ("person", "condition", "saved", "X", "boxes/photo", "sec/photo", "last save", "notes"))
    print("-" * 100)
    by_cond = {}
    for r in rows:
        by_cond.setdefault(r["cond"], []).append(r["bpp"] if r["saved"] - r["empty"] > 0 else None)
    for r in rows:
        notes = []
        med = [x for x in by_cond[r["cond"]] if x]
        if r["saved"] == 0:
            notes.append("not started")
        elif r["saved"] < r["assigned"]:
            notes.append("%d to go" % (r["assigned"] - r["saved"]))
        else:
            notes.append("DONE")
        if r["saved"] >= 5:
            if r["empty"] / r["saved"] > 0.4:
                notes.append("many 'no lesion' photos")
            if med and len(med) > 1 and r["bpp"] > 0:
                m = st.median(med)
                if r["bpp"] > 2 * m or r["bpp"] < m / 2:
                    notes.append("boxes/photo differs a lot from the group (%.1f vs %.1f)" % (r["bpp"], m))
            if r["sec"] is not None and r["sec"] < 4:
                notes.append("very fast (<4 s/photo): check quality")
        last = time.strftime("%m-%d %H:%M", time.localtime(r["last"])) if r["last"] else "-"
        print("%-10s %-18s %-9s %-6s %-12s %-10s %-10s %s" % (r["annotator"], r["cond"], "%d/%d" % (r["saved"], r["assigned"]), r["empty"],
              ("%.1f" % r["bpp"]) if r["saved"] - r["empty"] > 0 else "-", ("%.0f" % r["sec"]) if r["sec"] is not None else "-", last, "; ".join(notes)))
    tot_s, tot_a = sum(r["saved"] for r in rows), sum(r["assigned"] for r in rows)
    print("\nTOTAL saved %d / %d photos (%.0f %%). Calibration photos appear under every person, so they are counted once per person." % (tot_s, tot_a, 100.0 * tot_s / max(1, tot_a)))


if __name__ == "__main__":
    main()
