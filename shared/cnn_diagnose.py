#!/usr/bin/env python
"""
cnn_diagnose.py - independent check of what the LUMYVUE app did with ONE image. READ-ONLY: it changes no model, threshold or record.

Step 1 (C#, headless):   SkinDevApp.exe --diagnose <image> <outDir> [repeats]
Step 2 (this script):    python cnn_diagnose.py <outDir> [--model-dir <SkinDevApp/Model>]

What it checks
  A. Model identity   SHA-256 of precisionskin.onnx here == the hash the C# app computed.
  B. Tensor parity    the C# tensor (input_224_nhwc_float32.bin) vs a tensor rebuilt in Python from the same image with the
                      notebook/service preprocessing (BGR->RGB, resize 224 bilinear, (p/127.5)-1).
  C. Runtime parity   Python ONNX Runtime on the IDENTICAL C# tensor vs the four raw probabilities the C# app recorded.
  D. Sensitivity      same image, plausible alternative preprocessing, to show how much the four probabilities move (informational only).
  E. Detectors        every YOLO member re-run in Python: raw candidates, per-class NMS, ensemble fusion; compared with the boxes the app kept;
                      then each pair of kept boxes is labelled Duplicate / Nested / Overlap.
"""
import argparse, hashlib, json, os, sys
import numpy as np
import cv2
import onnxruntime as ort

CLASSES = ["Acne", "Hyperpigmentation", "Eczema", "Normal"]
WORKING_WIDTH = 640                       # ClientDashboardForm.AnalysisWidth


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def resize_to_width(img, w):
    if img.shape[1] <= w:
        return img.copy()
    s = w / img.shape[1]
    return cv2.resize(img, (w, int(round(img.shape[0] * s))), interpolation=cv2.INTER_LINEAR)


def to_tensor(bgr, interp=cv2.INTER_LINEAR):
    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
    rgb = cv2.resize(rgb, (224, 224), interpolation=interp)
    return ((rgb.astype(np.float32) / 127.5) - 1.0)[None]


def run_cls(sess, x):
    return sess.run([sess.get_outputs()[0].name], {sess.get_inputs()[0].name: x})[0][0]


def fmt(p):
    return "  ".join(f"{c} {v*100:7.3f}%" for c, v in zip(CLASSES, p))


# ------------------------------------------------------------------ detectors ---
def iou(a, b):
    iw = min(a[2], b[2]) - max(a[0], b[0]); ih = min(a[3], b[3]) - max(a[1], b[1])
    if iw <= 0 or ih <= 0: return 0.0
    i = iw * ih
    u = (a[2]-a[0])*(a[3]-a[1]) + (b[2]-b[0])*(b[3]-b[1]) - i
    return i / u if u > 0 else 0.0


def contain(a, b):
    iw = min(a[2], b[2]) - max(a[0], b[0]); ih = min(a[3], b[3]) - max(a[1], b[1])
    if iw <= 0 or ih <= 0: return 0.0
    return iw * ih / min((a[2]-a[0])*(a[3]-a[1]), (b[2]-b[0])*(b[3]-b[1]))


def relation(i, c):
    return "Duplicate" if i > 0.5 else "Nested" if c >= 0.8 else "Overlap" if i > 0 else "None"


def nms(boxes, thr, cap=60):
    keep = []
    for b in sorted(boxes, key=lambda x: -x[4]):
        if any(iou(b, k) > thr for k in keep): continue
        keep.append(b)
        if len(keep) >= cap: break
    return keep


