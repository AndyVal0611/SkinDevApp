"""Adds the full-fusion variant (Global + YOLO + ROI MobileNetV2) to the V6 deployment package.

Run:  python -I v6/tools/add_full_variant.py
- verifies the two extra ONNX files against the export metadata hashes,
- copies them to SkinDevApp/Model/V6 and v6/deployment is patched (config only gains optional entries; global_yolo is untouched),
- ROI constants are copied from CFG in 02_environment_security.cellsrc (ROI_MARGIN ... ROI_MAX_TOTAL).
"""
import hashlib, json, os, shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RUN = f"{ROOT}/v6/astra_runs/open_run02"; EXP = f"{RUN}/export"; DEP = f"{ROOT}/v6/deployment"; APP = f"{ROOT}/SkinDevApp/Model/V6"
sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
meta = json.load(open(f"{EXP}/v6_model_metadata.json")); status = meta["export_status"]
cal = json.load(open(f"{RUN}/fusion/calibration_thresholds.json"))

files = {"fusion_full": ("v6_fusion_full_seed42.onnx", "fusion_full_seed42"), "roi_features": ("v6_roi_features_mobilenetv2.onnx", "roi_features")}
cfg = json.load(open(f"{DEP}/v6_deployment_config.json"))
for key, (name, skey) in files.items():
    src = f"{EXP}/{name}"; h = sha(src)
    assert h == status[skey]["sha256"], f"{name}: hash differs from the export metadata"
    shutil.copyfile(src, f"{APP}/{name}")
    cfg["models"][key] = dict(file=name, sha256=h)
cfg["models"]["fusion_full"].update(inputs=["g_emb[N,128]", "g_logit[N,3]", "g_extra[N,2]", "det_ev[N,21]", "roi_feat[N,12,83]", "roi_mask[N,12]"], output="Identity:0 -> logits [N,3]",
                                    runtime_note="g_extra is [0,0]; roi_feat/roi_mask built from the YOLO-guided regions (see 'roi')")
cfg["models"]["roi_features"].update(input="roi_nhwc_m1p1[N,224,224,3] (RGB, (p/127.5)-1)", outputs=["Identity:0 -> embedding [N,64]", "Identity_1:0 -> softmax [N,4] (Acne, Hyperpigmentation, Eczema, Background)"])
cfg["calibration"]["full"] = cal["full"]
cfg["roi"] = dict(size=224, margin=0.25, alt_margin=0.60, min_side_frac=0.02, max_tier1=6, max_total=12, overlap_suppress_iou=0.70, embedding=64, classes=4,
                  feature_dim=83, note="port of build_roi_candidates / crop_roi / yolo_roi_features in 07_roi_branch.cellsrc")
cfg["decision"]["full_note"] = "full variant: calibration.full thresholds and band 0.0 (no band; the band was only ever selected on global_yolo, which also gave 0.0)"
json.dump(cfg, open(f"{DEP}/v6_deployment_config.json", "w"), indent=1)
shutil.copyfile(f"{DEP}/v6_deployment_config.json", f"{APP}/v6_deployment_config.json")
print("ok", {k: v["sha256"][:12] for k, v in cfg["models"].items()})
