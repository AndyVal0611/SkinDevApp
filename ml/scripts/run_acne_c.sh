#!/bin/bash
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
for id in C1_acne_new C2_acne_new_review C3_acne_new_review_neg; do
  python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --only $id >> ~/PrecisionSkinV1/lesion_experiments/queue_acne_c.log 2>&1
done
echo ALL_DONE >> ~/PrecisionSkinV1/lesion_experiments/queue_acne_c.log
