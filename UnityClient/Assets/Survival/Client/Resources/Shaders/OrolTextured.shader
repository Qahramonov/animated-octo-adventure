// OROL textured lit shader for imported models (CC0 scans): albedo, normal map and an ARM map
// (R = ambient occlusion, G = roughness, B = metallic), main light with shadows, SH ambient, fog.
// With _Triplanar = 1 the albedo is projected in object space instead of using UVs (decimated rock
// scans have no usable UVs); _VeinColor/_VeinAmount paint mineral veins so ore nodes read at a glance.
Shader "OROL/Textured"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _ArmMap ("AO / Roughness / Metallic", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Triplanar ("Triplanar (0/1)", Float) = 0
        _TriScale ("Triplanar scale", Float) = 0.6
        _VeinColor ("Vein colour", Color) = (1,1,1,1)
        _VeinAmount ("Vein amount", Range(0, 1)) = 0
        _Emission ("Emission", Range(0, 4)) = 0
        _Wrap ("Light Wrap", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Triplanar;
            float _TriScale;
            half4 _VeinColor;
            half _VeinAmount;
            half _Emission;
            half _Wrap;
        CBUFFER_END
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
            #pragma instancing_options assumeuniformscaling nolightprobe nolodfade maxcount:40

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);
            TEXTURE2D(_ArmMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                half3 normalOS : TEXCOORD5;
                half fogFactor : TEXCOORD6;
            };

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), u.x), lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), u.x), u.y);
                float b = lerp(lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), u.x), lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), u.x), u.y);
                return lerp(a, b, u.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 normalWS = normalize(i.normalWS);
                half3 albedo;
                half ao = 1.0, rough = 0.85, metal = 0.0;
                if (_Triplanar > 0.5)
                {
                    float3 q = i.positionOS * _TriScale;
                    half3 w = abs(normalize(i.normalOS));
                    w = w * w;
                    w /= (w.x + w.y + w.z);
                    albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, q.zy).rgb * w.x
                           + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, q.xz).rgb * w.y
                           + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, q.xy).rgb * w.z;
                    // Mineral veins: thin noisy bands in object space
                    float v = Noise3(i.positionOS * 3.1) * 0.6 + Noise3(i.positionOS * 9.0) * 0.4;
                    half vein = smoothstep(0.5 - _VeinAmount * 0.32, 0.5, v) * step(0.001, _VeinAmount);
                    albedo = lerp(albedo, _VeinColor.rgb, vein * 0.88);
                    metal = vein * 0.6;
                    rough = lerp(0.9, 0.45, vein);
                    // Cheap crevice darkening from the surface texture itself
                    ao = saturate(0.55 + dot(albedo, half3(0.6, 0.6, 0.6)));
                }
                else
                {
                    albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                    half3 arm = SAMPLE_TEXTURE2D(_ArmMap, sampler_MainTex, i.uv).rgb;
                    ao = arm.r;
                    rough = arm.g;
                    metal = arm.b;
                    half3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_MainTex, i.uv));
                    half3 tangent = normalize(i.tangentWS.xyz);
                    half3 bitangent = cross(normalWS, tangent) * i.tangentWS.w;
                    normalWS = normalize(tangent * nTS.x + bitangent * nTS.y + normalWS * nTS.z);
                }
                albedo *= _Color.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half ndl = dot(normalWS, mainLight.direction);
                half diffuse = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half3 diffuseColor = albedo * (1.0 - metal * 0.85);
                half3 color = diffuseColor * (mainLight.color * diffuse * shadow + SampleSH(normalWS) * ao);

                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                half3 h = SafeNormalize(mainLight.direction + viewDir);
                half shininess = lerp(96.0, 6.0, rough);
                half3 specColor = lerp(half3(0.04, 0.04, 0.04), albedo, metal);
                half spec = pow(saturate(dot(normalWS, h)), shininess) * (shininess + 8.0) * 0.04;
                color += specColor * spec * mainLight.color * saturate(ndl) * shadow;
                // Metals need something to reflect: a little sky-coloured ambient specular
                color += specColor * SampleSH(reflect(-viewDir, normalWS)) * metal * (1.0 - rough) * ao;
                color += albedo * _Emission;

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
            #pragma instancing_options assumeuniformscaling nolightprobe nolodfade maxcount:40
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 ShadowVert(A input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
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
            #pragma instancing_options assumeuniformscaling nolightprobe nolodfade maxcount:40

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 DepthVert(A input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
