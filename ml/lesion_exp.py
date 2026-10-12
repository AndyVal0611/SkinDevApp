#!/usr/bin/env python
"""
lesion_exp.py - controlled, reproducible YOLOv8 lesion-detector experiments (one factor changed per experiment).

  * every experiment is a SPEC (id, condition, data recipe, model, imgsz, seed, train-arg overrides, note); nothing else changes between experiments
  * originals are never touched: datasets are read through file lists, runs go to  <PROJECT>/lesion_experiments/runs/<id>/
  * VALIDATION photos only are used here (selection of model / confidence). The TEST sets are not opened by this file; see lesion_final_eval.py
  * every experiment is appended to  lesion_experiments/EXPERIMENTS.csv  (spec, dataset fingerprint, seed, epochs run, best epoch, val metrics, minutes)

    python lesion_exp.py list                 show the queue and what is done
    python lesion_exp.py run                  run every queued experiment that is not done (resumable, safe to stop and restart)
    python lesion_exp.py run --only H1_own    run one
"""
import argparse, csv, hashlib, json, os, random, shutil, sys, time, traceback
from pathlib import Path
import numpy as np

sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp")
import hyperpig_autolabel as H

P = Path.home() / "PrecisionSkinV1"
EXP = P / "lesion_experiments"; RUNS = EXP / "runs"; DATA = EXP / "data"; REG = EXP / "EXPERIMENTS.csv"
for d in (RUNS, DATA): d.mkdir(parents=True, exist_ok=True)
ACNE = P / "acne_detector_v4" / "dataset"                         # clean acne dataset v4 (WSL copy; the original stays in Downloads)
NEG_SRC = P / "acne_detector_v5" / "dataset" / "train" / "images"  # 115 healthy-skin negatives (labels absent = background)
HYP = P / "hyperpig_autolabel" / "hyperpig_only_v1" / "dataset"
EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}

BASE_ARGS = dict(imgsz=800, epochs=100, patience=30, batch=8, workers=2, cos_lr=True, close_mosaic=10,
                 fliplr=0.5, flipud=0.0, degrees=10, scale=0.5, hsv_h=0.015, hsv_s=0.5, hsv_v=0.35)       # = the acne baseline settings


def imgs(d): return sorted(p for p in Path(d).glob("*") if p.suffix.lower() in EXT)


# ------------------------------------------------------------------------------------------------ hyperpigmentation: a hand-boxed VALIDATION set
def prep_hyp():
    """Splits YOUR hand-boxed photos (the ones in the training folder, not the 100 test photos) into train / hand_val, and finds near-duplicate groups.
    Done once; the 100 hand-boxed TEST photos are not read here."""
    f = DATA / "hyp_split.json"
    if f.exists(): return json.loads(f.read_text())
    tr = imgs(HYP / "train" / "images")
    own = [p for p in tr if p.name.startswith("hand_") and "_rep" not in p.name]; pub = [p for p in tr if not p.name.startswith("hand_")]
    rng = random.Random(0); rng.shuffle(own); cand = own[:70]; own_train = own[70:]
    # honest validation: a validation photo must not have a look-alike (corr >= 0.90, 8 rotations/flips) among the remaining training photos
    rest = pub + own_train; T = {p.name: H.thumb(p) for p in rest + cand}
    rest_n = [p.name for p in rest if T[p.name] is not None]; A = np.stack([T[n] for n in rest_n])
    keep, dropped = [], []
    for p in cand:
        if T[p.name] is None: continue
        b = H.best_corr(np.stack([T[p.name]]), A)[0]
        (dropped if b.max() >= 0.90 else keep).append(p.name)
    # near-duplicate groups among the PUBLIC training photos (corr >= 0.95): keep one photo per group in the "dedup" recipe
    pn = [p.name for p in pub if T.get(p.name) is not None]; PA = np.stack([T[n] for n in pn]); c = H.best_corr(PA, PA); np.fill_diagonal(c, -1)
    seen, keep_pub = set(), []
    for i, n in enumerate(pn):
        if i in seen: continue
        keep_pub.append(n); seen.update(np.where(c[i] >= 0.95)[0].tolist())
    out = dict(hand_val=keep, hand_val_dropped_lookalike=dropped, own_train=[p.name for p in own_train], public_all=[p.name for p in pub], public_dedup=keep_pub)
    (DATA / "hyp_hand_val" / "images").mkdir(parents=True, exist_ok=True); (DATA / "hyp_hand_val" / "labels").mkdir(parents=True, exist_ok=True)
    for n in keep:
        shutil.copy2(HYP / "train" / "images" / n, DATA / "hyp_hand_val" / "images" / n)
        shutil.copy2(HYP / "train" / "labels" / (Path(n).stem + ".txt"), DATA / "hyp_hand_val" / "labels" / (Path(n).stem + ".txt"))
    f.write_text(json.dumps(out, indent=1))
    print("hyperpigmentation split: hand_val %d (dropped %d with a look-alike) | own train %d | public %d -> %d after near-duplicate removal" % (len(keep), len(dropped), len(own_train), len(pub), len(keep_pub)), flush=True)
    return out


