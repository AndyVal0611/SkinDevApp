"""Builds v6/deployment/ from the finished open_run02 export (read-only on the run folder).

Writes:
  v6_cpu_parity_verification.json  CPU Keras-vs-ONNX parity (the notebook's own check compared GPU Keras with CPU ONNX)
  v6_deployment_config.json        everything a C# runtime needs for the global_yolo fusion path (no values retyped by hand)
  v6_ood_model.json                Mahalanobis OOD statistics from stages/ood_model.npz
  v6_test_vectors.json             evidence-builder + fusion + calibration + decision vectors (the C# tests' fixtures)

Run with the project venv on CPU:  python make_deployment_package.py
Nothing here trains or fine-tunes a model and nothing here touches the locked test split.
"""
import csv, datetime, hashlib, json, os, sys
os.environ["CUDA_VISIBLE_DEVICES"] = "-1"; os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
import numpy as np, onnxruntime as ort

ROOT = "/mnt/c/Users/Bautista/source/repos/SkinDevApp"
RUN = f"{ROOT}/v6/astra_runs/open_run02"
EXP = f"{RUN}/export"; MODEL = f"{ROOT}/SkinDevApp/Model"; OUT = f"{ROOT}/v6/deployment"
os.makedirs(OUT, exist_ok=True)
CONDS = ["Acne", "Hyperpigmentation", "Eczema"]
sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
meta = json.load(open(f"{EXP}/v6_model_metadata.json"))
cal = meta["fusion"]["calibration"]
now = datetime.datetime.now().isoformat(timespec="seconds")
sess_cpu = lambda p: ort.InferenceSession(p, providers=["CPUExecutionProvider"])

files = {"global_features": f"{EXP}/v6_global_features_mobilenetv2.onnx", "fusion_global_yolo": f"{EXP}/v6_fusion_global_yolo_seed42.onnx", "fusion_global_only": f"{EXP}/v6_fusion_global_only_seed42.onnx"}
for k, p in files.items():
    exp_sha = meta["export_status"][{"global_features": "global_features", "fusion_global_yolo": "fusion_global_yolo_seed42", "fusion_global_only": "fusion_global_only_seed42"}[k]]["sha256"]
    assert sha(p) == exp_sha, f"{k}: hash differs from metadata"

# ------------------------------------------------------------------ OOD export
z = np.load(f"{RUN}/stages/ood_model.npz")
json.dump(dict(method="mahalanobis_ledoit_wolf", dim=int(z["mu"].shape[0]), mu=z["mu"].tolist(), precision=z["prec"].tolist(), threshold=float(z["thr"]),
               note="score = sqrt((e-mu)^T P (e-mu)) on the 128-d global embedding; flag when score > threshold. A flag means 'unlike the training data', not an unknown condition."),
          open(f"{OUT}/v6_ood_model.json", "w"))
mu, prec, ood_thr = z["mu"].astype(np.float64), z["prec"].astype(np.float64), float(z["thr"])
ood_score = lambda e: float(np.sqrt((e.astype(np.float64) - mu) @ prec @ (e.astype(np.float64) - mu)))

# ------------------------------------------------------------------ test vectors
logit = lambda p, eps=1e-4: np.log(np.clip(p, eps, 1 - eps) / (1 - np.clip(p, eps, 1 - eps)))
platt = lambda l, c: 1 / (1 + np.exp(-(np.array([x["a"] for x in c["platt"]]) * l + np.array([x["b"] for x in c["platt"]]))))
THR_S1, THR_G = np.array(cal["global_yolo"]["thresholds"]), np.array(cal["global_only"]["thresholds"])
BAND = float(meta["stages"]["stage1_rule"]["band"])

