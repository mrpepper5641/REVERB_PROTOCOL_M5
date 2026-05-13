# REVERB PROTOCOL — M5 端末デモ 仕様書 v2

最終更新：2026-05-07 ステータス：技術骨格完成 \+ ポストエフェクト \+ M5側UI \+ 入力マッピング（Pitch/Roll/Accel）まで実装済み

---

## 0\. 絶対ルール

RULE 01：M5 Stack Core2 を使用する

RULE 02：ジオメトリシェーダー（GS）を使用する

---

## 1\. 作品コンセプト

### 1.1 ゲーム本編との関係

REVERB PROTOCOL（本編）：

- 深海ステーションが原因不明の事故に遭遇  
- 主人公は強制的な意識転送によりサブ用の肉体で覚醒  
- 記憶が一部欠落している  
- 事故原因の解明と脱出を目指す

本作（M5端末デモ）：

- 本編内のコア機構を切り出した実装  
- 各乗組員に支給される携帯端末「M5」のデモンストレーション  
- 主人公が同僚（既に死亡または行方不明）のM5に接続して記録を読む

### 1.2 装置の劇中ロジック

- M5は乗組員全員に支給される個人観測端末  
- 主人公は自分のM5を所持しているが、サブボディで目覚めたため認証情報が一致しない  
- M5本体の機能は使えるが、自分のログ・権限・各種アクセス情報が紐付いていない  
- 実質的に「初期化された状態に近い」端末を持っている  
- 他者のPCや他者のM5に接続することで、権限を得たり情報を得ていく  
- 装置は持ち主以外の接続を異常として検知する → `OBSERVER MISMATCH` 警告  
- ただし破損のため拒絶機構が機能せず、読めてしまう

### 1.3 体験の核

「他人の記録を、不完全な装置を通して、不完全な記憶の主人公が覗く」三重の不確かさ。 鮮明化（答えが見える瞬間）は採用しない。観測者は最後まで断片しか手にできない。

---

## 2\. 世界観・ビジュアル設定

### 2.1 カラーパレット

| 用途 | カラー | RGB565近似 |
| :---- | :---- | :---- |
| ベース背景 | `#0E2626` | 0x10C4 |
| メインUI／グラフィック | `#8B9750` | 0x8C8A |
| 警告 | `#D01320` | 0xD0A4 |
| サブデュード | \- | 0x4444 |
| 背景黒 | \- | 0x0000 |

### 2.2 装置の物理的扱い方

「画面を上に向けて水平に持つ」（テーブル置きのイメージ）

---

## 3\. 入力マッピング（実装状況）

| 入力 | 役割 | 状態 |
| :---- | :---- | :---- |
| IMU pitch（前後傾き） | Plane 押し出し量（高さ） | ✅ 実装済み |
| IMU roll（左右傾き） | Plane 全体の回転 | ✅ 実装済み |
| IMU 加速度（衝撃） | Ghost Offset（左右複製） | ✅ 実装済み |
| タッチX/Y | 未割当 | ❌ |
| ボタン A | 観測モード切替（点群／面／ワイヤー） | ❌ |
| ボタン B | 時系列スクラブ（要決定） | ❌ |
| ボタン C | リセット | ❌ |

---

## 4\. 技術スタック

| 層 | 使用技術 |
| :---- | :---- |
| マイコン | Arduino IDE 2.3.x \+ M5Unified ライブラリ |
| ボード定義 | M5Stack 3.3.7 |
| ホスト | Unity 6 LTS（6000.4.2f1） |
| レンダリング | Built-In Render Pipeline |
| API互換性 | .NET Framework |
| エディタ | VS Code \+ C\# Dev Kit \+ Unity 拡張 |
| シェーダー | ShaderLab \+ HLSL（target 4.0） |

---

## 5\. プロジェクト構成

REVERB\_PROTOCOL\_M5/

└── Assets/

    ├── Scenes/

    │   └── Main.unity

    ├── Scripts/

    │   ├── M5Reader.cs

    │   ├── M5VisualController.cs

    │   └── PostEffect.cs

    ├── Shaders/

    │   ├── M5Visual.shader

    │   └── PostEffect.shader

    └── Materials/

        └── M5Visual\_Mat.mat

シーン構成：

Main (シーン)

├── Main Camera        ← PostEffect.cs アタッチ、PostShader 参照

├── Directional Light

├── M5Reader           ← 空のGameObject、M5Reader.cs アタッチ

└── Plane              ← M5Visual\_Mat 適用、M5VisualController.cs アタッチ

---

## 6\. 通信仕様

- USB Serial（COM ポート、自動検出）  
- ボーレート：115200  
- 起動時：1秒間 `M5_HANDSHAKE_v1\n` を100msごとに送信  
- 以降：CSV形式で50Hz送信

CSV：

D,P,R,AX,AY,AZ,TX,TY,A,B,C\\n

---

## 7\. 実装済みコード

### 7.1 M5側スケッチ（Arduino IDE）

