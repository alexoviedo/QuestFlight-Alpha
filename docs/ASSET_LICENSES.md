# Asset licenses and integrity record

Audit basis: frozen R&D commit `cd4131fa7732a6db2d7387921dfef33e2c206970`, tagged `product-extraction-source-2026-07-12`.

Every product binary must match an entry here or receive a new source/license review. Raw downloads are not product assets.

## Cockpit Completion V3 generated derivatives

The V3 instrument layout/mesh/atlas, neutralized source-meter mesh, avionics
layout/glyph mesh/glyph atlas, materials, shaders, and JSON manifests are
project-authored deterministic derivatives. They use only:

- the already recorded KOG_THORNS C172 source (CC BY 4.0, SHA-256
  `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`);
- project-authored vector/raster markings and glyphs;
- deterministic Unity authoring code committed with the derivatives.

No new external download or license boundary was introduced. KOG_THORNS
attribution and the existing change notice remain required.

## Airport near-field ground detail V1

The near-field layer introduces no external binary asset. Geometry is generated
offline by `AirportNearFieldDetailAuthoring` from project-owned procedural
rules. FAA runway facts are United States public-domain government data; the
bounded aeroway context and windsock location remain an OSM Produced Work under
ODbL-1.0 with `© OpenStreetMap contributors` attribution. The layer reuses the
existing `KBDU_ContextVegetation.mat`.

| Derived asset | Bytes | Terms | SHA-256 |
| --- | ---: | --- | --- |
| `AirportGrassClump.asset` | 6,540 | Project-owned procedural geometry | `613aa1520df6fa4fd607d72e975947cf97281c2ebfd04ffbd3ddad3d07928d35` |
| `AirportNearFieldCombined.asset` | 72,033 | Project-owned geometry derived from public-domain FAA and ODbL aeroway context | `daf1b395816835fb3ab44638ef6e56423b18d87b3acb7665a962ec7959ec503c` |
| `AirportNearFieldSourceV1.json` | 1,420 | Project-authored provenance/contract record | `b8b836ca4eac8fc9bb712e4e4d9d27554b8c748dc053a06b6db49db8eda44975` |

## Runtime visual assets

| Product asset | Source / creator | Terms | SHA-256 |
| --- | --- | --- | --- |
| `cessna172.glb` (26,962,216 bytes) | `Cessna172`, KOG_THORNS, [original Sketchfab model](https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/); attribution and change notice required | `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872` |
| `C172_FourPedalAssembly.asset` | Project-authored derivative of the frozen KOG_THORNS `cessna172.glb` pedal geometry; the missing copilot pedal is symmetry-derived and winding/tangents are corrected | CC BY 4.0; KOG_THORNS attribution retained; modified Unity runtime derivative | Source and output geometry/hash record: `Assets/Production/Cockpit/Pedals/C172_FourPedalAssemblyManifest.json` |
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

The standalone environment retains three aligned, bounded natural-color base derivatives from the official USGS NAIP Plus ImageServer. The source catalog records USDA-FSA APFO imagery acquired on 2019-08-03 and 2019-09-19 at 0.6 m source resolution. United States government works are public domain; courtesy credit is `USGS, USDA, The National Map: Orthoimagery`.

| Runtime derivative | Coverage / import | SHA-256 |
| --- | --- | --- |
| `KBDU_NAIP_2019_Near4km_4096.jpg` (6,320,221 bytes) | 4 km × 4 km, 4096², ASTC 6×6, mipmapped/streamed, anisotropy 8, mip bias `-0.15` | `446e5c7fa13fc653de1f3e8404829c0e54b7e919fe99ebfba79d747a0b1dadea` |
| `KBDU_NAIP_2019_Mid12km_4096.jpg` (7,273,113 bytes) | 12 km × 12 km, 4096², ASTC 6×6, mipmapped/streamed, anisotropy 8, mip bias `-0.15` | `5e8d6edbcc5dc937c78b5fb8e4a5f8f7a06bda726b4509684e237f2b17fbdf9a` |
| `KBDU_NAIP_2019_Far24km_2048.jpg` (1,705,067 bytes) | 24 km × 24 km, 2048², ASTC 6×6, mipmapped/streamed, anisotropy 8, mip bias `-0.10` | `799be878d5c6be73b5596cbea70ee4e122859a528231608e40796f1020617b33` |