def hyp_files(names): return [HYP / "train" / "images" / n for n in names]


def own_with_reps(own_names, reps):
    out = []
    for n in own_names:
        out.append(HYP / "train" / "images" / n)
        stem, ext = Path(n).stem, Path(n).suffix
        for r in range(1, reps):
            q = HYP / "train" / "images" / ("%s_rep%d%s" % (stem, r, ext))
            if q.exists(): out.append(q)
    return out


def recipe(name):
    """-> (train image list, {validation name: dir}, note)"""
    if name == "acne_base":
        return imgs(ACNE / "train" / "images"), {"val": ACNE / "val"}, "acne dataset v4, boxed photos only"
    if name == "acne_fixed":        # v4 training photos, with the 60 "second look" photos relabelled by you (your boxes + the missed spots you confirmed). Val labels untouched.
        d = DATA / "acne_fixed" / "train"
        if not (d / "READY").exists():
            shutil.rmtree(d, ignore_errors=True); (d / "images").mkdir(parents=True); (d / "labels").mkdir(parents=True)
            for p in imgs(ACNE / "train" / "images"):
                os.symlink(p, d / "images" / p.name); shutil.copy2(ACNE / "train" / "labels" / (p.stem + ".txt"), d / "labels" / (p.stem + ".txt"))
            fix = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing/Fix_Acne_Missed_Spots/Elaiza__acne_fix/labels"); ref = json.load(open(fix.parent.parent / "_drafts" / "meta.json"))["drafted_at"]
            n = 0
            for f in fix.glob("*.txt"):
                if f.stat().st_mtime > ref + 1 and (d / "labels" / f.name).exists(): shutil.copy2(f, d / "labels" / f.name); n += 1
            (d / "READY").write_text(str(n)); print("acne_fixed: %d labels replaced" % n, flush=True)
        return imgs(d / "images"), {"val": ACNE / "val"}, "acne v4 train with %s relabelled photos (second look)" % (d / "READY").read_text()
    if name == "acne_neg":
        return imgs(ACNE / "train" / "images") + sorted(NEG_SRC.glob("neg_*")), {"val": ACNE / "val"}, "acne v4 + 115 healthy-skin negatives (no boxes)"
    s = prep_hyp(); vals = {"hand_val": DATA / "hyp_hand_val", "public_val": HYP / "val"}
    if name == "hyp_mix":        return hyp_files(s["public_all"]) + own_with_reps(s["own_train"], 2), vals, "public boxes + your boxes x2 (the original recipe, minus the hand_val photos)"
    if name == "hyp_own":        return own_with_reps(s["own_train"], 2), vals, "only your boxes (x2)"
    if name in ("hyp_own_drafts", "hyp_own_drafts_cov", "hyp_own_drafts_v2"):
        # the ~290 helper drafts you reviewed and saved (copied to the WSL disk once); "cov" keeps only the photos in the style of your hand-boxed test photos
        d = DATA / ("hyp_drafts_v2" if name.endswith("_v2") else "hyp_drafts")
        if not (d / "READY").exists():
            B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Draft_Hyperpig"); S = B / "Elaiza__hyperpigmentation_draft"; ref = json.load(open(B / "_drafts/meta.json"))["drafted_at"]
            shutil.rmtree(d, ignore_errors=True); (d / "images").mkdir(parents=True); (d / "labels").mkdir(parents=True); info = {}
            for f in S.joinpath("labels").glob("*.txt"):
                bx = [tuple(map(float, l.split()[1:5])) for l in f.read_text().splitlines() if len(l.split()) >= 5]
                if f.stat().st_mtime <= ref + 1 or not bx: continue
                img = next(iter((S / "images").glob(f.stem + ".*")), None)
                if img is None: continue
                m = np.zeros((96, 96), np.uint8)
                for cx, cy, w, h in bx: m[int(max(0, cy - h / 2) * 96):int(min(1, cy + h / 2) * 96) + 1, int(max(0, cx - w / 2) * 96):int(min(1, cx + w / 2) * 96) + 1] = 1
                shutil.copy2(img, d / "images" / img.name); shutil.copy2(f, d / "labels" / f.name)
                info[img.name] = dict(cover=float(m.mean()), max_side=max((w * h) ** 0.5 for _, _, w, h in bx), n=len(bx))
            (d / "info.json").write_text(json.dumps(info)); (d / "READY").write_text(str(len(info))); print("hyp_drafts: %d reviewed drafts copied" % len(info), flush=True)
        info = json.loads((d / "info.json").read_text())
        names = [n for n, v in info.items() if name != "hyp_own_drafts_cov" or (v["cover"] <= 0.18 and v["max_side"] <= 0.40)]
        return own_with_reps(s["own_train"], 2) + [d / "images" / n for n in sorted(names)], vals, "your boxes x2 + %d of your %d reviewed drafts%s" % (len(names), len(info), "" if name != "hyp_own_drafts_cov" else " (only photos covering <= 18 % and no box > 40 %)")
    if name == "hyp_v2auto":
        # NO new human boxing: old boxes converted to rule v2 by code (touching / closer than 1.0 x the smaller box = one box; boxes under 0.09 dropped);
        # validation = the 30 validation photos you boxed under rule v2 (pass A). Scored later on pass B as well.
        d = DATA / "hyp_v2auto"
        if not (d / "READY").exists():
            import hyp_v2_transform as TR
            shutil.rmtree(d, ignore_errors=True); (d / "images").mkdir(parents=True); (d / "labels").mkdir(parents=True); k = 0
            def put(img, boxes, reps):
                nonlocal k
                for r in range(reps):
                    nm = "%s%s%s" % (img.stem, "" if r == 0 else "_rep%d" % r, img.suffix); shutil.copy2(img, d / "images" / nm)
                    (d / "labels" / (Path(nm).stem + ".txt")).write_text("".join("0 %.6f %.6f %.6f %.6f\n" % b for b in boxes)); k += 1
            for n in s["own_train"]:
                lab = HYP / "train" / "labels" / (Path(n).stem + ".txt"); bx = TR.F.rd(lab)
                if bx: put(HYP / "train" / "images" / n, TR.to_v2(bx, 1.0, 0.09), 2)
            dd = DATA / "hyp_drafts_v2"
            for img in sorted((dd / "images").glob("*")):
                bx = TR.F.rd(dd / "labels" / (img.stem + ".txt"))
                if bx: put(img, TR.to_v2(bx, 1.0, 0.09), 1)
            vd = DATA / "hyp_v2_valA"; shutil.rmtree(vd, ignore_errors=True); (vd / "images").mkdir(parents=True); (vd / "labels").mkdir(parents=True)
            BA = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Rebox_V2_PassA"); mp = json.load(open(BA / "name_map.json"))
            for orig, new in mp.items():
                src = next(iter((DATA / "hyp_hand_val" / "images").glob(Path(orig).stem + ".*")))
                shutil.copy2(src, vd / "images" / src.name); shutil.copy2(BA / "Elaiza__hyperpigmentation_v2" / "labels" / (new + ".txt"), vd / "labels" / (Path(orig).stem + ".txt"))
            (d / "READY").write_text(str(k)); print("hyp_v2auto: %d training images (converted boxes), validation %d photos (pass A)" % (k, len(mp)), flush=True)
        return imgs(d / "images"), {"v2_valA": DATA / "hyp_v2_valA"}, "old boxes converted to rule v2 by code (no new human boxing); validation = your pass A"
    if name == "hyp_dedup_mix":  return hyp_files(s["public_dedup"]) + own_with_reps(s["own_train"], 2), vals, "public photos de-duplicated + your boxes x2"
    if name == "hyp_balanced":
        rng = random.Random(1); pub = s["public_dedup"][:]; rng.shuffle(pub)
        return hyp_files(pub[:len(s["own_train"])]) + own_with_reps(s["own_train"], 2), vals, "public (same number of photos as yours) + your boxes x2"
    raise ValueError(name)


