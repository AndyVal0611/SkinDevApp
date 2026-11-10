@echo off
rem Builds the firmware host tests (needs Visual Studio with the C++ tools). Not part of the firmware.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
cd /d "%~dp0"
call "%~dp0firmware_host_test.exe"
cl /nologo /EHsc /std:c++17 /W3 /Imock firmware_sim.cpp /Fe:firmware_sim.exe
call "%~dp0firmware_host_test.exe"
