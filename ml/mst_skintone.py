#!/usr/bin/env python
"""
mst_skintone.py - the SEPARATE Monk Skin Tone (MST) classifier for PrecisionSkin (research only).

It is independent of the MobileNetV2 condition classifier and the lesion detector: its predictions never change a condition result.
Training data: MST-E (reference examples, few people) + SCIN (supplementary, tone labels are layperson ESTIMATES), after filtering.

Why it is built the way it is (lessons from the retired Fitzpatrick model, where the "labels" were folders that encoded ethnicity):
  * every split keeps ALL photos of one person / case together (group split) - otherwise the model memorises people;
  * the labels come from raters (MST-E: expert-approved; SCIN: layperson estimates), never from folder names of another dataset;
  * no hue / saturation augmentation (it would change the very thing being labelled);
  * the result is reported as exact shade, within 1 shade (raters disagree by about one step), and 3 broad bands, with a range from resampling people;
  * a brightness stress test shows how much the answer depends on lighting.

Functions: audit_scin / audit_mste (dataset audit), build_manifest, group_split, train_model, evaluate.
"""
import os, re, json, glob, hashlib, random
from pathlib import Path
import numpy as np
import pandas as pd

IMG_EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
BANDS = {1: "light", 2: "light", 3: "light", 4: "medium", 5: "medium", 6: "medium", 7: "dark", 8: "dark", 9: "dark", 10: "dark"}   # 1-3 / 4-6 / 7-10
IMG = 224


# ------------------------------------------------------------------------------------------------ dataset reading
def _find(folder, patterns):
    for pat in patterns:
        hit = sorted(glob.glob(os.path.join(folder, "**", pat), recursive=True))
        if hit:
            return hit[0]
    return None


def _truthy(v):
    return str(v).strip().lower() in ("yes", "true", "1", "1.0", "y")


def load_scin_table(scin_dir):
    """cases + labels joined on case_id. Column names are detected by substring (the official names are monk_skin_tone_label_us /
    monk_skin_tone_label_india, body_parts_head_or_neck, image_1_path ...) - check the audit output against your download."""
    cases = _find(scin_dir, ["scin_cases.csv", "*cases*.csv"]); labels = _find(scin_dir, ["scin_labels.csv", "*labels*.csv"])
    if not cases:
        raise FileNotFoundError("scin_cases.csv not found under " + str(scin_dir))
    df = pd.read_csv(cases, dtype=str)
    if labels:
        lab = pd.read_csv(labels, dtype=str)
        df = df.merge(lab, on="case_id", how="left", suffixes=("", "_lab"))
    return df


def scin_columns(df):
    cols = list(df.columns)
    monk = [c for c in cols if "monk" in c.lower()]
    us = [c for c in monk if "us" in c.lower().split("_")] or monk
    body = [c for c in cols if "head" in c.lower() and "neck" in c.lower()] or [c for c in cols if "face" in c.lower()]
    imgs = [c for c in cols if re.fullmatch(r"image_?\d+_?path", c.lower())]
    return dict(monk_all=monk, monk_primary=(us[0] if us else None), head_neck=(body[0] if body else None), images=imgs)


def audit_scin(scin_dir):
    df = load_scin_table(scin_dir); c = scin_columns(df)
    print("SCIN cases:", len(df), "| columns detected:", {k: v for k, v in c.items()})
    if c["monk_primary"]:
        m = pd.to_numeric(df[c["monk_primary"]], errors="coerce")
        print("cases with a Monk label (%s): %d (%.0f%%)" % (c["monk_primary"], m.notna().sum(), 100 * m.notna().mean()))
        print("MST distribution:", m.dropna().round().astype(int).value_counts().sort_index().to_dict())
    else:
        print("WARNING: no Monk column found; columns:", list(df.columns)[:40])
    if c["head_neck"]:
        print("cases with head/neck flagged (%s): %d" % (c["head_neck"], df[c["head_neck"]].map(_truthy).sum()))
    n_img = sum(df[col].notna().sum() for col in c["images"]); print("image paths listed:", n_img)
    if c["images"]:
        paths = pd.concat([df[col].dropna() for col in c["images"]])
        print("duplicate image paths listed more than once:", int(paths.duplicated().sum()))
    return df, c


def mste_label(path):
    s = str(path).replace("\\", "/")
    m = re.search(r"(?:mst|monk)[ _\-]?(\d{1,2})(?!\d)", s, re.I)
    if m and 1 <= int(m.group(1)) <= 10:
        return int(m.group(1))
    for part in reversed(Path(s).parts[:-1]):                       # a folder named 1..10
        if part.isdigit() and 1 <= int(part) <= 10:
            return int(part)
    return None


def mste_subject(path):
    s = Path(path).stem
    m = re.search(r"(?:subject|model|person|subj|s)[ _\-]?(\d+)", s, re.I)
    return ("mste_" + m.group(1)) if m else "mste_" + re.split(r"[_\-]", s)[0]


