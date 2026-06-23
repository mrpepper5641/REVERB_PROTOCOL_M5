using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ダウンロード中の演出UI
/// - パーセンテージカウンター
/// - プログレスバー
/// - スクロールするターミナルログ
/// </summary>
public class DownloadProgressUI : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float totalDuration = 4f;

    [Header("Colors")]
    [SerializeField] private Color textColor    = new Color(0.545f, 0.592f, 0.314f, 1f);
    [SerializeField] private Color barColor     = new Color(0.545f, 0.592f, 0.314f, 1f);
    [SerializeField] private Color barBgColor   = new Color(0.05f, 0.12f, 0.05f, 1f);
    [SerializeField] private Color panelColor   = new Color(0.02f, 0.06f, 0.02f, 0.92f);

    private Canvas   canvas;
    private Text     percentText;
    private Text     statusText;
    private Text     logText;
    private Image    barFill;
    private float    elapsed;
    private bool     running;

    private static readonly string[] logLines = {
        "> INITIALIZING TRANSFER PROTOCOL...",
        "> AUTHENTICATING CIPHER KEY...",
        "> ESTABLISHING SECURE TUNNEL...",
        "> BYPASS FIREWALL: LAYER 1",
        "> BYPASS FIREWALL: LAYER 2",
        "> ALLOCATING BUFFER 512MB...",
        "> DECRYPTING PAYLOAD HEADER...",
        "> CHUNK [0x00A1] RECEIVED",
        "> CHUNK [0x00A2] RECEIVED",
        "> CHUNK [0x00A3] RECEIVED",
        "> VERIFYING CHECKSUM...",
        "> CHUNK [0x00B0] RECEIVED",
        "> DATA STREAM: STABLE",
        "> CHUNK [0x00C4] RECEIVED",
        "> FILE INTEGRITY: CHECKING...",
        "> WRITING TO LOCAL BUFFER...",
        "> TRANSFER NEARLY COMPLETE...",
    };

    void Awake()
    {
        BuildUI();
        gameObject.SetActive(false);
    }

    void BuildUI()
    {
        // Canvas
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        // 半透明パネル
        var panel = CreateRect("Panel", gameObject.transform);
        panel.anchorMin = new Vector2(0.15f, 0.2f);
        panel.anchorMax = new Vector2(0.85f, 0.8f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        var panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = panelColor;

        // タイトル
        var title = CreateText("Title", panel, "> DOWNLOADING DATA...", 22, textColor);
        title.anchorMin = new Vector2(0f, 0.75f);
        title.anchorMax = new Vector2(1f, 1f);
        title.offsetMin = new Vector2(20, 0);
        title.offsetMax = new Vector2(-20, -10);
        title.GetComponent<Text>().fontStyle = FontStyle.Bold;

        // ログテキスト
        var logRT = CreateText("Log", panel, "", 13, new Color(textColor.r, textColor.g, textColor.b, 0.7f));
        logRT.anchorMin = new Vector2(0f, 0.3f);
        logRT.anchorMax = new Vector2(1f, 0.75f);
        logRT.offsetMin = new Vector2(20, 0);
        logRT.offsetMax = new Vector2(-20, 0);
        logText = logRT.GetComponent<Text>();
        logText.alignment = TextAnchor.LowerLeft;

        // プログレスバー背景
        var barBg = CreateRect("BarBg", panel);
        barBg.anchorMin = new Vector2(0.05f, 0.18f);
        barBg.anchorMax = new Vector2(0.95f, 0.28f);
        barBg.offsetMin = barBg.offsetMax = Vector2.zero;
        barBg.gameObject.AddComponent<Image>().color = barBgColor;

        // プログレスバー本体
        var barFillRT = CreateRect("BarFill", barBg);
        barFillRT.anchorMin = Vector2.zero;
        barFillRT.anchorMax = new Vector2(0f, 1f);
        barFillRT.offsetMin = barFillRT.offsetMax = Vector2.zero;
        barFill = barFillRT.gameObject.AddComponent<Image>();
        barFill.color = barColor;

        // パーセント
        var pctRT = CreateText("Percent", panel, "0%", 28, textColor);
        pctRT.anchorMin = new Vector2(0f, 0.03f);
        pctRT.anchorMax = new Vector2(1f, 0.18f);
        pctRT.offsetMin = new Vector2(20, 0);
        pctRT.offsetMax = new Vector2(-20, 0);
        percentText = pctRT.GetComponent<Text>();
        percentText.alignment = TextAnchor.MiddleLeft;
        percentText.fontStyle = FontStyle.Bold;
        percentText.fontSize = 28;
    }

    public void StartDownload(float duration)
    {
        totalDuration = duration;
        gameObject.SetActive(true);
        elapsed = 0f;
        running = true;
        logText.text = "";
        StartCoroutine(LogRoutine());
    }

    public void Hide()
    {
        running = false;
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (!running) return;
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / totalDuration);

        // パーセント（少しランダムに揺らす）
        float displayPct = t * 100f + Random.Range(-0.5f, 0.5f) * (1f - t) * 3f;
        displayPct = Mathf.Clamp(displayPct, 0f, 99.9f);
        if (t >= 1f) displayPct = 100f;
        percentText.text = displayPct.ToString("F1") + "%";

        // バー
        barFill.rectTransform.anchorMax = new Vector2(t, 1f);

        // バーの点滅（高速）
        float blink = Mathf.Sin(Time.time * 18f) * 0.15f + 0.85f;
        barFill.color = new Color(barColor.r * blink, barColor.g * blink, barColor.b * blink, 1f);
    }

    private IEnumerator LogRoutine()
    {
        var lines = new System.Collections.Generic.List<string>();
        float interval = totalDuration / logLines.Length;

        foreach (var line in logLines)
        {
            if (!running) yield break;
            lines.Add(line);
            // 最大6行表示
            if (lines.Count > 6) lines.RemoveAt(0);
            logText.text = string.Join("\n", lines);
            yield return new WaitForSeconds(interval * Random.Range(0.6f, 1.4f));
        }
    }

    // ─── ヘルパー ───────────────────────────────
    private RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private RectTransform CreateText(string name, RectTransform parent, string content, int size, Color color)
    {
        var rt = CreateRect(name, parent);
        var txt = rt.gameObject.AddComponent<Text>();
        txt.text = content;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = size;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleLeft;
        return rt;
    }
}
