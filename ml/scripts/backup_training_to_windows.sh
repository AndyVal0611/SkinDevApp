#!/bin/bash
# copies the finished training runs (final weights, per-epoch results, settings, training lists), the experiment table and the data lists from WSL to Windows (read-only on the originals; skips runs still training)
SRC=~/PrecisionSkinV1
DST=/mnt/c/Users/Bautista/Documents/PrecisionSkin_Training_Backup
mkdir -p $DST/hyperpigmentation_runs $DST/acne_runs $DST/tables
cp $SRC/lesion_experiments/EXPERIMENTS.csv $DST/tables/ 2>/dev/null
cp $SRC/lesion_experiments/data/hyp_split.json $DST/tables/ 2>/dev/null
for d in $SRC/lesion_experiments/runs/*/; do
  n=$(basename $d)
  [ -f $d/DONE ] || [ -f $d/done ] || [ -f $d/.done ] || ls $d | grep -qi '^done' || true
  if [ -f $d/weights/best.pt ] && grep -q "^$n," $SRC/lesion_experiments/EXPERIMENTS.csv; then
    mkdir -p $DST/hyperpigmentation_runs/$n/weights
    cp $d/weights/best.pt $DST/hyperpigmentation_runs/$n/weights/
    for f in results.csv args.yaml spec.json train_list.txt data.yaml; do [ -f $d/$f ] && cp $d/$f $DST/hyperpigmentation_runs/$n/; done
  fi
done
A=$SRC/runs/lesion_acne_boxed_only
for s in 42 7 123; do mkdir -p $DST/acne_runs/seed_$s/weights; cp $A/models/seed_$s/weights/best.pt $DST/acne_runs/seed_$s/weights/; cp $A/models/seed_$s/results.csv $DST/acne_runs/seed_$s/ 2>/dev/null; cp $A/models/seed_$s/args.yaml $DST/acne_runs/seed_$s/ 2>/dev/null; done
cp $A/DEPLOYMENT_SUMMARY.md $DST/acne_runs/ 2>/dev/null
cp -r $A/export $DST/acne_runs/ 2>/dev/null
cat > $DST/README.txt <<'R'
PrecisionSkin training backup (copied from WSL ~/PrecisionSkinV1)
hyperpigmentation_runs/<run>/weights/best.pt  final weights of every finished hyperpigmentation experiment (H0 ... ), with results.csv (per epoch), args.yaml / spec.json (settings), train_list.txt (training images)
acne_runs/seed_42|7|123  the three final acne models, plus DEPLOYMENT_SUMMARY.md and the ONNX export of the notebook run
tables/EXPERIMENTS.csv  one row per experiment (data fingerprint, settings, validation scores)
Not included: the training photos (they stay in Downloads\PrecisionSkin ... and WSL), runs that were still training when this was copied, last.pt files and plots.
The ONNX files used by the app are in SkinDevApp\Model (not in git).
R
du -sh $DST
echo BACKUP_DONE
