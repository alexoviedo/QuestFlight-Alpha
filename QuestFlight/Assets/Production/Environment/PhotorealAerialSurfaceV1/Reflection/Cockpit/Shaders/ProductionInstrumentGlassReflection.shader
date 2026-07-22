Shader "QuestFlight/Photoreal Aerial Surface V1/Instrument Glass Reflection"
{
    Properties
    {
        _Tint ("Neutral Glass Tint", Color) = (0.90, 0.94, 0.98, 1)
        [NoScaleOffset] _AuthoredReflectionCube ("Offline Cockpit Interior Reflection", Cube) = "black" {}
        _Opacity ("Base Opacity", Range(0, 0.12)) = 0.036
        _FresnelOpacity ("Edge Fresnel Opacity", Range(0, 0.12)) = 0.072
        _ReflectionStrength ("Reflection Strength", Range(0, 0.85)) = 0.68
        _DirectGlintStrength ("Directional Glint Strength", Range(0, 0.16)) = 0.10
        _PerceptualRoughness ("Perceptual Roughness", Range(0.1, 1)) = 0.34
        [HideInInspector] _InstrumentToAircraftRow0 ("Instrument To Aircraft Row 0", Vector) = (1,0,0,0)
        [HideInInspector] _InstrumentToAircraftRow1 ("Instrument To Aircraft Row 1", Vector) = (0,1,0,0)
        [HideInInspector] _InstrumentToAircraftRow2 ("Instrument To Aircraft Row 2", Vector) = (0,0,1,0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent+16" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Tint;
            samplerCUBE _AuthoredReflectionCube;
            half _Opacity;
            half _FresnelOpacity;
            half _ReflectionStrength;
            half _DirectGlintStrength;
            half _PerceptualRoughness;
            float4 _InstrumentToAircraftRow0;
            float4 _InstrumentToAircraftRow1;
            float4 _InstrumentToAircraftRow2;

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

            fixed4 frag(v2f input, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half faceSign = facing >= 0 ? 1.0h : -1.0h;
                half3 normal = normalize(input.worldNormal) * faceSign;
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half ndotv = saturate(abs(dot(normal, viewDirection)));
                half fresnel = pow(1.0h - ndotv, 5.0h);
                half opacity = min(_Opacity + fresnel * _FresnelOpacity, 0.10h);
                half3 instrumentLocalReflectionDirection = normalize(mul(
                    (half3x3)unity_WorldToObject, reflect(-viewDirection, normal)));
                half3 localReflectionDirection = normalize(half3(
                    dot(_InstrumentToAircraftRow0.xyz, instrumentLocalReflectionDirection),
                    dot(_InstrumentToAircraftRow1.xyz, instrumentLocalReflectionDirection),
                    dot(_InstrumentToAircraftRow2.xyz, instrumentLocalReflectionDirection)));
                half3 reflection = texCUBElod(_AuthoredReflectionCube,
                    half4(localReflectionDirection, _PerceptualRoughness * 4.0h)).rgb;
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half glint = pow(saturate(dot(
                    reflect(-lightDirection, normal), viewDirection)), 72.0h);
                // Real glass keeps roughly four-percent normal-incidence reflectance. Do not
                // multiply that optical signal by the very low coverage alpha: doing so erased
                // the instrument reflections in worn review. Alpha still controls occlusion;
                // the bounded RGB term represents reflected radiance in premultiplied blending.
                half opticalFresnel = 0.105h + 0.895h * fresnel;
                half headlinerCue = smoothstep(0.18h, 0.82h, localReflectionDirection.y);
                reflection += half3(0.20h, 0.19h, 0.175h) * headlinerCue * 0.055h;
                half3 premultiplied = _Tint.rgb * opacity * 0.018h +
                                      reflection * _ReflectionStrength * opticalFresnel;
                premultiplied += _LightColor0.rgb * glint * _DirectGlintStrength *
                                 (0.25h + opticalFresnel * 0.75h);
                return fixed4(premultiplied, opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
