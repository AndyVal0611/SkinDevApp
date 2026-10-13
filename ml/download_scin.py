#!/usr/bin/env python
"""
download_scin.py - downloads the part of the SCIN dataset that the skin-tone notebook uses (face / neck cases that have a Monk skin tone label).
SCIN: Google Research, public bucket dx-scin-public-data, "SCIN Data Use License" (read it; do not try to re-identify anyone; credit the dataset when you share results).

  python download_scin.py tables             # 2 small CSV files (about 2 MB)
  python download_scin.py plan              # how many cases / images would be downloaded and the approximate size (downloads nothing big)
  python download_scin.py images [--max N]  # the images (one per case), resumable: files that already exist are skipped

Everything goes to  Downloads\\MST_Data\\SCIN  (the folder the notebook expects):  scin_cases.csv, scin_labels.csv, dataset/images/...
Run it in Windows PowerShell or WSL with plain Python (no extra packages except pandas). Participant data stays out of git.
"""
import sys, time, argparse, urllib.request, urllib.error
from pathlib import Path
import pandas as pd

BASE = "https://storage.googleapis.com/dx-scin-public-data/"
DEST = Path.home() / "Downloads" / "MST_Data" / "SCIN"
if str(Path.home()).startswith("/home") and Path("/mnt/c/Users/Bautista/Downloads").exists():
    DEST = Path("/mnt/c/Users/Bautista/Downloads/MST_Data/SCIN")


def get(url, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    with urllib.request.urlopen(url, timeout=60) as r, open(tmp, "wb") as f:
        while True:
            b = r.read(1 << 16)
            if not b: break
            f.write(b)
    tmp.replace(dest)


def size_of(url):
    req = urllib.request.Request(url, method="HEAD")
    try:
        with urllib.request.urlopen(req, timeout=30) as r: return int(r.headers.get("Content-Length", 0))
    except Exception: return 0


def selection():
    cases = pd.read_csv(DEST / "scin_cases.csv"); labels = pd.read_csv(DEST / "scin_labels.csv")
    df = cases.merge(labels[["case_id", "monk_skin_tone_label_us", "monk_skin_tone_label_india", "gradable_for_monk_skin_tone_us", "gradable_for_monk_skin_tone_india"]], on="case_id", how="left")
    head = df["body_parts_head_or_neck"].astype(str).str.upper().eq("YES")
    has_tone = df["monk_skin_tone_label_us"].notna() | df["monk_skin_tone_label_india"].notna()
    sel = df[head & has_tone].copy()
    sel["image"] = sel["image_1_path"]; sel = sel[sel["image"].notna()]
    return df, sel


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("step", choices=["tables", "plan", "images"]); ap.add_argument("--max", type=int, default=0)
    a = ap.parse_args()
    if a.step == "tables":
        for f in ("scin_cases.csv", "scin_labels.csv"):
            print("downloading", f); get(BASE + "dataset/" + f, DEST / f)
        print("saved to", DEST); return
    if not (DEST / "scin_cases.csv").exists(): sys.exit("run 'tables' first")
    df, sel = selection()
    print("cases in SCIN: %d | head/neck cases with a Monk tone label: %d (these have an image path: %d)" % (len(df), len(df[df['body_parts_head_or_neck'].astype(str).str.upper().eq('YES')]), len(sel)))
    if a.max: sel = sel.head(a.max)
    if a.step == "plan":
        sample = sel["image"].sample(min(40, len(sel)), random_state=0).tolist(); sizes = [size_of(BASE + p) for p in sample]; mean = sum(sizes) / max(1, len(sizes))
        print("images to download: %d | average image %.0f KB | about %.2f GB in total" % (len(sel), mean / 1024, mean * len(sel) / 1e9))
        import shutil; print("free space on this drive: %.1f GB" % (shutil.disk_usage(DEST.anchor or "/").free / 1e9)); return
    n = 0
    for i, p in enumerate(sel["image"]):
        out = DEST / p
        if out.exists(): continue
        try: get(BASE + p, out); n += 1
        except urllib.error.URLError as e: print("failed", p, e)
        if (i + 1) % 100 == 0: print("%d / %d" % (i + 1, len(sel)), flush=True)
    print("downloaded %d new images to %s" % (n, DEST / "dataset" / "images"))


if __name__ == "__main__":
    main()
