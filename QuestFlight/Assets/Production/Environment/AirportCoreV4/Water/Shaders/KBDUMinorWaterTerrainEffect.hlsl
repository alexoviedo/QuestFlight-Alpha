#ifndef KBDU_MINOR_WATER_TERRAIN_EFFECT_INCLUDED
#define KBDU_MINOR_WATER_TERRAIN_EFFECT_INCLUDED

// Terrain-inline minor-water prototype. The caller supplies an authored mask
// sampled in the same terrain UV space. This function changes terrain shading
// only: it creates no renderer, transparent pass, depth write, or camera.
void ApplyKBDUMinorWaterTerrainEffect(
    half waterMask,
    half wetBankMask,
    half3 reflectionColor,
    half3 rippleNormal,
    half3 viewDirection,
    inout half3 albedo,
    inout half3 normalWorld,
    inout half roughness,
    inout half metallic)
{
    half channel = smoothstep(0.12h, 0.78h, saturate(waterMask));
    half bank = saturate(wetBankMask) * (1.0h - channel);
    half3 waterTint = half3(0.10h, 0.20h, 0.20h);
    half fresnel = pow(1.0h - saturate(dot(normalize(normalWorld), normalize(viewDirection))), 5.0h);

    normalWorld = normalize(lerp(normalWorld, rippleNormal, channel * 0.30h));
    albedo = lerp(albedo, albedo * half3(0.70h,0.78h,0.76h), bank * 0.45h);
    albedo = lerp(albedo, lerp(waterTint, reflectionColor, 0.18h + fresnel * 0.36h), channel * 0.72h);
    roughness = lerp(roughness, 0.34h, channel);
    roughness = lerp(roughness, 0.62h, bank * 0.40h);
    metallic = lerp(metallic, 0.0h, channel);
}

#endif
