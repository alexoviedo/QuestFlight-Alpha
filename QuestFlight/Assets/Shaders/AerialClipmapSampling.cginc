#ifndef QUESTFLIGHT_AERIAL_CLIPMAP_SAMPLING_INCLUDED
#define QUESTFLIGHT_AERIAL_CLIPMAP_SAMPLING_INCLUDED

sampler2D _ClipGlobalAlbedo;
UNITY_DECLARE_TEX2DARRAY(_ClipHighAlbedo);
UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_ClipMediumAlbedo);
float4 _ClipHighGridMinSize;
float4 _ClipMediumGridMinSize;
float4 _ClipHighUvScaleOffsetBlend;
float4 _ClipMediumUvScaleOffsetBlend;

half QuestFlightSampleAircraftCenteredClipmap(
    float2 world,
    float4 gridMinSize,
    float4 uvScaleOffsetBlend,
    bool highTier,
    out fixed3 color)
{
    color = fixed3(0, 0, 0);
    float tileSize = gridMinSize.z;
    float2 pagePosition = (world - gridMinSize.xy) * gridMinSize.w;
    half inside = step(0.0, pagePosition.x) * step(pagePosition.x, 3.0) *
        step(0.0, pagePosition.y) * step(pagePosition.y, 3.0);
    UNITY_BRANCH if (inside < 0.5h)
        return 0.0h;

    float2 cell = min(floor(pagePosition), 2.0);
    float slice = cell.y * 3.0 + cell.x;
    float2 pageUv = frac(pagePosition) * uvScaleOffsetBlend.xy +
        uvScaleOffsetBlend.zz;
    // Explicit world-space gradients remain continuous when the selected array
    // slice changes at a page edge. Terrain and projected roofs call this same
    // function, so they cannot choose different mips or edge weights.
    float2 pageUvDx = ddx(world) * gridMinSize.w * uvScaleOffsetBlend.xy;
    float2 pageUvDy = ddy(world) * gridMinSize.w * uvScaleOffsetBlend.xy;
    color = highTier
        ? _ClipHighAlbedo.SampleGrad(
            sampler_ClipHighAlbedo, float3(pageUv, slice), pageUvDx, pageUvDy).rgb
        : _ClipMediumAlbedo.SampleGrad(
            sampler_ClipHighAlbedo, float3(pageUv, slice), pageUvDx, pageUvDy).rgb;
    float edgeMeters = min(
        min(pagePosition.x, 3.0 - pagePosition.x),
        min(pagePosition.y, 3.0 - pagePosition.y)) * tileSize;
    return smoothstep(0.0, max(1.0, uvScaleOffsetBlend.w), edgeMeters);
}

#endif
