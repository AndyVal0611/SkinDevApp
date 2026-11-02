#!/usr/bin/env bash
# Process-local CUDA 12 library setup; no installs, persistent config or dataset writes.
set -euo pipefail
smoke_venv="${LUMYVUE_VENV:-$HOME/PrecisionSkinV1/.venv}"
smoke_site="$($smoke_venv/bin/python -c 'import sysconfig; print(sysconfig.get_paths()["purelib"])')"
smoke_libs="/usr/lib/wsl/lib"
for component in cudnn cublas cuda_runtime cuda_nvrtc cufft curand cusolver cusparse nvjitlink; do
    if [[ -d "$smoke_site/nvidia/$component/lib" ]]; then
        smoke_libs="$smoke_libs:$smoke_site/nvidia/$component/lib"
    fi
done
export LD_LIBRARY_PATH="$smoke_libs:${LD_LIBRARY_PATH:-}"
export TF_NUM_INTRAOP_THREADS=1 TF_NUM_INTEROP_THREADS=1 OMP_NUM_THREADS=1 OPENBLAS_NUM_THREADS=1
export CUDA_VISIBLE_DEVICES="${ASTRA_CUDA_DEVICE:-0}"
smoke_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec timeout -k 5 420 "$smoke_venv/bin/python" -u "$smoke_dir/${1:-smoke_train_synthetic.py}"
