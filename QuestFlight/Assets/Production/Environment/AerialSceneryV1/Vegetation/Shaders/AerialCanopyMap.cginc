#ifndef QUESTFLIGHT_AERIAL_CANOPY_MAP_INCLUDED
#define QUESTFLIGHT_AERIAL_CANOPY_MAP_INCLUDED

// Tier 2 and Tier 3 consume the validated lidar products directly. They do not create
// billboard geometry or a duplicate runtime texture.  Bounds are
// (minimum local X, minimum local Z, inverse width, inverse depth).
sampler2D _AerialCanopySurfaceFeatures;
// RGB is the DTM/terrain surface normal, not a CHM-derived canopy normal.
// Vegetation V1 intentionally consumes only the A validity bitfield.
sampler2D _AerialTerrainNormalAndValidity;
// This separate product is explicitly derived from the smoothed canopy-height
// model.  Its heuristic source-classification limitation remains documented.
sampler2D _AerialCanopyNormalOcclusion;
// Project-authored R8 geometry ownership on the exact lidar grid: 0 under
// conservative Tier0/Tier1 footprints (plus safety pad), spatially feathered to 1
// where the lidar canopy map owns representation. Tier2 never carves this mask.
// This replaces a scalar handoff,
// which cannot describe ownership over the finite 4,096 m lidar square.
sampler2D _AerialCanopyHandoffMask;
sampler2D _FullMapCanopyVisual;
float4 _AerialCanopyLocalBounds;
float4 _FullMapCanopyWorldMinInvSize;
float4 _FullMapCanopyGeometryFadeStartEnd;
float4 _AerialCanopyProductToSourceRow0;
float4 _AerialCanopyProductToSourceRow1;
float4 _AerialCanopySourceToProductRow0;
float4 _AerialCanopySourceToProductRow1;
half _AerialCanopyHeightQuantumMeters;
half _AerialCanopyColorStrength;
half _AerialCanopyNormalStrength;
half _FullMapCanopyColorStrength;
half _FullMapCanopyNormalStrength;
fixed3 _AerialCanopyTint;

struct QuestFlightAerialCanopySample
{
    half heightMeters;
    half density;
    half buildingHeightMeters;
    half occlusion;
    half coverage;
    half terrainValidity;
    half geometryToMapHandoff;
    half3 canopyNormalLocal;
    half fullMapWeight;
};

inline float2 QuestFlightAerialCanopyUv(float2 localXZ)
{
    // Product world/orthophoto positions use KBDU-local AEQD while the compact
    // lidar textures retain their source UTM13N grid. Convert before sampling
    // so the canopy response stays on the photographed trees.
    float2 sourceXZ = float2(
        dot(_AerialCanopyProductToSourceRow0.xy, localXZ) + _AerialCanopyProductToSourceRow0.z,
        dot(_AerialCanopyProductToSourceRow1.xy, localXZ) + _AerialCanopyProductToSourceRow1.z);
    float2 uv = (sourceXZ - _AerialCanopyLocalBounds.xy) * _AerialCanopyLocalBounds.zw;
    // Source row zero is north; Unity texture V zero is south after import.
    return float2(uv.x, uv.y);
}

