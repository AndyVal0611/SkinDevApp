// A simulated ESP32 for testing the LUMYVUE app on a PC: it runs the REAL firmware logic (the .ino, unchanged, against the Arduino mock) and talks
// the serial protocol over stdin / stdout. The three light duties are written to the file given as argument 1 so a test can look at them.
// It is not part of the firmware and does not test real hardware.
#include "Arduino.h"
#include "Preferences.h"
#include <windows.h>
#include <thread>
#include <mutex>
#include <iostream>

unsigned long g_millis = 0;
std::map<int, uint32_t> g_duty;
MockSerial Serial;

#include "../LUMYVUE_ESP32_Firmware/LUMYVUE_ESP32_Firmware.ino"

int main(int argc, char** argv) {
  std::string dutyFile = argc > 1 ? argv[1] : "";
  std::mutex m; std::deque<std::string> lines;
  std::thread reader([&] { std::string l; while (std::getline(std::cin, l)) { std::lock_guard<std::mutex> g(m); lines.push_back(l); } });
  reader.detach();
  ULONGLONG t0 = GetTickCount64();
  setup();
  unsigned long lastDump = 0;
  for (;;) {
    g_millis = (unsigned long)(GetTickCount64() - t0);
    { std::lock_guard<std::mutex> g(m); while (!lines.empty()) { for (char c : lines.front()) if (c != '\r') Serial.in.push_back(c); Serial.in.push_back('\n'); lines.pop_front(); } }
    loop();
    if (!Serial.out.empty()) { fwrite(Serial.out.data(), 1, Serial.out.size(), stdout); fflush(stdout); Serial.out.clear(); }
    if (!dutyFile.empty() && g_millis - lastDump >= 40) {
      lastDump = g_millis;
      FILE* f = fopen(dutyFile.c_str(), "w");
      if (f) { fprintf(f, "%u %u %u", g_duty[PIN_WARM], g_duty[PIN_COOL], g_duty[PIN_COB]); fclose(f); }
    }
    Sleep(2);
  }
}
