#!/bin/bash
# executes the read-only evidence notebook headlessly and saves the finished copy with all outputs
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd /mnt/c/Users/Bautista/source/repos/SkinDevApp/shared/notebooks
mkdir -p ~/PrecisionSkinV1/nb_exec
jupyter nbconvert --to notebook --execute PrecisionSkin_Training_Evidence_Acne_and_Hyperpigmentation.ipynb --output-dir ~/PrecisionSkinV1/nb_exec --output PrecisionSkin_Training_Evidence_Acne_and_Hyperpigmentation_WITH_OUTPUTS.ipynb --ExecutePreprocessor.timeout=7200 > ~/PrecisionSkinV1/nb_exec/evidence.log 2>&1
echo NB_DONE >> ~/PrecisionSkinV1/nb_exec/evidence.log
