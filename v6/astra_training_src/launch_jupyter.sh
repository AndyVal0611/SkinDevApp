#!/usr/bin/env bash
# Process-scoped CUDA environment only. No installation or persistent setting changes.
set -euo pipefail
astra_venv="${LUMYVUE_VENV:-$HOME/PrecisionSkinV1/.venv}"
if [[ $# -lt 1 ]]; then echo 'Usage: bash launch_jupyter.sh UNIQUE_RUN_ID'; exit 2; fi
export LUMYVUE_RUN_ID="$1"
astra_site="$($astra_venv/bin/python -c 'import sysconfig; print(sysconfig.get_paths()["purelib"])')"
astra_libs='/usr/lib/wsl/lib'
for component in cudnn cublas cuda_runtime cuda_nvrtc cufft curand cusolver cusparse nvjitlink; do
    if [[ -d "$astra_site/nvidia/$component/lib" ]]; then astra_libs="$astra_libs:$astra_site/nvidia/$component/lib"; fi
done
export LD_LIBRARY_PATH="$astra_libs:${LD_LIBRARY_PATH:-}"
export TF_NUM_INTRAOP_THREADS=1 TF_NUM_INTEROP_THREADS=1 OMP_NUM_THREADS=1 OPENBLAS_NUM_THREADS=1
export CUDA_VISIBLE_DEVICES="${ASTRA_CUDA_DEVICE:-0}"
if ! "$astra_venv/bin/python" -c 'import jupyterlab' >/dev/null 2>&1; then
    echo 'JupyterLab is not installed in this environment. See the manual guide for an explicit user-run install or use the existing WSL VS Code notebook kernel.'
    exit 2
fi
astra_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$astra_dir"
exec "$astra_venv/bin/python" -m jupyterlab --no-browser --notebook-dir="$astra_dir"
