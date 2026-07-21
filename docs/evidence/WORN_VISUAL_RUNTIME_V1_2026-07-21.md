# Worn Visual Runtime V1 evidence — 2026-07-21

## Purpose and source

`fix/worn-visual-runtime-v1` is a bounded response to the last worn review. It
addresses four reported defects together: sparse/flat and semantically
misaligned canopy, implausible procedural facades, cabin glass absent from the
seated view, and missing cockpit interior depth. The starting commit is
`f26cb45c743a2d019d8e57a262f3550c1c8a63fe`. Runway, water, terrain geometry,
mountains, sky, aircraft geometry, instruments, physics, input, and product
scope were intentionally left unchanged.

## Implemented representation

### Canopy

- 946 deterministic groves are derived offline from the aligned canopy,
  orthophoto, DTM, and semantic ownership masks.
- Building, road, runway, and water ownership is excluded before geometry is
  compiled.
- Represented source coverage inside the 0–1,500 m authoring zone is 82.576% of
  eligible canopy pixels.
- Median authored height is 16.0875 m versus a 10.75 m median source height
  (149.65%), a bounded visual calibration informed by photographed shadow
  length rather than a survey claim.
- Each cell uses three asymmetric rounded crown lobes and one grounded trunk.
  The result is 52,976 triangles in four fixed native batches, four draws, one
  opaque material, and no runtime callback, billboard, LOD crossfade, or
  per-tree GameObject.
- The broad terrain canopy map remains active beyond the raised-geometry zone.

### Buildings

- All 9,692 accepted OSM footprint shells remain represented in 16 fixed
  batches / 128,092 triangles.
- Per-wall vertices and physical metre-scale UVs prevent world/cell-scale
  material distortion.
- Stable semantic classification selects coherent residential, commercial,
  industrial, or civic palettes and module/floor scales.
- Windows keep plausible corner margins, doors occur only on a stable entrance
  wall, and both are opaque shader features with bounded static cubemap glint.
- No new renderer, draw, texture, material, transparent slot, or runtime update
  was introduced.

### Cockpit glass and depth

- The authoritative full cabin-glass mesh uses the one-pass
  `ProductionCockpitWindowReflection` material and is enabled for the seated
  interior view.
- A redundant imported transparent instrument shell is disabled; the
  authoritative instrument cover remains enabled.
- Two opaque cockpit casters use a dedicated layer-30 hard-shadow sun at 512
  resolution. The main sun excludes that layer, the bounded sun renders only
  that layer, glass casts no shadow, cascades are disabled, and nothing is
  constructed at runtime.
- Static AO/roughness tuning supplies additional interior depth without SSAO,
  a realtime reflection probe, or another camera.

## Data rights and generated assets

No external asset was downloaded for this milestone. Geometry, masks,
materials, and manifests are project-authored derivatives of the already
recorded public-domain USGS/USDA NAIP and terrain/canopy inputs plus existing
OpenStreetMap-derived footprint data. Raw source imagery and caches remain
outside Git. Each committed generated binary is paired with its Unity `.meta`
file and covered by the existing source manifests or the new deterministic
canopy/building manifest hashes.

## Validation results

| Gate | Result |
|---|---|
| Unity EditMode | PASS 115/115 |
| Unity PlayMode | PASS 9/9 |
| Product validation | PASS |
| Production Visual QA | PASS 81/81; human headset still required |
| Live Meta XR stereo | PASS for nonblank, consistent Quest-3-profile left/right eyes |
| Recorded simulator replay aggregate | BLOCKED_MISSING_RECORDED_REPLAYS (10 genuine `.vrs` files absent) |
| Android release-style build | PASS |

The reviewed Visual QA contact sheet is stored outside Git at
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\worn_visual_rework_20260721_105016\visual_qa_release_candidate\visual_qa_contact_sheet.png`.
The live stereo contact sheet is at
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\worn_visual_rework_20260721_105016\simulator_final\meta_xr_simulator\visual_gate\simulator_stereo_contact_sheet.png`.

## Static Quest planning budget

| Metric | Candidate | Ceiling |
|---|---:|---:|
| Selected draws | 55 | 75 |
| Selected triangles | 624,890 | 650,000 |
| Serialized mesh triangles | 793,677 | 800,000 |
| Visible materials | 25 | 25 |
| Estimated texture bytes | 210,601,912 (200.85 MiB) | 210 MiB |
| Transparent slots | 3 | 3 |
| Shaders / variants | 12 / 17 | current bounded contract |
| MSAA | 4x | 4x |

These Editor-derived planning values do not establish Quest CPU/GPU timing,
thermals, stale/tear behavior, comfort, or steady-state physical acceptance.

## Immutable APK candidate

- Path (ignored build output):
  `QuestFlight/Builds/Android/QuestFlight-production.apk`
- Size: 134,060,510 bytes
- SHA-256:
  `eb9499403126e4820ca0e42130ee1bddd84ff35c5b6909c9eb7184062ec431e8`

No prior worn score is attributed to this APK. The required final worn review
must inspect canopy height/density/grounding, close facade scale and semantic
coherence, cabin-glass visibility/reflection, and cockpit shadow stability.

## Candid limitations and merge gate

The candidate is materially more coherent at flight-simulator viewing ranges,
but it is not literal photorealism. Grove crowns are deliberately low-cost
aggregate shapes and can look stylized/faceted close up. Context buildings are
still inferred shells, not reference-authored assets, and their procedural
openings are not surveyed. The cabin reflection is static and bounded.
Instruments remain mostly static or blank because functional data binding was
out of scope.

The branch is machine-ready for its exact-APK worn smoke. It must not merge to
`main` until that smoke confirms that the four user-visible corrections are
actually present and free of a critical stereo, flicker, comfort, or scale
regression.
