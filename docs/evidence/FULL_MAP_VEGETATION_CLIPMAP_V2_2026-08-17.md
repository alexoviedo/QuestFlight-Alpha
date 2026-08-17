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
- Maximum selected tier counts: 24 / 240 / 780.
- Maximum tree draws: three per eye; one shared material.
- Maximum selected/crossfade tree triangles: 42,504.
- No per-tree GameObjects, per-tree `Update`, ordinary `BillboardRenderer`,
  runtime imagery classification, or runtime hierarchy construction.

LOD is camera/aircraft-centered and selected by projected screen size rather
than airport distance. Candidate pages are distance- and frustum-culled. LOD
changes use persistent screen-size hysteresis, minimum residency, bounded
alpha-to-coverage transitions, and a center-eye world-upright eight-view far
impostor. A final 180 m fade hands geometry back to the orthophoto.

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

Validation results are recorded in `docs/VALIDATION.md`. Machine captures and
simulator evidence do not constitute physical Quest frame, thermal, comfort, or
final photorealism acceptance.

## Known limitations

- Full-map species, age, height, and season are inferred. Surveyed height is not
  available across the complete 24 km square.
- Orthophoto color includes photographed illumination; the compiler samples the
  brighter canopy distribution before current-scene lighting to reduce, but not
  eliminate, double-lighting differences.
- The farthest canopy remains the orthophoto. This is intentional: individual
  geometry below useful projected size would add cost without reliable detail.
