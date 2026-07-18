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

The standalone environment uses three aligned, bounded natural-color derivatives from the official USGS NAIP Plus ImageServer. The source catalog records USDA-FSA APFO imagery acquired on 2019-08-03 and 2019-09-19 at 0.6 m source resolution. United States government works are public domain; courtesy credit is `USGS, USDA, The National Map: Orthoimagery`.

| Runtime derivative | Coverage / import | SHA-256 |
| --- | --- | --- |
| `KBDU_NAIP_2019_Near4km_4096.jpg` (6,320,221 bytes) | 4 km × 4 km, 4096², ASTC 6×6, mipmapped/streamed, anisotropy 8, mip bias `-0.15` | `446e5c7fa13fc653de1f3e8404829c0e54b7e919fe99ebfba79d747a0b1dadea` |
| `KBDU_NAIP_2019_Mid12km_4096.jpg` (7,273,113 bytes) | 12 km × 12 km, 4096², ASTC 6×6, mipmapped/streamed, anisotropy 8, mip bias `-0.15` | `5e8d6edbcc5dc937c78b5fb8e4a5f8f7a06bda726b4509684e237f2b17fbdf9a` |
| `KBDU_NAIP_2019_Far24km_2048.jpg` (1,705,067 bytes) | 24 km × 24 km, 2048², ASTC 6×6, mipmapped/streamed, anisotropy 8, mip bias `-0.10` | `799be878d5c6be73b5596cbea70ee4e122859a528231608e40796f1020617b33` |

Exact raster IDs, corrected requested/returned extents, raw external hashes, processing settings, near-to-mid and mid-to-far color-match statistics, output hashes, and the no-Google confirmation are pinned in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`. The three current Android ASTC mip-chain estimates total 22,420,784 bytes. The earlier `KBDU_NAIP_2019_Mid12km_2048.jpg` derivative remains rights-cleared and tracked, but is superseded and is not bound by the current terrain-pyramid material.

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

The reservoir retains the OSM way 35597714 shoreline (`© OpenStreetMap contributors`, ODbL-1.0). Its two normal maps are deterministic project-authored mathematical textures. The packed mask is a project-authored rendered derivative of the OSM shoreline; its depth channel is artistic distance-to-shore data, not surveyed bathymetry.

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

## Exclusions

- No Google Maps, Google Earth, or Street View content.
- No raw NAIP/orthophoto downloads or lossless processing intermediates; only optimized, provenance-recorded public-domain derivatives are committed. The current material binds the three-level Near4096/Mid4096/Far2048 pyramid above.
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

The rejected V4 `LightingData.asset`, bake manifest, two color lightmaps, and two directional lightmaps were deleted from the product tree by the V4.1 fail-closed quarantine. Two replacement bake attempts were also rejected and deleted because each exceeded the bounded V4.1 one-atlas remediation budget. Their logs remain external under the V4.1 artifact root; no V4.1 lightmap is accepted or committed.

V4 originally produced 55 LFS-covered binaries. V4.1 deletes the four rejected baked-lightmap textures and retains 51, all with paired Unity metadata. The existing non-LFS historical 5.43 MiB HDR blob remains a bounded policy exception; this branch does not rewrite history. No V4 raw download, build, APK, capture, or cache is committed.

## Environment Reconstruction V2 derivatives (feature branch)

- Terrain: USGS 3DEP 1 m bare-earth DEM, United States government work/public domain. External raw payload: 67,117,862 bytes, SHA-256 `93b32e8ef2fef464ff4ee49702d235b30eaff7d347f22b8c4add8848cd7e9d82`. Committed derivatives are the 5 m airport mesh, 25 m near mesh, terrain-surface asset/manifest, and 1024² normal map (`4ba7833e6f27f8ee3abceb79caf502e53f09672525038eedea7d89df6d764cc8`).
- Buildings and water source features: bounded OpenStreetMap snapshot dated 2026-05-31, ODbL-1.0, `© OpenStreetMap contributors`. Raw building response remains outside Git; SHA-256 `024560100b28946bb805478c6be091d752172200e7c8fcbe1ebe1d7bea858416`. Committed JSON/mesh derivatives retain stable OSM feature IDs and license class.
- Vegetation mask: public-domain USGS/USDA NAIP natural-color fallback, derived from committed Mid12km imagery SHA-256 `5e8d6edbcc5dc937c78b5fb8e4a5f8f7a06bda726b4509684e237f2b17fbdf9a`. External decoded working raster SHA-256 `9a565142b8d79c3029149f7dd36e15ad95688f61b67637e80ecc3c08618e3158`. Committed mask hashes are recorded in `vegetation_source_v2.json`.
- Vegetation meshes: four procedural species and their 12 OBJ LODs are project-owned original work. The shared MTL SHA-256 is `9bc39f427446720ec729dd82b994273b77eefab2cd808dc4aed8d36d78751884`.
- Parked aircraft reuse the already-audited KOG_THORNS CC BY 4.0 derivative and attribution above. V2 changes placement/ground contact and adds a third LOD; they do not change the license boundary.
- No raw DEM, raw OSM response, decoded NAIP raster, build, capture, cache, or package artifact is committed.
