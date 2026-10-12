#!/bin/bash
# hyperpigmentation round 2: H8 (more reviewed drafts), then one-factor changes on top of it; each resumable
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
for id in H8_drafts_v2 H9_v2_960 H10_v2_lowlr H11_v2_m800; do
  python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --only $id >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_v2.log 2>&1
done
echo ALL_DONE >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_v2.log
