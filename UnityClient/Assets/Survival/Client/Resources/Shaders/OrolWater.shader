// OROL ocean: depth-tinted transparent water using a low-res terrain height texture (no depth
// texture needed), animated analytic waves for normals, sun glint, fresnel sky reflection and
// shoreline foam. Single pass, mobile friendly.
Shader "OROL/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.22, 0.62, 0.62, 0.55)
        _DeepColor ("Deep", Color) = (0.03, 0.17, 0.30, 0.92)
        _FoamColor ("Foam", Color) = (0.95, 0.97, 1.0, 1)
        _HeightTex ("Terrain Height (R = height above sea, metres)", 2D) = "black" {}
        _WorldSize ("World Size", Float) = 5000
        _DepthRange ("Depth Range", Float) = 14
        _WaveScale ("Wave Scale", Float) = 0.08
        _WaveSpeed ("Wave Speed", Float) = 1.0
        _WaveHeight ("Wave Height", Float) = 0.12
        _Glint ("Sun Glint", Range(0, 4)) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_HeightTex);
            SAMPLER(sampler_HeightTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                float4 _HeightTex_ST;
                float _WorldSize;
                float _DepthRange;
                float _WaveScale;
                float _WaveSpeed;
                float _WaveHeight;
                half _Glint;
            CBUFFER_END

            float4 _OrolSkyHorizon;
            float4 _OrolSkyZenith;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            float WaveHeight(float2 p, float t)
            {
                return sin(p.x * 0.11 + t * 0.9) * 0.5 + sin(p.y * 0.13 - t * 1.1) * 0.35 + sin((p.x + p.y) * 0.07 + t * 0.6) * 0.45;
            }

            float3 WaveNormal(float2 p, float t)
            {
                float s = _WaveScale;
                float2 q = p * s * 10.0;
                float dx = cos(q.x * 1.0 + t * 1.3) * 0.6 + cos((q.x + q.y) * 0.7 + t * 0.9) * 0.4 + cos(q.x * 2.7 - q.y * 1.3 + t * 2.1) * 0.25;
                float dz = cos(q.y * 1.2 - t * 1.1) * 0.6 + cos((q.x + q.y) * 0.7 + t * 0.9) * 0.4 + cos(q.y * 2.9 + q.x * 1.1 - t * 1.7) * 0.25;
                return normalize(float3(-dx * 0.18, 1.0, -dz * 0.18));
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _WaveSpeed;
                positionWS.y += WaveHeight(positionWS.xz, t) * _WaveHeight;
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float2 uv = i.positionWS.xz / max(_WorldSize, 1.0);
                float terrain = SAMPLE_TEXTURE2D(_HeightTex, sampler_HeightTex, uv).r; // metres relative to sea
                bool inside = all(uv > 0.0) && all(uv < 1.0);
                float depth = inside ? max(0.0, -terrain) : _DepthRange * 4.0;

                half depth01 = saturate(depth / _DepthRange);
                half4 col = lerp(_ShallowColor, _DeepColor, depth01 * depth01 * (3.0 - 2.0 * depth01));

                float3 n = WaveNormal(i.positionWS.xz, t);
                float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun = GetMainLight();

                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                half3 sky = lerp(_OrolSkyHorizon.rgb, _OrolSkyZenith.rgb, 0.35);
                col.rgb = lerp(col.rgb, sky, fresnel * 0.6);

                half ndl = saturate(dot(n, sun.direction));
                col.rgb *= (0.55 + 0.45 * ndl) * sun.color + SampleSH(n) * 0.35;

                float3 h = SafeNormalize(sun.direction + viewDir);
                half glint = pow(saturate(dot(n, h)), 180.0) * _Glint;
                col.rgb += sun.color * glint;

                // Shoreline foam: animated bands where the water is shallow
                half shore = 1.0 - saturate(depth / 1.6);
                half bands = saturate(sin(depth * 5.0 - t * 2.2) * 0.5 + 0.5);
                half foam = shore * (0.45 + 0.55 * bands) * step(0.001, depth);
                col.rgb = lerp(col.rgb, _FoamColor.rgb, foam * 0.75);
                col.a = saturate(max(col.a, foam * 0.9) + fresnel * 0.2);

                col.rgb = MixFog(col.rgb, i.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
