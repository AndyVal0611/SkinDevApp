@echo off
rem ============================================================================
rem PrecisionSkin - start the Grad-CAM++ explanation service.
rem Keep this window open while scanning. Close it to stop the service.
rem
rem Python is chosen in this order:
rem   1. .venv\Scripts\python.exe next to this file
rem   2. the PRECISIONSKIN_PYTHON environment variable (full path to python.exe)
rem   3. py -3.12, then py -3.11, then python - the first one that has TensorFlow
rem
rem One-time setup if none has TensorFlow (Python 3.11 or 3.12 recommended):
rem   py -3.12 -m venv .venv
rem   .venv\Scripts\python -m pip install -r requirements_gradcam.txt
rem ============================================================================
cd /d "%~dp0"
title PrecisionSkin Grad-CAM++ service

set "PY="
if exist ".venv\Scripts\python.exe" set "PY=.venv\Scripts\python.exe"
if not defined PY if defined PRECISIONSKIN_PYTHON set "PY=%PRECISIONSKIN_PYTHON%"
if not defined PY (py -3.12 -c "import tensorflow" >nul 2>&1 && set "PY=py -3.12")
if not defined PY (py -3.11 -c "import tensorflow" >nul 2>&1 && set "PY=py -3.11")
if not defined PY (python -c "import tensorflow" >nul 2>&1 && set "PY=python")

if not defined PY (
    echo.
    echo  No Python with TensorFlow was found.
    echo  Set it up once, in this folder:
    echo      py -3.12 -m venv .venv
    echo      .venv\Scripts\python -m pip install -r requirements_gradcam.txt
    echo  or set PRECISIONSKIN_PYTHON to the full path of a python.exe that has TensorFlow.
    echo.
    pause
    exit /b 1
)

echo Using: %PY%
echo Starting the Grad-CAM++ service on http://127.0.0.1:8765  (loading the model can take up to a minute)
echo.
%PY% gradcam_service.py --model precisionskin_best.keras --labels labels.json

echo.
echo The service stopped. Press any key to close this window.
pause >nul
