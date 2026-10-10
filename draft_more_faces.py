#!/usr/bin/env python
"""
draft_more_faces.py - pre-draws boxes on the photos in Acne_Boxing/More_Faces (made by make_more_faces.py) with the trained acne helper,
so you only DELETE wrong boxes, ADD missed spots and press S (X = no acne on a face). Same helper, same cleaning and conf 0.15 as the earlier drafts.
Photos stay "not reviewed" until you save them. The empty ones (nothing found, or too dense) are left for you to box yourself.

    python draft_more_faces.py
"""
import argparse, os, sys, json, shutil
from pathlib import Path
import numpy as np

sys.path.insert(0, "/mnt/c/Users/Bautista/Downloads")
import acne_predraft as P

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing")
D = R / "More_Faces"
S = D / "Elaiza__acne_more"


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--conf", type=float, default=0.15); ap.add_argument("--imgsz", type=int, default=800)
    a = ap.parse_args()
    from ultralytics import YOLO
    import torch
    best = Path.home() / "PrecisionSkinV1/acne_predraft_v2/runs/acne_predraft/weights/best.pt"
    ref = json.load(open(D / "_drafts" / "meta.json"))["drafted_at"]
    reviewed = {p.stem for p in (S / "labels").glob("*.txt") if p.stat().st_mtime > ref + 1}
    print("photos you already saved (not redrawn): %d" % len(reviewed))
    (D / "drafts_original").mkdir(exist_ok=True)
    model = YOLO(str(best)); rows = []; dev = 0 if torch.cuda.is_available() else "cpu"
    imgs = sorted(p for p in (S / "images").iterdir() if p.stem not in reviewed)
    for i, img in enumerate(imgs):
        r = model.predict(str(img), imgsz=a.imgsz, conf=a.conf, iou=0.5, max_det=150, verbose=False, device=dev)[0]
        bx, cf = [], []
        if r.boxes is not None and len(r.boxes):
            for b, c in zip(r.boxes.xywhn.cpu().numpy(), r.boxes.conf.cpu().numpy()):
                if b[2] > 0.003 and b[3] > 0.003:
                    bx.append(tuple(map(float, b))); cf.append(float(c))
        kept, dense = P.clean_draft(bx, cf)
        text = "" if dense else "".join("0 %.6f %.6f %.6f %.6f\n" % b for b in kept)
        for f in (S / "labels" / (img.stem + ".txt"), D / "drafts_original" / (img.stem + ".txt")):
            f.write_text(text); os.utime(f, (ref - 5, ref - 5))                      # stays "not reviewed" until you save it
        rows.append((img.name, 0 if dense else len(kept), "dense" if dense else ""))
        if (i + 1) % 100 == 0:
            print("  drafted %d / %d" % (i + 1, len(imgs)), flush=True)
    (D / "START_BOXING_MORE_FACES.bat").write_text(
        '@echo off\r\ncd /d "%~dp0"\r\necho Drafts are drawn. DELETE wrong boxes, ADD missed spots, press S. X = no acne on a face / not a face. Press N for the next unreviewed photo.\r\n'
        'py -3.13 lesion_box_tool.py "Elaiza__acne_more" --label acne --annotator Elaiza --port 8806 --export "boxed_Acne_more"\r\npause\r\n')
    with_boxes = sum(1 for r in rows if r[1] > 0)
    print("\nDRAFTED %d photos | %d with boxes | %d empty (nothing found or too dense: box those yourself) | mean %.1f boxes per drafted photo" % (
        len(rows), with_boxes, len(rows) - with_boxes, np.mean([r[1] for r in rows if r[1] > 0]) if with_boxes else 0))
    print("Open:", D / "START_BOXING_MORE_FACES.bat")


if __name__ == "__main__":
    main()
