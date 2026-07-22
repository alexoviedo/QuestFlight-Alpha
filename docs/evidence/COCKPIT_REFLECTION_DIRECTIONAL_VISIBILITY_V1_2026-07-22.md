# Cockpit Reflection and Directional Visibility V1 — 2026-07-22

## Scope and source

This bounded correction starts from `feature/grove-cockpit-reflection-v1` at
`58822eafeb0002acb8e6ac1878dd7c0827e65638`. The accepted semantic-grove and
facade work is preserved unchanged. The correction addresses only three worn
defects: implausible cabin-reflection content, absent instrument-glass response,
and unstable or invisible cockpit shading. It changes no scene hierarchy,
camera ownership, seat calibration, flight authority, scenery, physics, input,
or shipping architecture.

## Geometry-derived optics

The old cabin texture was a hand-authored analytic region map. The replacement
is a deterministic 256×128 equirectangular EXR rendered offline from the actual
licensed C172 interior geometry: panel, gauges, yokes, seats, doors, headliner,
and window openings. Its SHA-256 is
`cef4433bab4a224d2fa02707aba62e964204c2b9693b5110bdf7c66b1fb78907`.
The cabin-glass shader box-projects that texture inside the serialized cabin
bounds and uses the existing environment reflection outside the cabin. No
runtime reflection camera or realtime probe update exists.

The production instrument-glass owner is the broad panel cover
`GlassPartNew_GlassMat_New_0_GlassMat_New_0`. The obsolete 8.6 cm single-lens
shell is disabled. The broad cover uses one low-opacity, depth-biased reflection
pass with the same geometry-derived cabin content; instrument faces remain
readable. Functional instrument data binding was not part of this correction.

## Stable directional cockpit depth

Offline authoring raycasts the actual cockpit geometry for four aircraft-local
sun directions (`+Z`, `+X`, `-Z`, `-X`) at 58.6 degrees elevation, using three
soft yaw samples per direction. The RGBA visibility result is serialized as
additional vertex streams on twenty existing opaque interior renderers:
231,110 vertices and 1,919,266 source-asset bytes. At runtime the existing
single-pass interior shader interpolates those channels from the current
aircraft-local sun direction.

This adds zero renderers, zero draws, zero materials, zero texture lookups, zero
runtime raycasts, and no shadow map, cascade, projector, or screen-space overlay.
It therefore cannot recreate the rejected overlapping-triangle flicker path.
The visibility manifest records every renderer, source mesh, stream asset,
vertex count, mean visibility, and SHA-256.

## Validation and evidence

Artifact root:
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\cockpit_reflection_shadow_final_20260722_045140`

| Gate | Result |
|---|---|
| Unity EditMode | PASS 117/117 |
| Unity PlayMode | PASS 9/9 |
| Full `validate_product.ps1` | PASS |
| Product integrity / dependency closure | PASS |
| Production Visual QA | PASS 81/81; human headset review required |
| Full-resolution lead inspection | PASS; no blank glass, overlay seam, or flickering shadow geometry |
| Static Quest planning | PASS |
| Meta XR Simulator 201.0 live stereo | PASS; nonblank, scene-consistent eyes |
| Recorded simulator aggregate | `BLOCKED_MISSING_RECORDED_REPLAYS` for ten genuine `.vrs` sessions |
| Android release-style build | PASS |

Primary full-resolution contact sheet:
`product_validation_final\visual_qa\visual_qa_contact_sheet.png`.
Cockpit-specific frames are `pasv1_18_cockpit_side_window.png`,
`pasv1_22_cockpit_windshield.png`, `pasv1_23_instrument_glass.png`, and
`pasv1_20_cockpit_shadow_candidate.png` under that run's `screenshots` folder.
Live stereo is under
`simulator_final\meta_xr_simulator\visual_gate\simulator_stereo_contact_sheet.png`.

## Static budget and APK

| Metric | Result | Ceiling |
|---|---:|---:|
| Maximum selected draws | 55 | 75 |
| Maximum selected triangles | 628,444 | 650,000 |
| Serialized mesh triangles | 793,823 | 800,000 |
| Maximum visible / active materials | 27 / 27 | 28 |
| Estimated texture bytes | 211,911,484 | 220,200,960 |
| Transparent material slots | 3 | 3 |
| Shaders / keyword combinations | 12 / 17 | bounded current contract |
| MSAA | 4x | 4x |

The final APK is ignored and not committed:

- path: `QuestFlight/Builds/Android/QuestFlight-production.apk`;
- size: 136,160,369 bytes;
- SHA-256: `a7666846cd7e30f4685c3bc4744240fc8df879eae4c1457dda2c8330569ac557`.

The APK was rebuilt after the final runtime manifest refresh. Documentation
changes after this build do not enter the Unity player. It will be rebuilt if
any runtime byte changes before worn acceptance.

## Decision boundary

This is a machine-validated worn-test candidate, not final C172 optical fidelity,
photorealism, comfort, or physical Quest performance acceptance. The static
reflection cannot represent a moving pilot, and four-direction visibility is a
bounded approximation rather than a full realtime penumbra. Exact-APK worn
review must confirm that cabin content reads plausibly, instrument sheen is
visible without obscuring gauges, directional depth is stable, and no flicker or
discomfort occurs. Main remains unchanged until that review passes.
