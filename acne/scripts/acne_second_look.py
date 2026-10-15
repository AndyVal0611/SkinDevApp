#!/usr/bin/env python
"""
acne_second_look.py - a "second look" at your acne TRAINING labels: photos where the trained detector confidently sees spots you did not box.
It writes a COPY (never your originals): Fix_Acne_Missed_Spots/ with your boxes + the suggested extra boxes, to open in the box tool;
you delete the wrong extras and keep the real ones, then press S. Test and validation photos are never included (their labels must not be tuned to the model).

    python acne_second_look.py [--limit 60] [--conf 0.45]
"""
import argparse, json, os, shutil, time
from pathlib import Path
import numpy as np
from ultralytics import YOLO

P = Path.home() / "PrecisionSkinV1"
TRAIN = P / "acne_detector_v4" / "dataset" / "train"
MODEL = P / "acne_detector_v4" / "runs" / "acne_f100_s123" / "weights" / "best.pt"
OUT = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/Fix_Acne_Missed_Spots")
TOOL = Path("/mnt/c/Users/Bautista/source/repos/SkinDevApp/shared/lesion_box_tool.py")


def read(p):
    return [tuple(map(float, l.split()[1:5])) for l in Path(p).read_text().splitlines() if len(l.split()) >= 5] if Path(p).exists() else []


def rel(a, b):
    def xy(b): return b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2
    a, b = xy(a), xy(b); iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    aa, bb = (a[2] - a[0]) * (a[3] - a[1]), (b[2] - b[0]) * (b[3] - b[1])
    return i / (aa + bb - i + 1e-9), i / (min(aa, bb) + 1e-9)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--limit", type=int, default=60); ap.add_argument("--conf", type=float, default=0.45); a = ap.parse_args()
    if OUT.exists(): raise SystemExit("exists already, not overwriting: %s" % OUT)
    m = YOLO(str(MODEL)); cand = []
    for img in sorted((TRAIN / "images").glob("*")):
        mine = read(TRAIN / "labels" / (img.stem + ".txt"))
        r = m.predict(str(img), imgsz=800, conf=a.conf, iou=0.5, verbose=False, device=0)[0]
        pr = [(tuple(map(float, b)), float(c)) for b, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy())] if len(r.boxes) else []
        extra = [(b, c) for b, c in pr if not any(rel(b, g)[0] > 0.2 or rel(b, g)[1] > 0.5 for g in mine)]
        if len(extra) >= 3: cand.append((sum(c for _, c in extra), img, mine, [b for b, _ in extra]))
    cand.sort(key=lambda t: -t[0]); cand = cand[:a.limit]
    S = OUT / "Elaiza__acne_fix"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (OUT / "_drafts").mkdir()
    for _, img, mine, extra in cand:
        shutil.copy2(img, S / "images" / img.name)
        (S / "labels" / (img.stem + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in mine + extra))
    (S / "classes.txt").write_text("acne\n"); time.sleep(1.5); ref = time.time()
    for _, img, _, _ in cand: os.utime(S / "labels" / (img.stem + ".txt"), (ref - 5, ref - 5))       # "not reviewed yet" until you save it
    json.dump({"drafted_at": ref, "note": "your boxes + suggested extra boxes; a photo counts as fixed once you save it"}, open(OUT / "_drafts" / "meta.json", "w"))
    shutil.copy2(TOOL, OUT / "lesion_box_tool.py")
    (OUT / "START_SECOND_LOOK.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho Your boxes + extra boxes the model is sure about. Delete the extras that are NOT acne spots, keep real ones, press S.\r\n'
                                               'py -3.13 lesion_box_tool.py "Elaiza__acne_fix" --label acne --annotator Elaiza --port 8815 --export "boxed_Acne_SECOND_LOOK"\r\npause\r\n')
    print("second look: %d training photos, %d suggested extra boxes (mean %.1f per photo)" % (len(cand), sum(len(c[3]) for c in cand), np.mean([len(c[3]) for c in cand]) if cand else 0))
    print("open:", OUT / "START_SECOND_LOOK.bat")


if __name__ == "__main__":
    main()
