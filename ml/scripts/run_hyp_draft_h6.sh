#!/bin/bash
# new hyperpigmentation drafts drawn by the H6 model (your boxes + reviewed drafts); appended to Draft_Hyperpig, saved photos untouched
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
export DRAFT_WEIGHTS=$HOME/PrecisionSkinV1/lesion_experiments/runs/H6_own_drafts/weights/best.pt
python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/hyperpig_predraft.py draft --conf 0.20 --limit 300 > ~/PrecisionSkinV1/draft_h6.log 2>&1
echo FINISHED >> ~/PrecisionSkinV1/draft_h6.log
