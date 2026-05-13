using UnityEngine;

public class M5VisualController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M5Reader m5Reader;
    [SerializeField] private Renderer targetRenderer;

    [Header("Extrude Mapping (Pitch)")]
    [SerializeField] private float maxRollDegrees = 60f;
    [SerializeField] private float maxExtrude = 1.0f;

    [Header("Rotation Mapping (Roll)")]
    [SerializeField] private float pitchRotationMultiplier = -1.0f;
    [SerializeField] private float maxTiltDegrees = 60f;

    [Header("Smoothing")]
    [Range(0f, 1f)]
    [SerializeField] private float smoothing = 0.15f;

    [Header("Disturbance (shake)")]
    [Tooltip("この値を超えた加速度でゴーストが出る。小さいほど敏感")]
    [SerializeField] private float disturbanceThreshold = 0.5f;
    [Tooltip("振ったときの最大ゴーストオフセット")]
    [SerializeField] private float maxGhostOffset = 0.12f;
    [Tooltip("振り終わった後の減衰時間（秒）")]
    [SerializeField] private float disturbanceDecayTime = 0.4f;

    // -------------------------------------------------------
    // Button A: Plane Swap
    // Inspector に複数の Plane GameObject をドラッグして登録する。
    // 将来的にはフォトショ等で作成したテクスチャを貼った Quad/Plane を追加する想定。
    // -------------------------------------------------------
    [Header("Button A: Plane Swap")]
    [Tooltip("Aボタンでサイクルする Plane を順番に登録する")]
    [SerializeField] private GameObject[] planes;
    [Tooltip("各Planeのメッシュ向き補正（°）。Planeとは向きが違うメッシュに使う。例：Quadなら(90,0,0)など")]
    [SerializeField] private Vector3[] planeRotationOffsets;

    [Header("Button B: Hack Trigger (Plane 4 only)")]
    [SerializeField] private int hackPlaneIndex = 4;
    [SerializeField] private HackMinigame hackMinigame;

    [Header("Button B: IMU Freeze (hold)")]
    [SerializeField] private bool imuFrozen = false;

    // -------------------------------------------------------
    // Touch 1: Plane Glow
    // タッチ座標とPlane UV のズレは FlipX / FlipY で調整する。
    // M5 の向きと Unity のカメラ方向によって変わるため、
    // 実機で確認しながら切り替える。
    // -------------------------------------------------------
    [Header("Touch 1: Plane Glow")]
    [SerializeField] private float touchGlow    = 1.5f;
    [SerializeField] private float touchFalloff = 80f;
    [Tooltip("タッチX軸が左右逆の場合にチェック")]
    [SerializeField] private bool touchFlipX = false;
    [Tooltip("タッチY軸が上下逆の場合にチェック（デフォルトON）")]
    [SerializeField] private bool touchFlipY = true;

    [Header("Glitch (Auto)")]
    [SerializeField] private float glitchIntervalMin = 10f;
    [SerializeField] private float glitchIntervalMax = 30f;
    [SerializeField] private float glitchDuration    = 0.5f;
    [SerializeField] private float glitchIntensity   = 0.35f;

    [Header("Touch 3: Scanline")]
    [SerializeField] private PostEffect postEffect;
    [SerializeField] private float scanlineMin      = 150f;
    [SerializeField] private float scanlineMax      = 750f;
    [SerializeField] private float scanlineDefault  = 450f;
    [Range(0f, 1f)]
    [SerializeField] private float scanlineSmoothing = 0.08f;

    [Header("Live (read-only)")]
    [SerializeField] private float currentExtrude;
    [SerializeField] private float currentTilt;
    [SerializeField] private float currentDisturbance;
    [SerializeField] private float currentScanline = 450f;

    // --- internal state ---
    private Material   runtimeMaterial;
    private Quaternion initialRotation;         // index 0 の Plane から取得した基準向き
    private Quaternion planeBaseRotation;       // initialRotation × 現在 Plane のオフセット
    private Transform  activePlaneTransform;    // 現在アクティブな Plane の Transform
    private int        planeIndex = 0;

    private static readonly int ExtrudeAmountID = Shader.PropertyToID("_ExtrudeAmount");
    private static readonly int GhostOffsetID   = Shader.PropertyToID("_GhostOffset");
    private static readonly int TouchPointID      = Shader.PropertyToID("_TouchPoint");
    private static readonly int TouchActiveID     = Shader.PropertyToID("_TouchActive");
    private static readonly int TouchGlowID       = Shader.PropertyToID("_TouchGlow");
    private static readonly int TouchFalloffID    = Shader.PropertyToID("_TouchFalloff");
    private static readonly int GlitchIntensityID   = Shader.PropertyToID("_GlitchIntensity");
    private static readonly int ShowHackButtonID    = Shader.PropertyToID("_ShowHackButton");
    private static readonly int HackButtonPulseID   = Shader.PropertyToID("_HackButtonPulse");

    private bool  prevBtnA = false;
    private bool  prevBtnB = false;
    private bool  prevBtnC = false;

    // --- Glitch state ---
    private float glitchCooldown      = 0f;
    private float glitchTimeRemaining = 0f;

    // -------------------------------------------------------

    void Start()
    {
        // planes 配列が設定されていれば index 0 のみ有効化
        if (planes != null && planes.Length > 0)
        {
            for (int i = 0; i < planes.Length; i++)
                if (planes[i] != null) planes[i].SetActive(i == 0);
        }

        // Target Renderer 未設定なら planes[0] から自動取得
        // （PlaneManager など Renderer を持たない GO にスクリプトを置いた場合の対応）
        if (targetRenderer == null && planes != null && planes.Length > 0 && planes[0] != null)
            targetRenderer = planes[0].GetComponent<Renderer>();

        // それでも見つからない場合は自分自身を探す（後方互換）
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null)
        {
            Debug.LogError("[M5VisualController] Renderer が見つかりません。Planes 配列を設定してください。");
            return;
        }

        runtimeMaterial      = targetRenderer.material;
        activePlaneTransform = targetRenderer.transform;
        initialRotation      = activePlaneTransform.rotation;
        planeBaseRotation    = initialRotation * GetPlaneOffset(0);

        // グリッチの初回発生タイミングをランダムに設定
        glitchCooldown = Random.Range(glitchIntervalMin, glitchIntervalMax);
    }

    // index番目のPlaneに設定された補正回転を返す
    private Quaternion GetPlaneOffset(int index)
    {
        if (planeRotationOffsets == null || index >= planeRotationOffsets.Length)
            return Quaternion.identity;
        return Quaternion.Euler(planeRotationOffsets[index]);
    }

    // Plane を切り替える。direction = +1 で次、-1 で前
    private void CyclePlane(int direction = 1)
    {
        if (planes == null || planes.Length == 0) return;

        // 現在の Plane をリセット・非表示
        var current = planes[planeIndex];
        if (current != null)
        {
            current.transform.rotation = initialRotation; // ロール回転を戻す
            current.SetActive(false);
        }

        planeIndex = (planeIndex + direction + planes.Length) % planes.Length;

        // 次の Plane を表示・参照更新
        var next = planes[planeIndex];
        if (next == null) return;

        next.SetActive(true);
        // initialRotation（index 0 基準）× 各Plane固有のオフセット を適用
        planeBaseRotation           = initialRotation * GetPlaneOffset(planeIndex);
        next.transform.rotation     = planeBaseRotation;

        var r = next.GetComponent<Renderer>();
        if (r != null)
        {
            targetRenderer       = r;
            runtimeMaterial      = r.material;
            activePlaneTransform = next.transform;
        }
    }

    void Update()
    {
        if (m5Reader == null || runtimeMaterial == null) return;

        // ======================================================
        // BUTTON HANDLING
        // ======================================================
        bool currBtnA = m5Reader.ButtonA;
        bool currBtnB = m5Reader.ButtonB;
        bool currBtnC = m5Reader.ButtonC;

        // A: 立ち上がりエッジ → 前の Plane へ
        if (currBtnA && !prevBtnA)
        {
            CyclePlane(-1);
            TriggerGlitch(0.3f);
        }

        // B: Plane 4 のみ hack 起動、それ以外は IMU フリーズ
        if (currBtnB && !prevBtnB)
        {
            if (planeIndex == hackPlaneIndex && hackMinigame != null)
            {
                hackMinigame.StartHack();
                TriggerGlitch(0.5f);
            }
            else
            {
                TriggerGlitch(0.3f);
            }
        }
        imuFrozen = currBtnB && (planeIndex != hackPlaneIndex);

        // C: 立ち上がりエッジ → 次の Plane へ
        if (currBtnC && !prevBtnC)
        {
            CyclePlane(+1);
            TriggerGlitch(0.3f);
        }

        prevBtnA = currBtnA;
        prevBtnB = currBtnB;
        prevBtnC = currBtnC;

        // ======================================================
        // TOUCH HANDLING  ※ imuFrozen 中も動作する
        // ======================================================
        Vector2 touch       = m5Reader.Touch;
        bool    touchActive = touch.x >= 0f;

        if (touchActive)
        {
            // FlipX / FlipY で実機のズレを補正する
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

        // Touch 3: タッチY → 走査線の密度
        if (postEffect != null)
        {
            float targetScanline = touchActive
                ? Mathf.Lerp(scanlineMin, scanlineMax, touch.y)
                : scanlineDefault;
            currentScanline          = Mathf.Lerp(currentScanline, targetScanline, 1f - scanlineSmoothing);
            postEffect.scanlineCount = currentScanline;
        }

        // ======================================================
        // HACK BUTTON INDICATOR (Plane 4 のみ表示)
        // ======================================================
        bool onHackPlane = (planeIndex == hackPlaneIndex);
        bool hackActive  = hackMinigame != null && hackMinigame.IsActive;
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat(ShowHackButtonID,
                onHackPlane && !hackActive ? 1f : 0f);
            runtimeMaterial.SetFloat(HackButtonPulseID,
                Mathf.Abs(Mathf.Sin(Time.time * 2.5f)));
        }

        // ======================================================
        // IMU MAPPING  ※ B ホールド中 or hack 進行中はスキップ
        // ======================================================
        if (imuFrozen || hackActive) return;

        // ---------- extrude (Pitch) ----------
        float extrudeInput  = m5Reader.Pitch;
        float extrudeNorm   = Mathf.Clamp01(Mathf.Abs(extrudeInput) / maxRollDegrees);
        float targetExtrude = extrudeNorm * maxExtrude;
        currentExtrude = Mathf.Lerp(currentExtrude, targetExtrude, 1f - smoothing);
        runtimeMaterial.SetFloat(ExtrudeAmountID, currentExtrude);

        // ---------- rotation (Roll) ----------
        float rotationInput = m5Reader.Roll;
        float clampedTilt   = Mathf.Clamp(rotationInput * pitchRotationMultiplier,
                                           -maxTiltDegrees, maxTiltDegrees);
        currentTilt = Mathf.Lerp(currentTilt, clampedTilt, 1f - smoothing);
        if (activePlaneTransform != null)
            activePlaneTransform.rotation = planeBaseRotation * Quaternion.Euler(currentTilt, 0f, 0f);

        // ---------- disturbance (shake) ----------
        float accelMag       = m5Reader.Accel.magnitude;
        float rawDisturbance = Mathf.Max(0f, Mathf.Abs(accelMag - 1.0f) - disturbanceThreshold);

        if (rawDisturbance > currentDisturbance)
            currentDisturbance = rawDisturbance;
        else
        {
            float decay    = 1f / Mathf.Max(0.01f, disturbanceDecayTime);
            currentDisturbance = Mathf.Max(0f, currentDisturbance - decay * Time.deltaTime);
        }

        // ======================================================
        // GLITCH UPDATE (ボタン・自動グリッチ)
        // ======================================================
        UpdateGlitch();

        // 振りグリッチ: ボタン/自動グリッチが発動していない間のみ
        // disturbance が GlitchIntensity・GhostOffset・PostEffect を全部駆動する
        if (glitchTimeRemaining <= 0f)
        {
            float shakeI = Mathf.Clamp01(currentDisturbance);
            runtimeMaterial.SetFloat(GlitchIntensityID, shakeI * glitchIntensity);
            runtimeMaterial.SetFloat(GhostOffsetID,     shakeI * maxGhostOffset);
            if (postEffect != null)
            {
                postEffect.aberrationBoost = shakeI * glitchIntensity * 0.08f;
                postEffect.grainBoost      = shakeI * glitchIntensity * 0.25f;
            }
        }
    }

    // グリッチをトリガーする（duration < 0 のときは glitchDuration を使う）
    private void TriggerGlitch(float duration = -1f)
    {
        glitchTimeRemaining = duration > 0f ? duration : glitchDuration;
    }

    private void UpdateGlitch()
    {
        if (runtimeMaterial == null) return;

        if (glitchTimeRemaining > 0f)
        {
            // グリッチ進行中
            glitchTimeRemaining -= Time.deltaTime;
            float t = Mathf.Clamp01(glitchTimeRemaining / glitchDuration);

            // 最初の70%は全力、残り30%でフェードアウト
            float intensity = t > 0.3f
                ? glitchIntensity
                : (t / 0.3f) * glitchIntensity;

            runtimeMaterial.SetFloat(GlitchIntensityID, intensity);
            runtimeMaterial.SetFloat(GhostOffsetID,    intensity * maxGhostOffset);
            if (postEffect != null)
            {
                postEffect.aberrationBoost = intensity * 0.08f;
                postEffect.grainBoost      = intensity * 0.25f;
            }

            // 終了処理
            if (glitchTimeRemaining <= 0f)
            {
                runtimeMaterial.SetFloat(GlitchIntensityID, 0f);
                runtimeMaterial.SetFloat(GhostOffsetID,     0f);
                if (postEffect != null) { postEffect.aberrationBoost = 0f; postEffect.grainBoost = 0f; }
            }
        }
        else
        {
            // 次のグリッチまでカウントダウン
            glitchCooldown -= Time.deltaTime;
            if (glitchCooldown <= 0f)
            {
                TriggerGlitch();
                glitchCooldown = Random.Range(glitchIntervalMin, glitchIntervalMax);
            }
        }
    }
}
