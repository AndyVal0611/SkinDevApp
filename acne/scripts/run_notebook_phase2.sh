#!/bin/bash
# executes the acne Phase 2 notebook headlessly; the finished copy with all outputs is saved next to the other run records
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd /mnt/c/Users/Bautista/source/repos/SkinDevApp/acne/notebooks
mkdir -p ~/PrecisionSkinV1/nb_exec
jupyter nbconvert --to notebook --execute PrecisionSkin_Acne_Phase2_FinalTraining.ipynb --output-dir ~/PrecisionSkinV1/nb_exec --output PrecisionSkin_Acne_Phase2_FinalTraining_WITH_OUTPUTS.ipynb --ExecutePreprocessor.timeout=86400 > ~/PrecisionSkinV1/nb_exec/acne_phase2.log 2>&1
echo NB_DONE >> ~/PrecisionSkinV1/nb_exec/acne_phase2.log
