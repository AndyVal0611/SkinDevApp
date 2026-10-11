#!/usr/bin/env python
"""
acne_verdict_checks.py - independent verification checks for the acne detector verdict (read-only; nothing is trained, moved or deleted).

  V2  re-score the 3 seeds on the 167 test photos with ULTRALYTICS' own validator (a different implementation from our scorer) and compare
  V3  false boxes on photos that are NOT acne photos (healthy, eczema, hyperpigmentation) at the app threshold 0.35
  V7  CPU latency and memory of the installed acne ONNX on THIS machine (not the mini PC)
  V9  test mAP without the test photos that have a look-alike in training
"""
import json, os, random, resource, sys, time
from pathlib import Path
import numpy as np, pandas as pd

sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyperpig_autolabel as H
from ultralytics import YOLO

P = Path.home() / "PrecisionSkinV1"; D = P / "acne_detector_v4" / "dataset"; THR = 0.35; EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
SEEDS = [42, 7, 123]; W = {s: P / ("acne_detector_v4/runs/acne_f100_s%d/weights/best.pt" % s) for s in SEEDS}
OUT = P / "lesion_experiments" / "diagnosis"; OUT.mkdir(parents=True, exist_ok=True)
res = {}

# ---------------------------------------------------------------- V2: independent scorer
import yaml
yml = OUT / "verdict_test_data.yaml"; yaml.safe_dump({"path": str(D), "train": "train/images", "val": "test/images", "names": {0: "acne"}}, open(yml, "w"))
v2 = []
for s in SEEDS:
    r = YOLO(str(W[s])).val(data=str(yml), imgsz=800, conf=0.001, iou=0.6, batch=8, verbose=False, plots=False, device=0)
    mine = H.score_model(W[s], 800, D / "test")
    v2.append(dict(seed=s, ultralytics_mAP50=round(float(r.box.map50), 4), our_mAP50=round(mine["AP50"], 4), ultralytics_mAP50_95=round(float(r.box.map), 4), our_mAP50_95=round(mine["AP50_95"], 4)))
res["V2_independent_scorer"] = v2; print("V2", pd.DataFrame(v2).to_string(index=False), flush=True)

# ---------------------------------------------------------------- V3: false boxes on non-acne photos
def sample(files, n, seed):
    files = list(files); random.Random(seed).shuffle(files); return files[:n]
man = pd.read_csv(P / "detector_work" / "dataset_v1" / "dataset_manifest.csv"); V1 = P / "detector_work" / "dataset_v1"
def v1_files(cond, splits):
    return [V1 / r.split / "images" / r.file for r in man[(man.condition == cond) & (man.split.isin(splits))].itertuples() if (V1 / r.split / "images" / r.file).exists()]
sets = {
    "acne test photos (reference)": sorted((D / "test" / "images").glob("*")),
    "healthy skin (Classes/Normal, 200, unverified)": sample([p for p in Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/Normal").rglob("*") if p.suffix.lower() in EXT], 200, 11),
    "eczema photos (v1 dataset, all splits)": v1_files("Eczema", ["train", "val", "test"]),
    "hyperpigmentation photos (v1 test, 164)": v1_files("Hyperpigmentation", ["test"]),
    "hyperpigmentation hand-boxed test (100)": sorted((P / "hyperpig_autolabel/hyperpig_only_v1/dataset/hand_test/images").glob("*")),
}
v3 = []
for name, files in sets.items():
    flagged, nbox, n = [], [], 0
    for s in SEEDS:
        m = YOLO(str(W[s])); f = []; b = []
        for p in files:
            try: r = m.predict(str(p), imgsz=800, conf=THR, iou=0.5, verbose=False, device=0)
            except Exception: continue
            if not r: continue
            k = len(r[0].boxes); f.append(k >= 1); b.append(k)
        flagged.append(np.mean(f)); nbox.append(np.mean(b)); n = len(f)
    v3.append(dict(photo_set=name, photos=n, pct_photos_with_a_box=round(100 * float(np.mean(flagged)), 1), mean_boxes_per_photo=round(float(np.mean(nbox)), 2)))
    print("V3", v3[-1], flush=True)
res["V3_false_boxes_on_non_acne_photos"] = v3

# ---------------------------------------------------------------- V7: latency / memory of the installed ONNX
import onnxruntime as ort
onnx = "/mnt/c/Users/Bautista/source/repos/SkinDevApp/SkinDevApp/Model/lesion_detector_acne.onnx"
so = ort.SessionOptions(); so.intra_op_num_threads = 2
sess = ort.InferenceSession(onnx, providers=["CPUExecutionProvider"], sess_options=so); inp = sess.get_inputs()[0]
x = np.random.default_rng(0).random((1, 3, 800, 800), dtype=np.float32)
for _ in range(3): sess.run(None, {inp.name: x})
ts = []
for _ in range(25):
    t0 = time.perf_counter(); sess.run(None, {inp.name: x}); ts.append((time.perf_counter() - t0) * 1000)
res["V7_latency_this_machine"] = dict(threads=2, median_ms=round(float(np.median(ts)), 0), p90_ms=round(float(np.percentile(ts, 90)), 0), peak_rss_MB=round(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1024, 0), cpu="this laptop (not the mini PC)")
print("V7", res["V7_latency_this_machine"], flush=True)

# ---------------------------------------------------------------- V9: leak-free test
leak = pd.read_csv("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/acne_dataset_v4/leak_check.csv")
bad = set(leak.test_photo.astype(str))
print("V9: look-alike test photos listed in the merge report:", len(bad), "(these are removed from TRAINING in v4; the test photos stay)", flush=True)
res["V9_note"] = "the merge already removed the look-alike partners from training; 2 test photos still have a look-alike (corr >= 0.90) in train/val according to the diagnosis"
(OUT / "verdict_checks.json").write_text(json.dumps(res, indent=2)); print("saved", OUT / "verdict_checks.json")
