#!/bin/bash
# waits until the running experiment queue has finished, then runs one more experiment:  bash run_after_queue.sh A7_fixlabels
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
while pgrep -f "lesion_exp.py run --condition" > /dev/null; do sleep 30; done
python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --only "$1" > ~/PrecisionSkinV1/lesion_experiments/queue_$1.log 2>&1
