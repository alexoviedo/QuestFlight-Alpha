Shader "QuestFlight/KBDU Terrain Pyramid"
{
    Properties
    {
        _NearAlbedo ("Near 4 km NAIP", 2D) = "gray" {}
        _NearSourceNormal ("Near 1 m DEM source normal (ENU)", 2D) = "gray" {}
        _RecoveryMidNWAlbedo ("Recovery mid NW 4 km NAIP", 2D) = "gray" {}
        _RecoveryMidNEAlbedo ("Recovery mid NE 4 km NAIP", 2D) = "gray" {}
        _RecoveryMidSWAlbedo ("Recovery mid SW 4 km NAIP", 2D) = "gray" {}
        _RecoveryMidSEAlbedo ("Recovery mid SE 4 km NAIP", 2D) = "gray" {}
        _MidAlbedo ("Mid 12 km NAIP fallback", 2D) = "gray" {}
        _FarAlbedo ("Far 24 km NAIP", 2D) = "gray" {}
        _DetailAlbedo ("CC0 near/mid microdetail", 2D) = "white" {}
        _NearWorldMinInvSize ("Near min XZ / inverse size", Vector) = (-2000,-2000,0.00025,0.00025)
        _RecoveryMidNWWorldMinInvSize ("Recovery NW min XZ / inverse size", Vector) = (-4032,-32,0.000246063,0.000246063)
        _RecoveryMidNEWorldMinInvSize ("Recovery NE min XZ / inverse size", Vector) = (-32,-32,0.000246063,0.000246063)
        _RecoveryMidSWWorldMinInvSize ("Recovery SW min XZ / inverse size", Vector) = (-4032,-4032,0.000246063,0.000246063)
        _RecoveryMidSEWorldMinInvSize ("Recovery SE min XZ / inverse size", Vector) = (-32,-4032,0.000246063,0.000246063)
        _MidWorldMinInvSize ("Mid min XZ / inverse size", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _FarWorldMinInvSize ("Far min XZ / inverse size", Vector) = (-12000,-12000,0.0000416667,0.0000416667)
        _NearBlendStartEnd ("Near to recovery overlap", Vector) = (1800,1980,0,0)
        _RecoveryBlendStartEnd ("Recovery to mid fallback overlap", Vector) = (3800,4000,0,0)
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
            #include "../AerialSceneryV1/Vegetation/Shaders/AerialCanopyMap.cginc"

            sampler2D _NearAlbedo;
            sampler2D _NearSourceNormal;
            sampler2D _RecoveryMidNWAlbedo;
            sampler2D _RecoveryMidNEAlbedo;
            sampler2D _RecoveryMidSWAlbedo;
            sampler2D _RecoveryMidSEAlbedo;
            sampler2D _MidAlbedo;
            sampler2D _FarAlbedo;
            sampler2D _DetailAlbedo;
            sampler2D _AirportCoreCleanPlateAlbedo;
            sampler2D _AirportCoreCleanPlateMask;
            float4 _NearWorldMinInvSize;
            float4 _RecoveryMidNWWorldMinInvSize;
            float4 _RecoveryMidNEWorldMinInvSize;
            float4 _RecoveryMidSWWorldMinInvSize;
            float4 _RecoveryMidSEWorldMinInvSize;
            float4 _MidWorldMinInvSize;
            float4 _FarWorldMinInvSize;
            float4 _NearBlendStartEnd;
            float4 _RecoveryBlendStartEnd;
            float4 _MidBlendStartEnd;
            float4 _DetailFadeStartEnd;
            float4 _DetailRadiusFadeStartEnd;
            float4 _NearSourceNormalFadeStartEnd;
            float4 _AirportCoreCleanPlateWorldMinInvSize;
            fixed4 _Tint;
            half _DetailScale;
            half _NearSourceNormalStrength;
            half _AirportCoreCleanPlateStrength;
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

            fixed3 SampleRecoveryTile(
                sampler2D source,
                float2 world,
                float4 worldMinInvSize,
                float2 worldDx,
                float2 worldDy)
            {
                float2 uv = saturate((world - worldMinInvSize.xy) * worldMinInvSize.zw);
                return tex2Dgrad(
                    source,
                    uv,
                    worldDx * worldMinInvSize.zw,
                    worldDy * worldMinInvSize.zw).rgb;
            }

            fixed3 SampleRecoveryNorth(float2 world, float2 worldDx, float2 worldDy)
            {
                UNITY_BRANCH if (world.x <= -32.0)
                    return SampleRecoveryTile(_RecoveryMidNWAlbedo, world,
                        _RecoveryMidNWWorldMinInvSize, worldDx, worldDy);
                UNITY_BRANCH if (world.x >= 32.0)
                    return SampleRecoveryTile(_RecoveryMidNEAlbedo, world,
                        _RecoveryMidNEWorldMinInvSize, worldDx, worldDy);
                fixed3 west = SampleRecoveryTile(_RecoveryMidNWAlbedo, world,
                    _RecoveryMidNWWorldMinInvSize, worldDx, worldDy);
                fixed3 east = SampleRecoveryTile(_RecoveryMidNEAlbedo, world,
                    _RecoveryMidNEWorldMinInvSize, worldDx, worldDy);
                return lerp(west, east, smoothstep(-32.0, 32.0, world.x));
            }

            fixed3 SampleRecoverySouth(float2 world, float2 worldDx, float2 worldDy)
            {
                UNITY_BRANCH if (world.x <= -32.0)
                    return SampleRecoveryTile(_RecoveryMidSWAlbedo, world,
                        _RecoveryMidSWWorldMinInvSize, worldDx, worldDy);
                UNITY_BRANCH if (world.x >= 32.0)
                    return SampleRecoveryTile(_RecoveryMidSEAlbedo, world,
                        _RecoveryMidSEWorldMinInvSize, worldDx, worldDy);
                fixed3 west = SampleRecoveryTile(_RecoveryMidSWAlbedo, world,
                    _RecoveryMidSWWorldMinInvSize, worldDx, worldDy);
                fixed3 east = SampleRecoveryTile(_RecoveryMidSEAlbedo, world,
                    _RecoveryMidSEWorldMinInvSize, worldDx, worldDy);
                return lerp(west, east, smoothstep(-32.0, 32.0, world.x));
            }

            fixed3 SampleRecovery(float2 world, float2 worldDx, float2 worldDy)
            {
                UNITY_BRANCH if (world.y <= -32.0)
                    return SampleRecoverySouth(world, worldDx, worldDy);
                UNITY_BRANCH if (world.y >= 32.0)
                    return SampleRecoveryNorth(world, worldDx, worldDy);
                fixed3 south = SampleRecoverySouth(world, worldDx, worldDy);
                fixed3 north = SampleRecoveryNorth(world, worldDx, worldDy);
                return lerp(south, north, smoothstep(-32.0, 32.0, world.y));
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 world = input.worldPosition.xz;
                float2 worldDx = ddx(world);
                float2 worldDy = ddy(world);
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
                half recoveryWeight = 1.0 - smoothstep(
                    _RecoveryBlendStartEnd.x, _RecoveryBlendStartEnd.y, radius);
                half midWeight = 1.0 - smoothstep(_MidBlendStartEnd.x, _MidBlendStartEnd.y, radius);
                fixed3 macro;
                UNITY_BRANCH if (radius <= _NearBlendStartEnd.x)
                {
                    macro = tex2Dgrad(_NearAlbedo, nearUv, nearUvDx, nearUvDy).rgb;
                }
                else if (radius < _NearBlendStartEnd.y)
                {
                    fixed3 nearColor = tex2Dgrad(_NearAlbedo, nearUv, nearUvDx, nearUvDy).rgb;
                    fixed3 recoveryColor = SampleRecovery(world, worldDx, worldDy);
                    macro = lerp(recoveryColor, nearColor, nearWeight);
                }
                else if (radius <= _RecoveryBlendStartEnd.x)
                {
                    macro = SampleRecovery(world, worldDx, worldDy);
                }
                else if (radius < _RecoveryBlendStartEnd.y)
                {
                    fixed3 recoveryColor = SampleRecovery(world, worldDx, worldDy);
                    fixed3 midColor = tex2Dgrad(_MidAlbedo, midUv, midUvDx, midUvDy).rgb;
                    macro = lerp(midColor, recoveryColor, recoveryWeight);
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

                UNITY_BRANCH if (aerialCanopyInside > 0.5h &&
                    (_AerialCanopyColorStrength > 0.001h || _AerialCanopyNormalStrength > 0.001h))
                {
                    aerialCanopy = QuestFlightSampleAerialCanopy(world);
                    macro = QuestFlightApplyAerialCanopyColor(macro, aerialCanopy);
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
                    levelColor = lerp(levelColor, fixed3(0.60,0.18,0.82), recoveryWeight);
                    levelColor = lerp(levelColor, fixed3(0.08,0.72,0.30), nearWeight);
                    if (_DiagnosticMode < 1.5)
                        return fixed4(levelColor, 1.0);
                    half overlap = max(
                        max(4.0 * nearWeight * (1.0 - nearWeight),
                            4.0 * recoveryWeight * (1.0 - recoveryWeight)),
                        4.0 * midWeight * (1.0 - midWeight));
                    if (_DiagnosticMode < 2.5)
                        return fixed4(lerp(fixed3(0.03,0.03,0.03), fixed3(1.0,0.12,0.02), overlap), 1.0);
                    half boundary = max(
                        max(BoundaryLine(radius, _NearBlendStartEnd.x), BoundaryLine(radius, _NearBlendStartEnd.y)),
                        max(
                            max(BoundaryLine(radius, _RecoveryBlendStartEnd.x),
                                BoundaryLine(radius, _RecoveryBlendStartEnd.y)),
                            max(BoundaryLine(radius, _MidBlendStartEnd.x),
                                BoundaryLine(radius, _MidBlendStartEnd.y))));
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
