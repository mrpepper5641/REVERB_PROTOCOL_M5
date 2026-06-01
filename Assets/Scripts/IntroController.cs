using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// イントロシーン:
///   1. mainVisual.png を表示
///   2. M5 接続待ち（接続されるまで "AWAITING DEVICE..." を点滅）
///   3. 接続後 "PRESS ANY BUTTON" を表示
///   4. M5 の BtnB または BtnC で Main シーンへ遷移
/// </summary>
public class IntroController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M5Reader m5Reader;
    [SerializeField] private RawImage displayImage;
    [SerializeField] private Texture2D mainVisualTexture;
    [SerializeField] private Text bootText;
    [SerializeField] private Text statusText;   // 接続状態 / プロンプト表示用

    [Header("Timing")]
    [SerializeField] private float fadeDuration = 1.0f;
    [SerializeField] private string nextSceneName = "Main";

    private bool transitioning = false;
    private bool m5WasConnected = false;
    private CanvasGroup canvasGroup;

    private static readonly string[] bootLines =
    {
        "> SYSTEM BOOT...",
        "> CORE INTEGRITY: OK",
        "> IMU LINK: STANDBY",
        "> REVERB PROTOCOL v1.2",
    };

    void Start()
    {
        canvasGroup = GetComponentInChildren<CanvasGroup>();

        if (displayImage != null && mainVisualTexture != null)
            displayImage.texture = mainVisualTexture;

        if (statusText != null)
            statusText.text = "";

        StartCoroutine(TypeBootText());
    }

    void Update()
    {
        if (transitioning) return;
        if (m5Reader == null) return;

        bool connected = m5Reader.IsConnected;

        // 接続状態が変わったとき
        if (connected && !m5WasConnected)
        {
            m5WasConnected = true;
            if (statusText != null)
                statusText.text = "> DEVICE LINKED\n> PRESS ANY BUTTON TO START";
        }
        else if (!connected)
        {
            // 点滅: 接続待ち表示
            bool blink = (Time.time % 1.0f) < 0.5f;
            if (statusText != null && !m5WasConnected)
                statusText.text = blink ? "> AWAITING DEVICE..." : "";
        }

        // M5 接続済み＆ボタン押下で遷移
        if (connected && (m5Reader.ButtonB || m5Reader.ButtonC))
            StartCoroutine(FadeAndLoad());
    }

    private IEnumerator TypeBootText()
    {
        if (bootText == null) yield break;
        bootText.text = "";
        yield return new WaitForSeconds(0.4f);
        foreach (string line in bootLines)
        {
            bootText.text += line + "\n";
            yield return new WaitForSeconds(0.55f);
        }
    }

    private IEnumerator FadeAndLoad()
    {
        transitioning = true;

        if (statusText != null)
            statusText.text = "> INITIALIZING...";

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            yield return null;
        }

        SceneManager.LoadScene(nextSceneName);
    }
}
