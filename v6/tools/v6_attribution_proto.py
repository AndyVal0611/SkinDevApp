"""Prototype run: V6 Grad-CAM++ on the 13 real golden photographs (+ one extra photo), faithfulness checks and overlay images.
Usage: python v6_attribution_proto.py <out_dir> [extra_image.png extra_boxes.json]
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cv2, numpy as np
from v6_attribution import V6Attribution, overlay, CONDS, COLORS_BGR

out = sys.argv[1]; os.makedirs(out, exist_ok=True)
CLASSES = "/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes"
G = json.load(open("/mnt/c/Users/Bautista/source/repos/SkinDevApp/v6/deployment/v6_real_photo_golden.json"))["photos"]
va = V6Attribution(); print("models loaded")
rows = []
for p in G:
    bgr = cv2.imread(f"{CLASSES}/{p['rel_path']}"); det = np.array(p["det_ev"], np.float32)
    y, cams = va.cams(bgr, det); ref = np.array(p["fusion_global_yolo_logits"])
    err = float(np.abs(y - ref).max())
    row = dict(id=p["image_id"], kind=p["kind"], recon_err=err)
    for j, c in enumerate(CONDS):
        t, r = va.deletion_drop(bgr, det, cams[j], j); row[c] = (t, r)
    rows.append(row)
    tiles = [bgr] + [overlay(bgr, cams[j], COLORS_BGR[j]) for j in range(3)]
    tiles = [cv2.resize(t, (320, int(320 * bgr.shape[0] / bgr.shape[1]))) for t in tiles]
    h = max(t.shape[0] for t in tiles); tiles = [cv2.copyMakeBorder(t, 0, h - t.shape[0], 0, 0, cv2.BORDER_CONSTANT, value=(40, 40, 40)) for t in tiles]
    cv2.imwrite(f"{out}/{p['image_id']}_{p['kind']}.png", np.hstack(tiles))
    print(f"{p['image_id']} {p['kind']:18s} reconstruction max |diff| {err:.2e} | fused logits {np.round(y, 2)}")
print("\nReconstruction (differentiable path vs exported pipeline): max difference over all photos = %.2e" % max(r["recon_err"] for r in rows))
for c in CONDS:
    top = np.array([r[c][0] for r in rows]); rnd = np.array([r[c][1] for r in rows])
    print(f"{c:18s} deleting top-20% of the map lowers the fused logit by {top.mean():+.3f} on average; a random 20% by {rnd.mean():+.3f}  | top beats random on {int((top > rnd).sum())}/{len(rows)} photos")
