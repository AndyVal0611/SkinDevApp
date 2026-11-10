/*
  Ring light controller for ESP32 (warm + cool channels) with a web GUI.

  The ring is common-positive: the red wire (+) goes to the supply, and each
  colour is switched on its NEGATIVE side:
      black wire (board pad A) -> ring pad Y-  (warm / yellow channel)
      white wire (board pad B) -> ring pad W-  (cool / white channel)

  The ESP32 can NOT drive the ring directly. Each channel needs a logic-level
  N-channel MOSFET (e.g. IRLZ44N, AO3400) used as a low-side switch:

      ESP32 GPIO --[100R]--+-- MOSFET gate
                           |
                         [10k]  (pulldown, keeps the light off while booting)
                           |
      supply GND ----------+-- MOSFET source (and ESP32 GND)
      ring Y- / W- -----------> MOSFET drain

  Open the page at http://ringlight.local or at the IP printed in the Serial
  Monitor (115200 baud). If the home WiFi is not reachable the board starts its
  own access point instead (see AP_SSID / AP_PASS, page at http://192.168.4.1).

  Works with ESP32 Arduino core 2.x and 3.x.

  v2: every light now FADES instead of switching abruptly. The soft (COB) lights fade in over 4 seconds as soon as the board gets power
  (the Mini PC supply), even while the WiFi is still connecting; all later changes glide over half a second. The ring light still
  starts OFF and is switched on by the LUMYVUE app when image capture begins.
*/

#include <WiFi.h>
#include <WebServer.h>
#include <ESPmDNS.h>
#include <Preferences.h>

// ---------------- Settings you should edit ----------------
const char* WIFI_SSID = "YOUR_WIFI_SSID";
const char* WIFI_PASS = "YOUR_WIFI_PASSWORD";

const char* AP_SSID   = "RingLight";      // fallback hotspot name
const char* AP_PASS   = "ringlight123";   // at least 8 characters, change it
const char* HOST_NAME = "ringlight";      // -> http://ringlight.local

const int PIN_WARM = 25;   // gate of the MOSFET on Y- (black wire / pad A)
const int PIN_COOL = 26;   // gate of the MOSFET on W- (white wire / pad B)
const int PIN_COB  = 27;   // gate of the MOSFET on the 12V COB lights (negative side)

// Chinrest motors on a TB6612FNG driver (motor power VM = 6 V from the buck converter)
const int PIN_A_IN1 = 16, PIN_A_IN2 = 17, PIN_A_PWM = 18;   // lift motor (up / down)
const int PIN_B_IN1 = 19, PIN_B_IN2 = 21, PIN_B_PWM = 22;   // front/back motor
const int PIN_STBY  = 23;
const int MOTOR_SPEED = 75;                 // % of full PWM, start low and raise if needed
const unsigned long MOTOR_HOLD_MS = 400;    // motor stops if no command arrives for this long
const unsigned long MOTOR_MAX_MS  = 10000;  // never run longer than this in one go (no limit switches)
// ----------------------------------------------------------

const uint32_t PWM_FREQ = 20000;               // 20 kHz: no visible or camera flicker
const uint8_t  PWM_BITS = 10;                  // 0..1023
const uint32_t PWM_MAX  = (1UL << PWM_BITS) - 1;
const int CH_WARM = 0, CH_COOL = 1, CH_COB = 2, CH_MA = 3, CH_MB = 4; // only used on core 2.x

WebServer server(80);
Preferences prefs;

bool on = true;
int  brightness = 60;   // 0..100
int  temp = 50;         // 0 = fully warm, 100 = fully cool

bool cobOn = true;      // COB lights start ON by themselves when the board powers up
int  cobBrightness = 100;   // 0..100

bool dirty = false;
unsigned long lastChange = 0;

// Motor type must be defined BEFORE the first function, or the Arduino IDE
// auto-generated prototypes fail with "'Motor' was not declared in this scope".
struct Motor {
  int in1, in2, pwm, ch;
  int dir;                    // -1, 0, +1
  unsigned long until, startedAt;
  bool locked;                // hit the max run time, wait for release
};

// ---- PWM helpers (core 2.x and 3.x use different LEDC calls) ----
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

// Gamma so the slider feels even to the eye
uint32_t toDuty(float x) {
  if (x <= 0) return 0;
  if (x >= 1) return PWM_MAX;
  return (uint32_t)(powf(x, 2.2f) * PWM_MAX + 0.5f);
}

// Targets and the values being output now (duty 0..PWM_MAX). The lights glide from "now" to the target.
float tgtWarm = 0, tgtCool = 0, tgtCob = 0;
float curWarm = 0, curCool = 0, curCob = 0;
const unsigned long FADE_MS_NORMAL    = 500;    // every normal change glides over 0.5 s
const unsigned long FADE_MS_SOFT_BOOT = 4000;   // soft lights fade in over 4 s after power-up
unsigned long cobFadeMs = FADE_MS_SOFT_BOOT;
unsigned long lastFade = 0;