# ------------------------------------------------------------------------------------------------ the experiment queue (priority order = diagnosis findings)
def spec(id, condition, recipe_name, note, seed=42, model="yolov8s.pt", **over):
    a = dict(BASE_ARGS); a["model"] = model; a.update(over)
    return dict(id=id, condition=condition, recipe=recipe_name, seed=seed, args=a, note=note)

QUEUE = [
    # hyperpigmentation: the diagnosis says public boxes are ~2x larger than yours and dominate training, the validation set is in the public style, and the m/n baselines never finished training
    spec("H0_mix", "hyperpigmentation", "hyp_mix", "baseline of the new protocol (same recipe as before, hand-boxed validation)"),
    spec("H1_own", "hyperpigmentation", "hyp_own", "FACTOR: training data = only your boxes"),
    spec("H6_own_drafts", "hyperpigmentation", "hyp_own_drafts", "FACTOR: your boxes + all reviewed drafts (two box styles mixed)"),
    spec("H7_own_drafts_cov", "hyperpigmentation", "hyp_own_drafts_cov", "FACTOR: your boxes + only the reviewed drafts in the test-photo style (coverage <= 18 %, no box > 40 %)"),
    # round 2: ~230 more drafts reviewed (518 saved in total); the next three change ONE thing each against H8
    spec("H8_drafts_v2", "hyperpigmentation", "hyp_own_drafts_v2", "FACTOR: your boxes + all 500+ reviewed drafts (round 2)"),
    spec("H9_v2_960", "hyperpigmentation", "hyp_own_drafts_v2", "FACTOR (vs H8): input 960 px", imgsz=960, batch=6),
    spec("H10_v2_lowlr", "hyperpigmentation", "hyp_own_drafts_v2", "FACTOR (vs H8): lr0 0.0005 AdamW", lr0=0.0005, optimizer="AdamW"),
    spec("H10_s7", "hyperpigmentation", "hyp_own_drafts_v2", "CONFIRM H10 with seed 7", seed=7, lr0=0.0005, optimizer="AdamW"),
    spec("H10_s123", "hyperpigmentation", "hyp_own_drafts_v2", "CONFIRM H10 with seed 123", seed=123, lr0=0.0005, optimizer="AdamW"),
    # round 3: better starting weights (same data and recipe as H10, seed 42)
    spec("H12_init_acne", "hyperpigmentation", "hyp_own_drafts_v2", "FACTOR (vs H10): start from the final acne detector's weights (same skin domain) instead of COCO", model="/home/bautista/PrecisionSkinV1/runs/lesion_acne_boxed_only/models/seed_123/weights/best.pt", lr0=0.0005, optimizer="AdamW"),
    spec("H13_init_mix", "hyperpigmentation", "hyp_own_drafts_v2", "FACTOR (vs H10): start from the public+own model (H0), then fine-tune on your boxes only", model="/home/bautista/PrecisionSkinV1/lesion_experiments/runs/H0_mix/weights/best.pt", lr0=0.0005, optimizer="AdamW"),
    spec("H20_v2auto", "hyperpigmentation", "hyp_v2auto", "FACTOR: train on old boxes converted to rule v2 by code; validated on your rule-v2 pass A", lr0=0.0005, optimizer="AdamW"),
    spec("H20_s7", "hyperpigmentation", "hyp_v2auto", "CONFIRM H20 with seed 7", seed=7, lr0=0.0005, optimizer="AdamW"),
    spec("H20_s123", "hyperpigmentation", "hyp_v2auto", "CONFIRM H20 with seed 123", seed=123, lr0=0.0005, optimizer="AdamW"),
    spec("H11_v2_m800", "hyperpigmentation", "hyp_own_drafts_v2", "FACTOR (vs H8): yolov8m", model="yolov8m.pt", batch=6),
    spec("H2_dedup", "hyperpigmentation", "hyp_dedup_mix", "FACTOR: remove near-duplicate public photos (2,524 of 3,031 had one)"),
    spec("H3_balanced", "hyperpigmentation", "hyp_balanced", "FACTOR: public photos limited to the number of your photos"),
    # acne: v4 baseline = 3 seeds; each factor tested with seed 42 only (seed std 0.006), winners are confirmed with 3 seeds
    spec("A0_base", "acne", "acne_base", "baseline of the new protocol (= v4 seed 42)"),
    spec("A1_neg", "acne", "acne_neg", "FACTOR: + 115 healthy-skin negatives"),
    spec("A2_colorsafe", "acne", "acne_base", "FACTOR: milder colour jitter (redness is the signal): hsv_h 0.005, hsv_s 0.3, hsv_v 0.25", hsv_h=0.005, hsv_s=0.3, hsv_v=0.25),
    spec("A3_box10", "acne", "acne_base", "FACTOR: box-loss gain 10 (tighter localisation, helps mAP50-95)", box=10.0),
    spec("A4_lowlr", "acne", "acne_base", "FACTOR: lr0 0.0005 (val loss rises after the best epoch: overfitting)", lr0=0.0005, optimizer="AdamW"),
    spec("A5_960", "acne", "acne_base", "FACTOR: input 960 px (small spots)", imgsz=960, batch=6),
    spec("A6_m800", "acne", "acne_base", "FACTOR: yolov8m instead of yolov8s", model="yolov8m.pt", batch=6),
    spec("A7_fixlabels", "acne", "acne_fixed", "FACTOR: 60 training photos relabelled (893 -> 1,430 boxes: missed spots added by you); validation labels unchanged"),
    # hyperpigmentation model size, fully trained this time (the old n/m runs stopped after 7 and 9 epochs)
    spec("H4_n640", "hyperpigmentation", "hyp_mix", "FACTOR: yolov8n 640 (fully trained)", model="yolov8n.pt", imgsz=640, batch=16),
    spec("H5_m800", "hyperpigmentation", "hyp_mix", "FACTOR: yolov8m 800 (fully trained)", model="yolov8m.pt", batch=6),
]


