#!/bin/bash
# waits until ALL acne experiments (including A7) have finished, then resumes the hyperpigmentation experiments (H0 resumes from its saved epoch)
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
while pgrep -f "lesion_exp.py run" > /dev/null || [ ! -f ~/PrecisionSkinV1/lesion_experiments/runs/A7_fixlabels/DONE ]; do sleep 30; done
python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --condition hyperpigmentation > ~/PrecisionSkinV1/lesion_experiments/queue_hyp.log 2>&1
