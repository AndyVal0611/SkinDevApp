"""Python REFERENCE for the C# real-photo parity tests (Phase 5).

For each chosen original photograph it runs the Python reference pipeline exactly as the notebook defines it (detect_condition / detector_evidence /
assemble for global_yolo / apply_platt / decide) and stores every intermediate result in v6/deployment/v6_real_photo_golden.json:
  V5 ONNX probabilities, Global embedding + logits, the three detector candidate pools, det_ev (21), fusion logits, calibrated probabilities, flags, state.

It ALSO checks that this replica reproduces the detections the model was TRAINED on (v6/astra_runs/open_run02/yolo/detections_*.jsonl) for the same
photographs, so the golden file really is the training-time reference and not a second implementation that merely agrees with the C# code.

Photos stay where they are (Downloads/PrecisionSkin/Classes/<class>/<file>); only their relative path and SHA-256 are stored. Locked test split is not used.
Run with the project venv:  python make_parity_golden.py
"""
import csv, hashlib, json, os, sys, time
os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
import cv2, numpy as np, onnxruntime as ort

ROOT = "/mnt/c/Users/Bautista/source/repos/SkinDevApp"
RUN = f"{ROOT}/v6/astra_runs/open_run02"; MODEL = f"{ROOT}/SkinDevApp/Model"; DEP = f"{ROOT}/v6/deployment"
CLASSES = "/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes"
SCR = "/mnt/c/Users/Bautista/AppData/Local/Temp/claude/C--Users-Bautista-source-repos-SkinDevApp/ebe51b4e-f083-464c-bf75-6d1ca0cac5fe/scratchpad"
MANIFEST = os.path.expanduser("~/PrecisionSkinV1/metadata/metadata/split_manifest.csv")
CONDS = ["Acne", "Hyperpigmentation", "Eczema"]
cfg = json.load(open(f"{DEP}/v6_deployment_config.json"))
THR_DEP = [cfg["detectors"]["per_condition"][c]["deployed_threshold"] for c in CONDS]
FLOOR, FUSION_IOU, POOL_MIN, MAX_BOXES = cfg["detectors"]["member_floor"], 0.5, cfg["detectors"]["candidate_pool_min_conf"], cfg["detectors"]["max_boxes_per_condition"]
sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
opts = ort.SessionOptions(); opts.intra_op_num_threads = 4
S = lambda p: ort.InferenceSession(p, opts, providers=["CPUExecutionProvider"])

# ------------------------------------------------------------ detector replica (copied from 06_yolo_three_detectors.cellsrc, section 24.1)
class Member:
    def __init__(self, path, imgsz):
        self.path, self.imgsz = path, imgsz; self.session = S(path); self.input_name = self.session.get_inputs()[0].name
class Spec:
    pass
SPEC = {}
for c in CONDS:
    meta = json.load(open(f"{MODEL}/lesion_detector_{c.lower()}.json")); sp = Spec(); sp.condition, sp.imgsz, sp.nms_iou, sp.threshold = c, int(meta["imgsz"]), float(meta["nms_iou"]), THR_DEP[CONDS.index(c)]
    files = [f"{MODEL}/lesion_detector_{c.lower()}.onnx"] + [f"{MODEL}/lesion_detector_{c.lower()}_m{k}.onnx" for k in (2, 3) if os.path.exists(f"{MODEL}/lesion_detector_{c.lower()}_m{k}.onnx")]
    sp.members = [Member(p, sp.imgsz) for p in files]; sp.fusion_iou = FUSION_IOU; sp.floor = FLOOR; SPEC[c] = sp

def iou_xyxy(a, b):
    iw = min(a[2], b[2]) - max(a[0], b[0]); ih = min(a[3], b[3]) - max(a[1], b[1])
    if iw <= 0 or ih <= 0: return 0.0
    i = iw * ih; u = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i
    return i / u if u > 0 else 0.0
