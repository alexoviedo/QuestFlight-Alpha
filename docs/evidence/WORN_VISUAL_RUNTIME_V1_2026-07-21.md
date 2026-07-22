# Worn Visual Runtime V1 evidence — 2026-07-21

## Scope and evidence boundary

`fix/worn-visual-runtime-v1` starts from standalone Quest baseline
`f26cb45c743a2d019d8e57a262f3550c1c8a63fe`. It addresses four worn-review
defects: close canopy quality and grounding, semantically incoherent context
facades, cabin glass absent from the seated view, and missing cockpit lighting
depth. Runway, water, terrain geometry, mountains, sky, instruments, physics,
input, and standalone Android/OpenXR ownership remain unchanged.

The preceding worn smoke scored 8/10 but used a different APK. It accepted
scale and placement while reporting molded-looking crowns, sparse/thin trunks,
weak branch/leaf perforation, and poor close-grove realism. That review guided
this iteration; no worn score is transferred to the final APK below.

## Final canopy representation

- The offline compiler clips public-domain canopy evidence against building,
  road, runway, airport, and water ownership, then grounds it to the DTM.
- All 143 semantic groves inside 650 m use 696 detailed trees. Each tree has a
  tapered triangular-prism trunk, three branches, and twelve fixed-world
  foliage cards arranged in vertical, diagonal, and horizontal crown layers.
- The sixteen-cell RGBA atlas contains perforated Front Range deciduous
  leaf-and-twig clusters. Local crown color/luminance is registered to the
  exact orthophoto canopy sample instead of applying a single plastic tint.
- Cards are static multiview planes, not camera-facing or per-eye billboards.
  The shared shader uses clipping and alpha-to-coverage with required 4x MSAA.
- Medium-distance cells retain low-cost rounded aggregate shells. Remaining
  source coverage uses the existing terrain canopy map, with no per-tree
  GameObject, `Update`, crossfade, dither, or runtime construction.
- Final canopy inventory: 886 compiled semantic groves, 58,018 triangles,
  67,266 vertices, four native batches/draws, one material, 59.364% raised
  geometry coverage in 0–1,500 m, and 100% geometry-or-terrain assignment.

The committed atlas is
`QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/Textures/ProductionNearFoliageAtlas_RGBA.png`,
SHA-256 `aae5ad67afc5e63dc0ce762bcae1bac819ebd7403dcd5d922af2fc1e474c671c`.
Its source/provenance record is the adjacent
`ProductionNearFoliageAtlas_SourceManifest.json`. The artifact-only chroma
source has SHA-256
`c433d450fc859a71fb3be9b19bb03ffcbf4a26adb8950ad984cfd18038e67d4d`;
it is not committed. No third-party reference image was supplied.

## Buildings and cockpit

All 9,692 accepted OSM footprint shells remain represented in sixteen fixed
batches / 128,092 triangles. Per-wall metre-scale coordinates and stable OSM
semantic families drive coherent residential, commercial, industrial, and
civic palettes, floor/module scale, corner margins, entrance-only doors, and
opaque cubemap-backed window glint. These are inferred context shells, not
surveyed hero architecture.

The authoritative cabin-glass mesh uses a bounded single-pass ground/sky
reflection response visible from the seated interior. One redundant imported
instrument cover is disabled while the authoritative cover remains. Cockpit
depth is bound to the existing opaque cabin geometry. The stable single-pass
interior shader preserves the imported base/normal/MRAO/emission inputs, bounds
legacy near-zero AO texels, and adds coherent directional sun/sky response plus
a restrained warm cabin bounce. The first machine candidate used a projected
two-triangle overlay; full-resolution review exposed visible triangle seams
across the sky and panel, so that renderer, mesh, material, and shader were
rejected and removed before delivery. The final product has no cockpit overlay,
realtime cockpit shadow map, additional transparent pass, reflection camera,
shadow camera, or cascade.

## Validation

| Gate | Result |
|---|---|
| Aerial-surface Python tests | PASS 22/22 |
| Deterministic aerial-surface validator | PASS |
| Unity EditMode | PASS 116/116 |
| Unity PlayMode | PASS 9/9 |
| Full `validate_product.ps1` | PASS |
| Product dependency closure | PASS; 266 approved, 0 blockers |
| Production Visual QA | PASS 81/81; `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED` |
| Full-resolution lead inspection | PASS for final canopy/facade/cockpit and regression frames |
| Meta XR Simulator 201.0 live stereo | PASS; Quest 3 profile, HMD, both Touch, consistent nonblank eyes |
| Recorded simulator aggregate | `BLOCKED_MISSING_RECORDED_REPLAYS` for ten genuine `.vrs` sessions |
| Static Quest planning | PASS; physical measurement still required |
| Android release-style build | PASS |

Primary external evidence root:
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\worn_visual_rework_20260721_105016\near_foliage`.
The final full-resolution contact sheet is under
`product_validation_cockpit_stable_final\visual_qa\visual_qa_contact_sheet.png`;
the dedicated surface sheet is
`product_validation_cockpit_stable_final\visual_qa\photoreal_aerial_surface_contact_sheet.png`.
Live stereo is under
`simulator_cockpit_stable_final\meta_xr_simulator\visual_gate\simulator_stereo_contact_sheet.png`.

## Static Quest planning budget

| Metric | Final | Ceiling | Result |
|---|---:|---:|---|
| Maximum selected draws | 55 | 75 | PASS |
| Active renderers | 95 | bounded current contract | PASS |
| Maximum selected triangles | 632,320 | 650,000 | PASS |
| Serialized scene triangles | 798,719 | 800,000 | PASS |
| Maximum visible / active materials | 26 / 27 | 28 | PASS |
| Estimated texture bytes | 211,849,528 (202.04 MiB) | 220,200,960 (210 MiB) | PASS |
| Transparent material slots | 3 | 3 | PASS |
| Shaders / keyword combinations | 12 / 17 | bounded current contract | PASS |
| MSAA | 4x | 4x | PASS |

These are Editor planning estimates. They do not establish Quest GPU/CPU
timing, thermals, stale/tear rate, allocation behavior, comfort, or worn image
quality.

## APK identity and decision

The final immutable APK was rebuilt after the reflection/source manifests were
refreshed:

- ignored path: `QuestFlight/Builds/Android/QuestFlight-production.apk`;
- size: 135,905,162 bytes;
- SHA-256: `36216609302a851d85db3255ea0f99dfadfaacb1cdc9324826416eb64d84e5d3`;
- build evidence: `near_foliage\product_validation_cockpit_stable_final\build`
  under the artifact root.

The APK and build directory are not committed.

The branch is a machine-validated merge candidate, not public-release or
literal-photorealism acceptance. Near crown detail is materially improved in
the inspected captures, while medium aggregate groves remain deliberately
simplified. Context buildings remain inferred shells. Cabin reflection and the
geometry-bound cockpit depth still require the exact-APK worn smoke. Long-duration
Quest performance remains unmeasured. The branch must remain unmerged until
that worn review confirms the visible canopy, glass, facade, and cockpit result.
