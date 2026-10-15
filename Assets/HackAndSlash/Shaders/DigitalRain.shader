// The Matrix's falling code: columns of mirrored glyphs (generated atlas, 16x16 cells) streaming down a curtain,
// each column at its own speed, each glyph flickering to a new one at its own rate, with a white-hot leading glyph.
Shader "VoidArena/DigitalRain"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.25, 1.8, 0.55, 1)
        _GlyphTex ("Glyph Atlas (16x16)", 2D) = "black" {}
        _Cells ("Glyphs per UV unit", Float) = 4
        _Speed ("Fall Speed", Float) = 1
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GlyphTex); SAMPLER(sampler_GlyphTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _GlyphTex_ST;
                float _Cells;
                float _Speed;
                half _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fogFactor : TEXCOORD1; };

            float Hash(float n) { return frac(sin(n * 91.3458) * 47453.5453); }
            float Hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 g = input.uv * _Cells;
                float col = floor(g.x);
                float row = floor(g.y);
                float speed = (0.55 + 1.6 * Hash(col)) * _Speed;
                const float period = 64.0;
                // the head falls (row index decreases); rows above it hold the fading trail
                float head = period - frac(_Time.y * speed * 0.18 + Hash(col + 7.7)) * period;
                float d = fmod(row - head + period * 4.0, period);
                float trail = 6.0 + 18.0 * Hash(col + 2.3);
                half bright = d < trail ? pow(1.0 - d / trail, 1.6) : 0.0;
                half lead = d < 1.0 ? 1.0 : 0.0;
                // each cell picks a glyph, and some cells keep changing their minds
                float rate = 1.0 + 9.0 * Hash2(float2(col, row) + 0.37);
                float change = step(0.6, Hash2(float2(col, row) + 9.1));
                float gid = floor(Hash2(float2(col, row) + floor(_Time.y * rate) * change * 0.0173) * 255.99);
                float2 cell = float2(fmod(gid, 16.0), floor(gid / 16.0));
                float2 auv = (cell + frac(g)) / 16.0;
                half glyph = SAMPLE_TEXTURE2D(_GlyphTex, sampler_GlyphTex, auv).r;
                half3 c = (_Color.rgb * bright + half3(1.6, 2.2, 1.7) * lead) * glyph * _Intensity;
                c *= ComputeFogIntensity(input.fogFactor);
                return half4(c, 0.0);
            }
            ENDHLSL
        }
    }
}
