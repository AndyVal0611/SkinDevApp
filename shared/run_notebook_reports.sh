#!/bin/bash
# executes the two report notebooks (acne, hyperpigmentation) one after the other, headlessly, and saves finished copies with all outputs.
# nothing is trained (TRAIN = False) and the test photos are not re-scored (the stored lock files are shown).
source ~/PrecisionSkinV1/.venv_det/bin/activate
R=/mnt/c/Users/Bautista/source/repos/SkinDevApp
mkdir -p ~/PrecisionSkinV1/nb_exec
for pair in "acne/notebooks/PrecisionSkin_Acne_Training_and_Results" "hyperpigmentation/notebooks/PrecisionSkin_Hyperpigmentation_Training_and_Results"; do
  d=$(dirname $pair); n=$(basename $pair)
  cd $R/$d
  jupyter nbconvert --to notebook --execute $n.ipynb --output ${n}_WITH_OUTPUTS.ipynb --ExecutePreprocessor.timeout=7200 > ~/PrecisionSkinV1/nb_exec/$n.log 2>&1
  echo "$n exit=$?" >> ~/PrecisionSkinV1/nb_exec/reports_done.log
done
echo NB_DONE >> ~/PrecisionSkinV1/nb_exec/reports_done.log
