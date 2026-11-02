"""Python REFERENCE for the C# parity tests of the FULL V6 variant (Global + YOLO + ROI MobileNetV2).

Re-runs the detector replica of make_parity_golden.py (same code, executed from that file) on the photographs already listed in
v6/deployment/v6_real_photo_golden.json, then follows 07_roi_branch.cellsrc exactly: build_roi_candidates -> crop_roi -> ROI MobileNetV2 ->
yolo_roi_features -> roi_feat [12,83] / roi_mask [12] -> full fusion ONNX -> Platt (calibration 'full').
Writes v6/deployment/v6_full_variant_golden.json. The locked test split is not used.
Run with the project venv:  python make_full_golden.py
"""
import json, math, os, time
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "make_parity_golden.py"), encoding="utf-8").read()
ns = {}
exec(compile(src.split("# ------------------------------------------------------------ choose photographs")[0], "make_parity_golden.py(replica)", "exec"), ns)
import cv2, numpy as np
CONDS, DEP, ROOT, RUN, S, iou_xyxy, SPEC, detect_condition = (ns[k] for k in ("CONDS", "DEP", "ROOT", "RUN", "S", "iou_xyxy", "SPEC", "detect_condition"))
CLASSES, g_sess, pre, det_ev, platt, sha = (ns[k] for k in ("CLASSES", "g_sess", "pre", "det_ev", "platt", "sha"))

cfg = json.load(open(f"{DEP}/v6_deployment_config.json")); R = cfg["roi"]; cal_full = cfg["calibration"]["full"]
EXP = f"{RUN}/export"; f_full = S(f"{EXP}/v6_fusion_full_seed42.onnx"); roi_s = S(f"{EXP}/v6_roi_features_mobilenetv2.onnx")
M, ALT, MINSIDE, T1MAX, TOTAL, OVL = R["margin"], R["alt_margin"], R["min_side_frac"], R["max_tier1"], R["max_total"], R["overlap_suppress_iou"]


def square_crop_box(x0, y0, x1, y1, W, H, margin):
    vals = np.array([x0, y0, x1, y1], float)
    if not np.isfinite(vals).all() or x1 - x0 < 2 or y1 - y0 < 2: return None
    side = max(x1 - x0, y1 - y0) * (1 + 2 * margin); side = min(side, W, H)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    nx0 = min(max(cx - side / 2, 0), W - side); ny0 = min(max(cy - side / 2, 0), H - side)
    return int(round(nx0)), int(round(ny0)), max(int(round(side)), 2)


def crop_roi(bgr, box, margin):
    H, W = bgr.shape[:2]; x0, y0, x1, y1 = box
    if max(x1 - x0, y1 - y0) < MINSIDE * min(W, H): return None
    if x0 < -1 or y0 < -1 or x1 > W + 1 or y1 > H + 1: return None
    sq = square_crop_box(x0, y0, x1, y1, W, H, margin)
    if sq is None: return None
    cx0, cy0, side = sq; crop = bgr[cy0:cy0 + side, cx0:cx0 + side]
    if crop.size == 0 or min(crop.shape[:2]) < 2: return None
    return cv2.cvtColor(cv2.resize(crop, (224, 224), interpolation=cv2.INTER_LINEAR), cv2.COLOR_BGR2RGB)


