/*
  LUMYVUE ESP32 lighting controller
  Firmware version : 1.0.0
  Protocol version : 1   (see PROTOCOL.md; the LUMYVUE app uses exactly the same protocol)

  What it does
    * Controls the two light groups of the LUMYVUE workstation over USB serial:
        SOFT lights  - the 12 V COB lights inside the housing      (GPIO 27)
        RING light   - the main light around the camera, two colour channels
                       warm / yellow (GPIO 25) and cool / white (GPIO 26)
    * The soft lights FADE IN by themselves as soon as the board gets power (about 4 seconds), without waiting for Windows or the app.
    * The ring light stays OFF until the app asks for it (CAPTURE_START / RING:n).
    * Every change glides (fades) instead of jumping; nothing in the code blocks.
    * Safety: if the ring light is on and the app goes silent for 30 s, the ring light switches itself off.

  Hardware (unchanged from the original ringlight.ino)
    The ESP32 pins drive logic-level N-channel MOSFETs (low-side switches); they do NOT power the lights directly.
    Gate -> 100 ohm from the GPIO, 10 k pulldown to GND (the light stays off while the board boots).
    The chin-rest motor driver (TB6612FNG) pins are left in their original safe state (motors stopped, driver awake);
    LUMYVUE does not move the chin rest.

  Arduino IDE settings
    Board  : "ESP32 Dev Module" (ESP32-WROOM-32 / DevKit) from the "esp32 by Espressif Systems" package, core 2.x or 3.x
    Library: Preferences (included with the ESP32 core). No other library is needed.
    Serial : 115200 baud, 8N1, lines end with \n (the app sends \n; \r\n is accepted too)
*/

#include <Preferences.h>
#include <string.h>
#include <math.h>

#define FW_VERSION       "1.0.0"
#define PROTOCOL_VERSION 1

// ===================================================================================================================================
//  Settings you may change (everything else must stay the same on every ESP32 so all firmware behaves alike)
// ===================================================================================================================================
const unsigned long SERIAL_BAUD        = 115200;
const unsigned long SOFT_FADE_IN_MS    = 4000;    // soft lights: time to fade in after power-up (and after the first command ends it)
const unsigned long FADE_MS            = 500;     // every other change glides over this time
const int           SOFT_DEFAULT       = 255;     // soft-light brightness (0..255) used at power-up when nothing was saved
const int           RING_DEFAULT       = 153;     // ring-light brightness when switched on (about 60 %)
const int           TEMP_DEFAULT       = 128;     // ring colour: 0 = fully warm, 255 = fully cool, 128 = neutral
const unsigned long RING_WATCHDOG_MS   = 30000;   // ring light off if no byte arrives for this long (the app polls every ~3 s)
const unsigned long SAVE_DELAY_MS      = 2000;    // settings are saved to flash this long after the last change

// ===================================================================================================================================
//  GPIO and PWM (identical to ringlight.ino)
// ===================================================================================================================================
const int PIN_WARM = 25;   // gate of the MOSFET on Y-  (ring, warm / yellow channel)
const int PIN_COOL = 26;   // gate of the MOSFET on W-  (ring, cool / white channel)
const int PIN_COB  = 27;   // gate of the MOSFET on the 12 V COB soft lights (negative side)

// Chin-rest motor driver (TB6612FNG). Not used by LUMYVUE: left stopped, exactly as in the original firmware.
const int PIN_A_IN1 = 16, PIN_A_IN2 = 17, PIN_A_PWM = 18;
const int PIN_B_IN1 = 19, PIN_B_IN2 = 21, PIN_B_PWM = 22;
const int PIN_STBY  = 23;

const uint32_t PWM_FREQ = 20000;                         // 20 kHz: no visible or camera flicker
const uint8_t  PWM_BITS = 10;                            // duty 0..1023
const uint32_t PWM_MAX  = (1UL << PWM_BITS) - 1;
const int CH_WARM = 0, CH_COOL = 1, CH_COB = 2, CH_MA = 3, CH_MB = 4;   // only used on core 2.x

