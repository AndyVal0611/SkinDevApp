#!/usr/bin/env python
"""make_hyp_reliability.py - 30 of your validation photos, copied WITHOUT their boxes, so you can box them again from scratch.
Comparing the two sets shows how much you agree with yourself on the same patches (the ceiling for box-level scores). Nothing in the original sets is touched."""
import json, random, shutil, time
from pathlib import Path
P = Path.home() / "PrecisionSkinV1"; V = P / "lesion_experiments/data/hyp_hand_val"
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); D = B / "Reliability_Rebox"
if D.exists(): raise SystemExit("exists already, not overwriting: %s" % D)
imgs = sorted((V / "images").glob("*")); random.Random(11).shuffle(imgs); imgs = imgs[:30]
S = D / "Elaiza__hyperpigmentation_rebox"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir()
for p in imgs: shutil.copy2(p, S / "images" / p.name); (S / "labels" / (p.stem + ".txt")).write_text("")
(S / "classes.txt").write_text("hyperpigmentation\n"); time.sleep(1.5)
json.dump({"drafted_at": time.time() + 2, "note": "blind re-box of 30 validation photos: from scratch, no drafts; X = no patch"}, open(D / "_drafts" / "meta.json", "w"))
shutil.copy2("/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_box_tool.py", D / "lesion_box_tool.py")
(D / "START_REBOX_30.bat").write_text('@echo off\r\ncd /d "%~dp0"\r\necho BLIND RE-BOX: 30 photos, from scratch, same rule as always (one box per patch). Do NOT look at your old boxes. X = no patch. S = save.\r\n'
    'py -3.13 lesion_box_tool.py "Elaiza__hyperpigmentation_rebox" --label hyperpigmentation --annotator Elaiza --port 8822 --export "boxed_rebox"\r\npause\r\n')
print("re-box set: %d photos" % len(imgs), "->", D / "START_REBOX_30.bat")
