#!/usr/bin/env python
"""
benchmark_pc.py - how fast is PrecisionSkin on THIS computer?  (CPU only; nothing is uploaded; no photos are saved)

What it measures
  1. System: CPU, cores, RAM, free disk.
  2. Classifier  (precisionskin.onnx, 224 px)          : preprocessing + inference, median / p95 in ms
  3. Lesion detector (lesion_detector_v1.onnx, 640 px) : preprocessing + inference + box decoding/NMS, median / p95 in ms
  4. One full capture = 3 views (front, left, right) x (classifier + detector), in seconds, plus peak memory
  5. Optional: the Grad-CAM++ service (--gradcam): time for one /gradcam request (all four class maps)

Setup (once, Windows PowerShell). Python 3.11 or 3.12 from python.org ("Add python to PATH"):
    py -3.12 -m pip install onnxruntime numpy opencv-python-headless

Run (put this file and the two .onnx files in one folder, or point --models at the app's Model folder):
    py -3.12 benchmark_pc.py --models "C:\\path\\to\\Model" --image "C:\\path\\to\\a_face_photo.jpg"

Grad-CAM++ too: start the service in another window first (see requirements_gradcam.txt):
    python gradcam_service.py --model "C:\\path\\to\\precisionskin_best.keras"
    py -3.12 benchmark_pc.py --models "C:\\path\\to\\Model" --image face.jpg --gradcam

Results are printed and saved as benchmark_result.txt / benchmark_result.json (send them back).
Close other programs while it runs (browser, antivirus scans) so the numbers are fair.
"""
import argparse
import base64
import json
import os
import platform
import shutil
import statistics as st
import subprocess
import sys
import time
import urllib.request

import numpy as np

try:
    import cv2
except ImportError:
    sys.exit("opencv is missing:  py -m pip install opencv-python-headless")
try:
    import onnxruntime as ort
except ImportError:
    sys.exit("onnxruntime is missing:  py -m pip install onnxruntime")

LINES = []


def out(s=""):
    print(s, flush=True)
    LINES.append(s)


def pct(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(round(p / 100 * (len(v) - 1))))]


def cpu_name():
    try:
        if os.name == "nt":
            r = subprocess.run(["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor).Name"], capture_output=True, text=True, timeout=20)
            if r.stdout.strip():
                return r.stdout.strip().splitlines()[0]
        elif os.path.exists("/proc/cpuinfo"):
            for ln in open("/proc/cpuinfo"):
                if ln.startswith("model name"):
                    return ln.split(":", 1)[1].strip()
    except Exception:
        pass
    return platform.processor() or "unknown"


def ram_gb():
    try:
        if os.name == "nt":
            import ctypes

            class MS(ctypes.Structure):
                _fields_ = [("l", ctypes.c_ulong), ("load", ctypes.c_ulong), ("tot", ctypes.c_ulonglong), ("avail", ctypes.c_ulonglong),
                            ("a", ctypes.c_ulonglong), ("b", ctypes.c_ulonglong), ("c", ctypes.c_ulonglong), ("d", ctypes.c_ulonglong), ("e", ctypes.c_ulonglong)]
            m = MS(); m.l = ctypes.sizeof(MS); ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
            return m.tot / 2**30, m.avail / 2**30
        info = {l.split(":")[0]: int(l.split()[1]) for l in open("/proc/meminfo") if l.split()[1].isdigit()}
        return info["MemTotal"] / 2**20, info["MemAvailable"] / 2**20
    except Exception:
        return float("nan"), float("nan")


def peak_mem_mb():
    try:
        if os.name == "nt":
            import ctypes
            from ctypes import wintypes

            class PMC(ctypes.Structure):
                _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD), ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                            ("a", ctypes.c_size_t), ("b", ctypes.c_size_t), ("c", ctypes.c_size_t), ("d", ctypes.c_size_t), ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]
            pm = PMC(); pm.cb = ctypes.sizeof(PMC)
            ctypes.windll.psapi.GetProcessMemoryInfo(ctypes.windll.kernel32.GetCurrentProcess(), ctypes.byref(pm), pm.cb)
            return pm.PeakWorkingSetSize / 2**20
        import resource
        return resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1024
    except Exception:
        return float("nan")


def find_model(folder, name):
    for root in (folder, os.path.join(folder, "Model"), os.getcwd()):
        p = os.path.join(root, name)
        if os.path.exists(p):
            return p
    sys.exit("Cannot find %s in %s (use --models <folder>)" % (name, folder))


