# Production Environment Data

The authored product scene uses a bounded KBDU-inspired environment. It contains optimized derivatives only; raw network responses and large source downloads remain outside Git.

## Coordinate Contract

- Origin: FAA KBDU airport reference point, 40.03936527 N, 105.22608958 W, 5288 ft / 1611.7824 m MSL
- Frame: local east-north-up
- Unity mapping: +X east, +Y up, +Z north
- Label: KBDU-inspired / not for navigation

## Retained Sources

- FAA / USDOT BTS: runway 08/26 endpoints, dimensions, heading, grade, surface, lighting, and airport identity; United States government work.
- USGS 3DEP: elevation samples used to bake terrain and runway blend; public-domain United States government data.
- OpenStreetMap: clipped and simplified airport/context geometry; ODbL 1.0, with required attribution © OpenStreetMap contributors.
- USGS/USDA NAIP Plus: bounded 2019 natural-color public-domain derivatives supply aligned near 4 km, mid 12 km, and far 24 km macro layers. Raw exports and processing intermediates remain external.
- Poly Haven: the `Kloppenheim 03 (Pure Sky)` HDRI supplies the photographic sky and static environment-reflection source under CC0 1.0. The raw 4K download remains external; the optimized 2K HDR derivative and its source manifest are committed.

Exact requests, retrieval timestamps, raw and derivative hashes, licenses, attribution, budgets, and frozen toolchain hashes are in `tools/kbdu_environment/kbdu_source_manifest.json`.

The current imagery requests, raster IDs, corrected service extents, processing settings, output hashes, and Android import budget are in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`. The batched OSM context license boundary and per-output hashes are in `QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_ContextAssetsManifest.json`; the KOG_THORNS/CC BY 4.0 parked-scenery attribution and change notice are in the adjacent `KBDU_ParkedAircraftSourceManifest.json`. Sky lineage is in `QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json`; water lineage is in `tools/kbdu_reservoir_water/asset_manifest.json`. The frozen source manifest's earlier NAIP availability-only record remains historical and was not rewritten.

## Product Geometry

- Near zone: fixed 4 km USGS-derived terrain plus airport context.
- Mid zone: fixed 12 km lower-detail terrain and selected combined context.
- Far zone: one immutable 24 km USGS-derived mesh sampled on a 100 m render grid, with baked normals and sealed inner/outer skirts. It has no `LODGroup`, crossfade, billboard, camera scaling, or runtime regeneration. The 100 m render grid is a disclosed bilinear resampling of the frozen 400 m USGS grid and does not add source elevation detail.
- Runway: one FAA endpoint-derived grade, one offline terrain blend, one pavement mesh shared by rendering and collision, and one controlled-depth marking mesh.
- Water: Boulder Reservoir only, represented by a triangulated OSM shoreline, an opaque ZWrite material, one selected-sky reflection cubemap, two project-authored normal maps, one artistic shore/depth/roughness mask, and a separate bank interface. The mask is not surveyed bathymetry. Minor hydrography is excluded.
- Macro ground: one 4096×4096 near NAIP atlas, one color-matched 4096×4096 mid atlas, and one color-matched 2048×2048 far atlas. One shared material maps them from local ENU coordinates and blends them over 1,650–2,000 m and 5,000–6,000 m radial overlap bands. CC0 microdetail fades before the far range.
- Airport art: three existing project-authored hangar variants, one fuel/service structure, fencing/signage, and ramp props with shared materials and non-crossfaded LODs.
- Surrounding buildings: 2,500 simplified extrusions derived from bounded OSM building footprints, offline-combined into 23 building LODGroups. These meshes are OSM Produced Works and retain OSM attribution; tagged heights and source completeness vary.
- Vegetation: 246 deterministic deciduous tree placements total, offline-combined into eight vegetation LODGroups with one shared material. Twenty-four placements carry sparse shrub companions. Six of the 246 tree placements are project-authored airport-perimeter cues; the remaining placements follow bounded mapped road/water context. These are visual cues, not a botanical inventory.
- Parked aircraft: three static, exterior-only scale cues adapted from the attributed CC BY 4.0 KOG_THORNS source. Their two LODs are offline-combined by level into one non-crossfaded `LODGroup` with one shared opaque material; they are not flyable, interactive, or part of the active aircraft hierarchy.
- Sky: one optimized 2048×1024 CC0 photographic HDR derivative imported as a 1024-face Android ASTC HDR cubemap and reused for the skybox, ambient environment, static custom reflection, and reservoir reflection source.

The serialized product environment is `QuestFlight/Assets/Production/Environment/ProductionEnvironmentRoot.prefab`. Runtime code validates and reports this hierarchy; it does not rebuild it.

## Compact Derivatives

- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json`
- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json`
- fixed meshes, materials, shaders, and optimized textures under `QuestFlight/Assets/Production/Environment`

The pinned source dataset contains 30,304 elevation samples, 54,768 source-grid terrain triangles, 5,522 bounded OSM features, and 31,533 quantized OSM points. The product validator checks schema, provenance, geometry, licensing, and static budgets.

## Limitations

- The three parked aircraft are simplified scale cues, not surveyed/current ramp occupancy. Their source attribution and change notice are mandatory; CC BY covers model copyright, not manufacturer trademarks or endorsement.
- The context materials serialize Unity's instancing-variant flag, but the current custom building and vegetation shaders do not compile GPU-instancing variants. The current optimization is offline mesh batching plus LODGroups; this documentation makes no GPU-instancing claim.
- OSM footprint coverage, tags, heights, and roof forms are incomplete. The simplified extrusions are scale cues, not surveyed structures.
- The scene remains bounded visual scenery. It is not surveyed airport geometry, an aeronautical database, current imagery, navigation data, production-photorealism evidence, or training-credit evidence.
- Simulator captures do not establish standalone performance. Headset stability, frame timing, and thermal behavior require the separate physical Quest gate.