# ------------------------------------------------------------------------------------------------ run
COLS = ["id", "condition", "recipe", "seed", "model", "imgsz", "overrides", "note", "train_photos", "dataset_fingerprint", "epochs_run", "best_epoch", "train_minutes",
        "val_set", "val_mAP50", "val_mAP50_95", "val_conf", "val_P", "val_R", "val_F1", "status"]


def registry():
    if not REG.exists(): return []
    return list(csv.DictReader(open(REG, encoding="utf-8")))


def log_rows(rows):
    new = not REG.exists()
    with open(REG, "a", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=COLS); (w.writeheader() if new else None); [w.writerow(r) for r in rows]


def run_one(sp):
    from ultralytics import YOLO
    import torch
    eid = sp["id"]; run = RUNS / eid; done = run / "DONE"
    train, vals, rnote = recipe(sp["recipe"])
    fp = hashlib.sha256("\n".join(sorted(str(p) for p in train)).encode()).hexdigest()[:16]
    if done.exists(): print("[%s] done -> skipped" % eid); return
    run.mkdir(parents=True, exist_ok=True)
    lst = run / "train_list.txt"; lst.write_text("\n".join(str(p) for p in train) + "\n")
    first_val = next(iter(vals.values()))
    yml = run / "data.yaml"
    import yaml
    yaml.safe_dump({"path": str(run), "train": str(lst), "val": str(first_val / "images"), "names": {0: sp["condition"]}}, open(yml, "w"))
    (run / "spec.json").write_text(json.dumps(sp, indent=1))
    args = dict(sp["args"]); model = args.pop("model"); t0 = time.time()
    print("=" * 30, eid, "|", sp["note"], "| train photos", len(train), flush=True)
    last = run / "weights" / "last.pt"
    if last.exists(): YOLO(str(last)).train(resume=True)
    else: YOLO(model).train(data=str(yml), seed=sp["seed"], device=0 if torch.cuda.is_available() else "cpu", project=str(RUNS), name=eid, exist_ok=True, plots=True, **args)
    best = run / "weights" / "best.pt"; assert best.exists(), "no best.pt for " + eid
    import pandas as pd
    d = pd.read_csv(run / "results.csv"); d.columns = [c.strip() for c in d.columns]
    rows = []
    for vname, vdir in vals.items():                                 # VALIDATION sets only; the confidence is chosen on the first (primary) validation set
        sv = H.score_model(best, args["imgsz"], vdir); conf = H.best_f1_conf(sv); p, r, f = H.at_conf(sv, conf)
        rows.append(dict(id=eid, condition=sp["condition"], recipe=sp["recipe"], seed=sp["seed"], model=model, imgsz=args["imgsz"],
                         overrides=json.dumps({k: v for k, v in args.items() if BASE_ARGS.get(k) != v}), note=sp["note"], train_photos=len(train), dataset_fingerprint=fp,
                         epochs_run=len(d), best_epoch=int(d["metrics/mAP50(B)"].idxmax()) + 1, train_minutes=round((time.time() - t0) / 60, 1), val_set=vname,
                         val_mAP50=round(sv["AP50"], 4), val_mAP50_95=round(sv["AP50_95"], 4), val_conf=round(conf, 3), val_P=round(p, 3), val_R=round(r, 3), val_F1=round(f, 3), status="ok"))
        print("   %s: mAP50 %.3f | mAP50-95 %.3f | P %.2f R %.2f F1 %.2f (conf %.2f)" % (vname, sv["AP50"], sv["AP50_95"], p, r, f, conf), flush=True)
    log_rows(rows); done.write_text("ok")


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("cmd", choices=["list", "run"]); ap.add_argument("--only", default=None)
    ap.add_argument("--condition", default=None, choices=["acne", "hyperpigmentation"], help="run only this condition's experiments (the rest stay queued)")
    a = ap.parse_args()
    reg = {r["id"] for r in registry()}
    if a.cmd == "list":
        for s in QUEUE: print("%-14s %-18s %s %s" % (s["id"], s["condition"], "DONE" if s["id"] in reg else "queued", s["note"]))
        return
    for sp in QUEUE:
        if a.only and sp["id"] != a.only: continue
        if a.condition and sp["condition"] != a.condition: continue
        try: run_one(sp)
        except Exception:
            traceback.print_exc(); log_rows([dict(id=sp["id"], condition=sp["condition"], recipe=sp["recipe"], seed=sp["seed"], note=sp["note"], status="FAILED")])
    print("QUEUE FINISHED", flush=True)


if __name__ == "__main__":
    main()
