using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PostEffect : MonoBehaviour
{
    [SerializeField] private Shader postShader;

    // Touch 3: M5VisualController から直接代入して変更する
    [Header("Scanline (runtime-settable)")]
    public float scanlineCount = 450f;

    // Glitch: M5VisualController がグリッチ中に一時的に加算する
    [Header("Base PostEffect Values")]
    [SerializeField] private float baseAberration = 0.03f;
    [SerializeField] private float baseGrain      = 0.10f;

    [System.NonSerialized] public float aberrationBoost = 0f;
    [System.NonSerialized] public float grainBoost      = 0f;

    private Material runtimeMaterial;
    private static readonly int ScanlineCountID    = Shader.PropertyToID("_ScanlineCount");
    private static readonly int AberrationStrengthID = Shader.PropertyToID("_AberrationStrength");
    private static readonly int GrainStrengthID    = Shader.PropertyToID("_GrainStrength");

    void OnEnable()
    {
        if (postShader == null) return;
        runtimeMaterial = new Material(postShader);
        runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
    }

    void OnDisable()
    {
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (runtimeMaterial == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        // 毎フレーム外部設定値をマテリアルへ反映（グリッチブースト込み）
        runtimeMaterial.SetFloat(ScanlineCountID,     scanlineCount);
        runtimeMaterial.SetFloat(AberrationStrengthID, baseAberration + aberrationBoost);
        runtimeMaterial.SetFloat(GrainStrengthID,     baseGrain      + grainBoost);

        Graphics.Blit(source, destination, runtimeMaterial);
    }
}