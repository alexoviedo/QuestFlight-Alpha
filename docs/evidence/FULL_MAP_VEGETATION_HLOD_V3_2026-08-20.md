# Full-map Vegetation HLOD V3 — 2026-08-20

## Scope and source

- Repository: `alexoviedo/T2-QuestFlight`
- Source branch/SHA: `feature/full-map-vegetation-clipmap-v2` /
  `62139668a7571dec06027a9efe6647b6a079c16d`
- Candidate branch: `feature/full-map-vegetation-hlod-v3`
- Shipping target: standalone Quest 3, Android ARM64, Vulkan, OpenXR
- Artifact root:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_vegetation_hlod_v3_20260820_124106`

This milestone changes vegetation representation only. Buildings, flight
authority, cockpit, water, runway, clouds, aircraft, and world extent remain
outside its runtime scope.

## Proven V2 defect

The accepted V2 database contains 901,744 placements in 576 one-kilometre
pages. The runtime admitted only the first 144 confidence-sorted records in
each page. That discarded most accepted coverage before any projected-size LOD
decision and reduced low vegetation to 327 represented shrub records. Sparse
groves were therefore a data-admission defect, not merely a tree-model defect.

## V3 compiled representation

The offline V3 compiler preserves every accepted V2 individual, classifies
bounded low vegetation by measured height/crown limits, and adds one bounded
aggregate for every occupied 85 m grove cell. Aggregate records lead each page;
individuals use a stable whole-page spatial order.

| Record class | Count |
| --- | ---: |
| ordinary trees | 892,037 |
| tall/narrow trees | 697 |
| shrubs | 9,010 |
| grove HLODs | 73,883 |
| total runtime records | 975,627 |

The database is 18,541,561 bytes with SHA-256
`ed139adb77ba55cc6614c2d1ba656b53540e3b9ef9b7891e2d4f05083af2eab8`.
Generation is reproducible through
`tools/full_map_vegetation/compile_vegetation_hlod_v3.py`.

## Runtime architecture

- One serialized `AirportCloseTreeLodRenderer`; no per-tree GameObjects.
- Aircraft-oriented 96-page envelope: 10 km forward, 7 km lateral, 3 km rear.
- Every retained page exposes 144 spatially complete far records.
- Nine closest pages within 2.4 km expose up to 600 individual records.
- Existing tree tiers: 344 / 122 / 2 triangles.
- Project-authored shrub tier: 24 triangles.
- Project-authored multi-lobe grove tier: 44 triangles.
- Maximum six vegetation draws, one shared material, and 81,584 selected
  vegetation triangles.
- No camera-facing ordinary billboard, LOD dither/crossfade, runtime mesh
  construction, per-tree update, or Gaussian-splat renderer.

## Far canopy and shrub evidence

All 1,296 external City 2020 high-resolution pages were analyzed offline at
0.667 m/texel. The accepted V2 crowns constrain the segmentation, and the
building-footprint exclusion removes structures. This corrected the rejected
first pass that classified approximately 68% of the map, including fields, as
vegetation. The accepted mask covers 0.302491 of the map and contains 7,288,724
canopy-supported and 91,958 shrub-supported analysis texels.

The committed 4096-square RGBA visual stores feathered coverage, shrub
fraction, and aggregate normal X/Z. It is ASTC 8x8, mipmapped, streamed, and
sampled in the existing terrain draw. Orthophoto color remains authoritative.
The PNG is 35,760,529 bytes with SHA-256
`0c12e4cd1801e134ba5dd3d4fae86b2bf9f6003cc4b497f249ab7a62ab0385fa`.

## Budget delta

| Planning metric | V2 | V3 | Delta |
| --- | ---: | ---: | ---: |
| vegetation draws | 7 | 6 | -1 |
| vegetation selected triangles | 50,694 | 81,584 | +30,890 |
| environment draws | 61 | 60 | -1 |
| environment triangles | 608,774 | 639,664 | +30,890 |
| environment materials | 12 | 12 | 0 |
| full-scene QA peak draws | 77 | 76 | -1 |
| full-scene QA peak triangles | 659,877 | 690,767 | +4.68% |
| full-scene QA materials | 38 | 38 | 0 |

The full-scene triangle delta remains below the approved baseline-plus-five-
percent ceiling. Static estimates are not physical Quest measurements.

## Visual inspection

- Production Visual QA: PASS, 123/123 nonblank views.
- Dedicated aerial QA: PASS, 40/40 nonblank before/after views.
- Full-resolution inspection covered airport scale, 100/300/500 m AGL,
  250/500/1,000 m vegetation distance, pattern altitude, top-down canopy, and
  the terrain canopy diagnostic.
- The reviewed result shows dense riparian/urban coverage, grounded individual
  trees, visible low vegetation, and orthophoto-preserving far handoff. The
  rejected full-field false-positive mask is not active.

Contact sheets:

- `visual_qa_final\visual_qa_contact_sheet.png`
- `visual_qa_final\photoreal_aerial_surface_contact_sheet.png`
- `aerial_qa_final\aerial_scenery_before_after_contact_sheet.png`
- `full_map_canopy_debug.png`

## Validation

- Unity EditMode: PASS, 332/332.
- Unity PlayMode: PASS, 10/10.
- Product Visual QA image analysis: PASS.
- Dedicated aerial QA: PASS, 40/40 nonblank.
- Product integrity and dependency closure: PASS in the complete validation
  pipeline before its original stale static-ceiling stop.
- Static Quest budget: PASS at 76/77 draws, 690,767/692,871 visible triangles,
  38/38 materials, 296,322,278/307,507,088 conservative texture bytes, 18/18
  inherited transparent slots, and 4x MSAA.
- Android release-style build: PASS. The installed checkpoint APK was
  1,509,522,642 bytes with SHA-256
  `164d0079640e4b1208d7a983e60beecb7791d0e6a180266a17d6d154d042b4df`.
- Meta XR Simulator timing is not claimed as Quest hardware timing.
- Worn Quest review: the vegetation was judged an improvement, with semantic
  false placements in water and athletic fields identified as the next bounded
  correction. No sustained frame-time or thermal acceptance was performed.

## Limitations

The placements remain visual inference rather than a botanical survey. Grove
HLODs are middle-distance aggregate silhouettes, not hero-tree substitutes.
No exact-APK worn quality, frame-time, thermal, stale/tear, or sustained
allocation acceptance is claimed. This milestone does not claim literal
photorealism, navigation accuracy, FAA approval, or training credit.
