#!/bin/bash
# executes the acne data-comparison notebook headlessly; the finished copy with all outputs is written next to the notebook run records
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd /mnt/c/Users/Bautista/source/repos/SkinDevApp/notebooks
mkdir -p ~/PrecisionSkinV1/nb_exec
jupyter nbconvert --to notebook --execute PrecisionSkin_Acne_Data_Comparison.ipynb --output-dir ~/PrecisionSkinV1/nb_exec --output PrecisionSkin_Acne_Data_Comparison_WITH_OUTPUTS.ipynb --ExecutePreprocessor.timeout=43200 > ~/PrecisionSkinV1/nb_exec/acne_comparison.log 2>&1
echo NB_DONE >> ~/PrecisionSkinV1/nb_exec/acne_comparison.log
