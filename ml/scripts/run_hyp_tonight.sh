#!/bin/bash
# hyperpigmentation experiments for tonight, in this order (cheap and most informative first); each is resumable
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
for id in H1_own H6_own_drafts H7_own_drafts_cov H0_mix; do
  python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --only $id >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_tonight.log 2>&1
done
echo ALL_DONE >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_tonight.log