軍用機材風UI完成版。以下を含む：

- ブートシーケンス（4.5秒、M5 TERMINAL → MEM CHECK → OBSERVER MISMATCH → PROCEEDING）  
- メイン画面（ヘッダ、ステータス、センサー値、ボタン状態、フッタ）  
- LINK ステータスの動的変動（4状態、確率配分 55/20/10/15、間隔 1.5〜4.5秒）  
- OBSERVER MISMATCH の点滅警告（10〜20秒ごと、1.6秒間、8回点滅）  
- 経過時間カウンタ \+ 伏字日付  
- 50Hz でのシリアル送信（UI描画と独立）

\#include \<M5Unified.h\>

// COLORS

\#define COLOR\_BASE  0x10C4

\#define COLOR\_KEY   0x8C8A

\#define COLOR\_DIM   0x4444

\#define COLOR\_WARN  0xD0A4

\#define COLOR\_BG    0x0000

// COMMUNICATION

unsigned long bootMillis \= 0;

const unsigned long HANDSHAKE\_DURATION\_MS \= 1000;

unsigned long lastSendMillis \= 0;

const unsigned long SEND\_INTERVAL\_MS \= 20;

// UI STATE

enum UIState { STATE\_BOOT, STATE\_MAIN };

UIState uiState \= STATE\_BOOT;

unsigned long stateStartMillis \= 0;

int bootStep \= \-1;

// CACHED VALUES

float lastPitch \= \-999.0;

float lastRoll  \= \-999.0;

float lastTx    \= \-999.0;

float lastTy    \= \-999.0;

int   lastBtnA  \= \-1;

int   lastBtnB  \= \-1;

int   lastBtnC  \= \-1;

unsigned long lastClockUpdate \= 0;

// LINK STATE

enum LinkState { LINK\_STABLE, LINK\_UNSTABLE, LINK\_DEGRADED, LINK\_LOST };

LinkState linkState \= LINK\_STABLE;

LinkState lastDrawnLinkState \= (LinkState)-1;

unsigned long lastLinkChange \= 0;

unsigned long nextLinkChangeInterval \= 5000;

// MISMATCH WARNING STATE

bool mismatchActive \= false;

unsigned long mismatchStartMillis \= 0;

unsigned long nextMismatchTime \= 15000;

const unsigned long MISMATCH\_DURATION\_MS \= 1600;

const int MISMATCH\_BLINK\_COUNT \= 8;

bool lastMismatchVisible \= false;

// HELPERS

void drawHLine(int y, uint16\_t color) {

  M5.Display.drawLine(10, y, 310, y, color);

}

void drawText(int x, int y, const char\* text, uint16\_t color, int size) {

  M5.Display.setTextColor(color, COLOR\_BG);

  M5.Display.setTextSize(size);

  M5.Display.setCursor(x, y);

  M5.Display.print(text);

}

void clearRegion(int x, int y, int w, int h) {

  M5.Display.fillRect(x, y, w, h, COLOR\_BG);

}

// LINK STATUS LOGIC

void updateLinkState(unsigned long now) {

  if (linkState \== LINK\_LOST) {

    if (now \- lastLinkChange \>= 1500\) {

      linkState \= LINK\_STABLE;

      lastLinkChange \= now;

      nextLinkChangeInterval \= 1500 \+ random(3000);

    }

    return;

  }

  if (now \- lastLinkChange \< nextLinkChangeInterval) return;

  lastLinkChange \= now;

  nextLinkChangeInterval \= 1500 \+ random(3000);

  int r \= random(100);

  if      (r \< 55\) linkState \= LINK\_STABLE;

  else if (r \< 75\) linkState \= LINK\_UNSTABLE;

  else if (r \< 85\) linkState \= LINK\_DEGRADED;

  else             linkState \= LINK\_LOST;

}

void renderLinkStatus() {

  if (linkState \== lastDrawnLinkState) return;

  lastDrawnLinkState \= linkState;

  clearRegion(100, 72, 200, 18);

  const char\* text;

  uint16\_t color;

  switch (linkState) {

    case LINK\_STABLE:   text \= "STABLE";   color \= COLOR\_KEY;  break;

    case LINK\_UNSTABLE: text \= "UNSTABLE"; color \= COLOR\_DIM;  break;

    case LINK\_DEGRADED: text \= "DEGRADED"; color \= COLOR\_DIM;  break;

    case LINK\_LOST:     text \= "LOST";     color \= COLOR\_WARN; break;

  }

  drawText(100, 72, text, color, 2);

}

// MISMATCH WARNING LOGIC

void updateMismatchState(unsigned long now) {

  if (mismatchActive) {

    if (now \- mismatchStartMillis \>= MISMATCH\_DURATION\_MS) {

      mismatchActive \= false;

      nextMismatchTime \= now \+ 10000 \+ random(10000);

      lastMismatchVisible \= true;

    }

  } else {

    if (now \>= nextMismatchTime) {

      mismatchActive \= true;

      mismatchStartMillis \= now;

    }

  }

}

