#!/usr/bin/env python
"""
make_more_faces.py - a box-from-scratch folder of the 'non_facial' acne photos that were never reviewed (no helper drafts).

The automatic face check missed many real faces (side profiles, close-ups), so this pool still has good photos, mixed with junk: rotated crops with
black corners, smeared (stretched) borders and tiny images. Those are filtered out first; the rest is shuffled and copied.
You box what shows acne on a face and press X on everything else. Photos you save are copied to boxed_Acne_more (never touches Elaiza__acne).

    python make_more_faces.py --count 900          (WSL, any venv with opencv + pandas)
"""
import argparse, json, random, shutil, time
from pathlib import Path
import numpy as np, pandas as pd, cv2

R = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Acne_Boxing")


def junk_reason(path, min_side):
    im = cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)
    if im is None:
        return "unreadable"
    h, w = im.shape[:2]
    if min(h, w) < min_side:
        return "small"
    g = cv2.cvtColor(cv2.resize(im, (128, 128), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2GRAY).astype(np.float32)
    if (g <= 8).mean() > 0.02:
        return "black corners (rotated crop)"
    for strip, ax in ((g[:, :12], 1), (g[:, -12:], 1), (g[:12, :], 0), (g[-12:, :], 0)):    # stretched edge: almost no variation ALONG the border direction
        if np.abs(np.diff(strip, axis=ax)).mean() < 0.6:
            return "smeared border (stretched crop)"
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--count", type=int, default=900); ap.add_argument("--min-side", type=int, default=400); ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--port", type=int, default=8806)
    a = ap.parse_args()
    D = R / "More_Faces"
    if D.exists():
        raise SystemExit("exists already, not overwriting: %s" % D)
    sp = pd.read_csv(R / "sprint_photos.csv")
    reviewed = set(pd.read_csv(R / "acne_dataset_v3" / "manifest_all_reviewed.csv").stem)           # photos you already reviewed (boxed or X) never come back
    pool = sp[(sp.role == "non_facial")]
    pool = pool[~pool.name.str.rsplit(".", n=1).str[0].isin(reviewed)]
    print("non_facial photos never reviewed:", len(pool))
    names = pool.name.tolist(); random.Random(a.seed).shuffle(names)
    keep, why = [], {}
    for n in names:
        r = junk_reason(R / "Elaiza__acne/images" / n, a.min_side)
        if r:
            why[r] = why.get(r, 0) + 1
        else:
            keep.append(n)
        if len(keep) >= a.count:
            break
    print("looked at %d | kept %d | rejected: %s" % (len(keep) + sum(why.values()), len(keep), why))
    S = D / "Elaiza__acne_more"
    for d in (S / "images", S / "labels", D / "_drafts"):
        d.mkdir(parents=True, exist_ok=True)
    for n in keep:
        shutil.copy2(R / "Elaiza__acne/images" / n, S / "images" / n)
        (S / "labels" / (Path(n).stem + ".txt")).write_text("")                               # empty = not boxed yet
    (S / "classes.txt").write_text("acne\n")
    json.dump({"drafted_at": time.time() + 2, "note": "More_Faces: box from scratch (no drafts); a photo counts as reviewed once you save it"}, open(D / "_drafts" / "meta.json", "w"))
    shutil.copy2("/mnt/c/Users/Bautista/Downloads/lesion_box_tool.py", D / "lesion_box_tool.py")
    (D / "START_BOXING_MORE_FACES.bat").write_text(
        '@echo off\r\ncd /d "%~dp0"\r\necho Box every acne spot on FACES from scratch. Press X on photos with no acne, or that are not a face / skin of a face. S = save.\r\n'
        'py -3.13 lesion_box_tool.py "Elaiza__acne_more" --label acne --annotator Elaiza --port ' + str(a.port) + ' --export "boxed_Acne_more"\r\npause\r\n')
    (D / "classes.txt").write_text("acne\n")
    print("ready:", D / "START_BOXING_MORE_FACES.bat")


if __name__ == "__main__":
    main()
