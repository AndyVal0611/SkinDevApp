#!/usr/bin/env python
"""make_hyp_reliability_v2.py - two blind passes (A today, B at least 12 hours later) of the same 30 validation photos under annotation rule v2. Copies images only."""
import json, shutil, time
from pathlib import Path
P = Path.home() / "PrecisionSkinV1"; V = P / "lesion_experiments/data/hyp_hand_val/images"
B = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing"); R = B / "Reliability_Rebox/Elaiza__hyperpigmentation_rebox/images"
names = sorted(p.name for p in R.glob("*"))
RULE = ("One box per CONNECTED dark patch (touching or closer than the smaller one's width = ONE box). Skip marks smaller than 7% of the photo. "
        "Speckles = one box only if the area is darker as a whole. Stay inside one facial region. Edge where the darkening fades to half. Usually 1-5 boxes. "
        "Do not zoom in to find more. X = nothing qualifies.")
for tag, port, order_seed in (("A", 8823, 1), ("B", 8824, 2), ("C", 8825, 3)):
    D = B / ("Rebox_V2_Pass" + tag)
    if D.exists(): print("exists, skipped:", D); continue
    S = D / "Elaiza__hyperpigmentation_v2"; (S / "images").mkdir(parents=True); (S / "labels").mkdir(parents=True); (D / "_drafts").mkdir()
    import random; order = names[:]; random.Random(order_seed).shuffle(order)
    for i, n in enumerate(order):                                   # new file names so the order and the old boxes cannot be recognised from the name
        shutil.copy2(V / n, S / "images" / ("v2%s_%02d%s" % (tag, i, Path(n).suffix))); (S / "labels" / ("v2%s_%02d.txt" % (tag, i))).write_text("")
    json.dump({n_: "v2%s_%02d" % (tag, i) for i, n_ in enumerate(order)}, open(D / "name_map.json", "w"))
    (S / "classes.txt").write_text("hyperpigmentation\n"); time.sleep(1.5)
    json.dump({"drafted_at": time.time() + 2, "note": "rule v2, pass " + tag}, open(D / "_drafts" / "meta.json", "w"))
    shutil.copy2("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Independent_Final_Set/lesion_box_tool.py", D / "lesion_box_tool.py")
    (D / ("START_V2_PASS_%s.bat" % tag)).write_text('@echo off\r\ncd /d "%~dp0"\r\necho RULE V2, PASS ' + tag + '. Read hyperpigmentation/docs/HYPERPIGMENTATION_ANNOTATION_RULE_V2.md first. Do not look at older boxes.\r\n'
        'py -3.13 lesion_box_tool.py "Elaiza__hyperpigmentation_v2" --label hyperpigmentation --annotator Elaiza --port %d --rule "%s" --export "boxed_v2_%s"\r\npause\r\n' % (port, RULE, tag))
    print("pass", tag, "->", D)
