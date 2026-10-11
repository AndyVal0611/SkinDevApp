"""
gradcam_enrichment.py - does the Grad-CAM++ map actually point at the lesions you boxed?  (research tool, never touches the live service)

For every hand-boxed acne test photo it measures, for the ACNE class map:
  enrichment   share of the map's heat that falls inside your boxes divided by the share of the picture those boxes cover.
               1.0 = no better than a flat map; 2.0 = twice as much heat on the lesions as chance.
  pointing     does the single hottest pixel fall inside one of your boxes?  (compared with the chance of that by box area)
and compares with two baselines that know nothing about the photo: a flat map, and the AVERAGE acne map of all photos (a fixed face-centre blob).
If the real maps do not beat the average-map baseline, they carry no photo-specific information about the lesions.

It also scores a "contrast" map  relu(cam_c - mean of the other classes' cams)  and how alike the four class maps are.

    python gradcam_enrichment.py --model precisionskin_best.keras --photos <test/images> --labels <test/labels>
"""
import argparse, os, sys
from pathlib import Path
import numpy as np

os.environ.setdefault("CUDA_VISIBLE_DEVICES", "-1"); os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cv2
import tensorflow as tf
import gradcam_service as G

S = 224


def boxes_mask(label_path):
    m = np.zeros((S, S), np.float32)
    for ln in Path(label_path).read_text().splitlines():
        t = ln.split()
        if len(t) >= 5:
            cx, cy, w, h = map(float, t[1:5])
            x0, x1 = int(round((cx - w / 2) * S)), int(round((cx + w / 2) * S)); y0, y1 = int(round((cy - h / 2) * S)), int(round((cy + h / 2) * S))
            m[max(0, y0):min(S, y1), max(0, x0):min(S, x1)] = 1.0
    return m


def up(cam):
    return np.maximum(cv2.resize(np.maximum(np.asarray(cam, np.float32), 0), (S, S), interpolation=cv2.INTER_CUBIC), 0)


def enrich(heat, mask):
    tot = heat.sum()
    return float((heat * mask).sum() / tot / mask.mean()) if tot > 1e-9 and mask.mean() > 0 else float("nan")


def point_hit(heat, mask):
    y, x = np.unravel_index(int(np.argmax(heat)), heat.shape)
    return bool(mask[y, x] > 0)


def pearson(a, b):
    a = a.ravel() - a.mean(); b = b.ravel() - b.mean(); d = np.sqrt((a * a).sum() * (b * b).sum())
    return float((a * b).sum() / d) if d > 1e-12 else 0.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="precisionskin_best.keras"); ap.add_argument("--photos", required=True); ap.add_argument("--labels", required=True)
    ap.add_argument("--layers", nargs="+", default=["Conv_1", "block_13_expand_relu"]); ap.add_argument("--limit", type=int, default=0)
    a = ap.parse_args()
    model = tf.keras.models.load_model(a.model, compile=False)
    imgs = sorted(p for p in Path(a.photos).iterdir() if p.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp", ".bmp"})
    imgs = [p for p in imgs if (Path(a.labels) / (p.stem + ".txt")).exists()]
    if a.limit:
        imgs = imgs[:a.limit]
    print("photos:", len(imgs))
    acne = G.CLASS_NAMES.index("Acne")
    for layer in a.layers:
        cam = G.GradCAMPlusPlus(model, layer)
        rows = []; acne_maps = []; masks = []; preds = []
        for p in imgs:
            bgr = cv2.imdecode(np.fromfile(str(p), np.uint8), cv2.IMREAD_COLOR)
            cams, pred, probs, _, _ = cam.compute_all(G.preprocess(bgr))
            ups = [up(c) for c in cams]
            others = np.mean([u for i, u in enumerate(ups) if i != acne], axis=0)
            contrast = np.maximum(ups[acne] - others, 0)
            m = boxes_mask(Path(a.labels) / (p.stem + ".txt"))
            acne_maps.append(ups[acne]); masks.append(m); preds.append(pred)
            sims = [pearson(ups[i], ups[j]) for i in range(4) for j in range(i + 1, 4)]
            rows.append(dict(e_acne=enrich(ups[acne], m), e_contrast=enrich(contrast, m), hit_acne=point_hit(ups[acne], m), hit_contrast=point_hit(contrast, m) if contrast.max() > 0 else False,
                             chance=float(m.mean()), sim=float(np.mean(sims)), pred_acne=pred == acne, e_flat=1.0))
        prior = np.mean(acne_maps, axis=0)                    # fixed "average acne map" (photo-independent)
        e_prior = [enrich(prior, m) for m in masks]; hit_prior = [point_hit(prior, m) for m in masks]
        ok = lambda k: np.nanmean([r[k] for r in rows])
        print("\n=== layer %s ===" % layer)
        print("class maps: mean Pearson between the four class maps %.2f" % ok("sim"))
        print("photos predicted Acne: %d of %d" % (sum(r["pred_acne"] for r in rows), len(rows)))
        print("enrichment (1.0 = chance):  acne map %.2f | contrast map %.2f | average-acne-map baseline %.2f | flat 1.00" % (ok("e_acne"), ok("e_contrast"), np.nanmean(e_prior)))
        print("pointing (hottest pixel inside a box):  acne map %.0f%% | contrast %.0f%% | average-map baseline %.0f%% | chance by area %.0f%%" % (
            100 * ok("hit_acne"), 100 * ok("hit_contrast"), 100 * np.mean(hit_prior), 100 * ok("chance")))
        sel = [r for r in rows if r["pred_acne"]]
        if sel:
            print("only photos the model calls Acne (%d): enrichment %.2f | pointing %.0f%%" % (len(sel), np.nanmean([r["e_acne"] for r in sel]), 100 * np.mean([r["hit_acne"] for r in sel])))


if __name__ == "__main__":
    main()