def audit_mste(mste_dir):
    imgs = [p for p in Path(mste_dir).rglob("*") if p.suffix.lower() in IMG_EXT]
    lab = [mste_label(p) for p in imgs]; sub = [mste_subject(p) for p in imgs]
    print("MST-E images:", len(imgs), "| label found for %d | subjects (from file names): %d" % (sum(x is not None for x in lab), len(set(sub))))
    print("MST distribution:", pd.Series([x for x in lab if x]).value_counts().sort_index().to_dict())
    if imgs and sum(x is None for x in lab) > 0:
        print("WARNING: no MST number could be read from these names, e.g.:", [str(p.relative_to(mste_dir)) for p, x in zip(imgs, lab) if x is None][:5])
    return imgs


def build_manifest(scin_dir=None, mste_dir=None, face_only=True, use_scin=True, max_per_case=1):
    """-> DataFrame(path, mst, group, source). SCIN: one image per case by default (several photos of one case are near duplicates)."""
    rows = []
    if mste_dir and Path(mste_dir).exists():
        for p in Path(mste_dir).rglob("*"):
            if p.suffix.lower() in IMG_EXT and mste_label(p):
                rows.append(dict(path=str(p), mst=mste_label(p), group=mste_subject(p), source="MST-E"))
    if use_scin and scin_dir and Path(scin_dir).exists():
        df = load_scin_table(scin_dir); c = scin_columns(df)
        if not c["monk_primary"]:
            raise RuntimeError("No Monk skin-tone column in the SCIN table: " + str(list(df.columns)[:30]))
        m = pd.to_numeric(df[c["monk_primary"]], errors="coerce")
        keep = m.notna()
        if face_only and c["head_neck"]:
            keep &= df[c["head_neck"]].map(_truthy)
        for i in df[keep].index:
            taken = 0
            for col in c["images"]:
                rel = df.at[i, col]
                if not isinstance(rel, str):
                    continue
                p = Path(scin_dir) / rel
                if not p.exists():
                    hit = list(Path(scin_dir).rglob(Path(rel).name))
                    p = hit[0] if hit else None
                if p is None:
                    continue
                rows.append(dict(path=str(p), mst=int(round(float(m[i]))), group="scin_" + str(df.at[i, "case_id"]), source="SCIN")); taken += 1
                if taken >= max_per_case:
                    break
    out = pd.DataFrame(rows)
    if len(out):
        out = out[(out.mst >= 1) & (out.mst <= 10)].reset_index(drop=True); out["band"] = out.mst.map(BANDS)
    return out


# ------------------------------------------------------------------------------------------------ split (people / cases never shared)
def group_split(df, seed=42, fractions=(0.70, 0.15, 0.15)):
    """returns df with a 'split' column; every group (person / SCIN case) lands in exactly ONE split; stratified by the group's majority band."""
    rng = random.Random(seed); g = df.groupby("group")
    band = g.band.agg(lambda s: s.value_counts().index[0]); size = g.size()
    split = {}
    for b in sorted(band.unique()):
        ids = sorted(band[band == b].index); rng.shuffle(ids)
        n = len(ids); n_tr = int(round(fractions[0] * n)); n_va = int(round(fractions[1] * n))
        if n >= 3:
            n_tr = min(n_tr, n - 2); n_va = max(1, min(n_va, n - n_tr - 1))
        for k, gid in enumerate(ids):
            split[gid] = "train" if k < n_tr else ("val" if k < n_tr + n_va else "test")
    out = df.copy(); out["split"] = out.group.map(split)
    sets = {s: set(out[out.split == s].group) for s in ("train", "val", "test")}
    assert not (sets["train"] & sets["val"] or sets["train"] & sets["test"] or sets["val"] & sets["test"]), "a person / case is in two splits"
    return out


# ------------------------------------------------------------------------------------------------ model
def _load(path, size=IMG):
    import tensorflow as tf
    img = tf.io.decode_image(tf.io.read_file(path), channels=3, expand_animations=False)
    return tf.image.resize(img, (size, size))


def make_ds(df, train, batch=32, brightness=1.0):
    import tensorflow as tf
    paths = df.path.values; y = (df.mst.values - 1).astype("int32")
    ds = tf.data.Dataset.from_tensor_slices((paths, y))
    if train:
        ds = ds.shuffle(len(df), seed=1)
    def prep(p, lab):
        x = _load(p)
        if train:                                                   # NO hue / saturation changes: they would alter the labelled quantity
            x = tf.image.random_flip_left_right(x)
            x = tf.image.random_brightness(x, 25.0)
            x = tf.image.random_contrast(x, 0.85, 1.15)
        if brightness != 1.0:
            x = x * brightness
        x = tf.clip_by_value(x, 0.0, 255.0)
        return tf.keras.applications.mobilenet_v2.preprocess_input(x), lab
    return ds.map(prep, num_parallel_calls=tf.data.AUTOTUNE).batch(batch).prefetch(tf.data.AUTOTUNE)


