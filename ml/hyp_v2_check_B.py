#!/usr/bin/env python
"""hyp_v2_check_B.py - the model trained on converted rule-v2 boxes (H20) against pass B, the second look it never saw for choosing anything; old-style models for comparison.
The threshold of every model is the one that was best on pass A (not on B)."""
import json, shutil, sys
from pathlib import Path
sys.path.insert(0, "/mnt/c/Users/Bautista/source/repos/SkinDevApp/ml")
import hyp_final_eval as F
import hyperpig_autolabel as H

BB = Path("/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Hyperpigmentation_Boxing/Rebox_V2_PassB"); mp = json.load(open(BB / "name_map.json"))
VB = F.E / "data" / "hyp_v2_valB"; shutil.rmtree(VB, ignore_errors=True); (VB / "images").mkdir(parents=True); (VB / "labels").mkdir(parents=True)
for orig, new in mp.items():
    src = next(iter((F.E / "data/hyp_hand_val/images").glob(Path(orig).stem + ".*"))); shutil.copy2(src, VB / "images" / src.name)
    shutil.copy2(BB / "Elaiza__hyperpigmentation_v2/labels" / (new + ".txt"), VB / "labels" / (Path(orig).stem + ".txt"))
VA = F.E / "data" / "hyp_v2_valA"; R = F.E / "runs"
print("%-34s %9s %9s | %9s %9s %6s %6s %6s" % ("model", "A mAP50", "A mAP95", "B mAP50", "B mAP95", "P", "R", "F1"))
for name, wt in (("H20 trained on converted v2 boxes", R / "H20_v2auto/weights/best.pt"), ("H10 seed 42 (old style)", R / "H10_v2_lowlr/weights/best.pt"),
                 ("H10 seed 7 (old style)", R / "H10_s7/weights/best.pt"), ("H10 seed 123 (old style)", R / "H10_s123/weights/best.pt")):
    sa = H.score_model(wt, 800, VA); c = H.best_f1_conf(sa); sb = H.score_model(wt, 800, VB); p, r, f = H.at_conf(sb, c)
    print("%-34s %9.3f %9.3f | %9.3f %9.3f %6.2f %6.2f %6.2f  (conf from A: %.3f)" % (name, sa["AP50"], sa["AP50_95"], sb["AP50"], sb["AP50_95"], p, r, f, c))
