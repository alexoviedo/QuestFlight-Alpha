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
    half3 photographedBase = albedo;
    half3 waterTint = photographedBase * half3(0.76h, 0.86h, 0.91h);
    half fresnel = pow(1.0h - saturate(dot(normalize(normalWorld), normalize(viewDirection))), 5.0h);

    normalWorld = normalize(lerp(normalWorld, rippleNormal, channel * 0.18h));
    albedo = lerp(albedo, photographedBase * half3(0.82h,0.88h,0.86h), bank * 0.28h);
    half3 reflectedWater = lerp(waterTint, reflectionColor, 0.08h + fresnel * 0.18h);
    albedo = lerp(albedo, reflectedWater, channel * 0.46h);
    roughness = lerp(roughness, 0.42h, channel);
    roughness = lerp(roughness, 0.62h, bank * 0.40h);
    metallic = lerp(metallic, 0.0h, channel);
}

#endif
