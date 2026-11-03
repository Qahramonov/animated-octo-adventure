// OROL mobile lit shader: vertex colours as albedo, main light with shadows, SH ambient, optional
// Blinn specular / rim / emission, vegetation wind sway (vertex alpha = sway weight), procedural
// detail noise for terrain, optional inner clip radius (far-terrain ring) and GPU instancing.
// Kept deliberately cheap for mid-range Android GPUs (no textures, one light).
Shader "OROL/VertexLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Gloss ("Specular", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(1, 128)) = 24
        _Rim ("Rim", Range(0, 2)) = 0
        _RimColor ("Rim Colour", Color) = (1,1,1,1)
        _Emission ("Emission", Range(0, 8)) = 0
        _Wind ("Wind Sway", Range(0, 1)) = 0
        _DetailStrength ("Detail Noise", Range(0, 1)) = 0
        _DetailScale ("Detail Scale", Float) = 0.35
        _ClipInnerRadius ("Clip Inner Radius", Float) = 0
        _Wrap ("Light Wrap", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Gloss;
            half _Smoothness;
            half _Rim;
            half4 _RimColor;
            half _Emission;
            half _Wind;
            half _DetailStrength;
            float _DetailScale;
            float _ClipInnerRadius;
            half _Wrap;
        CBUFFER_END

        // Globals driven by the weather/day-night system
        float _OrolWindStrength;
        float4 _OrolWindDir;

        float3 ApplyWind(float3 positionWS, float weight)
        {
            if (_Wind <= 0.0 || weight <= 0.0) return positionWS;
            float strength = _Wind * (0.35 + _OrolWindStrength) * weight;
            float phase = _Time.y * 1.7 + positionWS.x * 0.21 + positionWS.z * 0.17;
            float sway = sin(phase) * 0.6 + sin(phase * 2.3 + 1.7) * 0.25;
            float2 dir = _OrolWindDir.xy;
            if (dot(dir, dir) < 0.0001) dir = float2(0.8, 0.6);
            positionWS.xz += dir * sway * strength * 0.35;
            positionWS.y -= abs(sway) * strength * 0.05;
            return positionWS;
        }

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            float a = Hash12(i);
            float b = Hash12(i + float2(1, 0));
            float c = Hash12(i + float2(0, 1));
            float d = Hash12(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // GLES: per-instance light probe/LOD arrays exceed GL_MAX_FRAGMENT_UNIFORM_VECTORS (seen on the emulator) -> shader fails to link
            #pragma instancing_options nolightprobe nolodfade

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = ApplyWind(positionWS, input.color.a);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // no per-instance data is read in the fragment stage (keeps GLES uniform count low)
                if (_ClipInnerRadius > 0.0)
                {
                    float2 d = i.positionWS.xz - _WorldSpaceCameraPos.xz;
                    clip(dot(d, d) - _ClipInnerRadius * _ClipInnerRadius);
                }

                half3 albedo = i.color.rgb * _Color.rgb;
                if (_DetailStrength > 0.0)
                {
                    float n = ValueNoise(i.positionWS.xz * _DetailScale) * 0.6 + ValueNoise(i.positionWS.xz * _DetailScale * 4.1) * 0.4;
                    albedo *= 1.0 + (n - 0.5) * _DetailStrength;
                }

                half3 normalWS = normalize(i.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                half ndl = dot(normalWS, mainLight.direction);
                half diffuse = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half3 lit = albedo * mainLight.color * diffuse * shadow;
                half3 ambient = albedo * SampleSH(normalWS);

                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                half3 spec = 0;
                if (_Gloss > 0.0)
                {
                    half3 h = SafeNormalize(mainLight.direction + viewDir);
                    spec = mainLight.color * pow(saturate(dot(normalWS, h)), _Smoothness) * _Gloss * shadow;
                }
                half3 rim = 0;
                if (_Rim > 0.0)
                {
                    half f = 1.0 - saturate(dot(normalWS, viewDir));
                    rim = _RimColor.rgb * (f * f * f) * _Rim;
                }

                half3 color = lit + ambient + spec + rim + albedo * _Emission;
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = ApplyWind(positionWS, input.color.a);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz), input.color.a);
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target
            {
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