// ===================================================================================================================================
//  PWM helpers (core 2.x and 3.x use different LEDC calls)
// ===================================================================================================================================
void pwmBegin(int pin, int ch) {
#if defined(ESP_ARDUINO_VERSION_MAJOR) && ESP_ARDUINO_VERSION_MAJOR >= 3
  ledcAttach(pin, PWM_FREQ, PWM_BITS);
#else
  ledcSetup(ch, PWM_FREQ, PWM_BITS);
  ledcAttachPin(pin, ch);
#endif
}

void pwmWrite(int pin, int ch, uint32_t duty) {
#if defined(ESP_ARDUINO_VERSION_MAJOR) && ESP_ARDUINO_VERSION_MAJOR >= 3
  ledcWrite(pin, duty);
#else
  ledcWrite(ch, duty);
#endif
}

// Gamma so a brightness slider feels even to the eye (same as the original firmware)
uint32_t toDuty(float x) {
  if (x <= 0) return 0;
  if (x >= 1) return PWM_MAX;
  return (uint32_t)(powf(x, 2.2f) * PWM_MAX + 0.5f);
}

// ===================================================================================================================================
//  Lighting state
// ===================================================================================================================================
Preferences prefs;

int  softLevel = SOFT_DEFAULT;   // remembered soft brightness 0..255
bool softOn    = true;           // soft lights are on at power-up
int  ringLevel = RING_DEFAULT;   // remembered ring brightness 0..255 (used when the ring is switched on)
bool ringOn    = false;          // the ring light is OFF until the app asks for it
int  tempLevel = TEMP_DEFAULT;   // ring colour 0 warm .. 255 cool
bool captureActive = false;      // between CAPTURE_START and CAPTURE_END

// Target duty (what the lights are heading to) and current duty (what the pins output now)
float tgtWarm = 0, tgtCool = 0, tgtCob = 0;
float curWarm = 0, curCool = 0, curCob = 0;
unsigned long softFadeMs = SOFT_FADE_IN_MS;   // slow at power-up, normal after the first command or once the target is reached
unsigned long lastFade = 0;

bool dirty = false;
unsigned long lastChange = 0;
unsigned long lastRx = 0;

void applyOutput() {
  float s = softOn ? softLevel / 255.0f : 0.0f;
  float b = ringOn ? ringLevel / 255.0f : 0.0f;
  float t = tempLevel / 255.0f;
  // Warm and cool shares add up to the brightness, so the supply never has to deliver more than one full channel at a time.
  tgtWarm = (float)toDuty(b * (1.0f - t));
  tgtCool = (float)toDuty(b * t);
  tgtCob  = (float)toDuty(s);
}

float slew(float cur, float tgt, float step) {
  if (cur < tgt) return (cur + step < tgt) ? cur + step : tgt;
  if (cur > tgt) return (cur - step > tgt) ? cur - step : tgt;
  return cur;
}

// Called from loop(): moves every light a little closer to its target. Never blocks.
void fadeUpdate() {
  unsigned long now = millis();
  unsigned long dt = now - lastFade;
  if (dt < 10) return;
  if (dt > 50) dt = 50;                                   // never jump after a long pause
  lastFade = now;
  float stepNormal = (float)PWM_MAX * dt / FADE_MS;
  float stepSoft   = (float)PWM_MAX * dt / softFadeMs;
  curWarm = slew(curWarm, tgtWarm, stepNormal);
  curCool = slew(curCool, tgtCool, stepNormal);
  curCob  = slew(curCob,  tgtCob,  stepSoft);
  pwmWrite(PIN_WARM, CH_WARM, (uint32_t)(curWarm + 0.5f));
  pwmWrite(PIN_COOL, CH_COOL, (uint32_t)(curCool + 0.5f));
  pwmWrite(PIN_COB,  CH_COB,  (uint32_t)(curCob + 0.5f));
  if (softFadeMs != FADE_MS && tgtCob > 0 && curCob >= tgtCob) softFadeMs = FADE_MS;   // the slow power-up fade is finished
}

void markChanged() {
  dirty = true;
  lastChange = millis();
}

// ===================================================================================================================================
//  Serial protocol (see PROTOCOL.md). One command per line, one answer per command.
// ===================================================================================================================================
// Commands:  HELLO  PING  STATUS  SOFT:n  RING:n  RINGLEVEL:n  TEMP:n  CAPTURE_START  CAPTURE_END  ALL_OFF      (n = 0..255)
// Answers:   READY:LUMYVUE:<fw>:<protocol>   PONG   STATUS:...   OK:<command>[:<n>]   ERROR:INVALID_COMMAND   ERROR:INVALID_BRIGHTNESS
// "OK" means the command was ACCEPTED and the target was set. It does not mean the light has finished fading or reached a measured brightness.