void renderMismatchWarning(unsigned long now) {

  bool shouldBeVisible \= false;

  if (mismatchActive) {

    unsigned long elapsed \= now \- mismatchStartMillis;

    unsigned long periodMs \= MISMATCH\_DURATION\_MS / MISMATCH\_BLINK\_COUNT;

    int phase \= (elapsed / periodMs) % 2;

    shouldBeVisible \= (phase \== 0);

  }

  if (shouldBeVisible \== lastMismatchVisible) return;

  lastMismatchVisible \= shouldBeVisible;

  clearRegion(180, 8, 130, 12);

  if (shouldBeVisible) {

    drawText(180, 8, "\! OBS MISMATCH", COLOR\_WARN, 1);

  } else {

    if (\!mismatchActive) {

      drawText(240, 8, "v1.2", COLOR\_DIM, 1);

    }

  }

}

// BOOT

void renderBoot(unsigned long elapsed) {

  int desiredStep \= 0;

  if      (elapsed \>=  500 && elapsed \< 1000\) desiredStep \= 1;

  else if (elapsed \>= 1000 && elapsed \< 1500\) desiredStep \= 2;

  else if (elapsed \>= 1500 && elapsed \< 2500\) desiredStep \= 3;

  else if (elapsed \>= 2500 && elapsed \< 3000\) desiredStep \= 4;

  else if (elapsed \>= 3000 && elapsed \< 4000\) desiredStep \= 5;

  else if (elapsed \>= 4000 && elapsed \< 4500\) desiredStep \= 6;

  else if (elapsed \>= 4500\) desiredStep \= 99;

  if (desiredStep \== bootStep) return;

  bootStep \= desiredStep;

  switch (bootStep) {

    case 1: drawText(20, 30, "M5 TERMINAL", COLOR\_KEY, 3); break;

    case 2: drawText(20, 75, "v1.2 / BOOT", COLOR\_DIM, 2); break;

    case 3: drawText(20, 110, "\> MEM CHECK...", COLOR\_KEY, 2); break;

    case 4:

      clearRegion(20, 110, 280, 20);

      drawText(20, 110, "\> MEM OK", COLOR\_KEY, 2);

      break;

    case 5: drawText(20, 140, "\! OBSERVER MISMATCH", COLOR\_WARN, 2); break;

    case 6: drawText(20, 170, "\> PROCEEDING...", COLOR\_DIM, 2); break;

    case 99:

      uiState \= STATE\_MAIN;

      stateStartMillis \= millis();

      drawMainStaticUI();

      break;

  }

}

// MAIN UI

void drawMainStaticUI() {

  M5.Display.fillScreen(COLOR\_BG);

  drawText(10, 5, "M5 TERMINAL", COLOR\_KEY, 2);

  drawHLine(25, COLOR\_DIM);

  drawText(10, 32, "\> UNIT-M5 ONLINE",   COLOR\_KEY, 2);

  drawText(10, 52, "\> SUBJECT: \#\#\#\#\#\#\#\#", COLOR\_DIM, 2);

  drawText(10, 72, "\> LINK:",             COLOR\_DIM, 2);

  drawHLine(95, COLOR\_DIM);

  drawText(10, 104, "PITCH:",  COLOR\_DIM, 2);

  drawText(10, 124, "ROLL :",  COLOR\_DIM, 2);

  drawText(10, 148, "TX/TY:",  COLOR\_DIM, 2);

  drawText(10, 168, "BTN  :",  COLOR\_DIM, 2);

  drawHLine(190, COLOR\_DIM);

  drawText(10, 198, "TX 50Hz", COLOR\_DIM, 1);

  drawText(85, 198, "\#\#-\#\#-\#\#\#\#", COLOR\_DIM, 1);

  lastPitch \= \-999.0;

  lastRoll  \= \-999.0;

  lastTx    \= \-999.0;

  lastTy    \= \-999.0;

  lastBtnA  \= \-1;

  lastBtnB  \= \-1;

  lastBtnC  \= \-1;

  lastDrawnLinkState \= (LinkState)-1;

  renderLinkStatus();

  lastMismatchVisible \= true;

  mismatchActive \= false;

  nextMismatchTime \= millis() \+ 10000 \+ random(10000);

  renderMismatchWarning(millis());

}

