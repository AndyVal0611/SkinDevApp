"""
upgrade_existing_scans.py - re-render the DISPLAY images of scans saved before the display change, so old scans match the new app:
  * overlay_<Class>.png        opacity now follows the class score (floor 0.15), same as the app's new RenderSingleClass
  * localization_overlay.png   new thin box style (white halo, strongest on top, count legend)
  * localization_combined.png  predicted-class overlay + new-style boxes (no labels)

Never touched: original.png, analysed.png, model_input_224.png, heatmap_*.png, camraw_*.npy, record.json, overlay_all.png, the database.
Before anything is rewritten the old files are copied once into <capture folder>/_before_display_update/ (an existing backup is never overwritten).

    python upgrade_existing_scans.py --root "<...>/LUMYVUE/Captures" --check     # validates the renderer against the saved old files, writes nothing
    python upgrade_existing_scans.py --root "<...>/LUMYVUE/Captures" --apply
"""
import argparse, json, shutil, sys
from pathlib import Path
import numpy as np, cv2

NAMES = ["Acne", "Hyperpigmentation", "Eczema", "Normal"]
COL = [(0, 0, 255), (255, 80, 0), (0, 190, 255), (60, 200, 0)]       # BGR, same as ClassPalette.ColorsBgr
GAMMA, FLOOR, ALPHA_MAX = 1.25, 0.15, 0.60                          # ClassPalette.Gamma / ProbabilityFloor / default AlphaMax


def read(p, flag=cv2.IMREAD_COLOR):
    return cv2.imdecode(np.fromfile(str(p), np.uint8), flag)


def write(p, img):
    ok, buf = cv2.imencode(".png", img)
    buf.tofile(str(p))


def render_class(base, heat8, cls, prob, scaled, alpha_max=ALPHA_MAX):
    h, w = base.shape[:2]
    heat = cv2.resize(heat8.astype(np.float32) / 255.0, (w, h), interpolation=cv2.INTER_LINEAR)
    a = alpha_max * np.power(heat, GAMMA) * ((FLOOR + (1.0 - FLOOR) * min(1.0, max(0.0, prob))) if scaled else 1.0)
    out = base.astype(np.float32) * (1 - a[..., None]) + np.array(COL[cls], np.float32) * a[..., None]
    return np.clip(out, 0, 255).astype(np.uint8)


def fit_alpha(base, heats, f):
    """the overlay opacity setting was per scan: recover it from the OLD saved overlay (best match over a few candidates)."""
    for k, n in enumerate(NAMES):
        old = read(f / ("overlay_%s.png" % n))
        if old is None or old.shape != base.shape:
            continue
        best = min(np.arange(0.20, 1.001, 0.05), key=lambda al: float(np.abs(render_class(base, heats[k], k, 0, False, al).astype(np.int16) - old.astype(np.int16)).mean()))
        return float(best)
    return ALPHA_MAX