def load_image(path):
    if path and os.path.exists(path):
        im = cv2.imdecode(np.fromfile(path, np.uint8), cv2.IMREAD_COLOR)
        if im is not None:
            return im, os.path.basename(path)
    rng = np.random.default_rng(0)                                         # synthetic skin-like frame (timing is nearly the same)
    base = np.full((720, 1280, 3), (150, 170, 205), np.uint8)
    noise = rng.integers(0, 25, base.shape, dtype=np.uint8)
    return cv2.add(base, noise), "synthetic 1280x720 frame (give a real photo with --image for the most faithful detector timing)"


def session(path, threads):
    so = ort.SessionOptions()
    so.intra_op_num_threads = threads                                       # 0 = ONNX Runtime default
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return ort.InferenceSession(path, so, providers=["CPUExecutionProvider"])


def prep_classifier(img, shape):
    nchw = len(shape) == 4 and shape[1] == 3
    s = 224
    x = cv2.resize(cv2.cvtColor(img, cv2.COLOR_BGR2RGB), (s, s), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 127.5 - 1.0
    x = np.transpose(x, (2, 0, 1)) if nchw else x
    return x[None]


def prep_detector(img, size=640):
    h, w = img.shape[:2]
    r = min(size / h, size / w)
    nh, nw = int(round(h * r)), int(round(w * r))
    canvas = np.full((size, size, 3), 114, np.uint8)
    canvas[(size - nh) // 2:(size - nh) // 2 + nh, (size - nw) // 2:(size - nw) // 2 + nw] = cv2.resize(img, (nw, nh), interpolation=cv2.INTER_LINEAR)
    x = cv2.cvtColor(canvas, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    return np.transpose(x, (2, 0, 1))[None]


def decode_detector(o, conf=0.20, iou=0.45):
    p = o[0]                                                                # [4+C, N]
    if p.shape[0] > p.shape[1]:
        p = p.T
    boxes, scores = p[:4].T, p[4:].max(0)
    keep = scores >= conf
    b, s = boxes[keep], scores[keep]
    if len(b) == 0:
        return 0
    xywh = np.stack([b[:, 0] - b[:, 2] / 2, b[:, 1] - b[:, 3] / 2, b[:, 2], b[:, 3]], 1).tolist()
    idx = cv2.dnn.NMSBoxes(xywh, s.tolist(), conf, iou)
    return len(idx)


def timeit(fn, runs, warmup):
    for _ in range(warmup):
        fn()
    t = []
    for _ in range(runs):
        t0 = time.perf_counter(); fn(); t.append((time.perf_counter() - t0) * 1000)
    return t


def row(name, t):
    return {"step": name, "median_ms": round(st.median(t), 1), "p95_ms": round(pct(t, 95), 1), "min_ms": round(min(t), 1), "max_ms": round(max(t), 1)}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--models", default=".", help="folder with precisionskin.onnx and lesion_detector_v1.onnx (or its Model subfolder)")
    ap.add_argument("--image", default=None, help="a face photo (jpg/png) used for all timings")
    ap.add_argument("--runs", type=int, default=30)
    ap.add_argument("--warmup", type=int, default=5)
    ap.add_argument("--gradcam", action="store_true", help="also time the Grad-CAM++ service (must be running on 127.0.0.1:8765)")
    ap.add_argument("--gradcam-url", default="http://127.0.0.1:8765")
    a = ap.parse_args()

    tot, avail = ram_gb()
    out("=" * 78); out("PrecisionSkin benchmark  -  " + time.strftime("%Y-%m-%d %H:%M:%S")); out("=" * 78)
    out("CPU      : %s" % cpu_name())
    out("Threads  : %s logical cores (os.cpu_count)" % os.cpu_count())
    out("RAM      : %.1f GB total, %.1f GB available now" % (tot, avail))
    out("Disk free: %.0f GB (%s)" % (shutil.disk_usage(os.path.abspath(a.models)).free / 2**30, os.path.abspath(a.models)))
    out("OS       : %s | Python %s | onnxruntime %s | providers: %s" % (platform.platform(), platform.python_version(), ort.__version__, ort.get_available_providers()))
    cls_path, det_path = find_model(a.models, "precisionskin.onnx"), find_model(a.models, "lesion_detector_v1.onnx")
    img, desc = load_image(a.image)
    out("Image    : %s" % desc)
    out("Runs     : %d timed runs after %d warm-up runs per step" % (a.runs, a.warmup))

    results = {"cpu": cpu_name(), "logical_cores": os.cpu_count(), "ram_total_gb": round(tot, 1), "ort": ort.__version__, "steps": [], "by_threads": {}}
    out("\n--- Classifier and detector, by number of ONNX Runtime threads (0 = default) ---")
    out("%-9s %-34s %10s %10s" % ("threads", "step", "median ms", "p95 ms"))
    best_threads, best_total = None, 1e9
    for th in sorted(set([1, 2, 4, 0])):
        sc, sd = session(cls_path, th), session(det_path, th)
        ci, di = sc.get_inputs()[0], sd.get_inputs()[0]
        tc = timeit(lambda: sc.run(None, {ci.name: prep_classifier(img, ci.shape)}), a.runs, a.warmup)
        td = timeit(lambda: decode_detector(sd.run(None, {di.name: prep_detector(img)})[0]), a.runs, a.warmup)
        rc, rd = row("classifier (prep + inference)", tc), row("detector (prep + inference + NMS)", td)
        for r in (rc, rd):
            out("%-9s %-34s %10.1f %10.1f" % (th, r["step"], r["median_ms"], r["p95_ms"]))
        results["by_threads"][str(th)] = [rc, rd]
        tot_ms = rc["median_ms"] + rd["median_ms"]
        if tot_ms < best_total:
            best_total, best_threads = tot_ms, th

    out("\n--- One full capture (3 views, classifier + detector each), threads = %s ---" % best_threads)
    sc, sd = session(cls_path, best_threads), session(det_path, best_threads)
    ci, di = sc.get_inputs()[0], sd.get_inputs()[0]

    def capture():
        for _ in range(3):
            sc.run(None, {ci.name: prep_classifier(img, ci.shape)})
            decode_detector(sd.run(None, {di.name: prep_detector(img)})[0])
    tcap = timeit(capture, max(5, a.runs // 3), 2)
    out("3 views : median %.2f s | p95 %.2f s" % (st.median(tcap) / 1000, pct(tcap, 95) / 1000))
    out("Peak memory of this benchmark process: %.0f MB" % peak_mem_mb())
    results["capture_3views_s"] = {"median": round(st.median(tcap) / 1000, 3), "p95": round(pct(tcap, 95) / 1000, 3), "threads": best_threads}
    results["peak_memory_mb"] = round(peak_mem_mb())

    if a.gradcam:
        out("\n--- Grad-CAM++ service (%s) ---" % a.gradcam_url)
        try:
            with urllib.request.urlopen(a.gradcam_url + "/health", timeout=5) as r:
                health = json.loads(r.read().decode())
            out("health: %s" % json.dumps(health)[:240])
            payload = json.dumps({"image_base64": base64.b64encode(cv2.imencode(".jpg", cv2.resize(img, (640, 480)))[1].tobytes()).decode(), "all_classes": True}).encode()

            def call():
                req = urllib.request.Request(a.gradcam_url + "/gradcam", data=payload, headers={"Content-Type": "application/json"})
                with urllib.request.urlopen(req, timeout=60) as r:
                    return json.loads(r.read().decode())
            resp = call()
            if not resp.get("ok"):
                out("service answered but not ok: %s" % str(resp)[:200])
            tg = timeit(call, max(5, a.runs // 3), 2)
            out("/gradcam all-class request: median %.0f ms | p95 %.0f ms" % (st.median(tg), pct(tg, 95)))
            results["gradcam_all_classes"] = row("gradcam all-class", tg)
        except Exception as exc:
            out("Grad-CAM++ service not reachable (%s). Start gradcam_service.py first." % exc)

    out("\n--- Reading the result (rough guide) ---")
    cap = results["capture_3views_s"]["median"]
    out("Capture (3 views, classifier + detector): %.2f s -> %s" % (cap, "comfortable" if cap < 1.5 else "acceptable" if cap < 4 else "SLOW: report to the team"))
    out("Single detector call (prep + inference + NMS): %.0f ms. Reference: the dev laptop measured about 66 ms with this same script (inference alone is about 32 ms)." % results["by_threads"][str(best_threads)][1]["median_ms"])
    out("Free RAM after the run should stay above ~4 GB while the Grad-CAM++ service and the app are both open.")
    open("benchmark_result.txt", "w", encoding="utf-8").write("\n".join(LINES) + "\n")
    json.dump(results, open("benchmark_result.json", "w"), indent=2)
    out("\nSaved: benchmark_result.txt and benchmark_result.json (in %s)" % os.getcwd())


if __name__ == "__main__":
    main()
