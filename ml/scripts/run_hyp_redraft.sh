#!/bin/bash
# waits until the acne final training has finished (seed 123 DONE marker), then trains the hyperpigmentation helper and redraws the unreviewed drafts
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
until [ -f ~/PrecisionSkinV1/runs/lesion_acne_boxed_only/models/seed_123/DONE ]; do sleep 30; done
sleep 240        # let the notebook finish its own scoring cells first
python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/hyp_redraft.py > ~/PrecisionSkinV1/hyp_redraft.log 2>&1
