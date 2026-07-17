# Terrain source and simplification report

Status: integrated and machine-validated; visual acceptance fails and Quest timing was not run.

## Source

- Dataset: USGS 3DEP Bare Earth DEM Dynamic Service.
- Endpoint: `https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer`.
- Source resolution: 1 m.
- Near source extent: 4 km × 4 km, 4001 × 4001 F32 samples.
- Raw bytes / SHA-256: 67,117,862 / `93b32e8ef2fef464ff4ee49702d235b30eaff7d347f22b8c4add8848cd7e9d82`.
- Terms: United States government work; public domain. Courtesy credit: USGS, The National Map, 3D Elevation Program.
- Raw TIFF and service responses remain outside Git under the milestone artifact root.

## Runtime derivatives

| Layer | Extent | Grid | Spacing | Approximate triangles |
| --- | --- | ---: | ---: | ---: |
| Airport | 1.6 km × 0.6 km | 321 × 121 | 5 m | 76,800 |
| Near | 4 km × 4 km with airport cutout | 161 × 161 | 25 m | 51,200 before cutout |
| Mid retained | 12 km × 12 km | 81 × 81 | 150 m | unchanged legacy topology |
| Far retained | 24 km × 24 km | 61 × 61 | 400 m | unchanged legacy topology |

The V2 airport edge is deterministically sealed to the 25 m near grid over a 40 m band. The 5 m and 25 m grids divide exactly, avoiding an independent transition strip or runtime morph. A 1024² high-frequency normal derivative is generated from the 1 m source; its committed PNG is 898,012 bytes with SHA-256 `4ba7833e6f27f8ee3abceb79caf502e53f09672525038eedea7d89df6d764cc8` and is imported as a mipmapped Android normal texture.

The runtime terrain source estimate is 128,000 triangles for the new airport/near layers. Three deterministic pipeline tests cover ENU orientation, runway-grade blending, and the sealed inner/outer boundary.

The endpoint-aligned runtime grids were reconstructed at 1 m and compared with the source target after centimeter quantization:

| Region | Average absolute error | Maximum absolute error |
| --- | ---: | ---: |
| Airport 5 m grid, including authored runway grade | 0.0407 m | 3.7056 m |
| Near 25 m grid outside the airport patch | 0.2117 m | 14.1289 m |
| Shared airport/near boundary | — | 0.0080 m |

The large isolated maxima mean the current regular-grid reduction is not a proven error-bounded adaptive simplification. The average and seam results are useful, but the maximum outliers remain a machine gate/visual-review concern and cannot be hidden by the normal map.

## Limitations and outstanding evidence

- This regular, zone-weighted runtime grid is measured but does not yet satisfy an explicit maximum-error acceptance threshold. An adaptive refinement decision must be based on inspected low-altitude views and the integrated triangle budget.
- The final authored seam and product integrity tests pass; 24/24 Visual QA captures are nonblank and within static budget.
- Full-resolution review still finds faceting, image stretch, and visible near/mid resolution transitions. Terrain geometry scores 4.5/10 and ground integration 5/10.
- Simulator stereo and physical Quest timing were not run because the lead visual gate did not reach 7/10.
- No camera-relative scaling, terrain morphing, runtime LOD generation, or duplicate terrain authority is used.