void applyOutput() {
  float b = on ? brightness / 100.0f : 0.0f;
  float t = temp / 100.0f;
  // Warm and cool shares add up to the brightness setting, so the supply
  // never has to deliver more than one full channel at a time.
  tgtWarm = (float)toDuty(b * (1.0f - t));
  tgtCool = (float)toDuty(b * t);
  tgtCob  = (float)toDuty(cobOn ? cobBrightness / 100.0f : 0.0f);
}

float slew(float cur, float tgt, float step) {
  if (cur < tgt) return (cur + step < tgt) ? cur + step : tgt;
  if (cur > tgt) return (cur - step > tgt) ? cur - step : tgt;
  return cur;
}

// Call often (loop, and while the WiFi connects): moves every light a little closer to its target.
void fadeUpdate() {
  unsigned long now = millis();
  unsigned long dt = now - lastFade;
  if (dt < 10) return;
  if (dt > 50) dt = 50;                       // never jump after a long pause
  lastFade = now;
  float stepNormal = (float)PWM_MAX * dt / FADE_MS_NORMAL;
  float stepCob    = (float)PWM_MAX * dt / cobFadeMs;
  curWarm = slew(curWarm, tgtWarm, stepNormal);
  curCool = slew(curCool, tgtCool, stepNormal);
  curCob  = slew(curCob,  tgtCob,  stepCob);
  pwmWrite(PIN_WARM, CH_WARM, (uint32_t)(curWarm + 0.5f));
  pwmWrite(PIN_COOL, CH_COOL, (uint32_t)(curCool + 0.5f));
  pwmWrite(PIN_COB,  CH_COB,  (uint32_t)(curCob + 0.5f));
  if (cobFadeMs != FADE_MS_NORMAL && tgtCob > 0 && curCob >= tgtCob) cobFadeMs = FADE_MS_NORMAL;   // the slow start-up fade is done
}

void markChanged() {
  dirty = true;
  lastChange = millis();
}

// ---- Chinrest motors (dead-man: they run only while commands keep arriving) ----
Motor lift = {PIN_A_IN1, PIN_A_IN2, PIN_A_PWM, CH_MA, 0, 0, 0, false};
Motor fb   = {PIN_B_IN1, PIN_B_IN2, PIN_B_PWM, CH_MB, 0, 0, 0, false};

void motorStopPins(Motor &m) {
  digitalWrite(m.in1, LOW);
  digitalWrite(m.in2, LOW);
  pwmWrite(m.pwm, m.ch, 0);
  m.dir = 0;
}

void motorRun(Motor &m, int dir) {
  unsigned long now = millis();
  m.until = now + MOTOR_HOLD_MS;
  if (dir == 0) { motorStopPins(m); m.locked = false; return; }
  if (m.locked) return;
  if (m.dir == 0) m.startedAt = now;
  if (now - m.startedAt > MOTOR_MAX_MS) { motorStopPins(m); m.locked = true; return; }
  digitalWrite(m.in1, dir > 0 ? HIGH : LOW);
  digitalWrite(m.in2, dir > 0 ? LOW : HIGH);
  pwmWrite(m.pwm, m.ch, PWM_MAX * MOTOR_SPEED / 100);
  m.dir = dir;
}

void motorUpdate(Motor &m) {
  if ((m.dir != 0 || m.locked) && millis() > m.until) {
    motorStopPins(m);
    m.locked = false;
  }
}

