# 49b. V5 vs V6 on validation images V5 never trained on (development data only; the locked test split is NOT touched)
# Paste into a NEW cell of the open_run02 notebook (kernel must still hold the run state) and run it once.
_vdf, _vsets = cached_roi_sets("validation"); _vdf = _vdf.reset_index(drop=True)
_b = DF.set_index("image_id").loc[_vdf.image_id]
_y = _b[["y_acne", "y_hyperpigmentation", "y_eczema"]].to_numpy().copy(); _y[~_b.eval_ok.values] = -1
_ex = v5_exposure_flags(); _exposed = dict(zip(DF.image_id.values, _ex.exposed.values))
_keep = np.array([not _exposed[i] for i in _vdf.image_id]) & (_y >= 0).all(axis=1)      # V5-unseen AND fully verified
_grp = _vdf.group_id.values[_keep]; _yk = _y[_keep]
print(f"validation images: {len(_vdf)} | V5-unseen and fully verified: {int(_keep.sum())} | hyperpigmentation present: {int((_yk[:, 1] == 1).sum())} | all-negative: {int((_yk == 0).all(1).sum())}")

# thresholds come from the FUSION split only (same rule as section 39)
_yf = FUSION_STATE["y"]; _ffd = FUSION_STATE["df"]; _gf = global_inputs(_ffd)["scores"]; _v5f = v5_scores(_ffd)
def _thr(sc): return [best_f1_threshold(_yf[_yf[:, j] >= 0, j], sc[_yf[:, j] >= 0, j])[0] if ((_yf[:, j] == 1).sum() >= CFG.MIN_POS_FOR_METRIC) else 0.5 for j in range(3)]
_thrB, _thrA2 = _thr(_gf), _thr(_v5f)

_S = {}
_S["A_v5_argmax"] = v5_argmax_decisions(_vdf)
_S["A2_v5_thresholded"] = (v5_scores(_vdf) >= np.array(_thrA2)).astype(float)
_S["B_global_E"] = (global_inputs(_vdf)["scores"] >= np.array(_thrB)).astype(float)
for _n, _v in [("E_global_plus_yolo", "global_yolo"), ("F_full_fusion", "full"), ("D_roi_only", "roi_only"), ("X_global_recalibrated", "global_only")]:
    _X = assemble(_vdf, _vsets, _v, max_tier=3)[0]
    _p = apply_platt(fusion_predict(_X, _v), CALIB[_v]["platt"])
    _S[_n] = (_p >= np.array(CALIB[_v]["thresholds"])).astype(float)
_S = {k: v[_keep] for k, v in _S.items()}

_HP = CFG.CONDITIONS.index("Hyperpigmentation")
def _missed_rate(y, d):                                    # hyperpigmentation present but the system names NO condition
    pos = y[:, _HP] == 1
    return float(((d[pos] < 0.5).all(1)).mean()) if pos.any() else np.nan
def _hp_recall(y, d):
    pos = y[:, _HP] == 1
    return float((d[pos, _HP] >= 0.5).mean()) if pos.any() else np.nan
def _false_alarm_rate(y, d):                               # all-negative image flagged hyperpigmentation
    neg = (y == 0).all(1)
    return float((d[neg, _HP] >= 0.5).mean()) if neg.any() else np.nan

_rows = []
for _name, _d in _S.items():
    _m = metrics_dec(_yk, np.full(_d.shape, np.nan), _d)
    _rows.append(dict(system=_name, n=len(_yk), hp_pos=int((_yk[:, _HP] == 1).sum()), hp_missed_as_no_condition=int(round(_missed_rate(_yk, _d) * (_yk[:, _HP] == 1).sum())),
                      hp_recall=_hp_recall(_yk, _d), all_neg=int((_yk == 0).all(1).sum()), all_neg_flagged_hp=int(round(_false_alarm_rate(_yk, _d) * (_yk == 0).all(1).sum())),
                      f1_acne=_m.f1[0], f1_hyper=_m.f1[1], f1_eczema=_m.f1[2], macro_f1=macro_f1_dec(_yk, _d)))
FAIR = pd.DataFrame(_rows); display(FAIR.round(3))

# paired differences vs V5 (cluster bootstrap over leakage groups; negative = V6 better for the two error rates, positive = better for macro F1)
_prow = []
for _name in ["B_global_E", "E_global_plus_yolo", "F_full_fusion"]:
    for _label, _fn, _good in [("macro_f1", lambda idx, a=_name: macro_f1_dec(_yk[idx], _S[a][idx]) - macro_f1_dec(_yk[idx], _S["A_v5_argmax"][idx]), "higher"),
                               ("hp_missed_rate", lambda idx, a=_name: _missed_rate(_yk[idx], _S[a][idx]) - _missed_rate(_yk[idx], _S["A_v5_argmax"][idx]), "lower"),
                               ("false_alarm_rate", lambda idx, a=_name: _false_alarm_rate(_yk[idx], _S[a][idx]) - _false_alarm_rate(_yk[idx], _S["A_v5_argmax"][idx]), "lower")]:
        _lo, _hi, _nv = cluster_bootstrap(_grp, _fn)
        _prow.append(dict(system=_name, vs="A_v5_argmax", metric=_label, difference=_fn(np.arange(len(_yk))), ci_lo=_lo, ci_hi=_hi, better_when=_good, excludes_zero=bool(_lo == _lo and (_lo > 0 or _hi < 0))))
PAIR = pd.DataFrame(_prow); display(PAIR.round(3))
write_csv(FAIR, os.path.join(CFG.DIRS["metrics"], "v5_vs_v6_fair_set.csv")); write_csv(PAIR, os.path.join(CFG.DIRS["metrics"], "v5_vs_v6_fair_set_paired.csv"))
print("Development data only. Validation was used to choose Global E over D and to stop ROI training, so V6 may look slightly better here than on fresh photos. V5 was not tuned on it.")
print("An interval that includes 0 means the data do not show a difference; it does not show equivalence. Abstention (system G) is not in this table.")
