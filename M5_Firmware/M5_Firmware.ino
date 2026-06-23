#include <M5Unified.h>
#include <SD.h>

// ============================================================
// COLORS (RGB565 approximations)
// ============================================================
#define COLOR_BASE  0x10C4
#define COLOR_KEY   0x8C8A
#define COLOR_DIM   0x4444
#define COLOR_WARN  0xD0A4
#define COLOR_BG    0x0000
#define COLOR_OK    0x07E0
#define COLOR_CYAN  0x07FF

// ============================================================
// COMMUNICATION
// ============================================================
unsigned long bootMillis = 0;
const unsigned long HANDSHAKE_DURATION_MS = 1000;
unsigned long lastSendMillis = 0;
const unsigned long SEND_INTERVAL_MS = 20;

static char cmdBuf[64];
static int  cmdLen = 0;

// ============================================================
// UI STATE
// ============================================================
enum UIState { STATE_BOOT, STATE_MAIN };
UIState uiState = STATE_BOOT;
unsigned long stateStartMillis = 0;
int bootStep = -1;

// ============================================================
// MAIN UI cached values
// ============================================================
float lastPitch = -999.0, lastRoll = -999.0;
float lastTx = -999.0,    lastTy   = -999.0;
int   lastBtnA = -1, lastBtnB = -1, lastBtnC = -1;
unsigned long lastClockUpdate = 0;

// ============================================================
// LINK STATUS
// ============================================================
enum LinkState { LINK_STABLE, LINK_UNSTABLE, LINK_DEGRADED, LINK_LOST };
LinkState linkState = LINK_STABLE;
LinkState lastDrawnLinkState = (LinkState)-1;
unsigned long lastLinkChange = 0;
unsigned long nextLinkChangeInterval = 5000;

// ============================================================
// MISMATCH WARNING
// ============================================================
bool mismatchActive = false;
unsigned long mismatchStartMillis = 0;
unsigned long nextMismatchTime = 15000;
const unsigned long MISMATCH_DURATION_MS = 1600;
const int MISMATCH_BLINK_COUNT = 8;
bool lastMismatchVisible = false;

// ============================================================
// HACK MINIGAME
// HS: 0=IDLE 1=INIT 2=T1 3=T2 4=T3 5=TL1 6=TL2 7=TL3 8=SUCCESS 9=FAIL
// ============================================================
enum HackPhase : int {
  HP_IDLE    = 0,
  HP_INIT    = 1,
  HP_TOUCH1  = 2,
  HP_TOUCH2  = 3,
  HP_TOUCH3  = 4,
  HP_TILT1   = 5,
  HP_TILT2   = 6,
  HP_TILT3   = 7,
  HP_SUCCESS = 8,
  HP_FAIL    = 9
};

static constexpr uint32_t INIT_DURATION_MS   = 2200;
static constexpr uint32_t TOUCH1_TIMEOUT_MS  = 1800;
static constexpr uint32_t TOUCH2_TIMEOUT_MS  = 1800;
static constexpr uint32_t TOUCH3_TIMEOUT_MS  = 2000;
static constexpr uint32_t TILT_TIMEOUT_MS    = 5000;
static constexpr uint32_t TILT1_HOLD_MS      = 2000;
static constexpr uint32_t TILT2_HOLD_MS      = 2500;
static constexpr uint32_t TILT3_HOLD_MS      = 2000;
static constexpr uint32_t RESULT_HOLD_MS     = 2500;
static constexpr float    TILT1_DEG          = 15.0f;
static constexpr float    TILT2_DEG          = 10.0f;
static constexpr float    TILT3_DEG          = 18.0f;

struct HackState {
  HackPhase phase         = HP_IDLE;
  int       progress      = 0;
  bool      firstSideLeft = false;
  bool      tiltLeft      = false;
  uint32_t  holdStart     = 0;
  bool      holding       = false;
  uint32_t  stepStart     = 0;
  bool      drawDirty     = true;
} hack;

// ============================================================
// ============================================================
// SOUND SYSTEM
// ============================================================
// ============================================================

// ── WAV バッファ (PSRAM に読み込む) ─────────────────────────
static uint8_t* wavBoot        = nullptr;  static size_t wavBootSz        = 0;
static uint8_t* wavConnect     = nullptr;  static size_t wavConnectSz     = 0;
static uint8_t* wavHackStart   = nullptr;  static size_t wavHackStartSz   = 0;
static uint8_t* wavHackStep    = nullptr;  static size_t wavHackStepSz    = 0;
static uint8_t* wavSuccess     = nullptr;  static size_t wavSuccessSz     = 0;
static uint8_t* wavFail        = nullptr;  static size_t wavFailSz        = 0;