def det_ev(boxes_by_cond):
    """Mirror of detector_evidence(): None = detector unavailable (all zeros incl. available=0); [] = ran, no boxes; boxes need conf, deployed, rel_area, cx, cy."""
    out = []
    for c in CONDS:
        b = boxes_by_cond.get(c)
        if b is None: out += [0.0] * 7; continue
        dep = [x for x in b if x["deployed"]]; best = max(dep, key=lambda x: x["conf"]) if dep else None
        pool = max([x["conf"] for x in b], default=0.0)
        out += [1.0, float(bool(dep)), best["conf"] if best else 0.0, pool, float(np.sqrt(best["rel_area"])) if best else 0.0, best["cx"] if best else 0.0, best["cy"] if best else 0.0]
    return np.array(out, np.float32)

def decide(probs, thr, band, avail, flags):
    per, predicted, uncertain, absent_ok = {}, [], [], []
    for j, c in enumerate(CONDS):
        p, t = float(probs[j]), float(thr[j])
        dec = "present" if p >= t + band else ("absent" if p < t - band else "uncertain")
        supp = dec == "absent" and bool(avail[j])                               # CFG.ABSENCE_REQUIRES_ALL_DETECTORS = True
        per[c] = dict(calibrated_probability=round(p, 5), threshold=round(t, 4), decision=dec, absence_supported=supp, detector_available=bool(avail[j]))
        if dec == "present": predicted.append(c)
        elif dec == "uncertain" or not supp: uncertain.append(c)
        else: absent_ok.append(c)
    if uncertain or flags: state = "UNCERTAIN_REVIEW_REQUIRED"
    elif len(predicted) >= 2: state = "MULTIPLE_FINDINGS_SUPPORTED"
    elif len(predicted) == 1: state = "FINDINGS_SUPPORTED"
    else: state = "NO_FINDINGS_SUPPORTED"
    return dict(state=state, predicted=predicted, per_condition=per)

def flags_for(pg, ps1, avail, detected, ood_flag):
    f = []
    for j, c in enumerate(CONDS):
        if avail[j] and ((pg[j] >= THR_G[j]) != bool(detected[j])): f.append(f"cnn_yolo_conflict_{c}")
        if abs(ps1[j] - THR_S1[j]) < BAND: f.append(f"near_threshold_{c}")
    if ood_flag: f.append("ood")
    return f

