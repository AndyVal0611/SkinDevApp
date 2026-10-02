"""
PrecisionSkin - Grad-CAM++ diagnostic (research tool, does NOT touch the live service)
=====================================================================================

Answers, with numbers, the question "are the four class heatmaps really
independent, or do they only look alike?" and compares candidate target layers
by measured localisation behaviour (NOT by how nice the picture looks).

For every image x layer it reports

  1. RAW pairwise similarity of the four class CAMs (before any percentile
     normalisation / blur / colour):  Pearson r and cosine.
  2. Gradient spatial uniformity.  With a GlobalAveragePooling head,
     d logit_c / d A_k(i,j) = (d logit_c / d pooled_k) / (H*W)  for EVERY (i,j)
     of the final feature map, i.e. the gradient carries no spatial information
     and a class CAM is just  sum_k w_ck * A_k(i,j).  Reported as a mean
     coefficient-of-variation of the gradient across space (0 = perfectly uniform).
  3. Channel-weight similarity between classes (cosine of the Grad-CAM++ weights).
  4. Shared-component share: how much of each class CAM's energy is explained by
     the class-AVERAGED CAM (a high value = "all classes highlight the same thing").
  5. Faithfulness (deletion / insertion, Petsiuk et al. 2018) vs a random-order
     baseline.  This is the only criterion here that tests whether a layer's
     attribution actually tracks what the model uses, so it is the one to choose
     a layer by.  It is NOT lesion localisation - no lesion ground truth exists.
  6. Optional: agreement with the heatmaps the running service already saved
     (heatmap_<Class>.png in a capture folder) to prove this script reproduces
     the service's numbers.

Nothing here shifts, masks or recolours a map.

Usage (from the folder that holds gradcam_service.py, inside the service venv):
    python gradcam_diagnose.py --model precisionskin_best.keras ^
        --images "C:\\...\\Captures\\2026-10-02\\130519_875e088c\\analysed.png" ^
        --saved-heatmaps "C:\\...\\Captures\\2026-10-02\\130519_875e088c" --out diag_out
"""
from __future__ import annotations

import argparse
import itertools
import json
import os
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gradcam_service as gs  # noqa: E402  (reuses the service's own preprocess / CAM maths)

CLASSES = gs.CLASS_NAMES                      # Acne, Hyperpigmentation, Eczema, Normal
BGR = {"Acne": (0, 0, 255), "Hyperpigmentation": (255, 0, 0),
       "Eczema": (0, 190, 255), "Normal": (0, 200, 0)}
DEFAULT_LAYERS = ["Conv_1", "out_relu", "block_13_expand_relu",
                  "block_10_expand_relu", "block_6_expand_relu", "block_3_expand_relu"]


# ------------------------------------------------------------------ helpers
def pearson(a, b):
    a = a.ravel() - a.mean()
    b = b.ravel() - b.mean()
    d = np.sqrt((a * a).sum() * (b * b).sum())
    return float((a * b).sum() / d) if d > 1e-12 else float("nan")


def cosine(a, b):
    a = a.ravel()
    b = b.ravel()
    d = np.linalg.norm(a) * np.linalg.norm(b)
    return float((a * b).sum() / d) if d > 1e-12 else float("nan")


def pairs_stat(vectors, fn):
    vals = [fn(vectors[i], vectors[j]) for i, j in itertools.combinations(range(len(vectors)), 2)]
    vals = [v for v in vals if np.isfinite(v)]
    return (float(np.mean(vals)), float(np.min(vals)), float(np.max(vals))) if vals else (float("nan"),) * 3


# --------------------------------------------------------- gradients per class
def class_gradients(eng, x, contrast=False):
    """Return conv (H,W,K), logits(4,), probs(4,), grads list[4] of (H,W,K).
    Every class gets its OWN tape.gradient on its OWN scalar target."""
    tf = eng.tf
    x_t = tf.convert_to_tensor(x, dtype=tf.float32)
    with tf.GradientTape(persistent=True) as tape:
        conv, logits, probs = eng._forward(x_t, tape=tape)
        targets = []
        for c in range(len(CLASSES)):
            if contrast:                                   # logit_c - mean(other logits)
                others = [logits[:, j] for j in range(len(CLASSES)) if j != c]
                targets.append(logits[:, c] - tf.add_n(others) / float(len(others)))
            else:
                targets.append(logits[:, c])
    grads = [tf.cast(tape.gradient(t, conv), tf.float32).numpy()[0] for t in targets]
    del tape
    return (tf.cast(conv, tf.float32).numpy()[0], logits.numpy()[0], probs.numpy()[0], grads)


