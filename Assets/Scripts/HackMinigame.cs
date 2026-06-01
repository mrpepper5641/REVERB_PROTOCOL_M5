using System.Text;
using UnityEngine;

/// <summary>
/// Hack minigame HUD — dramatic hacker UI with smooth water-rising progress bar.
///
/// M5 HS values:
///   0=IDLE  1=INIT  2=TOUCH1  3=TOUCH2  4=TOUCH3
///   5=TILT1  6=TILT2  7=TILT3  8=SUCCESS  9=FAIL
/// </summary>
public class HackMinigame : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M5Reader m5Reader;

    [Header("Auto-reset after result (seconds)")]
    [SerializeField] private float resultHoldTime = 3.0f;

    // ── public state ──────────────────────────────────────────
    public enum State { Idle, InProgress, Success, Fail }
    public State CurrentState { get; private set; } = State.Idle;
    public bool  IsActive     => CurrentState != State.Idle;

    // ── progress tracking ─────────────────────────────────────
    private float displayProgress = 0f;   // raw value from M5 (0-100)
    private float visualProgress  = 0f;   // smoothly animated display value

    private float resultTimer = 0f;

    // ── hex dump scrolling ────────────────────────────────────
    private const int HEX_LINES = 5;
    private string[] hexLines = new string[HEX_LINES];
    private float    hexTimer = 0f;
    private const float HEX_INTERVAL = 0.055f;
    private System.Random sysRng = new System.Random();

    // ── glitch ────────────────────────────────────────────────
    private float glitchOffsetX = 0f;
    private float glitchTimer   = 0f;

    // ── init phase scan line ──────────────────────────────────
    private float scanY = 0f;

    // ── GUIStyle cache ────────────────────────────────────────
    private GUIStyle monoStyle;

    // ── step labels ──────────────────────────────────────────
    private static readonly string[] StepLabels =
    {
        "INIT  :: BYPASSING SECURITY",   // hs=1
        "INPUT :: SEQUENCE ALPHA",       // hs=2
        "INPUT :: SEQUENCE BETA",        // hs=3
        "INPUT :: SEQUENCE GAMMA",       // hs=4
        "TILT  :: ANGLE OVERRIDE I",     // hs=5
        "TILT  :: ANGLE OVERRIDE II",    // hs=6
        "TILT  :: ANGLE OVERRIDE III",   // hs=7
        "ACCESS GRANTED",                // hs=8
        "INTRUSION DETECTED",            // hs=9
    };

    // ─────────────────────────────────────────────────────────
    void Start()
    {
        if (M5Reader.Instance != null) m5Reader = M5Reader.Instance;

        for (int i = 0; i < HEX_LINES; i++)
            hexLines[i] = MakeHexLine();
    }

    void Update()
    {
        if (m5Reader == null) return;

        int hs = m5Reader.HackState;
        int hp = m5Reader.HackProgress;

        // ── hex scroll ──────────────────────────────────────
        hexTimer += Time.deltaTime;
        if (hexTimer >= HEX_INTERVAL)
        {
            hexTimer = 0f;
            int lineToUpdate = Random.Range(0, HEX_LINES);
            hexLines[lineToUpdate] = MakeHexLine();
        }

        // ── glitch ──────────────────────────────────────────
        glitchTimer -= Time.deltaTime;
        if (glitchTimer <= 0f)
        {
            glitchOffsetX = Random.value > 0.65f ? Random.Range(-4f, 4f) : 0f;
            glitchTimer   = Random.Range(0.07f, 0.35f);
        }

        // ── init scan line ──────────────────────────────────
        if (hs == 1)
            scanY = Mathf.Repeat(scanY + Time.deltaTime * 180f, Screen.height);

        // ── state machine ───────────────────────────────────
        switch (CurrentState)
        {
            case State.Idle:
                if (hs >= 1 && hs <= 7)
                {
                    CurrentState    = State.InProgress;
                    displayProgress = 0f;
                    visualProgress  = 0f;
                }
                break;

            case State.InProgress:
                if (hs == 8)
                {
                    CurrentState    = State.Success;
                    displayProgress = 100f;
                    resultTimer     = resultHoldTime;
                }
                else if (hs == 9)
                {
                    CurrentState = State.Fail;
                    resultTimer  = resultHoldTime;
                }
                else
                {
                    // never let display go backward
                    displayProgress = Mathf.Max(displayProgress, (float)hp);
                }
                break;

            case State.Success:
            case State.Fail:
                resultTimer -= Time.deltaTime;
                if (resultTimer <= 0f) Reset();
                break;
        }

        // ── smooth visual progress (water-rising) ───────────
        if (CurrentState == State.InProgress)
        {
            // Constant fill speed toward target
            float fillSpeed = 14f;
            visualProgress = Mathf.MoveTowards(visualProgress, displayProgress, fillSpeed * Time.deltaTime);

            // Tiny noise to feel alive — only when "working" toward target
            if (visualProgress < displayProgress - 0.5f)
            {
                float noise  = Mathf.Sin(Time.time * 37.1f) * 0.35f
                             + Mathf.Sin(Time.time * 19.7f) * 0.15f;
                visualProgress = Mathf.Clamp(visualProgress + noise * Time.deltaTime * 2f,
                                             0f, displayProgress + 0.3f);
            }
        }
        else if (CurrentState == State.Success)
        {
            visualProgress = Mathf.MoveTowards(visualProgress, 100f, 60f * Time.deltaTime);
        }
    }

    // ─────────────────────────────────────────────────────────
    public void StartHack()
    {
        if (CurrentState != State.Idle) return;
        displayProgress = 0f;
        visualProgress  = 0f;
        m5Reader?.SendCommand("CMD,HACK_START");
        // CurrentState will flip to InProgress when M5 responds with hs>=1
    }

    public void Reset()
    {
        CurrentState    = State.Idle;
        displayProgress = 0f;
        visualProgress  = 0f;
    }

    // ─────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (CurrentState == State.Idle) return;

        EnsureStyle();

        int hs = m5Reader != null ? m5Reader.HackState : 0;

        if (hs == 1)
            DrawInitScreen();        // INIT フェーズ専用
        else
            DrawHackHUD(hs);         // 通常ハック HUD
    }

    // ─────────────────────────────────────────────────────────
    // INIT フェーズ: ドラマチック全画面演出
    // ─────────────────────────────────────────────────────────
    void DrawInitScreen()
    {
        float sw = Screen.width;
        float sh = Screen.height;

        // 半透明オーバーレイ
        DrawRect(0, 0, sw, sh, new Color(0f, 0.04f, 0.02f, 0.88f));

        // スキャンライン
        DrawRect(0, scanY, sw, 2f, new Color(0.545f, 0.592f, 0.314f, 0.25f));
        DrawRect(0, Mathf.Repeat(scanY + sh * 0.5f, sh), sw, 1f,
                 new Color(0.545f, 0.592f, 0.314f, 0.12f));

        // 中央テキスト
        float cy = sh * 0.32f;
        DrawLabel(0, cy,        sw, "SECURITY BYPASS INITIATED",
                  new Color(0.545f, 0.592f, 0.314f, 1f), 22, true, TextAnchor.MiddleCenter);
        DrawLabel(0, cy + 34f,  sw, "ANALYZING COUNTERMEASURES...",
                  new Color(0.3f, 0.45f, 0.2f, 0.8f), 14, false, TextAnchor.MiddleCenter);

        // hex dump 3行
        float hx = sw * 0.1f, hy = sh * 0.55f;
        for (int i = 0; i < 3; i++)
        {
            float ox = (i == 1 && glitchOffsetX != 0f) ? glitchOffsetX : 0f;
            DrawLabel(hx + ox, hy + i * 16f, sw * 0.8f, hexLines[i],
                      new Color(0.25f, 0.45f, 0.25f, 0.65f), 11, false, TextAnchor.UpperLeft);
        }

        // 点滅 ALERT
        if (Mathf.Sin(Time.time * 8f) > 0f)
            DrawLabel(0, sh * 0.82f, sw, "! COUNTERMEASURE ACTIVE — BYPASSING",
                      new Color(0.85f, 0.15f, 0.1f, 0.9f), 13, true, TextAnchor.MiddleCenter);
    }

    // ─────────────────────────────────────────────────────────
    // 通常ハック HUD (画面下部)
    // ─────────────────────────────────────────────────────────
    void DrawHackHUD(int hs)
    {
        float sw = Screen.width;
        float sh = Screen.height;

        float panelW = Mathf.Min(sw * 0.72f, 900f);
        float panelH = 200f;
        float panelX = 18f;
        float panelY = sh - panelH - 18f;

        // 外枠 + パネル背景
        DrawRect(panelX - 3f, panelY - 3f, panelW + 6f, panelH + 6f,
                 new Color(0.545f, 0.592f, 0.314f, 0.22f));
        DrawRect(panelX, panelY, panelW, panelH,
                 new Color(0.01f, 0.04f, 0.02f, 0.93f));

        float x = panelX + 12f;
        float y = panelY + 10f;
        float innerW = panelW - 24f;

        // ── ヘッダー ──────────────────────────────────────
        Color headerCol = CurrentState == State.Fail    ? new Color(0.85f, 0.15f, 0.1f, 1f)
                        : CurrentState == State.Success ? new Color(0.2f,  0.9f,  0.4f, 1f)
                        : new Color(0.545f, 0.592f, 0.314f, 1f);

        string statusText = GetStatusLabel(hs);
        string blink = (Time.time % 0.55f < 0.28f) ? "█" : " ";
        DrawLabel(x, y, innerW,
                  $"▶ BYPASS {blink}  ::  {statusText}",
                  headerCol, 13, true, TextAnchor.UpperLeft);
        y += 20f;

        // ── hex dump 2行 ──────────────────────────────────
        Color hexCol = new Color(0.28f, 0.48f, 0.28f, 0.65f);
        for (int i = 0; i < 2; i++)
        {
            float ox = (glitchOffsetX != 0f && i == 0) ? glitchOffsetX : 0f;
            DrawLabel(x + ox, y, innerW, hexLines[i + 2], hexCol, 10, false, TextAnchor.UpperLeft);
            y += 14f;
        }
        y += 6f;

        // ── セグメント式プログレスバー (6分割) ───────────
        const int SEG_COUNT = 6;
        float gap    = 3f;
        float barH   = 24f;
        float segW   = (innerW - gap * (SEG_COUNT - 1)) / SEG_COUNT;

        for (int s = 0; s < SEG_COUNT; s++)
        {
            float sx   = x + s * (segW + gap);
            float sMin = s * (100f / SEG_COUNT);
            float sMax = (s + 1) * (100f / SEG_COUNT);

            float fill = Mathf.Clamp01((visualProgress - sMin) / (sMax - sMin));

            // ノイズ: アクティブセグメントの先端をわずかに揺らす
            float drawFill = fill;
            if (fill > 0.02f && fill < 0.98f)
                drawFill += Mathf.Sin(Time.time * 43f + s * 2.3f) * 0.018f;
            drawFill = Mathf.Clamp01(drawFill);

            // 背景
            DrawRect(sx, y, segW, barH, new Color(0.06f, 0.1f, 0.05f, 0.9f));

            // 塗り
            if (drawFill > 0f)
            {
                Color fillCol;
                if (CurrentState == State.Fail)
                    fillCol = new Color(0.7f, 0.08f, 0.05f, 0.9f);
                else if (CurrentState == State.Success || fill >= 1f)
                    fillCol = new Color(0.2f, 0.85f, 0.35f, 0.9f);
                else
                    fillCol = new Color(0.45f, 0.55f, 0.22f, 0.88f);

                DrawRect(sx, y, segW * drawFill, barH, fillCol);
            }

            // 枠線
            DrawRectOutline(sx, y, segW, barH,
                            new Color(0.545f, 0.592f, 0.314f, 0.45f));

            // セグメント番号（完了分のみ薄く）
            if (fill >= 1f)
            {
                DrawLabel(sx, y + 5f, segW, (s + 1).ToString(),
                          new Color(0.1f, 0.1f, 0.08f, 0.6f), 10, true, TextAnchor.UpperCenter);
            }
        }


        y += barH + 8f;

        // ── ボトムステータス行 ＋ パーセント（右揃え、パネル内） ──
        float pct = visualProgress;
        string pctStr = $"{(int)pct:D2}.{(int)(Mathf.Repeat(pct, 1f) * 10):D1}%";
        string bottom = GetBottomLine(hs);
        DrawLabel(x, y, innerW - 70f, bottom,
                  new Color(0.35f, 0.52f, 0.25f, 0.75f), 11, false, TextAnchor.UpperLeft);
        DrawLabel(x, y, innerW, pctStr,
                  headerCol, 13, true, TextAnchor.UpperRight);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────
    private string GetStatusLabel(int hs)
    {
        if (CurrentState == State.Success) return "ACCESS GRANTED";
        if (CurrentState == State.Fail)    return "INTRUSION DETECTED";
        if (hs >= 1 && hs <= 9)            return StepLabels[hs - 1];
        return "STANDBY";
    }

    private string GetBottomLine(int hs)
    {
        if (CurrentState == State.Success)
            return "> DECRYPTION COMPLETE  ::  ALL 6 LAYERS BYPASSED  ::  DATA ACQUIRED";
        if (CurrentState == State.Fail)
            return "> ALERT  ::  COUNTERMEASURE TRIGGERED  ::  CONNECTION TERMINATED";
        int layer = Mathf.Clamp(hs, 1, 7);
        return $"> BYPASS LAYER {layer}/7  ::  PORT SCAN ACTIVE  ::  {hexLines[4].Substring(0, Mathf.Min(20, hexLines[4].Length))}...";
    }

    private string MakeHexLine()
    {
        var sb = new StringBuilder();
        sb.Append($"{sysRng.Next(0x10000):X4}: ");
        for (int i = 0; i < 8; i++)
        {
            sb.Append($"{sysRng.Next(256):X2}");
            sb.Append(i == 3 ? "  " : " ");
        }
        sb.Append(" |");
        for (int i = 0; i < 8; i++)
        {
            int c = sysRng.Next(256);
            sb.Append((char)(c >= 32 && c < 127 ? c : '.'));
        }
        sb.Append("|");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // GUIStyle
    // ─────────────────────────────────────────────────────────
    private void EnsureStyle()
    {
        if (monoStyle != null) return;
        monoStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = false,
            clipping = TextClipping.Clip,
        };
    }

    private void DrawLabel(float x, float y, float w, string text,
                           Color col, int size, bool bold, TextAnchor anchor)
    {
        monoStyle.fontSize   = size;
        monoStyle.fontStyle  = bold ? FontStyle.Bold : FontStyle.Normal;
        monoStyle.alignment  = anchor;
        monoStyle.normal.textColor = col;
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y, w, size * 2f + 4f), text, monoStyle);
    }

    private void DrawRect(float x, float y, float w, float h, Color col)
    {
        GUI.color = col;
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
    }

    private void DrawRectOutline(float x, float y, float w, float h, Color col)
    {
        GUI.color = col;
        GUI.DrawTexture(new Rect(x,       y,       w, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x,       y+h-1,   w, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x,       y,       1, h), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x+w-1,   y,       1, h), Texture2D.whiteTexture);
    }
}
