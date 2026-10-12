#!/bin/bash
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/hyp_final_eval.py > ~/PrecisionSkinV1/hyp_final_eval.log 2>&1
echo FINISHED >> ~/PrecisionSkinV1/hyp_final_eval.log
