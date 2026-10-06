Shader "QuestFlight/Photoreal Aerial Surface V1/Cockpit Window Reflection"
{
    Properties
    {
        _WindshieldTint ("Windshield Tint", Color) = (0.77, 0.86, 0.95, 1)
        _SideWindowTint ("Side Window Tint", Color) = (0.70, 0.82, 0.92, 1)
        [NoScaleOffset] _AuthoredReflectionCube ("Authored Ground/Sky Reflection", Cube) = "black" {}
        [NoScaleOffset] _CockpitInteriorReflectionCube ("Offline Cockpit Interior Reflection", Cube) = "black" {}
        _WindshieldOpacity ("Windshield Opacity", Range(0, 0.3)) = 0.075
        _SideWindowOpacity ("Side Window Opacity", Range(0, 0.3)) = 0.09
        _FresnelOpacity ("Edge Fresnel Opacity", Range(0, 0.3)) = 0.11
        _ReflectionStrength ("Reflection Strength", Range(0, 0.7)) = 0.46
        _InteriorReflectionStrength ("Interior Reflection Weight", Range(0, 1)) = 0.90
        _InteriorSkyLeak ("Interior Sky Leak", Range(0, 0.5)) = 0.08
        _DirectGlintStrength ("Directional Glint Strength", Range(0, 0.12)) = 0.055
        _PerceptualRoughness ("Perceptual Roughness", Range(0.05, 1)) = 0.24
        _SideAxisStart ("Side Axis Start", Range(0, 1)) = 0.52
        _SideAxisEnd ("Side Axis End", Range(0, 1)) = 0.82
        [HideInInspector] _WindowToAircraftRow0 ("Window To Aircraft Row 0", Vector) = (1,0,0,0)
        [HideInInspector] _WindowToAircraftRow1 ("Window To Aircraft Row 1", Vector) = (0,1,0,0)
        [HideInInspector] _WindowToAircraftRow2 ("Window To Aircraft Row 2", Vector) = (0,0,1,0)
        [HideInInspector] _CockpitReflectionCapturePosition ("Capture Position", Vector) = (0,0,0,0)
        [HideInInspector] _CockpitReflectionBoxMin ("Cabin Box Min", Vector) = (-1,-1,-1,0)
        [HideInInspector] _CockpitReflectionBoxMax ("Cabin Box Max", Vector) = (1,1,1,0)
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
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _WindshieldTint;
            fixed4 _SideWindowTint;
            samplerCUBE _AuthoredReflectionCube;
            samplerCUBE _CockpitInteriorReflectionCube;
            half _WindshieldOpacity;
            half _SideWindowOpacity;
            half _FresnelOpacity;
            half _ReflectionStrength;
            half _InteriorReflectionStrength;
            half _InteriorSkyLeak;
            half _DirectGlintStrength;
            half _PerceptualRoughness;
            half _SideAxisStart;
            half _SideAxisEnd;
            float4 _WindowToAircraftRow0;
            float4 _WindowToAircraftRow1;
            float4 _WindowToAircraftRow2;
            float4 _CockpitReflectionCapturePosition;
            float4 _CockpitReflectionBoxMin;
            float4 _CockpitReflectionBoxMax;

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
                float3 objectPosition : TEXCOORD3;
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
                output.objectPosition = input.vertex.xyz;
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
                half3 localNormal = normalize(input.objectNormal);
                half sideWeight = smoothstep(_SideAxisStart, _SideAxisEnd, abs(localNormal.x));
                half opacity = lerp(_WindshieldOpacity, _SideWindowOpacity, sideWeight) +
                               fresnel * _FresnelOpacity;
                opacity = min(opacity, 0.16h);

                half3 tint = lerp(_WindshieldTint.rgb, _SideWindowTint.rgb, sideWeight);
                half3 reflectionDirection = reflect(-viewDirection, normal);
                half3 authoredReflection = texCUBE(
                    _AuthoredReflectionCube, reflectionDirection).rgb;
                half3 windowLocalReflectionDirection = normalize(mul(
                    (half3x3)unity_WorldToObject, reflectionDirection));
                half3 aircraftReflectionDirection = normalize(half3(
                    dot(_WindowToAircraftRow0.xyz, windowLocalReflectionDirection),
                    dot(_WindowToAircraftRow1.xyz, windowLocalReflectionDirection),
                    dot(_WindowToAircraftRow2.xyz, windowLocalReflectionDirection)));
                float4 homogeneousPosition = float4(input.objectPosition, 1.0);
                float3 aircraftPosition = float3(
                    dot(_WindowToAircraftRow0, homogeneousPosition),
                    dot(_WindowToAircraftRow1, homogeneousPosition),
                    dot(_WindowToAircraftRow2, homogeneousPosition));
                // Finite box projection anchors the geometry-baked panel, doors, seats and
                // headliner to the actual cabin volume. Unlike the previous painted gradient,
                // reflected content now changes plausibly across the windshield and side glass.
                float3 safeDirection = aircraftReflectionDirection +
                    (1.0 - step(0.0001, abs(aircraftReflectionDirection))) * 0.0001;
                float3 rbMin = (_CockpitReflectionBoxMin.xyz - aircraftPosition) / safeDirection;
                float3 rbMax = (_CockpitReflectionBoxMax.xyz - aircraftPosition) / safeDirection;
                float3 rbExit = lerp(rbMin, rbMax, step(0.0, safeDirection));
                float intersectionDistance = min(min(rbExit.x, rbExit.y), rbExit.z);
                half3 cabinSampleDirection = normalize(
                    aircraftPosition - _CockpitReflectionCapturePosition.xyz +
                    aircraftReflectionDirection * intersectionDistance);
                half3 cockpitReflection = texCUBElod(
                    _CockpitInteriorReflectionCube,
                    half4(cabinSampleDirection, _PerceptualRoughness * 4.0h)).rgb;
                half skyWeight = smoothstep(-0.08h, 0.20h, reflectionDirection.y);
                half3 proceduralGround = half3(0.13h, 0.15h, 0.10h);
                half3 proceduralSky = half3(0.34h, 0.48h, 0.62h);
                half3 proceduralReflection = lerp(proceduralGround, proceduralSky, skyWeight);
                half3 exteriorReflection = lerp(authoredReflection, proceduralReflection, 0.16h);
                float3 cameraWindowPosition = mul(
                    unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float4 cameraWindowHomogeneous = float4(cameraWindowPosition, 1.0);
                float3 cameraAircraftPosition = float3(
                    dot(_WindowToAircraftRow0, cameraWindowHomogeneous),
                    dot(_WindowToAircraftRow1, cameraWindowHomogeneous),
                    dot(_WindowToAircraftRow2, cameraWindowHomogeneous));
                half3 insideMinimum = step(
                    _CockpitReflectionBoxMin.xyz - 0.08, cameraAircraftPosition);
                half3 insideMaximum = step(
                    cameraAircraftPosition, _CockpitReflectionBoxMax.xyz + 0.08);
                half cameraInsideCabin = insideMinimum.x * insideMinimum.y * insideMinimum.z *
                    insideMaximum.x * insideMaximum.y * insideMaximum.z;
                half interiorMix = saturate(
                    _InteriorReflectionStrength * cameraInsideCabin);
                half3 interiorReflection = lerp(
                    cockpitReflection,
                    exteriorReflection,
                    saturate(_InteriorSkyLeak + fresnel * 0.10h));
                half3 reflection = lerp(exteriorReflection, interiorReflection, interiorMix);
                // Premultiplied transmission and reflected radiance are separate optical
                // terms. The previous path mixed reflection into a tint and then multiplied
                // the result by the low transmission alpha, suppressing cabin content twice.
                // Keep the accepted clear-glass alpha while preserving a visible, bounded
                // four-to-eighteen percent cabin reflection across normal/grazing views.
                half opticalReflection = saturate(
                    _ReflectionStrength * (0.12h + fresnel * 0.28h));
                half3 liftedReflection = lerp(
                    reflection, sqrt(saturate(reflection)), 0.32h);
                half3 premultiplied = tint * opacity * 0.025h +
                                      liftedReflection * opticalReflection;
                half3 sunDirection = normalize(_WorldSpaceLightPos0.xyz);
                half sunGlint = pow(saturate(dot(
                    reflect(-sunDirection, normal), viewDirection)), 96.0h);
                premultiplied += _LightColor0.rgb * sunGlint * _DirectGlintStrength;
                return fixed4(premultiplied, opacity);
            }
            
#pragma multi_compile_instancing
ENDCG
        }
    }
    Fallback Off
}
