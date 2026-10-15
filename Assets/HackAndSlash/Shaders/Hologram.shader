// Blade Runner 2049 advertising hologram: additive, fresnel-rimmed, scanlined, with a rolling interference band,
// flicker, and horizontal slice displacement while _Glitch is up.
Shader "VoidArena/Hologram"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.4, 1.6, 2.4, 1)
        _NoiseTex ("Interference (R scan, G band, B noise)", 2D) = "gray" {}
        _ScanDensity ("Scanlines per metre", Float) = 9
        _Glitch ("Glitch", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 2)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _NoiseTex_ST;
                float _ScanDensity;
                half _Glitch;
                half _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            float Hash(float n) { return frac(sin(n * 127.1) * 43758.5453); }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                // glitch: slice the figure into horizontal bands and shove them sideways
                float band = floor(ws.y * 1.7 + floor(_Time.y * 24.0));
                float shove = (Hash(band) - 0.5) * 2.0 * step(0.55, Hash(band + 3.1));
                float3 right = normalize(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                ws += right * shove * _Glitch * 0.9;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 n = normalize(input.normalWS);
                half fresnel = pow(1.0 - saturate(abs(dot(n, v))), 2.2);
                float y = input.positionWS.y;
                half scan = 0.55 + 0.45 * step(0.5, frac(y * _ScanDensity - _Time.y * 1.3));
                half roll = smoothstep(0.0, 0.08, 0.08 - abs(frac(y * 0.035 - _Time.y * 0.12) - 0.5) + 0.04);
                half3 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(input.positionWS.x * 0.05 + _Time.y * 0.05, y * 0.05 - _Time.y * 0.2)).rgb;
                half flicker = 0.88 + 0.12 * sin(_Time.y * 61.0) * sin(_Time.y * 7.3);
                half body = 0.18 + fresnel * 1.35 + roll * 0.8;
                half3 c = _Color.rgb * body * scan * flicker * (0.75 + 0.5 * noise.b) * _Opacity;
                c *= 1.0 + _Glitch * 1.5 * step(0.7, Hash(floor(y * 6.0) + floor(_Time.y * 30.0)));
                c *= ComputeFogIntensity(input.fogFactor);
                return half4(c, 0.0);
            }
            ENDHLSL
        }
    }
}
