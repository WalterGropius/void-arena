// HDRI night sky with a light-polluted horizon haze. Below the horizon the panorama fades into the city glow so the
// roof edge never reveals the photo's ground plane. _Flash is driven by lightning.
Shader "VoidArena/NightSky"
{
    Properties
    {
        [NoScaleOffset] _Tex ("Cubemap (HDR)", Cube) = "grey" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Float) = 0.6
        _Rotation ("Rotation", Range(0, 360)) = 0
        _HorizonColor ("City Haze", Color) = (0.12, 0.07, 0.14, 1)
        _Flash ("Lightning Flash", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Tex); SAMPLER(sampler_Tex);
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            half4 _HorizonColor;
            half _Flash;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.dir);
                float a = radians(_Rotation);
                float s = sin(a), c = cos(a);
                float3 r = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
                half3 sky = SAMPLE_TEXTURECUBE_LOD(_Tex, sampler_Tex, r, 0).rgb * _Tint.rgb * _Exposure;
                half3 haze = _HorizonColor.rgb;
                half below = smoothstep(0.03, -0.1, d.y);
                half3 col = lerp(sky, haze, below);
                col += haze * 0.9 * exp(-abs(d.y) * 7.0);
                col *= 1.0 + _Flash * (0.6 + 0.4 * saturate(d.y * 2.0 + 0.5));
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
