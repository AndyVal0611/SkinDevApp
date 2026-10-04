# PrecisionSkin – Run the whole app on a new laptop

This guide sets up **everything**: the WPF app (scan, results, report, settings), the **Grad-CAM++ service**, and the new **lesion localization** detector.
You need about **1 hour** the first time (most of it downloads). Windows 10 or 11, 64-bit, with a webcam and internet.

The patient/participant data, scans and database of the original machine are **not** on GitHub. Your copy starts empty and creates its own.

---

## 1. Install the tools (once)

| Tool | Notes |
|---|---|
| **Git** | https://git-scm.com/download/win (default options). |
| **Visual Studio 2022 or newer, Community** | https://visualstudio.microsoft.com/ – in the installer tick the workload **".NET desktop development"**. Also make sure the individual component **".NET Framework 4.7.2 targeting pack"** is ticked. |
| **Python 3.12** (3.11 also works; **not 3.13 or 3.14**) | https://www.python.org/downloads/ – tick **"Add python.exe to PATH"** and keep the **py launcher**. |

Check in PowerShell: `git --version` and `py -3.12 --version`.

---

## 2. Get the code

In PowerShell:

    cd C:\Users\<you>\source\repos
    git clone https://github.com/AndyVal0611/SkinDevApp
    cd SkinDevApp
    git checkout master

(If you already cloned it: `git checkout master` then `git pull`.)
Everything the app needs is in this folder, including both ONNX models (`SkinDevApp\Model\`) and the Keras model (`precisionskin_best.keras`).

---

## 3. Set up the Grad-CAM++ service (once, about 10 minutes)

In the same PowerShell window, **inside the SkinDevApp folder** (the one that contains `start_gradcam_service.bat`):

    py -3.12 -m venv .venv
    .venv\Scripts\python -m pip install --upgrade pip
    .venv\Scripts\python -m pip install -r requirements_gradcam.txt

This downloads TensorFlow (large). When it finishes, **double-click `start_gradcam_service.bat`**.
A black window opens; loading the model takes up to a minute. When it prints **READY**, the service is running on `http://127.0.0.1:8765`.
**Keep that window open while you use the app** (closing it stops the service).

Quick check: open `http://127.0.0.1:8765/health` in a browser. It should show `"ok": true`.

---

## 4. Build and run the app

1. Open `SkinDevApp.slnx` in Visual Studio.
2. At the top choose **Debug** and **x64** (not "Any CPU", not x86).
3. **Build > Build Solution** (`Ctrl+Shift+B`). The first build downloads the NuGet packages, so it needs internet. Wait for "Build succeeded".
4. Press **F5** to run.

**First sign-in:** there is no default account. The login screen asks you to **create the first researcher account** (password of at least 8 characters). Remember it; it only exists on this laptop.

---

## 5. Try a scan

1. Register a **test participant** (use a teammate, not a real patient) and record consent.
2. On the preparation screen, check the AI classifier is ready (the Grad-CAM++ service is optional but needed for heatmaps).
3. Scan **Front, Left, Right** with the webcam (good, even light; face the camera; hold still).
4. On the **Results** page use the toggle above the images: **Original / Grad-CAM++ / Localization / Combined**.
5. Researcher screens (signed in): **Comparison Gallery**, **Generate Research Report**, **Settings** (including "Lesion localization (detector)").

---

## 6. If something goes wrong

| Problem | What to do |
|---|---|
| Build error about missing packages | Check internet, then **Tools > NuGet Package Manager > Package Manager Settings > allow restore**, or right-click the solution > **Restore NuGet Packages**, then build again. |
| Build says the file is locked | Close any running copy of the app, then rebuild. |
| The platform box says "Any CPU" | Choose **x64**. |
| `python` is not recognised | Use `py -3.12` instead of `python`. |
| The service window says "No Python with TensorFlow was found" | Run the three commands in section 3 inside the **SkinDevApp** folder. |
| Port 8765 already in use | Another copy of the service is running. Close it (or the app's launcher window) and start again. |
| "Grad-CAM++ unavailable" on Results | The service window is not open or still loading. The scan, scores and localization still work. |
| No camera found | Windows **Settings > Privacy & security > Camera**: allow apps (and desktop apps) to use the camera; close other apps using it. Any webcam works; pick it in **Settings**. |
| "Localization not available" on an old scan | Normal: scans made before the feature have no localization. Make a new scan. |

---

## 7. Good to know

- Localization boxes are **candidate lesion locations** from a research detector: not a diagnosis, not a lesion count; lesions can be missed. Eczema is a pilot class.
- Do not copy real participants' photos to other computers or online tools without the study's consent terms.
- Training and the notebooks are **not** needed to run the app.
