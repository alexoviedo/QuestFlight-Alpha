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

### Frozen extraction NAIP availability-only record

The frozen extraction pipeline queried USGS/USDA NAIP Plus availability, but downloaded and committed no imagery pixels at that milestone. The decision record SHA-256 is `e42528b838d2d016413ac89671d520b097a05ac717cb2147e6bea9ae469a04cd`; it is retained as historical provenance and is not rewritten to describe the later standalone recovery.

### Standalone recovery NAIP derivatives

The standalone recovery added bounded natural-color derivatives from the official USGS NAIP Plus ImageServer. Catalog records identify USDA-FSA APFO imagery acquired 2019-09-19 at 0.6 m source resolution. United States government works are public domain; courtesy credit is `USGS, USDA, The National Map: Orthoimagery`.

| Runtime derivative | Coverage / import | SHA-256 |
| --- | --- | --- |
| `KBDU_NAIP_2019_Near4km_4096.jpg` (6,320,221 bytes) | 4 km × 4 km, 4096², ASTC 6×6, mipmapped/streamed | `446e5c7fa13fc653de1f3e8404829c0e54b7e919fe99ebfba79d747a0b1dadea` |
| `KBDU_NAIP_2019_Mid12km_2048.jpg` (1,873,985 bytes) | 12 km × 12 km, 2048², ASTC 6×6, mipmapped/streamed | `d75316cb03a8ddcdc6d326186f9fc5e936d19d90ffc4e42ffe627888aa8593cd` |

Exact raster IDs, corrected requested/returned extents, raw external hashes, processing settings, color-match statistics, output hashes, and the no-Google confirmation are pinned in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`.

### Project-authored standalone airport art

| Product source/runtime asset | Terms | SHA-256 |
| --- | --- | --- |
| `tools/kbdu_airport_art/source/KBDU_AirportKit.blend` | Project-owned original work; Blender source | `48b07cf7d54e56c7607a4839c27cc3ea0d089a9472dfa225a12c7bc1693e0c24` |
| `KBDU_AirportKit.glb` | Project-owned generated geometry; no external mesh input | `d16a398e150aa069a4eedbdb9d0604bc807b4d6175a5e002baeaa44ea1547c5d` |
| `KBDU_AirportSharedAtlas.png` | Project-owned generated pixels | `fdc5d4d8198a355e48cfdf3df4a17e3d8a92d271a1322970583fee965ecddfaf` |
| `KBDU_RunwaySurface_Albedo.jpg` | Project-owned deterministic procedural pixels | `6a3cfa432f8556830879b6ee615b080f499c5682f5f2157c40b47f683b64c3b3` |
| `KBDU_RunwaySurface_MetallicSmoothness.png` | Project-owned deterministic procedural pixels | `599b4281144eb5861467a190d2d52f377f2b95dc5867623a86fc049ab091b031` |

The exact combined Unity mesh hashes and their source-manifest links are recorded in `tools/kbdu_airport_art/kbdu_environment_integration_manifest.json`.

### Standalone far-terrain post-pass

The far mesh is a deterministic project-authored mesh derived from the frozen public-domain USGS 3DEP grid. The committed 241×241/100 m height package SHA-256 is `23eac876f89605fa291ecfbf23216ad8a248beaa9df277b01c47cae149551226`; the final serialized mesh is 5,210,555 bytes with SHA-256 `989317a3dbd870536e995e443b634f48083f06ab9fdb11eb4316339add988d96`. The 100 m grid is bilinear resampling for mesh density and baked-normal continuity, not additional source elevation detail.

The 60,038-byte `Terrain_AirportPatch_Near_Transition_Mesh.asset` is a deterministic project-authored seam interpolation between the existing public-domain USGS-derived airport patch and near terrain. It adds no source data and has SHA-256 `8524212a4814473cc25733babeb0457b1f667b5913981726a3676e3364791c2a`. Its exact size/hash and the other standalone recovery meshes are pinned in `tools/kbdu_airport_art/kbdu_environment_integration_manifest.json`.

### Native JSBSim package binaries

These four development-gated binaries are built from JSBSim 1.3.1 revision `3b25f25e49b42d0489c04ac805674fc1450ca579` and the project C ABI wrapper. JSBSim and the linked package are distributed under GNU LGPL-2.1-or-later; source/revision/build/license records and all 18 package payload hashes are pinned by `Packages/com.alexoviedo.questflight.jsbsim/SOURCE_MANIFEST.json` (SHA-256 `4b5ea046f70fb1b51ba762f1d7fc3dd4365354686287b59d7600f71434340d21`).

| Package binary | Bytes | SHA-256 |
| --- | ---: | --- |
| `Runtime/Plugins/Windows/x86_64/JSBSim.dll` | 2,007,040 | `9f80c6af748ede9c0addff022ce78337dfa2930046e3a8cfe155cc610b942bc7` |
| `Runtime/Plugins/Windows/x86_64/qfl_jsbsim_native.dll` | 37,888 | `5ee4ccd81cc296a946c843cacf0ea5ab0d132c78c3dba3c9056eeddb612c83d4` |
| `Runtime/Plugins/Android/arm64-v8a/libJSBSim.so` | 2,446,528 | `38c2fe56ba86568763caab81d62a4e3096d4b8fd2222d19ee824934fbc4af8a1` |
| `Runtime/Plugins/Android/arm64-v8a/libqfl_jsbsim_native.so` | 49,000 | `bf5f10df987a2411e0889b307eca4cd7fe08feef3c1427bfc1da7ce098162bb5` |

The standalone recovery's 11 new true-binary commit candidates total 13,348,076 bytes. Their sorted `path<TAB>bytes<TAB>lowercase-sha256` inventory digest is `65bd12083bc99ed2c1e401dd6d62adf28515229a1c0117809004a47cd7b6394d`; all Unity/package binaries have paired metadata and all 11 match Git LFS rules.

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

## Historical clean-product post-pass ledger (2026-07-12)

The frozen extraction ledger above remains immutable provenance. The one targeted clean-product visual pass completed on 2026-07-12 changed only project-authored YAML/shader assets; the licensed GLB and raster textures remained byte-identical. These hashes describe that historical milestone, not the later standalone-recovery state:

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

At that milestone, `QuestFlight/Assets/Production/Environment` contained 97 non-`.meta` files and 11,281,449 bytes. Its product-repository-relative inventory digest was `5758adcfeed1d97412b4ec853a8b16d13185c2d7a31e465eefa206eaeafc2d5a`, using the same sorted-line algorithm documented above.

## Exclusions

- No Google Maps, Google Earth, or Street View content.
- No raw NAIP/orthophoto downloads or lossless processing intermediates; only the two optimized, provenance-recorded public-domain derivatives above.
- No raw FAA, USGS, OSM, DEM, imagery, or download archives.
- No legacy splat/capture data.
- No binary runtime asset whose creator, source, terms, or exact content hash is unclear.
