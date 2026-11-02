# 51c. Is the Keras-vs-ONNX gap (0.04 on the Global feature model) a real export problem or just GPU-vs-CPU numeric noise? And does it flip any decision?
# Paste into a NEW cell of the open_run02 notebook (same kernel, after sections 50-51) and run once. Development images only; test untouched.
import onnxruntime as ort
_smp = DF[DF.split == "validation"].head(160)
_xs = np.stack([read_rgb_norm(p) for p in _smp.proc_path]).astype(np.float32)
_gm = keras.models.load_model(os.path.join(CFG.DIRS["checkpoints"], GLOBAL_SELECTED[0], f"seed_{GLOBAL_SELECTED[1]}", "final_best.keras"), custom_objects={"MaskedBCE": MaskedBCE}, compile=False)
_fm = embedding_model(_gm)
print("Keras dtype policy:", _gm.dtype_policy.name, "| compute dtype:", _gm.compute_dtype)
_k_gpu = _fm.predict(_xs, batch_size=16, verbose=0)
with tf.device("/CPU:0"):
    _k_cpu = _fm.predict(_xs, batch_size=16, verbose=0)
_st = EXPORT_STATUS["global_features"]; _sess = load_onnx_verified(_st["path"], _st["sha256"]); _inp = _sess.get_inputs()[0].name
_o = [_sess.run(None, {_inp: _xs[i:i + 16]}) for i in range(0, len(_xs), 16)]
_o_emb = np.concatenate([a[0] for a in _o]); _o_log = np.concatenate([a[1] for a in _o])
_sig = lambda z: 1 / (1 + np.exp(-z))
_thr = np.array(_thrB) if "_thrB" in globals() else np.array([0.5] * 3)
def _row(name, a, b):
    return dict(pair=name, max_abs_diff_embedding=float(np.abs(a[0] - b[0]).max()), max_abs_diff_logits=float(np.abs(a[1] - b[1]).max()),
                decisions_flipped_at_0_5=int(((_sig(a[1]) >= 0.5) != (_sig(b[1]) >= 0.5)).sum()), decisions_flipped_at_tuned_thresholds=int(((_sig(a[1]) >= _thr) != (_sig(b[1]) >= _thr)).sum()))
_ob = [_o_emb, _o_log]
PARITY_CHECK = pd.DataFrame([_row("Keras GPU vs Keras CPU", _k_gpu, _k_cpu), _row("Keras CPU vs ONNX CPU", _k_cpu, _ob), _row("Keras GPU vs ONNX CPU", _k_gpu, _ob)])
display(PARITY_CHECK.round(5))
print(f"{len(_xs)} validation images x 3 conditions = {len(_xs) * 3} decisions per pair.")
print("Reading: if Keras CPU vs ONNX CPU is near 1e-4 or below while GPU-vs-CPU is large, the gap is GPU numeric noise, not an export defect. If CPU vs ONNX is also large, the export itself differs.")
write_csv(PARITY_CHECK, os.path.join(CFG.DIRS["export"], "v6_global_features_parity_check.csv"))
keras.backend.clear_session()
