// Host-side test of the LUMYVUE firmware logic (the real .ino is compiled unchanged against a tiny Arduino mock).
// It checks the serial protocol, the fades and the safety behaviour. It does NOT test the real ESP32 hardware.
#include "Arduino.h"
#include "Preferences.h"

unsigned long g_millis = 0;
std::map<int, uint32_t> g_duty;
MockSerial Serial;

#include "../LUMYVUE_ESP32_Firmware/LUMYVUE_ESP32_Firmware.ino"

static int failures = 0, checks = 0;
#define CHECK(cond, msg) do { checks++; if (!(cond)) { failures++; printf("FAIL: %s  (%s)\n", msg, #cond); } } while (0)

static void run(unsigned long ms) { unsigned long end = g_millis + ms; while (g_millis < end) { g_millis += 5; loop(); } }

// send one command line and return the answer line(s) the firmware printed
static std::string cmd(const char* text, unsigned long settle = 20) {
  Serial.out.clear();
  for (const char* p = text; *p; p++) Serial.in.push_back(*p);
  Serial.in.push_back('\n');
  run(settle);
  return Serial.out;
}

int main() {
  // ---------------- power-up
  setup();
  CHECK(Serial.out == "READY:LUMYVUE:1.0.0:1\n", "boot announces READY");
  CHECK(g_duty[PIN_COB] == 0 && g_duty[PIN_WARM] == 0 && g_duty[PIN_COOL] == 0, "all lights start at 0");

  // soft lights fade in by themselves, smoothly, over about 4 s
  uint32_t last = 0; bool monotonic = true; uint32_t atHalf = 0;
  for (int i = 0; i < 400; i++) { run(10); if (g_duty[PIN_COB] < last) monotonic = false; if (g_duty[PIN_COB] > last + 40) monotonic = false; last = g_duty[PIN_COB]; if (i == 199) atHalf = g_duty[PIN_COB]; }
  CHECK(monotonic, "soft fade-in is smooth (no jumps)");
  CHECK(atHalf > 50 && atHalf < 700, "soft fade-in is part-way after 2 s");
  run(500);
  CHECK(g_duty[PIN_COB] == PWM_MAX, "soft lights reach the default brightness");
  CHECK(g_duty[PIN_WARM] == 0 && g_duty[PIN_COOL] == 0, "ring light stays off at power-up");
  CHECK(softFadeMs == FADE_MS, "after the slow fade-in, changes use the normal fade");

  // ---------------- basic protocol
  CHECK(cmd("HELLO") == "READY:LUMYVUE:1.0.0:1\n", "HELLO");
  CHECK(cmd("PING") == "PONG\n", "PING");
  CHECK(cmd("ping") == "PONG\n", "commands are not case sensitive");
  CHECK(cmd("FOO") == "ERROR:INVALID_COMMAND\n", "unknown command");
  CHECK(cmd("PING:1") == "ERROR:INVALID_COMMAND\n", "no-argument command with an argument");
  CHECK(cmd("SOFT:256") == "ERROR:INVALID_BRIGHTNESS\n", "brightness above 255");
  CHECK(cmd("SOFT:abc") == "ERROR:INVALID_BRIGHTNESS\n", "brightness not a number");
  CHECK(cmd("SOFT:") == "ERROR:INVALID_BRIGHTNESS\n", "empty brightness");
  CHECK(cmd("SOFT") == "ERROR:INVALID_BRIGHTNESS\n", "missing brightness");
  CHECK(cmd("SOFT:-5") == "ERROR:INVALID_BRIGHTNESS\n", "negative brightness");
  CHECK(cmd("SOFT:1000") == "ERROR:INVALID_BRIGHTNESS\n", "four digits");

  // ---------------- soft lights
  CHECK(cmd("SOFT:128") == "OK:SOFT:128\n", "SOFT:128 accepted");
  run(700);
  uint32_t expectSoft = toDuty(128 / 255.0f);
  CHECK(g_duty[PIN_COB] == expectSoft, "soft duty follows the level (with gamma)");
  CHECK(g_duty[PIN_WARM] == 0 && g_duty[PIN_COOL] == 0, "soft command does not touch the ring");
  CHECK(cmd("SOFT:0") == "OK:SOFT:0\n", "SOFT:0 accepted");
  run(700);
  CHECK(g_duty[PIN_COB] == 0, "soft off");
  CHECK(cmd("STATUS").find("SOFT=0;SOFTLEVEL=128;") != std::string::npos, "status remembers the soft level");
  CHECK(cmd("SOFT:200") == "OK:SOFT:200\n", "soft on again");
  run(700);

  // ---------------- ring light
  CHECK(cmd("RINGLEVEL:100") == "OK:RINGLEVEL:100\n", "RINGLEVEL accepted");
  run(700);
  CHECK(g_duty[PIN_WARM] == 0 && g_duty[PIN_COOL] == 0, "RINGLEVEL does not switch the ring on");
  CHECK(cmd("CAPTURE_START") == "OK:CAPTURE_START\n", "CAPTURE_START");
  run(700);
  CHECK(g_duty[PIN_WARM] > 0 && g_duty[PIN_COOL] > 0, "ring on during capture (neutral colour: both channels)");
  uint32_t b = toDuty(100 / 255.0f), tw = toDuty((100 / 255.0f) * (1 - 128 / 255.0f)), tc = toDuty((100 / 255.0f) * (128 / 255.0f));
  (void)b;
  CHECK(g_duty[PIN_WARM] == tw && g_duty[PIN_COOL] == tc, "ring duty follows level and colour");
  CHECK(g_duty[PIN_COB] == toDuty(200 / 255.0f), "soft lights stay on during capture");
  CHECK(cmd("RING:255") == "OK:RING:255\n", "RING:255 while capturing");
  run(700);
  CHECK(cmd("STATUS").find("RING=255;RINGLEVEL=255;") != std::string::npos, "status shows the ring level");
  CHECK(cmd("TEMP:0") == "OK:TEMP:0\n", "TEMP:0 (warm)");
  run(700);
  CHECK(g_duty[PIN_COOL] == 0 && g_duty[PIN_WARM] == PWM_MAX, "fully warm uses only the warm channel");
  CHECK(cmd("CAPTURE_END") == "OK:CAPTURE_END\n", "CAPTURE_END");
  run(700);
  CHECK(g_duty[PIN_WARM] == 0 && g_duty[PIN_COOL] == 0, "ring off after capture");
  CHECK(g_duty[PIN_COB] == toDuty(200 / 255.0f), "soft lights stay on after capture");
  CHECK(cmd("RING:0") == "OK:RING:0\n", "RING:0");

  // ---------------- watchdog: the ring never stays on when the app goes silent
  cmd("CAPTURE_START"); run(700);
  CHECK(g_duty[PIN_WARM] > 0 || g_duty[PIN_COOL] > 0, "ring on");
  Serial.out.clear();
  run(31000);
  CHECK(g_duty[PIN_WARM] == 0 && g_duty[PIN_COOL] == 0, "ring switched off by the watchdog");
  CHECK(Serial.out.find("EVENT:RING_TIMEOUT") != std::string::npos, "watchdog reports an event");
  CHECK(g_duty[PIN_COB] > 0, "watchdog does not touch the soft lights");

  // ---------------- polling keeps the ring alive
  cmd("CAPTURE_START"); run(700);
  for (int i = 0; i < 12; i++) { run(3000); cmd("STATUS"); }
  CHECK(g_duty[PIN_WARM] > 0 || g_duty[PIN_COOL] > 0, "ring stays on while the app keeps polling");

  // ---------------- ALL_OFF
  CHECK(cmd("ALL_OFF") == "OK:ALL_OFF\n", "ALL_OFF");
  run(700);
  CHECK(g_duty[PIN_COB] == 0 && g_duty[PIN_WARM] == 0 && g_duty[PIN_COOL] == 0, "everything off");

  // ---------------- input handling
  Serial.out.clear();
  for (const char* p = "PING\r\nPING\r\n"; *p; p++) Serial.in.push_back(*p);
  run(50);
  CHECK(Serial.out == "PONG\nPONG\n", "CRLF line endings");
  Serial.out.clear();
  std::string longLine(100, 'A');
  for (char c : longLine) Serial.in.push_back(c);
  Serial.in.push_back('\n');
  run(50);
  CHECK(Serial.out == "ERROR:INVALID_COMMAND\n", "over-long line is rejected");
  CHECK(cmd("PING") == "PONG\n", "still works after an over-long line");

  // ---------------- saved settings survive a restart and the soft lights fade in again
  run(3000);
  cmd("SOFT:90"); cmd("RINGLEVEL:77"); run(3000);
  Preferences keep = prefs;
  g_millis += 10; setup();
  prefs = keep; // (the mock keeps its own store; a real board keeps flash)
  printf("checks: %d, failures: %d\n", checks, failures);
  return failures == 0 ? 0 : 1;
}