// ── Hold フィードバック音 ────────────────────────────────────
static uint32_t lastHoldBeepMs = 0;

// ── タッチ点滅 ───────────────────────────────────────────────
static uint32_t blinkTimer = 0;
static bool     blinkState = false;
static constexpr uint32_t BLINK_INTERVAL_MS = 280;

// ── チャンネル割り当て ───────────────────────────────────────
//   ch 0 : WAV 再生 (hack_start / success / fail)
//   ch 1 : UI トーン (ボタン / タッチ / ステップ clear)
//   ch 2 : shake / hold フィードバック

// WAV ファイルを SD から PSRAM/SRAM に読み込む
static void loadWav(const char* path, uint8_t** buf, size_t* sz) {
  *buf = nullptr; *sz = 0;
  if (!SD.exists(path)) {
    Serial.printf("[SND] not found: %s\n", path);
    return;
  }
  File f = SD.open(path, FILE_READ);
  if (!f) return;
  *sz = f.size();
  // PSRAM 優先 → なければ通常 heap
  *buf = (uint8_t*)heap_caps_malloc(*sz, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
  if (!*buf) *buf = (uint8_t*)malloc(*sz);
  if (*buf) {
    f.read(*buf, *sz);
    Serial.printf("[SND] loaded %s (%u B)\n", path, (unsigned)*sz);
  } else {
    *sz = 0;
    Serial.printf("[SND] OOM: %s\n", path);
  }
  f.close();
}

// WAV 再生 (ch0 / 重要イベント用)
static void sndWav(uint8_t* buf, size_t sz, uint8_t vol = 240, bool stop = true) {
  if (buf && sz) M5.Speaker.playWav(buf, sz, vol, 0, stop);
}

// トーン再生 (ch1 / UI フィードバック用)
static void sndTone(uint32_t freq, uint32_t ms, uint8_t vol = 180) {
  M5.Speaker.setAllChannelVolume(vol);
  M5.Speaker.tone(freq, ms, 1, true);
}

// Shake / Hold 用トーン (ch2)
static void sndTone2(uint32_t freq, uint32_t ms, uint8_t vol = 140) {
  M5.Speaker.tone(freq, ms, 2, true);
}

// ── ステップクリア音: 上昇アルペジオ ────────────────────────
// step = 1..6 → ピッチが段階的に上がる
static void sndStepClear(int step) {
  // WAV があれば優先
  if (wavHackStep && wavHackStepSz) {
    sndWav(wavHackStep, wavHackStepSz, 200, false);
    return;
  }
  // フォールバック: 2音アルペジオ
  const uint32_t freqs[] = { 523, 587, 659, 740, 831, 988 }; // C5→B5
  uint32_t f = freqs[constrain(step - 1, 0, 5)];
  sndTone(f, 120);
  // 2音目は少し後で鳴らしたいが delay は使えないので 1音のみ
  // (step clear は低頻度なので十分)
}


// ── Tilt ホールド中フィードバック ────────────────────────────
//   holdRatio 0→1 に連動して周波数が上昇する
static void updateHoldSound(bool holding, float holdRatio, uint32_t now) {
  if (!holding) { lastHoldBeepMs = 0; return; }
  uint32_t interval = 600 - (uint32_t)(holdRatio * 400); // 600ms → 200ms
  if (now - lastHoldBeepMs >= interval) {
    uint32_t f = 330 + (uint32_t)(holdRatio * 550); // 330Hz → 880Hz
    sndTone2(f, 60);
    lastHoldBeepMs = now;
  }
}

// Sound システム初期化 (setup() から呼ぶ)
void soundSetup() {
  M5.Speaker.begin();
  M5.Speaker.setVolume(220);

  // SD 初期化 (Core2 の CS = GPIO 4)
  if (!SD.begin(4, SPI, 25000000)) {
    Serial.println("[SND] SD init failed — WAV disabled, tones only");
    return;
  }
  loadWav("/sounds/boot.wav",         &wavBoot,      &wavBootSz);
  loadWav("/sounds/connect.wav",      &wavConnect,   &wavConnectSz);
  loadWav("/sounds/hack_start.wav",   &wavHackStart, &wavHackStartSz);
  loadWav("/sounds/hack_step.wav",    &wavHackStep,  &wavHackStepSz);
  loadWav("/sounds/hack_success.wav", &wavSuccess,   &wavSuccessSz);
  loadWav("/sounds/hack_fail.wav",    &wavFail,      &wavFailSz);
  Serial.println("[SND] Sound system ready");
}

// ============================================================
// HELPERS
// ============================================================
void drawHLine(int y, uint16_t color) {
  M5.Display.drawLine(10, y, 310, y, color);
}
void drawText(int x, int y, const char* text, uint16_t color, int size) {
  M5.Display.setTextColor(color, COLOR_BG);
  M5.Display.setTextSize(size);
  M5.Display.setCursor(x, y);
  M5.Display.print(text);
}
void clearRegion(int x, int y, int w, int h) {
  M5.Display.fillRect(x, y, w, h, COLOR_BG);
}
void drawBar(int x, int y, int w, int h, float ratio, uint16_t color) {
  ratio = constrain(ratio, 0.0f, 1.0f);
  M5.Display.fillRect(x, y, w, h, COLOR_DIM);
  int fw = (int)(ratio * w);
  if (fw > 0) M5.Display.fillRect(x, y, fw, h, color);
  M5.Display.drawRect(x-1, y-1, w+2, h+2, COLOR_KEY);
}

// ============================================================
// SERIAL COMMAND READER
// ============================================================
void readSerialCmd() {
  while (Serial.available()) {
    char c = Serial.read();
    if (c == '\n' || c == '\r') {
      if (cmdLen > 0) {
        cmdBuf[cmdLen] = '\0';
        if      (strcmp(cmdBuf, "CMD,HACK_START") == 0) startHack();
        else if (strcmp(cmdBuf, "CMD,CONNECTED") == 0)  onUnityConnected();
        cmdLen = 0;
      }
    } else {
      if (cmdLen < (int)sizeof(cmdBuf) - 1)
        cmdBuf[cmdLen++] = c;
    }
  }
}

// ============================================================
// HACK UI
// ============================================================

void drawHackInitUI(uint32_t elapsed, bool dirty) {
  if (dirty) {
    M5.Display.fillScreen(COLOR_BG);
    drawText(10, 8, "SECURITY BYPASS", COLOR_KEY, 2);
    drawHLine(30, COLOR_DIM);
    drawText(10, 50, "> SCANNING LAYER 0...", COLOR_DIM, 1);
    drawText(10, 65, "> ANALYZING FIREWALL...", COLOR_DIM, 1);
    drawText(10, 80, "> INJECTING PAYLOAD...", COLOR_DIM, 1);
    drawText(10, 120, "BYPASS PROGRESS", COLOR_DIM, 1);
  }
  float ratio = constrain((float)elapsed / INIT_DURATION_MS, 0.0f, 1.0f);
  drawBar(10, 140, 300, 20, ratio, COLOR_CYAN);
  clearRegion(130, 170, 60, 18);
  char buf[16];
  snprintf(buf, sizeof(buf), "%3d%%", (int)(ratio * 100));
  drawText(130, 170, buf, COLOR_KEY, 2);
  if ((elapsed / 300) % 2 == 0) {
    drawText(10, 210, "! COUNTERMEASURE ACTIVE !", COLOR_WARN, 1);
  } else {
    clearRegion(10, 210, 300, 12);
  }
}

void drawHackTouchSideUI(int stepNum, bool isStep2, bool blink) {
  M5.Display.fillScreen(COLOR_BG);
  char buf[32];
  snprintf(buf, sizeof(buf), "STEP %d/6 - TOUCH", stepNum);
  drawText(10, 8, buf, COLOR_KEY, 2);
  drawHLine(30, COLOR_DIM);
  M5.Display.drawFastVLine(160, 40, 155, COLOR_KEY);

  bool needLeft  = !isStep2 || (isStep2 && !hack.firstSideLeft);
  bool needRight = !isStep2 || (isStep2 &&  hack.firstSideLeft);

  // 押すべき側: blink でオン(CYAN)/オフ(背景色=不可視)
  uint16_t colLeft  = needLeft  ? (blink ? COLOR_CYAN : COLOR_BG) : COLOR_DIM;
  uint16_t colRight = needRight ? (blink ? COLOR_CYAN : COLOR_BG) : COLOR_DIM;

  drawText(25,  105, "<<<", colLeft,  3);
  drawText(180, 105, ">>>", colRight, 3);

  drawText(30,  70, needLeft  ? "PRESS" : "",  needLeft  ? COLOR_KEY : COLOR_BG, 1);
  drawText(185, 70, needRight ? "PRESS" : "", needRight ? COLOR_KEY : COLOR_BG, 1);

  drawText(10, 200, "TIME", COLOR_DIM, 1);
  drawBar(50, 198, 250, 14, 1.0f, COLOR_CYAN);
}

void drawHackTouchCenterUI(bool blink) {
  M5.Display.fillScreen(COLOR_BG);
  drawText(10, 8, "STEP 3/6 - TOUCH", COLOR_KEY, 2);
  drawHLine(30, COLOR_DIM);

  uint16_t boxCol = blink ? COLOR_CYAN : COLOR_BG;
  M5.Display.fillRect(88, 50, 144, 140, COLOR_BG);
  M5.Display.drawRect(88, 50, 144, 140, boxCol);
  drawText(100, 100, "PRESS", boxCol, 2);
  drawText(100, 120, "HERE",  boxCol, 2);

  drawText(10, 200, "TIME", COLOR_DIM, 1);
  drawBar(50, 198, 250, 14, 1.0f, COLOR_CYAN);
}

void drawHackTiltUI(int stepNum, float threshDeg,
                    bool holding, uint32_t holdMs,
                    uint32_t elapsed, uint32_t timeout,
                    uint32_t holdRequired, float currentRoll) {
  M5.Display.fillScreen(COLOR_BG);
  char buf[32];
  snprintf(buf, sizeof(buf), "STEP %d/6 - TILT", stepNum + 3);
  drawText(10, 8, buf, COLOR_KEY, 2);
  drawHLine(30, COLOR_DIM);
  const char* dir = hack.tiltLeft ? "<<  TILT LEFT" : "TILT RIGHT  >>";
  drawText(20, 55, dir, COLOR_CYAN, 2);
  snprintf(buf, sizeof(buf), "NEED: %.0f deg", threshDeg);
  drawText(20, 85, buf, COLOR_KEY, 2);
  snprintf(buf, sizeof(buf), "NOW : %+.1f deg", currentRoll);
  bool inZone = hack.tiltLeft ? (currentRoll <= -threshDeg) : (currentRoll >= threshDeg);
  drawText(20, 110, buf, inZone ? COLOR_OK : COLOR_WARN, 2);
  float holdRatio = holding
    ? constrain((float)holdMs / (float)holdRequired, 0.0f, 1.0f) : 0.0f;
  drawText(10, 148, "HOLD", COLOR_DIM, 1);
  drawBar(55, 146, 245, 18, holdRatio, holding ? COLOR_OK : COLOR_DIM);
  float timeRatio = 1.0f - constrain((float)elapsed / (float)timeout, 0.0f, 1.0f);
  drawText(10, 200, "TIME", COLOR_DIM, 1);
  drawBar(50, 198, 250, 14, timeRatio, COLOR_CYAN);
}

void drawHackResult(bool success) {
  M5.Display.fillScreen(success ? (uint16_t)0x0320 : (uint16_t)0x3000);
  M5.Display.setTextSize(3);
  uint16_t col = success ? COLOR_OK : COLOR_WARN;
  drawText(40, 85,  success ? "ACCESS"    : "INTRUSION", col, 3);
  drawText(40, 125, success ? "GRANTED"   : "DETECTED",  col, 3);
}

// ============================================================
// HACK STATE MACHINE
// ============================================================
void onUnityConnected() {
  // Unity接続音: WAV優先 → なければ上昇トーン
  if (wavConnect && wavConnectSz) {
    sndWav(wavConnect, wavConnectSz, 220, true);
  } else {
    sndTone(330, 60); delay(70);
    sndTone(550, 60); delay(70);
    sndTone(880, 150);
  }
}

void startHack() {
  if (hack.phase != HP_IDLE) return;
  hack.phase     = HP_INIT;
  hack.progress  = 0;
  hack.stepStart = millis();
  hack.drawDirty = true;

  // ── INIT 開始音: WAV があれば再生、なければドラマチックなトーン列 ──
  if (wavHackStart && wavHackStartSz) {
    sndWav(wavHackStart, wavHackStartSz, 255, true);
  } else {
    // フォールバック: 下降アルペジオ (hack 感)
    M5.Speaker.tone(880, 80, 1, true);
    delay(90);
    M5.Speaker.tone(660, 80, 1, true);
    delay(90);
    M5.Speaker.tone(440, 200, 1, true);
  }
}

void updateHack(float roll) {
  uint32_t now     = millis();
  uint32_t elapsed = now - hack.stepStart;

  auto  touch  = M5.Touch.getDetail();
  float tx_raw = touch.isPressed() ? (float)touch.x / 320.0f : -1.0f;

  switch (hack.phase) {

  case HP_INIT:
    drawHackInitUI(elapsed, hack.drawDirty);
    hack.drawDirty = false;
    hack.progress = (int)(constrain((float)elapsed / INIT_DURATION_MS, 0.f, 1.f) * 10);
    if (elapsed >= INIT_DURATION_MS) {
      hack.progress  = 0;
      hack.phase     = HP_TOUCH1;
      hack.stepStart = now;
      hack.drawDirty = true;
      // TOUCH1 開始の警告音
      sndTone(1047, 60); // C6 ピッ
    }
    break;

  case HP_TOUCH1:
    // 点滅更新
    if (now - blinkTimer >= BLINK_INTERVAL_MS) {
      blinkTimer = now; blinkState = !blinkState;
      drawHackTouchSideUI(1, false, blinkState);
    } else if (hack.drawDirty) {
      drawHackTouchSideUI(1, false, blinkState); hack.drawDirty = false;
    }
    drawBar(50, 198, 250, 14,
      1.0f - constrain((float)elapsed / TOUCH1_TIMEOUT_MS, 0.f, 1.f), COLOR_CYAN);
    if (elapsed > TOUCH1_TIMEOUT_MS) {
      hack.phase = HP_FAIL; hack.drawDirty = true;
      sndTone(150, 400);
      break;
    }
    if (touch.wasPressed()) {
      hack.firstSideLeft = (tx_raw < 0.5f);
      hack.progress = 17;
      hack.phase = HP_TOUCH2; hack.stepStart = now; hack.drawDirty = true;
      blinkTimer = 0;
      sndStepClear(1);
    }
    break;

  case HP_TOUCH2:
    if (now - blinkTimer >= BLINK_INTERVAL_MS) {
      blinkTimer = now; blinkState = !blinkState;
      drawHackTouchSideUI(2, true, blinkState);
    } else if (hack.drawDirty) {
      drawHackTouchSideUI(2, true, blinkState); hack.drawDirty = false;
    }
    drawBar(50, 198, 250, 14,
      1.0f - constrain((float)elapsed / TOUCH2_TIMEOUT_MS, 0.f, 1.f), COLOR_CYAN);
    if (elapsed > TOUCH2_TIMEOUT_MS) {
      hack.phase = HP_FAIL; hack.drawDirty = true;
      sndTone(150, 400);
      break;
    }
    if (touch.wasPressed()) {
      bool isLeft  = (tx_raw < 0.5f);
      if (isLeft != hack.firstSideLeft) {
        hack.progress = 33;
        hack.phase = HP_TOUCH3; hack.stepStart = now; hack.drawDirty = true;
        blinkTimer = 0;
        sndStepClear(2);
      } else {
        hack.phase = HP_FAIL; hack.drawDirty = true;
        sndTone(150, 400);
      }
    }
    break;

  case HP_TOUCH3:
    if (now - blinkTimer >= BLINK_INTERVAL_MS) {
      blinkTimer = now; blinkState = !blinkState;
      drawHackTouchCenterUI(blinkState);
    } else if (hack.drawDirty) {
      drawHackTouchCenterUI(blinkState); hack.drawDirty = false;
    }
    drawBar(50, 198, 250, 14,
      1.0f - constrain((float)elapsed / TOUCH3_TIMEOUT_MS, 0.f, 1.f), COLOR_CYAN);
    if (elapsed > TOUCH3_TIMEOUT_MS) {
      hack.phase = HP_FAIL; hack.drawDirty = true;
      sndTone(150, 400);
      break;
    }
    if (touch.wasPressed()) {
      if (tx_raw >= 0.27f && tx_raw <= 0.73f) {
        hack.progress  = 50;
        hack.phase     = HP_TILT1;
        hack.stepStart = now;
        hack.holding   = false; hack.holdStart = 0;
        hack.tiltLeft  = (random(0, 2) == 0);
        hack.drawDirty = true;
        blinkTimer = 0;
        sndStepClear(3);
      } else {
        hack.phase = HP_FAIL; hack.drawDirty = true;
        sndTone(150, 400);
      }
    }
    break;

  case HP_TILT1:
  case HP_TILT2:
  case HP_TILT3: {
    float    threshDeg;
    uint32_t holdRequired;
    int      baseProgress, nextProgress;
    HackPhase nextPhase;
    int       stepNum;

    if      (hack.phase == HP_TILT1) { threshDeg=TILT1_DEG; holdRequired=TILT1_HOLD_MS; baseProgress=50; nextProgress=67; nextPhase=HP_TILT2; stepNum=1; }
    else if (hack.phase == HP_TILT2) { threshDeg=TILT2_DEG; holdRequired=TILT2_HOLD_MS; baseProgress=67; nextProgress=83; nextPhase=HP_TILT3; stepNum=2; }
    else                              { threshDeg=TILT3_DEG; holdRequired=TILT3_HOLD_MS; baseProgress=83; nextProgress=100;nextPhase=HP_SUCCESS; stepNum=3; }

    if (elapsed > TILT_TIMEOUT_MS) {
      hack.phase = HP_FAIL; hack.drawDirty = true;
      sndTone(150, 500);
      break;
    }

    bool inZone = hack.tiltLeft ? (roll <= -threshDeg) : (roll >= threshDeg);

    if (inZone && !hack.holding) {
      hack.holding = true; hack.holdStart = now; hack.drawDirty = true;
      sndTone2(440, 80); // ゾーン IN 確認音
    } else if (!inZone && hack.holding) {
      hack.holding = false; hack.holdStart = 0; hack.drawDirty = true;
      sndTone2(220, 60); // ゾーン OUT
    }

    if (hack.holding) {
      uint32_t heldMs   = now - hack.holdStart;
      float    holdRatio = constrain((float)heldMs / holdRequired, 0.0f, 1.0f);
      int add = (int)(holdRatio * 17.0f);
      hack.progress = min(baseProgress + add, nextProgress);

      // ホールド進行中の上昇音フィードバック
      updateHoldSound(true, holdRatio, now);

      if (heldMs >= holdRequired) {
        hack.progress  = nextProgress;
        hack.phase     = nextPhase;
        hack.stepStart = now;
        hack.holding   = false; hack.holdStart = 0;
        if (nextPhase != HP_SUCCESS) hack.tiltLeft = (random(0, 2) == 0);
        hack.drawDirty = true;
        lastHoldBeepMs = 0;
        sndStepClear(stepNum + 3);
        break;
      }
    } else {
      updateHoldSound(false, 0.0f, now);
    }

    drawHackTiltUI(
      hack.phase - HP_TILT1 + 1, threshDeg,
      hack.holding, hack.holding ? (now - hack.holdStart) : 0,
      elapsed, TILT_TIMEOUT_MS, holdRequired, roll
    );
    break;
  }

  case HP_SUCCESS:
    if (hack.drawDirty) {
      drawHackResult(true);
      hack.drawDirty = false;
      // 成功音: WAV 優先
      if (wavSuccess && wavSuccessSz) {
        sndWav(wavSuccess, wavSuccessSz, 255, true);
      } else {
        // フォールバック: 上昇ファンファーレ
        M5.Speaker.tone(523, 100, 1, true); delay(110);
        M5.Speaker.tone(659, 100, 1, true); delay(110);
        M5.Speaker.tone(784, 100, 1, true); delay(110);
        M5.Speaker.tone(1047,300, 1, true);
      }
    }
    if (elapsed > RESULT_HOLD_MS) { hack.phase = HP_IDLE; hack.progress = 0; drawMainStaticUI(); }
    break;

  case HP_FAIL:
    if (hack.drawDirty) {
      drawHackResult(false);
      hack.drawDirty = false;
      // 失敗音: WAV 優先
      if (wavFail && wavFailSz) {
        sndWav(wavFail, wavFailSz, 255, true);
      } else {
        // フォールバック: 下降ブザー
        M5.Speaker.tone(300, 150, 1, true); delay(160);
        M5.Speaker.tone(200, 150, 1, true); delay(160);
        M5.Speaker.tone(120, 400, 1, true);
      }
    }
    if (elapsed > RESULT_HOLD_MS) { hack.phase = HP_IDLE; hack.progress = 0; drawMainStaticUI(); }
    break;

  default: break;
  }
}

// ============================================================
// LINK STATUS / MISMATCH / BOOT
// ============================================================
void updateLinkState(unsigned long now) {
  if (linkState == LINK_LOST) {
    if (now - lastLinkChange >= 1500) { linkState=LINK_STABLE; lastLinkChange=now; nextLinkChangeInterval=1500+random(3000); }
    return;
  }
  if (now - lastLinkChange < nextLinkChangeInterval) return;
  lastLinkChange = now; nextLinkChangeInterval = 1500 + random(3000);
  int r = random(100);
  if      (r < 55) linkState = LINK_STABLE;
  else if (r < 75) linkState = LINK_UNSTABLE;
  else if (r < 85) linkState = LINK_DEGRADED;
  else             linkState = LINK_LOST;
}
void renderLinkStatus() {
  if (linkState == lastDrawnLinkState) return;
  lastDrawnLinkState = linkState;
  clearRegion(100, 72, 200, 18);
  const char* text; uint16_t color;
  switch (linkState) {
    case LINK_STABLE:   text="STABLE";   color=COLOR_KEY;  break;
    case LINK_UNSTABLE: text="UNSTABLE"; color=COLOR_DIM;  break;
    case LINK_DEGRADED: text="DEGRADED"; color=COLOR_DIM;  break;
    case LINK_LOST:     text="LOST";     color=COLOR_WARN; break;
  }
  drawText(100, 72, text, color, 2);
}
void updateMismatchState(unsigned long now) {
  if (mismatchActive) {
    if (now - mismatchStartMillis >= MISMATCH_DURATION_MS) { mismatchActive=false; nextMismatchTime=now+10000+random(10000); lastMismatchVisible=true; }
  } else { if (now >= nextMismatchTime) { mismatchActive=true; mismatchStartMillis=now; } }
}
void renderMismatchWarning(unsigned long now) {
  bool show = false;
  if (mismatchActive) { unsigned long e=now-mismatchStartMillis; unsigned long p=MISMATCH_DURATION_MS/MISMATCH_BLINK_COUNT; show=(e/p)%2==0; }
  if (show == lastMismatchVisible) return;
  lastMismatchVisible = show;
  clearRegion(180, 8, 130, 12);
  if (show) drawText(180, 8, "! OBS MISMATCH", COLOR_WARN, 1);
  else if (!mismatchActive) drawText(240, 8, "v1.2", COLOR_DIM, 1);
}
void renderBoot(unsigned long elapsed) {
  // 音: 500ms開始、4秒 → 4500msで終了
  // 各ステップを500ms〜4500msに均等分散 (約700msごと)
  int s=0;
  if      (elapsed>=500  && elapsed<1200) s=1;
  else if (elapsed>=1200 && elapsed<1900) s=2;
  else if (elapsed>=1900 && elapsed<2600) s=3;
  else if (elapsed>=2600 && elapsed<3300) s=4;
  else if (elapsed>=3300 && elapsed<4000) s=5;
  else if (elapsed>=4000 && elapsed<4600) s=6;
  else if (elapsed>=4600)                 s=99;
  if (s==bootStep) return; bootStep=s;
  switch(bootStep){
    case 1:
      drawText(20,30,"M5 TERMINAL",COLOR_KEY,3);
      // 起動音: WAV優先 → なければトーン列
      if (wavBoot && wavBootSz) {
        sndWav(wavBoot, wavBootSz, 220, true);
      } else {
        sndTone(220, 80); delay(90);
        sndTone(330, 80); delay(90);
        sndTone(440, 80); delay(90);
        sndTone(660, 200);
      }
      break;
    case 2: drawText(20,75,"v1.2 / BOOT",COLOR_DIM,2); break;
    case 3: drawText(20,110,"> MEM CHECK...",COLOR_KEY,2); break;
    case 4: clearRegion(20,110,280,20); drawText(20,110,"> MEM OK",COLOR_KEY,2); break;
    case 5: drawText(20,140,"! OBSERVER MISMATCH",COLOR_WARN,2); break;
    case 6: drawText(20,170,"> PROCEEDING...",COLOR_DIM,2); break;
    case 99: M5.Speaker.stop(0); uiState=STATE_MAIN; stateStartMillis=millis(); drawMainStaticUI(); Serial.println("BOOT_DONE"); break;
  }
}

// ============================================================
// MAIN UI
// ============================================================
void drawMainStaticUI() {
  M5.Display.fillScreen(COLOR_BG);
  drawText(10,5,"M5 TERMINAL",COLOR_KEY,2); drawHLine(25,COLOR_DIM);
  drawText(10,32,"> UNIT-M5 ONLINE",COLOR_KEY,2);
  drawText(10,52,"> SUBJECT: ########",COLOR_DIM,2);
  drawText(10,72,"> LINK:",COLOR_DIM,2); drawHLine(95,COLOR_DIM);
  drawText(10,104,"PITCH:",COLOR_DIM,2); drawText(10,124,"ROLL :",COLOR_DIM,2);
  drawText(10,148,"TX/TY:",COLOR_DIM,2); drawText(10,168,"BTN  :",COLOR_DIM,2);
  drawHLine(190,COLOR_DIM);
  drawText(10,198,"TX 50Hz",COLOR_DIM,1); drawText(85,198,"##-##-####",COLOR_DIM,1);
  lastPitch=lastRoll=lastTx=lastTy=-999.0; lastBtnA=lastBtnB=lastBtnC=-1;
  lastDrawnLinkState=(LinkState)-1; renderLinkStatus();
  lastMismatchVisible=true; mismatchActive=false;
  nextMismatchTime=millis()+10000+random(10000); renderMismatchWarning(millis());
}
void renderMain(float pitch, float roll, float tx, float ty, int bA, int bB, int bC) {
  if (abs(pitch-lastPitch)>=0.1) { clearRegion(100,104,100,18); char b[16]; snprintf(b,16,"%+06.1f",pitch); drawText(100,104,b,COLOR_KEY,2); lastPitch=pitch; }
  if (abs(roll-lastRoll)>=0.1)   { clearRegion(100,124,100,18); char b[16]; snprintf(b,16,"%+06.1f",roll);  drawText(100,124,b,COLOR_KEY,2); lastRoll=roll; }
  if (abs(tx-lastTx)>=0.01||abs(ty-lastTy)>=0.01) {
    clearRegion(100,148,200,18); char b[20];
    if(tx<0) snprintf(b,20,"-.--/-.--"); else snprintf(b,20,"%.2f/%.2f",tx,ty);
    drawText(100,148,b,COLOR_KEY,2); lastTx=tx; lastTy=ty;
  }
  if (bA!=lastBtnA||bB!=lastBtnB||bC!=lastBtnC) {
    clearRegion(100,168,200,18); char b[12];
    snprintf(b,12,"%c %c %c",bA?'A':'-',bB?'B':'-',bC?'C':'-');
    drawText(100,168,b,COLOR_KEY,2); lastBtnA=bA; lastBtnB=bB; lastBtnC=bC;
  }
  unsigned long now=millis();
  if (now-lastClockUpdate>=1000) {
    lastClockUpdate=now;
    unsigned long up=(now-stateStartMillis)/1000;
    clearRegion(160,198,100,12); char b[16];
    snprintf(b,16,"%02d:%02d:%02d",(int)(up/3600)%100,(int)(up/60)%60,(int)(up%60));
    drawText(160,198,b,COLOR_DIM,1);
  }
}

// ============================================================
// SETUP / LOOP
// ============================================================
void setup() {
  auto cfg = M5.config();
  M5.begin(cfg);
  M5.Imu.init();
  Serial.begin(115200);
  randomSeed(micros());
  M5.Display.fillScreen(COLOR_BG);

  // サウンドシステム初期化
  soundSetup();

  bootMillis=stateStartMillis=millis(); uiState=STATE_BOOT; bootStep=-1;
}

void loop() {
  M5.update();
  unsigned long now = millis();

  if (now - bootMillis < HANDSHAKE_DURATION_MS) {
    static unsigned long lastHS = 0;
    if (now-lastHS >= 100) { Serial.println("M5_HANDSHAKE_v1"); lastHS=now; }
  }

  readSerialCmd();

  float ax, ay, az;
  M5.Imu.getAccel(&ax, &ay, &az);
  float pitch = atan2(-ax, sqrt(ay*ay+az*az)) * 180.0 / PI;
  float roll  = atan2(ay, az) * 180.0 / PI;

  float tx=-1.0, ty=-1.0;
  auto t = M5.Touch.getDetail();
  if (t.isPressed()) { tx=(float)t.x/320.0; ty=(float)t.y/240.0; }

  int btnA=M5.BtnA.isPressed()?1:0;
  int btnB=M5.BtnB.isPressed()?1:0;
  int btnC=M5.BtnC.isPressed()?1:0;

  // ── ボタン音 (立ち上がりエッジのみ) ─────────────────────────
  bool pressedA = M5.BtnA.wasPressed();
  bool pressedB = M5.BtnB.wasPressed();
  bool pressedC = M5.BtnC.wasPressed();
  if (pressedA) sndTone(330, 65);
  if (pressedB) sndTone(500, 65);
  if (pressedC) sndTone(330, 65);


  if (hack.phase != HP_IDLE) updateHack(pitch);

  if (now-bootMillis>=HANDSHAKE_DURATION_MS && now-lastSendMillis>=SEND_INTERVAL_MS) {
    lastSendMillis=now;
    Serial.print("D,");
    Serial.print(pitch,2); Serial.print(",");
    Serial.print(roll, 2); Serial.print(",");
    Serial.print(ax,   3); Serial.print(",");
    Serial.print(ay,   3); Serial.print(",");
    Serial.print(az,   3); Serial.print(",");
    Serial.print(tx,   3); Serial.print(",");
    Serial.print(ty,   3); Serial.print(",");
    Serial.print(btnA);    Serial.print(",");
    Serial.print(btnB);    Serial.print(",");
    Serial.print(btnC);    Serial.print(",");
    Serial.print((int)hack.phase); Serial.print(",");
    Serial.println(hack.progress);
  }

  if (uiState == STATE_BOOT) {
    renderBoot(now - stateStartMillis);
  } else if (hack.phase == HP_IDLE) {
    updateLinkState(now); renderLinkStatus();
    updateMismatchState(now); renderMismatchWarning(now);
    renderMain(pitch, roll, tx, ty, btnA, btnB, btnC);
  }
}
