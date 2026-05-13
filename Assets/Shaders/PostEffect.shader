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