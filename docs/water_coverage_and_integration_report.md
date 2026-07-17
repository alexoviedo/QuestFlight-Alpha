# Water coverage and integration report

Status: deterministic source coverage and Unity integration complete; visual acceptance fails.

The V2 pipeline audits all 800 water features in the pinned 12 km OpenStreetMap context (ODbL-1.0; `© OpenStreetMap contributors`). It represents 277 features meeting the requested bounded thresholds and records an exclusion reason for the remaining 523.

| Zone / geometry | Represented features |
| --- | ---: |
| 0–2 km polygons ≥500 m² | 24 |
| 0–2 km waterways ≥3 m interpreted width | 76 |
| 2–6 km polygons ≥5,000 m² | 54 |
| 2–6 km named major canal/river segments ≥5 m | 123 |

`KBDU_WaterFeatureManifestV2.json` records stable/source IDs, ENU geometry, interpreted width, area, zone, license class, and terrain-integration mode. The source pipeline is deterministic and has unit coverage for area orientation, fail-closed thresholds, source accounting, and byte-stable regeneration.

The Unity authoring path preserves the existing higher-quality Boulder Reservoir surface, then batches all other eligible polygons and waterways into one opaque shared-material surface and one terrain-sampled bank mesh. It uses the authoritative terrain sampler and a 0.13 m depth separation, avoiding coplanar overlap, planar reflections, SSR, refraction cameras, tessellation, and per-feature scripts. The production placement manifest receives every represented water feature.

The final placement manifest contains all 277 represented features, and static draw/material budgets pass. The representation remains deliberately conservative: non-reservoir surfaces conform to sampled terrain rather than claiming surveyed water levels or bathymetry. Full-resolution inspection rejects the result: secondary water and bank meshes form conspicuous dark segmented ribbons, while Boulder Reservoir remains flat and hard-edged. Water scores 3/10. Simulator stereo and Quest timing were not run.
