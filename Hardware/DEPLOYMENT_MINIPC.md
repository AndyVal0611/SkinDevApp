# LUMYVUE + ESP32 lighting – deploying to the Mini PC

Development happens on the laptop; the Mini PC only runs the finished files. **Nothing on the Mini PC needs Visual Studio or the Arduino IDE**, and nothing in the app refers to the laptop's COM port: the app finds the ESP32's COM port by itself on the Mini PC (or you choose it in Lighting).

Versions: firmware **1.0.0**, protocol **1** (`Hardware/PROTOCOL.md`).

## 1. Build the application on the laptop (Visual Studio)

1. Open `SkinDevApp.slnx`.
2. Set the configuration to **Release** and choose **Build → Rebuild Solution**. (This is a classic .NET Framework 4.7.2 WPF application: the "publish" is simply the Release output folder.)
3. The finished program is the whole folder `SkinDevApp\bin\Release\`: `SkinDevApp.exe`, its DLLs, the AI model files, the Grad-CAM / detector files, the `Kiosk` scripts and the configuration. Copy the **whole folder**, not just the exe.

## 2. Transfer to the Mini PC (USB flash drive)

1. Copy `bin\Release` and the firmware folder `Hardware\LUMYVUE_ESP32_Firmware` (with `PROTOCOL.md`) to the flash drive.
2. On the Mini PC copy `bin\Release` to a permanent place, e.g. `C:\LUMYVUE\`.
3. Windows 10/11 already includes .NET Framework 4.7.2+. Nothing else must be installed.
4. A copied database keeps the laptop's scan paths. For a fresh Mini PC just start the app (it creates an empty database). To move existing patients and scans, ask for the safe procedure first.

## 3. Flash the ESP32 (on any computer with the Arduino IDE)

Follow `LUMYVUE_ESP32_Firmware/README.md`. In short: ESP32 Dev Module, open the .ino, Verify, Upload, Serial Monitor at 115200 shows `READY:LUMYVUE:1.0.0:1`. Do this on the laptop (or your teammate's computer), **not** on the Mini PC. The firmware is the same for everyone.

## 4. Connect the ESP32 to the Mini PC

1. Plug the ESP32's USB cable into a USB port of the Mini PC. Use the **same port every time** (Windows gives each port its own COM number).
2. Windows installs the USB-serial driver (CP210x or CH340). If the Mini PC is offline and the driver is missing, install the driver from the flash drive.
3. Device Manager → **Ports (COM & LPT)** shows the board, e.g. `Silicon Labs CP210x USB to UART Bridge (COM5)` or `USB-SERIAL CH340 (COM5)`.

## 5. Configure LUMYVUE

1. Start LUMYVUE → menu → **Lighting**.
2. "Controller port (USB)" stays on **Automatic**. LUMYVUE asks every non-Bluetooth serial port until the controller answers, then shows `Connected to the light controller – COM5 · firmware 1.0.0`.
3. If it stays "not connected", choose the COM port from Device Manager in the list, press *Try again*. Close the Arduino Serial Monitor first – only one program can use the port.

## 6. Test both physical lights

1. Lighting → **Soft lights**: move the slider; the soft lights follow. Tap the button to turn them off and on.
2. Lighting → **Test ring light**: the ring light turns on (it switches itself off after one minute or when you leave the page). Move the brightness and colour sliders.
3. Scan screen → press **Start camera**: the ring light comes on before the camera opens. After each image is captured the ring turns off, then it comes on again for the next angle (front, left, right). The **Lighting** panel on the scan screen changes both brightnesses while capturing.
4. Cancel a scan (New scan / leave the page): the ring turns off.

## 7. Automatic start of LUMYVUE

Use `Kiosk\Install-LumyvueKiosk.ps1` (run as administrator) – see `Kiosk\README.txt`:
* LUMYVUE starts full screen after every sign-in and is started again if it stops unexpectedly.
* Turn on automatic sign-in (`netplwiz`) and, in the BIOS, "power on after power loss".
* After a restart LUMYVUE reconnects to the ESP32 by itself (it checks the link every few seconds). If the ESP32 is unplugged, LUMYVUE keeps working without lighting and reconnects when it is back.

## 8. USB power and what happens at power-on (check this on the Mini PC)

This depends on the Mini PC's BIOS and cannot be known from the laptop:

| If the Mini PC's USB ports… | Then |
|---|---|
| lose power when the Mini PC is off (usual) | the ESP32 starts when the Mini PC gets power: the **soft lights fade in at power-on**, before Windows. This is the intended behaviour |
| keep power when the Mini PC is off ("USB standby power" / ErP off) | the ESP32 stays on. LUMYVUE's shutdown turns the lights off, but at the next start the board does not restart: the soft lights come on only when LUMYVUE connects (the board then fades them in). Set the BIOS option for USB standby power to **disabled** to get the first case |

Also check: while LUMYVUE opens the COM port the soft-light fade must **not** restart. LUMYVUE keeps DTR/RTS off to avoid resetting the board, but some boards reset anyway (then add a 10 µF capacitor EN→GND, see the firmware README). Watch the lights when you start LUMYVUE after a power-up.

## 9. Test the whole workflow and the shutdown

1. Power-cycle the Mini PC: soft lights fade in, Windows starts, LUMYVUE opens, Lighting shows *Connected*.
2. Run a three-angle scan (front, left, right): ring on/off per angle, results appear normally.
3. Press **Shut down** → confirm: a scan that is saving finishes, **both lights go off**, the Mini PC shuts down. If the ESP32 is unplugged the shutdown still proceeds (the light step waits at most 1.5 s).

## What is verified, and how

| Item | Status |
|---|---|
| Protocol and firmware behaviour (parsing, errors, fades, watchdog, saved settings) | **Compiled and tested on a PC** (MSVC with an Arduino stand-in): 49 checks pass. **Not** compiled for the ESP32 with the Arduino IDE |
| LUMYVUE ↔ firmware logic (auto detect, connect, commands, acknowledgments, ring on/off, independent brightness, lost connection, reconnect, shutdown) | **Tested on a PC** against a simulated ESP32 running the real firmware logic: 23 checks pass |
| C# application | **Compiled** (MSBuild Release/Debug), 104 unit tests pass; the new screens were rendered and checked |
| Capture hooks (ring on before each view, off after each, off on cancel/failure) | **Code inspection + compiled**; exercised only in part by the simulator tests. Not run with a real camera session |
| Real ESP32, USB serial on Windows, real lights, DTR reset behaviour, USB power at boot, kiosk start-up, Windows shutdown with the lights | **Not tested on hardware.** Use sections 5–9 on the Mini PC |
