# KBDU runway photoreal material V1 — 2026-08-07

## Scope and source

This bounded pass replaces only the visual material on the existing authoritative
KBDU runway 08/26 pavement. It does not alter runway geometry, grade, collider,
transform, spawn/reset behavior, or the separately authored marking geometry.

The appearance target was derived from the [City of Boulder airport page](https://bouldercolorado.gov/government/departments/airport)
and its summer/aerial imagery together with the [current FAA-derived AOPA airport record](https://www.aopa.org/Kneeboard/Kneeboard/GeneratePdf?airportId=KBDU):
maintained neutral-gray asphalt, dense fine aggregate, restrained oxidation and
wear, sparse sealed cracks, and no exaggerated damage or repeating slab pattern.
Reference photographs remain external and uncommitted.

The project source is a generated, texture-only input with no third-party pixels:

- source: `tools/kbdu_runway_photoreal/sources/KBDU_Runway_Asphalt_GeneratedSource.png`
- source SHA-256: `c8c239b1e8024a2296ca7e9fb158949922a7f6528bda41c8728de9c19bc34fa0`
- prompt/provenance: `tools/kbdu_runway_photoreal/SOURCE_PROMPT.md`
- deterministic compiler: `tools/kbdu_runway_photoreal/generate_runway_material.py`
- compiler seed: `20260807`

## Root cause and implementation

The previous shader used a texture, but normalized away most of its color and
mixed only a small fraction into a flat warm-brown base. Its macro wear, repairs,
and joints were analytic sine-like fields, which limited close detail and risked
large-scale procedural repetition.

The replacement keeps the one opaque runway draw and its existing bounded depth
bias, but now preserves the generated photographic aggregate signal. A second
rotated sample breaks repetition without another texture sampler. The existing
packed-texture slot is repurposed as a unique runway-local 08-to-26 macro map:

- R: longitudinal tire polish and wear;
- G: perceptual roughness;
- B: restrained repairs and edge weathering.

Runtime derivatives are 1024² albedo and tangent normal textures plus a 2048×256
source macro map imported as 1024×128 on Android. They use ASTC 6×6, mipmaps,
trilinear filtering, and anisotropy 8. The material remains opaque, instancing
compatible, unlightmapped, and uses no displacement, transparency, decal, or
additional pass.

## Cost

The material adds zero renderers, materials, draws, transparent slots, shader
passes, or texture samplers. Estimated Android residency for its three textures
is 1,327,872 bytes, down from the prior manifest estimate of 2,505,152 bytes
(-1,177,280 bytes). The integrated simulator contract remained 51 renderers,
504,064 triangles, 10 materials, and 4× MSAA. The repository-wide static budget
runner is currently blocked before reporting by a pre-existing duplicate-aircraft
selection in the accepted OVR controller branch; no runway budget regression is
involved.

## Visual review

Full-resolution ground, cockpit, takeoff, and aerial images were inspected. The
result has continuous neutral-gray asphalt, readable fine aggregate near the
surface, restrained longitudinal wear, aligned markings, and no visible slab
bands, large periodic squares, z-fighting, or duplicate runway. The runway
remains legible from pattern altitude without overpowering the orthophoto.

Key external artifacts:

- visual QA root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\runway_photoreal_v1_20260807_101217\visual_qa`
- cockpit: `screenshots\04_runway_from_cockpit.png`
- surface/markings: `screenshots\05_runway_surface_markings.png`
- takeoff: `screenshots\13_takeoff_roll.png`
- approach/aerial: `screenshots\16_approach.png`
- stereo contact sheet: `simulator\meta_xr_simulator\visual_gate\simulator_stereo_contact_sheet.png`

## Validation

- deterministic texture regeneration: **PASS**; recorded runtime hashes reproduced;
- Python compiler syntax: **PASS**;
- runway-specific EditMode test: **PASS 1/1**;
- shader import/compile: **PASS**;
- full EditMode invocation: **BLOCKED BY FOUR PRE-EXISTING COCKPIT/OVR FAILURES** after correcting and rerunning the runway-specific import assertion;
- PlayMode: **7/9**, with two pre-existing aircraft hierarchy/visibility failures unrelated to the runway;
- inspected runway Visual QA: **PASS** across cockpit, surface, takeoff, and aerial views;
- aggregate Visual QA wrapper: **BLOCKED** by an existing missing magnetic-compass QA material contract and stale retired V4 shot IDs;
- Meta XR live OpenXR stereo: both eyes connected, nonblank, single-pass instanced, scene-consistent, 72 Hz requested/measured, 4× MSAA; aggregate status **BLOCKED_MISSING_RECORDED_REPLAYS** for the absent genuine `.vrs` library and an existing runtime-name check;
- product dependency closure: **BLOCKED** by eight pre-existing unclassified OVR/cockpit dependencies;
- Android ARM64/Vulkan/OpenXR release-style build: **PASS**, 174,592,893 bytes, SHA-256 `0ab18fbe6533e467b999eb26d0141cce93951492add0606d4ea9f2859c71f0a8`.

No physical Quest visual or timing acceptance is claimed for this material yet.
The APK and all captures remain external artifacts and are not committed.
