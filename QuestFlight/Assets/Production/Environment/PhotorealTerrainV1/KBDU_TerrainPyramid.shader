Shader "QuestFlight/KBDU Terrain Pyramid"
{
    Properties
    {
        _NearAlbedo ("Near 4 km NAIP", 2D) = "gray" {}
        _NearSourceNormal ("Near 1 m DEM source normal (ENU)", 2D) = "gray" {}
        _MidAlbedo ("Mid 12 km NAIP fallback", 2D) = "gray" {}
        _FarAlbedo ("Far 24 km NAIP", 2D) = "gray" {}
        _DetailAlbedo ("CC0 near/mid microdetail", 2D) = "white" {}
        _NearWorldMinInvSize ("Near min XZ / inverse size", Vector) = (-2000,-2000,0.00025,0.00025)
        _MidWorldMinInvSize ("Mid min XZ / inverse size", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _FarWorldMinInvSize ("Far min XZ / inverse size", Vector) = (-12000,-12000,0.0000416667,0.0000416667)
        _NearBlendStartEnd ("Near to recovery overlap", Vector) = (1800,1980,0,0)
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
        _AerialCanopySurfaceFeatures ("Aerial canopy height/density/building", 2D) = "black" {}
        _AerialTerrainNormalAndValidity ("Aerial terrain normal/validity", 2D) = "black" {}
        _AerialCanopyNormalOcclusion ("Aerial canopy normal/occlusion", 2D) = "gray" {}
        _AerialCanopyHandoffMask ("Aerial canopy geometry/map ownership", 2D) = "black" {}
        _AerialCanopyLocalBounds ("Aerial canopy min XZ / inverse size", Vector) = (-2048,-2048,0.000244140625,0.000244140625)
        _AerialCanopyProductToSourceRow0 ("Product AEQD to lidar UTM row 0", Vector) = (1,0,0,0)
        _AerialCanopyProductToSourceRow1 ("Product AEQD to lidar UTM row 1", Vector) = (0,1,0,0)
        _AerialCanopySourceToProductRow0 ("Lidar UTM normal to product row 0", Vector) = (1,0,0,0)
        _AerialCanopySourceToProductRow1 ("Lidar UTM normal to product row 1", Vector) = (0,1,0,0)
        _AerialCanopyHeightQuantumMeters ("Aerial canopy height quantum", Float) = 0.25
        _AerialCanopyColorStrength ("Aerial canopy color strength", Range(0,1)) = 0
        _AerialCanopyNormalStrength ("Aerial canopy normal strength", Range(0,1)) = 0
        _AerialCanopyTint ("Aerial canopy tint", Color) = (0.25,0.34,0.16,1)
        _FullMapWaterMask ("Full-map water coverage", 2D) = "black" {}
        _FullMapWaterWorldMinInvSize ("Water min XZ / inverse size", Vector) = (-12000,-12000,0.0000416667,0.0000416667)
        _FullMapWaterReflectionStrength ("Water neutral reflection strength", Range(0,1)) = 0.095
        _FullMapWaterFresnelStrength ("Water grazing reflection strength", Range(0,1)) = 0.14
        _FullMapWaterSunGlintStrength ("Water sun glint strength", Range(0,1)) = 0.24
        _FullMapWaterCrestGlintStrength ("Water guaranteed crest glint", Range(0,1)) = 0.12
        _FullMapWaterDistortionStrength ("Water reflection distortion", Range(0,0.1)) = 0.022
        _FullMapWaterRoughness ("Water reflection roughness", Range(0,1)) = 0.52
        _Tint ("Shared terrain tint", Color) = (1,1,1,1)
        _Roughness ("Roughness", Range(0,1)) = 0.92
        [HideInInspector] _ClipmapEnabled ("Aircraft-centered aerial clipmap enabled", Float) = 0
        [Enum(Off,0,MacroLevel,1,BlendWeight,2,TexelDensityAndBoundaries,3,FullMapWaterMask,4)]
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "../../../Shaders/AerialClipmapSampling.cginc"
            #include "../AerialSceneryV1/Vegetation/Shaders/AerialCanopyMap.cginc"

            sampler2D _NearAlbedo;
            sampler2D _NearSourceNormal;
            sampler2D _MidAlbedo;
            sampler2D _FarAlbedo;
            sampler2D _DetailAlbedo;
            sampler2D _AirportCoreCleanPlateAlbedo;
            sampler2D _AirportCoreCleanPlateMask;
            sampler2D _FullMapWaterMask;
            float4 _NearWorldMinInvSize;
            float4 _MidWorldMinInvSize;
            float4 _FarWorldMinInvSize;
            float4 _NearBlendStartEnd;
            float4 _MidBlendStartEnd;
            float4 _DetailFadeStartEnd;
            float4 _DetailRadiusFadeStartEnd;
            float4 _NearSourceNormalFadeStartEnd;
            float4 _AirportCoreCleanPlateWorldMinInvSize;
            float4 _FullMapWaterWorldMinInvSize;
            fixed4 _Tint;
            half _DetailScale;
            half _NearSourceNormalStrength;
            half _AirportCoreCleanPlateStrength;
            half _FullMapWaterReflectionStrength;
            half _FullMapWaterFresnelStrength;
            half _FullMapWaterSunGlintStrength;
            half _FullMapWaterCrestGlintStrength;
            half _FullMapWaterDistortionStrength;
            half _FullMapWaterRoughness;
            half _Roughness;
            half _DiagnosticMode;
            half _ClipmapEnabled;

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
                float2 worldDx = ddx(world);
                float2 worldDy = ddy(world);
                float2 waterUv =
                    (world - _FullMapWaterWorldMinInvSize.xy) *
                    _FullMapWaterWorldMinInvSize.zw;
                half waterInside = step(0.0, waterUv.x) * step(waterUv.x, 1.0) *
                    step(0.0, waterUv.y) * step(waterUv.y, 1.0);
                half3 waterTierCoverage = half3(0.0h, 0.0h, 0.0h);
                half waterCoverage = 0.0h;
                half waterReflectionAuthority = 0.0h;
                UNITY_BRANCH if (waterInside > 0.5h)
                {
                    waterTierCoverage = tex2Dgrad(
                        _FullMapWaterMask,
                        saturate(waterUv),
                        worldDx * _FullMapWaterWorldMinInvSize.zw,
                        worldDy * _FullMapWaterWorldMinInvSize.zw).rgb;
                    waterCoverage = max(
                        waterTierCoverage.r,
                        max(waterTierCoverage.g, waterTierCoverage.b));
                    waterReflectionAuthority = saturate(
                        waterTierCoverage.r +
                        waterTierCoverage.g * 0.42h +
                        waterTierCoverage.b * 0.10h);
                }
                float2 aerialCanopyUv = QuestFlightAerialCanopyUv(world);
                half aerialCanopyInside = step(0.0, aerialCanopyUv.x) * step(aerialCanopyUv.x, 1.0) *
                    step(0.0, aerialCanopyUv.y) * step(aerialCanopyUv.y, 1.0);
                QuestFlightAerialCanopySample aerialCanopy;
                aerialCanopy.heightMeters = 0.0h;
                aerialCanopy.density = 0.0h;
                aerialCanopy.buildingHeightMeters = 0.0h;
                aerialCanopy.occlusion = 0.0h;
                aerialCanopy.coverage = 0.0h;
                aerialCanopy.terrainValidity = 0.0h;
                aerialCanopy.geometryToMapHandoff = 0.0h;
                aerialCanopy.canopyNormalLocal = half3(0.0h, 1.0h, 0.0h);
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

                UNITY_BRANCH if (_ClipmapEnabled > 0.5h)
                {
                    macro = tex2Dgrad(_ClipGlobalAlbedo, farUv, farUvDx, farUvDy).rgb;
                    fixed3 clipColor;
                    half clipWeight = QuestFlightSampleAircraftCenteredClipmap(
                        world, _ClipMediumGridMinSize, _ClipMediumUvScaleOffsetBlend,
                        false, clipColor);
                    macro = lerp(macro, clipColor, clipWeight);
                    clipWeight = QuestFlightSampleAircraftCenteredClipmap(
                        world, _ClipHighGridMinSize, _ClipHighUvScaleOffsetBlend,
                        true, clipColor);
                    macro = lerp(macro, clipColor, clipWeight);
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

                UNITY_BRANCH if (aerialCanopyInside > 0.5h &&
                    (_AerialCanopyColorStrength > 0.001h || _AerialCanopyNormalStrength > 0.001h))
                {
                    aerialCanopy = QuestFlightSampleAerialCanopy(world);
                    fixed3 canopyColor = QuestFlightApplyAerialCanopyColor(macro, aerialCanopy);
                    macro = canopyColor;
                }

                half cameraDistance = distance(_WorldSpaceCameraPos, input.worldPosition);
                half detailWeight = 1.0 - smoothstep(
                    _DetailFadeStartEnd.x, _DetailFadeStartEnd.y, cameraDistance);
                // This detail is camera-relative rather than airport-relative. The previous
                // world-radius multiplier silently removed ground relief everywhere outside
                // the airport corridor even when a high-resolution clipmap page was resident.
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
                    if (_DiagnosticMode > 3.5)
                        return fixed4(
                            lerp(fixed3(0.16,0.16,0.16), fixed3(0.0,1.0,1.0), waterCoverage),
                            1.0);
                    fixed3 levelColor = lerp(fixed3(0.12,0.32,0.78), fixed3(0.94,0.58,0.08), midWeight);
                    levelColor = lerp(levelColor, fixed3(0.08,0.72,0.30), nearWeight);
                    if (_DiagnosticMode < 1.5)
                        return fixed4(levelColor, 1.0);
                    half overlap = max(
                        4.0 * nearWeight * (1.0 - nearWeight),
                        4.0 * midWeight * (1.0 - midWeight));
                    if (_DiagnosticMode < 2.5)
                        return fixed4(lerp(fixed3(0.03,0.03,0.03), fixed3(1.0,0.12,0.02), overlap), 1.0);
                    half boundary = max(
                        max(BoundaryLine(radius, _NearBlendStartEnd.x), BoundaryLine(radius, _NearBlendStartEnd.y)),
                        max(BoundaryLine(radius, _MidBlendStartEnd.x),
                            BoundaryLine(radius, _MidBlendStartEnd.y)));
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
                UNITY_BRANCH if (aerialCanopy.coverage > 0.001h && _AerialCanopyNormalStrength > 0.001h)
                {
                    normal = QuestFlightApplyAerialCanopyNormal(normal, aerialCanopy);
                }
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half rippleX = sin(world.x * 0.011h + world.y * 0.006h) +
                    sin(world.x * -0.004h + world.y * 0.017h) * 0.55h;
                half rippleZ = cos(world.x * 0.008h - world.y * 0.013h) +
                    cos(world.x * 0.019h + world.y * 0.003h) * 0.45h;
                half3 waterNormal = normalize(half3(
                    rippleX * _FullMapWaterDistortionStrength,
                    1.0h,
                    rippleZ * _FullMapWaterDistortionStrength));
                half diffuse = saturate(dot(normal, lightDirection));
                half3 irradiance = saturate(max(ShadeSH9(half4(normal, 1.0)), 0.0h) * 0.35h +
                    _LightColor0.rgb * (0.08h + diffuse * 0.55h));
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half specular = pow(saturate(dot(normal, halfDirection)), lerp(28.0, 5.0, surfaceRoughness)) *
                    (1.0 - surfaceRoughness) * 0.045;
                half3 color = albedo * irradiance + _LightColor0.rgb * specular;
                UNITY_BRANCH if (waterReflectionAuthority > 0.001h)
                {
                    half3 reflectionDirection = reflect(-viewDirection, waterNormal);
                    half reflectionMip = _FullMapWaterRoughness * 6.0h;
                    half4 encodedReflection =
                        UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectionDirection, reflectionMip);
                    half3 skyReflection = DecodeHDR(encodedReflection, unity_SpecCube0_HDR);
                    half fresnel = pow(
                        1.0h - saturate(dot(waterNormal, viewDirection)),
                        5.0h);
                    half skyLuminance = dot(
                        skyReflection,
                        half3(0.2126h, 0.7152h, 0.0722h));
                    half reflectionSignal = saturate(
                        (skyLuminance - 0.08h) * 1.15h);
                    half3 neutralReflection = lerp(
                        skyLuminance.xxx,
                        skyReflection,
                        0.08h);
                    half reflectionWeight = waterReflectionAuthority *
                        reflectionSignal *
                        (_FullMapWaterReflectionStrength +
                         fresnel * _FullMapWaterFresnelStrength);
                    // Add only reflected radiance. Never replace or tint the
                    // orthophoto base color: water should read as a restrained
                    // highlight, not a blue translucent overlay.
                    color += neutralReflection * reflectionWeight;
                    half sunAlignment = saturate(
                        dot(waterNormal, halfDirection));
                    half broadSunSheen = pow(
                        sunAlignment,
                        lerp(8.0h, 3.0h, _FullMapWaterRoughness));
                    half sunGlint = pow(
                        sunAlignment,
                        lerp(96.0h, 36.0h, _FullMapWaterRoughness));
                    color += _LightColor0.rgb *
                        (broadSunSheen * _FullMapWaterSunGlintStrength * 0.32h +
                         sunGlint * _FullMapWaterSunGlintStrength) *
                        waterReflectionAuthority;
                    // A static, world-locked crest response guarantees that
                    // major water remains legible on Quest even when the
                    // runtime reflection probe is dim or the exact sun lobe is
                    // outside the pilot's route. It is sparse additive light,
                    // not a diffuse color overlay, and is smooth in stereo.
                    half crestSignal = abs(
                        rippleX * 0.62h + rippleZ * 0.38h) * 0.55h;
                    half crestGlint = smoothstep(
                        0.30h,
                        0.78h,
                        crestSignal);
                    half viewGrazing = 1.0h -
                        saturate(dot(waterNormal, viewDirection));
                    half crestViewWeight = 0.42h +
                        0.58h * viewGrazing * viewGrazing;
                    half3 neutralGlint = lerp(
                        half3(1.0h, 1.0h, 1.0h),
                        _LightColor0.rgb,
                        0.65h);
                    color += neutralGlint * crestGlint *
                        crestViewWeight *
                        _FullMapWaterCrestGlintStrength *
                        waterReflectionAuthority;
                }
                fixed4 output = fixed4(color, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