void renderMain(float pitch, float roll, float tx, float ty,

                int btnA, int btnB, int btnC) {

  if (abs(pitch \- lastPitch) \>= 0.1) {

    clearRegion(100, 104, 100, 18);

    char buf\[16\];

    snprintf(buf, sizeof(buf), "%+06.1f", pitch);

    drawText(100, 104, buf, COLOR\_KEY, 2);

    lastPitch \= pitch;

  }

  if (abs(roll \- lastRoll) \>= 0.1) {

    clearRegion(100, 124, 100, 18);

    char buf\[16\];

    snprintf(buf, sizeof(buf), "%+06.1f", roll);

    drawText(100, 124, buf, COLOR\_KEY, 2);

    lastRoll \= roll;

  }

  if (abs(tx \- lastTx) \>= 0.01 || abs(ty \- lastTy) \>= 0.01) {

    clearRegion(100, 148, 200, 18);

    char buf\[20\];

    if (tx \< 0.0) {

      snprintf(buf, sizeof(buf), "-.--/-.--");

    } else {

      snprintf(buf, sizeof(buf), "%.2f/%.2f", tx, ty);

    }

    drawText(100, 148, buf, COLOR\_KEY, 2);

    lastTx \= tx;

    lastTy \= ty;

  }

  if (btnA \!= lastBtnA || btnB \!= lastBtnB || btnC \!= lastBtnC) {

    clearRegion(100, 168, 200, 18);

    char buf\[12\];

    snprintf(buf, sizeof(buf), "%c %c %c",

      btnA ? 'A' : '-',

      btnB ? 'B' : '-',

      btnC ? 'C' : '-');

    drawText(100, 168, buf, COLOR\_KEY, 2);

    lastBtnA \= btnA;

    lastBtnB \= btnB;

    lastBtnC \= btnC;

  }

  unsigned long now \= millis();

  if (now \- lastClockUpdate \>= 1000\) {

    lastClockUpdate \= now;

    unsigned long uptime \= (now \- stateStartMillis) / 1000;

    int hh \= (uptime / 3600\) % 100;

    int mm \= (uptime / 60\) % 60;

    int ss \= uptime % 60;

    clearRegion(160, 198, 100, 12);

    char buf\[16\];

    snprintf(buf, sizeof(buf), "%02d:%02d:%02d", hh, mm, ss);

    drawText(160, 198, buf, COLOR\_DIM, 1);

  }

}

void setup() {

  auto cfg \= M5.config();

  M5.begin(cfg);

  M5.Imu.init();

  Serial.begin(115200);

  randomSeed(micros());

  M5.Display.fillScreen(COLOR\_BG);

  bootMillis \= millis();

  stateStartMillis \= bootMillis;

  uiState \= STATE\_BOOT;

  bootStep \= \-1;

}

void loop() {

  M5.update();

  unsigned long now \= millis();

  if (now \- bootMillis \< HANDSHAKE\_DURATION\_MS) {

    static unsigned long lastHandshake \= 0;

    if (now \- lastHandshake \>= 100\) {

      Serial.println("M5\_HANDSHAKE\_v1");

      lastHandshake \= now;

    }

  }

  float ax, ay, az;

  M5.Imu.getAccel(\&ax, \&ay, \&az);

  float pitch \= atan2(-ax, sqrt(ay \* ay \+ az \* az)) \* 180.0 / PI;

  float roll  \= atan2(ay, az) \* 180.0 / PI;

  float tx \= \-1.0, ty \= \-1.0;

  auto t \= M5.Touch.getDetail();

  if (t.isPressed()) {

    tx \= (float)t.x / 320.0;

    ty \= (float)t.y / 240.0;

  }

  int btnA \= M5.BtnA.isPressed() ? 1 : 0;

  int btnB \= M5.BtnB.isPressed() ? 1 : 0;

  int btnC \= M5.BtnC.isPressed() ? 1 : 0;

  if (now \- bootMillis \>= HANDSHAKE\_DURATION\_MS &&

      now \- lastSendMillis \>= SEND\_INTERVAL\_MS) {

    lastSendMillis \= now;

    Serial.print("D,");

    Serial.print(pitch, 2); Serial.print(",");

    Serial.print(roll, 2);  Serial.print(",");

    Serial.print(ax, 3);    Serial.print(",");

    Serial.print(ay, 3);    Serial.print(",");

    Serial.print(az, 3);    Serial.print(",");

    Serial.print(tx, 3);    Serial.print(",");

    Serial.print(ty, 3);    Serial.print(",");

    Serial.print(btnA);     Serial.print(",");

    Serial.print(btnB);     Serial.print(",");

    Serial.println(btnC);

  }

  if (uiState \== STATE\_BOOT) {

    renderBoot(now \- stateStartMillis);

  } else {

    updateLinkState(now);

    renderLinkStatus();

    updateMismatchState(now);

    renderMismatchWarning(now);

    renderMain(pitch, roll, tx, ty, btnA, btnB, btnC);

  }

}

### 7.2 M5Reader.cs

`Assets/Scripts/M5Reader.cs`

using System;

using System.Collections.Concurrent;

using System.IO.Ports;

using System.Linq;

using System.Threading;

using UnityEngine;

public class M5Reader : MonoBehaviour

{

    \[Header("Serial Settings")\]

    \[SerializeField\] private int baudRate \= 115200;

    \[SerializeField\] private string handshakeString \= "M5\_HANDSHAKE\_v1";

    \[Header("Live Values (read-only)")\]

    \[SerializeField\] private bool isConnected \= false;

    \[SerializeField\] private string connectedPort \= "";

    \[SerializeField\] private float pitch;

    \[SerializeField\] private float roll;

