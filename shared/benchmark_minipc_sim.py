#!/usr/bin/env python
"""
benchmark_minipc_sim.py - time the REAL installed models (classifier + 3 acne + 3 hyperpigmentation ONNX files) on this PC while limiting it
to look like the Acer TC-1650 mini PC (Core i5 11th gen, CPU only). Read-only: nothing is trained, installed or changed.

What it can and cannot do
  * It pins this process to the first --cpus logical processors (default 12 = 6 cores with hyper-threading, like an i5-11400) and uses 2 ONNX Runtime
    threads per model, as the app does (LesionDetector / OnnxInference intraOpThreads = 2). No GPU is used.
  * It CANNOT copy the mini PC's per-core speed. A newer laptop CPU is faster per core, so these numbers are a LOWER BOUND for the mini PC.
    The real answer is still benchmark_pc.py / this script run on the mini PC itself.
  * It times preprocessing + ONNX inference + box decoding for each model. It does not time the C# weighted box fusion (tiny next to inference).

usage:  python benchmark_minipc_sim.py --models "<...>\\SkinDevApp\\Model" --image face.jpg [--cpus 12] [--runs 20] [--out result]
needs:  pip install onnxruntime numpy opencv-python-headless psutil
"""
import argparse, ctypes, json, os, platform, statistics as st, sys, time
import numpy as np
import cv2
import onnxruntime as ort
import psutil


def pin_cpus(n):
    """Windows: restrict this process to the first n logical processors."""
    total = os.cpu_count() or 1
    n = max(1, min(n, total))
    try:
        psutil.Process().cpu_affinity(list(range(n)))
        return n, sorted(psutil.Process().cpu_affinity()) == list(range(n))
    except Exception:
        return n, False


def session(path, threads=2):
    so = ort.SessionOptions()
    so.intra_op_num_threads = threads
    so.inter_op_num_threads = 1
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return ort.InferenceSession(path, so, providers=["CPUExecutionProvider"])


