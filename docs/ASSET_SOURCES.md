# Product Asset Sources

This file records only assets present in the clean product dependency closure. License terms and binary hashes are recorded in `docs/ASSET_LICENSES.md`, `THIRD_PARTY_NOTICES.md`, and `tools/kbdu_environment/kbdu_source_manifest.json`.

## Aircraft

- Asset: Cessna 172 by KOG_THORNS
- Original page: https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4
- License: Creative Commons Attribution 4.0 (CC BY 4.0)
- Product path: `QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb`
- Purpose: cockpit and exterior visual baseline
- Caveat: the license covers the model copyright, not Cessna/Textron trademarks or endorsement. Public commercial release requires a branding review.

The product does not contain a runtime-generated or substitute aircraft. The imported asset is tracked with Git LFS and its exact hash is verified by `scripts/sync_local_aircraft_asset.ps1 -VerifyOnly`.

## KBDU-Inspired Environment Data

The product contains bounded, optimized derivatives rather than raw downloads.

- FAA / USDOT BTS airport and runway services
  - use: KBDU identifiers and runway 08/26 endpoints, dimensions, heading, grade, and surface metadata
  - status: United States government work / public-domain source data
- USGS 3DEP Bare Earth DEM
  - source: `https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer`
  - use: fixed near, mid, and far terrain meshes and elevation/slope inputs
  - status: United States government data / public domain
- OpenStreetMap through Overpass
  - source: `https://overpass-api.de/api/interpreter`
  - use: clipped aeroway, building, land-cover, road, and Boulder Reservoir geometry
  - license: ODbL 1.0
  - attribution: © OpenStreetMap contributors; https://www.openstreetmap.org/copyright
- USGS/USDA NAIP Plus
  - use: availability query only
  - status: no imagery pixels or raw imagery are committed

Committed compact derivatives are:

- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json`
- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json`
- baked production meshes and the deterministic macro albedo under `QuestFlight/Assets/Production/Environment`

The exact source requests, timestamps, hashes, licenses, and geometry budgets are pinned in `tools/kbdu_environment/kbdu_source_manifest.json`. The environment is KBDU-inspired and not for navigation.

## CC0 Ground Detail

Three 1K diffuse maps from Poly Haven are used as Quest-bounded microdetail. Poly Haven publishes them under CC0 1.0: https://polyhaven.com/license.

| Runtime use | Asset | Author | Source |
| --- | --- | --- | --- |
| dry prairie | Withered Grass | Charlotte Baglioni | https://polyhaven.com/a/withered_grass |
| sparse green grass | Sparse Grass | Amal Kumar | https://polyhaven.com/a/sparse_grass |
| dry soil | Dry Ground 01 | Rob Tuytel | https://polyhaven.com/a/dry_ground_01 |

The product copies only the optimized 1024×1024 maps. Android imports use mipmaps, trilinear filtering, anisotropy, and ASTC compression. No raw archives are committed.

## Project-Authored Derivatives

- Fixed terrain, runway, marking, bank, water, and context meshes
- `ProductionKbduMacroAlbedo.png`, generated deterministically from the pinned elevation and land-cover derivatives
- Production ground, runway-marking, and stable-water shaders
- Unity scene, prefabs, materials, scripts, QA views, tests, and build tooling

The macro albedo is a synthetic land-cover derivative, not an orthophoto and not a photorealism claim. No Google Maps, Google Earth, Street View, paid, or unclear-license content is included.

## Future Asset Rule

Every committed binary must have a source, license, and hash record. Raw captures, downloads, archives, caches, and unclear-license assets remain outside Git.
