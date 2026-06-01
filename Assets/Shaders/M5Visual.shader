Shader "Unlit/M5Visual"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.054, 0.149, 0.149, 1.0)
        _LineColor ("Line Color", Color) = (0.545, 0.592, 0.314, 1.0)
        _ExtrudeAmount ("Extrude Amount", Range(0, 2)) = 0.3
        _GhostOffset ("Ghost Offset", Range(0, 0.5)) = 0.0
        // --- Texture ---
        _MainTex  ("Texture", 2D) = "white" {}
        _TexBlend ("Texture Blend", Range(0, 1)) = 0.0
        // --- Button A: render mode ---
        _RenderMode ("Render Mode", Float) = 0
        _WireWidth  ("Wire Width",  Range(0.001, 0.15)) = 0.03
        _PointSize  ("Point Size",  Range(0.05,  0.5))  = 0.18
        // --- Touch 1: plane glow ---
        _TouchPoint   ("Touch Point (UV)",    Vector) = (0.5, 0.5, 0, 0)
        _TouchActive  ("Touch Active",         Float)  = 0
        _TouchGlow    ("Touch Glow Intensity", Range(0, 3))   = 1.5
        _TouchFalloff ("Touch Glow Falloff",   Range(5, 300)) = 80
        // --- Glitch ---
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 0
        // --- Hack button indicator ---
        _ShowHackButton  ("Show Hack Button",  Float)        = 0
        _HackButtonPulse ("Hack Button Pulse", Range(0, 1))  = 0
    }

    // ================================================================
    // CGINCLUDE: 両パスで共有するコード
    // ================================================================
    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv     : TEXCOORD0;
    };

    struct v2g
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv     : TEXCOORD0;
    };

    struct g2f
    {
        float4 pos   : SV_POSITION;
        float  layer : TEXCOORD0;
        float3 bary  : TEXCOORD1;
        float2 uv    : TEXCOORD2;
    };

    sampler2D _MainTex;
    float     _TexBlend;
    fixed4 _BaseColor;
    fixed4 _LineColor;
    float  _ExtrudeAmount;
    float  _GhostOffset;
    float  _RenderMode;
    float  _WireWidth;
    float  _PointSize;
    float4 _TouchPoint;
    float  _TouchActive;
    float  _TouchGlow;
    float  _TouchFalloff;
    float  _GlitchIntensity;
    float  _ShowHackButton;
    float  _HackButtonPulse;

    v2g vert(appdata v)
    {
        v2g o;
        o.vertex = v.vertex;
        o.normal = v.normal;
        o.uv     = v.uv;
        return o;
    }

    // ── ジオメトリ補助: 三角形1枚を変形して出力 ──────────────────
    // triangle修飾子はエントリポイントのみに付けるため、ここでは外す
    // applyGlitch=1.0: メインパスのみジオメトリ歪みを適用、ゴーストパスは0.0
    void emitTriangle(v2g input[3],
                      inout TriangleStream<g2f> stream,
                      float2 xyShift, float extrudeFactor, float layerId,
                      float animHash, float bandPhase, float applyGlitch)
    {
        float3 barys[3] = {
            float3(1, 0, 0),
            float3(0, 1, 0),
            float3(0, 0, 1)
        };

        for (int i = 0; i < 3; i++)
        {
            g2f o;
            float4 vpos = input[i].vertex;
            vpos.xyz += input[i].normal * _ExtrudeAmount * extrudeFactor;

            // ジオメトリ歪みはメインパスのみ（ゴーストに適用すると斜め線・縁が崩れる）
            if (applyGlitch > 0.5 && _GlitchIntensity > 0.001)
            {
                // ① VHSスキャンバンド
                float band      = floor(input[i].uv.y * 8.0 + _Time.y * 0.5 + bandPhase);
                float bandNoise = frac(sin(band * 127.1 + _Time.y * 6.3 + bandPhase * 17.3)
                                      * 43758.5453);
                vpos.x += (bandNoise - 0.5) * _GlitchIntensity * 0.8;

                // ② 三角形単位のブロックノイズ
                if (animHash > 0.85)
                    vpos.x += (frac(animHash * 17.3 + _Time.y * 0.8) - 0.5)
                              * _GlitchIntensity * 1.5;
                if (animHash < 0.08)
                    vpos.y += (frac(animHash * 31.7 + _Time.y * 1.2) - 0.5)
                              * _GlitchIntensity * 1.0;
            }

            o.pos   = UnityObjectToClipPos(vpos);
            // ゴーストのクリップ空間シフト（xyShift != 0 のときのみ）
            o.pos.x += xyShift.x * o.pos.w;
            o.pos.y += xyShift.y * o.pos.w;
            o.layer = layerId;
            o.bary  = barys[i];
            o.uv    = input[i].uv;
            stream.Append(o);
        }
        stream.RestartStrip();
    }

    // ── フラグメントシェーダー（両パス共通）──────────────────────
    fixed4 frag(g2f i) : SV_Target
    {
        // ── Wire/Point モード ─────────────────────────────────
        if (_RenderMode > 0.5 && _RenderMode < 1.5)
        {
            float edge = min(i.bary.x, min(i.bary.y, i.bary.z));
            if (edge > _WireWidth) discard;
        }
        else if (_RenderMode > 1.5)
        {
            float peak = max(i.bary.x, max(i.bary.y, i.bary.z));
            if (peak < 1.0 - _PointSize) discard;
        }

        fixed4 col;

        if (i.layer < 1.5)
        {
            // ── メインレイヤー ────────────────────────────────
            // Unity Plane UV は上下・左右逆なので反転
            float2 baseUV = float2(1.0 - i.uv.x, 1.0 - i.uv.y);

            // 色収差: _GhostOffset に応じてR/B チャンネルを左右にずらす
            // → メインパスのフラグメントで完結するので Plane 外には絶対はみ出ない
            float g = _GhostOffset * 0.05; // 微細な色収差 (0.12*0.05=0.006 UV)
            float r = tex2D(_MainTex, clamp(baseUV + float2(-g, 0.0), 0.001, 0.999)).r;
            float greenV = tex2D(_MainTex, baseUV).g;
            float b = tex2D(_MainTex, clamp(baseUV + float2( g, 0.0), 0.001, 0.999)).b;
            float a = tex2D(_MainTex, baseUV).a;
            fixed4 texCol = fixed4(r, greenV, b, a);

            col.rgb = lerp(_LineColor.rgb, texCol.rgb, _TexBlend);
            col.a   = lerp(1.0,           texCol.a,   _TexBlend);

            // タッチグロー
            if (_TouchActive > 0.5)
            {
                float2 diff = i.uv - _TouchPoint.xy;
                float  dist = dot(diff, diff);
                float  glow = exp(-dist * _TouchFalloff) * _TouchGlow;
                col.rgb += _LineColor.rgb * glow;
            }
        }
        else
        {
            // ── ゴーストレイヤー: GhostOffset=0のとき非表示、振ったとき出現 ──
            float go = saturate(_GhostOffset * 25.0);
            if      (i.layer < 2.5) col = fixed4(0.06, 0.0,  0.0,  0.0) * go; // R
            else if (i.layer < 3.5) col = fixed4(0.0,  0.0,  0.06, 0.0) * go; // B
            else if (i.layer < 4.5) col = fixed4(0.0,  0.05, 0.05, 0.0) * go; // Cyan
            else if (i.layer < 5.5) col = fixed4(0.05, 0.0,  0.05, 0.0) * go; // Magenta
            else                    col = fixed4(0.02, 0.0,  0.04, 0.0) * go; // dim purple
        }

        return col;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        // ============================================================
        // Pass 0: メインレイヤー — アルファブレンド対応
        // ============================================================
        Pass
        {
            Name "MAIN"
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma geometry geom_main
            #pragma fragment frag
            #pragma target 4.0

            [maxvertexcount(3)]   // main のみ = 1 triangle
            void geom_main(triangle v2g input[3], inout TriangleStream<g2f> stream)
            {
                float3 triCenter = (input[0].vertex.xyz
                                  + input[1].vertex.xyz
                                  + input[2].vertex.xyz) / 3.0;
                float triHash = frac(sin(dot(triCenter.xy, float2(127.1, 311.7))
                                        + triCenter.z * 74.3) * 43758.5453);
                float animHash = frac(triHash
                    + floor(_Time.y * 8.0) * 0.317 * _GlitchIntensity);

                emitTriangle(input, stream, float2(0.0, 0.0), 1.0, 1.0, animHash, 0.0, 1.0);
            }
            ENDCG
        }

        // ============================================================
        // Pass 1: ゴーストレイヤー × 5 — 加算合成（色収差エフェクト）
        //
        //  Blend One One = 出力色がバックバッファに「加算」される。
        //  ゴーストが重なる領域の色が合成され、本物の色収差になる。
        //  GhostOffset = 0 のときはゴーストが黒 → 加算しても不変。
        // ============================================================
        Pass
        {
            Name "GHOSTS"
            ZWrite Off          // 深度は書かない（メインパスが既に書いた）
            ZTest LEqual        // 深度テストは通常通り
            Blend One One       // 加算合成

            CGPROGRAM
            #pragma vertex   vert
            #pragma geometry geom_ghost
            #pragma fragment frag
            #pragma target 4.0

            [maxvertexcount(15)]   // 5 ghosts × 3 vertices
            void geom_ghost(triangle v2g input[3], inout TriangleStream<g2f> stream)
            {
                float3 triCenter = (input[0].vertex.xyz
                                  + input[1].vertex.xyz
                                  + input[2].vertex.xyz) / 3.0;
                float triHash = frac(sin(dot(triCenter.xy, float2(127.1, 311.7))
                                        + triCenter.z * 74.3) * 43758.5453);
                float animHash = frac(triHash
                    + floor(_Time.y * 8.0) * 0.317 * _GlitchIntensity);

                float g = _GhostOffset;

                // 時間ステップでランダム方向に切替（2Hz）
                float gStep = floor(_Time.y * 2.0 + triHash * 3.0);
                float gs = _GhostOffset * 0.5; // 飛び幅を拡大（0.1→0.5）

                float2 d1 = float2(frac(sin(gStep*127.1        )*43758.5453)*2.0-1.0, frac(sin(gStep*311.7        )*43758.5453)*2.0-1.0) * gs;
                float2 d2 = float2(frac(sin(gStep* 74.3+17.3   )*43758.5453)*2.0-1.0, frac(sin(gStep*191.9+17.3   )*43758.5453)*2.0-1.0) * gs;
                float2 d3 = float2(frac(sin(gStep*233.1+ 5.19  )*43758.5453)*2.0-1.0, frac(sin(gStep* 57.3+ 5.19  )*43758.5453)*2.0-1.0) * gs;
                float2 d4 = float2(frac(sin(gStep*419.7+ 8.65  )*43758.5453)*2.0-1.0, frac(sin(gStep*163.1+ 8.65  )*43758.5453)*2.0-1.0) * gs;
                float2 d5 = float2(frac(sin(gStep*512.3+11.11  )*43758.5453)*2.0-1.0, frac(sin(gStep* 93.7+11.11  )*43758.5453)*2.0-1.0) * gs;

                emitTriangle(input, stream, d1, 0.0, 2.0, animHash, 1.73, 0.0); // R
                emitTriangle(input, stream, d2, 0.0, 3.0, animHash, 3.46, 0.0); // B
                emitTriangle(input, stream, d3, 0.0, 4.0, animHash, 5.19, 0.0); // Cyan
                emitTriangle(input, stream, d4, 0.0, 5.0, animHash, 6.92, 0.0); // Magenta
                emitTriangle(input, stream, d5, 0.0, 6.0, animHash, 8.65, 0.0); // dim
            }
            ENDCG
        }
    }
}