Exact raster IDs, corrected requested/returned extents, raw external hashes, processing settings, near-to-mid and mid-to-far color-match statistics, output hashes, and the no-Google confirmation are pinned in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`. These base assets remain licensed inputs; the recovery branch additionally binds the optimized derivatives below.

### Aerial Scenery Recovery V2 NAIP derivatives

Recovery V2 adds no new license boundary. It derives five optimized runtime images from the same official public-domain 2019 USGS/USDA NAIP Plus source. Raw service responses and decoded working rasters remain external.

| Committed derivative | Runtime contract | SHA-256 |
|---|---|---|
| `AerialSceneryRecoveryV2/Ground/KBDU_NAIP_2019_MidNW_2048.jpg` | 2048² hot quadrant, 1.984375 m/texel, ASTC 6×6, mipmapped | `677142d71bb8b66ba4e181c5a2d966cd73a4b87e21c631eef3c48a339c00b054` |
| `AerialSceneryRecoveryV2/Ground/KBDU_NAIP_2019_MidNE_2048.jpg` | 2048² hot quadrant, 1.984375 m/texel, ASTC 6×6, mipmapped | `91ea5f1f4517c081900634073767b22abf821c9d5115de5de8ff0e706c16d907` |
| `AerialSceneryRecoveryV2/Ground/KBDU_NAIP_2019_MidSW_2048.jpg` | 2048² hot quadrant, 1.984375 m/texel, ASTC 6×6, mipmapped | `47fe667d0eb344a43dc4469ef332918fc930123187836afe0f5798d5e272e526` |
| `AerialSceneryRecoveryV2/Ground/KBDU_NAIP_2019_MidSE_2048.jpg` | 2048² hot quadrant, 1.984375 m/texel, ASTC 6×6, mipmapped | `3d4cc4dd0adaa40c28ffe719a347574a26ff8928e92d04d89910c9aac15a64c2` |
| `AerialSceneryRecoveryV2/Ground/KBDU_NAIP_2019_MidBase12km_4096.jpg` | 4096² 12 km fallback, 2.9296875 m/texel, ASTC 10×10, mipmapped | `6d547c0d1ccc7ff646f59cc2d6ba209d601dda9da0cb61f00beea899b8aeff24` |

The source manifest SHA-256 is `f1ed4c73f02b786098766eb75731b11a0abf0e2b29ba88b41579ae9e97f0f6c2`; the ground contract SHA-256 is `5814882a72c299f207d4f9734bc18d93b14f3b8bc9ffb5cfd9cac85f5df819fa`. Automatic mip streaming is disabled for these five assets because the custom world-space UV path lacks a proven streaming-UV contract; this is documented as resident texture cost, not called streaming.

### Photographic sky derivative

The selected sky is [Kloppenheim 03 (Pure Sky)](https://polyhaven.com/a/kloppenheim_03_puresky), authored by Greg Zaal with pure-sky edits by Jarod Guest and distributed by Poly Haven under CC0 1.0. The raw 4096×2048 HDR remains outside Git; the committed runtime file is a project-generated, linear-radiance 2048×1024 derivative with no tone mapping or clipping.

| Product asset | Source / terms | SHA-256 |
| --- | --- | --- |
| `Kloppenheim03_PureSky_2K.hdr` (5,430,234 bytes) | Poly Haven `kloppenheim_03_puresky`; CC0-1.0 | `ffd28511add85d9508b51f50d3bbe4d822f4a5240800edb4f014edb75abdb1aa` |

The official raw-source MD5, raw SHA-256 (`f906a7334e5d57007fa5516bd141df2ada8fdc3d2ba0c8bdf445d64f1bbb010a`), authorship, official URLs, derivative tool, import settings, and external evidence hashes are pinned in `QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json` and summarized by the adjacent `SOURCE_AND_LICENSE.md`.

### Batched OpenStreetMap context

The bounded source database is `kbdu_reference_context.json` (SHA-256 `5d0d0c3c71532b5106119b8e647e303e12bdeb662960ef78543050166bb8e3e0`) under ODbL-1.0. The extruded building meshes are OSM Produced Works. Their public display retains `© OpenStreetMap contributors` attribution; the shared atlas, vegetation meshes, shaders, materials, and serialized hierarchy are project-authored presentation assets.

The exact hashes for all 64 generated LOD mesh assets and their prefab/material outputs are pinned in `QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_ContextAssetsManifest.json` (25,991 bytes; SHA-256 `3f9802eb0912c0f3f93c014ce0f676366f42d8328a5df7ab0caa13d64d991018`). The shared project-authored `KBDU_ContextAtlas.png` is 13,716 bytes with SHA-256 `51b0ef4df8f15c3b8366a2c8a62a517ea48931b85f173efabcb6767daf79153f`.

That manifest records 246 deterministic tree placements total, 24 shrub companions, and six project-authored airport-perimeter tree placements. The remaining placements follow the bounded mapped road/water context. Vegetation meshes and presentation are project-authored; this does not alter the ODbL boundary of the source context database.

Three static parked-aircraft scale cues use a separately reviewed CC BY 4.0 scenery adaptation of the pinned KOG_THORNS source. The adaptation retains only exterior body geometry; it removes cockpit, interior, glass, textures, source materials, branding presentation, and animation, then joins and decimates the result into two opaque LODs. The three placements are offline-combined per LOD into one non-crossfaded `LODGroup` and use one shared material. They are neither flyable nor interactive. Copyright attribution and the change notice must remain with the product; CC BY does not grant manufacturer trademark rights or imply endorsement.

| Parked-aircraft derivative | Terms / purpose | SHA-256 |
| --- | --- | --- |
| `KBDU_ParkedAircraft_LODs.glb` (232,272 bytes) | KOG_THORNS `Cessna172`, CC BY 4.0; exterior-only static scenery adaptation | `0f723238782d47d63496222aa2232a53da58823b2dbfee614a1628dcf2e56c34` |
| `parked_aircraft_airport_LOD0.asset` (767,880 bytes) | Three offline-combined instances, 9,600 triangles total | `448f9cb2ae138eafbd23e18f616e313f147eb1f21413acedee166c1145638598` |
| `parked_aircraft_airport_LOD1.asset` (248,949 bytes) | Three offline-combined instances, 2,700 triangles total | `6ff371b190cf2fcc9e31c82c86bbe69143b71d4fc8bbaea96fff742e36bd2c08` |
| `KBDU_ParkedAircraft.mat` (799 bytes) | Project-authored shared opaque vertex-color material | `ebd0dfc4bb8656efc7c8f1cb8b05247df1a840fb8c3fa2ff4599f2adc28724e4` |
| `KBDU_ParkedAircraftSourceManifest.json` (2,697 bytes) | Attribution, source hash, change notice, exclusions, Blender version, and LOD contract | `c24ed699960d5a818585d69c3433f134c87a246d772924976762d2a255525784` |

The context materials serialize Unity's instancing-variant flag, but the current custom building and vegetation shaders do not compile GPU-instancing variants. The current implementation therefore relies on offline mesh batching and non-crossfaded LODGroups and makes no GPU-instancing claim.

### Project-authored reservoir presentation assets

The reservoir retains the OSM way 35597714 shoreline (`© OpenStreetMap contributors`, ODbL-1.0). Its two normal maps are deterministic project-authored mathematical textures. The packed mask is a project-authored rendered derivative of the OSM shoreline; its depth channel is artistic distance-to-shore data, not surveyed bathymetry. On Aerial Scenery Recovery V2 these assets remain licensed and retained but are unbound; water presentation is orthophoto-only.

| Product asset | Terms | SHA-256 |
| --- | --- | --- |
| `BoulderReservoir_BroadWave_NM.png` (158,829 bytes) | Project-authored deterministic texture | `5f68754e86e9631a3a8034a6f5f13e3094bc738ca6f286a5a7b5dc56e87d686a` |
| `BoulderReservoir_Ripple_NM.png` (221,702 bytes) | Project-authored deterministic texture | `4eb439560cd96ce5fc9f8dd59a54284f93f7b8888165c7e71b7fcf5c78e2ea80` |
| `BoulderReservoir_DepthShoreRoughness.png` (307,644 bytes) | Project-authored OSM shoreline Produced Work; retain OSM attribution | `258683cba2eca00d585dbe99c24e762da55b67e9c7a5387b3be860ec230356bb` |

The generator, authoritative mesh lineage, channel semantics, Android import estimate, and output hashes are pinned in `tools/kbdu_reservoir_water/asset_manifest.json`.

### Project-authored standalone airport art

| Product source/runtime asset | Terms | SHA-256 |
| --- | --- | --- |
| `tools/kbdu_airport_art/source/KBDU_AirportKit.blend` | Project-owned original work; Blender source | `afab623afc03b6d33a1f142e71ff93a42e831053722a5c186f374d9c3ea80d2c` |
| `KBDU_AirportKit.glb` | Project-owned generated geometry; no external mesh input | `4cd24d44adf8e099ef451dcd4ed879796de42c5a3ef1ba96e90eb30d7f9fac21` |
| `KBDU_AirportSharedAtlas.png` | Project-owned generated pixels | `e4d11fc26949394750bb949434d50ca5870a2569383037dfa47fdd07b534ab07` |
| `KBDU_RunwaySurface_Albedo.jpg` | Project-owned deterministic procedural pixels | `6a3cfa432f8556830879b6ee615b080f499c5682f5f2157c40b47f683b64c3b3` |
| `KBDU_RunwaySurface_MetallicSmoothness.png` | Project-owned deterministic procedural pixels | `599b4281144eb5861467a190d2d52f377f2b95dc5867623a86fc049ab091b031` |

The single 512×512 atlas carries bounded wall corrugation, roof seams, door panel joints, and lower-edge grime cues without adding a material or texture. The Quonset source uses outward shell winding and double-wound end-cap fans; with back-face culling, only one cap winding is front-facing from either direction. The exact front/rear validation counts, generated-output hashes, and preview paths are pinned in `tools/kbdu_airport_art/kbdu_airport_art_manifest.json`. Exact combined Unity mesh hashes and their source-manifest links are recorded in `tools/kbdu_airport_art/kbdu_environment_integration_manifest.json`.

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

The photoreal standalone-environment milestone's 11 changed or new true-binary commit candidates total 15,911,609 bytes. Their sorted `path<TAB>bytes<TAB>lowercase-sha256` inventory digest is `caa7cf2b4bc62ea40092aeec1fb703df72e7c5da4a0271bdfeee4d63c8259172`; all Unity binaries have paired metadata and all 11 match Git LFS rules, including the optimized 2K HDR sky.

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

### Airport-close Poly Haven tree derivative

The airport-close hybrid LOD tier adapts Poly Haven's
[Tree Small 02](https://polyhaven.com/a/tree_small_02), distributed under
[CC0 1.0](https://polyhaven.com/license). The raw 1K `.blend` and source
textures remain external. Blender 5.1.2 evaluates the source trunk, branch,
and foliage geometry. The deterministic project compiler emits source-leaf
hero geometry, a reduced volumetric-clump middle tier, and a world-upright
eight-azimuth far impostor. Intermediate Blender renders remain external.
The two optimized runtime atlases and all mesh tiers use the existing shared
opaque canopy material.

| Product derivative | Bytes | SHA-256 |
| --- | ---: | --- |
| `AirportCloseTreeLods.bytes` | 270,694 | `295659ab4f5d18ce92f05388a936a37149747ff0f735726a2abe23b96bd9194f` |
| `AirportCloseTree_LOD0.asset` | 518,217 | `a320adda130595931639860f42441deb92b57a899f56862bdad84e73c737f053` |
| `AirportCloseTree_LOD1.asset` | 29,605 | `cdeb28117aeda419896ec12cc2f713ff8ed908fbc436954a6b7d38faa6de153d` |
| `AirportCloseTree_LOD2.asset` | 3,731 | `2ff108d3f327b49d2769e36c4fe4bdfc64447686d08880157c13c4b0eadb944d` |
| `AirportCloseTree_HeroLeaf_RGBA.png` | 667,438 | `67a613d7b34086d4f6c1303e4b53b052b968f57f2a2d40259a697a04e768fd1f` |
| `AirportCloseTree_Impostor8_RGBA.png` | 1,932,310 | `d58cbaeb9f7b6b61e3982c66e79360d5933e97b257af3e4759f81265a34787d2` |
| `ProductionAirportCloseTreeInstances.json` | 257,444 | `1a92e2af0270cff7de1dbb6febf5e4f339920e34ef455387819995a5af889eb7` |

The external source blend SHA-256 is
`af3fef6da3eb97057d9d4d3c4281c5944696b8d518715deddd8500482d30b1bd`.
Exact processing and LOD records are committed in
`AirportCloseTreeLods_SourceManifest.json`. The derivative is a visual
archetype; placement, scale, and tint remain driven by the existing bounded
orthophoto/lidar semantic compilation and are not a surveyed tree inventory.
The two runtime atlases add an estimated 1,557,696 Android ASTC bytes.

## Exclusions

- No Google Maps, Google Earth, or Street View content.
- No raw NAIP/orthophoto downloads or lossless processing intermediates; only optimized, provenance-recorded public-domain derivatives are committed. The recovery material binds the retained near/far sources plus the four hot quadrants and 12 km fallback recorded above.
- No raw FAA, USGS, OSM, DEM, imagery, or download archives.
- No legacy splat/capture data.
- No binary runtime asset whose creator, source, terms, or exact content hash is unclear.

## Airport-Core Photoreal Benchmark V4 assets (failed feature branch)

V4 uses only project-owned output, existing rights-cleared project derivatives, public-domain NAIP imagery, bounded OSM Produced Works, and five Poly Haven CC0 texture sources: `asphalt_02`, `brown_brick_02`, `concrete_wall_005`, `corrugated_iron_02`, and `worn_corrugated_iron`. Raw maps, API snapshots, reference photographs, and contact sheets remain external. Exact source URLs, source MD5/SHA-256 values, derivative hashes, and status are recorded in the adjacent V4 manifests:

- `AirportCoreV4/Buildings/AirportCoreBuildingSourceManifestV4.json`;
- `AirportCoreV4/Materials/SOURCE_MANIFEST.json`;
- `AirportCoreV4/CleanPlate/SOURCE_MANIFEST.json`;
- `AirportCoreV4/Runway/SOURCE_MANIFEST.json`;
- `AirportCoreV4/Water/Reports/AirportCoreWaterVegetation_SourceManifest.json`.

The executive terminal, Type 2 hangar row, fuel/service kit, airport props, vegetation geometry, analytic water normals, procedural masks, and Blender sources are project-owned. The clean plate is a bounded derivative of the already-recorded public-domain NAIP atlas; replacement bounds retain OSM attribution where applicable. V4.1 combines the existing project-owned apron-prop and fuel-service source meshes into six serialized Unity mesh derivatives (`AirportCorePropsCombined_LOD0/1/2` and `FuelServiceCore_LOD0/1/2_V4`); it introduces no new external source or license boundary.

V4.3 introduces no external asset or new license boundary. It deletes the obsolete project-authored runway feature mask and reservoir suppression include, renames the retained project-authored reservoir packed mask without changing its GUID or pixels, and deterministically regenerates the minor-water RGBA mask from the already-recorded OSM-derived V2 surface geometry. The reservoir shader reuses the committed public-domain NAIP mid-zone texture already licensed above. Current output hashes and the failed-visual status are pinned in the runway and water source manifests listed above.

V4.3.1 introduces no external binary asset or license boundary. Its runtime changes are project-authored shader source only: a bounded runway depth bias and a tuned response for the existing OSM-derived terrain water mask. The new isolation shader, evidence shader, camera authoring, tests, and image analyzer are project-authored Editor/tooling sources. No diagnostic capture is committed. Updated runtime hashes are pinned in the runway and water source manifests.

### KBDU runway photoreal material source (2026-08-07)

The runway material refresh uses one project-generated, top-down asphalt source
created with OpenAI's built-in image-generation tool. It contains no downloaded
texture pixels. The committed source prompt, exact generated-source hash,
reference URLs, deterministic compiler, optimized derivative hashes, and
runtime texture settings are recorded in
`tools/kbdu_runway_photoreal/SOURCE_PROMPT.md` and the adjacent
`AirportCoreV4/Runway/SOURCE_MANIFEST.json`.

City of Boulder photographs were inspected only as uncommitted appearance
references. FAA-derived public-domain runway facts establish that Runway 08/26
is a maintained asphalt surface; they do not establish surveyed material color
or physical friction. The project-generated source and deterministic normal and
wear/roughness derivatives are project-owned. The refresh adds no external
license boundary, material, renderer, transparent pass, or runtime camera.

The rejected V4 `LightingData.asset`, bake manifest, two color lightmaps, and two directional lightmaps were deleted from the product tree by the V4.1 fail-closed quarantine. Two replacement bake attempts were also rejected and deleted because each exceeded the bounded V4.1 one-atlas remediation budget. Their logs remain external under the V4.1 artifact root; no V4.1 lightmap is accepted or committed.

V4 originally produced 55 LFS-covered binaries. V4.1 deletes the four rejected baked-lightmap textures and retains 51, all with paired Unity metadata. The existing non-LFS historical 5.43 MiB HDR blob remains a bounded policy exception; this branch does not rewrite history. No V4 raw download, build, APK, capture, or cache is committed.

### V4.2 project-generated terrain and bake derivatives

V4.2 introduces no external source. Its visual-only airport mesh is a deterministic derivative of the existing public-domain USGS 3DEP airport mesh; the authoritative terrain query asset and collider bindings are unchanged. The GI parameters, `LightingData.asset`, lightmap, and bake manifest are project-generated from the already rights-cleared benchmark scene. The EXR is covered by Git LFS; raw bake caches, captures, builds, and APKs remain external or ignored.

| V4.2 asset | Bytes | Source / terms | SHA-256 |
| --- | ---: | --- | --- |
| `KBDU_AirportTerrain_5m_V2.asset` | 3,410,887 | Existing USGS 3DEP United States government work/public-domain derivative | `84a4303216d5b46932a4f28f46dc9fb6a8594c439735956fcec68700aad1fb24` |
| `KBDU_AirportTerrain_ErrorBounded_V4_2.asset` | 705,505 | Project-generated error-bounded derivative of the preceding mesh | `d0d85a3ce221c75ef2f08ff0f42018b864288bb828c06ea2f67bcaeddbd701be` |
| `KBDU_AirportTerrain_ErrorBounded_V4_2.json` | 1,953 | Project-authored provenance/validation report | `a216a058205c5745631bcea247b9bbd087ff60c260f01fb7d05a29044183f8aa` |
| `AirportCoreV42OneAtlas.giparams` | 660 | Project-authored Unity bake parameters | `876a14671822301f453722ba777515da7e70ed3f23084f8a2f704d5f19facdd8` |
| `LightingData.asset` | 25,817 | Unity-generated bounded airport-core bake data | `f3ffdd5c2af906c4cd5561eb1b4397daf6c21820edfddbe56b9a7fe3115e8283` |
| `Lightmap-0_comp_light.exr` | 508,708 | Project-generated 512² non-directional baked lighting | `e1c7d437122306f50d617285b31cd1a36d55737478da15a99a371fdae747edf2` |
| `BAKE_MANIFEST.json` | 528 | Project-authored finite-bake/hash record | `3876d0009533d64f4dded2fd7dd5bf5a8e7e9f18c4b497b1481a89dfe7f37fea` |

## Environment Reconstruction V2 derivatives (feature branch)

- Terrain: USGS 3DEP 1 m bare-earth DEM, United States government work/public domain. External raw payload: 67,117,862 bytes, SHA-256 `93b32e8ef2fef464ff4ee49702d235b30eaff7d347f22b8c4add8848cd7e9d82`. Committed derivatives are the 5 m airport mesh, 25 m near mesh, terrain-surface asset/manifest, and 1024² normal map (`4ba7833e6f27f8ee3abceb79caf502e53f09672525038eedea7d89df6d764cc8`).
- Buildings and water source features: bounded OpenStreetMap snapshot dated 2026-05-31, ODbL-1.0, `© OpenStreetMap contributors`. Raw building response remains outside Git; SHA-256 `024560100b28946bb805478c6be091d752172200e7c8fcbe1ebe1d7bea858416`. Committed JSON/mesh derivatives retain stable OSM feature IDs and license class.
- Vegetation mask: public-domain USGS/USDA NAIP natural-color fallback, derived from committed Mid12km imagery SHA-256 `5e8d6edbcc5dc937c78b5fb8e4a5f8f7a06bda726b4509684e237f2b17fbdf9a`. External decoded working raster SHA-256 `9a565142b8d79c3029149f7dd36e15ad95688f61b67637e80ecc3c08618e3158`. Committed mask hashes are recorded in `vegetation_source_v2.json`.
- Vegetation meshes: four procedural species and their 12 OBJ LODs are project-owned original work. The shared MTL SHA-256 is `9bc39f427446720ec729dd82b994273b77eefab2cd808dc4aed8d36d78751884`.
- Parked aircraft reuse the already-audited KOG_THORNS CC BY 4.0 derivative and attribution above. V2 changes placement/ground contact and adds a third LOD; they do not change the license boundary.
- No raw DEM, raw OSM response, decoded NAIP raster, build, capture, cache, or package artifact is committed.

## Aerial Scenery Representation V1 derivatives (failed feature branch)

Outcome: **FAIL — preserve the feature branch only**. This milestone introduces
no broader content right and is not accepted as visually complete. Its source,
license, derivative, and byte/hash records remain useful even though the final
inspected environment score is 5.5/10 and all ten required recorded simulator
replays are missing. The complete gate record is [Aerial Scenery Representation
V1 evidence](evidence/AERIAL_SCENERY_REPRESENTATION_V1_2026-07-18.md).

All abbreviated paths in the following table are relative to
`QuestFlight/Assets/Production/Environment`.

| Canonical product asset | Bytes | Source / terms | SHA-256 |
| --- | ---: | --- | --- |
| `AerialSceneryV1/KBDU_AerialSceneryManifestV1.asset` | 2,759 | Project-authored serialized integration manifest | `00190a936b1ee77d087474278f7a832ada0ec30784856b6aa437872bc654845b` |
| `AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json` | 16,973 | Project-authored USGS 3DEP public-domain provenance and output inventory | `1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5` |
| `AerialSceneryV1/Lidar/KBDU_Lidar_NearTerrain_Fixed.obj` | 3,451,412 | Deterministic derivative of USGS 3DEP `CO_DRCOG_1_2020`; United States government work/public domain | `4689de66d4b47bdfc1a3ea6e8c33e906d4308bbb77317f23e3dc5185b5cefce8` |
| `AerialSceneryV1/TerrainIntegration/KBDU_Lidar_NearTerrain_UnityLocal_V1.asset` | 3,335,929 | Project-authored Unity serialization of the same USGS 3DEP derivative | `00d92d21960f21b2e270f8f610a278d068e0bc70ee8c02f0441d389d879d33e6` |
| `AerialSceneryV1/TerrainIntegration/KBDU_AirportPatch_LidarConformed_V1.asset` | 1,344,814 | Project-authored visual-boundary derivative of the existing USGS-derived airport patch and fixed lidar mesh | `6ce5895bb3b605db306bb8bd132259cf81285608928b26838d7e4977a20774cd` |
| `AerialSceneryV1/Vegetation/SOURCE_MANIFEST.json` | 9,053 | Project-authored inventory; geometry/presentation are project-owned and distribution/handoff outputs derive from public-domain USGS 3DEP lidar | `2f95980df1b3b952ecee2f3564a216e8e7840c9dc1c57bf2a8b5906b037513bc` |
| `AerialSceneryV1/Buildings/KBDU_BuildingSpatialContractV1.json` | 2,278,923 | Bounded OpenStreetMap ODbL-1.0 derivative; retain `© OpenStreetMap contributors` | `dbb4ad1db8051e34b6948a72b69257bb83fd80fd38daf83a89806cea2be3d87a` |
| `AerialSceneryV1/Buildings/KBDU_BuildingBatchReplacementV1.json` | 136,195 | Project-authored replacement mapping for the OSM-derived building representation | `6275afa651698fe064d8f05bb15ff42e5943969dcbecfeff95755cf2c7b1b7a7` |
| `AerialSceneryV1/Buildings/KBDU_BuildingRuntimeAssetInventoryV1.json` | 12,640 | Project-authored per-asset inventory for OSM-derived building runtime content | `d3edbd92e9a39277b1b091cb07fd223ec49acd5b32e258fdf8ecf98085d12061` |

`KBDU_BuildingRuntimeAssetInventoryV1.json` is the canonical 24-asset hash
inventory. It records relative path, byte count, SHA-256, source, and license for
23 generated building meshes plus their prefab: 24 assets, 6,455,715 bytes, all
verified byte-for-byte. The runtime geometry is an OSM ODbL-1.0 derivative and
retains `© OpenStreetMap contributors`; Unity
serialization and presentation are project-authored. The building spatial
contract covers 27,679 features, with 9,695 represented by geometry, 17,984
ordinary far features represented only by the licensed orthophoto, and zero
lidar height overrides.

The vegetation manifest records `external_assets` as empty and inventories the
project-authored geometry, Blender source, atlas, shader, material, prefab,
configuration, and every generated output with exact hashes and terms. Its 22
near instances, 248 mid clusters, 323 canopy components, and distribution and
handoff maps are deterministic derivatives of the public-domain lidar source;
they do not establish botanical or surveyed accuracy.

The standalone replacement-building prefab has no dependency on the older
combined prefab. The product package closure still includes that older prefab
through `ProductionEnvironmentRoot.prefab`, plus 57 legacy building meshes
(including 12 older 4–6 km LOD assets), even though its 20 building groups are
serialized inactive. Those dependencies remain governed by the existing
`PhotorealTerrainV1/KBDU_ContextAssetsManifest.json` OSM/project-authored terms;
their inactive state does not remove either their package presence or license
obligations.

Raw EPT/LAZ nodes, WESM downloads, source archives, processing caches, simulator
recordings, screenshots, builds, and APKs remain external and must not be
committed. The Aerial Scenery V1 license record makes no FAA, navigation,
training-credit, certified-fidelity, production-photorealism, compatibility, or
Quest-performance claim.

## Aerial Scenery Recovery V2 derivative records

Recovery V2 introduces no new external model or imagery provider. It reuses the OSM ODbL-1.0 building records, public-domain USGS 3DEP canopy products, the existing project-authored vegetation family, and the public-domain NAIP source recorded above. Current deterministic record hashes are:

- building spatial contract: `132cf7f768621becc0dab60b976f3d777b17f03323fc8f9f29bfd9d50dde4521`;
- building replacement map: `14c46122204b627e546bb567ad4308b783311a3d500d5ef7dca34804405aeb4e`;
- building runtime inventory: `1c9f555a5dfbef345842fdb7ab63e55c19b92db0e64fcf4cffb4bf6373796d11`;
- vegetation manifest: `315076dff38bb58c8ae23f05cd65aeb3953fbe730c61ccb785d47de8ffa1db49`.

That historical Recovery V2 representation contains 2,875 fixed closed building shells and 24,804 orthophoto-only building features. Its vegetation contains 22 Tier 0/1 placements in two batches; Tier 2/3 are orthophoto plus canopy-map representations. Those counts describe the accepted baseline dependency closure, not the later Photoreal Aerial Surface V1 candidate below. No raw OSM response, lidar point cloud, decoded NAIP raster, build, capture, or cache is committed.

## Standalone Scenery Baseline V1 integration record

The baseline integration introduces no new external visual asset, provider, or license. It promotes the validated Recovery V2 dependency closure unchanged. `Kloppenheim03_PureSky_2K.hdr` is normalized to the repository's existing Git LFS rule without changing its decoded bytes or rewriting history; the historical non-LFS blob is a bounded exception. The content SHA-256 remains `ffd28511add85d9508b51f50d3bbe4d822f4a5240800edb4f014edb75abdb1aa`. Future matching HDR revisions must use LFS.

## Photoreal Aerial Surface V1 records

This section records the rights and deterministic lineage of the tagged development baseline. It does not establish literal photorealism, navigation suitability, release acceptance, or measured Quest performance.

### City of Boulder 2020 aerial imagery

The selected visual-context source is the City of Boulder ArcGIS item `22b7c93a74394696824258f807f345ba`, titled `2020 Imagery`. The official item identifies Pictometry International as producer, describes 9-inch orthogonal imagery flown in Spring and Summer 2020, and carries an explicit Creative Commons CC0 1.0 Universal link. The source credit retained for provenance is `City of Boulder; source imagery produced by Pictometry International (2020)`. CC0 does not imply City of Boulder or Pictometry endorsement.

| Pinned source record | SHA-256 |
| --- | --- |
| ArcGIS item response | `b7f17522b698a8657e85b9b750bc5ca5e0e388976ac915907a13ccb76b52f4b0` |
| normalized ArcGIS item contract | `aafc729bdb6dd666d003912b9db06025e1579e144733b1c7e4a49ce4ffd58663` |
| map-service metadata response | `ddecdb4e09f830ce2f6e57a13b2bc5db64c5c7dae8e1adf99d716aca9580da80` |
| normalized map-service contract | `177a053bdbc7ecd3484c21e6d39ecf7ca18ea810ab750f76e7ebfe48bad19c79` |
| final canonical source snapshot | `6fa6baea9eb2d876577da883d88b029e018a75dad9728f3faad13bed76ead02b` |
| ordered source tile set (5,551 tiles) | `41c88c222bae40db5ba061332e2919e99b682015c7ce7bc203f0c7d85c8a6906` |

The official item, service, CC0 URL, bounded source-tile contract, processing settings, derivative hashes, and prohibited-source checks are pinned in `QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_AerialSurfaceV1_SourceManifest.json`. Raw ArcGIS responses, the complete source snapshot, and cache tiles remain external and are not committed.

| Optimized runtime derivative | Runtime extent / resolution | SHA-256 |
| --- | --- | --- |
| `KBDU_City2020_Near4km_8192.jpg` | 4 km, 8192², 0.48828125 m/texel | `143c4946c06464dbdea1567cd5fe284acc4c5ba7718a38900a2e83a8eccbfb53` |
| `KBDU_City2020_MidBase12km_8192.jpg` | 12 km, 8192², 1.46484375 m/texel | `f54243142070533985202d5aa656516008b290e1005f82b5237a4bc8fd82f110` |
| `KBDU_City2020_MidNW_2048.jpg` | bounded mid cell, 2048², 1.984375 m/texel | `8337f2567ae444cc01f1dfae2c10e4eedb41ec3baf7531ba7b68f311939e8601` |
| `KBDU_City2020_MidNE_2048.jpg` | bounded mid cell, 2048², 1.984375 m/texel | `74316eda5c927a74cf70ead1d9f818ffaf274d6515356a587b37f538a64dbfb4` |
| `KBDU_City2020_MidSW_2048.jpg` | bounded mid cell, 2048², 1.984375 m/texel | `b56bd374dc98f6e4daa1e63b7f236d8ee0aed60ecad550d0404776431f7c82ac` |
| `KBDU_City2020_MidSE_2048.jpg` | bounded mid cell, 2048², 1.984375 m/texel | `3f0ddf836a191b28e775c40788eaf7427450f06acd2a4e020817672978103fb0` |
| `KBDU_City2020_Far24km_4096.jpg` | 24 km, 4096², 5.859375 m/texel | `222cebe04b7ad7d09198fca2a1ba39a2581286b448d5611c53723d53a578457b` |

The runtime water policy is deliberately `orthophoto_only`: reservoir and minor-water pixels use the same selected aerial surface as adjacent terrain. This candidate introduces no water renderer, tint, transparency, reflection, animation, or extra water draw.

### USGS 3DEP semantic-surface derivatives

The terrain and elevated-surface inputs derive from USGS 3DEP work unit `CO_DRCOG_1_2020`, a United States government work/public-domain source. The canonical source inventory is `AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json` (SHA-256 `1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5`). Raw EPT/LAZ nodes and processing caches remain external and are prohibited from Git.

| Compact derivative | Semantic | SHA-256 |
| --- | --- | --- |
| `KBDU_Lidar_DTM_R16.png` | bare-earth digital terrain model | `d5e703ea15af0c3bdd7a6280d68b56addcda1c481639cb09adef5dd9b18cb745` |
| `KBDU_Lidar_DSM_R16.png` | digital surface model | `01622d3c3703de6e7dcb70587b04fc5409ccde83b45f37f97f612981de6bf8dc` |
| `KBDU_AerialSurface_nDSM_R16.png` | normalized height above ground | `7c05d9e7d94264b1d64f241570ab687c51d38a8e5129652d51e463577d8cc961` |
| `KBDU_AerialSurface_SurfaceClass_R8.png` | deterministic surface-class/validity flags | `a531df9852cc4794eb3a012888880fbdcd469df68c42c5cd5ebeda824c22a686` |
| `KBDU_ProductAeqd_BareEarth_Fixed.obj` | fixed product-AEQD bare-earth authoring mesh | `cfdefa0c60f0432c4cbc5a52597b85a16a8afb8c3a7f3f09c6be0d0ed2f74ce4` |

The fixed terrain has 46,426 vertices and 92,000 triangles. Its exact frame conversion, runway preservation, mesh-to-raster error, and public-domain lineage are pinned in `PhotorealAerialSurfaceV1/Terrain/KBDU_ProductAeqd_BareEarth_Manifest.json`; those measurements do not establish surveyed real-world accuracy.

### OSM footprint and generated semantic meshes

Building semantics use bounded OpenStreetMap footprints under ODbL-1.0 and retain `© OpenStreetMap contributors`. The compact source authority is `ReconstructionV2/KBDU_ReferenceContextV2.json` (SHA-256 `584a47ebde144760bb70c4f4a20bfd58baa591e368a1093164cc464506a8f104`); the raw Overpass response remains external. The current building-surface manifest covers 9,692 non-hero footprints in the 0–4 km semantic layer, while two additional airport-core footprints remain explicitly owned by authored hero objects. Sixteen closed, footprint-preserving batches contain 128,092 triangles. Heights prioritize OSM tags and deterministic rules; only 28 bounded candidates use non-authoritative lidar support, and no surveyed-height or 10-percent vertical-error claim is made.

The canopy layer is not a sparse 22-tree representation. Its final policy is terrain-map-only outside the existing hero radius: 63,308 eligible pixels are handed to terrain shading, 144 source-eligible pixels remain in the separately authored hero radius, and the semantic layer owns zero raised canopy batches, zero raised triangles, and zero sparse individual-tree instances. Building/airport/water/road exclusions and two exact reviewed false-positive exclusions constrain the public-domain 3DEP-derived field. Because the source work unit contains no LAS vegetation or building classes, the return-structure classification is a deterministic visual heuristic, not a botanical inventory or surveyed semantic truth.

One deterministic raised-canopy candidate is retained only as rejected-design evidence in the canopy manifest: 29 retained components and 1,960 triangles if emitted. It is inactive and was rejected after inspected QA because its silhouettes read as blocks and cliffs. It may not enter the runtime dependency closure.

Generated OBJ authoring inputs are converted offline to coordinate-preserving Unity-native mesh assets for the 16 accepted building batches and one terrain mesh. The final semantic runtime owns no raised canopy native mesh. `PhotorealAerialSurfaceV1/KBDU_AerialSurface_UnityLocalMeshManifestV1.json` inventories accepted per-source and per-native hashes and verifies that the generic OBJ importer is not the runtime authority. Project-authored serialization, shaders, materials, masks, and conversion tools do not alter the ODbL boundary of the building footprints or the public-domain status of the 3DEP derivatives. Source archives, raw downloads, decoded working rasters, builds, captures, APKs, and caches remain uncommitted.

## Rounded Canopy Volume V1 derivative record

Rounded Canopy Volume V1 introduces no new external asset, imagery provider, or
license boundary. Its Blender source, exported `AerialGroveCluster_T2` mesh,
atlas, material presentation, shaders, authoring code, and configuration are
project-authored. The 42 fixed-world cluster locations and their height/density
parameters are deterministic derivatives of the public-domain USGS 3DEP source
recorded by `AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json`, SHA-256
`1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5`.

The canonical current inventory is
`AerialSceneryV1/Vegetation/SOURCE_MANIFEST.json`: 9,197 bytes, SHA-256
`edeb44cdeb7b7a39283b984acdb799188be1dce6ad87c01febcf14437e1a3ad1`.
Its `external_assets` list is empty. The `.blend`, `.fbx`, and raster derivatives
are governed by the repository's existing Git LFS rules; every committed asset
retains its paired Unity `.meta` file. No new third-party notice is required.

The 3DEP work unit lacks vegetation classes 3/4/5 and building class 6, so its
canopy placement is a deterministic visual heuristic rather than botanical or
surveyed truth. Raw EPT/LAZ, downloads, processing caches, rejected grove
experiments, screenshots, simulator recordings, builds, and APKs remain
external and must not be committed.

## Quest Visual Fidelity V1 derivative record

Quest Visual Fidelity V1 introduces no new third-party asset or license boundary. The height-calibrated grove distribution, facade semantic mapping, native vertex-color streams, procedural opaque facade response, cockpit-window response, and baked-occlusion calibration are project-authored derivatives of already recorded inputs.

Building semantics continue to derive from bounded OpenStreetMap footprint/tag data under ODbL-1.0 with `© OpenStreetMap contributors`. Canopy placement continues to derive from the public-domain USGS 3DEP work unit `CO_DRCOG_1_2020`. Facade pixels use the existing documented project/CC0 building atlas; no new texture, photograph, archive, model, or external source was downloaded or committed.

The regenerated Blender, FBX, OBJ, Unity-native mesh, prefab, configuration, shader, and manifest files retain their paired Unity metadata and established source/license records. Raw geospatial data, caches, screenshots, simulator evidence, build outputs, and APKs remain external to Git.

## Worn Visual Runtime V1 near-foliage derivative

The near-foliage runtime atlas is a project-owned generated output with no
third-party reference image input. Its exact prompt, generation time, source
and derivative hashes, dimensions, chroma-key processing, import contract, and
raw-source exclusion are recorded in
`PhotorealAerialSurfaceV1/Canopy/ProductionNearFoliageAtlas_SourceManifest.json`.
The committed RGBA derivative is
`Canopy/Textures/ProductionNearFoliageAtlas_RGBA.png`, SHA-256
`aae5ad67afc5e63dc0ce762bcae1bac819ebd7403dcd5d922af2fc1e474c671c`.
The unprocessed chroma source remains artifact-only and is not committed.

Atlas use does not alter the public-domain status of the 3DEP/NAIP placement
inputs or the ODbL boundary of the OSM building footprints. The project-owned
static cards add no new renderer, material, transparent slot, runtime download,
or camera-facing billboard. Rejected canopy iterations, captures, build output,
and APKs remain outside Git.

The cockpit stable-interior shader and its controller/authoring changes are
project-authored source. They reuse only the already audited aircraft textures
and existing CC0/project reflection inputs, add no downloaded texture or model,
and remove the rejected overlay mesh/material/shader from the product closure.
# Airport hero corridor V1

- Geometry and Blender source: project-authored.
- Footprints: OpenStreetMap contributors, ODbL-1.0, through the pinned KBDU reference context.
- City of Boulder hangar pages: factual/reference use only; no City photographs are committed.
- Source Blender SHA-256: `15e2223ad29d71332e8b994203ec403ff527a12cf957012917135f8db1c52549`.

## Airport Parked Aircraft V1

The powered-aircraft scenery derivative continues to use the audited
KOG_THORNS `Cessna172` under CC BY 4.0. The new sailplane source is
`Glider Flying` by app4soft / EGPJET from
[OpenGameArt](https://opengameart.org/content/glider-flying), published under
CC0 1.0. Raw sailplane sources remain external.

| Asset | Source / terms | SHA-256 |
| --- | --- | --- |
| `KBDU_ParkedC172_FixedDetail.glb` | Exterior-only static derivative of KOG_THORNS `Cessna172`; CC BY 4.0 | `2c2c8154343d24dd093c878c1f94b33ba7a71445eb886fbf93d119452df7e391` |
| `KBDU_ParkedSailplane_FixedDetail.glb` | Exterior-only static derivative of app4soft / EGPJET `Glider Flying`; CC0 1.0 | `cb69e85643cdefc3a90f4376f98f4e6d19319117a94268ad09ada9382a9bb50b` |
| External `FFGL.blend` | app4soft / EGPJET; CC0 1.0; not committed | `c367729605b0d67d1d989d18be1ca92b0279037006110858f1e7fc00909cd077` |

The deterministic conversion removes interiors, animation, source materials,
source textures, and interaction; joins, triangulates, decimates, ground-pivots,
and vertex-colors the retained exterior geometry. Exact byte counts, source
URLs, Blender version, LOD inventory, and change notice are recorded in
`AirportParkedAircraftSourceManifestV1.json`. CC BY 4.0 does not grant
Cessna/Textron trademark rights or imply endorsement.

### Bounded road-traffic derivatives

The traffic route compiler uses the bounded road network in the already
committed `KBDU_ReferenceContextV2.json` OpenStreetMap derivative. Route data
remains under ODbL 1.0 and public display must retain
`© OpenStreetMap contributors` with
<https://www.openstreetmap.org/copyright>.

The parked-vehicle authority is derived offline from the City of Boulder
`AP2020Cached9inWM` ArcGIS service at level 19
(`0.228595274 m/px`). The City item identifies the imagery as nine-inch 2020
orthophotography and applies CC0 1.0 Universal. Source tiles and caches remain
external and are not committed. The committed derivative contains only
27,948 compact positions, source-tile/pixel axes, OSM building associations,
confidence values, and source/license metadata. Sixteen detections outside the
actual 12 km product-image extent are excluded.

GeoDeep 0.9.13 and its
`car_aerial_detection_yolo7_ITCVD_deepness.onnx` model were used only as an
external AGPL-3.0 offline authoring dependency. Neither GeoDeep, its ONNX model,
nor its code enters the Unity runtime or the committed product dependency
closure. The model SHA-256 used for the scan is
`4875d27f34985a080c4def32bc9408b9820e4178e55acdc5fcc185e0e8f29a56`.
The deterministic project-owned semantic pass rejects detections on OSM roads,
highways, aeroways, water, and building footprints before the compact derivative
is produced.

The five optimized runtime vehicle silhouettes—sedan, hatchback, crossover/SUV,
pickup, and cargo van—derive from RGSDev's
[Free Low Poly Vehicles Pack](https://opengameart.org/content/free-low-poly-vehicles-pack),
dedicated under
[CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/).
The raw archive and FBX sources remain external. Blender 5.1.2 normalizes scale,
reduces geometry, removes source materials, joins each model to one submesh,
and encodes body, glass, tire, wheel/trim, headlamp, and tail-lamp classes in
vertex colors. One project-owned 128-square detail texture and one instanced
opaque material serve every model.

Exact source/archive hashes, per-derivative triangles and dimensions, native
source pixels/axes, OSM network lineage, paint/fleet weights, and runtime limits are pinned
in `tools/kbdu_traffic/traffic_vehicle_derivatives_v1.json` and
`tools/kbdu_traffic/traffic_sources_v1.json`. The compiler and validators are
`tools/kbdu_traffic/detect_parked_vehicles.py`,
`tools/kbdu_traffic/build_traffic_manifest.py`,
`tools/kbdu_traffic/validate_traffic_manifest.py`, and the Traffic V1 EditMode
suite. CC0 attribution is retained as provenance and does not imply RGSDev
endorsement.

### KBDU airport perimeter fence

The placement authority is derived from OpenStreetMap KBDU aerodrome way
`77513664`, licensed under ODbL 1.0 with `© OpenStreetMap contributors`
attribution. The raw OSM snapshot remains external; the committed compact JSON
contains only the reviewed boundary vertices, section contracts, source hash,
and attribution.

City of Boulder planning documents and airport imagery were used for factual
and visual research. A Wikimedia Commons CC BY 4.0 airport photograph and an
Airport-Data photograph were inspected as references. Alex also supplied an
external green privacy-slat chain-link photograph for visual direction. No
reference photograph is committed, redistributed, or sampled into runtime
textures. The four fixed meshes, fence material/shader logic, authoring code,
and tests are project-authored. Exact public URLs and hashes are recorded in
`docs/KBDU_AIRPORT_PERIMETER_FENCE.md` and the milestone evidence.

### Boulder Cloud Field V1

The cloud meshes, shader, material, placement profile, and authoring code are
project-authored. `KBDU_CloudImpostorAtlasV3.png` was generated specifically
for this project with OpenAI image generation on 2026-08-10; it contains no
downloaded photograph, model, logo, or watermark. The committed PNG has
SHA-256 `a4bf9047b4427f71e902cb6aa1b5c3ff1296e29f4f5d0dd685be5ecc4548dd82`
and is imported as a maximum-512-square, mipmapped, streaming Android ASTC 6x6
runtime texture. Prompt, tool, source URLs, runtime policy, and license record
are preserved in `Assets/Production/Environment/CloudsV1/SOURCE_AND_LICENSE.md`
and `SOURCE_MANIFEST.json`. No raw download or third-party cloud photography is
committed.

### Golden Eagle thermal cue

The bounded soaring-raptor derivative uses `Animated Eagle` by Clement Abai /
Asim3d from [Sketchfab](https://sketchfab.com/3d-models/animated-eagle-de73b663acbc426e8e6c107fd25e08cd),
published under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
The raw glTF remains external. Blender 5.1.2 normalizes its measured
extended-wing pose to a 2.1 m wingspan, preserves the complete 208-frame flight
animation, and authors soar, bank, and transition clips. Project-owned offline
derivatives recolor the bald-eagle head/tail islands and add restrained
feather-scale normal and roughness response while retaining one skinned
renderer and one material. Exact source/derivative hashes and the
change notice are recorded in
`Assets/Production/Environment/Wildlife/SOURCE_MANIFEST.json`.
