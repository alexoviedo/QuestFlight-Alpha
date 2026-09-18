# Existing baked-lighting refresh

User authorized one narrow refresh and explicitly skipped Meta simulator validation. The existing scene was baked directly without running the historical airport integration command, which would reconstruct airport content.

Unity's bake snapshot contains two GI-contributing renderers: `ExecutiveTerminal_3335_LOD0` and `Type2THangarRow_3354_LOD0`. Eighteen light probes were refreshed. The result remains one 512 x 512 non-directional lightmap. The scene file is byte-identical to the pre-refresh scene; no sun, ambient, fog, geometry, physics, static flags or visibility distances changed.

This is **not** map-wide shadow coverage. New bridge concrete, grove and streamed building shaders use their own sun/ambient shading rather than this lightmap. A rebake cannot add missing cast shadows to those systems. Enabling GI across the map would require a separately scoped rendering/asset change and is outside this narrow refresh.

Bake succeeded with no non-finite diagnostic. Independent OpenEXR inspection found all 512 x 512 x 4 samples finite; the atlas preview was inspected. Mean absolute change versus the previous atlas is 0.01216 across RGBA channels; this does not establish perceptible improvement in the headset. The original Android ASTC HDR 6x6 import settings and GUIDs were restored after Unity reset its generated texture platform overrides. Atlas dimensions and compression remain unchanged.

Evidence and pre-refresh backup: `../T2-QuestFlight-product-artifacts/lighting_refresh_20260918/`. No simulator run or broad automated test campaign. Android ARM64/Vulkan/OpenXR build succeeded with zero errors and 17 warnings; build integrity passed. The APK was installed successfully on Quest. SHA-256: `a863b0cb49832f35a774b011dd99ea13afceef507f56a9bfe2d52e20ed95fe1d`. No worn visual acceptance or new performance measurement is claimed.