    \[SerializeField\] private float ax, ay, az;

    \[SerializeField\] private float tx, ty;

    \[SerializeField\] private int btnA, btnB, btnC;

    public bool IsConnected \=\> isConnected;

    public float Pitch \=\> pitch;

    public float Roll \=\> roll;

    public Vector3 Accel \=\> new Vector3(ax, ay, az);

    public Vector2 Touch \=\> new Vector2(tx, ty);

    public bool ButtonA \=\> btnA \== 1;

    public bool ButtonB \=\> btnB \== 1;

    public bool ButtonC \=\> btnC \== 1;

    private SerialPort serialPort;

    private Thread readThread;

    private volatile bool keepReading \= false;

    private readonly ConcurrentQueue\<string\> incomingLines \= new ConcurrentQueue\<string\>();

    private float portScanTimer \= 0f;

    private const float PORT\_SCAN\_INTERVAL \= 1.0f;

    void Update()

    {

        if (\!isConnected)

        {

            portScanTimer \+= Time.deltaTime;

            if (portScanTimer \>= PORT\_SCAN\_INTERVAL)

            {

                portScanTimer \= 0f;

                TryConnect();

            }

            return;

        }

        while (incomingLines.TryDequeue(out string line))

        {

            ParseLine(line);

        }

    }

    private void TryConnect()

    {

        string\[\] ports \= SerialPort.GetPortNames();

        foreach (string portName in ports.Reverse())

        {

            if (TryOpenAndHandshake(portName))

            {

                isConnected \= true;

                connectedPort \= portName;

                Debug.Log($"\[M5Reader\] Connected on {portName}");

                StartReadThread();

                return;

            }

        }

    }

    private bool TryOpenAndHandshake(string portName)

    {

        SerialPort sp \= null;

        try

        {

            sp \= new SerialPort(portName, baudRate);

            sp.ReadTimeout \= 500;

            sp.NewLine \= "\\n";

            sp.Open();

            DateTime deadline \= DateTime.Now.AddMilliseconds(1500);

            while (DateTime.Now \< deadline)

            {

                try

                {

                    string line \= sp.ReadLine().Trim();

                    if (line.Contains(handshakeString))

                    {

                        serialPort \= sp;

                        return true;

                    }

                }

                catch (TimeoutException) { }

            }

            sp.Close();

            return false;

        }

        catch (Exception e)

        {

            Debug.LogWarning($"\[M5Reader\] Failed to open {portName}: {e.Message}");

            try { sp?.Close(); } catch { }

            return false;

        }

    }

    private void StartReadThread()

    {

        keepReading \= true;

        readThread \= new Thread(ReadLoop) { IsBackground \= true };

        readThread.Start();

    }

    private void ReadLoop()

    {

        while (keepReading && serialPort \!= null && serialPort.IsOpen)

        {

            try

            {

                string line \= serialPort.ReadLine();

                if (\!string.IsNullOrEmpty(line))

                {

                    incomingLines.Enqueue(line.Trim());

                }

            }

            catch (TimeoutException) { }

            catch (Exception e)

            {

                Debug.LogWarning($"\[M5Reader\] Read error: {e.Message}");

                break;

            }

        }

    }

    private void ParseLine(string line)

    {

        if (\!line.StartsWith("D,")) return;

        string\[\] parts \= line.Split(',');

        if (parts.Length \< 11\) return;

        try

        {

            pitch \= float.Parse(parts\[1\], System.Globalization.CultureInfo.InvariantCulture);

            roll  \= float.Parse(parts\[2\], System.Globalization.CultureInfo.InvariantCulture);

            ax    \= float.Parse(parts\[3\], System.Globalization.CultureInfo.InvariantCulture);

            ay    \= float.Parse(parts\[4\], System.Globalization.CultureInfo.InvariantCulture);

            az    \= float.Parse(parts\[5\], System.Globalization.CultureInfo.InvariantCulture);

            tx    \= float.Parse(parts\[6\], System.Globalization.CultureInfo.InvariantCulture);

            ty    \= float.Parse(parts\[7\], System.Globalization.CultureInfo.InvariantCulture);

            btnA  \= int.Parse(parts\[8\]);

            btnB  \= int.Parse(parts\[9\]);

            btnC  \= int.Parse(parts\[10\]);

        }

        catch { }

    }

    void OnApplicationQuit() \=\> Disconnect();

    void OnDestroy() \=\> Disconnect();

    private void Disconnect()

    {

        keepReading \= false;

        try { readThread?.Join(500); } catch { }

        try { serialPort?.Close(); } catch { }

        serialPort \= null;

        isConnected \= false;

    }

}

### 7.3 M5VisualController.cs

`Assets/Scripts/M5VisualController.cs`

軸の対応：

- M5 Pitch → 押し出し量（Roll Mapping のラベルだが Pitch を読む）  
- M5 Roll  → Plane 回転（Pitch Multiplier のラベルだが Roll を読む）  
- M5 Accel → Ghost Offset

