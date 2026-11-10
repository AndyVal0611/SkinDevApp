# LUMYVUE ESP32 lighting firmware

| | |
|---|---|
| Firmware version | **1.0.0** |
| Protocol version | **1** (see `PROTOCOL.md`, shipped next to this file) |
| File | `LUMYVUE_ESP32_Firmware.ino` (open it from the folder `LUMYVUE_ESP32_Firmware`; Arduino IDE needs the folder name to match the file name) |

This is the one official firmware. Everyone's ESP32 must run this exact file so the LUMYVUE app works with it. Do **not** change the pins, the PWM settings or the command names.

## What you need

* Arduino IDE 2.x (or 1.8.19)
* In *Boards Manager*: **"esp32" by Espressif Systems** (core 2.x or 3.x both work)
* Libraries: **none to install.** `Preferences` ships with the ESP32 core.
* A USB cable that carries **data** (some charge-only cables do not)
* The USB-to-serial driver for your board if Windows does not install it: **CP210x** (Silicon Labs) or **CH340/CH341** – look at the small chip next to the USB port

## Board settings (Tools menu)

| Setting | Value |
|---|---|
| Board | **ESP32 Dev Module** (ESP32-WROOM-32 / "DevKit") |
| Upload speed | 921600 (use 115200 if uploads fail) |
| Flash size / partition | defaults |
| Port | the COM port that appears when you plug the board in |
| Serial Monitor | **115200 baud**, "Newline" line ending |

## Upload steps

1. Install the ESP32 core (Boards Manager → search `esp32` → Install).
2. Open `LUMYVUE_ESP32_Firmware/LUMYVUE_ESP32_Firmware.ino`.
3. Plug the ESP32 in with the USB cable. Tools → Port → choose the new COM port.
4. Tools → Board → **ESP32 Dev Module**.
5. Click **Verify** (tick). It must finish with no error.
6. Click **Upload** (arrow). If you see `Connecting......`, hold the **BOOT** button on the board until the percentage starts, then release.
7. Open the Serial Monitor at 115200 baud. Press **EN/RESET** on the board: you should see `READY:LUMYVUE:1.0.0:1`. The soft lights fade in over about 4 seconds.
8. Test by typing into the Serial Monitor (send with Enter): `PING` → `PONG`, `SOFT:128` → `OK:SOFT:128`, `CAPTURE_START` → ring light on, `CAPTURE_END` → ring off, `STATUS`, `ALL_OFF`.

## GPIO and PWM reference (unchanged from the original `ringlight.ino`)

| Function | GPIO | Notes |
|---|---|---|
| Ring light, warm / yellow channel (Y-) | **25** | gate of a logic-level N-MOSFET, low-side switch |
| Ring light, cool / white channel (W-) | **26** | gate of a logic-level N-MOSFET |
| Soft lights (12 V COB, negative side) | **27** | gate of a logic-level N-MOSFET |
| Chin-rest motor driver (TB6612FNG) | 16, 17, 18 / 19, 21, 22 / 23 | **not used by LUMYVUE**: left stopped, driver awake (STBY high) exactly as in the original firmware |

The pins only switch MOSFET gates (100 Ω in series, 10 kΩ pulldown so the lights stay off while the board boots). They never power the lights directly. Do not rewire anything for this firmware.

| PWM | Value |
|---|---|
| Frequency | 20 kHz (no visible or camera flicker) |
| Resolution | 10 bit (duty 0–1023) |
| Channels (core 2.x) | warm 0, cool 1, soft 2, motor A 3, motor B 4 |
| Brightness curve | gamma 2.2, so a slider feels even to the eye |

## Settings you may change (top of the .ino)

`SOFT_FADE_IN_MS` (4000), `FADE_MS` (500), `SOFT_DEFAULT` (255), `RING_DEFAULT` (153), `TEMP_DEFAULT` (128), `RING_WATCHDOG_MS` (30000). Nothing else should differ between boards.

## Troubleshooting

| Problem | What to do |
|---|---|
| The IDE cannot find the board / no COM port | Try another cable (data cable), another USB port, install the CP210x or CH340 driver, then unplug and plug again |
| `Failed to connect to ESP32: Wrong boot mode` / stuck at `Connecting......` | Hold **BOOT** while it says Connecting, release when the upload starts; try upload speed 115200 |
| `'ledcAttach' was not declared` / `ledcSetup` errors | The file handles both cores 2.x and 3.x. Update or reinstall the "esp32" package, then Verify again |
| `Board "ESP32 Dev Module" not found` | Install the **esp32 by Espressif Systems** package in Boards Manager |
| Garbage text in the Serial Monitor | Set the monitor to **115200** baud |
| Lights never come on | Check the 12 V supply and the MOSFETs; the board prints `READY:...` when it works |
| The app says "Light controller not connected" | The COM port must not be open in the Serial Monitor or the Arduino upload at the same time. Close those, then press *Try again* in LUMYVUE → Lighting |
| The soft-light fade-in starts again when LUMYVUE opens | The board resets when the port is opened. LUMYVUE keeps DTR/RTS off, but some boards still reset: put a 10 µF capacitor between EN and GND on the board |

## Checking the firmware without a board (optional, on a PC)

`Hardware/host_test/` compiles this exact .ino against a tiny Arduino stand-in with Visual Studio's C++ tools (`build.bat`) and runs 49 checks of the protocol, fades and safety behaviour. It does not replace testing on a real board.
