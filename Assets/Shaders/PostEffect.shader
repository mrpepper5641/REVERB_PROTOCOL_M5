Shader "Hidden/PostEffect"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}

        _VignetteIntensity ("Vignette Intensity", Range(0, 2)) = 0.9
        _VignetteSmoothness ("Vignette Smoothness", Range(0.01, 1)) = 0.7
        _VignetteColor ("Vignette Color", Color) = (0, 0, 0, 1)

        //_ScanlineCount ("Scanline Count", Range(100, 1500)) = 600 初期値
        _ScanlineCount ("Scanline Count", Range(100, 1500)) = 450
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.15

        //_AberrationStrength ("Aberration Strength", Range(0, 0.05)) = 0.005 初期値
        _AberrationStrength ("Aberration Strength", Range(0, 0.05)) = 0.01

        //_GrainStrength ("Grain Strength", Range(0, 0.5)) = 0.08 初期値
        _GrainStrength ("Grain Strength", Range(0, 0.5)) = 0.1
        // Ghost glow (set from PostEffect.cs at runtime)
        _GhostOffset ("Ghost Offset", Range(0, 0.2)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;

            float _VignetteIntensity;
            float _VignetteSmoothness;
            fixed4 _VignetteColor;

            float _ScanlineCount;
            float _ScanlineIntensity;

            float _AberrationStrength;
            float _GrainStrength;
            float _GhostOffset;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // pseudo-random hash, returns 0..1
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centeredUV = i.uv - 0.5;

                // ----- Chromatic Aberration -----
                float2 dir = centeredUV;
                float2 rOffset = dir * _AberrationStrength;
                float2 bOffset = dir * -_AberrationStrength;

                fixed4 col;
                col.r = tex2D(_MainTex, i.uv + rOffset).r;
                col.g = tex2D(_MainTex, i.uv).g;
                col.b = tex2D(_MainTex, i.uv + bOffset).b;
                col.a = 1.0;

                // ----- Scanlines -----
                float scan = sin(i.uv.y * _ScanlineCount * 3.14159) * 0.5 + 0.5;
                float scanFactor = lerp(1.0 - _ScanlineIntensity, 1.0, scan);
                col.rgb *= scanFactor;

                // ----- Film Grain -----
                float grain = hash(i.uv + _Time.y) - 0.5;  // -0.5 .. +0.5
                col.rgb += grain * _GrainStrength;

                // ----- Ghost Glow: 四方八方ランダム (R / B / Cyan / Magenta) -----
                if (_GhostOffset > 0.001)
                {
                    float g = _GhostOffset * 0.18; // 大きめオフセット（読めなくてOK）
                    float tStep = floor(_Time.y * 3.0); // 3Hz でランダム方向切替

                    float a1 = frac(sin(tStep * 127.1          ) * 43758.5453) * 6.2832;
                    float a2 = frac(sin(tStep * 311.7 + 17.3   ) * 43758.5453) * 6.2832;
                    float a3 = frac(sin(tStep *  74.3 +  5.19  ) * 43758.5453) * 6.2832;
                    float a4 = frac(sin(tStep * 191.9 +  8.65  ) * 43758.5453) * 6.2832;

                    // R
                    fixed3 s1 = tex2D(_MainTex, i.uv + float2(cos(a1), sin(a1)) * g).rgb;
                    col.r += dot(s1, float3(0.299,0.587,0.114)) * s1.r * _GhostOffset * 4.0;

                    // B
                    fixed3 s2 = tex2D(_MainTex, i.uv + float2(cos(a2), sin(a2)) * g).rgb;
                    col.b += dot(s2, float3(0.299,0.587,0.114)) * s2.b * _GhostOffset * 4.0;

                    // Cyan (G+B)
                    fixed3 s3 = tex2D(_MainTex, i.uv + float2(cos(a3), sin(a3)) * g).rgb;
                    float  l3 = dot(s3, float3(0.299,0.587,0.114));
                    col.g += l3 * s3.g * _GhostOffset * 2.5;
                    col.b += l3 * s3.b * _GhostOffset * 2.5;

                    // Magenta (R+B)
                    fixed3 s4 = tex2D(_MainTex, i.uv + float2(cos(a4), sin(a4)) * g).rgb;
                    float  l4 = dot(s4, float3(0.299,0.587,0.114));
                    col.r += l4 * s4.r * _GhostOffset * 3.0;
                    col.b += l4 * s4.b * _GhostOffset * 3.0;
                }

                // ----- Vignette -----
                float dist = length(centeredUV);
                float mask = smoothstep(_VignetteSmoothness, 0.0, dist * _VignetteIntensity);
                col.rgb = lerp(_VignetteColor.rgb, col.rgb, mask);

                return col;
            }
            ENDCG
        }
    }
}