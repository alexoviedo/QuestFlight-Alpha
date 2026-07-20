# Photoreal Aerial Surface V1 evidence — 2026-07-20

## Outcome

**ACCEPTED DEVELOPMENT BASELINE — merge `feature/photoreal-aerial-surface-v1`
to `main` and tag the exact integrated result.**

The milestone materially fixes the semantic-topology defect that caused lidar
relief and generated geometry to miss the features visible in the ground
imagery. Product validation, dependency closure, 114 EditMode tests, nine
PlayMode tests, the 81-view machine Visual QA, the scoped static budget, and an
Android release-style build pass. Full-resolution machine review still finds
close vegetation, canopy volume, and some reflection/lighting presentation below
the requested visual targets. A subsequent worn Quest 3 smoke of the exact APK
confirmed correct building/ground alignment, mostly strong building presentation,
consistent left/right ground sharpness, no noticed stereo instability or
discomfort, and an overall human score of 8/10. The remaining visible blocker is
the absence of rounded canopy volume. No physical Quest timing acceptance was
collected for this exact APK.

This document does not claim literal photorealism, surveyed topology, navigation
accuracy, FAA approval, training credit, final flight-model fidelity, or final
Quest performance.

## Source and scope

- Source `main` and annotated development-baseline tag:
  `197283fc4f17ae1f78db57c6fcc4e27dc995cd66` /
  `standalone-scenery-baseline-v1`.
