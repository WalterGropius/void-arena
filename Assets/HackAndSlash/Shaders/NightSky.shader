// HDRI night sky with a light-polluted horizon haze. Below the horizon the panorama fades into the city glow so the
// roof edge never reveals the photo's ground plane. _Flash is driven by lightning. Procedural stars come out when the
// storm clears (_Stars), and _SolidMix blends to a flat colour (the white Construct, or its black glitch).
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
        _Stars ("Stars", Range(0, 1)) = 0
        _SolidColor ("Solid Colour", Color) = (1, 1, 1, 1)
        _SolidMix ("Solid Mix", Range(0, 1)) = 0
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
            half _Stars;
            half4 _SolidColor;
            half _SolidMix;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            half3 StarField(float3 d)
            {
                // cells on a lat/long grid; one possible star per cell, jittered, twinkling
                float2 ll = float2(atan2(d.z, d.x), asin(clamp(d.y, -1.0, 1.0)));
                float2 g = ll * float2(220.0, 220.0);
                float2 cell = floor(g);
                float h = Hash(cell);
                float2 jitter = float2(Hash(cell + 7.1), Hash(cell + 3.7));
                float dist = length(frac(g) - jitter);
                float star = step(0.985, h) * smoothstep(0.22, 0.0, dist);
                float tw = 0.65 + 0.35 * sin(_Time.y * (2.0 + h * 9.0) + h * 40.0);
                half3 tint = lerp(half3(0.75, 0.82, 1.0), half3(1.0, 0.86, 0.7), Hash(cell + 1.3));
                return tint * star * tw * (2.0 + 6.0 * pow(Hash(cell + 9.9), 6.0));
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
                col += StarField(d) * _Stars * smoothstep(0.02, 0.25, d.y);
                col *= 1.0 + _Flash * (0.6 + 0.4 * saturate(d.y * 2.0 + 0.5));
                col = lerp(col, _SolidColor.rgb, _SolidMix);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
