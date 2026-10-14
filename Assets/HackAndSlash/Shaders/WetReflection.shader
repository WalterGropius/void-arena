// Planar reflection overlay for the rain-soaked roof. Samples the mirrored camera (PlanarReflection.cs) in screen
// space, masked by the puddle map and a Fresnel term, and adds it on top of the lit floor.
Shader "VoidArena/WetReflection"
{
    Properties
    {
        _MaskTex ("Puddle Mask (R)", 2D) = "white" {}
        _NormalTex ("Distortion Normal", 2D) = "bump" {}
        _Tile ("World Tile Size (m)", Float) = 3.5
        _Strength ("Strength", Float) = 0.9
        _DampReflect ("Reflection Outside Puddles", Range(0, 1)) = 0.18
        _MinFresnel ("Min Fresnel", Range(0, 1)) = 0.35
        _Distortion ("Distortion", Float) = 0.012
        _Blur ("Blur (mip)", Range(0, 6)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WetReflection"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                float4 _NormalTex_ST;
                float _Tile;
                float _Strength;
                float _DampReflect;
                float _MinFresnel;
                float _Distortion;
                float _Blur;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 world = input.positionWS.xz / _Tile;
                half puddle = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, world).r;
                half2 n = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, world).xy * 2.0 - 1.0;
                // slow shimmer, as if rain keeps disturbing the water
                half2 n2 = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, world * 3.1 + _Time.y * float2(0.031, 0.047)).xy * 2.0 - 1.0;

                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                uv.x = 1.0 - uv.x; // the reflection camera is a proper (non-mirrored) camera, so its image is flipped horizontally
                uv += (n * (1.0 - puddle) * 2.0 + n2 * 0.6) * _Distortion;
                half lod = _Blur * (1.0 - puddle * 0.7);
                half3 refl = SAMPLE_TEXTURE2D_LOD(_ReflectionTex, sampler_ReflectionTex, uv, lod).rgb;

                half3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half ndv = saturate(v.y);
                half fresnel = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
                half mask = lerp(_DampReflect, 1.0, puddle);
                half3 c = refl * mask * lerp(_MinFresnel, 1.0, fresnel) * _Strength;
                c *= ComputeFogIntensity(input.fogFactor);
                return half4(c, 0.0);
            }
            ENDHLSL
        }
    }
}