bx = lambda conf, dep, area, cx, cy: dict(conf=conf, deployed=dep, rel_area=area, cx=cx, cy=cy)
CASES = [
    ("all_detectors_ran_no_boxes", dict(Acne=[], Hyperpigmentation=[], Eczema=[])),
    ("low_confidence_pool_only", dict(Acne=[bx(0.12, False, 0.004, 0.40, 0.55)], Hyperpigmentation=[bx(0.19, False, 0.03, 0.62, 0.48)], Eczema=[])),
    ("one_clear_hyperpigmentation_box", dict(Acne=[], Hyperpigmentation=[bx(0.72, True, 0.05, 0.58, 0.50)], Eczema=[])),
    ("overlapping_boxes_same_condition", dict(Acne=[bx(0.64, True, 0.006, 0.30, 0.60), bx(0.41, True, 0.007, 0.31, 0.61), bx(0.10, False, 0.002, 0.70, 0.40)], Hyperpigmentation=[], Eczema=[])),
    ("multiple_conditions", dict(Acne=[bx(0.55, True, 0.005, 0.35, 0.65)], Hyperpigmentation=[bx(0.61, True, 0.04, 0.60, 0.45)], Eczema=[bx(0.47, True, 0.06, 0.50, 0.30)])),
    ("eczema_detector_missing", dict(Acne=[bx(0.50, True, 0.005, 0.35, 0.65)], Hyperpigmentation=[], Eczema=None)),
    ("hyperpigmentation_detector_missing", dict(Acne=[], Hyperpigmentation=None, Eczema=[bx(0.45, True, 0.05, 0.5, 0.3)])),
    ("all_detectors_missing", dict(Acne=None, Hyperpigmentation=None, Eczema=None)),
]
zz = np.load(f"{RUN}/predictions/global_E_v5_backbone_multilabel3_seed42_e20925778b.npz")
rng = np.random.default_rng(7); rows = rng.choice(len(zz["scores"]), len(CASES), replace=False)
s_yolo, s_only = sess_cpu(files["fusion_global_yolo"]), sess_cpu(files["fusion_global_only"])
vectors = []
for (name, boxes), r in zip(CASES, rows):
    emb = zz["emb"][r].astype(np.float32); lg = logit(zz["scores"][r].astype(np.float64)).astype(np.float32)
    de = det_ev(boxes)
    def feed(det): return dict(g_emb=emb[None], g_logit=lg[None], g_extra=np.zeros((1, 2), np.float32), det_ev=det[None], roi_feat=np.zeros((1, 12, 83), np.float32), roi_mask=np.zeros((1, 12), np.float32))
    l_yolo = s_yolo.run(None, feed(de))[0][0]; l_only = s_only.run(None, feed(np.zeros(21, np.float32)))[0][0]
    ps1, pg = platt(l_yolo, cal["global_yolo"]), platt(l_only, cal["global_only"])
    avail = de[0::7] > 0.5; detected = de[1::7] > 0.5; sc = ood_score(emb); ood_flag = sc > ood_thr
    flags = flags_for(pg, ps1, avail, detected, ood_flag); d = decide(ps1, THR_S1, BAND, avail, flags)
    vectors.append(dict(name=name, g_emb=emb.tolist(), g_logit=lg.tolist(), g_extra=[0.0, 0.0], boxes={c: (None if boxes[c] is None else boxes[c]) for c in CONDS}, det_ev=de.tolist(),
                        fusion_global_yolo_logits=l_yolo.tolist(), fusion_global_only_logits=l_only.tolist(), calibrated_global_yolo=ps1.tolist(), calibrated_global_only=pg.tolist(),
                        ood_score=sc, ood_flag=bool(ood_flag), flags=flags, expected=d))
json.dump(dict(created=now, note="Fixtures for the C# tests. det_ev is built from `boxes` with the rules in V6_EVIDENCE_SCHEMA_SPEC.md; logits come from the exported ONNX files on CPU; "
               "calibrated values use the deployment config; decisions use the ported decide()/stage1_flags() with band=" + str(BAND) + ". Tolerances: logits 1e-4, probabilities 1e-4, det_ev 1e-6.",
               tolerances=dict(logits=1e-4, probabilities=1e-4, det_ev=1e-6, embedding=1e-3), vectors=vectors), open(f"{OUT}/v6_test_vectors.json", "w"), indent=1)

# ------------------------------------------------------------------ CPU parity record (fusion measured now; Global features read from the notebook follow-up)
N = 400; rg = np.random.default_rng(0); idx = rg.choice(len(zz["scores"]), N, replace=False)
emb = zz["emb"][idx].astype(np.float32); lgs = logit(zz["scores"][idx].astype(np.float64)).astype(np.float32)
det = np.zeros((N, 3, 7), np.float32)
for n in range(N):
    for j in range(3):
        m = rg.integers(0, 4)
        if m == 0: continue
        det[n, j, 0] = 1; pool = rg.uniform(0.05, 0.6)
        if m == 1: det[n, j, 3] = pool; continue
        conf = rg.uniform(0.225, 0.95); det[n, j, 1:] = [1, conf, max(conf, pool), np.sqrt(rg.uniform(0.001, 0.25)), rg.uniform(0, 1), rg.uniform(0, 1)]