def nms_boxes(boxes, thr, cap):
    keep = []
    for b in sorted(boxes, key=lambda x: -x[4]):
        if any(iou_xyxy(b, k) > thr for k in keep): continue
        keep.append(b)
        if len(keep) >= cap: break
    return keep
def run_member(member, bgr, nms_iou, floor):
    H, W = bgr.shape[:2]; Sz = member.imgsz
    scale = min(Sz / W, Sz / H); nw, nh = max(1, round(W * scale)), max(1, round(H * scale)); px, py = (Sz - nw) // 2, (Sz - nh) // 2
    canvas = np.full((Sz, Sz, 3), 114, np.uint8); canvas[py:py + nh, px:px + nw] = cv2.resize(bgr, (nw, nh), interpolation=cv2.INTER_LINEAR)
    x = (cv2.cvtColor(canvas, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0).transpose(2, 0, 1)[None]
    o = member.session.run(None, {member.input_name: x})[0][0]                                   # [1+4, N] rows first
    best = o[4:].argmax(0); sc = o[4:].max(0); sel = np.where((best == 0) & (sc >= floor))[0]; out = []
    for i in sel:
        cx, cy, bw, bh = o[0, i], o[1, i], o[2, i], o[3, i]
        x0 = (cx - bw / 2 - px) / scale; y0 = (cy - bh / 2 - py) / scale; x1 = (cx + bw / 2 - px) / scale; y1 = (cy + bh / 2 - py) / scale
        x0, x1 = max(0, min(W - 1, x0)), max(0, min(W - 1, x1)); y0, y1 = max(0, min(H - 1, y0)), max(0, min(H - 1, y1))
        if x1 - x0 < 2 or y1 - y0 < 2: continue
        out.append((float(x0), float(y0), float(x1), float(y1), float(sc[i])))
    return nms_boxes(out, nms_iou, MAX_BOXES)
def detect_condition(spec, bgr):
    H, W = bgr.shape[:2]; per_member = [[b + (mi,) for b in run_member(m, bgr, spec.nms_iou, spec.floor)] for mi, m in enumerate(spec.members)]
    clusters = []
    def avg(cl):
        w = sum(b[4] for b in cl) or 1.0
        return [sum(b[k] * b[4] for b in cl) / w for k in range(4)]
    for b in sorted([b for pm in per_member for b in pm], key=lambda x: -x[4]):
        hit = None
        for cl in clusters:
            if iou_xyxy(avg(cl), b) >= spec.fusion_iou: hit = cl; break
        if hit is None: clusters.append([b])
        else: hit.append(b)
    fused = [dict(box=tuple(avg(cl)), conf=min(1.0, sum(x[4] for x in cl) / len(spec.members))) for cl in clusters]
    fused = sorted([f for f in fused if f["conf"] >= POOL_MIN], key=lambda f: -f["conf"])[:MAX_BOXES]
    recs = []
    for f in fused:
        x0, y0, x1, y1 = f["box"]
        recs.append(dict(conf=round(float(f["conf"]), 5), x0=x0, y0=y0, x1=x1, y1=y1, rel_area=round(((x1 - x0) * (y1 - y0)) / (W * H), 6), cx=round((x0 + x1) / 2 / W, 4), cy=round((y0 + y1) / 2 / H, 4), deployed=bool(f["conf"] >= spec.threshold)))
    return recs

def det_ev(pools):
    out = []
    for c in CONDS:
        b = pools[c]
        if b is None: out += [0.0] * 7; continue
        dep = [x for x in b if x["deployed"]]; best = max(dep, key=lambda x: x["conf"]) if dep else None
        out += [1.0, float(bool(dep)), best["conf"] if best else 0.0, max([x["conf"] for x in b], default=0.0), float(np.sqrt(best["rel_area"])) if best else 0.0, best["cx"] if best else 0.0, best["cy"] if best else 0.0]
    return np.array(out, np.float32)

# ------------------------------------------------------------ models and calibration
g_sess = S(f"{ROOT}/v6/astra_runs/open_run02/export/v6_global_features_mobilenetv2.onnx")
f_yolo = S(f"{RUN}/export/v6_fusion_global_yolo_seed42.onnx"); f_only = S(f"{RUN}/export/v6_fusion_global_only_seed42.onnx"); v5 = S(f"{MODEL}/precisionskin.onnx")
cal_y, cal_g = cfg["calibration"]["global_yolo"], cfg["calibration"]["global_only"]; THR_Y, THR_G = np.array(cal_y["thresholds"]), np.array(cal_g["thresholds"]); BAND = cfg["decision"]["band"]
platt = lambda l, c: 1 / (1 + np.exp(-(np.array([x["a"] for x in c["platt"]]) * l + np.array([x["b"] for x in c["platt"]]))))
def pre(bgr): return ((cv2.resize(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB), (224, 224), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 127.5) - 1.0)[None]
def decide(p, avail, flags):
    st, absent_ok, unc, pred = [], [], [], []
    for j in range(3):
        d = "present" if p[j] >= THR_Y[j] + BAND else ("absent" if p[j] < THR_Y[j] - BAND else "uncertain"); sup = d == "absent" and bool(avail[j]); st.append((d, sup))
        if d == "present": pred.append(CONDS[j])
        elif d == "uncertain" or not sup: unc.append(CONDS[j])
    state = "UNCERTAIN_REVIEW_REQUIRED" if (unc or flags) else ("MULTIPLE_FINDINGS_SUPPORTED" if len(pred) >= 2 else "FINDINGS_SUPPORTED" if len(pred) == 1 else "NO_FINDINGS_SUPPORTED")
    return state, [s[0] for s in st], [s[1] for s in st]

# ------------------------------------------------------------ choose photographs (validation split, verified labels, original file present, in the training cache)
man = {r["image_id"]: r for r in csv.DictReader(open(MANIFEST, encoding="utf-8"))}
ml = {r["image_id"]: r for r in csv.DictReader(open(f"{SCR}/ml.csv", encoding="utf-8")) if r["review_status"] == "verified" and r["split"] == "validation"}
cache = {}
for line in open(f"{RUN}/yolo/detections_ce6ae3186e771bfe.jsonl", encoding="utf-8"):
    r = json.loads(line)
    if r["image_id"] in ml and r.get("source") == "original": cache[r["image_id"]] = r
rng = np.random.default_rng(11)
def pool(pred):
    ids = [i for i in sorted(cache) if i in man and pred(ml[i]) and os.path.exists(f"{CLASSES}/{man[i]['label_class']}/{man[i]['filename']}")]
    rng.shuffle(ids); return ids
absent = lambda r, c: r[c] == "absent"
groups = [("hyperpigmentation", pool(lambda r: r["hyperpigmentation"] == "present"), 5), ("acne", pool(lambda r: r["acne"] == "present" and r["hyperpigmentation"] != "present"), 3),
          ("eczema", pool(lambda r: r["eczema"] == "present"), 2), ("clean", pool(lambda r: all(r[c] == "absent" for c in ("acne", "hyperpigmentation", "eczema"))), 3)]
chosen = []
for name, ids, n in groups: chosen += [(name, i) for i in ids[:n]]
print("photos:", len(chosen), {g[0]: min(len(g[1]), g[2]) for g in groups})

# ------------------------------------------------------------ run the reference
out = []; worst_cache = 0.0; n_cache_ok = 0
for kind, iid in chosen:
    m = man[iid]; rel = f"{m['label_class']}/{m['filename']}"; path = f"{CLASSES}/{rel}"
    bgr = cv2.imread(path); H, W = bgr.shape[:2]; t0 = time.time()
    pools = {c: detect_condition(SPEC[c], bgr) for c in CONDS}; t_det = time.time() - t0
    x = pre(bgr); eo = g_sess.run(None, {g_sess.get_inputs()[0].name: x}); emb = [a for a in eo if a.shape[-1] == 128][0][0]; lg = [a for a in eo if a.shape[-1] == 3][0][0]
    gl = np.log(np.clip(1 / (1 + np.exp(-lg.astype(np.float64))), 1e-4, 1 - 1e-4) / (1 - np.clip(1 / (1 + np.exp(-lg.astype(np.float64))), 1e-4, 1 - 1e-4))).astype(np.float32)
    de = det_ev(pools)
    feed = lambda d: dict(g_emb=emb[None].astype(np.float32), g_logit=gl[None], g_extra=np.zeros((1, 2), np.float32), det_ev=d[None], roi_feat=np.zeros((1, 12, 83), np.float32), roi_mask=np.zeros((1, 12), np.float32))
    ly = f_yolo.run(None, feed(de))[0][0]; lo = f_only.run(None, feed(np.zeros(21, np.float32)))[0][0]; py, po = platt(ly, cal_y), platt(lo, cal_g)
    avail, det = de[0::7] > 0.5, de[1::7] > 0.5; flags = []
    for j in range(3):
        if avail[j] and ((po[j] >= THR_G[j]) != bool(det[j])): flags.append("cnn_yolo_conflict_" + CONDS[j])
        if abs(py[j] - THR_Y[j]) < BAND: flags.append("near_threshold_" + CONDS[j])
    state, dec, sup = decide(py, avail, flags); v5p = v5.run(None, {v5.get_inputs()[0].name: x})[0][0]
    # faithfulness: does this replica reproduce the training-time detections of the same photograph?
    cr = cache[iid]["detectors"]; diff = 0.0; ok = True
    for c in CONDS:
        cb = sorted([b for b in cr[c]["boxes"]], key=lambda b: -b["conf"]); rb = pools[c]
        if len(cb) != len(rb): ok = False; continue
        for a, b in zip(cb, rb): diff = max(diff, abs(a["conf"] - b["conf"]), abs(a["cx"] - b["cx"]), abs(a["cy"] - b["cy"]), abs(a["rel_area"] - b["rel_area"]))
    worst_cache = max(worst_cache, diff); n_cache_ok += int(ok)
    out.append(dict(kind=kind, image_id=iid, rel_path=rel, sha256=sha(path), width=W, height=H, v5_probs=[float(v) for v in v5p], emb=[float(v) for v in emb], logits=[float(v) for v in lg],
                    pools=pools, det_ev=[float(v) for v in de], fusion_global_yolo_logits=[float(v) for v in ly], fusion_global_only_logits=[float(v) for v in lo],
                    calibrated_global_yolo=[float(v) for v in py], calibrated_global_only=[float(v) for v in po], flags=flags, state=state, decisions=dec, absence_supported=[bool(s) for s in sup], seconds_detectors=round(t_det, 2)))
    print(f"  {iid} {kind:18s} {W}x{H} state={state:28s} deployed boxes={[sum(b['deployed'] for b in pools[c]) for c in CONDS]} pool={[len(pools[c]) for c in CONDS]} cache_match={ok} ({diff:.5f}) det_s={t_det:.1f}")
json.dump(dict(created=time.strftime("%Y-%m-%dT%H:%M:%S"), note="Python reference outputs for the C# real-photo parity tests. Photos are referenced by Classes-relative path + SHA-256 and are not copied.",
               replica_vs_training_cache=dict(photos=len(out), same_box_count_for_all_conditions=n_cache_ok, max_abs_diff_conf_cx_cy_area=worst_cache),
               tolerances=dict(v5_probs=1e-4, global_logits=1e-3, pool_conf=2e-3, pool_coords_px=0.5, det_ev=2e-3, fusion_logits=2e-3, calibrated=2e-3), photos=out), open(f"{DEP}/v6_real_photo_golden.json", "w"), indent=1)
print(f"replica vs training cache: same box count on {n_cache_ok}/{len(out)} photos, max abs diff {worst_cache:.6f}")
