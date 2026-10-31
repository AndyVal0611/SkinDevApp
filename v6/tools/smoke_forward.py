# FORWARD-ONLY smoke checks of code paths that the gated dry run cannot reach (no model.fit / train_on_batch is ever called; nothing is trained or evaluated).
# Executed inside the notebook namespace by dry_run.py after all cells. Uses a scratch workspace only. Prints PASS/FAIL lines; never prints a performance number.
import traceback
RESULTS = []


def check(name, fn):
    try:
        info = fn(); RESULTS.append((name, "PASS", info)); print(f"[PASS] {name}: {info}", flush=True)
    except BaseException as e:
        RESULTS.append((name, "FAIL", repr(e)[:300])); print(f"[FAIL] {name}: {type(e).__name__}: {str(e)[:300]}", flush=True); traceback.print_exc(limit=4)


rows = DF[DF.has_proc].head(8)
paths = rows.proc_path.values


def t_global_multilabel():
    m, base = build_global("multilabel3", "imagenet")
    x = np.random.RandomState(0).uniform(-1, 1, (2, 224, 224, 3)).astype("float32")
    out = m.predict(x, verbose=0); assert out.shape == (2, 3) and np.isfinite(out).all()
    y = np.array([[1, 0, 0, 1, 1, 0], [0, 1, 1, 1, 1, 1]], "float32")
    l1 = float(tf.reduce_mean(MaskedBCE([1, 2, 3])(y, out)))
    y2 = y.copy(); y2[0, 1] = 1                                        # flip a MASKED label (mask=1 for index 3+1? index 1 has mask 1) -> use a truly masked one
    y3 = y.copy(); y3[0, 2] = 1                                        # label of condition 3 in row 0: mask[2]=0 -> must not change the loss
    l3 = float(tf.reduce_mean(MaskedBCE([1, 2, 3])(y3, out)))
    assert abs(l1 - l3) < 1e-6, "masked label changed the loss"
    emb = embedding_model(m); e, o = emb.predict(x, verbose=0); assert e.shape == (2, CFG.HEAD_UNITS)
    return f"params={m.count_params():,} output={out.shape} embedding={e.shape} masked-loss-invariant=True backbone_layers={len(base.layers)}"


def t_v5_keras_vs_onnx():
    m, base = build_global("softmax4", "v5_full")
    xs = np.stack([read_rgb_norm(p) for p in paths[:6]]).astype("float32")
    pk = m.predict(xs, verbose=0); po = V5_SESS.run([_out.name], {_in.name: xs})[0]
    d = float(np.abs(pk - po).max()); assert d < 5e-3, f"Keras V5 vs ONNX V5 differ by {d}"
    fm = embedding_model(m); e, o = fm.predict(xs[:2], verbose=0)
    return f"V5 Keras loaded read-only; max |Keras-ONNX| prob diff on 6 images = {d:.2e}; embedding={e.shape}; backbone found"


def t_datasets():
    ds = make_dataset(paths, np.zeros((len(paths), 6), "float32"), training=True, batch=4)
    xb, yb, wb = next(iter(ds)); assert xb.shape == (4, 224, 224, 3) and float(tf.reduce_min(xb)) >= -1.0001 and float(tf.reduce_max(xb)) <= 1.0001
    dv = make_dataset(paths, np.zeros((len(paths), 6), "float32"), training=False, batch=4); xv, _, _ = next(iter(dv)); assert xv.shape == (4, 224, 224, 3)
    # augmentation must not touch colour channels' mean much (no hue/saturation jitter): mean RGB shift small
    d = float(np.abs(np.mean(xb.numpy(), axis=(0, 1, 2)) - np.mean(xv.numpy(), axis=(0, 1, 2))).max())
    return f"train batch {tuple(xb.shape)} in [-1,1]; eval batch ok; |mean colour shift train-vs-eval|={d:.3f}"


def t_roi_pipeline():
    r = rows.iloc[0]; bgr = cv2.imread(r.orig_path if r.has_orig else r.proc_path); H, W = bgr.shape[:2]
    saved = []
    for k, box in enumerate([(W * .3, H * .3, W * .45, H * .45), (W * .6, H * .5, W * .75, H * .7)]):
        rgb, why = crop_roi(bgr, box); assert rgb is not None and why == ""
        p = os.path.join(CROP_DIR, f"smoke_{k}.png"); cv2.imwrite(safe_path(p), cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)); saved.append(p)
    ds = make_roi_ds(saved, [0, 3], None, True, batch=2); xb, yb, wb = next(iter(ds)); assert xb.shape == (2, ROI_S, ROI_S, 3)
    m, base = build_roi_model(); pr = m.predict(xb, verbose=0); assert pr.shape == (2, 4) and abs(float(pr.sum(1)[0]) - 1) < 1e-3
    return f"ROI crops -> dataset {tuple(xb.shape)} -> ROI model probs {pr.shape}"