def raw_cams(conv, grads):
    cams, notes_all = [], []
    for g in grads:
        notes = []
        cam = gs.GradCAMPlusPlus._gradcam_plus_plus(conv, g, notes)
        if cam is None:
            cam = np.zeros(conv.shape[:2], np.float32)
            notes.append("degenerate -> zeros")
        cams.append(cam)
        notes_all.append(notes)
    return cams, notes_all


def grad_weights(conv, g1):
    g2 = g1 * g1
    g3 = g2 * g1
    sum_a = conv.sum(axis=(0, 1), keepdims=True)
    denom = 2.0 * g2 + sum_a * g3
    alpha = np.where(np.abs(denom) > 1e-10, g2 / (denom + 1e-10), 0.0)
    alpha = np.where(np.abs(g1) > 1e-10, alpha, 0.0)
    return (alpha * np.maximum(g1, 0.0)).sum(axis=(0, 1))


def grad_spatial_cv(g1):
    """Mean over channels (weighted by channel |gradient| mass) of std/mean|g| over space."""
    flat = g1.reshape(-1, g1.shape[-1])                    # (HW, K)
    mass = np.abs(flat).mean(axis=0)
    cv = flat.std(axis=0) / (mass + 1e-12)
    w = mass / (mass.sum() + 1e-12)
    return float((cv * w).sum())


# ------------------------------------------------------------- faithfulness
def faithfulness(eng, x, cams, class_idx, steps=12, n_rand=3, seed=0):
    """Deletion / insertion AUC of softmax prob(class_idx).

    own   : order pixels by THIS class's CAM
    other : order pixels by each OTHER class's CAM (cross-class test)
    rand  : order by a SMOOTH random map at the CAM's own resolution (a fair
            baseline: scattered-pixel noise would be out-of-distribution and
            make any blobby map look good)

    specificity_gap > 0  => this class's own map hurts/helps this class more
    than other classes' maps do, i.e. it carries class-specific evidence.
    gap_vs_random  > 0   => the map beats a smooth random map.
    """
    rng = np.random.default_rng(seed)
    S = gs.IMG_SIZE
    n = S * S
    fracs = np.linspace(0.0, 1.0, steps + 1)
    base = np.zeros_like(x)                                # mid-grey in [-1,1] space
    trap = getattr(np, "trapezoid", None) or np.trapz

    def order_of(cam):
        heat = cv2.resize(cam, (S, S), interpolation=cv2.INTER_CUBIC)
        return np.argsort(-heat.ravel(), kind="stable")

    def curves(order):
        dels, inss = [], []
        for f in fracs:
            k = int(round(f * n))
            mask = np.zeros(n, np.float32)
            mask[order[:k]] = 1.0
            m = mask.reshape(1, S, S, 1)
            dels.append(x * (1 - m) + base * m)
            inss.append(base * (1 - m) + x * m)
        p = eng.model(np.concatenate(dels + inss, 0), training=False).numpy()[:, class_idx]
        return float(trap(p[: len(fracs)], fracs)), float(trap(p[len(fracs):], fracs))

    own_d, own_i = curves(order_of(cams[class_idx]))
    od, oi = [], []
    for j, c in enumerate(cams):
        if j != class_idx:
            a, b = curves(order_of(c))
            od.append(a)
            oi.append(b)
    shape = cams[class_idx].shape
    rd, ri = [], []
    for _ in range(n_rand):
        a, b = curves(order_of(rng.random(shape).astype(np.float32)))
        rd.append(a)
        ri.append(b)
    return {
        "own_del_auc": own_d, "own_ins_auc": own_i,
        "other_del_auc": float(np.mean(od)), "other_ins_auc": float(np.mean(oi)),
        "rand_del_auc": float(np.mean(rd)), "rand_ins_auc": float(np.mean(ri)),
        "specificity_gap": float((np.mean(od) - own_d) + (own_i - np.mean(oi))),
        "gap_vs_random": float((np.mean(rd) - own_d) + (own_i - np.mean(ri))),
    }


