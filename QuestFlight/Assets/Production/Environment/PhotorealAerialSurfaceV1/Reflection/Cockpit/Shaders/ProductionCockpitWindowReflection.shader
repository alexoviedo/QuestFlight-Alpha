Shader "QuestFlight/Photoreal Aerial Surface V1/Cockpit Window Reflection"
{
    Properties
    {
        _WindshieldTint ("Windshield Tint", Color) = (0.77, 0.86, 0.95, 1)
        _SideWindowTint ("Side Window Tint", Color) = (0.70, 0.82, 0.92, 1)
        _WindshieldOpacity ("Windshield Opacity", Range(0, 0.2)) = 0.045
        _SideWindowOpacity ("Side Window Opacity", Range(0, 0.2)) = 0.065
        _FresnelOpacity ("Edge Fresnel Opacity", Range(0, 0.2)) = 0.085
        _ReflectionStrength ("Reflection Strength", Range(0, 0.5)) = 0.24
        _DirectGlintStrength ("Directional Glint Strength", Range(0, 0.12)) = 0.055
        _PerceptualRoughness ("Perceptual Roughness", Range(0.05, 1)) = 0.24
        _SideAxisStart ("Side Axis Start", Range(0, 1)) = 0.52
        _SideAxisEnd ("Side Axis End", Range(0, 1)) = 0.82
    }

    SubShader
    {
        Tags { "Queue"="Transparent+12" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ UNITY_SPECCUBE_BLENDING
            #pragma multi_compile __ UNITY_SPECCUBE_BOX_PROJECTION
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "UnityGlobalIllumination.cginc"

            fixed4 _WindshieldTint;
            fixed4 _SideWindowTint;
            half _WindshieldOpacity;
            half _SideWindowOpacity;
            half _FresnelOpacity;
            half _ReflectionStrength;
            half _DirectGlintStrength;
            half _PerceptualRoughness;
            half _SideAxisStart;
            half _SideAxisEnd;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                half3 objectNormal : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.objectNormal = input.normal;
                return output;
            }

            half3 SampleAuthoredReflection(float3 worldPosition, half3 viewDirection, half3 reflectionDirection)
            {
                UnityGIInput data;
                UNITY_INITIALIZE_OUTPUT(UnityGIInput, data);
                data.worldPos = worldPosition;
                data.worldViewDir = viewDirection;
                data.probeHDR[0] = unity_SpecCube0_HDR;
                data.probeHDR[1] = unity_SpecCube1_HDR;
                #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
                    data.boxMin[0] = unity_SpecCube0_BoxMin;
                #endif
                #ifdef UNITY_SPECCUBE_BOX_PROJECTION
                    data.boxMax[0] = unity_SpecCube0_BoxMax;
                    data.probePosition[0] = unity_SpecCube0_ProbePosition;
                    data.boxMax[1] = unity_SpecCube1_BoxMax;
                    data.boxMin[1] = unity_SpecCube1_BoxMin;
                    data.probePosition[1] = unity_SpecCube1_ProbePosition;
                #endif
                Unity_GlossyEnvironmentData glossy;
                glossy.roughness = _PerceptualRoughness;
                glossy.reflUVW = reflectionDirection;
                return UnityGI_IndirectSpecular(data, 1.0h, glossy);
            }

            fixed4 frag(v2f input, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half faceSign = facing >= 0 ? 1.0h : -1.0h;
                half3 normal = normalize(input.worldNormal) * faceSign;
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half ndotv = saturate(abs(dot(normal, viewDirection)));
                half fresnel = pow(1.0h - ndotv, 5.0h);
                half3 localNormal = normalize(input.objectNormal);
                half sideWeight = smoothstep(_SideAxisStart, _SideAxisEnd, abs(localNormal.x));
                half opacity = lerp(_WindshieldOpacity, _SideWindowOpacity, sideWeight) +
                               fresnel * _FresnelOpacity;
                opacity = min(opacity, 0.20h);

                half3 tint = lerp(_WindshieldTint.rgb, _SideWindowTint.rgb, sideWeight);
                half3 reflectionDirection = reflect(-viewDirection, normal);
                half3 reflection = SampleAuthoredReflection(
                    input.worldPosition, viewDirection, reflectionDirection);
                half reflectionWeight = _ReflectionStrength * (0.32h + fresnel * 0.68h);
                half reflectionVisibility = saturate(opacity * 2.75h + fresnel * 0.05h);
                half3 premultiplied = tint * opacity * 0.08h +
                                      reflection * reflectionWeight * reflectionVisibility;
                half3 sunDirection = normalize(_WorldSpaceLightPos0.xyz);
                half sunGlint = pow(saturate(dot(
                    reflect(-sunDirection, normal), viewDirection)), 96.0h);
                premultiplied += _LightColor0.rgb * sunGlint * _DirectGlintStrength;
                return fixed4(premultiplied, opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
