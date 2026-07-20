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
float4 _AerialCanopyLocalBounds;
half _AerialCanopyHeightQuantumMeters;
half _AerialCanopyColorStrength;
half _AerialCanopyNormalStrength;
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
};

inline float2 QuestFlightAerialCanopyUv(float2 localXZ)
{
    float2 uv = (localXZ - _AerialCanopyLocalBounds.xy) * _AerialCanopyLocalBounds.zw;
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
    QuestFlightAerialCanopySample sample;
    sample.heightMeters = features.r * 255.0h * _AerialCanopyHeightQuantumMeters;
    sample.density = features.g;
    sample.buildingHeightMeters = features.b * 255.0h * _AerialCanopyHeightQuantumMeters;
    sample.occlusion = canopyNormalAndOcclusion.a;
    // The R8 texture is the final Tier2/Tier3 canopy weight. Its generator already
    // zeros DTM nodata, sub-threshold HAG/density, building-confounded pixels,
    // and Tier0/Tier1 ownership. Do not recreate a broader heuristic here.
    sample.coverage = geometryToMapHandoff;
    sample.terrainValidity = step(0.5h / 255.0h, terrainNormalAndValidity.a) * insideBounds;
    sample.geometryToMapHandoff = geometryToMapHandoff;
    sample.canopyNormalLocal = normalize(canopyNormalAndOcclusion.rgb * 2.0h - 1.0h);
    return sample;
}

inline fixed3 QuestFlightApplyAerialCanopyColor(
    fixed3 orthophotoColor,
    QuestFlightAerialCanopySample canopy)
{
    half weight = canopy.coverage * canopy.density * _AerialCanopyColorStrength;
    half shaded = lerp(1.0h, 0.62h, canopy.occlusion);
    fixed3 canopyColor = lerp(orthophotoColor, _AerialCanopyTint, 0.54h) * shaded;
    return lerp(orthophotoColor, canopyColor, saturate(weight));
}

inline half3 QuestFlightApplyAerialCanopyNormal(
    half3 terrainNormalLocal,
    QuestFlightAerialCanopySample canopy)
{
    half weight = canopy.coverage * canopy.density * _AerialCanopyNormalStrength;
    return normalize(lerp(terrainNormalLocal, canopy.canopyNormalLocal, saturate(weight)));
}

#endif
