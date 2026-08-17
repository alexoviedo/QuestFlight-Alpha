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
  `b56c7a0bea6e986d28e730f30fd978f7e7eb1119227a0cd3221e6b559f30af00`.
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

## Deterministic dimension correction

The previous full-map fallback inferred every ordinary height from crown width
and a stable hash, then clamped crowns to 15.5 m and heights to 23.5 m. In the
accepted database, 732,915 crowns (81.28%) and 328,666 heights (36.45%) landed
exactly on those caps. That was the measured source of the uniform skyline.

`tools/full_map_vegetation/rebuild_tree_dimensions.py` now preserves all
901,744 accepted positions, terrain bases, page records, colors, rotations, and
seeds byte-for-byte. It partitions physical size from deterministic imagery
crown/tonal evidence and blends the aligned compact 3DEP canopy height only where
orthophoto vegetation ownership and lidar support agree. The 1,047 accepted
airport-close records remain verbatim. Unsupported full-map crowns use context
allometry with no hash-derived height.

Before/after dimension statistics:

| Metric | Before | After |
| --- | ---: | ---: |
| Height p05 / median / p95 | 17.79 / 22.47 / 23.50 m | 8.49 / 12.48 / 16.34 m |
| Crown p05 / median / p95 | 11.92 / 15.50 / 15.50 m | 7.71 / 9.58 / 10.89 m |
| Unique height values (cm) | 1,864 | 2,078 |
| Unique crown values (cm) | 382 | 918 |
| Exact 23.5 m height records | 328,666 | 0 |
| Exact 15.5 m crown records | 732,915 | 0 |

The final height sources are 1,047 preserved airport-close records, 20,060
orthophoto-owned/lidar-supported blends, 880,310 deterministic orthophoto/context
allometry records, and 327 shrub records. This changes pixel coverage and must
still receive physical Quest visual acceptance, but it does not change the static
draw, triangle, material, texture, renderer, or database-size budgets.

## Downloaded reference disposition

The five downloaded Sketchfab archives were inspected outside Git. Candidate
source meshes ranged from 19,921 to 168,967 triangles per asset; the bush alone
was 135,433 triangles. Direct product use would violate the intended density and
Quest budget, so they are silhouette references only. Exact hashes and decisions
are recorded in `FullMapVegetationV2/SOURCE_MANIFEST.json`.

## Validation

- Serialized authoring: PASS; 61 draws / 608,774 triangles / 12 materials.
- EditMode: 331/331 PASS, including the seven full-map vegetation contracts and
  the uninitialized-residency fail-closed regression test.
- Product PlayMode: 10/10 PASS after retaining the existing seven-draw
  vegetation contract.
- Android release-style build: PASS; SHA-256
  `b0831331c50d8484372171d4e2822326eda3cc066ce6298a37de2c9ecea7d9ed`.
- Current-product Visual QA rendered 68 deterministic views. The lead inspected
  the full-resolution vegetation/terrain frames; low shrubs, ordinary crowns,
  and mature corridor trees no longer share one skyline height. The aggregate
  runner remains blocked by a pre-existing missing elevated semantic-surface
  candidate and therefore omits its 32 legacy V4 A/B views. Two unrelated
  static views also retain their existing marginal budget failures (77 versus
  75 draws, and 653,377 versus 650,000 triangles).
- Full product validation remains blocked by the same broader source-closure
  audit (two excluded and two unclassified legacy dependencies) plus the
  aggregate Visual QA blocker above; the dimension change introduces neither.
- Physical Quest LOD-transition review: the extended-coverage candidate was
  reported improved; the exact dimension-corrected APK is installed and its
  visual acceptance remains pending.
- Offline height-distribution and immutable-field gate: PASS; report retained in
  the external `tree_height_fidelity_v1_*` artifact root.

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
