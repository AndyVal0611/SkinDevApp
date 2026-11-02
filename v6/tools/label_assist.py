"""Suggestion helper for tools/label_tool.py (TRAIN split only). Run with the training venv (needs tensorflow + scikit-learn):

  ~/PrecisionSkinV1/.venv/bin/python label_assist.py

It (1) embeds the train images once with the ImageNet MobileNetV2 (NOT the V5 model, so it stays independent of the baseline),
(2) fits one small logistic regression per condition on the train rows YOU have already labelled, and
(3) writes suggestions for the unlabelled train images to <annotations folder>/label_assist/suggestions_train.csv.
Suggestions are only a prompt: the label tool records any row you saw a suggestion for as blinded_to_ai = "no", so such rows can never be
used as evaluation labels. Validation, fusion and test images are never embedded or suggested.
"""
import argparse, csv, os, sys, time

os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
HOME = os.path.expanduser("~")
COND = ["acne", "hyperpigmentation", "eczema"]
ap = argparse.ArgumentParser()
ap.add_argument("--annotations", default=os.path.join(HOME, "PrecisionSkinV1/v6/annotations/multilabel_annotations.csv"))
ap.add_argument("--manifest", default=os.path.join(HOME, "PrecisionSkinV1/metadata/metadata/split_manifest.csv"))
ap.add_argument("--processed-root", default=os.path.join(HOME, "PrecisionSkinV1/dataset_processed/processed"))
ap.add_argument("--min-each", type=int, default=8, help="needs this many present AND absent labelled examples before a condition gets suggestions")
args = ap.parse_args()

import numpy as np
from PIL import Image

out_dir = os.path.join(os.path.dirname(args.annotations), "label_assist")
os.makedirs(out_dir, exist_ok=True)
emb_path = os.path.join(out_dir, "embeddings_train.npz")
sug_path = os.path.join(out_dir, "suggestions_train.csv")

with open(args.manifest, newline="", encoding="utf-8") as f:
    MAN = {r["image_id"]: (r["split"], r["label_class"], r["filename"]) for r in csv.DictReader(f)}
with open(args.annotations, newline="", encoding="utf-8") as f:
    ROWS = [r for r in csv.DictReader(f) if r["split"] == "train"]
ids = [r["image_id"] for r in ROWS if r["image_id"] in MAN]

# 1. embeddings (cached; only missing images are computed)
cache = {}
if os.path.exists(emb_path):
    z = np.load(emb_path, allow_pickle=True)
    cache = dict(zip(z["ids"].tolist(), z["emb"]))
todo = [i for i in ids if i not in cache]
if todo:
    import tensorflow as tf
    for g in tf.config.list_physical_devices("GPU"):
        tf.config.experimental.set_memory_growth(g, True)
    net = tf.keras.applications.MobileNetV2(include_top=False, weights=None, pooling="avg", input_shape=(224, 224, 3))
    net.load_weights(os.path.join(HOME, ".keras/models/mobilenet_v2_weights_tf_dim_ordering_tf_kernels_1.0_224_no_top.h5"))
    t0 = time.time()
    for b in range(0, len(todo), 64):
        chunk, imgs = todo[b:b + 64], []
        for i in chunk:
            s, c, fn = MAN[i]
            try:
                im = Image.open(os.path.join(args.processed_root, s, c, fn)).convert("RGB").resize((224, 224))
                imgs.append(np.asarray(im, np.float32) / 127.5 - 1.0)
            except Exception:
                imgs.append(np.zeros((224, 224, 3), np.float32))
        e = net.predict(np.stack(imgs), verbose=0)
        for i, v in zip(chunk, e):
            cache[i] = v.astype(np.float32)
        print(f"embedded {min(b + 64, len(todo))}/{len(todo)}  ({time.time() - t0:.0f}s)", flush=True)
    np.savez(emb_path, ids=np.array(list(cache)), emb=np.stack(list(cache.values())))

# 2. fit on what the researcher has labelled (train rows with a decided value for that condition)
from sklearn.linear_model import LogisticRegression
X_all = np.stack([cache[i] for i in ids])
lab = {r["image_id"]: r for r in ROWS if r["review_status"] in ("verified", "partial")}
unl = [k for k, i in enumerate(ids) if i not in lab]
P = {}
status = []
for c in COND:
    idx = [k for k, i in enumerate(ids) if i in lab and lab[i][c] in ("present", "absent")]
    y = np.array([1 if lab[ids[k]][c] == "present" else 0 for k in idx])
    npos, nneg = int(y.sum()), int((1 - y).sum())
    if npos < args.min_each or nneg < args.min_each:
        status.append(f"{c}: not enough labels yet (present {npos}, absent {nneg}; need {args.min_each} each) - no suggestions")
        continue
    clf = LogisticRegression(C=0.5, max_iter=2000, class_weight="balanced").fit(X_all[idx], y)
    P[c] = clf.predict_proba(X_all[unl])[:, 1]
    status.append(f"{c}: fitted on {npos} present / {nneg} absent")

with open(sug_path, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow(["image_id"] + ["p_" + c for c in COND])
    for n, k in enumerate(unl):
        w.writerow([ids[k]] + [f"{P[c][n]:.3f}" if c in P else "" for c in COND])
print("\n".join(status))
print(f"wrote {len(unl)} suggestions -> {sug_path}")
