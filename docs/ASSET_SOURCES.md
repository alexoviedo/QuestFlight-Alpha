# Product Asset Sources

This file records only assets present in the clean product dependency closure. License terms and binary hashes are recorded in `docs/ASSET_LICENSES.md`, `THIRD_PARTY_NOTICES.md`, and `tools/kbdu_environment/kbdu_source_manifest.json`.

## Aircraft

- Asset: Cessna 172 by KOG_THORNS
- Original page: https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4
- License: Creative Commons Attribution 4.0 (CC BY 4.0)
- Product path: `QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb`
- Purpose: cockpit and exterior visual baseline
- Caveat: the license covers the model copyright, not Cessna/Textron trademarks or endorsement. Public commercial release requires a branding review.

The flyable product aircraft is neither runtime-generated nor substituted. The imported asset is tracked with Git LFS and its exact hash is verified by `scripts/sync_local_aircraft_asset.ps1 -VerifyOnly`. Separately authored parked-aircraft scenery is non-interactive environment context and never replaces the flyable product aircraft.

### Cockpit-visible original cowling derivative (2026-09-22)

- Derivative: `QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Meshes/C172_CockpitOriginalEngineCowling.asset`.
- Source: the recorded KOG_THORNS C172 above, CC BY 4.0; source SHA-256 `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`.
- Changes: `CockpitEngineCowlAuthoring.AuthorExteriorBatch` transforms the original `Cessna_Exterior_Body_MAT_0_Body_MAT_0` mesh into cockpit coordinates and clips triangles at local `z=0.46 m`, preserving source surface positions, interpolated normals, and UV channels. Tangents and bounds are regenerated. The 2,520-triangle, 1,740-vertex derivative reuses the original exterior material/textures; the existing dark liner and imported source asset are unchanged. It is enabled only by the cockpit visibility profile, avoiding overlap with the complete external-view fuselage.
- Reproduce the mesh in Unity: `QuestFlightLab.Editor.CockpitEngineCowlAuthoring.AuthorExteriorBatch`. For product integration, `QuestFlight.Editor.DemoFinishAuthoring.RunBatch` authors the aircraft first and then serializes the new renderer into the startup showroom suppression list. No runtime mesh construction occurs.
- Derived asset SHA-256: `e7b7c906901bebbcf0b95e19002483aff4fba0880d3249c838ad011d18ee3bef` (228,670 bytes). KOG_THORNS attribution and this modification notice apply to the derivative.

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
  - source: `https://imagery.nationalmap.gov/arcgis/rest/services/USGSNAIPPlus/ImageServer`
  - use: bounded 2019 natural-color macro imagery for the aligned near 4 km, mid 12 km, and far 24 km zones
  - status: USDA-FSA/USGS United States government work / public domain
  - current product derivatives: one 4096×4096 near atlas, one color-matched 4096×4096 mid atlas, and one color-matched 2048×2048 far atlas
  - raw policy: source exports, lossless intermediates, service receipts, and previews remain outside Git

Committed compact derivatives are:

- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json`
- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json`
- baked production meshes and the deterministic macro albedo under `QuestFlight/Assets/Production/Environment`

The exact source requests, timestamps, hashes, licenses, and geometry budgets are pinned in `tools/kbdu_environment/kbdu_source_manifest.json`. The environment is KBDU-inspired and not for navigation.