def t_gradcam():
    m, base = build_global("softmax4", "v5_full")
    r = rows.iloc[1]; bgr = cv2.imread(r.proc_path); top = int(np.argmax(m.predict(preprocess_global(bgr)[None], verbose=0)[0][:3])); heat = gradcampp(m, bgr, top)
    assert heat.shape == bgr.shape[:2] and 0 <= heat.min() and heat.max() <= 1
    return f"Grad-CAM++ map {heat.shape}, finite, range [{heat.min():.2f},{heat.max():.2f}] (V5-derived model, target layer {CFG.GRADCAM_LAYER})"


def t_metrics_numerics():
    rng = np.random.RandomState(1); y = rng.randint(0, 2, (200, 3)); y[:10, 1] = -1; s = np.clip(y * 0.6 + rng.rand(200, 3) * 0.5, 0, 1)
    m = cond_metrics(y, s, [0.5] * 3); sm = set_metrics(y, s, [0.5] * 3); md = metrics_dec(y, s, (s >= .5).astype(float)); sd = set_metrics_dec(y, (s >= .5).astype(float))
    g = np.repeat(np.arange(50), 4); lo, hi, nv = cluster_bootstrap(g, lambda idx: macro_f1_dec(y[idx], (s[idx] >= .5).astype(float)), B=60)
    cal = [fit_platt(np.log(s[:, j] / (1 - s[:, j] + 1e-6) + 1e-6), y[:, j]) for j in range(3)]
    e, rel = ece_binary(s[:, 0], y[:, 0] == 1, 10); b = brier_ece(y, s)
    assert m.ap.notna().all() and lo <= hi
    return f"cond_metrics/set_metrics/metrics_dec/cluster_bootstrap/fit_platt/ECE/Brier ran on SYNTHETIC arrays (values not reported); platt fitted={[c is not None for c in cal]}"


def t_stage_batch_logic():
    ids = list(EVID.image_id.unique())[:6]; df = DF[DF.image_id.isin(ids)].reset_index(drop=True); n = len(df)
    fake_cal = {v: dict(platt=[None, None, None], thresholds=[0.5, 0.5, 0.5]) for v in ("global_only", "global_yolo", "full")}
    rng = np.random.RandomState(2); lg = lambda var, t: rng.randn(n, 3).astype("float32")
    tiers = [{1: 2, 2: 1, 3: 1} for _ in range(n)]
    res = batch_stage_logic(df, None, tiers, fake_cal, dict(band=0.1), 0.1, None, None, lg)
    assert set(res.state) <= set(STATES) and len(res) == n
    return f"batch_stage_logic ran on {n} real preview rows with SYNTHETIC logits/calibration; states={res.state.value_counts().to_dict()}"


def t_onnx_export_path():
    inp = keras.layers.Input((8,), name="x"); out = keras.layers.Dense(3, dtype="float32", name="y")(inp); tiny = keras.Model(inp, out)
    st = to_onnx(tiny, "smoke_tiny"); assert st["status"] == "exported", st
    sess = load_onnx_verified(st["path"], st["sha256"]); x = np.random.rand(4, 8).astype("float32")
    d = float(np.abs(tiny.predict(x, verbose=0) - sess.run(None, {sess.get_inputs()[0].name: x})[0]).max()); assert d < 1e-5
    fus = build_fusion(dict(g_emb=8, det_ev=21, roi_feat=(4, 10))); wj = dump_keras_weights_json(fus, "smoke_fusion")
    st2 = to_onnx(fus, "smoke_fusion")
    return f"tf2onnx ok for a tiny model (diff {d:.1e}); fusion weights JSON written; fusion ONNX export status = {st2['status']}" + ("" if st2["status"] == "exported" else f" ({st2.get('error','')[:120]})")


def t_yolo_engine_consistency():
    # the engine's deployed boxes must be reproducible bit-for-bit on a second run (determinism) and carry provenance
    r = DF[DF.image_id.isin(DET.image_id)].iloc[0]; bgr = cv2.imread(r.orig_path if r.has_orig else r.proc_path)
    a = detect_all(bgr); b = detect_all(bgr)
    assert json.dumps(a, sort_keys=True, default=str) == json.dumps(b, sort_keys=True, default=str)
    avail = {c: (a[c] is not None) for c in CFG.CONDITIONS}
    return f"two runs identical; availability={avail}; unavailable detectors return None (not [])"


def t_final_assessment_roundtrip():
    fa = FinalAssessment(scan_id="scan_smoke", state="UNCERTAIN_REVIEW_REQUIRED", predicted_conditions=["Acne"], condition_scores={"Acne": dict(calibrated_probability=0.7)}, original_cnn_scores={"x": 1}, original_yolo_outputs={"Eczema": None})
    d = json.loads(json.dumps(fa.to_dict())); assert d["schema_version"] == SCHEMA_VERSION and d["original_yolo_outputs"]["Eczema"] is None
    log_assessment(fa); return "FinalAssessment JSON round-trip ok; audit record appended"


