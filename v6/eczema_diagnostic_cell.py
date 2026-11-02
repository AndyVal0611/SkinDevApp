# 49c. Why did Eczema F1 drop from V5 to V6 on V5-unseen validation photos? (development data only; test untouched)
# Run AFTER the 49b cell (it reuses _vdf, _vsets, _keep, _yk, _S, _thrA2, _thrB from that cell). Same kernel, no restart.
_E = CFG.CONDITIONS.index("Eczema")
_ids = _vdf.image_id.values[_keep]; _org = _vdf.image_origin.values[_keep] if "image_origin" in _vdf.columns else np.array(["?"] * int(_keep.sum()))
_scores = {"V5": v5_scores(_vdf)[_keep, _E], "Global E": global_inputs(_vdf)["scores"][_keep, _E]}
for _n, _v in [("Global+YOLO", "global_yolo"), ("Full fusion", "full")]:
    _scores[_n] = apply_platt(fusion_predict(assemble(_vdf, _vsets, _v, max_tier=3)[0], _v), CALIB[_v]["platt"])[_keep, _E]
_decs = {"V5": _S["A_v5_argmax"][:, _E], "Global E": _S["B_global_E"][:, _E], "Global+YOLO": _S["E_global_plus_yolo"][:, _E], "Full fusion": _S["F_full_fusion"][:, _E]}
_pos = _yk[:, _E] == 1; _neg = _yk[:, _E] == 0
print(f"eczema in the {len(_yk)} V5-unseen photos: {int(_pos.sum())} present, {int(_neg.sum())} absent")
_rows = []
for _n in _scores:
    _d = _decs[_n] >= 0.5
    _rows.append(dict(system=_n, ap=float(average_precision_score(_pos.astype(int), _scores[_n])), auc=float(roc_auc_score(_pos.astype(int), _scores[_n])),
                      tp=int((_d & _pos).sum()), fn=int((~_d & _pos).sum()), fp=int((_d & _neg).sum()), tn=int((~_d & _neg).sum())))
display(pd.DataFrame(_rows).round(3))
print("thresholds on the eczema column -> V5 thresholded (A2):", round(float(_thrA2[_E]), 3), "| Global E (B):", round(float(_thrB[_E]), 3), "| Global+YOLO:", round(float(CALIB["global_yolo"]["thresholds"][_E]), 3), "| Full:", round(float(CALIB["full"]["thresholds"][_E]), 3))
print("eczema positives in the FUSION split that set those thresholds:", int((FUSION_STATE["y"][:, _E] == 1).sum()))
_d5, _dE = _decs["V5"] >= 0.5, _decs["Full fusion"] >= 0.5
_chg = pd.DataFrame(dict(image_id=_ids, origin=_org, truth=np.where(_pos, "eczema", "no eczema"), v5_score=_scores["V5"].round(3), full_score=_scores["Full fusion"].round(3), v5_says=_d5, full_says=_dE))
_worse = _chg[(_chg.v5_says == (_chg.truth == "eczema")) & (_chg.full_says != (_chg.truth == "eczema"))]
print(f"\nphotos where V5 was right on eczema and the full fusion is wrong: {len(_worse)}"); display(_worse)
print("\nPer image_origin, eczema positives and full-fusion recall:"); display(_chg[_pos].groupby("origin").agg(n=("image_id", "size"), v5_found=("v5_says", "mean"), full_found=("full_says", "mean")).round(2))
write_csv(_chg, os.path.join(CFG.DIRS["metrics"], "eczema_fair_set_diagnostic.csv"))
