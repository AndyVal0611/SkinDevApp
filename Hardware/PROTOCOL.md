# LUMYVUE lighting protocol – version 1

One official protocol. The firmware (`LUMYVUE_ESP32_Firmware.ino`, version **1.0.0**) and the app (`ESP32Controller.cs`, protocol **1**) use exactly this. If anything here changes, change the firmware, the app and this file together and raise the version numbers.

## Link

| Item | Value |
|---|---|
| Connection | USB (the board's USB-to-serial chip appears as a COM port on the Mini PC) |
| Baud rate | **115200**, 8 data bits, no parity, 1 stop bit, no flow control |
| Framing | ASCII text, **one command per line**, terminated by `\n` (`\r\n` is also accepted) |
| Case | command names are not case sensitive; the firmware answers in upper case |
| Maximum line | 63 characters (longer lines are answered with `ERROR:INVALID_COMMAND`) |
| Brightness range | **0–255** on the wire. The app shows 0–100 % and converts: `wire = round(percent × 255 / 100)`; `percent = round(wire × 100 / 255)` |

The board prints boot messages and may print `EVENT:...` lines at any time; the app ignores every line that is not the answer to its command.

## Commands and answers

| Command | Meaning | Answer |
|---|---|---|
| `HELLO` | Start of communication. The app sends it to every port while looking for the controller | `READY:LUMYVUE:<firmware>:<protocol>` e.g. `READY:LUMYVUE:1.0.0:1` |
| `PING` | Is the controller there? | `PONG` |
| `STATUS` | Report the lighting state | `STATUS:SOFT=<n>;SOFTLEVEL=<n>;RING=<n>;RINGLEVEL=<n>;TEMP=<n>;CAPTURE=<0\|1>;FW=<firmware>;P=<protocol>;UP=<seconds>` |
| `SOFT:<n>` | Soft lights to brightness `n`; `SOFT:0` turns them off (the level is remembered) | `OK:SOFT:<n>` |
| `RING:<n>` | Ring light **on** at brightness `n` (and remembered); `RING:0` turns the ring off | `OK:RING:<n>` |
| `RINGLEVEL:<n>` | Remember the ring brightness `n` (1–255) **without switching the ring on**; if the ring is on it changes at once | `OK:RINGLEVEL:<n>` |
| `TEMP:<n>` | Ring colour: 0 = fully warm, 255 = fully cool, 128 = neutral | `OK:TEMP:<n>` |
| `CAPTURE_START` | An image capture begins: ring on at the remembered level | `OK:CAPTURE_START` |
| `CAPTURE_END` | The capture ended, was cancelled or failed: ring off (soft lights unchanged) | `OK:CAPTURE_END` |
| `ALL_OFF` | Both soft lights and ring off (levels remembered) | `OK:ALL_OFF` |

Errors:

| Answer | When |
|---|---|
| `ERROR:INVALID_COMMAND` | unknown command, an argument on a command that takes none, or a line that is too long |
| `ERROR:INVALID_BRIGHTNESS` | the number is missing, not made of 1–3 digits, or above 255 |

Unsolicited line: `EVENT:RING_TIMEOUT` – the ring was switched off by the firmware's safety watchdog.

### STATUS fields

| Field | Meaning |
|---|---|
| `SOFT` | current soft setting (0 when the soft lights are off) |
| `SOFTLEVEL` | remembered soft brightness |
| `RING` | current ring setting (0 when the ring is off) |
| `RINGLEVEL` | remembered ring brightness |
| `TEMP` | ring colour |
| `CAPTURE` | 1 between `CAPTURE_START` and `CAPTURE_END` |
| `FW`, `P` | firmware version, protocol version |
| `UP` | seconds since the board started (a small number after the app connected means the board restarted) |

## What an answer means (important)

`OK:` means the controller **accepted** the command and set its target. It does **not** mean the light has finished fading and it is **not a measurement**: the hardware has no light sensor, so neither the firmware nor LUMYVUE knows the real brightness. The percentages on screen are the controller's settings.

## Behaviour that is part of the protocol

* **Power-up:** the soft lights fade in by themselves over `SOFT_FADE_IN_MS` (4 s) to the saved brightness (default 255). The ring light is off. The board prints `READY:LUMYVUE:...` when it has started.
* **Fades:** every later change glides over `FADE_MS` (0.5 s). Nothing blocks.
* **Connecting never restarts the fade:** the app only reads `STATUS` after connecting; it sends `SOFT:<n>` only if the soft lights are off.
* **Watchdog:** if the ring light is on and no byte arrives for 30 s, the firmware switches the ring off and prints `EVENT:RING_TIMEOUT`. The app asks `STATUS` every 3 s, which keeps a real capture alive.
* **Saved settings:** soft level, ring level and colour are saved to flash 2 s after the last change.

## Default values

| Setting | Default |
|---|---|
| Soft brightness at power-up | 255 (100 %) |
| Ring brightness | 153 (60 %) |
| Ring colour | 128 (neutral) |
| Soft fade-in time | 4000 ms |
| Normal fade time | 500 ms |
| Ring watchdog | 30000 ms |
