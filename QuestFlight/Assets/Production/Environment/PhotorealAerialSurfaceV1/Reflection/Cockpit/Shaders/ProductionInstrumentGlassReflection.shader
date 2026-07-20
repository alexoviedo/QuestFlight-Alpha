Shader "QuestFlight/Photoreal Aerial Surface V1/Instrument Glass Reflection"
{
    Properties
    {
        _Tint ("Neutral Glass Tint", Color) = (0.90, 0.94, 0.98, 1)
        _Opacity ("Base Opacity", Range(0, 0.12)) = 0.018
        _FresnelOpacity ("Edge Fresnel Opacity", Range(0, 0.12)) = 0.035
        _ReflectionStrength ("Reflection Strength", Range(0, 0.25)) = 0.10
        _PerceptualRoughness ("Perceptual Roughness", Range(0.1, 1)) = 0.42
    }

    SubShader
    {
        Tags { "Queue"="Transparent+16" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            #include "UnityGlobalIllumination.cginc"

            fixed4 _Tint;
            half _Opacity;
            half _FresnelOpacity;
            half _ReflectionStrength;
            half _PerceptualRoughness;

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
                half opacity = min(_Opacity + fresnel * _FresnelOpacity, 0.10h);
                half3 reflection = SampleAuthoredReflection(
                    input.worldPosition, viewDirection, reflect(-viewDirection, normal));
                half3 premultiplied = _Tint.rgb * opacity * 0.035h +
                                      reflection * _ReflectionStrength *
                                      (0.25h + fresnel * 0.75h) * opacity;
                return fixed4(premultiplied, opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