def prep_cls(img):
    x = cv2.resize(cv2.cvtColor(img, cv2.COLOR_BGR2RGB), (224, 224), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 127.5 - 1.0
    return x[None]


def prep_det(img, size=800):
    h, w = img.shape[:2]
    r = min(size / h, size / w)
    nh, nw = int(round(h * r)), int(round(w * r))
    canvas = np.full((size, size, 3), 114, np.uint8)
    canvas[(size - nh) // 2:(size - nh) // 2 + nh, (size - nw) // 2:(size - nw) // 2 + nw] = cv2.resize(img, (nw, nh), interpolation=cv2.INTER_LINEAR)
    return np.transpose(cv2.cvtColor(canvas, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0, (2, 0, 1))[None]


def decode(o, conf, iou=0.6):
    p = o[0]
    if p.shape[0] > p.shape[1]:
        p = p.T
    boxes, scores = p[:4].T, p[4:].max(0)
    keep = scores >= conf
    b, s = boxes[keep], scores[keep]
    if len(b) == 0:
        return 0
    xywh = np.stack([b[:, 0] - b[:, 2] / 2, b[:, 1] - b[:, 3] / 2, b[:, 2], b[:, 3]], 1).tolist()
    return len(cv2.dnn.NMSBoxes(xywh, s.tolist(), conf, iou))


def pct(v, p):
    v = sorted(v); return v[min(len(v) - 1, int(round(p / 100 * (len(v) - 1))))]


def timeit(fn, runs, warm=3):
    for _ in range(warm): fn()
    t = []
    for _ in range(runs):
        t0 = time.perf_counter(); fn(); t.append((time.perf_counter() - t0) * 1000)
    return t


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--models", required=True)
    ap.add_argument("--image", default=None)
    ap.add_argument("--cpus", type=int, default=12, help="logical processors to allow (12 = 6 cores + HT)")
    ap.add_argument("--threads", type=int, default=2, help="ONNX Runtime threads per model (the app uses 2)")
    ap.add_argument("--runs", type=int, default=20)
    ap.add_argument("--out", default="minipc_sim_result")
    a = ap.parse_args()

    lines = []
    def out(s=""):
        print(s, flush=True); lines.append(s)

    n, ok = pin_cpus(a.cpus)
    proc = psutil.Process()
    cpu = platform.processor()
    try:
        import subprocess
        cpu = subprocess.run(["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor).Name"], capture_output=True, text=True).stdout.strip() or cpu
    except Exception:
        pass
    out("THIS PC     : %s | %d logical CPUs | RAM %.1f GB" % (cpu, os.cpu_count(), psutil.virtual_memory().total / 2**30))
    out("SIMULATION  : process limited to %d logical CPUs (affinity set: %s), %d ONNX threads per model, CPU provider only, onnxruntime %s" % (n, ok, a.threads, ort.__version__))
    out("CAVEAT      : the real mini PC has slower cores than this laptop, so treat these numbers as a LOWER BOUND.")

    img = None
    if a.image and os.path.exists(a.image):
        img = cv2.imdecode(np.fromfile(a.image, np.uint8), cv2.IMREAD_COLOR)
    if img is None:
        rng = np.random.default_rng(0)
        img = cv2.add(np.full((720, 1280, 3), (150, 170, 205), np.uint8), rng.integers(0, 25, (720, 1280, 3), dtype=np.uint8))
        out("IMAGE       : synthetic 1280x720 frame (use --image with a real face photo for the most faithful timing)")
    else:
        out("IMAGE       : %s (%dx%d)" % (a.image, img.shape[1], img.shape[0]))

    M = a.models
    files = {
        "classifier": ["precisionskin.onnx"],
        "acne": ["lesion_detector_acne.onnx", "lesion_detector_acne_m2.onnx", "lesion_detector_acne_m3.onnx"],
        "hyperpigmentation": ["lesion_detector_hyperpigmentation.onnx", "lesion_detector_hyperpigmentation_m2.onnx", "lesion_detector_hyperpigmentation_m3.onnx"],
    }
    thr = {"acne": 0.275, "hyperpigmentation": 0.225}
    res = {"cpu": cpu, "logical_cpus_allowed": n, "threads_per_model": a.threads, "load_s": {}, "single_ms": {}}

    rss0 = proc.memory_info().rss / 2**20
    sess = {}
    out("\n--- Model load time (one-time, at app start) ---")
    for grp, fl in files.items():
        for f in fl:
            p = os.path.join(M, f)
            if not os.path.exists(p):
                out("  missing: %s" % p); continue
            t0 = time.perf_counter(); sess[f] = session(p, a.threads); dt = time.perf_counter() - t0
            res["load_s"][f] = round(dt, 2); out("  %-48s %5.2f s" % (f, dt))
    rss1 = proc.memory_info().rss / 2**20
    out("  memory after loading all models: +%.0f MB (process total %.0f MB)" % (rss1 - rss0, rss1))

    x_cls, x_det = prep_cls(img), prep_det(img)
    def run_cls(f):
        s = sess[f]; s.run(None, {s.get_inputs()[0].name: prep_cls(img)})
    def run_det(f, grp):
        s = sess[f]; o = s.run(None, {s.get_inputs()[0].name: prep_det(img)})[0]; decode(o, thr[grp])

    out("\n--- One model, one photo: preprocess + inference + decode, median / p95 (ms), %d runs ---" % a.runs)
    for grp, fl in files.items():
        for f in fl:
            if f not in sess: continue
            t = timeit((lambda f=f: run_cls(f)) if grp == "classifier" else (lambda f=f, g=grp: run_det(f, g)), a.runs)
            res["single_ms"][f] = [round(st.median(t), 1), round(pct(t, 95), 1)]
            out("  %-48s %7.1f / %7.1f" % (f, st.median(t), pct(t, 95)))

    cf = files["classifier"][0]
    def seeds(grp, k): return [f for f in files[grp][:k] if f in sess]
    out("\n--- One photo, models run one after another (as the app does) ---")
    table = []
    for label, ak, hk in [("classifier only", 0, 0), ("classifier + 1 acne + 1 hyperpigmentation seed", 1, 1),
                          ("classifier + 2 + 2 seeds", 2, 2), ("classifier + 3 + 3 seeds (INSTALLED)", 3, 3)]:
        def photo(ak=ak, hk=hk):
            run_cls(cf)
            for f in seeds("acne", ak): run_det(f, "acne")
            for f in seeds("hyperpigmentation", hk): run_det(f, "hyperpigmentation")
        t = timeit(photo, max(5, a.runs // 2), warm=2)
        table.append((label, st.median(t) / 1000, pct(t, 95) / 1000))
        out("  %-52s median %5.2f s   p95 %5.2f s" % (label, table[-1][1], table[-1][2]))
    res["per_photo_s"] = {l: [round(m, 2), round(p, 2)] for l, m, p in table}

    inst = table[-1][1]
    out("\n--- One full scan = 3 views (front, left, right), installed 3 + 3 seeds ---")
    out("  estimated: %.1f s (3 x %.2f s). Grad-CAM++ and the C# box fusion are NOT included." % (3 * inst, inst))
    res["scan_3_views_s"] = round(3 * inst, 1)
    rss2 = proc.memory_info().rss / 2**20
    out("  process memory at the end: %.0f MB" % rss2)

    # also: all six detectors started together in threads (if the app ever ran them in parallel)
    from concurrent.futures import ThreadPoolExecutor
    def parallel():
        with ThreadPoolExecutor(6) as ex:
            list(ex.map(lambda q: run_det(q[0], q[1]), [(f, "acne") for f in seeds("acne", 3)] + [(f, "hyperpigmentation") for f in seeds("hyperpigmentation", 3)]))
    t = timeit(parallel, max(5, a.runs // 2), warm=2)
    out("\n--- If the 6 detectors ran in parallel on this CPU limit: median %.2f s per photo (detectors only) ---" % (st.median(t) / 1000))
    res["six_detectors_parallel_s"] = round(st.median(t) / 1000, 2)

    open(a.out + ".txt", "w", encoding="utf-8").write("\n".join(lines))
    json.dump(res, open(a.out + ".json", "w"), indent=1)
    out("\nsaved %s.txt and %s.json" % (a.out, a.out))


if __name__ == "__main__":
    main()
