using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// フロー:
///   Plane0(LOGIN) → BtnB でハック開始 → ハック成功で自動 Plane1
///   Plane1(logDisplay) → BtnC で Plane2
///   Plane2(downloadWindow) → BtnB でダウンロード演出 → 完了画面
/// </summary>
public class M5VisualController : MonoBehaviour
{
    // -------------------------------------------------------
    [Header("References")]
    [SerializeField] private M5Reader m5Reader;
    [SerializeField] private Renderer targetRenderer;

    [Header("Planes (0=LOGIN / 1=logDisplay / 2=downloadWindow)")]
    [SerializeField] private GameObject[] planes;
    [SerializeField] private Vector3[] planeRotationOffsets;

    [Header("Hack Minigame")]
    [SerializeField] private HackMinigame hackMinigame;

    [Header("Download Complete UI")]
    [Tooltip("ダウンロード完了時に表示する Canvas/Panel")]
    [SerializeField] private GameObject downloadCompleteUI;
    [Tooltip("ダウンロード演出の秒数")]
    [SerializeField] private float downloadDuration = 4f;

    // -------------------------------------------------------
    [Header("Extrude / Rotation")]
    [SerializeField] private float maxRollDegrees = 60f;
    [SerializeField] private float maxExtrude     = 1.0f;
    [SerializeField] private float pitchRotationMultiplier = -1.0f;
    [SerializeField] private float maxTiltDegrees = 60f;

    [Header("Smoothing")]
    [Range(0f, 1f)]
    [SerializeField] private float smoothing = 0.15f;

    [Header("Disturbance (shake)")]
    [SerializeField] private float disturbanceThreshold  = 0.5f;
    [SerializeField] private float maxGhostOffset        = 0.04f;
    [SerializeField] private float disturbanceDecayTime  = 0.4f;

    [Header("Touch Glow")]
    [SerializeField] private float touchGlow    = 1.5f;
    [SerializeField] private float touchFalloff = 80f;
    [SerializeField] private bool  touchFlipX   = false;
    [SerializeField] private bool  touchFlipY   = true;

    [Header("Glitch (Auto)")]
    [SerializeField] private float glitchIntervalMin = 10f;
    [SerializeField] private float glitchIntervalMax = 30f;
    [SerializeField] private float glitchDuration    = 0.5f;
    [SerializeField] private float glitchIntensity   = 0.35f;

    [Header("Scanline")]
    [SerializeField] private PostEffect postEffect;
    [SerializeField] private float scanlineMin      = 150f;
    [SerializeField] private float scanlineMax      = 750f;
    [SerializeField] private float scanlineDefault  = 450f;
    [Range(0f,1f)]
    [SerializeField] private float scanlineSmoothing = 0.08f;

    [Header("Live (read-only)")]
    [SerializeField] private float currentExtrude;
    [SerializeField] private float currentTilt;
    [SerializeField] private float currentDisturbance;
    [SerializeField] private float currentScanline = 450f;
    [SerializeField] private int   currentPlaneIndex;

    // -------------------------------------------------------
    // フローステート
    // -------------------------------------------------------
    private enum FlowState
    {
        Login,          // Plane0: BtnB でハック開始
        LogDisplay,     // Plane1: 自動表示、BtnC で次へ
        Downloading,    // Plane2: ダウンロード演出中
        DownloadDone    // 完了画面
    }
    private FlowState flowState = FlowState.Login;
    private float downloadTimer = 0f;

    // -------------------------------------------------------
    // 内部変数
    // -------------------------------------------------------
    private Material   runtimeMaterial;
    private Quaternion initialRotation;
    private Quaternion planeBaseRotation;
    private Transform  activePlaneTransform;
    private int        planeIndex = 0;

    private static readonly int ExtrudeAmountID    = Shader.PropertyToID("_ExtrudeAmount");
    private static readonly int GhostOffsetID      = Shader.PropertyToID("_GhostOffset");
    private static readonly int TouchPointID       = Shader.PropertyToID("_TouchPoint");
    private static readonly int TouchActiveID      = Shader.PropertyToID("_TouchActive");
    private static readonly int TouchGlowID        = Shader.PropertyToID("_TouchGlow");
    private static readonly int TouchFalloffID     = Shader.PropertyToID("_TouchFalloff");
    private static readonly int GlitchIntensityID  = Shader.PropertyToID("_GlitchIntensity");
    private static readonly int ShowHackButtonID   = Shader.PropertyToID("_ShowHackButton");
    private static readonly int HackButtonPulseID  = Shader.PropertyToID("_HackButtonPulse");

    private bool prevBtnA = false;
    private bool prevBtnB = false;
    private bool prevBtnC = false;

    private float glitchCooldown      = 0f;
    private float glitchTimeRemaining = 0f;

    // -------------------------------------------------------