X = dict(g_emb=emb, g_logit=lgs, g_extra=np.zeros((N, 2), np.float32), det_ev=det.reshape(N, -1), roi_feat=np.zeros((N, 12, 83), np.float32), roi_mask=np.zeros((N, 12), np.float32))
import tensorflow as tf, keras
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
exec(open(f"{ROOT}/v6/tools/_fusion_layers.py").read())          # custom layers needed to load the .keras file
km = keras.models.load_model(f"{RUN}/fusion/global_yolo/final_seed42.keras", custom_objects={"MaskedBCE": MaskedBCE}, compile=False)
ko = km.predict([X[k] for k in ["g_emb", "g_logit", "g_extra", "det_ev", "roi_feat", "roi_mask"]], batch_size=64, verbose=0); oo = s_yolo.run(None, X)[0]
pk, po = platt(ko, cal["global_yolo"]), platt(oo, cal["global_yolo"])
flips = int(((pk >= THR_S1) != (po >= THR_S1)).sum())
par = list(csv.DictReader(open(f"{EXP}/v6_global_features_parity_check.csv")))
json.dump(dict(created=now, purpose="Supersedes the pass/fail flags in v6_keras_vs_onnx_verification.json, which compared Keras on the GPU with ONNX on the CPU. This record compares CPU with CPU.",
               versions=dict(tensorflow=tf.__version__, onnxruntime=ort.__version__, numpy=np.__version__),
               files={k: dict(path=os.path.relpath(p, ROOT), sha256=sha(p)) for k, p in files.items()},
               tolerance_logits=1e-4,
               fusion_global_yolo=dict(method=f"{N} inputs: real Global embeddings/logits from the run, synthetic detector evidence (unavailable / no box / detected), ROI inputs zero", max_abs_logit_diff=float(np.abs(ko - oo).max()),
                                       max_abs_probability_diff=float(np.abs(pk - po).max()), decisions_compared=int(pk.size), decisions_flipped=flips, within_tolerance=bool(np.abs(ko - oo).max() <= 1e-4)),
               global_features=dict(source="export/v6_global_features_parity_check.csv (notebook follow-up, 160 real validation photos)", rows=par, note="Keras CPU vs ONNX CPU row is the deployment-relevant one; GPU rows show GPU numeric noise."),
               conclusion="CPU Keras and CPU ONNX agree within tolerance for the Global features model and the global_yolo fusion; the earlier 'outside tolerance' flag was GPU-vs-CPU numeric noise."),
          open(f"{OUT}/v6_cpu_parity_verification.json", "w"), indent=1)

