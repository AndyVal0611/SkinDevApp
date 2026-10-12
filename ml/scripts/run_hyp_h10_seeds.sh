#!/bin/bash
# confirm the best hyperpigmentation setting (H10) with two more seeds
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
for id in H10_s7 H10_s123; do
  python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --only $id >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_seeds.log 2>&1
done
echo ALL_DONE >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_seeds.log