def train_model(df, out_dir, epochs_head=6, epochs_ft=14, batch=32, seed=42, lr=1e-3):
    import tensorflow as tf
    tf.keras.utils.set_random_seed(seed)
    tr, va = df[df.split == "train"], df[df.split == "val"]
    base = tf.keras.applications.MobileNetV2(input_shape=(IMG, IMG, 3), include_top=False, weights="imagenet")
    base.trainable = False
    inp = tf.keras.Input((IMG, IMG, 3)); x = base(inp, training=False)
    x = tf.keras.layers.GlobalAveragePooling2D()(x); x = tf.keras.layers.Dropout(0.3)(x)
    out = tf.keras.layers.Dense(10, activation="softmax")(x)
    model = tf.keras.Model(inp, out)
    counts = tr.mst.value_counts(); cw = {k - 1: float(len(tr) / (10 * counts.get(k, 1))) for k in range(1, 11)}
    cw = {k: min(v, 5.0) for k, v in cw.items()}                      # shades with no / few examples: cap the weight
    Path(out_dir).mkdir(parents=True, exist_ok=True)
    ck = tf.keras.callbacks.ModelCheckpoint(str(Path(out_dir) / "mst_best.keras"), monitor="val_loss", save_best_only=True)
    es = tf.keras.callbacks.EarlyStopping(monitor="val_loss", patience=5, restore_best_weights=True)
    dtr, dva = make_ds(tr, True, batch), make_ds(va, False, batch)
    model.compile(optimizer=tf.keras.optimizers.Adam(lr), loss=tf.keras.losses.SparseCategoricalCrossentropy(), metrics=["accuracy"])
    model.fit(dtr, validation_data=dva, epochs=epochs_head, class_weight=cw, callbacks=[ck, es], verbose=2)
    base.trainable = True
    for layer in base.layers[:-30]:
        layer.trainable = False
    model.compile(optimizer=tf.keras.optimizers.Adam(lr / 20), loss=tf.keras.losses.SparseCategoricalCrossentropy(), metrics=["accuracy"])
    model.fit(dtr, validation_data=dva, epochs=epochs_ft, class_weight=cw, callbacks=[ck, es], verbose=2)
    return model


def predict(model, df, brightness=1.0, batch=32):
    return model.predict(make_ds(df, False, batch, brightness), verbose=0)


def metrics(df, probs):
    t = df.mst.values; pred = probs.argmax(1) + 1; exp = (probs * np.arange(1, 11)).sum(1)
    tb = df.band.values; pb = np.array([BANDS[int(p)] for p in pred])
    return dict(n=len(df), exact=float((pred == t).mean()), within1=float((np.abs(pred - t) <= 1).mean()), band=float((pb == tb).mean()), mae_expected=float(np.abs(exp - t).mean()))


def evaluate(model, df_test, boot=1000, seed=0):
    probs = predict(model, df_test); m = metrics(df_test, probs)
    # range from resampling PEOPLE / CASES (photos of one person are not independent)
    groups = df_test.group.unique(); rng = np.random.default_rng(seed); idx_by = {g: np.where(df_test.group.values == g)[0] for g in groups}
    w1, bd = [], []
    for _ in range(boot):
        pick = np.concatenate([idx_by[g] for g in rng.choice(groups, len(groups))])
        mm = metrics(df_test.iloc[pick], probs[pick]); w1.append(mm["within1"]); bd.append(mm["band"])
    m["within1_95"] = (float(np.percentile(w1, 2.5)), float(np.percentile(w1, 97.5))); m["band_95"] = (float(np.percentile(bd, 2.5)), float(np.percentile(bd, 97.5)))
    m["by_source"] = {s: metrics(df_test[df_test.source == s], probs[(df_test.source == s).values]) for s in df_test.source.unique()}
    m["brightness_stress"] = {str(b): metrics(df_test, predict(model, df_test, b)) for b in (0.75, 1.25)}
    conf = np.zeros((10, 10), int)
    for t, p in zip(df_test.mst.values, probs.argmax(1) + 1):
        conf[t - 1, p - 1] += 1
    m["confusion"] = conf.tolist()
    return m


def verdict(m):
    lines = []
    lines.append("Exact shade %.0f%% | within 1 shade %.0f%% (95%% range %.0f-%.0f%%) | 3 bands %.0f%% | mean error %.2f shades (n=%d photos)" % (
        100 * m["exact"], 100 * m["within1"], 100 * m["within1_95"][0], 100 * m["within1_95"][1], 100 * m["band"], m["mae_expected"], m["n"]))
    drop = max(abs(v["within1"] - m["within1"]) for v in m["brightness_stress"].values())
    lines.append("Lighting stress (+/-25%% brightness): within-1 changes by up to %.0f points." % (100 * drop))
    ok = m["within1_95"][0] >= 0.80
    lines.append("RELIABLE enough to use for subgroup reporting: " + ("YES (lower end of the range is at least 80%)" if ok else
                 "NOT YET (lower end of the within-1 range is below 80%). Do not use its labels to judge the condition classifier; use rater-assigned labels."))
    return lines