    void Start()
    {
        if (M5Reader.Instance != null) m5Reader = M5Reader.Instance;

        if (planes != null && planes.Length > 0)
            for (int i = 0; i < planes.Length; i++)
                if (planes[i] != null) planes[i].SetActive(i == 0);

        if (targetRenderer == null && planes != null && planes.Length > 0 && planes[0] != null)
            targetRenderer = planes[0].GetComponent<Renderer>();
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null) { Debug.LogError("[M5VC] Renderer not found."); return; }

        runtimeMaterial      = targetRenderer.material;
        activePlaneTransform = targetRenderer.transform;
        initialRotation      = activePlaneTransform.rotation;
        planeBaseRotation    = initialRotation * GetPlaneOffset(0);
        glitchCooldown       = Random.Range(glitchIntervalMin, glitchIntervalMax);

        if (downloadCompleteUI != null) downloadCompleteUI.SetActive(false);
    }

    void Update()
    {
        if (m5Reader == null || runtimeMaterial == null) return;

        bool currBtnA = m5Reader.ButtonA;
        bool currBtnB = m5Reader.ButtonB;
        bool currBtnC = m5Reader.ButtonC;

        HandleFlow(currBtnA, currBtnB, currBtnC);
        HandleTouch();
        HandleIMU();
        UpdateGlitch();

        prevBtnA = currBtnA;
        prevBtnB = currBtnB;
        prevBtnC = currBtnC;
    }

    // -------------------------------------------------------
    // フロー制御
    // -------------------------------------------------------
    private void HandleFlow(bool currBtnA, bool currBtnB, bool currBtnC)
    {
        switch (flowState)
        {
            // ── Plane0: LOGIN ─────────────────────────────────
            case FlowState.Login:
                // ハックボタンインジケーター（円リング）は非表示
                bool hackActive = hackMinigame != null && hackMinigame.IsActive;
                if (runtimeMaterial != null)
                {
                    runtimeMaterial.SetFloat(ShowHackButtonID, 0f);
                }

                // BtnB でハック開始
                if (currBtnB && !prevBtnB && !hackActive)
                {
                    hackMinigame?.StartHack();
                    TriggerGlitch(0.5f);
                }

                // ハック成功 → Plane1へ自動遷移
                if (hackMinigame != null &&
                    hackMinigame.CurrentState == HackMinigame.State.Success)
                {
                    GoToPlane(1);
                    flowState = FlowState.LogDisplay;
                    TriggerGlitch(0.8f);
                }
                break;

            // ── Plane1: logDisplay ────────────────────────────
            case FlowState.LogDisplay:
                if (runtimeMaterial != null)
                    runtimeMaterial.SetFloat(ShowHackButtonID, 0f);

                // BtnC で Plane2へ
                if (currBtnC && !prevBtnC)
                {
                    GoToPlane(2);
                    flowState = FlowState.Downloading;
                    downloadTimer = 0f;
                    TriggerGlitch(0.3f);
                }
                break;

            // ── Plane2: ダウンロード演出 ───────────────────────
            case FlowState.Downloading:
                if (runtimeMaterial != null)
                    runtimeMaterial.SetFloat(ShowHackButtonID, 0f);

                downloadTimer += Time.deltaTime;
                if (downloadTimer >= downloadDuration)
                {
                    flowState = FlowState.DownloadDone;
                    if (downloadCompleteUI != null) downloadCompleteUI.SetActive(true);
                    TriggerGlitch(1.0f);
                }
                break;

            case FlowState.DownloadDone:
                // 完了画面を表示したまま終了
                break;
        }
    }

    // -------------------------------------------------------
    private void GoToPlane(int index)
    {
        if (planes == null || index >= planes.Length) return;

        var current = planes[planeIndex];
        if (current != null)
        {
            current.transform.rotation = initialRotation;
            current.SetActive(false);
        }

        planeIndex = index;
        currentPlaneIndex = index;

        var next = planes[planeIndex];
        if (next == null) return;
        next.SetActive(true);

        planeBaseRotation        = initialRotation * GetPlaneOffset(planeIndex);
        next.transform.rotation  = planeBaseRotation;

        var r = next.GetComponent<Renderer>();
        if (r != null)
        {
            targetRenderer       = r;
            runtimeMaterial      = r.material;
            activePlaneTransform = next.transform;
        }
    }

    private Quaternion GetPlaneOffset(int index)
    {
        if (planeRotationOffsets == null || index >= planeRotationOffsets.Length)
            return Quaternion.identity;
        return Quaternion.Euler(planeRotationOffsets[index]);
    }

    // -------------------------------------------------------
    // タッチ
    // -------------------------------------------------------
    private void HandleTouch()
    {
        Vector2 touch       = m5Reader.Touch;
        bool    touchActive = touch.x >= 0f;

        if (touchActive)
        {
            float tu = touchFlipX ? 1f - touch.x : touch.x;
            float tv = touchFlipY ? 1f - touch.y : touch.y;
            runtimeMaterial.SetVector(TouchPointID, new Vector4(tu, tv, 0f, 0f));
            runtimeMaterial.SetFloat(TouchActiveID, 1f);
        }
        else
        {
            runtimeMaterial.SetFloat(TouchActiveID, 0f);
        }
        runtimeMaterial.SetFloat(TouchGlowID,    touchGlow);
        runtimeMaterial.SetFloat(TouchFalloffID, touchFalloff);

        if (postEffect != null)
        {
            float targetScanline = touchActive
                ? Mathf.Lerp(scanlineMin, scanlineMax, touch.y)
                : scanlineDefault;
            currentScanline          = Mathf.Lerp(currentScanline, targetScanline, 1f - scanlineSmoothing);
            postEffect.scanlineCount = currentScanline;
        }
    }

    // -------------------------------------------------------
    // IMU
    // -------------------------------------------------------
    private void HandleIMU()
    {
        bool hackActive = hackMinigame != null && hackMinigame.IsActive;
        bool imuFrozen  = m5Reader.ButtonB && (flowState != FlowState.Login);
        if (imuFrozen || hackActive) return;

        // Extrude (Pitch)
        float extrudeNorm   = Mathf.Clamp01(Mathf.Abs(m5Reader.Pitch) / maxRollDegrees);
        currentExtrude      = Mathf.Lerp(currentExtrude, extrudeNorm * maxExtrude, 1f - smoothing);
        runtimeMaterial.SetFloat(ExtrudeAmountID, currentExtrude);

        // Rotation (Roll)
        float clampedTilt = Mathf.Clamp(m5Reader.Roll * pitchRotationMultiplier,
                                        -maxTiltDegrees, maxTiltDegrees);
        currentTilt = Mathf.Lerp(currentTilt, clampedTilt, 1f - smoothing);
        if (activePlaneTransform != null)
            activePlaneTransform.rotation = planeBaseRotation * Quaternion.Euler(currentTilt, 0f, 0f);

        // Disturbance (Shake)
        float accelMag       = m5Reader.Accel.magnitude;
        float rawDisturbance = Mathf.Max(0f, Mathf.Abs(accelMag - 1.0f) - disturbanceThreshold);
        if (rawDisturbance > currentDisturbance)
            currentDisturbance = rawDisturbance;
        else
        {
            float decay = 1f / Mathf.Max(0.01f, disturbanceDecayTime);
            currentDisturbance = Mathf.Max(0f, currentDisturbance - decay * Time.deltaTime);
        }

        if (glitchTimeRemaining <= 0f)
        {
            float shakeI = Mathf.Clamp01(currentDisturbance);
            runtimeMaterial.SetFloat(GlitchIntensityID, shakeI * glitchIntensity);
            runtimeMaterial.SetFloat(GhostOffsetID,     shakeI * maxGhostOffset);
            if (postEffect != null)
            {
                postEffect.aberrationBoost = shakeI * glitchIntensity * 0.08f;
                postEffect.grainBoost      = shakeI * glitchIntensity * 0.25f;
                postEffect.ghostOffset     = shakeI * maxGhostOffset;
            }
        }
    }

    // -------------------------------------------------------
    // グリッチ
    // -------------------------------------------------------
    private void TriggerGlitch(float duration = -1f)
    {
        glitchTimeRemaining = duration > 0f ? duration : glitchDuration;
    }

    private void UpdateGlitch()
    {
        if (runtimeMaterial == null) return;

        if (glitchTimeRemaining > 0f)
        {
            glitchTimeRemaining -= Time.deltaTime;
            float t         = Mathf.Clamp01(glitchTimeRemaining / glitchDuration);
            float intensity = t > 0.3f ? glitchIntensity : (t / 0.3f) * glitchIntensity;

            runtimeMaterial.SetFloat(GlitchIntensityID, intensity);
            runtimeMaterial.SetFloat(GhostOffsetID,    intensity * maxGhostOffset);
            if (postEffect != null)
            {
                postEffect.aberrationBoost = intensity * 0.08f;
                postEffect.grainBoost      = intensity * 0.25f;
                postEffect.ghostOffset     = intensity * maxGhostOffset;
            }

            if (glitchTimeRemaining <= 0f)
            {
                runtimeMaterial.SetFloat(GlitchIntensityID, 0f);
                runtimeMaterial.SetFloat(GhostOffsetID,     0f);
                if (postEffect != null) { postEffect.aberrationBoost = 0f; postEffect.grainBoost = 0f; postEffect.ghostOffset = 0f; }
            }
        }
        else
        {
            glitchCooldown -= Time.deltaTime;
            if (glitchCooldown <= 0f)
            {
                TriggerGlitch();
                glitchCooldown = Random.Range(glitchIntervalMin, glitchIntervalMax);
            }
        }
    }
}
