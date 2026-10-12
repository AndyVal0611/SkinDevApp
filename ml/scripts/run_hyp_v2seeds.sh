#!/bin/bash
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
for id in H20_s7 H20_s123; do
  python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --only $id >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_v2seeds.log 2>&1
done
echo ALL_DONE >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_v2seeds.log