char lineBuf[64];
uint8_t lineLen = 0;
bool lineOverflow = false;

void replyReady() {
  Serial.print("READY:LUMYVUE:");
  Serial.print(FW_VERSION);
  Serial.print(":");
  Serial.println(PROTOCOL_VERSION);
}

void replyStatus() {
  Serial.print("STATUS:SOFT=");       Serial.print(softOn ? softLevel : 0);
  Serial.print(";SOFTLEVEL=");        Serial.print(softLevel);
  Serial.print(";RING=");             Serial.print(ringOn ? ringLevel : 0);
  Serial.print(";RINGLEVEL=");        Serial.print(ringLevel);
  Serial.print(";TEMP=");             Serial.print(tempLevel);
  Serial.print(";CAPTURE=");          Serial.print(captureActive ? 1 : 0);
  Serial.print(";FW=");               Serial.print(FW_VERSION);
  Serial.print(";P=");                Serial.print(PROTOCOL_VERSION);
  Serial.print(";UP=");               Serial.println(millis() / 1000UL);
}

void replyOkValue(const char* name, int v) {
  Serial.print("OK:");
  Serial.print(name);
  Serial.print(":");
  Serial.println(v);
}

void replyOk(const char* name) {
  Serial.print("OK:");
  Serial.println(name);
}

void replyError(const char* what) {
  Serial.print("ERROR:");
  Serial.println(what);
}

// A brightness argument: 1 to 3 digits only, 0..255
bool parseLevel(const char* s, int* out) {
  if (s == NULL || *s == 0) return false;
  int n = 0, v = 0;
  for (const char* p = s; *p; p++, n++) {
    if (*p < '0' || *p > '9' || n >= 3) return false;
    v = v * 10 + (*p - '0');
  }
  if (v > 255) return false;
  *out = v;
  return true;
}

void handleLine(char* line) {
  // split "NAME:ARG"
  char* arg = NULL;
  for (char* p = line; *p; p++) {
    if (*p == ':') { *p = 0; arg = p + 1; break; }
  }
  for (char* p = line; *p; p++) if (*p >= 'a' && *p <= 'z') *p = *p - 'a' + 'A';   // command names are not case sensitive

  int v = 0;

  if (!strcmp(line, "SOFT") || !strcmp(line, "RING") || !strcmp(line, "RINGLEVEL") || !strcmp(line, "TEMP")) {
    if (!parseLevel(arg, &v)) { replyError("INVALID_BRIGHTNESS"); return; }
    if (!strcmp(line, "SOFT")) {
      softFadeMs = FADE_MS;                              // any soft command ends the slow power-up fade
      if (v > 0) { softLevel = v; softOn = true; } else { softOn = false; }
      applyOutput(); markChanged();
      replyOkValue("SOFT", v);
    } else if (!strcmp(line, "RING")) {
      if (v > 0) { ringLevel = v; ringOn = true; } else { ringOn = false; captureActive = false; }
      applyOutput(); markChanged();
      replyOkValue("RING", v);
    } else if (!strcmp(line, "RINGLEVEL")) {
      ringLevel = (v > 0) ? v : ringLevel;               // remember the level; the ring is not switched on by this command
      applyOutput(); markChanged();
      replyOkValue("RINGLEVEL", ringLevel);
    } else {
      tempLevel = v;
      applyOutput(); markChanged();
      replyOkValue("TEMP", v);
    }
    return;
  }

  if (arg != NULL) { replyError("INVALID_COMMAND"); return; }   // the remaining commands take no argument

  if (!strcmp(line, "HELLO"))              { replyReady(); }
  else if (!strcmp(line, "PING"))          { Serial.println("PONG"); }
  else if (!strcmp(line, "STATUS"))        { replyStatus(); }
  else if (!strcmp(line, "CAPTURE_START")) { captureActive = true; ringOn = true; applyOutput(); markChanged(); replyOk("CAPTURE_START"); }
  else if (!strcmp(line, "CAPTURE_END"))   { captureActive = false; ringOn = false; applyOutput(); markChanged(); replyOk("CAPTURE_END"); }
  else if (!strcmp(line, "ALL_OFF"))       { softFadeMs = FADE_MS; softOn = false; ringOn = false; captureActive = false; applyOutput(); markChanged(); replyOk("ALL_OFF"); }
  else                                     { replyError("INVALID_COMMAND"); }
}