// ---- Web page ----
const char PAGE[] PROGMEM = R"rawliteral(
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Ring Light</title>
<style>
  body{margin:0;font-family:system-ui,sans-serif;background:#14161a;color:#eee;display:flex;justify-content:center}
  .card{width:min(420px,92vw);margin:24px 0;padding:22px;background:#1e2127;border-radius:16px}
  h1{margin:0 0 18px;font-size:22px}
  button{font:inherit;border:0;border-radius:10px;padding:12px 16px;cursor:pointer;background:#2c313a;color:#eee}
  #pwr{width:100%;font-size:18px;padding:16px;margin-bottom:22px}
  #pwr.on,#cpwr.on{background:#f5b942;color:#1a1a1a;font-weight:700}
  #cpwr{width:100%;font-size:18px;padding:16px;margin-bottom:6px}
  label{display:flex;justify-content:space-between;margin:18px 0 8px;font-size:14px;color:#b8bdc7}
  input[type=range]{width:100%;height:28px}
  #t{-webkit-appearance:none;appearance:none;height:14px;border-radius:7px;
     background:linear-gradient(90deg,#ffae42,#fff3dc,#bfe0ff)}
  #t::-webkit-slider-thumb{-webkit-appearance:none;width:26px;height:26px;border-radius:50%;background:#fff;border:2px solid #555}
  #t::-moz-range-thumb{width:26px;height:26px;border-radius:50%;background:#fff;border:2px solid #555}
  .hold{touch-action:none;user-select:none;-webkit-user-select:none;padding:18px 16px}
  .row{display:flex;gap:8px;margin-top:22px}
  .row button{flex:1}
  .ends{display:flex;justify-content:space-between;font-size:12px;color:#8a909b;margin-top:4px}
  #msg{margin-top:16px;font-size:12px;color:#e07a7a;min-height:16px}
</style>
</head>
<body>
<div class="card">
  <h1>Ring Light</h1>
  <button id="pwr">Power</button>

  <label>Brightness <span id="bv"></span></label>
  <input type="range" id="b" min="0" max="100">

  <label>Color temperature <span id="tv"></span></label>
  <input type="range" id="t" min="0" max="100">
  <div class="ends"><span>Warm</span><span>Cool</span></div>

  <div class="row">
    <button data-t="0">Warm</button>
    <button data-t="50">Neutral</button>
    <button data-t="100">Cool</button>
  </div>

  <h1 style="margin-top:30px">COB lights</h1>
  <button id="cpwr">COB</button>
  <label>COB brightness <span id="cbv"></span></label>
  <input type="range" id="cb" min="0" max="100">

  <h1 style="margin-top:30px">Chinrest</h1>
  <div class="row">
    <button class="hold" data-m="lift" data-d="1">Up</button>
    <button class="hold" data-m="lift" data-d="-1">Down</button>
  </div>
  <div class="row">
    <button class="hold" data-m="fb" data-d="1">Forward</button>
    <button class="hold" data-m="fb" data-d="-1">Back</button>
  </div>
  <div id="msg"></div>
</div>
<script>
var $ = function(id){ return document.getElementById(id); };
var st = {on:true, b:60, t:50, cob:true, cb:100}, timer = null;

function render(){
  $('pwr').className = st.on ? 'on' : '';
  $('pwr').textContent = st.on ? 'ON  (tap to turn off)' : 'OFF  (tap to turn on)';
  $('b').value = st.b;  $('bv').textContent = st.b + '%';
  $('t').value = st.t;
  $('tv').textContent = st.t < 34 ? 'warm' : (st.t > 66 ? 'cool' : 'neutral');
  $('cpwr').className = st.cob ? 'on' : '';
  $('cpwr').textContent = st.cob ? 'COB ON  (tap to turn off)' : 'COB OFF  (tap to turn on)';
  $('cb').value = st.cb;  $('cbv').textContent = st.cb + '%';
}

function send(){
  clearTimeout(timer);
  timer = setTimeout(function(){
    fetch('/api/set?on=' + (st.on ? 1 : 0) + '&b=' + st.b + '&t=' + st.t + '&cob=' + (st.cob ? 1 : 0) + '&cb=' + st.cb)
      .then(function(){ $('msg').textContent = ''; })
      .catch(function(){ $('msg').textContent = 'Lost connection to the light'; });
  }, 60);
}

$('pwr').onclick = function(){ st.on = !st.on; render(); send(); };
$('b').oninput = function(){ st.b = +this.value; if (st.b > 0) st.on = true; render(); send(); };
$('t').oninput = function(){ st.t = +this.value; render(); send(); };
$('cpwr').onclick = function(){ st.cob = !st.cob; render(); send(); };
$('cb').oninput = function(){ st.cb = +this.value; if (st.cb > 0) st.cob = true; render(); send(); };
Array.prototype.forEach.call(document.querySelectorAll('[data-t]'), function(btn){
  btn.onclick = function(){ st.t = +btn.getAttribute('data-t'); render(); send(); };
});

var holdTimer = null;
function motor(m, d){ fetch('/api/motor?m=' + m + '&d=' + d).catch(function(){}); }
Array.prototype.forEach.call(document.querySelectorAll('.hold'), function(btn){
  var m = btn.getAttribute('data-m'), d = +btn.getAttribute('data-d');
  function start(e){ e.preventDefault(); clearInterval(holdTimer); motor(m, d);
    holdTimer = setInterval(function(){ motor(m, d); }, 150); }
  function stop(){ if (holdTimer === null) return; clearInterval(holdTimer); holdTimer = null; motor(m, 0); }
  btn.addEventListener('pointerdown', start);
  btn.addEventListener('pointerup', stop);
  btn.addEventListener('pointerleave', stop);
  btn.addEventListener('pointercancel', stop);
});

fetch('/api/state').then(function(r){ return r.json(); })
  .then(function(s){ st = s; render(); })
  .catch(function(){ $('msg').textContent = 'Could not read light state'; });
render();
</script>
</body>
</html>
)rawliteral";

void sendState() {
  String j = "{\"on\":";
  j += on ? "true" : "false";
  j += ",\"b\":";
  j += String(brightness);
  j += ",\"t\":";
  j += String(temp);
  j += ",\"cob\":";
  j += cobOn ? "true" : "false";
  j += ",\"cb\":";
  j += String(cobBrightness);
  j += "}";
  server.send(200, "application/json", j);
}

void handleRoot()  { server.send_P(200, "text/html", PAGE); }
void handleState() { sendState(); }

void handleMotor() {
  int d = server.hasArg("d") ? constrain(server.arg("d").toInt(), -1, 1) : 0;
  String m = server.hasArg("m") ? server.arg("m") : String("");
  if (m == "lift") motorRun(lift, d);
  else if (m == "fb") motorRun(fb, d);
  server.send(200, "application/json", "{\"ok\":true}");
}

void handleSet() {
  if (server.hasArg("on")) on = server.arg("on").toInt() != 0;
  if (server.hasArg("b"))  brightness = constrain(server.arg("b").toInt(), 0, 100);
  if (server.hasArg("t"))  temp = constrain(server.arg("t").toInt(), 0, 100);
  if (server.hasArg("cob")) cobOn = server.arg("cob").toInt() != 0;
  if (server.hasArg("cb"))  cobBrightness = constrain(server.arg("cb").toInt(), 0, 100);
  applyOutput();
  markChanged();
  sendState();
}

void startWiFi() {
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  Serial.print("Connecting to WiFi");
  unsigned long t0 = millis();
  unsigned long lastDot = 0;
  while (WiFi.status() != WL_CONNECTED && millis() - t0 < 15000) {
    delay(10);
    fadeUpdate();                              // the soft lights keep fading in while we wait
    if (millis() - lastDot >= 300) { Serial.print("."); lastDot = millis(); }
  }
  Serial.println();

  if (WiFi.status() == WL_CONNECTED) {
    Serial.print("Open http://");
    Serial.println(WiFi.localIP());
    if (MDNS.begin(HOST_NAME)) {
      Serial.print("or http://");
      Serial.print(HOST_NAME);
      Serial.println(".local");
    }
  } else {
    Serial.println("WiFi failed, starting access point");
    WiFi.mode(WIFI_AP);
    WiFi.softAP(AP_SSID, AP_PASS);
    Serial.print("Join WiFi '");
    Serial.print(AP_SSID);
    Serial.print("' then open http://");
    Serial.println(WiFi.softAPIP());
  }
}

void setup() {
  Serial.begin(115200);

  pwmBegin(PIN_WARM, CH_WARM);
  pwmBegin(PIN_COOL, CH_COOL);
  pwmBegin(PIN_COB, CH_COB);

  pinMode(PIN_A_IN1, OUTPUT); pinMode(PIN_A_IN2, OUTPUT);
  pinMode(PIN_B_IN1, OUTPUT); pinMode(PIN_B_IN2, OUTPUT);
  pinMode(PIN_STBY, OUTPUT);
  pwmBegin(PIN_A_PWM, CH_MA);
  pwmBegin(PIN_B_PWM, CH_MB);
  motorStopPins(lift);
  motorStopPins(fb);
  digitalWrite(PIN_STBY, HIGH);   // driver awake, motors stopped
  pwmWrite(PIN_WARM, CH_WARM, 0);
  pwmWrite(PIN_COOL, CH_COOL, 0);
  pwmWrite(PIN_COB, CH_COB, 0);

  prefs.begin("ring", false);
  on         = false;   // always start OFF; the app (or web page) turns it on
  brightness = prefs.getUChar("b", 60);
  temp       = prefs.getUChar("t", 50);
  cobOn      = true;    // COB lights always start ON with the board (and the mini PC)
  cobBrightness = prefs.getUChar("cb", 100);
  applyOutput();

  startWiFi();

  server.on("/", handleRoot);
  server.on("/api/state", handleState);
  server.on("/api/set", handleSet);
  server.on("/api/motor", handleMotor);
  server.begin();
}

void loop() {
  server.handleClient();
  fadeUpdate();
  motorUpdate(lift);
  motorUpdate(fb);

  // Save to flash 2 s after the last change (not on every slider movement)
  if (dirty && millis() - lastChange > 2000) {
    prefs.putBool("on", on);
    prefs.putUChar("b", (uint8_t)brightness);
    prefs.putUChar("t", (uint8_t)temp);
    prefs.putUChar("cb", (uint8_t)cobBrightness);
    dirty = false;
  }
}
