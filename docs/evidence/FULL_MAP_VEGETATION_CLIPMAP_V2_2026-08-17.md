# Full-map vegetation clipmap V2 evidence

## Scope and source

- Source branch/SHA: `feature/full-map-building-facades-v2` / `3bb7150b1d824d6fab81b86de7aa8f1f3cace628`.
- Candidate branch: `feature/full-map-vegetation-clipmap-v2`.
- Coverage: product-local X/Z `[-12000,+12000)` m, split into 576 fixed 1 km runtime pages.
- Semantic placement/color: City of Boulder 2020 orthophoto, analyzed offline at 1.333 m/pixel.
- Stem exclusion: all 57,407 authoritative full-map building footprints plus a bounded wall buffer.
- Grounding: `KBDU_ProductionTerrainSurfaceV2`.
- Airport-near continuity: all 1,047 previously worn/accepted close-tree placements are preserved verbatim.

The RGB classification is deterministic visual inference, not an individual-tree
or botanical survey. Raw orthophotos and downloaded high-poly reference models
remain external.

## Representation

- 901,744 quantized tree/shrub records across all 576 pages.
- Compact database: 17,137,784 bytes; SHA-256
  `0a24adb52cd6280f70b11a9d5e2d6d9c7cf7082713c89af1cfd1ee79b1a5b942`.
- Source candidates examined: 4,217,470.
- Runtime maximum page population: 1,600; mean nonempty population: 1,565.53.
- Existing optimized tree tiers: 344 / 122 / 2 triangles.
- Maximum selected tier counts: 24 / 240 / 4,875.
- Stable far candidate envelope: at most 96 pages × 144 deterministic samples = 13,824.
- Maximum tree draws: seven per eye; one shared material.
- Maximum selected/crossfade tree triangles: 50,694.
- No per-tree GameObjects, per-tree `Update`, ordinary `BillboardRenderer`,
  runtime imagery classification, or runtime hierarchy construction.

The near tiers retain the accepted building clipmap grid and mandatory committed
and predicted pages. Far visibility uses a larger stable aircraft-oriented
envelope: 10 km forward, 7 km to either side, and 3 km rearward, capped at 96
pages. Each page contributes the same deterministic prefix of its confidence-
ranked, spatially hashed placements; the renderer does not rerank 37,000-plus
page candidates into a changing global population every frame. A 1 km exit halo
retains pages across boundaries, and head movement does not evict aircraft-
forward coverage. Page entry never uses a culled-to-visible fade. Actual LOD
changes retain screen-size hysteresis, two-second residency, bounded alpha-to-
coverage, and a center-eye world-upright eight-view far impostor.

The authored scene estimate is 61 draws, 608,774 triangles, and 12 materials,
remaining below the 75-draw and 650,000-triangle development ceilings without a
new material, texture, transparent slot, hierarchy object, or per-tree callback.

The prior four opaque/static grove hulls are removed from the product dependency
closure. This eliminates the previously rejected boulder/blob silhouette while
recovering four static draws and their serialized triangles.

## Downloaded reference disposition

The five downloaded Sketchfab archives were inspected outside Git. Candidate
source meshes ranged from 19,921 to 168,967 triangles per asset; the bush alone
was 135,433 triangles. Direct product use would violate the intended density and
Quest budget, so they are silhouette references only. Exact hashes and decisions
are recorded in `FullMapVegetationV2/SOURCE_MANIFEST.json`.

## Validation

- Serialized authoring: PASS; 61 draws / 608,774 triangles / 12 materials.
- Focused vegetation EditMode: 6/6 PASS.
- Product PlayMode: 10/10 PASS after retaining the existing seven-draw
  vegetation contract.
- Android release-style build: PASS; SHA-256
  `49580f9fcd2b502d46a7fda8e113c718a9a104afbd4c53a75c21dd9c3b63d63a`.
- Physical Quest LOD-transition review: pending the installed-candidate worn
  check.

Machine evidence does not constitute physical Quest frame, thermal, comfort,
or final photorealism acceptance.

## Known limitations

- Full-map species, age, height, and season are inferred. Surveyed height is not
  available across the complete 24 km square.
- Orthophoto color includes photographed illumination; the compiler samples the
  brighter canopy distribution before current-scene lighting to reduce, but not
  eliminate, double-lighting differences.
- The farthest canopy remains the orthophoto. This is intentional: individual
  geometry below useful projected size would add cost without reliable detail.
