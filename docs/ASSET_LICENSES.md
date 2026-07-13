# Asset licenses and integrity record

Audit basis: frozen R&D commit `cd4131fa7732a6db2d7387921dfef33e2c206970`, tagged `product-extraction-source-2026-07-12`.

Every product binary must match an entry here or receive a new source/license review. Raw downloads are not product assets.

## Runtime visual assets

| Product asset | Source / creator | Terms | SHA-256 |
| --- | --- | --- | --- |
| `cessna172.glb` (26,962,216 bytes) | `Cessna172`, KOG_THORNS, [original Sketchfab model](https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/); attribution and change notice required | `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872` |
| `withered_grass_diff_1k.jpg` | Withered Grass, Charlotte Baglioni, [Poly Haven](https://polyhaven.com/a/withered_grass) | [CC0 1.0](https://polyhaven.com/license) | `8bebfb639ef74f651bc445526c46c845beee52a86d831848b82eb2bbbf2d98ee` |
| `sparse_grass_diff_1k.jpg` | Sparse Grass, Amal Kumar, [Poly Haven](https://polyhaven.com/a/sparse_grass) | CC0 1.0 | `ae94f2b34597b9108eefd88217f55eccaec6d6b382e858a478ee92df90e66617` |
| `dry_ground_01_diff_1k.jpg` | Dry Ground 01, Rob Tuytel, [Poly Haven](https://polyhaven.com/a/dry_ground_01) | CC0 1.0 | `75222fc97a82b635a09cf8f2891dd58bc42e49599b9fda920b2c50193fe85e9f` |
| `ProductionKbduMacroAlbedo.png` | Deterministic project generator using the pinned USGS elevation/slope and OSM land-cover/aeroway/road derivative | Project-authored pixels; underlying OSM database remains ODbL-1.0 and requires attribution | `80dcd04e23484746a25bd7b31791a613f18cf95b89665081d2bd20636794417d` |

Poly Haven's current license page states that its assets are CC0 and may be used commercially and redistributed without attribution. Attribution is retained as provenance and courtesy.

## Pinned geospatial derivatives

Snapshot: `20260710T214309Z`. Label: `KBDU-inspired / not for navigation`.

| Committed derivative | Lineage | SHA-256 |
| --- | --- | --- |
| `kbdu_terrain_rings.json` | USGS 3DEP public-domain elevation samples plus deterministic project quantization | `712fccb9bcbaf0b79f6f8bf8e7a9f59c05b022fe9e478b022d8ca2595aa767b7` |
| `kbdu_reference_context.json` | OSM ODbL-1.0 bounded derivative plus FAA/BTS public-domain runway/airport facts | `5d0d0c3c71532b5106119b8e647e303e12bdeb662960ef78543050166bb8e3e0` |
| `kbdu_source_manifest.json` | Project-authored provenance manifest containing all raw request, chunk, tool, and derivative hashes | `22ef678c6071e527e551b99f8c35e6fb554ced7a6501091b9b7dff9b6963ff1f` |

### FAA / USDOT BTS NTAD

- Sources: `NTAD_Aviation_Facilities` and `Runways_View`, derived from FAA aeronautical data.
- The current service metadata states that each dataset is a United States government work, is not protected by U.S. copyright, and is available for unrestricted public use.
- Courtesy credit: Federal Aviation Administration (FAA); USDOT Bureau of Transportation Statistics (BTS).
- Frozen raw airport response SHA-256: `43d735c2b82b48e5773c31b27ced0563180dd63500d36beff4aeca29c9838278`.
- Frozen raw runway response SHA-256: `f1f2c8cc37f920399fbddd11e99bf53763e28a74482c9b3804c5fd0486918ced`.
- Service records: <https://services.arcgis.com/xOi1kZaI0eWDREZv/ArcGIS/rest/services/NTAD_Aviation_Facilities/FeatureServer> and <https://services.arcgis.com/xOi1kZaI0eWDREZv/ArcGIS/rest/services/Runways_View/FeatureServer>.

### USGS 3DEP

- Source: USGS 3DEP Bare Earth DEM service.
- USGS states that USGS-authored data and information are in the U.S. public domain. Credit is requested but not a copyright condition.
- Courtesy credit: U.S. Geological Survey 3D Elevation Program (3DEP).
- Frozen request-index SHA-256: `53048c3d5b9908511845bbf45c56830ec1df3254dd19155ae3de9a825411339d`.
- Frozen service-metadata SHA-256: `d454148c69783db1b4816f48d1279d7c248b8e26ec70d4156a9874b49fd9d570`.
- The 33 individual response hashes remain pinned in `kbdu_source_manifest.json`; raw responses stay outside Git.
- Policy: <https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits>.

### OpenStreetMap

- Source: bounded Overpass API extracts.
- License: Open Data Commons Open Database License 1.0 (`ODbL-1.0`).
- Required notice: `© OpenStreetMap contributors`, with <https://www.openstreetmap.org/copyright>.
- Frozen airport extract SHA-256: `e1677f58c73a6ab62b5266c853c4e790ccf92c5bfe86f7bdb8cfbadc3981a25d`.
- Frozen context extract SHA-256: `2c4a4afe035e88f0b2db466a82e3fd20f25e76905e3e56233e73789ed1a69eda`.
- The compact derivative database `kbdu_reference_context.json` must remain available in machine-readable form under ODbL-1.0 when a build derived from it is publicly distributed.
- The baked meshes and macro image are Produced Works. Their public display still carries the OSM attribution notice; project code and independently authored material/shader work are not relicensed as ODbL database content.

### NAIP availability only

The pipeline queried USGS/USDA NAIP Plus availability, but downloaded and committed no imagery pixels. The decision record SHA-256 is `e42528b838d2d016413ac89671d520b097a05ac717cb2147e6bea9ae469a04cd`; it is provenance, not runtime imagery.

## Baked production environment integrity

Key deterministic outputs from the frozen source:

| Output | SHA-256 |
| --- | --- |
| `ProductionEnvironmentRoot.prefab` | `036af4fc90d13da215e5443de7402a50b8d2cdd3b534209e200ff4879ca4c583` |
| airport-patch terrain mesh | `3779b536b54d44f059471522484f78c4c464b5d3a389241e9d759303f43ed7a6` |
| near 4 km terrain mesh | `0d883b833f51fb9f1c3fe5dee5db7f913a2755c29326a8a7bad758547965790e` |
| mid 12 km terrain mesh | `7f71e71ee80017245b6da2bec8ddb07540b6a9348cfc4c9b426bdb40cc5a1f04` |
| immutable far 24 km terrain mesh | `fec1a3044846eb60549baf9b073bfc279baf18b933b85e4ee6949b1c63e593a2` |
| FAA runway mesh | `d37311c9446d27721c1a0fc7f109cf891fcf5bad4303c042a55a9518e548375c` |
| runway marking mesh | `4275e9acadecf9b044dc42d11eab882cf8ecb3aa6bb76230d46d91608f4dbc9c` |
| OSM Boulder Reservoir water mesh | `aed735d9d3bc95e102354dfb489c2aa5008a5c5e092444bb3706c7bccd47bba3` |
| project-authored shore-bank mesh | `3fdc0b16b9939abb655d38fd832a3c7e3c8588100950960a72fc5c7e1904f5b4` |
| sparse project-authored vegetation mesh | `b23232056bde5f404888be018e2ad20aa57d7d3adc3b87b7e40422da5fcb2f03` |

The complete frozen R&D `Assets/Production/Environment` non-`.meta` inventory contains 97 files and 11,303,670 bytes. Its inventory digest is `aeb0804cb12e65db9aaf18897ec20c01dabc8aa74192b65800eb88a8c70bf2db`, computed as SHA-256 over UTF-8 lines sorted by frozen R&D repository-relative path in the form `path<TAB>bytes<TAB>lowercase-file-sha256`, joined with LF and no trailing LF.

## Current clean-product post-pass ledger

The frozen extraction ledger above remains immutable provenance. The one targeted clean-product visual pass changed only project-authored YAML/shader assets; the licensed GLB and raster textures remained byte-identical. Current reviewed hashes are:

| Product asset | SHA-256 |
| --- | --- |
| `Production_BoulderReservoir_Opaque.mat` | `4ec8e76fd13f16ddcdca653c35ca7450342e57d6e96a2b922c0978deb3b0c05a` |
| `Production_FarTerrain_Immutable.mat` | `8feadd90e379ed5a0dc41a0ac3c9a5dbc379492a63f3a2d6a8151f7fad538915` |
| `Production_MacroGround_Green.mat` | `7f6cdd915e509535e912b707b972a92264021f1a6d5298f91ac3077d2efc9e69` |
| `Production_MacroGround_Harvest.mat` | `d47437e29a510e973f15d00349a327a81f9cffe6c885b1b86be802b099c210a8` |
| `Production_MacroGround.mat` | `b32f38809ab6cb2cfb041ff59ae9df2a948580b78c33e656d1b510d8d8fd799f` |
| `Production_WaterBank.mat` | `36d72e629d18e9ab9ee8c6d068133160da31975cc8d522c760681a2f13a1f7ed` |
| `ProductionEnvironmentRoot.prefab` | `c2610881d8bae183feea32f15f89417c5465511c75ba16f7c4e61f6d26581bda` |
| `ProductionC172AircraftRig.prefab` | `0b64406328896b9eb3349df464369b709ef5def635dab1b034452b8fa21e5de9` |
| `ProductionWorldRoot.prefab` | `3275aae94f0e47751e13bf460019e73d81b19d7ccfe3fba6ded2c7aef0d15dbb` |
| `ProductionMacroGround.shader` | `e5e9f81c4beb2c26f34d5ca7b4b539f366aa12d6519f057fd4dfe03a62c4901f` |
| `ProductionStableWater.shader` | `a4eb553c7d84d849565fae9ee7eab20b79570d609b56223e5e70d0e23c54bac7` |

The current `QuestFlight/Assets/Production/Environment` non-`.meta` inventory contains 97 files and 11,281,449 bytes. Its product-repository-relative inventory digest is `5758adcfeed1d97412b4ec853a8b16d13185c2d7a31e465eefa206eaeafc2d5a`, using the same sorted-line algorithm documented above.

## Exclusions

- No Google Maps, Google Earth, or Street View content.
- No NAIP/orthophoto pixels.
- No raw FAA, USGS, OSM, DEM, imagery, or download archives.
- No legacy splat/capture data.
- No binary runtime asset whose creator, source, terms, or exact content hash is unclear.