using UnityEngine;

public class M5VisualController : MonoBehaviour

{

    \[Header("References")\]

    \[SerializeField\] private M5Reader m5Reader;

    \[SerializeField\] private Renderer targetRenderer;

    \[Header("Extrude Mapping (Pitch)")\]

    \[SerializeField\] private float maxRollDegrees \= 60f;

    \[SerializeField\] private float maxExtrude \= 1.0f;

    \[Header("Rotation Mapping (Roll)")\]

    \[SerializeField\] private float pitchRotationMultiplier \= \-1.0f;

    \[SerializeField\] private float maxTiltDegrees \= 60f;

    \[Header("Smoothing")\]

    \[Range(0f, 1f)\]

    \[SerializeField\] private float smoothing \= 0.15f;

    \[Header("Disturbance (shake/impact)")\]

    \[SerializeField\] private float disturbanceThreshold \= 0.7f;

    \[SerializeField\] private float maxGhostOffset \= 1.0f;

    \[SerializeField\] private float disturbanceDecayTime \= 0.3f;

    \[Header("Live (read-only)")\]

    \[SerializeField\] private float currentExtrude;

    \[SerializeField\] private float currentTilt;

    \[SerializeField\] private float currentDisturbance;

    \[SerializeField\] private float currentGhostOffset;

    private Material runtimeMaterial;

    private Quaternion initialRotation;

    private static readonly int ExtrudeAmountID \= Shader.PropertyToID("\_ExtrudeAmount");

    private static readonly int GhostOffsetID \= Shader.PropertyToID("\_GhostOffset");

    void Start()

    {

        if (targetRenderer \== null) targetRenderer \= GetComponent\<Renderer\>();

        runtimeMaterial \= targetRenderer.material;

        initialRotation \= transform.rotation;

    }

    void Update()

    {

        if (m5Reader \== null || runtimeMaterial \== null) return;

        // extrude (driven by Pitch)

        float extrudeInput \= m5Reader.Pitch;

        float extrudeNorm \= Mathf.Clamp01(Mathf.Abs(extrudeInput) / maxRollDegrees);

        float targetExtrude \= extrudeNorm \* maxExtrude;

        currentExtrude \= Mathf.Lerp(currentExtrude, targetExtrude, 1f \- smoothing);

        runtimeMaterial.SetFloat(ExtrudeAmountID, currentExtrude);

        // rotation (driven by Roll)

        float rotationInput \= m5Reader.Roll;

        float clampedTilt \= Mathf.Clamp(rotationInput \* pitchRotationMultiplier,

                                        \-maxTiltDegrees, maxTiltDegrees);

        currentTilt \= Mathf.Lerp(currentTilt, clampedTilt, 1f \- smoothing);

        transform.rotation \= initialRotation \* Quaternion.Euler(currentTilt, 0f, 0f);

        // disturbance (driven by Acceleration)

        float accelMag \= m5Reader.Accel.magnitude;

        float rawDisturbance \= Mathf.Max(0f, Mathf.Abs(accelMag \- 1.0f) \- disturbanceThreshold);

        if (rawDisturbance \> currentDisturbance)

        {

            currentDisturbance \= rawDisturbance;

        }

        else

        {

            float decayPerSecond \= 1f / Mathf.Max(0.01f, disturbanceDecayTime);

            currentDisturbance \= Mathf.Max(0f,

                currentDisturbance \- decayPerSecond \* Time.deltaTime);

        }

        currentGhostOffset \= Mathf.Clamp01(currentDisturbance) \* maxGhostOffset;

        runtimeMaterial.SetFloat(GhostOffsetID, currentGhostOffset);

    }

}

### 7.4 M5Visual.shader

`Assets/Shaders/M5Visual.shader`

ジオメトリシェーダーで Plane を3層に複製：

- メイン（緑、layer 1）  
- 左複製（暗赤、layer 2）  
- 右複製（暗青、layer 3）

Shader "Unlit/M5Visual"