def detect_member(path, imgsz, nms_iou, frame_bgr, conf_floor):
    """Mirrors LesionDetector.DetectSingle for a single-class model. Returns (raw_count_above_floor, boxes_before_nms, boxes_after_nms)."""
    H, W = frame_bgr.shape[:2]
    S = imgsz
    scale = min(S / W, S / H)
    nw, nh = max(1, round(W*scale)), max(1, round(H*scale))
    px, py = (S - nw)//2, (S - nh)//2
    canvas = np.full((S, S, 3), 114, np.uint8)
    canvas[py:py+nh, px:px+nw] = cv2.resize(frame_bgr, (nw, nh), interpolation=cv2.INTER_LINEAR)
    x = (cv2.cvtColor(canvas, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0).transpose(2, 0, 1)[None]
    sess = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
    o = sess.run(None, {sess.get_inputs()[0].name: x})[0][0]         # [4+nc, N]
    cand = []
    for i in range(o.shape[1]):
        sc = o[4:, i].max()
        if sc < conf_floor: continue
        cx, cy, bw, bh = o[0, i], o[1, i], o[2, i], o[3, i]
        x0 = (cx - bw/2 - px)/scale; y0 = (cy - bh/2 - py)/scale; x1 = (cx + bw/2 - px)/scale; y1 = (cy + bh/2 - py)/scale
        x0 = max(0, min(W-1, x0)); x1 = max(0, min(W-1, x1)); y0 = max(0, min(H-1, y0)); y1 = max(0, min(H-1, y1))
        if x1 - x0 < 2 or y1 - y0 < 2: continue
        cand.append((x0, y0, x1, y1, float(sc)))
    return cand, nms(cand, nms_iou)


def fuse(member_boxes, thr, n_members):
    """Mirrors LesionDetector.Detect (weighted box fusion over the members' per-class NMS output)."""
    clusters = []
    allb = sorted([b for mb in member_boxes for b in mb], key=lambda b: -b[4])
    def avg(cl):
        w = sum(b[4] for b in cl) or 1.0
        return [sum(b[k]*b[4] for b in cl)/w for k in range(4)]
    for b in allb:
        for cl in clusters:
            if iou(avg(cl), b) >= 0.5: cl.append(b); break
        else: clusters.append([b])
    out = []
    for cl in clusters:
        a = avg(cl); conf = min(1.0, sum(b[4] for b in cl)/n_members)
        if conf >= thr: out.append((*a, conf))
    return sorted(out, key=lambda b: -b[4])


def report_pairs(name, boxes):
    pairs = [(i, j) for i in range(len(boxes)) for j in range(i+1, len(boxes))]
    n = 0
    for i, j in pairs:
        v, c = iou(boxes[i], boxes[j]), contain(boxes[i], boxes[j])
        r = relation(v, c)
        if r != "None":
            n += 1
            print(f"    {name}: box {i} vs {j}: IoU {v:.3f}  contained {c:.3f}  -> {r}")
    if n == 0: print(f"    {name}: no overlapping pair")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("outdir")
    ap.add_argument("--model-dir", default=os.path.join(os.path.dirname(__file__), "..", "SkinDevApp", "Model"))
    a = ap.parse_args()
    d = json.load(open(os.path.join(a.outdir, "cnn_diagnostic.json"), encoding="utf-8"))
    cls = d["classifier"]
    print("IMAGE ", d["image"])
    print("TRACE")
    for s in d["preprocessing_trace"]: print("  -", s)

    # A
    mp = os.path.join(a.model_dir, "precisionskin.onnx")
    h = sha256(mp)
    print("\nA. MODEL IDENTITY")
    print("  python  SHA-256", h)
    print("  C# app  SHA-256", cls["model_sha256"], " ->", "IDENTICAL" if h == cls["model_sha256"] else "*** DIFFERENT ***")

    # B
    cs = np.fromfile(os.path.join(a.outdir, cls["tensor_file"]), np.float32).reshape(1, 224, 224, 3)
    src = cv2.imread(d["image"], cv2.IMREAD_COLOR)
    py = to_tensor(resize_to_width(src, WORKING_WIDTH))
    print("\nB. TENSOR PARITY (C# tensor vs tensor rebuilt in Python from the same file)")
    print("  max |diff| = %.9f   (values span [%.3f, %.3f])   bytes identical: %s" % (np.abs(cs - py).max(), cs.min(), cs.max(), np.array_equal(cs, py)))
    print("  C# tensor SHA-256 recorded:", cls["tensor_sha256"][:16], " recomputed:", hashlib.sha256(cs.tobytes()).hexdigest()[:16])

    # C
    sess = ort.InferenceSession(mp, providers=["CPUExecutionProvider"])
    p_cs = run_cls(sess, cs)
    p_py = run_cls(sess, py)
    p_app = np.array([float(v) for v in cls["raw_probabilities"][0]])
    print("\nC. RUNTIME PARITY (identical C# tensor)")
    print("  C# app       ", fmt(p_app))
    print("  Python ORT   ", fmt(p_cs))
    print("  max |diff| C# vs Python ORT on the same tensor: %.3e" % np.abs(p_cs - p_app).max())
    print("  C# %d repeats bitwise identical: %s" % (cls["repeats"], cls["repeats_bitwise_identical"]))

    # D
    print("\nD. SENSITIVITY (informational; NOT what the app uses)")
    variants = {
        "as deployed (640 px working frame -> 224 bilinear)": py,
        "straight from the original file -> 224 bilinear": to_tensor(src),
        "224 with INTER_AREA": to_tensor(resize_to_width(src, WORKING_WIDTH), cv2.INTER_AREA),
        "mirrored left-right": to_tensor(cv2.flip(resize_to_width(src, WORKING_WIDTH), 1)),
        "centre 80% crop": to_tensor(resize_to_width(src, WORKING_WIDTH)[int(.1*640):int(.9*640), int(.1*640):int(.9*640)]),
    }
    for k, x in variants.items():
        print("  %-52s %s" % (k, fmt(run_cls(sess, x))))
    print("  channel order check: the same image fed as BGR (wrong on purpose):  ",
          fmt(run_cls(sess, py[..., ::-1].copy())))

    # E
    print("\nE. DETECTORS")
    frame = resize_to_width(src, WORKING_WIDTH)
    det = d["detectors"]
    thr = det.get("thresholds_used(Acne,Hyperpigmentation,Eczema)", [0.275, 0.225, 0.25])
    for cname, idx in (("acne", 0), ("hyperpigmentation", 1)):
        meta = json.load(open(os.path.join(a.model_dir, f"lesion_detector_{cname}.json"), encoding="utf-8"))
        files = [f"lesion_detector_{cname}.onnx"] + [f"lesion_detector_{cname}_m{k}.onnx" for k in range(2, 7) if os.path.exists(os.path.join(a.model_dir, f"lesion_detector_{cname}_m{k}.onnx"))]
        print(f"  -- {cname}: imgsz {meta['imgsz']}, model NMS IoU {meta['nms_iou']}, class threshold {thr[idx]}, members {len(files)}")
        members = []
        for f in files:
            p = os.path.join(a.model_dir, f)
            cand, kept = detect_member(p, meta["imgsz"], meta["nms_iou"], frame, 0.02)
            above = [b for b in cand if b[4] >= thr[idx]]
            print(f"    {f}: sha {sha256(p)[:12]}  raw candidates >=0.02: {len(cand)}  >=threshold: {len(above)}  after per-class NMS(0.02 floor): {len(kept)}  after NMS and >=threshold: {len([b for b in kept if b[4] >= thr[idx]])}")
            members.append(kept)
        fused = fuse(members, thr[idx], len(files))
        app = [b for b in det.get("boxes", []) if b["cls"].lower() == cname]
        print(f"    fused here: {len(fused)} box(es); app kept: {len(app)}")
        for b in fused: print("      python   conf %.3f  [%d %d %d %d]" % (b[4], *[round(v) for v in b[:4]]))
        for b in app: print("      C# app   conf %.3f  [%d %d %d %d]" % (b["confidence"], *[round(b[k]) for k in ("x0", "y0", "x1", "y1")]))
        match = len(fused) == len(app) and all(any(iou(f, (b["x0"], b["y0"], b["x1"], b["y1"], 0)) > 0.95 for b in app) for f in fused)
        print("    python and app boxes agree:", match)
        print("    overlap analysis of what the app kept:")
        report_pairs(cname, [(b["x0"], b["y0"], b["x1"], b["y1"]) for b in app])
        for k, mb in enumerate(members):
            report_pairs(f"member {k+1} after its own NMS", [b[:4] for b in mb if b[4] >= thr[idx]])


if __name__ == "__main__":
    sys.exit(main())
