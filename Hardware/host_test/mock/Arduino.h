// Minimal stand-in for the Arduino core, only so the firmware logic can be compiled and tested on a PC (not part of the firmware).
#pragma once
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cmath>
#include <string>
#include <deque>
#include <map>
#define ESP_ARDUINO_VERSION_MAJOR 3
#define HIGH 1
#define LOW 0
#define OUTPUT 1
extern unsigned long g_millis;
inline unsigned long millis() { return g_millis; }
inline void pinMode(int, int) {}
inline void digitalWrite(int, int) {}
extern std::map<int, uint32_t> g_duty;
inline bool ledcAttach(int, uint32_t, uint8_t) { return true; }
inline void ledcWrite(int pin, uint32_t duty) { g_duty[pin] = duty; }
struct MockSerial {
  std::deque<char> in; std::string out;
  void begin(unsigned long) {}
  int available() { return (int)in.size(); }
  int read() { if (in.empty()) return -1; char c = in.front(); in.pop_front(); return (unsigned char)c; }
  void print(const char* s) { out += s; }
  void print(int v) { out += std::to_string(v); }
  void print(unsigned long v) { out += std::to_string(v); }
  void println(const char* s) { out += s; out += "\n"; }
  void println(int v) { out += std::to_string(v) + "\n"; }
  void println(unsigned long v) { out += std::to_string(v) + "\n"; }
};
extern MockSerial Serial;
