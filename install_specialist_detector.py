#!/usr/bin/env python
"""
install_specialist_detector.py - put a trained single-class detector into the app as a specialist (it replaces the 3-class model's boxes for that class only).

    python install_specialist_detector.py --cls acne --pt <best.pt> --imgsz 800 --conf 0.25 [--test-map50 0.41]

Exports best.pt to ONNX (opset 12, fixed size), writes SkinDevApp/Model/lesion_detector_<cls>.onnx + .json (classes, imgsz, nms_iou, sha256, source,
test metric). The app picks the files up on the next build/start. Remove the two files to go back to the 3-class model for that class.
After installing, set the class confidence threshold in the app Settings to the value chosen on validation (the final report prints it).
Run in WSL with ultralytics, from the SkinDevApp repo folder.
"""
import argparse, datetime, hashlib, json, shutil
from pathlib import Path

MODEL_DIR = Path(__file__).resolve().parent / "SkinDevApp" / "Model"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cls", required=True, choices=["acne", "hyperpigmentation", "eczema"]); ap.add_argument("--pt", required=True)
    ap.add_argument("--imgsz", type=int, default=800); ap.add_argument("--conf", type=float, default=0.25); ap.add_argument("--nms-iou", type=float, default=0.45)
    ap.add_argument("--test-map50", type=float, default=None); ap.add_argument("--note", default="")
    a = ap.parse_args()
    from ultralytics import YOLO
    onnx = Path(YOLO(a.pt).export(format="onnx", imgsz=a.imgsz, opset=12, simplify=True, dynamic=False))
    dst = MODEL_DIR / ("lesion_detector_%s.onnx" % a.cls); MODEL_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copy2(onnx, dst)
    sha = hashlib.sha256(dst.read_bytes()).hexdigest()
    meta = dict(model="PrecisionSkin %s specialist detector" % a.cls, source_weights=str(a.pt), classes=[a.cls], imgsz=a.imgsz, default_conf=a.conf, nms_iou=a.nms_iou,
                onnx_sha256=sha, installed=datetime.datetime.now().isoformat(timespec="seconds"), test_mAP50_single_run=a.test_map50, note=a.note)
    (MODEL_DIR / ("lesion_detector_%s.json" % a.cls)).write_text(json.dumps(meta, indent=2))
    print("installed:", dst, "|", dst.stat().st_size // 1024, "KB | sha256", sha[:12])
    print("Next: rebuild the app, then set the %s confidence threshold in Settings to %.2f." % (a.cls, a.conf))


if __name__ == "__main__":
    main()
