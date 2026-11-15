// OROL terrain: photo textures (CC0, Poly Haven) blended by slope and height. Grass / forest floor / sand are
// projected from above, rock on steep faces from the sides. The mesh's vertex colour keeps tinting the result so
// biomes, snow caps and beaches still read from far away. One detail normal map, main light with shadows, fog.
Shader "OROL/Terrain"
{
    Properties
    {
        _Grass ("Grass", 2D) = "white" {}
        _Forest ("Forest floor", 2D) = "white" {}
        _Rock ("Rock", 2D) = "white" {}
        _Sand ("Sand", 2D) = "white" {}
        _GrassN ("Grass normal", 2D) = "bump" {}
        _RockN ("Rock normal", 2D) = "bump" {}
        _Tiling ("Tiling (1/m)", Float) = 0.22
        _SeaLevel ("Sea level", Float) = 30
        _TintStrength ("Vertex tint", Range(0, 1)) = 0.6
        _NormalStrength ("Detail normal", Range(0, 2)) = 0.9
        _ClipInnerRadius ("Clip Inner Radius", Float) = 0
        _Wrap ("Light Wrap", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Grass);  SAMPLER(sampler_Grass);
            TEXTURE2D(_Forest);
            TEXTURE2D(_Rock);
            TEXTURE2D(_Sand);
            TEXTURE2D(_GrassN);
            TEXTURE2D(_RockN);

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _SeaLevel;
                half _TintStrength;
                half _NormalStrength;
                float _ClipInnerRadius;
                half _Wrap;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

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
                return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), u.x), lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                if (_ClipInnerRadius > 0.0)
                {
                    float2 d = p.xz - _WorldSpaceCameraPos.xz;
                    clip(dot(d, d) - _ClipInnerRadius * _ClipInnerRadius);
                }
                half3 n = normalize(i.normalWS);

                // Blend weights: rock on steep faces, sand near the sea, forest floor in large noisy patches
                half rockW = smoothstep(0.80, 0.60, n.y);
                half sandW = smoothstep(_SeaLevel + 3.2, _SeaLevel + 1.0, p.y) * (1.0 - rockW);
                half patches = ValueNoise(p.xz * 0.011) * 0.65 + ValueNoise(p.xz * 0.047) * 0.35;
                half forestW = smoothstep(0.52, 0.68, patches) * (1.0 - rockW) * (1.0 - sandW);
                half grassW = saturate(1.0 - rockW - sandW - forestW);

                float2 uvTop = p.xz * _Tiling;
                half3 grass = SAMPLE_TEXTURE2D(_Grass, sampler_Grass, uvTop).rgb;
                half3 forest = SAMPLE_TEXTURE2D(_Forest, sampler_Grass, uvTop * 0.83 + 0.37).rgb;
                half3 sand = SAMPLE_TEXTURE2D(_Sand, sampler_Grass, uvTop * 1.2).rgb;
                // Rock: side projections only (the top projection would stretch on cliffs)
                half sideX = abs(n.x) / (abs(n.x) + abs(n.z) + 0.0001);
                float2 uvSide = (sideX > 0.5 ? p.zy : p.xy) * (_Tiling * 0.6);
                half3 rock = SAMPLE_TEXTURE2D(_Rock, sampler_Grass, uvSide).rgb;

                half3 albedo = grass * grassW + forest * forestW + sand * sandW + rock * rockW;
                // Large-scale brightness variation hides texture repetition
                albedo *= 0.86 + 0.28 * ValueNoise(p.xz * 0.06);
                // Vertex colour (biome / snow / beach colour of the generator), normalised around mid grey
                albedo *= lerp(half3(1, 1, 1), saturate(i.color.rgb * 2.3), _TintStrength);

                // Detail normal: grass from above, rock from the side (approximate tangent frames)
                half3 gn = UnpackNormal(SAMPLE_TEXTURE2D(_GrassN, sampler_Grass, uvTop));
                half3 rn = UnpackNormal(SAMPLE_TEXTURE2D(_RockN, sampler_Grass, uvSide));
                half3 bump = half3(gn.x, 0, gn.y) * (1.0 - rockW) + (sideX > 0.5 ? half3(0, rn.y, rn.x) : half3(rn.x, rn.y, 0)) * rockW;
                n = normalize(n + bump * _NormalStrength);

                float4 shadowCoord = TransformWorldToShadowCoord(p);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _SeaLevel;
                half _TintStrength;
                half _NormalStrength;
                float _ClipInnerRadius;
                half _Wrap;
            CBUFFER_END
            float3 _LightDirection;

            float4 ShadowVert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _SeaLevel;
                half _TintStrength;
                half _NormalStrength;
                float _ClipInnerRadius;
                half _Wrap;
            CBUFFER_END

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