def draw_boxes(frame, boxes, labels):
    o = frame.copy(); L = max(o.shape[:2]); th = max(2, L // 600); fs = max(0.4, L / 1100.0)
    write_labels = labels and len(boxes) <= 12
    for b in sorted(boxes, key=lambda x: x["confidence"]):
        x0, y0, x1, y1 = b["px"]
        p0, p1 = (int(round(x0)), int(round(y0))), (int(round(x1)), int(round(y1)))
        cv2.rectangle(o, p0, p1, (255, 255, 255), th + 2, cv2.LINE_AA)
        cv2.rectangle(o, p0, p1, COL[b["cls"]], th, cv2.LINE_AA)
        if write_labels:
            t = "%s %.2f" % (NAMES[b["cls"]], b["confidence"])
            (tw, tht), _ = cv2.getTextSize(t, cv2.FONT_HERSHEY_SIMPLEX, fs, 1); ty = max(tht + 2, p0[1] - 3)
            cv2.rectangle(o, (p0[0], ty - tht - 3), (p0[0] + tw + 4, ty + 2), COL[b["cls"]], -1)
            cv2.putText(o, t, (p0[0] + 2, ty - 1), cv2.FONT_HERSHEY_SIMPLEX, fs, (255, 255, 255), 1, cv2.LINE_AA)
    if labels and boxes:
        y = 6
        for k in sorted({b["cls"] for b in boxes}):
            n = sum(1 for b in boxes if b["cls"] == k); t = "%s %d box%s" % (NAMES[k], n, "" if n == 1 else "es")
            (tw, tht), _ = cv2.getTextSize(t, cv2.FONT_HERSHEY_SIMPLEX, fs, 1)
            cv2.rectangle(o, (6, y), (6 + tw + 8, y + tht + 8), COL[k], -1)
            cv2.putText(o, t, (10, y + tht + 3), cv2.FONT_HERSHEY_SIMPLEX, fs, (255, 255, 255), 1, cv2.LINE_AA); y += tht + 12
    return o


def capture_folders(root):
    return sorted(p.parent for p in Path(root).rglob("record.json"))


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--root", required=True)
    g = ap.add_mutually_exclusive_group(required=True); g.add_argument("--check", action="store_true"); g.add_argument("--apply", action="store_true")
    a = ap.parse_args()
    folders = capture_folders(a.root); print("capture folders:", len(folders))
    worst = []; alphas = []; done = skipped = 0
    for f in folders:
        try:
            rec = json.load(open(f / "record.json", encoding="utf-8"))
            base = read(f / "analysed.png")
            pct = (rec.get("scores") or {}).get("onnx_percent")
            if base is None or not pct:
                skipped += 1; continue
            heats = [read(f / ("heatmap_%s.png" % n), cv2.IMREAD_GRAYSCALE) for n in NAMES]
            if any(h is None for h in heats):
                skipped += 1; continue
            al = fit_alpha(base, heats, f)
            if a.check:                                              # does my renderer reproduce the OLD saved overlays (uniform strength)?
                for k, n in enumerate(NAMES):
                    old = read(f / ("overlay_%s.png" % n))
                    if old is not None and old.shape == base.shape:
                        worst.append(float(np.abs(render_class(base, heats[k], k, 0, False, al).astype(np.int16) - old.astype(np.int16)).mean()))
                alphas.append(al)
                continue
            bk = f / "_before_display_update"
            if not bk.exists():
                bk.mkdir()
                for nm in [("overlay_%s.png" % n) for n in NAMES] + ["localization_overlay.png", "localization_combined.png"]:
                    if (f / nm).exists():
                        shutil.copy2(f / nm, bk / nm)
            for k, n in enumerate(NAMES):
                write(f / ("overlay_%s.png" % n), render_class(base, heats[k], k, pct.get(n, 0.0) / 100.0, True, al))
            loc = rec.get("localization") or {}
            if loc.get("status") == "OK":
                h, w = base.shape[:2]
                boxes = [{"cls": int(b["class_index"]), "confidence": float(b["confidence"]),
                          "px": (b["xyxy_norm"][0] * w, b["xyxy_norm"][1] * h, b["xyxy_norm"][2] * w, b["xyxy_norm"][3] * h)} for b in loc.get("boxes", [])]
                write(f / "localization_overlay.png", draw_boxes(base, boxes, True))
                top = max(range(4), key=lambda i: pct.get(NAMES[i], 0.0))
                write(f / "localization_combined.png", draw_boxes(render_class(base, heats[top], top, pct.get(NAMES[top], 0.0) / 100.0, True, al), boxes, False))
            done += 1
        except Exception as ex:
            print("SKIPPED", f, "->", type(ex).__name__, ex); skipped += 1
    if a.check:
        print("renderer vs saved OLD overlays: mean abs pixel difference (0-255) median %.2f | max %.2f over %d images" % (np.median(worst), max(worst), len(worst)) if worst else "nothing to compare")
        print("opacity recovered per scan: %s" % sorted({round(x, 2) for x in alphas}))
    else:
        print("updated %d capture folders, skipped %d" % (done, skipped))


if __name__ == "__main__":
    main()
