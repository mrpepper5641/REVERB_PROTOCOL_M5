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

    [Header("Download Progress UI")]
    [SerializeField] private DownloadProgressUI downloadProgressUI;

    [Header("M5 Boot Wait")]
    [Tooltip("M5起動中に表示するオーバーレイ (任意)")]
    [SerializeField] private GameObject m5BootingOverlay;

    [Header("Hack Popup")]
    [Tooltip("BtnBで表示するハックポップアップ")]
    [SerializeField] private GameObject hackPopupUI;

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
    [SerializeField] private float disturbanceThreshold  = 0.2f;
    [SerializeField] private float maxGhostOffset        = 0.08f;
    [SerializeField] private float disturbanceDecayTime  = 0.6f;

    [Header("Window Drift (IMU)")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float camDriftAmount   = 0.8f;   // カメラのXYズレ幅
    [SerializeField] private float planeDriftAmount = 0.4f;   // PlaneのXYズレ幅
    [SerializeField] private float driftSmoothing   = 0.08f;  // ドリフトのなめらかさ
    [SerializeField] private float driftDeadzone    = 3f;     // この角度以内は動かない（deg）
    [SerializeField] private float zMoveScale       = 6.0f;   // 前後移動の幅
    [SerializeField] private float zVelocityDecay  = 0.80f;  // 速度の減衰
    [SerializeField] private float zPositionDecay  = 0.88f;  // 位置の復元力
    [SerializeField] private float zAccelThreshold = 0.06f;  // ノイズカット閾値

    private Vector3 camInitialPos;
    private Vector3 camCurrentOffset;
    private Vector3 planeInitialPos;
    private Vector3 planeCurrentOffset;
    private float   planeCurrentZ;
    private float   zVelocity;
    private Vector3 prevAccel;

    [Header("Touch Glow")]
    [SerializeField] private float touchGlow    = 1.5f;
    [SerializeField] private float touchFalloff = 300f;
    [SerializeField] private bool  touchFlipX   = false;
    [SerializeField] private bool  touchFlipY   = true;

    [Header("Glitch (Auto)")]
    [SerializeField] private float glitchIntervalMin = 2f;
    [SerializeField] private float glitchIntervalMax = 7f;
    [SerializeField] private float glitchDuration    = 0.8f;
    [SerializeField] private float glitchIntensity   = 0.65f;
    [SerializeField] [Range(0f,1f)] private float monoGlitchChance = 0.35f;

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
        Booting,        // M5 boot完了待ち（操作不可）
        Login,          // Plane0: 初期状態（BtnBでグリッチ→ポップアップ）
        HackReady,      // Plane0: ポップアップ表示中（BtnBでハック開始）
        Hacking,        // Plane0: ハック中（成功を待つ）
        LogDisplay,     // Plane1: 自動表示、BtnC で次へ
        DownloadReady,  // Plane2: 表示済み、BtnBでダウンロード開始
        Downloading,    // Plane2: ダウンロード演出中
        DownloadDone    // 完了画面
    }
    private FlowState flowState = FlowState.Booting;
    private float bootingFallbackTimer = 0f;
    private const float BOOTING_FALLBACK_SEC = 12f; // M5未接続でも12秒後に解除
    private float downloadTimer = 0f;

    // 全画面グリッチ用
    private float bigGlitchTimer = 0f;
    private const float BigGlitchDuration = 0.8f;
    private const float BigGlitchGhostOffset = 0.2f;

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
    private bool prevConnected = false;
    private bool connectSEPlayed = false;
    private bool hackFailed = false;

    private float glitchCooldown        = 0f;
    private float glitchTimeRemaining   = 0f;
    private float connectGlitchTimer    = 0f;
    private float monoTimer             = 0f;
    private const float ConnectGlitchDuration = 2.5f;

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
        if (m5BootingOverlay != null)  m5BootingOverlay.SetActive(true);

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null) camInitialPos = mainCamera.transform.position;
        if (planes != null && planes.Length > 0 && planes[0] != null)
            planeInitialPos = planes[0].transform.position;
    }

    void Update()
    {
        if (m5Reader == null || runtimeMaterial == null) return;

        bool currBtnA = m5Reader.ButtonA;
        bool currBtnB = m5Reader.ButtonB;
        bool currBtnC = m5Reader.ButtonC;

        bool currConnected = m5Reader.IsConnected;
        if (currConnected && !prevConnected) OnM5Connected();
        prevConnected = currConnected;

        HandleFlow(currBtnA, currBtnB, currBtnC);
        HandleTouch();
        HandleIMU();
        HandleWindowDrift();
        UpdateGlitch();
        UpdateBigGlitch();
        UpdateConnectGlitch();

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
            // ── M5 boot完了待ち ─────────────────────────────────
            case FlowState.Booting:
                bootingFallbackTimer += Time.deltaTime;
                bool bootDone = (m5Reader != null && m5Reader.IsBootDone)
                             || bootingFallbackTimer >= BOOTING_FALLBACK_SEC;
                if (bootDone)
                {
                    if (m5BootingOverlay != null) m5BootingOverlay.SetActive(false);
                    flowState = FlowState.Login;
                }
                return; // Booting中は以降のフロー処理をスキップ

            // ── Plane0: LOGIN（初期）────────────────────────────
            case FlowState.Login:
                if (runtimeMaterial != null)
                    runtimeMaterial.SetFloat(ShowHackButtonID, 0f);

                // BtnB → 全画面グリッチ + ポップアップ表示
                if (currBtnB && !prevBtnB)
                {
                    TriggerBigGlitch();
                    if (hackPopupUI != null)
                        hackPopupUI.SetActive(true);
                    flowState = FlowState.HackReady;
                }
                break;

            // ── Plane0: ポップアップ表示中 ──────────────────────
            case FlowState.HackReady:
                if (runtimeMaterial != null)
                    runtimeMaterial.SetFloat(ShowHackButtonID, 0f);

                // BtnB → ハック開始
                if (currBtnB && !prevBtnB)
                {
                    if (hackPopupUI != null)
                        hackPopupUI.SetActive(false);
                    hackMinigame?.StartHack();
                    TriggerGlitch(0.5f);
                    flowState = FlowState.Hacking;
                }
                break;

            // ── Plane0: ハック中（成功/失敗待ち）───────────────
            case FlowState.Hacking:
                if (hackMinigame != null)
                {
                    if (hackMinigame.CurrentState == HackMinigame.State.Success)
                    {
                        hackFailed = false;
                        GoToPlane(1);
                        flowState = FlowState.LogDisplay;
                        TriggerGlitch(0.8f);
                    }
                    else if (hackMinigame.CurrentState == HackMinigame.State.Fail)
                    {
                        hackFailed = true;
                    }
                    else if (hackFailed && hackMinigame.CurrentState == HackMinigame.State.Idle)
                    {
                        hackFailed = false;
                        if (hackPopupUI != null) hackPopupUI.SetActive(true);
                        flowState = FlowState.HackReady;
                    }
                }
                break;

            // ── Plane1: logDisplay ────────────────────────────
            case FlowState.LogDisplay:
                if (runtimeMaterial != null)
                    runtimeMaterial.SetFloat(ShowHackButtonID, 0f);

                // BtnC で Plane2へ（ダウンロード待機）
                if (currBtnC && !prevBtnC)
                {
                    GoToPlane(2);
                    flowState = FlowState.DownloadReady;
                    TriggerGlitch(0.3f);
                }
                break;

            // ── Plane2: ダウンロード待機（BtnBで開始）──────────
            case FlowState.DownloadReady:
                if (runtimeMaterial != null)
                    runtimeMaterial.SetFloat(ShowHackButtonID, 0f);

                // BtnB でダウンロード演出開始
                if (currBtnB && !prevBtnB)
                {
                    flowState = FlowState.Downloading;
                    downloadTimer = 0f;
                    TriggerGlitch(0.3f);
                    if (downloadProgressUI != null)
                        downloadProgressUI.StartDownload(downloadDuration);
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
                    if (downloadProgressUI != null) downloadProgressUI.Hide();
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
    // ウィンドウドリフト（IMU連動）
    // -------------------------------------------------------
    private void HandleWindowDrift()
    {
        if (mainCamera == null) return;

        float roll  = m5Reader.Roll;
        float pitch = m5Reader.Pitch;

        // デッドゾーン適用
        float driftX = Mathf.Abs(roll)  > driftDeadzone ? roll  / maxTiltDegrees : 0f;
        float driftY = Mathf.Abs(pitch) > driftDeadzone ? pitch / maxRollDegrees : 0f;
        driftX = Mathf.Clamp(driftX, -1f, 1f);
        driftY = Mathf.Clamp(driftY, -1f, 1f);

        // カメラ: 傾きと同方向にズレる（覗き込む感覚）
        Vector3 targetCamOffset = new Vector3(driftX * camDriftAmount, -driftY * camDriftAmount, 0f);
        camCurrentOffset = Vector3.Lerp(camCurrentOffset, targetCamOffset, 1f - driftSmoothing);
        mainCamera.transform.position = camInitialPos + camCurrentOffset;

        // Plane: カメラと逆方向に動く（パララックス）
        Vector3 targetPlaneOffset = new Vector3(-driftX * planeDriftAmount, driftY * planeDriftAmount, 0f);
        planeCurrentOffset = Vector3.Lerp(planeCurrentOffset, targetPlaneOffset, 1f - driftSmoothing);

        // Z軸: pitch/rollで重力成分を除去して動的加速度を取得
        Vector3 accel = m5Reader.Accel;
        float pitchRad = m5Reader.Pitch * Mathf.Deg2Rad;
        float rollRad  = m5Reader.Roll  * Mathf.Deg2Rad;

        // 重力をデバイス座標系に変換して引く
        float gravX = -Mathf.Sin(rollRad);
        float gravY =  Mathf.Cos(pitchRad) * Mathf.Cos(rollRad);
        float gravZ =  Mathf.Sin(pitchRad);

        float dynX = accel.x - gravX;
        float dynY = accel.y - gravY;
        float dynZ = accel.z - gravZ;

        // 最も大きい動的成分を「前後動作」として使う
        float dominantDyn = Mathf.Abs(dynY) > Mathf.Abs(dynZ) ? dynY : dynZ;

        if (Mathf.Abs(dominantDyn) > zAccelThreshold)
            zVelocity += dominantDyn * zMoveScale;
        zVelocity   *= zVelocityDecay;
        planeCurrentZ = planeCurrentZ * zPositionDecay + zVelocity * Time.deltaTime;

        if (activePlaneTransform != null)
        {
            activePlaneTransform.position = planeInitialPos + planeCurrentOffset
                                          + new Vector3(0f, 0f, planeCurrentZ);
        }
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
        float d = duration > 0f ? duration : glitchDuration;
        glitchTimeRemaining = d;
        if (Random.value < monoGlitchChance)
            monoTimer = d;
    }

    // 全画面グリッチ（遷移演出用）
    private void TriggerBigGlitch()
    {
        bigGlitchTimer = BigGlitchDuration;
    }

    private void UpdateBigGlitch()
    {
        if (bigGlitchTimer <= 0f) return;
        bigGlitchTimer -= Time.deltaTime;
        float t = bigGlitchTimer / BigGlitchDuration; // 1→0

        float intensity = Mathf.Sin(t * Mathf.PI); // 0→1→0 の山形
        float ghostOffset = intensity * BigGlitchGhostOffset;

        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat(GlitchIntensityID, intensity);
            runtimeMaterial.SetFloat(GhostOffsetID, ghostOffset);
        }
        if (postEffect != null)
        {
            postEffect.aberrationBoost = intensity * 0.15f;
            postEffect.grainBoost      = intensity * 0.5f;
            postEffect.ghostOffset     = ghostOffset;
        }
    }

    private void OnM5Connected()
    {
        connectGlitchTimer = ConnectGlitchDuration;
        if (!connectSEPlayed)
        {
            connectSEPlayed = true;
            AudioManager.Instance?.PlayM5ConnectSE();
        }
    }

    private void UpdateConnectGlitch()
    {
        if (connectGlitchTimer <= 0f)
        {
            if (postEffect != null) postEffect.glitchIntensity = 0f;
            return;
        }
        connectGlitchTimer -= Time.deltaTime;
        float t = connectGlitchTimer / ConnectGlitchDuration; // 1→0

        // 最初は強く、だんだん落ち着く
        float intensity = Mathf.Pow(t, 0.5f);
        if (postEffect != null)
        {
            postEffect.glitchIntensity = intensity;
            postEffect.aberrationBoost = Mathf.Max(postEffect.aberrationBoost, intensity * 0.05f);
            postEffect.grainBoost      = Mathf.Max(postEffect.grainBoost,      intensity * 0.4f);
        }
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
                postEffect.wobbleIntensity = intensity * 0.9f;
            }

            // モノクロタイマー
            if (monoTimer > 0f)
            {
                monoTimer -= Time.deltaTime;
                if (postEffect != null)
                    postEffect.monoIntensity = Mathf.Clamp01(monoTimer / 0.3f);
            }
            else if (postEffect != null)
            {
                postEffect.monoIntensity = 0f;
            }

            if (glitchTimeRemaining <= 0f)
            {
                runtimeMaterial.SetFloat(GlitchIntensityID, 0f);
                runtimeMaterial.SetFloat(GhostOffsetID,     0f);
                if (postEffect != null)
                {
                    postEffect.aberrationBoost = 0f;
                    postEffect.grainBoost      = 0f;
                    postEffect.ghostOffset     = 0f;
                    postEffect.wobbleIntensity = 0f;
                }
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
