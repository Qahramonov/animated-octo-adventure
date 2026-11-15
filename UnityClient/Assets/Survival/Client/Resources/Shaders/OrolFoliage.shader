// OROL foliage: alpha-tested, two-sided cards from a texture atlas (bark strip on the left, branch sprites and a
// baked impostor on the right). Everything right of the bark strip sways in the wind. Main light with shadows,
// soft wrap lighting for the translucent look of leaves, SH ambient, fog, shadow caster with the same cut-out.
Shader "OROL/Foliage"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.38
        _BarkU ("Bark strip width (u)", Range(0, 1)) = 0.125
        _Wind ("Wind sway", Range(0, 1)) = 0.5
        _Wrap ("Light wrap", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cutoff;
            half _BarkU;
            half _Wind;
            half _Wrap;
        CBUFFER_END

        float _OrolWindStrength;
        float4 _OrolWindDir;

        float3 Sway(float3 positionWS, float2 uv)
        {
            float weight = step(_BarkU, uv.x) * _Wind;
            float phase = _Time.y * 1.6 + positionWS.x * 0.23 + positionWS.z * 0.19;
            float sway = sin(phase) * 0.6 + sin(phase * 2.7 + positionWS.y) * 0.3;
            float2 dir = _OrolWindDir.xy;
            if (dot(dir, dir) < 0.0001) dir = float2(0.8, 0.6);
            positionWS.xz += dir * sway * weight * (0.06 + _OrolWindStrength * 0.12);
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 positionWS = Sway(TransformObjectToWorld(input.positionOS.xyz), input.uv);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                half3 albedo = tex.rgb * _Color.rgb;
                half3 n = normalize(i.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = lerp(0.35, 1.0, mainLight.shadowAttenuation) * mainLight.distanceAttenuation;
                half ndl = dot(n, mainLight.direction);
                half diffuse = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half3 color = albedo * (mainLight.color * diffuse * shadow + SampleSH(n));
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;

            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V ShadowVert(float4 positionOS : POSITION, float3 normalOS : NORMAL, float2 uv : TEXCOORD0)
            {
                V o;
                float3 positionWS = Sway(TransformObjectToWorld(positionOS.xyz), uv);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = uv;
                return o;
            }

            half4 ShadowFrag(V i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V DepthVert(float4 positionOS : POSITION, float2 uv : TEXCOORD0)
            {
                V o;
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(positionOS.xyz), uv));
                o.uv = uv;
                return o;
            }

            half4 DepthFrag(V i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
