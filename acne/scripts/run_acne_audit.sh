#!/bin/bash
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/acne/scripts/acne_annotation_audit.py > ~/PrecisionSkinV1/acne_audit.log 2>&1
echo FINISHED >> ~/PrecisionSkinV1/acne_audit.log