- Candidate branch: `feature/photoreal-aerial-surface-v1`.
- Artifact root:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\photoreal_aerial_surface_v1_20260720_114230`.
- Standalone Quest 3 remains the sole shipping target: Android ARM64, IL2CPP,
  Vulkan, OpenXR single-pass instanced, ASTC, and 4x MSAA.
- Water remains orthophoto-only. Runway, mountains, sky, input, flight authority,
  and instrument functionality were not expanded.

## Supported root cause and alignment correction

Two independent coordinate errors explained the visible mismatch.

1. Unity's generic OBJ importer changes the handedness of source vertices. The
   semantic building meshes were therefore mirrored east/west even though the
   source OBJ coordinates were correct. An editor-only native mesh compiler now
   parses the documented `+X east, +Y up, +Z north` vertices directly, preserves
   source bounds, writes non-readable native Unity meshes, and serializes them
   with identity transforms. Runtime conversion or repair is not used.
2. The lidar rasters use an origin-relative UTM zone 13N grid while the
   orthophoto, OSM geometry, runway, and Unity product world use local WGS84
   azimuthal-equidistant coordinates. Treating their numeric deltas as the same
   frame introduced as much as 7.444086 m of horizontal error. Offline queries
   now use an exact `pyproj` AEQD↔UTM transform with a maximum round-trip
   residual of `3.026798e-9` m. The fixed runtime sampling affine has a maximum
   position residual of `0.002037` m and rotates lidar horizontal normals
   `0.145446988159°` counterclockwise into product axes.

The final architecture assigns narrow authority to each source rather than
warping one monolithic mesh: 3DEP DTM owns bare-earth elevation, OSM owns
building footprints, lidar DSM/nDSM supplies only gated height and canopy-map
evidence, the legal orthophoto owns appearance, and authored objects own the
airport hero layer.

## Imagery source and runtime pyramid

The selected imagery is the City of Boulder `2020 Imagery` ArcGIS item
`22b7c93a74394696824258f807f345ba`, produced by Pictometry International and
published under CC0-1.0. The item describes nine-inch orthogonal source imagery
from Spring/Summer 2020. Runtime detail is claimed only at the actual compiled
densities, not at the publisher's source description.

| Runtime level | Extent / texture | Effective density |
| --- | --- | ---: |
| Airport near | 4 km / 8192² | 0.48828125 m/texel |
| Pattern base | 12 km / 8192² | 1.46484375 m/texel |
| Pattern overlap cells | four bounded 2048² cells | 1.984375 m/texel |
| Far context | 24 km / 4096² | 5.859375 m/texel |

The seven aligned textures use fixed world-space mappings, mipmaps, Android
ASTC formats, and an explicitly serialized `GeospatialMipController`. No AI
upscaling, runtime map download, or world expansion is present. The exact
source/item/service hashes, 5,551-tile snapshot hash, output hashes, and import
ledger are in `docs/ENVIRONMENT_IMAGERY_DECISION.md`,
`docs/GEOSPATIAL_TEXTURE_PYRAMID.md`, and the committed source manifest. Raw
tiles and source responses remain external.

## DTM, DSM, nDSM, and terrain

USGS 3DEP work unit `CO_DRCOG_1_2020` is a United States government work/public-
domain source. Compact committed products include DTM, DSM, nDSM, surface class,
normal, density/occlusion, and confidence rasters; raw EPT/LAZ data remains
external.

Only the DTM displaces terrain. DSM/nDSM is never applied as ground relief
because it contains roofs, crowns, vehicles, fill, and noise. The fixed
product-AEQD near terrain contains 46,426 vertices and 92,000 triangles. Outside
declared high-variation source cells, its mesh-to-raster reconstruction error is
0.101071 m mean absolute, 0.357723 m p95, and 1.387371 m maximum. The authored
runway plane differs by at most `4.528938e-7` m. The integrated airport boundary
reported exactly 0 m disagreement across 4,404 probes. These are deterministic
mesh/source and integration measurements, not surveyed terrain accuracy.

## Building semantic layer

- 9,692 non-hero OSM footprints in the 0–4 km semantic layer are represented
  by 16 offline spatial batches totaling 128,092 triangles.
- Two airport-core footprints remain owned by authored hero objects and are not
  duplicated by the semantic layer.
- Footprints are emitted in the product AEQD frame without mirroring or cell-
  sized HLOD rectangles. Native mesh bounds and centroids are verified against
  their sources.
- OSM tags and deterministic rules remain the primary height sources. Only 28
  bounded candidates use non-authoritative lidar support; no surveyed-height or
  10-percent vertical-error claim is made.
- One shared opaque material separates roof and facade response. This solves
  coverage, grounding, footprint, and scale-pop architecture, but the simplified
  roof forms, generic facades, repetitive material response, and weak local
  lighting remain visibly below the requested art standard.

## Canopy decision

The final representation deliberately contains **zero raised ordinary-canopy
meshes, zero canopy triangles, and zero added canopy-geometry draws**. Inspected
QA showed that the deterministic 29-component / 1,960-triangle raised candidate
read as block/pyramid patches and cliff-like terrain silhouettes. It is retained
only as inactive rejected-design evidence and is excluded from product
dependency closure.

Instead, 63,308 eligible non-hero pixels are assigned to height/density/normal/
occlusion response in the existing terrain draw, while 144 eligible pixels stay
with the separately authored 0–250 m hero owner. All 63,452 eligible source
pixels are assigned exactly once. Building, airport, road, water, hero, and two
signature-pinned false-positive exclusions constrain the map. This removes the
misaligned raised-ground defect at no canopy draw cost, but it does not provide
convincing volumetric groves. Near hero trees remain visibly low-detail props,
and ordinary canopy becomes subtle or flat at oblique flight views.

## Reflection and cockpit presentation

The candidate serializes one static imagery-aware global ground/sky cubemap,
one bounded static airport-local reflection probe, and the aircraft probe anchor
at the center of gravity. The probe has a custom texture, a zero culling mask,
and no runtime render update; there is no reflection/refraction camera or SSR.
Dedicated window and instrument-glass materials add restrained static cubemap
response. The duplicate exterior glass renderer is disabled.

The 1024², no-cascade, 14 m cockpit-shadow profile remains disabled by default
because no exact-APK physical Quest timing or worn flicker/crawl acceptance was
collected. Full-resolution images show improved grounding in some aircraft
surfaces, but underside/strut reflection direction, cockpit material realism,
and static/blank instrument presentation remain unresolved. Instrument function
was explicitly out of scope.

## Static budget delta

These values are Editor planning evidence. They do not establish physical Quest
frame timing, thermals, memory bandwidth, comfort, or worn quality.

| Metric | Accepted baseline | Candidate | Delta | Candidate ceiling |
| --- | ---: | ---: | ---: | ---: |
| Maximum selected draws | 55 | 53 | -2 | 75 |
| Maximum selected triangles | 534,976 | 601,406 | +66,430 | 650,000 |
| Serialized scene triangles | 671,940 | 751,459 | +79,519 | recorded separately |
| Maximum visible materials | 24 | 26 | +2 | 28 |
| Estimated texture bytes | 185,361,306 | 211,849,528 | +26,488,222 | 220,200,960 |
| Transparent material slots | 2 | 3 | +1 | 3 |
| Active renderers | 94 | 93 | -1 | recorded separately |
| Shaders / keyword combinations | 11 / 17 | 13 / 18 | +2 / +1 | recorded separately |
| MSAA | 4x | 4x | 0 | 4x |

The candidate passes the scoped development ceilings, but texture memory and
transparent slots are close to their limits. No Quest hardware allocation or
timing result is attributed to this branch.

## Visual QA and simulator

`run_production_visual_qa.ps1` captured 81/81 nonblank, structurally valid
views and classified the result
`PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`. Every full-resolution image was
inspected across the final `visual_qa_r7` set, along with the affected final-
validation-r3 seam/semantic frames and both full-resolution live-stereo eyes.
The candidate fixes mirrored semantic geometry, raised-ground canopy artifacts,
duplicate semantic ownership, and obvious building scale-pop architecture. The
canopy false-color diagnostic confirms the terrain response is predominantly
aligned to photographed canopy rather than open fields or building roofs.

| Lead category | Accepted baseline | Candidate | Milestone target | Result |
| --- | ---: | ---: | ---: | --- |
| Near ground | 5.5 | 7.0 | 7.5 | below target |
| Pattern-altitude ground | 6.5 | 7.5 | 8.0 | below target |
| Building spatial alignment | 5.5 | 7.5 | 7.5 | meets spatial target |
| Roof integration | not separately scored | 6.5 | 8.0 | below target |
| Facade/material appearance | 4.5 | 5.5 | 7.0 | below target |
| Near vegetation | 4.0 | 4.5 | 7.0 minimum | below target |
| Aerial canopy | 4.0 | 5.5 | 8.0 | below target |
| Cockpit glass | 4.0 | 6.5 | 7.5 | below target |
| Aircraft reflections | 5.0 | 6.5 | 7.5 | below target |
| Cockpit shadow candidate | 3.5 | 5.0, disabled by default | 7.0 if enabled | not accepted |
| Overall aerial scenery | 5.5 | **6.8** | **8.0** | **FAIL merge art gate** |

The machine pass is not an art-quality pass. Context buildings remain simple,
facade/roof lighting remains generic, near trees remain prop-like, the map-only
canopy response lacks convincing volume, and low-oblique imagery remains soft.
Runway and water presentation remain the frozen baseline. Therefore the
requested high lead-review targets and original 8/10 merge gate were not met.
The semantically correct map-only canopy is preferable to false raised relief,
but its weak volume is still a visible product limitation.

### Worn Quest 3 smoke

The exact 134,254,889-byte APK with SHA-256
`37ae3ce45f6acde21d181d9708dfb884b22a3599f17321628ca81c7fe5310d11`
was installed and launched on the connected Quest 3. Android reported the Unity
activity active with PID `6254`; the captured launch log contained no matching
crash, ANR, OOM, or Unity-exception signature.

Alex reported:

- buildings and ground semantic features were aligned;
- buildings were grounded and mostly looked great;
- canopy shading aligned with photographed vegetation but still lacked raised,
  rounded volume;
- left/right ground sharpness was consistent;
- no noticed stereo instability or discomfort; and
- overall worn visual quality **8/10**.

The smoke artifacts are external at
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\quick_headset_smoke_20260720_173210`.
This short visual witness is sufficient for a development-baseline merge. It is
not a frame-time, thermal, allocation, comfort-duration, or release acceptance.

