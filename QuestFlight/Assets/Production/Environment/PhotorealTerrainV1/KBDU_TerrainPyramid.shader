Shader "QuestFlight/KBDU Terrain Pyramid"
{
    Properties
    {
        _NearAlbedo ("Near 4 km NAIP", 2D) = "gray" {}
        _NearSourceNormal ("Near 1 m DEM source normal (ENU)", 2D) = "gray" {}
        _MidAlbedo ("Mid 12 km NAIP", 2D) = "gray" {}
        _FarAlbedo ("Far 24 km NAIP", 2D) = "gray" {}
        _DetailAlbedo ("CC0 near/mid microdetail", 2D) = "white" {}
        _NearWorldMinInvSize ("Near min XZ / inverse size", Vector) = (-2000,-2000,0.00025,0.00025)
        _MidWorldMinInvSize ("Mid min XZ / inverse size", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _FarWorldMinInvSize ("Far min XZ / inverse size", Vector) = (-12000,-12000,0.0000416667,0.0000416667)
        _NearBlendStartEnd ("Near to mid overlap", Vector) = (1650,2000,0,0)
        _MidBlendStartEnd ("Mid to far overlap", Vector) = (5000,6000,0,0)
        _DetailScale ("Detail world scale", Float) = 0.0666667
        _DetailFadeStartEnd ("Detail camera fade", Vector) = (180,1500,0,0)
        _DetailRadiusFadeStartEnd ("Detail world-radius fade", Vector) = (4500,6800,0,0)
        _NearSourceNormalFadeStartEnd ("Near source-normal camera fade", Vector) = (250,2500,0,0)
        _NearSourceNormalStrength ("Near source-normal strength", Range(0,1)) = 0
        _AirportCoreCleanPlateAlbedo ("Airport core bounded clean plate", 2D) = "gray" {}
        _AirportCoreCleanPlateMask ("Airport core clean-plate replacement mask", 2D) = "black" {}
        _AirportCoreCleanPlateWorldMinInvSize ("Clean-plate min XZ / inverse size", Vector) = (0,0,1,1)
        _AirportCoreCleanPlateStrength ("Clean-plate integration strength", Range(0,1)) = 0
        _AirportCoreMinorWaterMask ("Airport core minor-water terrain mask", 2D) = "black" {}
        _AirportCoreMinorWaterNearWorldMinInvSize ("Minor-water near min XZ / inverse size", Vector) = (-2000,-2000,0.00025,0.00025)
        _AirportCoreMinorWaterMidWorldMinInvSize ("Minor-water mid min XZ / inverse size", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _AirportCoreMinorWaterStrength ("Minor-water terrain effect strength", Range(0,1)) = 0
        _AirportCoreMinorWaterReflectionColor ("Minor-water reflection color", Color) = (0.48,0.60,0.67,1)
        _Tint ("Shared terrain tint", Color) = (1,1,1,1)
        _Roughness ("Roughness", Range(0,1)) = 0.92
        [Enum(Off,0,MacroLevel,1,BlendWeight,2,TexelDensityAndBoundaries,3)]
        _DiagnosticMode ("Terrain diagnostic", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 120
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "../AirportCoreV4/Water/Shaders/KBDUMinorWaterTerrainEffect.hlsl"

            sampler2D _NearAlbedo;
            sampler2D _NearSourceNormal;
            sampler2D _MidAlbedo;
            sampler2D _FarAlbedo;
            sampler2D _DetailAlbedo;
            sampler2D _AirportCoreCleanPlateAlbedo;
            sampler2D _AirportCoreCleanPlateMask;
            sampler2D _AirportCoreMinorWaterMask;
            float4 _NearWorldMinInvSize;
            float4 _MidWorldMinInvSize;
            float4 _FarWorldMinInvSize;
            float4 _NearBlendStartEnd;
            float4 _MidBlendStartEnd;
            float4 _DetailFadeStartEnd;
            float4 _DetailRadiusFadeStartEnd;
            float4 _NearSourceNormalFadeStartEnd;
            float4 _AirportCoreCleanPlateWorldMinInvSize;
            float4 _AirportCoreMinorWaterNearWorldMinInvSize;
            float4 _AirportCoreMinorWaterMidWorldMinInvSize;
            fixed4 _AirportCoreMinorWaterReflectionColor;
            fixed4 _Tint;
            half _DetailScale;
            half _NearSourceNormalStrength;
            half _AirportCoreCleanPlateStrength;
            half _AirportCoreMinorWaterStrength;
            half _Roughness;
            half _DiagnosticMode;

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
                UNITY_FOG_COORDS(2)
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
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            half BoundaryLine(half radius, half boundary)
            {
                half width = max(4.0, fwidth(radius) * 1.5);
                return 1.0 - smoothstep(width, width * 2.0, abs(radius - boundary));
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 world = input.worldPosition.xz;
                float2 nearUv = saturate((world - _NearWorldMinInvSize.xy) * _NearWorldMinInvSize.zw);
                float2 midUv = saturate((world - _MidWorldMinInvSize.xy) * _MidWorldMinInvSize.zw);
                float2 farUv = saturate((world - _FarWorldMinInvSize.xy) * _FarWorldMinInvSize.zw);
                float2 nearUvDx = ddx(nearUv);
                float2 nearUvDy = ddy(nearUv);
                float2 midUvDx = ddx(midUv);
                float2 midUvDy = ddy(midUv);
                float2 farUvDx = ddx(farUv);
                float2 farUvDy = ddy(farUv);
                half radius = max(abs(world.x), abs(world.y));
                half nearWeight = 1.0 - smoothstep(_NearBlendStartEnd.x, _NearBlendStartEnd.y, radius);
                half midWeight = 1.0 - smoothstep(_MidBlendStartEnd.x, _MidBlendStartEnd.y, radius);
                fixed3 macro;
                UNITY_BRANCH if (radius <= _NearBlendStartEnd.x)
                {
                    macro = tex2Dgrad(_NearAlbedo, nearUv, nearUvDx, nearUvDy).rgb;
                }
                else if (radius < _NearBlendStartEnd.y)
                {
                    fixed3 nearColor = tex2Dgrad(_NearAlbedo, nearUv, nearUvDx, nearUvDy).rgb;
                    fixed3 midColor = tex2Dgrad(_MidAlbedo, midUv, midUvDx, midUvDy).rgb;
                    macro = lerp(midColor, nearColor, nearWeight);
                }
                else if (radius <= _MidBlendStartEnd.x)
                {
                    macro = tex2Dgrad(_MidAlbedo, midUv, midUvDx, midUvDy).rgb;
                }
                else if (radius < _MidBlendStartEnd.y)
                {
                    fixed3 midColor = tex2Dgrad(_MidAlbedo, midUv, midUvDx, midUvDy).rgb;
                    fixed3 farColor = tex2Dgrad(_FarAlbedo, farUv, farUvDx, farUvDy).rgb;
                    macro = lerp(farColor, midColor, midWeight);
                }
                else
                {
                    macro = tex2Dgrad(_FarAlbedo, farUv, farUvDx, farUvDy).rgb;
                }

                float2 cleanPlateUv =
                    (world - _AirportCoreCleanPlateWorldMinInvSize.xy) *
                    _AirportCoreCleanPlateWorldMinInvSize.zw;
                half cleanPlateInside = step(0.0, cleanPlateUv.x) * step(cleanPlateUv.x, 1.0) *
                    step(0.0, cleanPlateUv.y) * step(cleanPlateUv.y, 1.0);
                UNITY_BRANCH if (_AirportCoreCleanPlateStrength > 0.001h && cleanPlateInside > 0.5h)
                {
                    float2 cleanPlateUvDx = ddx(cleanPlateUv);
                    float2 cleanPlateUvDy = ddy(cleanPlateUv);
                    half cleanPlateWeight = tex2Dgrad(
                        _AirportCoreCleanPlateMask,
                        cleanPlateUv,
                        cleanPlateUvDx,
                        cleanPlateUvDy).r * _AirportCoreCleanPlateStrength;
                    fixed3 cleanPlate = tex2Dgrad(
                        _AirportCoreCleanPlateAlbedo,
                        cleanPlateUv,
                        cleanPlateUvDx,
                        cleanPlateUvDy).rgb;
                    macro = lerp(macro, cleanPlate, saturate(cleanPlateWeight));
                }

                half cameraDistance = distance(_WorldSpaceCameraPos, input.worldPosition);
                half detailWeight = 1.0 - smoothstep(
                    _DetailFadeStartEnd.x, _DetailFadeStartEnd.y, cameraDistance);
                detailWeight *= 1.0 - smoothstep(
                    _DetailRadiusFadeStartEnd.x,
                    _DetailRadiusFadeStartEnd.y,
                    radius);
                fixed3 detailMultiplier = fixed3(1,1,1);
                float2 detailUv = world * _DetailScale;
                float2 detailUvDx = ddx(detailUv);
                float2 detailUvDy = ddy(detailUv);
                UNITY_BRANCH if (detailWeight > 0.0)
                {
                    fixed3 detailSample = tex2Dgrad(
                        _DetailAlbedo, detailUv, detailUvDx, detailUvDy).rgb;
                    half detailLuminance = max(
                        0.12, dot(detailSample, fixed3(0.2126, 0.7152, 0.0722)));
                    fixed3 detailModulation = lerp(
                        fixed3(1,1,1), detailSample / detailLuminance, 0.15);
                    half detailLumaModulation = lerp(
                        0.92,
                        1.08,
                        saturate((detailLuminance - 0.32) * 1.85));
                    detailMultiplier = lerp(
                        fixed3(1,1,1),
                        detailModulation * detailLumaModulation,
                        detailWeight);
                }
                fixed3 albedo = macro * _Tint.rgb * detailMultiplier;
                half surfaceRoughness = _Roughness;

                if (_DiagnosticMode > 0.5)
                {
                    fixed3 levelColor = lerp(fixed3(0.12,0.32,0.78), fixed3(0.94,0.58,0.08), midWeight);
                    levelColor = lerp(levelColor, fixed3(0.08,0.72,0.30), nearWeight);
                    if (_DiagnosticMode < 1.5)
                        return fixed4(levelColor, 1.0);
                    half overlap = max(4.0 * nearWeight * (1.0 - nearWeight), 4.0 * midWeight * (1.0 - midWeight));
                    if (_DiagnosticMode < 2.5)
                        return fixed4(lerp(fixed3(0.03,0.03,0.03), fixed3(1.0,0.12,0.02), overlap), 1.0);
                    half boundary = max(
                        max(BoundaryLine(radius, _NearBlendStartEnd.x), BoundaryLine(radius, _NearBlendStartEnd.y)),
                        max(BoundaryLine(radius, _MidBlendStartEnd.x), BoundaryLine(radius, _MidBlendStartEnd.y)));
                    return fixed4(lerp(levelColor * 0.62, fixed3(1,1,1), boundary), 1.0);
                }

                half3 normal = normalize(input.worldNormal);
                half nearSourceNormalWeight = nearWeight * _NearSourceNormalStrength *
                    (1.0 - smoothstep(
                        _NearSourceNormalFadeStartEnd.x,
                        max(_NearSourceNormalFadeStartEnd.x + 1.0, _NearSourceNormalFadeStartEnd.y),
                        cameraDistance));
                UNITY_BRANCH if (nearSourceNormalWeight > 0.001)
                {
                    half3 nearSourceNormal =
                        tex2Dgrad(_NearSourceNormal, nearUv, nearUvDx, nearUvDy).xyz * 2.0 - 1.0;
                    nearSourceNormal.y = max(nearSourceNormal.y, 0.001);
                    nearSourceNormal *= rsqrt(max(dot(nearSourceNormal, nearSourceNormal), 0.0001));
                    normal = normalize(lerp(normal, nearSourceNormal, nearSourceNormalWeight));
                }
                float2 minorWaterNearUv =
                    (world - _AirportCoreMinorWaterNearWorldMinInvSize.xy) *
                    _AirportCoreMinorWaterNearWorldMinInvSize.zw;
                float2 minorWaterMidUv =
                    (world - _AirportCoreMinorWaterMidWorldMinInvSize.xy) *
                    _AirportCoreMinorWaterMidWorldMinInvSize.zw;
                half minorNearInside = step(0.0, minorWaterNearUv.x) * step(minorWaterNearUv.x, 1.0) *
                    step(0.0, minorWaterNearUv.y) * step(minorWaterNearUv.y, 1.0);
                half minorMidInside = step(0.0, minorWaterMidUv.x) * step(minorWaterMidUv.x, 1.0) *
                    step(0.0, minorWaterMidUv.y) * step(minorWaterMidUv.y, 1.0);
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                UNITY_BRANCH if (_AirportCoreMinorWaterStrength > 0.001h &&
                    (minorNearInside > 0.5h || minorMidInside > 0.5h))
                {
                    half2 nearPacked = tex2Dgrad(
                        _AirportCoreMinorWaterMask,
                        saturate(minorWaterNearUv),
                        ddx(minorWaterNearUv),
                        ddy(minorWaterNearUv)).rg;
                    half2 midPacked = tex2Dgrad(
                        _AirportCoreMinorWaterMask,
                        saturate(minorWaterMidUv),
                        ddx(minorWaterMidUv),
                        ddy(minorWaterMidUv)).ba;
                    half nearToMid = smoothstep(1750.0h, 2100.0h, radius);
                    half2 minorPacked = lerp(nearPacked * minorNearInside,
                        midPacked * minorMidInside, nearToMid) * _AirportCoreMinorWaterStrength;
                    half2 rippleSlope = half2(
                        sin(world.x * 0.081h + world.y * 0.047h),
                        cos(world.y * 0.073h - world.x * 0.039h)) * 0.045h;
                    half3 rippleNormal = normalize(half3(rippleSlope.x, 1.0h, rippleSlope.y));
                    half terrainMetallic = 0.0h;
                    ApplyKBDUMinorWaterTerrainEffect(
                        minorPacked.r,
                        minorPacked.g,
                        _AirportCoreMinorWaterReflectionColor.rgb,
                        rippleNormal,
                        viewDirection,
                        albedo,
                        normal,
                        surfaceRoughness,
                        terrainMetallic);
                }
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half diffuse = saturate(dot(normal, lightDirection));
                half3 irradiance = saturate(max(ShadeSH9(half4(normal, 1.0)), 0.0h) * 0.35h +
                    _LightColor0.rgb * (0.08h + diffuse * 0.55h));
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half specular = pow(saturate(dot(normal, halfDirection)), lerp(28.0, 5.0, surfaceRoughness)) *
                    (1.0 - surfaceRoughness) * 0.045;
                fixed4 output = fixed4(
                    albedo * irradiance + _LightColor0.rgb * specular,
                    1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