def build_roi_candidates(det_rows, W, H):
    cand = sorted(det_rows, key=lambda r: -r["conf"]); box = lambda r: (r["x0"], r["y0"], r["x1"], r["y1"]); chosen = []
    def dup(r): return any(iou_xyxy(box(r), box(c)) > OVL for c in chosen)
    per = {c: [r for r in cand if r["deployed"] and r["condition"] == c] for c in CONDS}; t1 = []
    while len(t1) < T1MAX and any(per.values()):
        for c in CONDS:
            if per[c] and len(t1) < T1MAX:
                r = per[c].pop(0)
                if not dup(r): chosen.append(r); t1.append(r)
    n_alt = min(len(t1), max(0, TOTAL // 4)); t2 = []
    for r in cand:
        if len(t1) + len(t2) + n_alt >= TOTAL: break
        if not r["deployed"] and not dup(r): chosen.append(r); t2.append(r)
    out = []
    for tier, lst, alt in [(1, t1, False), (2, t2, False), (3, t1[:n_alt], True)]:
        for r in lst:
            wr, hr = (r["x1"] - r["x0"]) / W, (r["y1"] - r["y0"]) / H
            out.append(dict(tier=tier, alt=alt, condition=r["condition"], conf=r["conf"], deployed=r["deployed"], support_frac=r["ensemble_support"] / max(r["ensemble_size"], 1),
                            x0=r["x0"], y0=r["y0"], x1=r["x1"], y1=r["y1"], rel_area=r["rel_area"], cx=r["cx"], cy=r["cy"], w_rel=wr, h_rel=hr,
                            aspect=float(np.clip(wr * W / max(hr * H, 1e-6), 0.1, 10.0))))
    return out


def yolo_roi_features(c):
    onehot = [1.0 if c["condition"] == k else 0.0 for k in CONDS]
    return onehot + [c["conf"], c["rel_area"], c["cx"], c["cy"], c["w_rel"], c["h_rel"], float(np.log(c["aspect"])), float(c["deployed"]), c["support_frac"],
                     float(c["tier"] == 1), float(c["tier"] == 2), float(c["tier"] == 3)]


gold = json.load(open(f"{DEP}/v6_real_photo_golden.json")); photos = []
for g in gold["photos"]:
    path = f"{CLASSES}/{g['rel_path']}"; assert sha(path) == g["sha256"], "photo changed: " + path
    bgr = cv2.imread(path); H, W = bgr.shape[:2]
    pools = {c: detect_condition(SPEC[c], bgr) for c in CONDS}
    rows = []                                                                      # what the training cache stores per box (rounded)
    for c in CONDS:
        for b in pools[c]:
            rows.append(dict(condition=c, conf=b["conf"], x0=round(b["x0"], 2), y0=round(b["y0"], 2), x1=round(b["x1"], 2), y1=round(b["y1"], 2), rel_area=b["rel_area"], cx=b["cx"], cy=b["cy"],
                             deployed=b["deployed"], ensemble_support=b["ensemble_support"], ensemble_size=b["ensemble_size"]))
    feat = np.zeros((TOTAL, 83), np.float32); mask = np.zeros(TOTAL, np.float32); kept = []; crops = []; where = []
    for c in build_roi_candidates(rows, W, H):
        if len(where) >= TOTAL: break
        rgb = crop_roi(bgr, (c["x0"], c["y0"], c["x1"], c["y1"]), ALT if c["alt"] else M)
        if rgb is None: continue
        crops.append((rgb.astype(np.float32) / 127.5) - 1.0); where.append(c)
    if crops:
        emb, pr = roi_s.run(None, {roi_s.get_inputs()[0].name: np.stack(crops)})
        for k, c in enumerate(where):
            feat[k] = np.concatenate([emb[k], pr[k], yolo_roi_features(c)]); mask[k] = 1.0
            kept.append(dict(tier=c["tier"], alt=c["alt"], condition=CONDS.index(c["condition"]), conf=c["conf"], x0=c["x0"], y0=c["y0"], x1=c["x1"], y1=c["y1"], roi_probs=[float(v) for v in pr[k]]))
    emb_g = np.array(g["emb"], np.float32); lg = np.array(g["logits"], np.float64)
    p = 1 / (1 + np.exp(-lg)); p = np.clip(p, 1e-4, 1 - 1e-4); gl = np.log(p / (1 - p)).astype(np.float32)
    de = det_ev(pools)
    lf = f_full.run(None, dict(g_emb=emb_g[None], g_logit=gl[None], g_extra=np.zeros((1, 2), np.float32), det_ev=de[None], roi_feat=feat[None], roi_mask=mask[None]))[0][0]
    pf = platt(lf, cal_full)
    photos.append(dict(image_id=g["image_id"], rel_path=g["rel_path"], sha256=g["sha256"], regions=kept, n_regions=len(kept), roi_feat=feat.tolist(), roi_mask=mask.tolist(),
                       full_logits=[float(v) for v in lf], calibrated_full=[float(v) for v in pf], decisions=[bool(pf[j] >= cal_full["thresholds"][j]) for j in range(3)],
                       calibrated_global_yolo_ref=g["calibrated_global_yolo"]))
    print(f"  {g['image_id']} {g['kind']:18s} regions={len(kept):2d} full={np.round(pf, 3).tolist()} global_yolo={np.round(g['calibrated_global_yolo'], 3).tolist()}")
json.dump(dict(created=time.strftime("%Y-%m-%dT%H:%M:%S"), note="Python reference outputs of the full variant for the C# parity tests (same photographs as v6_real_photo_golden.json).",
               tolerances=dict(roi_feat=3e-3, full_logits=3e-3, calibrated=3e-3), thresholds=cal_full["thresholds"], photos=photos), open(f"{DEP}/v6_full_variant_golden.json", "w"))
print("wrote", len(photos), "photos")