void readSerial() {
  while (Serial.available() > 0) {
    int c = Serial.read();
    if (c < 0) break;
    lastRx = millis();
    if (c == '\n' || c == '\r') {
      if (lineOverflow) { replyError("INVALID_COMMAND"); lineOverflow = false; lineLen = 0; continue; }
      if (lineLen == 0) continue;                        // empty line (the second half of \r\n)
      lineBuf[lineLen] = 0;
      lineLen = 0;
      handleLine(lineBuf);
      continue;
    }
    if (lineOverflow) continue;
    if (lineLen < sizeof(lineBuf) - 1) lineBuf[lineLen++] = (char)c;
    else lineOverflow = true;                            // too long: ignore the rest of the line, then report an error
  }
}

// ===================================================================================================================================
//  setup / loop
// ===================================================================================================================================
void setup() {
  Serial.begin(SERIAL_BAUD);

  // light outputs: the MOSFET gates start at 0 (lights off)
  pwmBegin(PIN_WARM, CH_WARM);
  pwmBegin(PIN_COOL, CH_COOL);
  pwmBegin(PIN_COB, CH_COB);
  pwmWrite(PIN_WARM, CH_WARM, 0);
  pwmWrite(PIN_COOL, CH_COOL, 0);
  pwmWrite(PIN_COB, CH_COB, 0);

  // chin-rest motor driver: same safe start as the original firmware (motors stopped, driver awake)
  pinMode(PIN_A_IN1, OUTPUT); pinMode(PIN_A_IN2, OUTPUT);
  pinMode(PIN_B_IN1, OUTPUT); pinMode(PIN_B_IN2, OUTPUT);
  pinMode(PIN_STBY, OUTPUT);
  pwmBegin(PIN_A_PWM, CH_MA);
  pwmBegin(PIN_B_PWM, CH_MB);
  digitalWrite(PIN_A_IN1, LOW); digitalWrite(PIN_A_IN2, LOW); pwmWrite(PIN_A_PWM, CH_MA, 0);
  digitalWrite(PIN_B_IN1, LOW); digitalWrite(PIN_B_IN2, LOW); pwmWrite(PIN_B_PWM, CH_MB, 0);
  digitalWrite(PIN_STBY, HIGH);

  // saved settings (the soft lights always come on at power-up, at the saved brightness or the default)
  prefs.begin("lumyvue", false);
  int s = prefs.getUChar("soft", SOFT_DEFAULT);
  softLevel = (s > 0) ? s : SOFT_DEFAULT;
  int r = prefs.getUChar("ring", RING_DEFAULT);
  ringLevel = (r > 0) ? r : RING_DEFAULT;
  tempLevel = prefs.getUChar("temp", TEMP_DEFAULT);
  softOn = true;                  // soft lights: fade in by themselves
  ringOn = false;                 // ring light: off until the app asks
  captureActive = false;
  softFadeMs = SOFT_FADE_IN_MS;
  applyOutput();

  lastFade = millis();
  lastRx = millis();
  replyReady();                   // tells an app that is already listening that the board has (re)started
}

void loop() {
  readSerial();
  fadeUpdate();

  // safety: never leave the ring light on if the app stopped talking (crash, cable pulled)
  if (ringOn && millis() - lastRx > RING_WATCHDOG_MS) {
    ringOn = false;
    captureActive = false;
    applyOutput();
    Serial.println("EVENT:RING_TIMEOUT");
  }

  // save to flash a moment after the last change (not on every slider movement)
  if (dirty && millis() - lastChange > SAVE_DELAY_MS) {
    prefs.putUChar("soft", (uint8_t)softLevel);
    prefs.putUChar("ring", (uint8_t)ringLevel);
    prefs.putUChar("temp", (uint8_t)tempLevel);
    dirty = false;
  }
}
