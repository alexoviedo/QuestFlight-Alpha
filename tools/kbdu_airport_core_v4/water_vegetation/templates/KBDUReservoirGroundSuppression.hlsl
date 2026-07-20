#ifndef KBDU_RESERVOIR_GROUND_SUPPRESSION_INCLUDED
#define KBDU_RESERVOIR_GROUND_SUPPRESSION_INCLUDED

// Must be called by the terrain shader before the opaque reservoir pass. The
// packed reservoir mask alpha suppresses photographed highlights/objects under
// the authoritative water mesh without modifying terrain geometry.
void ApplyKBDUReservoirGroundSuppression(
    half suppression,
    half3 neutralSubstrate,
    inout half3 terrainAlbedo,
    inout half terrainRoughness)
{
    half amount = smoothstep(0.05h, 0.92h, saturate(suppression));
    terrainAlbedo = lerp(terrainAlbedo, neutralSubstrate, amount);
    terrainRoughness = lerp(terrainRoughness, 0.72h, amount);
}

#endif
