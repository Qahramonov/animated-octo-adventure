// OROL sky dome: gradient sky driven by the day/night cycle globals, sun disc + glow, moon and
// procedural twinkling stars. Rendered on an inside-out sphere that follows the camera.
Shader "OROL/Sky"
{
    Properties
    {
        _SunSize ("Sun Size", Range(0.001, 0.2)) = 0.035
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _SunSize;
            CBUFFER_END

            float4 _OrolSkyZenith;
            float4 _OrolSkyHorizon;
            float4 _OrolSkyGround;
            float4 _OrolSunDir;     // xyz = direction towards the sun
            float4 _OrolSunColor;
            float4 _OrolMoonDir;
            float _OrolNight;        // 0 = day, 1 = full night
            float _OrolStorm;        // 0..1 overcast

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float Hash13(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 0.000001;
                #else
                    o.positionCS.z = o.positionCS.w * 0.999999;
                #endif
                o.dir = positionWS - _WorldSpaceCameraPos.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;

                half3 col;
                if (up >= 0.0)
                {
                    float t = pow(saturate(up), 0.45);
                    col = lerp(_OrolSkyHorizon.rgb, _OrolSkyZenith.rgb, t);
                }
                else
                {
                    col = lerp(_OrolSkyHorizon.rgb, _OrolSkyGround.rgb, saturate(-up * 4.0));
                }

                float3 sunDir = normalize(_OrolSunDir.xyz);
                float sd = dot(d, sunDir);
                // Mie-ish glow around the sun, stronger near the horizon
                half glow = pow(saturate(sd), 8.0) * 0.35 + pow(saturate(sd), 64.0) * 0.6;
                col += _OrolSunColor.rgb * glow * (1.0 - _OrolStorm * 0.8);
                // Sun disc
                half disc = smoothstep(1.0 - _SunSize * 0.02, 1.0 - _SunSize * 0.012, sd);
                col += _OrolSunColor.rgb * disc * 6.0 * (1.0 - _OrolStorm) * step(-0.05, sunDir.y);

                // Moon
                float3 moonDir = normalize(_OrolMoonDir.xyz);
                half moon = smoothstep(0.9993, 0.9996, dot(d, moonDir));
                col += half3(0.75, 0.8, 0.9) * moon * _OrolNight * (1.0 - _OrolStorm);

                // Stars
                if (_OrolNight > 0.01 && up > 0.0)
                {
                    float3 cell = floor(d * 220.0);
                    float h = Hash13(cell);
                    float star = step(0.9965, h);
                    float twinkle = 0.6 + 0.4 * sin(_Time.y * (1.0 + h * 4.0) + h * 40.0);
                    col += star * twinkle * _OrolNight * saturate(up * 3.0) * (1.0 - _OrolStorm);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
