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
- USGS/USDA NAIP Plus: coverage availability was checked, but no imagery pixels are committed.

Exact requests, retrieval timestamps, raw and derivative hashes, licenses, attribution, budgets, and frozen toolchain hashes are in `tools/kbdu_environment/kbdu_source_manifest.json`.

## Product Geometry

- Near zone: fixed 4 km USGS-derived terrain plus airport context.
- Mid zone: fixed 12 km lower-detail terrain and selected combined context.
- Far zone: one immutable 24 km USGS-derived mesh with no `LODGroup`, crossfade, billboard, camera scaling, or runtime regeneration.
- Runway: one FAA endpoint-derived grade, one offline terrain blend, one pavement mesh shared by rendering and collision, and one controlled-depth marking mesh.
- Water: Boulder Reservoir only, represented by a triangulated OSM shoreline, an opaque ZWrite material, and a separate bank interface. Minor hydrography is excluded.
- Macro ground: one unique 1024×1024 deterministic land-cover derivative plus distance-faded CC0 microdetail.

The serialized product environment is `QuestFlight/Assets/Production/Environment/ProductionEnvironmentRoot.prefab`. Runtime code validates and reports this hierarchy; it does not rebuild it.

## Compact Derivatives

- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json`
- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json`
- fixed meshes, materials, shaders, and textures under `QuestFlight/Assets/Production/Environment`

The pinned source dataset contains 30,304 elevation samples, 54,768 source-grid terrain triangles, 5,522 bounded OSM features, and 31,533 quantized OSM points. The product validator checks schema, provenance, geometry, licensing, and static budgets.

## Limitations

The scene is simplified and visually stylized. It is not surveyed airport geometry, an aeronautical database, an orthophoto product, navigation data, photorealism evidence, or training-credit evidence. Headset stability and performance require a separate worn acceptance gate.