{

    Properties

    {

        \_BaseColor ("Base Color", Color) \= (0.054, 0.149, 0.149, 1.0)

        \_LineColor ("Line Color", Color) \= (0.545, 0.592, 0.314, 1.0)

        \_ExtrudeAmount ("Extrude Amount", Range(0, 2)) \= 0.3

        \_GhostOffset ("Ghost Offset", Range(0, 0.5)) \= 0.0

    }

    SubShader

    {

        Tags { "RenderType"="Opaque" }

        LOD 100

        Pass

        {

            CGPROGRAM

            \#pragma vertex vert

            \#pragma geometry geom

            \#pragma fragment frag

            \#pragma target 4.0

            \#include "UnityCG.cginc"

            struct appdata

            {

                float4 vertex : POSITION;

                float3 normal : NORMAL;

            };

            struct v2g

            {

                float4 vertex : POSITION;

                float3 normal : NORMAL;

            };

            struct g2f

            {

                float4 pos : SV\_POSITION;

                float layer : TEXCOORD0;

            };

            fixed4 \_BaseColor;

            fixed4 \_LineColor;

            float \_ExtrudeAmount;

            float \_GhostOffset;

            v2g vert (appdata v)

            {

                v2g o;

                o.vertex \= v.vertex;

                o.normal \= v.normal;

                return o;

            }

            void emitTriangle(triangle v2g input\[3\],

                              inout TriangleStream\<g2f\> stream,

                              float xShift, float layerId)

            {

                for (int i \= 0; i \< 3; i++)

                {

                    g2f o;

                    float4 v \= input\[i\].vertex;

                    v.xyz \+= input\[i\].normal \* \_ExtrudeAmount;

                    v.x   \+= xShift;

                    o.pos \= UnityObjectToClipPos(v);

                    o.layer \= layerId;

                    stream.Append(o);

                }

                stream.RestartStrip();

            }

            \[maxvertexcount(9)\]

            void geom (triangle v2g input\[3\], inout TriangleStream\<g2f\> stream)

            {

                emitTriangle(input, stream, \-\_GhostOffset, 2.0);

                emitTriangle(input, stream, \+\_GhostOffset, 3.0);

                emitTriangle(input, stream, 0.0, 1.0);

            }

            fixed4 frag (g2f i) : SV\_Target

            {

                if (i.layer \< 1.5) {

                    return \_LineColor;

                } else if (i.layer \< 2.5) {

                    return fixed4(0.4, 0.0, 0.0, 1.0);

                } else {

                    return fixed4(0.0, 0.0, 0.5, 1.0);

                }

            }

            ENDCG

        }

    }

}

### 7.5 PostEffect.shader

`Assets/Shaders/PostEffect.shader`

カメラに適用するImage Effect。ビネット・走査線・色収差・グレインの4種を統合。

Shader "Hidden/PostEffect"

{

    Properties

    {

        \_MainTex ("Source", 2D) \= "white" {}

        \_VignetteIntensity ("Vignette Intensity", Range(0, 2)) \= 0.9

        \_VignetteSmoothness ("Vignette Smoothness", Range(0.01, 1)) \= 0.7

        \_VignetteColor ("Vignette Color", Color) \= (0, 0, 0, 1\)

        \_ScanlineCount ("Scanline Count", Range(100, 1500)) \= 450

        \_ScanlineIntensity ("Scanline Intensity", Range(0, 1)) \= 0.25

        \_AberrationStrength ("Aberration Strength", Range(0, 0.05)) \= 0.03

        \_GrainStrength ("Grain Strength", Range(0, 0.5)) \= 0.1

    }

    SubShader

    {

        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" }

        Cull Off

        ZWrite Off

        ZTest Always

        Pass

        {

            CGPROGRAM

            \#pragma vertex vert

            \#pragma fragment frag

            \#include "UnityCG.cginc"

            struct appdata

            {

                float4 vertex : POSITION;

                float2 uv : TEXCOORD0;

            };

            struct v2f

            {

                float2 uv : TEXCOORD0;

                float4 vertex : SV\_POSITION;

            };

            sampler2D \_MainTex;

            float \_VignetteIntensity;

            float \_VignetteSmoothness;

            fixed4 \_VignetteColor;

            float \_ScanlineCount;

            float \_ScanlineIntensity;

            float \_AberrationStrength;

            float \_GrainStrength;

            v2f vert (appdata v)

            {

                v2f o;

                o.vertex \= UnityObjectToClipPos(v.vertex);

                o.uv \= v.uv;

                return o;

            }

            float hash(float2 p)

            {

                return frac(sin(dot(p, float2(12.9898, 78.233))) \* 43758.5453);

            }

            fixed4 frag (v2f i) : SV\_Target

            {

                float2 centeredUV \= i.uv \- 0.5;

                // Chromatic Aberration

                float2 dir \= centeredUV;

                float2 rOffset \= dir \* \_AberrationStrength;

                float2 bOffset \= dir \* \-\_AberrationStrength;

                fixed4 col;

                col.r \= tex2D(\_MainTex, i.uv \+ rOffset).r;

                col.g \= tex2D(\_MainTex, i.uv).g;

                col.b \= tex2D(\_MainTex, i.uv \+ bOffset).b;

                col.a \= 1.0;

                // Scanlines

                float scan \= sin(i.uv.y \* \_ScanlineCount \* 3.14159) \* 0.5 \+ 0.5;

                float scanFactor \= lerp(1.0 \- \_ScanlineIntensity, 1.0, scan);

                col.rgb \*= scanFactor;

                // Film Grain

                float grain \= hash(i.uv \+ \_Time.y) \- 0.5;

                col.rgb \+= grain \* \_GrainStrength;

                // Vignette

                float dist \= length(centeredUV);

                float mask \= smoothstep(\_VignetteSmoothness, 0.0, dist \* \_VignetteIntensity);

                col.rgb \= lerp(\_VignetteColor.rgb, col.rgb, mask);

                return col;

            }

            ENDCG

        }

    }

}