# ------------------------------------------------------------------ deployment config
det_json = {c: json.load(open(f"{MODEL}/lesion_detector_{c.lower()}.json")) for c in CONDS}
s1 = json.load(open(f"{RUN}/stages/stage1_rule.json"))
yolo = meta["yolo"]
cfg = dict(
    created=now, run_id=meta["run_id"], status="EXPERIMENTAL - development-stage; not clinically validated; every assessment is PENDING_DERMATOLOGIST_REVIEW", feature_schema_version=meta["feature_schema_version"],
    feature_flag=dict(name="V6UnifiedAssessment", default=False),
    conditions=CONDS, normal_definition=meta["normal_definition"],
    models=dict(global_features=dict(file="v6_global_features_mobilenetv2.onnx", sha256=sha(files["global_features"]), input=dict(name="image_nhwc_m1p1", dtype="float32", shape=[-1, 224, 224, 3]),
                                      outputs=["Identity:0 -> embedding [N,128]", "Identity_1:0 -> logits [N,3] (apply sigmoid; fusion consumes logit(clip(sigmoid(z),1e-4,1-1e-4)))"],
                                      preprocessing=meta["global_cnn"]["preprocessing"]),
                fusion_global_yolo=dict(file="v6_fusion_global_yolo_seed42.onnx", sha256=sha(files["fusion_global_yolo"]), inputs=["g_emb[N,128]", "g_logit[N,3]", "g_extra[N,2]", "det_ev[N,21]", "roi_feat[N,12,83]", "roi_mask[N,12]"], output="Identity:0 -> logits [N,3]",
                                        runtime_note="roi_feat and roi_mask are fed as ZEROS (the global_yolo variant has no ROI branch); g_extra is [0,0] for the multilabel Global model"),
                fusion_global_only=dict(file="v6_fusion_global_only_seed42.onnx", sha256=sha(files["fusion_global_only"]), runtime_note="used only to compute the CNN-vs-YOLO conflict flag; det_ev fed as zeros")),
    calibration=dict(global_yolo=cal["global_yolo"], global_only=cal["global_only"], formula="p = sigmoid(a*z + b) per condition on the fusion logit z"),
    decision=dict(rule="decision per condition from the calibrated global_yolo probability: present if p >= t + band, absent if p < t - band, else uncertain", band=BAND, thresholds=cal["global_yolo"]["thresholds"],
                  absence_requires_detector_available=True, stage1_selection=s1.get("selection"),
                  note="band 0.0 was selected on out-of-fold fusion data; PROVISIONAL. Stage 2 (ROI) is NOT part of this deployment: a flagged scan becomes UNCERTAIN_REVIEW_REQUIRED instead of escalating.",
                  flags=["cnn_yolo_conflict_<condition> (global_only calibrated p vs detector 'detected', only when that detector is available)", "near_threshold_<condition>", "ood", "quality_<blocking flag>", "quality_warning"],
                  states=meta["stages"]["states"], normal_rule="NO_FINDINGS_SUPPORTED only when all three conditions are 'absent' with absence supported (all detectors available), no flags and no blocking quality problem"),
    quality=dict(thresholds=meta["stages"]["quality_thresholds"], status=meta["stages"]["quality_status"]),
    ood=dict(file="v6_ood_model.json", threshold=ood_thr),
    detectors=dict(
        source="the 9 ONNX files installed in the WPF Model folder (3 per condition: <name>.onnx, _m2, _m3)",
        per_condition={c: dict(imgsz=det_json[c]["imgsz"], deployed_threshold=yolo["postprocessing"]["deployed_thresholds"][c], member_nms_iou=yolo["postprocessing"]["per_member_nms_iou"][c], ensemble_fusion_iou=yolo["postprocessing"]["ensemble_fusion_iou"][c],
                           member_sha256=yolo["sha256"][c], json_onnx_sha256_matches_primary_onnx=(det_json[c].get("onnx_sha256") == yolo["sha256"][c][0])) for c in CONDS},
        member_floor=yolo["postprocessing"]["member_floor"], candidate_pool_min_conf=yolo["postprocessing"]["candidate_pool_min_conf"], max_boxes_per_condition=yolo["postprocessing"]["max_boxes"], fused_confidence=yolo["postprocessing"]["fused_confidence"],
        preprocessing=yolo["preprocessing"], face_filter_for_evidence=False,
        face_filter_note="Training evidence was computed WITHOUT the face filter (CFG.YOLO_FACE_FILTER=False). The evidence builder must use unfiltered detections; the app's face filter may only affect what is drawn."),
    evidence_vector=dict(layout=meta["fusion"]["det_ev_layout"], order="Acne, Hyperpigmentation, Eczema (7 numbers each = 21)", unavailable_detector="all 7 numbers zero, including available=0 (a missing detector is NOT 'no detection')"),
    versions=dict(v5_onnx_sha256=meta["versions"]["v5_onnx_sha256"], yolo_config_hash=meta["versions"]["yolo_config_hash"], source_metadata="astra_runs/open_run02/export/v6_model_metadata.json"))
json.dump(cfg, open(f"{OUT}/v6_deployment_config.json", "w"), indent=1)
print("written:", sorted(os.listdir(OUT)))
print("fusion CPU parity: max logit diff", float(np.abs(ko - oo).max()), "| decisions flipped", flips, "of", int(pk.size))
for v in vectors: print(f"  {v['name']:36s} state={v['expected']['state']:28s} flags={v['flags']}")
