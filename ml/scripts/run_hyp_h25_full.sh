#!/bin/bash
# resume H25 with patience 100 so it runs all 100 epochs (cosine schedule and the final no-mosaic epochs are reached)
source ~/PrecisionSkinV1/.venv_det/bin/activate
cd ~/PrecisionSkinV1
python - <<'PY'
import torch
p = "/home/bautista/PrecisionSkinV1/lesion_experiments/runs/H25_v2human_all_pub/weights/last.pt"
ck = torch.load(p, map_location="cpu", weights_only=False); print("epoch in checkpoint:", ck.get("epoch"), "| patience was", ck["train_args"].get("patience"))
ck["train_args"]["patience"] = 100; torch.save(ck, p); print("patience set to 100")
PY
python -u /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/lesion_exp.py run --only H25_v2human_all_pub >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_v2all.log 2>&1
echo ALL_DONE >> ~/PrecisionSkinV1/lesion_experiments/queue_hyp_v2all.log
