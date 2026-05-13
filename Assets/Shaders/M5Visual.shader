Shader "Unlit/M5Visual"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.054, 0.149, 0.149, 1.0)
        _LineColor ("Line Color", Color) = (0.545, 0.592, 0.314, 1.0)
        _ExtrudeAmount ("Extrude Amount", Range(0, 2)) = 0.3
        _GhostOffset ("Ghost Offset", Range(0, 0.5)) = 0.0
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
    void emitTriangle(v2g input[3],
                      inout TriangleStream<g2f> stream,
                      float2 xyShift, float extrudeFactor, float layerId,
                      float animHash, float bandPhase)
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
            vpos.x   += xyShift.x;
            vpos.y   += xyShift.y;

            if (_GlitchIntensity > 0.001)
            {
                // ① VHSスキャンバンド（レイヤーごとに位相が違うのでRGBが別々の帯でずれる）
                float band      = floor(input[i].uv.y * 8.0 + _Time.y * 0.5 + bandPhase);
                float bandNoise = frac(sin(band * 127.1 + _Time.y * 6.3 + bandPhase * 17.3)
                                      * 43758.5453);
                vpos.x += (bandNoise - 0.5) * _GlitchIntensity * 3.0;

                // ② 三角形単位のブロックノイズ
                if (animHash > 0.75)
                    vpos.x += (frac(animHash * 17.3 + _Time.y * 0.8) - 0.5)
                              * _GlitchIntensity * 4.5;
                if (animHash < 0.12)
                    vpos.y += (frac(animHash * 31.7 + _Time.y * 1.2) - 0.5)
                              * _GlitchIntensity * 2.5;
            }

            o.pos   = UnityObjectToClipPos(vpos);
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

        // ── レイヤー着色 ─────────────────────────────────────
        fixed4 col;
        fixed3 L = _LineColor.rgb;
        if      (i.layer < 1.5) col = _LineColor;
        else if (i.layer < 2.5) col = fixed4(L.r*0.90, L.g*0.06, L.b*0.06, 1.0); // 左上  R
        else if (i.layer < 3.5) col = fixed4(L.r*0.06, L.g*0.06, L.b*0.92, 1.0); // 右下  B
        else if (i.layer < 4.5) col = fixed4(L.r*0.06, L.g*0.70, L.b*0.70, 1.0); // 左下  C(cyan)
        else if (i.layer < 5.5) col = fixed4(L.r*0.65, L.g*0.06, L.b*0.65, 1.0); // 右上  M(magenta)
        else                    col = fixed4(L.r*0.25, L.g*0.25, L.b*0.25, 1.0);  // 右遠  dim

        // ── ゴーストの不透明度を GhostOffset に連動（加算パス用）──
        // GhostOffset = 0 のとき ghostAlpha = 0 → 黒 → 加算しても何も変わらない
        // GhostOffset が大きくなるほど色収差が強まる
        if (i.layer > 1.5)
        {
            float ghostAlpha = saturate(_GhostOffset * 10.0);
            col.rgb *= ghostAlpha;
        }

        // ── タッチグロー（メインレイヤーのみ）────────────────
        if (_TouchActive > 0.5 && i.layer < 1.5)
        {
            float2 diff = i.uv - _TouchPoint.xy;
            float  dist = dot(diff, diff);
            float  glow = exp(-dist * _TouchFalloff) * _TouchGlow;
            col.rgb += _LineColor.rgb * glow;
        }

        // ── ハックボタン インジケーター（メインレイヤーのみ）─────
        if (_ShowHackButton > 0.5 && i.layer < 1.5)
        {
            float2 d          = i.uv - float2(0.5, 0.5);
            float  r          = sqrt(dot(d, d));
            float  ring       = exp(-pow((r - 0.18) / 0.012, 2.0));
            float  brightness = 0.6 + 0.4 * _HackButtonPulse;
            col.rgb += fixed3(0.3, 0.9, 0.4) * ring * brightness;
        }

        return col;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        // ============================================================
        // Pass 0: メインレイヤー — 不透明で描画
        // ============================================================
        Pass
        {
            Name "MAIN"
            ZWrite On

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

                emitTriangle(input, stream, float2(0.0, 0.0), 1.0, 1.0, animHash, 0.0);
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

                //                          xyShift                      extrude layer  hash      bandPhase
                emitTriangle(input, stream, float2(-g,      g*0.6 ), 0.55, 2.0, animHash, 1.73); // 左上  R
                emitTriangle(input, stream, float2( g,     -g*0.4 ), 0.55, 3.0, animHash, 3.46); // 右下  B
                emitTriangle(input, stream, float2(-g*0.5, -g     ), 0.45, 4.0, animHash, 5.19); // 左下  C(cyan)
                emitTriangle(input, stream, float2( g*0.8,  g     ), 0.45, 5.0, animHash, 6.92); // 右上  M(magenta)
                emitTriangle(input, stream, float2( g*1.7,  g*0.15), 0.30, 6.0, animHash, 8.65); // 右遠  dim
            }
            ENDCG
        }
    }
}
