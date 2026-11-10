#pragma once
#include "Arduino.h"
class Preferences {
 public:
  std::map<std::string, uint8_t> store;
  bool begin(const char*, bool) { return true; }
  uint8_t getUChar(const char* k, uint8_t def) { auto it = store.find(k); return it == store.end() ? def : it->second; }
  void putUChar(const char* k, uint8_t v) { store[k] = v; }
};
