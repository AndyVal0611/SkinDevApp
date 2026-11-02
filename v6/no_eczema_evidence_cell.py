# 49d. Candidate: fusion variant 'no_eczema_evidence' on the V5-unseen validation photos (hypothesis test, development data only; test untouched)
# Run AFTER the 49b cell (needs _vdf, _vsets, _keep, _yk, _S, _grp from that cell). Same kernel, no restart. No threshold is changed or re-tuned here.
_v = "no_eczema_evidence"
_X = assemble(_vdf, _vsets, _v, max_tier=3)[0]
_p = apply_platt(fusion_predict(_X, _v), CALIB[_v]["platt"])
_S["N_no_eczema_evidence"] = (_p >= np.array(CALIB[_v]["thresholds"])).astype(float)[_keep]
_pk = _p[_keep]
_E = CFG.CONDITIONS.index("Eczema")

_rows = []
for _name in ["A_v5_argmax", "E_global_plus_yolo", "F_full_fusion", "N_no_eczema_evidence"]:
    _d = _S[_name]; _m = metrics_dec(_yk, np.full(_d.shape, np.nan), _d)
    _rows.append(dict(system=_name, hp_missed_as_no_condition=int(round(_missed_rate(_yk, _d) * (_yk[:, _HP] == 1).sum())), all_neg_flagged_hp=int(round(_false_alarm_rate(_yk, _d) * (_yk == 0).all(1).sum())),
                      f1_acne=_m.f1[0], f1_hyper=_m.f1[1], f1_eczema=_m.f1[2], macro_f1=macro_f1_dec(_yk, _d)))
CAND = pd.DataFrame(_rows); display(CAND.round(3))
_pos = (_yk[:, _E] == 1).astype(int)
print(f"eczema ranking of the candidate on the {int(_pos.sum())} eczema photos: AP {average_precision_score(_pos, _pk[:, _E]):.3f}, AUC {roc_auc_score(_pos, _pk[:, _E]):.3f}")

_prow = []
for _ref in ["A_v5_argmax", "F_full_fusion"]:
    for _label, _fn in [("macro_f1", lambda idx, r=_ref: macro_f1_dec(_yk[idx], _S["N_no_eczema_evidence"][idx]) - macro_f1_dec(_yk[idx], _S[r][idx])),
                        ("hp_missed_rate", lambda idx, r=_ref: _missed_rate(_yk[idx], _S["N_no_eczema_evidence"][idx]) - _missed_rate(_yk[idx], _S[r][idx])),
                        ("false_alarm_rate", lambda idx, r=_ref: _false_alarm_rate(_yk[idx], _S["N_no_eczema_evidence"][idx]) - _false_alarm_rate(_yk[idx], _S[r][idx]))]:
        _lo, _hi, _nv = cluster_bootstrap(_grp, _fn)
        _prow.append(dict(candidate="N_no_eczema_evidence", vs=_ref, metric=_label, difference=_fn(np.arange(len(_yk))), ci_lo=_lo, ci_hi=_hi, excludes_zero=bool(_lo == _lo and (_lo > 0 or _hi < 0))))
CPAIR = pd.DataFrame(_prow); display(CPAIR.round(3))
write_csv(CAND, os.path.join(CFG.DIRS["metrics"], "candidate_no_eczema_evidence_fair_set.csv")); write_csv(CPAIR, os.path.join(CFG.DIRS["metrics"], "candidate_no_eczema_evidence_paired.csv"))
print("Hypothesis only: this variant was looked at because it scored higher on the fusion split. An interval that includes 0 means no difference is shown. Do not re-tune thresholds on these photos.")