Key external evidence:

- standard contact sheet:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\photoreal_aerial_surface_v1_20260720_114230\visual_qa_r7\visual_qa_contact_sheet.png`;
- dedicated aerial-surface sheet:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\photoreal_aerial_surface_v1_20260720_114230\visual_qa_r7\photoreal_aerial_surface_contact_sheet.png`;
- semantic validator:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\photoreal_aerial_surface_v1_20260720_114230\aerial_surface_validation_map_only.json`;
- final one-command validation:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\photoreal_aerial_surface_v1_20260720_114230\final_validation_r3`.

Meta XR Simulator v201.0 completed one live Quest-3-profile 72 Hz session. HMD,
both Touch controllers, OpenXR camera ownership, plausible distinct eye poses,
nonblank/consistent eyes, authored hierarchy, CG pivot, mountains, runway, and
reference integrity pass. The aggregate remains
`BLOCKED_MISSING_RECORDED_REPLAYS` because all ten genuine `.vrs` recordings are
absent. Simulator evidence is API/stereo/tracking/composition evidence only.

## Validation and build

| Gate | Result |
| --- | --- |
| Aerial-surface Python unit suite | PASS, 40/40 |
| Semantic asset validator | PASS |
| Product integrity | PASS |
| Product dependency closure | PASS |
| EditMode | PASS, 114/114 |
| PlayMode | PASS, 9/9 |
| Production Visual QA | PASS machine integrity, 81/81; machine lead art review 6.8/10 |
| Static Quest planning budget | PASS within the scoped candidate ceilings |
| Meta XR live stereo | PASS |
| Meta XR aggregate replay | BLOCKED, ten missing genuine recordings |
| Android release-style build | PASS |
| Worn Quest visual smoke | PASS development baseline, 8/10; canopy-volume defect retained |
| Physical Quest timing acceptance | NOT RUN for this APK |

The Android build uses scene `Assets/Scenes/ProductionVerticalSlice.unity` and
application ID `com.alexoviedo.t2.questflight`. The uncommitted build output is
`QuestFlight/Builds/Android/QuestFlight-production.apk`, 134,254,889 bytes,
SHA-256
`37ae3ce45f6acde21d181d9708dfb884b22a3599f17321628ca81c7fe5310d11`.

## Delivery decision and next bounded milestone

Merge and tag the exact feature result as an accepted standalone Quest
development baseline. The worn 8/10 witness resolves the earlier machine-only
art uncertainty while preserving the canopy-volume limitation explicitly. This
is not a claim of literal photorealism or release readiness.

The next bounded milestone should be **rounded, flight-scale canopy volume**:
retain the exact AEQD/UTM transform, DTM terrain, OSM footprint batches,
ownership masks, City imagery pyramid, and aligned terrain-map canopy; add only
a screen-size-bounded rounded grove representation for meaningful near/mid
components while holding current static ceilings. Do not reintroduce raised
terrain patches, per-tree map-wide scattering, generic OBJ importing, or another
imagery/lidar ingestion pass.