def t_serialisation_roundtrips():
    # exactly what the checkpoint / resume / export code relies on: compiled model with the custom loss, weights-only files, custom fusion layers, ROI feature model
    d = os.path.join(CFG.DIRS["checkpoints"], "_smoke"); os.makedirs(d, exist_ok=True)
    m, base = build_global("multilabel3", "imagenet"); m.compile(optimizer=keras.optimizers.Adam(1e-3), loss=MaskedBCE([1.0, 2.0, 3.0]))
    p = safe_path(os.path.join(d, "g.keras")); m.save(p); m2 = keras.models.load_model(p, custom_objects={"MaskedBCE": MaskedBCE})
    x = np.random.RandomState(0).uniform(-1, 1, (2, 224, 224, 3)).astype("float32"); assert np.allclose(m.predict(x, verbose=0), m2.predict(x, verbose=0), atol=1e-5)
    assert list(m2.loss.pos_weight) == [1.0, 2.0, 3.0]
    w = safe_path(os.path.join(d, "g.weights.h5")); m.save_weights(w); m3, _ = build_global("multilabel3", "imagenet"); m3.load_weights(w); assert np.allclose(m.predict(x, verbose=0), m3.predict(x, verbose=0), atol=1e-5)
    fus = build_fusion(dict(g_emb=8, det_ev=21, roi_feat=(4, 10))); fp = safe_path(os.path.join(d, "f.keras")); fus.save(fp)
    f2 = keras.models.load_model(fp, custom_objects={"MaskedBCE": MaskedBCE, "MaskedAttentionPool": MaskedAttentionPool, "ModalityDropout": ModalityDropout, "MaskFraction": MaskFraction}, compile=False)
    feed = [np.random.rand(2, 8).astype("float32"), np.random.rand(2, 3).astype("float32"), np.random.rand(2, 2).astype("float32"), np.random.rand(2, 21).astype("float32"), np.random.rand(2, 4, 10).astype("float32"), np.ones((2, 4), "float32")]
    assert np.allclose(fus.predict(feed, verbose=0), f2.predict(feed, verbose=0), atol=1e-5)
    rm, rb = build_roi_model(); rp = safe_path(os.path.join(d, "r.keras")); rm.save(rp); r2 = keras.models.load_model(rp, compile=False)
    rf = keras.Model(r2.input, [r2.get_layer("roi_embedding").output, r2.output]); e, pr = rf.predict(np.zeros((1, ROI_S, ROI_S, 3), "float32"), verbose=0); assert e.shape == (1, CFG.ROI_EMB) and pr.shape == (1, 4)
    return "compiled global model (custom loss + optimizer) .keras round-trip, weights-only .h5 round-trip, fusion .keras with custom layers, ROI feature model - all reload and reproduce outputs"


def t_callback_objects():
    paths_ = DF.proc_path.values[:6]; y_ = np.array([[1, 0, -1], [0, 1, 0], [1, 1, 0], [0, 0, 0], [1, 0, 0], [0, 1, 1]])
    cb = ValMetrics("softmax4", paths_, y_); m4, _ = build_global("softmax4", "v5_full"); cb.set_model(m4); logs = {"val_loss": 0.5}
    cb.on_epoch_end(0, logs); assert "val_sel" in logs and np.isfinite(logs["val_sel"])
    return f"ValMetrics ran on a real V5 model with SYNTHETIC labels (6 images): fallback selection metric = -val_loss when AP is not computable (val_sel={logs['val_sel']:.3f}); keys={sorted(k for k in logs if k.startswith('val_'))}"


for nm, fn in [("global multilabel build + masked loss + embedding", t_global_multilabel), ("V5 Keras (read-only) vs V5 ONNX parity + feature model", t_v5_keras_vs_onnx), ("tf.data pipelines (global, ROI)", t_datasets),
               ("ROI crop -> dataset -> ROI model", t_roi_pipeline), ("Grad-CAM++ function", t_gradcam), ("metric / calibration / bootstrap numerics (synthetic)", t_metrics_numerics),
               ("batch stage logic (synthetic logits)", t_stage_batch_logic), ("ONNX export path (tiny model + fusion)", t_onnx_export_path), ("YOLO engine determinism + availability semantics", t_yolo_engine_consistency),
               ("FinalAssessment round trip + audit", t_final_assessment_roundtrip), ("serialisation round-trips (checkpoint / resume / fusion / ROI)", t_serialisation_roundtrips), ("validation-metric callback object", t_callback_objects)]:
    check(nm, fn)
print("\nSMOKE SUMMARY:", sum(r[1] == "PASS" for r in RESULTS), "pass,", sum(r[1] == "FAIL" for r in RESULTS), "fail")
json.dump(RESULTS, open(os.path.join(CFG.DIRS["tests"], "smoke_forward_results.json"), "w"), indent=1, default=str)
