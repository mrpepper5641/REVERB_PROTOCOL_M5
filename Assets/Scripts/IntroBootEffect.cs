using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Intro シーン起動時のブートエフェクト
/// 黒画面 → フリッカー点灯 → グリッチが安定していく
/// </summary>
public class IntroBootEffect : MonoBehaviour
{
    [Header("Flicker")]
    [SerializeField] private int flickerCount = 3;

    [Header("Glitch Settle")]
    [SerializeField] private float glitchSettleDuration = 1.2f;
    [SerializeField] private float startGhostOffset = 0.18f;
    [SerializeField] private float startGlitchIntensity = 0.9f;

    [Header("References")]
    [SerializeField] private Camera mainCam;

    private Image overlay;
    private PostEffect postFx;

    void Awake()
    {
        overlay = GetComponent<Image>();
        // 最初は黒（画面オフ状態）
        if (overlay != null)
            overlay.color = new Color(0f, 0f, 0f, 1f);

        if (mainCam == null)
            mainCam = Camera.main;

        if (mainCam != null)
            postFx = mainCam.GetComponent<PostEffect>();
    }

    void Start()
    {
        SetGlitch(startGhostOffset, startGlitchIntensity);
        StartCoroutine(BootSequence());
    }

    private IEnumerator BootSequence()
    {
        // 最初は黒画面で少し待つ
        yield return new WaitForSeconds(0.3f);

        // フリッカー：黒→一瞬点灯→黒 を繰り返し、最後に点灯したまま
        for (int i = 0; i < flickerCount; i++)
        {
            float progress = (float)i / flickerCount;

            // 点灯（オーバーレイ消す）
            float onTime = Mathf.Lerp(0.04f, 0.18f, progress); // だんだん長く点灯
            SetOverlay(0f);
            yield return new WaitForSeconds(onTime);

            // 消灯（黒に戻す）- 最後の1回は消さない
            if (i < flickerCount - 1)
            {
                SetOverlay(1f);
                float offTime = Random.Range(0.05f, 0.15f);
                yield return new WaitForSeconds(offTime);
            }
        }

        // 点灯完了、オーバーレイ完全に消す
        SetOverlay(0f);

        // グリッチを徐々に安定させる
        float elapsed = 0f;
        while (elapsed < glitchSettleDuration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / glitchSettleDuration;
            float curve = 1f - Mathf.Pow(progress, 0.6f);

            SetGlitch(startGhostOffset * curve, startGlitchIntensity * curve);

            // ランダムな短い暗転フリッカー
            if (Random.value < 0.015f)
            {
                SetOverlay(0.6f);
                yield return new WaitForSeconds(0.03f);
                SetOverlay(0f);
            }

            yield return null;
        }

        SetGlitch(0f, 0f);
        SetOverlay(0f);

        // 自分自身を無効化（以後は不要）
        gameObject.SetActive(false);
    }

    private void SetOverlay(float blackAlpha)
    {
        if (overlay != null)
            overlay.color = new Color(0f, 0f, 0f, blackAlpha);
    }

    private void SetGlitch(float ghostOffset, float glitchIntensity)
    {
        if (postFx != null)
        {
            postFx.ghostOffset = ghostOffset;
            postFx.aberrationBoost = glitchIntensity * 0.05f;
            postFx.grainBoost = glitchIntensity * 0.2f;
        }
    }
}