The base imagery lineage is pinned in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json` and documented in `docs/KBDU_ORTHOPHOTO_PROVENANCE.md`. Aerial Scenery Recovery V2 adds four 2048² 1.984375 m/texel hot quadrants (ASTC 6×6) and one 4096² 2.9296875 m/texel 12 km fallback (ASTC 10×10), all derived from the same official public-domain 2019/0.6 m source. Their exact source requests, processing, hashes, and world-space blend contract are in `QuestFlight/Assets/Production/Environment/AerialSceneryRecoveryV2/Ground/KBDU_GroundImageryRecoveryV2_SourceManifest.json` and `KBDU_GroundImageryRecoveryV2_Contract.json`. No Google-derived or AI-upscaled imagery is used.

## Photographic Sky

- Asset: Kloppenheim 03 (Pure Sky), original by Greg Zaal with sky edits by Jarod Guest
- Official page: https://polyhaven.com/a/kloppenheim_03_puresky
- License: CC0 1.0
- Raw source: official 4096×2048 HDR download; retained only in the external milestone artifact tree
- Product derivative: `QuestFlight/Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr`
- Processing: exact 2×2 linear-radiance box reduction to 2048×1024 Radiance HDR, without tone mapping or clipping
- Purpose: skybox, ambient environment, and static custom reflection; retained but not currently bound as a reservoir reflection source on the orthophoto-only recovery branch

The selected-source hash, raw-source hash, runtime hash, authorship, official URLs, processing tool, Unity import settings, and external evidence hashes are pinned in `QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json`.

## CC0 Ground Detail

Three 1K diffuse maps from Poly Haven are used as Quest-bounded microdetail. Poly Haven publishes them under CC0 1.0: https://polyhaven.com/license.

| Runtime use | Asset | Author | Source |
| --- | --- | --- | --- |
| dry prairie | Withered Grass | Charlotte Baglioni | https://polyhaven.com/a/withered_grass |
| sparse green grass | Sparse Grass | Amal Kumar | https://polyhaven.com/a/sparse_grass |
| dry soil | Dry Ground 01 | Rob Tuytel | https://polyhaven.com/a/dry_ground_01 |

The product copies only the optimized 1024×1024 maps. Android imports use mipmaps, trilinear filtering, anisotropy, and ASTC compression. No raw archives are committed.

## CC BY Parked-Scenery Adaptation

Three static parked-aircraft scale cues use the separately reviewed KOG_THORNS source under CC BY 4.0. The original flyable-aircraft GLB remains unchanged. The committed scenery derivative retains only exterior body geometry, removes cockpit/interior/glass/textures/source materials/branding presentation/animation, and provides 3,200- and 900-triangle source LODs. Three serialized placements are offline-combined per LOD, use one shared opaque material, and add one active draw slot. Attribution, the change notice, exact hashes, and the no-endorsement caveat are pinned in `QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_ParkedAircraftSourceManifest.json` and the context manifest.

## Project-Authored Derivatives

- Fixed terrain, runway, marking, retained water-source, and context meshes
- `ProductionKbduMacroAlbedo.png`, generated deterministically from the pinned elevation and land-cover derivatives
- Production ground and runway-marking shaders; water shaders are retained source but inactive on the recovery branch
- `KBDU_AirportKit.glb` and its shared atlas, generated by the committed Blender script from project-owned geometry
- project-authored runway albedo/metallic-smoothness textures generated by the committed deterministic Python script
- project-authored `KBDU_ContextAtlas.png`, vegetation meshes, context shaders, materials, and batching recipe
- two retained project-authored reservoir normal maps and one packed shore/depth/roughness texture; the packed map derives from the OSM shoreline and retains OSM attribution, its depth channel is artistic rather than surveyed bathymetry, and none is actively bound on the recovery branch
- Unity scene, prefabs, materials, scripts, QA views, tests, and build tooling

The recovery branch covers 27,679 OSM Produced Work records with 2,875 fixed closed footprint-preserving shells and 24,804 ordinary features represented by orthophoto only. Hero/near shells retain original footprints; 1,697 mid shells may use bounded simplification to six vertices while preserving exact bounds and ±10% area. The shells are offline-combined into 16 renderers, one shared opaque material, and no runtime LODGroups. Vegetation uses 22 Tier 0/1 placements in two batches; Tier 2/3 use the orthophoto plus public-domain lidar canopy maps. The product makes no unsupported GPU-instancing claim for these batches.

The legacy macro albedo remains a synthetic land-cover derivative. The current near/mid/far runtime atlases are bounded public-domain NAIP derivatives and are not current navigation data or a production-photorealism claim. No Google Maps, Google Earth, Street View, paid, or unclear-license content is included.

## Airport terminal finish (2026-09-22)

This correction reuses the project-authored `ExecutiveTerminal` LOD meshes and existing `AirportCoreTerminalAtlas` textures. Texture source/license/hash records remain in `QuestFlight/Assets/Production/Environment/AirportCoreV4/Materials/SOURCE_MANIFEST.json`; no new photograph or texture pixels are included in the product.

The source specification (`tools/kbdu_airport_core_v4/buildings/airport_core_building_spec.json`) places the terminal facade toward the east apron. The imported geometry had reversed that direction. The [City airport address map](https://bouldercolorado.gov/airport-hangar-addresses) identifies building 3335, and the [City 2020 orthophoto service](https://gis.bouldercolorado.gov/ags_svr1/rest/services/raster/AP2020Cached9inWM/MapServer) shows its apron-side overhang on the east and vehicle parking on the west. The inspected reference crop remains external at `T2-QuestFlight-product-artifacts/demo_finish_20260922/terminal_city_2020.jpg`; local ENU extent is x=-440..-325 m, z=-280..-170 m. It is reference evidence only.

Reproduce with `QuestFlightLab.Editor.AirportTerminalFinishAuthoring.AuthorBatch`, then `QuestFlight.Editor.ProductLightingRefreshBatch.RunBatch` to refresh the existing bounded lightmap. The terminal parent receives a 180-degree yaw at the unchanged source footprint; all three LODs inherit it. `AirportCoreV4Authoring` also preserves the corrected rotation on regeneration. The terminal material alone enables `_MetricTerminalWalls=1`: atlas tile 0 repeats every 1.4 m and tile 1 every 4 m on vertical walls using a single dominant wall-plane projection. The mapping preserves the source 0.012 atlas gutters and computes mip gradients before wrapping. Other atlas tiles and non-wall faces retain original coordinates. There are no added meshes, textures, draw calls, or texture samples. These material scales are presentation choices, not surveyed masonry dimensions.

## Future Asset Rule

Every committed binary must have a source, license, and hash record. Raw captures, downloads, archives, caches, and unclear-license assets remain outside Git.


### C172 complete pedal linkage repair � 2026-09-23

`Assets/Production/Cockpit/Pedals/C172_FourPedalAssembly.asset` remains a CC BY 4.0 derivative of KOG_THORNS C172 (frozen source SHA-256 `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`). Four full footplates preserve the original texture coordinates. Project-authored fixed bearings and separate rudder/toe hinges replace incomplete source floor hardware. Reference: Cessna 172 service manual Figure 10-2; visual dimensions approximate. Output: 15,440 vertices, 13,024 triangles, one material, nine bones. Mesh SHA-256: `aa13dd4a58df306ca67c7cc8355006657a458b2c82a98880fd43ba7c030ecdb9`. See the paired v2 derivation manifest and [repair evidence](evidence/COCKPIT_CLEARANCE_PEDALS_2026-09-23.md).


## Distant branch cluster candidate (2026-09-28)

Assets/Production/Environment/MapBranchGrovesV1/DistantClusters contains offline captures generated by GroveClusterAuthoring.Bake from project-authored BranchCluster.asset, the project-owned broadleaf atlas and the Poly Haven Pine Tree 01 needle atlas. DistantClusters/SOURCE.json records exact input/output SHA-256 hashes and original provenance records. No new downloaded artwork is used. The 36 reusable templates each have 25 views; two 2048-square atlases use Android ASTC 6x6. These are candidate LOD derivatives, not measured Quest performance or botanical accuracy.