### 7.6 PostEffect.cs

`Assets/Scripts/PostEffect.cs`

using UnityEngine;

\[ExecuteAlways\]

\[RequireComponent(typeof(Camera))\]

public class PostEffect : MonoBehaviour

{

    \[SerializeField\] private Shader postShader;

    private Material runtimeMaterial;

    void OnEnable()

    {

        if (postShader \== null) return;

        runtimeMaterial \= new Material(postShader);

        runtimeMaterial.hideFlags \= HideFlags.HideAndDontSave;

    }

    void OnDisable()

    {

        if (runtimeMaterial \!= null)

        {

            if (Application.isPlaying)

                Destroy(runtimeMaterial);

            else

                DestroyImmediate(runtimeMaterial);

            runtimeMaterial \= null;

        }

    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)

    {

        if (runtimeMaterial \== null)

        {

            Graphics.Blit(source, destination);

            return;

        }

        Graphics.Blit(source, destination, runtimeMaterial);

    }

}

---

## 8\. 達成済みマイルストーン

■ 環境構築フェーズ                     ✅ 完了

■ 通信基盤フェーズ                     ✅ 完了

■ 描画フェーズ（GS）                    ✅ 完了

■ 統合フェーズ                          ✅ 完了

■ A. ポストエフェクト                ✅ 完了

■ C. M5側UIの作り込み              ✅ 完了

■ B. 入力マッピング拡張

  ✅ 優先度1：Pitch → 押し出し、Roll → 回転

  ✅ 優先度2：加速度 → Ghost Offset (左右複製)

  ⏭ 優先度3：ボタン

  ⏭ 優先度4：タッチ

---

## 9\. 次のステップ

### 直近の作業

**B. 入力マッピング拡張の続き**：

#### 優先度3：ボタン

仕様書では：

- A：観測モード切替（点群／面／ワイヤー）  
- B：時系列スクラブ（要決定）  
- C：リセット

A はシェーダーのレンダリング方式を切り替える、けっこう大きい変更。 B は未確定。 C はシンプルだが、何をリセットするのか定義が必要。

#### 優先度4：タッチ

タッチXY を使った演出。例：

- Plane の上に小さい点を出す  
- 触った位置が画面に転写される

### 中期の作業

- 状態遷移実装（STATE 0〜3）  
- 形状の作り込み（Plane → 別の世界観に合うもの）  
- GS表現の発展（分裂・崩壊・複数層）  
- 音響（ノイズ、心拍、無線断片）  
- SDカード活用（ダミーログ）  
- 統合・テンポ調整

---

## 10\. 確定済みパラメータ

### M5Visual.shader

- \_BaseColor \= \#0E2626  
- \_LineColor \= \#8B9750  
- 押し出し最大 \= 1.0  
- Ghost Offset 最大 \= 0.5（C\# 側でクランプ）

### PostEffect.shader

- VignetteIntensity \= 0.9  
- VignetteSmoothness \= 0.7  
- ScanlineCount \= 450  
- ScanlineIntensity \= 0.25  
- AberrationStrength \= 0.03  
- GrainStrength \= 0.10

### M5VisualController.cs

- maxRollDegrees \= 60  
- maxExtrude \= 1.0  
- pitchRotationMultiplier \= \-1.0  
- maxTiltDegrees \= 60  
- smoothing \= 0.15  
- disturbanceThreshold \= 0.7  
- maxGhostOffset \= 1.0  
- disturbanceDecayTime \= 0.3

### M5側スケッチ

- ボーレート \= 115200  
- 送信レート \= 50Hz  
- LINK 状態確率 \= 55/20/10/15（STABLE/UNSTABLE/DEGRADED/LOST）  
- LINK 切替間隔 \= 1.5〜4.5秒  
- MISMATCH 表示頻度 \= 10〜20秒に1回、1.6秒間  
- MISMATCH 点滅回数 \= 8回

---

## 11\. 未決事項

- A-1：持ち主（同僚）の素性（性別／年齢／職務／死因）  
- ボタンBの具体的役割  
- セッション想定時間  
- STATE 3で見せるGS形状の元ネタ  
- 作品タイトル

---

## 12\. トラブル対応

### Unity側でCOMポートが開けない

原因：Arduino IDE が COM ポートを掴んでいる。 対処：Arduino IDE を完全に閉じる、または書き込み後にシリアルモニタを閉じる。

### 平面がピンク（マゼンタ）で表示される

原因：シェーダーコンパイルエラー。 対処：Console を確認、エラー箇所修正。

### M5の電源が落ちている

M5左側面の電源ボタンを長押しで起動、もしくは USB-C を抜き挿し。

---

## 13\. 再開時の手順

1. Unity Hub から `REVERB_PROTOCOL_M5` を開く  
2. `Main.unity` シーンを開く  
3. M5 を USB-C で PC 接続  
4. Arduino IDE は閉じておく  
5. Unity の ▶ Play で動作確認  
6. M5 を傾けて Plane が反応すればOK

