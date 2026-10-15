#!/bin/bash
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
for id in H25_s7 H25_s123; do
  python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/shared/lesion_exp.py run --only $id >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_h25seeds.log 2>&1
done
echo ALL_DONE >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_h25seeds.log