# ----------------------------------------------------------------- rendering
def tint(img_bgr, heat01, bgr, alpha=0.6, gamma=1.25):
    a = (alpha * np.power(np.clip(heat01, 0, 1), gamma))[..., None].astype(np.float32)
    col = np.zeros_like(img_bgr, np.float32)
    col[:] = bgr
    return np.clip(img_bgr.astype(np.float32) * (1 - a) + col * a, 0, 255).astype(np.uint8)


def panel(img_bgr, cams, probs, layer, path, tile=256):
    img = cv2.resize(img_bgr, (tile, tile))
    gmax = max(float(c.max()) for c in cams) + 1e-12
    row1, row2 = [img.copy()], [img.copy()]
    for k, name in enumerate(CLASSES):
        h_norm = cv2.resize(gs.postprocess_heatmap(cams[k], tile, tile), (tile, tile))
        h_raw = np.clip(cv2.resize(cams[k], (tile, tile), interpolation=cv2.INTER_CUBIC) / gmax, 0, 1)
        t1, t2 = tint(img, h_norm, BGR[name]), tint(img, h_raw, BGR[name])
        for t, txt in ((t1, f"{name} {probs[k]*100:.1f}%"), (t2, f"{name} raw-scale")):
            cv2.putText(t, txt, (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (0, 0, 0), 3, cv2.LINE_AA)
            cv2.putText(t, txt, (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (255, 255, 255), 1, cv2.LINE_AA)
        row1.append(t1)
        row2.append(t2)
    cv2.putText(row1[0], f"{layer} | row1: per-class normalised", (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
    cv2.putText(row2[0], "row2: common raw scale", (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
    cv2.imwrite(path, np.vstack([np.hstack(row1), np.hstack(row2)]))


# ---------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default=gs.DEFAULT_MODEL_PATH)
    ap.add_argument("--images", nargs="+", required=True)
    ap.add_argument("--layers", nargs="+", default=DEFAULT_LAYERS)
    ap.add_argument("--saved-heatmaps", nargs="*", default=None,
                    help="capture folder(s) (same order as --images) containing heatmap_<Class>.png")
    ap.add_argument("--out", default="diag_out")
    ap.add_argument("--no-faith", action="store_true")
    ap.add_argument("--contrast", action="store_true", help="also analyse logit_c - mean(other logits) targets (exploratory)")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    import tensorflow as tf
    tf.keras.mixed_precision.set_global_policy("float32")
    model = tf.keras.models.load_model(args.model, compile=False)

    results = []
    for img_i, path in enumerate(args.images):
        img_bgr = gs.decode_image(open(path, "rb").read())
        x = gs.preprocess(img_bgr)
        stem = os.path.splitext(os.path.basename(os.path.dirname(path) or path))[0] or "img"
        for layer in args.layers:
            t0 = time.perf_counter()
            eng = gs.GradCAMPlusPlus(model, layer)
            conv, logits, probs, grads = class_gradients(eng, x)
            cams, notes = raw_cams(conv, grads)
            H, W, K = conv.shape

            r_mean, r_min, r_max = pairs_stat(cams, pearson)
            c_mean, _, _ = pairs_stat(cams, cosine)
            weights = [grad_weights(conv, g) for g in grads]
            w_cos_mean, _, _ = pairs_stat(weights, cosine)
            mean_cam = np.mean(cams, axis=0)
            shared = [max(0.0, pearson(c, mean_cam)) ** 2 for c in cams]
            rec = {
                "image": path, "layer": layer, "feature_map": [H, W, K],
                "logits": [float(v) for v in logits], "probs": [float(v) for v in probs],
                "predicted": CLASSES[int(np.argmax(probs))],
                "raw_cam_pearson_mean": r_mean, "raw_cam_pearson_min": r_min, "raw_cam_pearson_max": r_max,
                "raw_cam_cosine_mean": c_mean,
                "grad_spatial_cv": float(np.mean([grad_spatial_cv(g) for g in grads])),
                "weight_cosine_mean": w_cos_mean,
                "shared_component_r2": dict(zip(CLASSES, map(float, shared))),
                "raw_peaks": dict(zip(CLASSES, [float(c.max()) for c in cams])),
                "notes": notes,
            }
            if args.contrast:
                cconv, _, _, cgr = class_gradients(eng, x, contrast=True)
                ccams, _ = raw_cams(cconv, cgr)
                rec["contrast_cam_pearson_mean"] = pairs_stat(ccams, pearson)[0]

            if not args.no_faith:
                rec["faithfulness"] = {n: faithfulness(eng, x, cams, k) for k, n in enumerate(CLASSES)}
                pk = int(np.argmax(probs))
                rec["faith_spec_pred"] = rec["faithfulness"][CLASSES[pk]]["specificity_gap"]
                rec["faith_rand_pred"] = rec["faithfulness"][CLASSES[pk]]["gap_vs_random"]
                rec["faith_spec_all"] = float(np.mean([v["specificity_gap"] for v in rec["faithfulness"].values()]))

            if args.saved_heatmaps and img_i < len(args.saved_heatmaps) and layer == "Conv_1":
                folder = args.saved_heatmaps[img_i]
                agree = {}
                for k, n in enumerate(CLASSES):
                    sp = os.path.join(folder, f"heatmap_{n}.png")
                    if os.path.exists(sp):
                        saved = cv2.imread(sp, cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255.0
                        mine = gs.postprocess_heatmap(cams[k], saved.shape[1], saved.shape[0])
                        agree[n] = pearson(mine, saved)
                rec["agreement_with_saved_service_maps_pearson"] = agree

            for k, n in enumerate(CLASSES):
                np.save(os.path.join(args.out, f"{stem}__{layer}__{n}.npy"), cams[k])
            panel(img_bgr, cams, probs, layer, os.path.join(args.out, f"{stem}__{layer}.png"))
            rec["seconds"] = round(time.perf_counter() - t0, 1)
            results.append(rec)
            print(f"[{stem}] {layer:22s} {H:>2}x{W:<2}x{K:<4} rawPearson mean {r_mean:.3f} (min {r_min:.3f}) "
                  f"gradCV {rec['grad_spatial_cv']:.3f}  wCos {w_cos_mean:.3f}  "
                  + (f"spec(pred) {rec.get('faith_spec_pred', float('nan')):+.3f} vsRand(pred) {rec.get('faith_rand_pred', float('nan')):+.3f}" if not args.no_faith else ""),
                  flush=True)

    with open(os.path.join(args.out, "diagnostics.json"), "w") as fh:
        json.dump(results, fh, indent=1)

    print("\n=== SUMMARY by layer (mean over images) ===")
    print(f"{'layer':24s}{'map':>10s}{'rawPearson':>12s}{'gradCV':>9s}{'wCos':>8s}{'shared r2':>11s}{'spec(pred)':>11s}{'spec(all4)':>11s}{'vsRand':>8s}")
    for layer in args.layers:
        rs = [r for r in results if r["layer"] == layer]
        f = lambda key: float(np.nanmean([r[key] for r in rs]))
        sh = float(np.mean([np.mean(list(r["shared_component_r2"].values())) for r in rs]))
        sp = float(np.nanmean([r.get("faith_spec_pred", np.nan) for r in rs]))
        sa = float(np.nanmean([r.get("faith_spec_all", np.nan) for r in rs]))
        vr = float(np.nanmean([r.get("faith_rand_pred", np.nan) for r in rs]))
        m = rs[0]["feature_map"]
        print(f"{layer:24s}{str(m[0])+'x'+str(m[1]):>10s}{f('raw_cam_pearson_mean'):>12.3f}{f('grad_spatial_cv'):>9.3f}"
              f"{f('weight_cosine_mean'):>8.3f}{sh:>11.3f}{sp:>+11.3f}{sa:>+11.3f}{vr:>+8.3f}")
    print(f"\nArtifacts in: {os.path.abspath(args.out)}")


if __name__ == "__main__":
    main()