inline QuestFlightAerialCanopySample QuestFlightSampleAerialCanopy(float2 localXZ)
{
    float2 uv = QuestFlightAerialCanopyUv(localXZ);
    half insideBounds = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    float2 sampledUv = saturate(uv);
    half4 features = tex2D(_AerialCanopySurfaceFeatures, sampledUv);
    half4 terrainNormalAndValidity = tex2D(_AerialTerrainNormalAndValidity, sampledUv);
    half4 canopyNormalAndOcclusion = tex2D(_AerialCanopyNormalOcclusion, sampledUv);
    half geometryToMapHandoff = tex2D(_AerialCanopyHandoffMask, sampledUv).r * insideBounds;
    float2 fullUv = (localXZ - _FullMapCanopyWorldMinInvSize.xy) *
        _FullMapCanopyWorldMinInvSize.zw;
    half fullInside = step(0.0, fullUv.x) * step(fullUv.x, 1.0) *
        step(0.0, fullUv.y) * step(fullUv.y, 1.0);
    half4 fullMap = tex2D(_FullMapCanopyVisual, saturate(fullUv));
    half cameraDistance = distance(localXZ, _WorldSpaceCameraPos.xz);
    half geometryHandoff = smoothstep(
        _FullMapCanopyGeometryFadeStartEnd.x,
        max(_FullMapCanopyGeometryFadeStartEnd.x + 1.0,
            _FullMapCanopyGeometryFadeStartEnd.y),
        cameraDistance);
    half fullCoverage = fullMap.r * fullInside * geometryHandoff;
    QuestFlightAerialCanopySample sample;
    sample.heightMeters = features.r * 255.0h * _AerialCanopyHeightQuantumMeters;
    sample.density = max(features.g, fullMap.r);
    sample.buildingHeightMeters = features.b * 255.0h * _AerialCanopyHeightQuantumMeters;
    sample.occlusion = lerp(
        canopyNormalAndOcclusion.a,
        0.30h + fullMap.r * 0.34h,
        fullCoverage);
    // The R8 texture is the final Tier2/Tier3 canopy weight. Its generator already
    // zeros DTM nodata, sub-threshold HAG/density, building-confounded pixels,
    // and Tier0/Tier1 ownership. Do not recreate a broader heuristic here.
    sample.coverage = saturate(
        geometryToMapHandoff + fullCoverage * (1.0h - geometryToMapHandoff));
    sample.terrainValidity = step(0.5h / 255.0h, terrainNormalAndValidity.a) * insideBounds;
    sample.geometryToMapHandoff = geometryToMapHandoff;
    half3 sourceNormal = canopyNormalAndOcclusion.rgb * 2.0h - 1.0h;
    half2 productNormalXZ = half2(
        dot(_AerialCanopySourceToProductRow0.xy, sourceNormal.xz),
        dot(_AerialCanopySourceToProductRow1.xy, sourceNormal.xz));
    half2 fullNormalXZ = fullMap.ba * 2.0h - 1.0h;
    half fullNormalY = sqrt(saturate(1.0h - dot(fullNormalXZ, fullNormalXZ)));
    half3 localCanopyNormal = normalize(
        half3(productNormalXZ.x, sourceNormal.y, productNormalXZ.y));
    sample.canopyNormalLocal = normalize(lerp(
        localCanopyNormal,
        half3(fullNormalXZ.x, max(0.18h, fullNormalY), fullNormalXZ.y),
        fullCoverage));
    sample.fullMapWeight = fullCoverage;
    return sample;
}

inline fixed3 QuestFlightApplyAerialCanopyColor(
    fixed3 orthophotoColor,
    QuestFlightAerialCanopySample canopy)
{
    half localWeight = canopy.coverage * (1.0h - canopy.fullMapWeight) *
        canopy.density * _AerialCanopyColorStrength;
    half shaded = lerp(1.0h, 0.62h, canopy.occlusion);
    fixed3 canopyColor = lerp(orthophotoColor, _AerialCanopyTint, 0.54h) * shaded;
    fixed3 result = lerp(orthophotoColor, canopyColor, saturate(localWeight));
    // The full-map source is the same orthophoto displayed by the terrain.  It
    // adds aggregate crown relief without painting a second green overlay.
    half fullWeight = canopy.fullMapWeight * _FullMapCanopyColorStrength;
    return result * lerp(1.0h, lerp(0.95h, 0.82h, canopy.occlusion), fullWeight);
}

inline half3 QuestFlightApplyAerialCanopyNormal(
    half3 terrainNormalLocal,
    QuestFlightAerialCanopySample canopy)
{
    half strength = lerp(
        _AerialCanopyNormalStrength,
        _FullMapCanopyNormalStrength,
        canopy.fullMapWeight);
    half weight = canopy.coverage * canopy.density * strength;
    return normalize(lerp(terrainNormalLocal, canopy.canopyNormalLocal, saturate(weight)));
}

#endif
