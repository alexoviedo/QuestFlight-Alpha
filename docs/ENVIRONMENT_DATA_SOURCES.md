# Production Environment Data

The authored product scene uses a bounded KBDU-inspired environment. It contains optimized derivatives only; raw network responses and large source downloads remain outside Git.

## Coordinate Contract

- Origin: FAA KBDU airport reference point, 40.03936527 N, 105.22608958 W, 5288 ft / 1611.7824 m MSL
- Frame: local east-north-up
- Unity mapping: +X east, +Y up, +Z north
- Label: KBDU-inspired / not for navigation

## Retained Sources

- FAA / USDOT BTS: runway 08/26 endpoints, dimensions, heading, grade, surface, lighting, and airport identity; United States government work.
- City of Boulder `2020 Imagery`: bounded CC0-1.0 aerial derivatives produced from the official public map-service item; the item identifies Pictometry International as imagery producer. Raw metadata responses, source snapshots, and cache tiles remain external.
- USGS 3DEP: public-domain DTM, DSM, normalized height, surface-class, terrain, and canopy derivatives. Raw EPT/LAZ nodes and processing caches remain external.
- OpenStreetMap: clipped and simplified airport/context geometry and semantic building footprints; ODbL 1.0, with required attribution © OpenStreetMap contributors.
- USGS/USDA NAIP Plus: bounded 2019 natural-color public-domain derivatives remain the historical baseline and alignment/fallback source. Raw exports and processing intermediates remain external.
- Poly Haven: the `Kloppenheim 03 (Pure Sky)` HDRI supplies the photographic sky and static environment-reflection source under CC0 1.0. The raw 4K download remains external; the optimized 2K HDR derivative and its source manifest are committed.

Exact requests, retrieval timestamps, raw and derivative hashes, licenses, attribution, budgets, and frozen toolchain hashes are in `tools/kbdu_environment/kbdu_source_manifest.json`.

The City 2020 item/service contract, cache snapshot, processing settings, optimized output hashes, and orthophoto-only water rule are in `QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_AerialSurfaceV1_SourceManifest.json`. Its pinned source hashes are: item response `b7f17522b698a8657e85b9b750bc5ca5e0e388976ac915907a13ccb76b52f4b0`, item contract `aafc729bdb6dd666d003912b9db06025e1579e144733b1c7e4a49ce4ffd58663`, service response `ddecdb4e09f830ce2f6e57a13b2bc5db64c5c7dae8e1adf99d716aca9580da80`, service contract `177a053bdbc7ecd3484c21e6d39ecf7ca18ea810ab750f76e7ebfe48bad19c79`, source snapshot `6fa6baea9eb2d876577da883d88b029e018a75dad9728f3faad13bed76ead02b`, and ordered 5,551-tile set `41c88c222bae40db5ba061332e2919e99b682015c7ce7bc203f0c7d85c8a6906`.

Historical NAIP requests, raster IDs, corrected service extents, processing settings, and hashes remain in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`. Recovery source and selection contracts remain in `QuestFlight/Assets/Production/Environment/AerialSceneryRecoveryV2/Ground/KBDU_GroundImageryRecoveryV2_SourceManifest.json` and `KBDU_GroundImageryRecoveryV2_Contract.json`. The batched OSM context license boundary and per-output hashes are in `QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_ContextAssetsManifest.json`; the KOG_THORNS/CC BY 4.0 parked-scenery attribution and change notice are in the adjacent `KBDU_ParkedAircraftSourceManifest.json`. Sky lineage is in `QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json`; retained water-source lineage is in `tools/kbdu_reservoir_water/asset_manifest.json`. The frozen source manifest's earlier NAIP availability-only record remains historical and was not rewritten.

Aerial Scenery V1 retains the canonical source-lidar manifest under `QuestFlight/Assets/Production/Environment/AerialSceneryV1`; Recovery V2 remains the tagged development baseline. The Photoreal Aerial Surface V1 feature candidate adds the bounded City imagery and hybrid semantic products under `AerialSurfaceV1` and `PhotorealAerialSurfaceV1`. This documentation records provenance and current deterministic content only; it does not claim that the candidate has passed final visual, simulator, physical-Quest, or merge acceptance.

## Product Geometry

- Near zone: the Photoreal Aerial Surface V1 candidate uses one fixed 4 km bare-earth mesh sampled from the public-domain 3DEP DTM after exact product-AEQD-to-UTM conversion. It contains 46,426 vertices and 92,000 triangles; its coordinate-preserving Unity-native derivative is generated offline. The mesh-to-raster report does not establish surveyed real-world accuracy, and the authoritative runway collider and physics authority remain unchanged.
- Mid zone: fixed 12 km lower-detail terrain and selected combined context.
- Far zone: one immutable 24 km USGS-derived mesh sampled on a 100 m render grid, with baked normals and sealed inner/outer skirts. It has no `LODGroup`, crossfade, billboard, camera scaling, or runtime regeneration. The 100 m render grid is a disclosed bilinear resampling of the frozen 400 m USGS grid and does not add source elevation detail.
- Runway: one FAA endpoint-derived grade, one offline terrain blend, one pavement mesh shared by rendering and collision, and one controlled-depth marking mesh.
- Water: `OrthophotoOnly`. Reservoir and minor-water pixels use the same City 2020 aerial surface as adjacent terrain. No water renderer, material, tint, transparency, reflection, animation, or extra water draw is introduced. Older OSM shoreline and project-authored water assets remain governed by their recorded terms but are not the candidate presentation authority.
- Macro ground: the candidate uses one aligned City 2020 CC0 pyramid: an 8192² / 4 km near level at 0.48828125 m/texel, four 2048² bounded mid cells at 1.984375 m/texel, an 8192² / 12 km mid base at 1.46484375 m/texel, and a 4096² / 24 km far level at 5.859375 m/texel. Mipmaps, ASTC import estimates, and fixed world-space UV contracts are static planning records, not measured Quest residency or frame-time evidence.
- Airport art: three existing project-authored hangar variants, one fuel/service structure, fencing/signage, and ramp props with shared materials and non-crossfaded LODs.
- Surrounding buildings: OSM footprints are the semantic authority. The candidate represents 9,692 non-hero footprints within 0–4 km as 16 closed, footprint-preserving mesh batches totaling 128,092 triangles; two additional airport-core footprints remain explicitly owned by authored hero objects. Heights prioritize OSM tags and deterministic rules. Twenty-eight bounded candidates use non-authoritative lidar support, but the surveyed-height/10-percent vertical-error gate remains false. The footprints and generated meshes are OSM ODbL-1.0 derivatives and retain `© OpenStreetMap contributors`.
- Vegetation: the candidate replaces the stale 22-tree summary with a terrain-map-only representation outside the existing hero radius. The semantic layer hands 63,308 eligible pixels to terrain shading, leaves 144 source-eligible pixels to the separately authored hero radius, and owns zero raised canopy batches, zero raised triangles, and zero sparse individual-tree instances. Building ownership, airport/road/water exclusions, and two exact reviewed false-positive exclusions constrain the public-domain 3DEP-derived field. A 29-component/1,960-triangle raised candidate is retained only as rejected evidence after its block-and-cliff silhouettes failed inspected QA. Because source LAS vegetation classes are absent, neither representation is a botanical inventory or surveyed canopy map.
- Parked aircraft: three static, exterior-only scale cues adapted from the attributed CC BY 4.0 KOG_THORNS source. Their two LODs are offline-combined by level into one non-crossfaded `LODGroup` with one shared opaque material; they are not flyable, interactive, or part of the active aircraft hierarchy.
- Sky: one optimized 2048×1024 CC0 photographic HDR derivative imported as a 1024-face Android ASTC HDR cubemap and reused for the skybox, ambient environment, and static custom reflection. The orthophoto-only recovery does not bind it as a reservoir reflection source.

The serialized product environment is `QuestFlight/Assets/Production/Environment/ProductionEnvironmentRoot.prefab`. Runtime code validates and reports this hierarchy; it does not rebuild it.

## Compact Derivatives

- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json`
- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json`
- fixed meshes, materials, shaders, and optimized textures under `QuestFlight/Assets/Production/Environment`

The pinned source dataset contains 30,304 elevation samples, 54,768 source-grid terrain triangles, 5,522 bounded OSM features, and 31,533 quantized OSM points. The product validator checks schema, provenance, geometry, licensing, and static budgets.

## Limitations

- The three parked aircraft are simplified scale cues, not surveyed/current ramp occupancy. Their source attribution and change notice are mandatory; CC BY covers model copyright, not manufacturer trademarks or endorsement.
- The candidate optimization uses 16 offline-batched building meshes, one fixed coordinate-preserving Unity-native terrain mesh, and a zero-draw terrain canopy map outside the hero radius. No raised canopy batch is active. This documentation makes no unsupported GPU-instancing claim.
- OSM footprint coverage, tags, heights, and roof forms are incomplete. The simplified extrusions are scale cues, not surveyed structures.
- The scene remains bounded visual scenery. It is not surveyed airport geometry, an aeronautical database, current imagery, navigation data, production-photorealism evidence, or training-credit evidence.
- Simulator captures do not establish standalone performance. Headset stability, frame timing, and thermal behavior require the separate physical Quest gate.
- Aerial Scenery V1 is a failed historical visual milestone retained only on its feature branch. Its deterministic, build, test, source/license, and static-planning checks do not override the 5.5/10 inspected environment result or the missing ten recorded simulator replays.
- Aerial Scenery Recovery V2 remains the accepted development baseline; its historical 2,875-building and 22-placement counts do not describe the later semantic candidate recorded above.
- Photoreal Aerial Surface V1 remains a feature candidate until inspected Visual QA, stereo simulator checks, validation, and build results are recorded. Provenance and topology checks alone are not visual acceptance or physical Quest performance evidence.

## Aerial Scenery V1 lidar derivative

The optional V1 lidar contract is under
`QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar`. Its canonical
manifest is `KBDU_LidarManifestV1.json`. These data assets and the fixed OBJ are
offline derivatives; the lidar tools do not edit a prefab or construct runtime
hierarchy.

The exact source work unit is USGS 3DEP `CO_DRCOG_1_2020` (United States
government work / public domain):

- EPT: `https://s3-us-west-2.amazonaws.com/usgs-lidar-public/CO_DRCOG_1_2020/ept.json`
- STAC: `https://s3-us-west-2.amazonaws.com/usgs-lidar-stac/ept/CO_DRCOG_1_2020.json`
- WESM CSV: `https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/metadata/WESM.csv`
- Frozen WESM CSV SHA-256: `25079acea46bad8db53574c4343d0574177eabfa7515c6b5ec0686bbf85d044b`
- WESM work unit/project IDs: 193309 / 193312 (`CO_DRCOG_2020_B20`)
- WESM acquisition interval: 2020-05-29 through 2020-08-03
- WESM status: QL 1, USGS Lidar Base Specification 2.1, linear-mode lidar, LPC category `Meets`
- WESM original-workunit CRS metadata: horizontal EPSG:6342, vertical EPSG:5703, GEOID18

The delivered EPT/STAC item is reprojected to horizontal EPSG:3857 and omits a
vertical CRS, achieved-accuracy metrics, QL, nominal pulse spacing, and nominal
pulse density. The exact-workunit WESM association above is retained separately;
numeric accuracy or density is not inferred from its QL category. The derivative
therefore does not establish 0.05 m real-world or operational accuracy.

The frozen depth-8 snapshot contains 758 LAZ nodes, 193,586,406 bytes,
22,373,242 declared/decoded points, and 18,834,395 points inside the exact
product bounds. Raw snapshot manifest SHA-256 is
`8d5aa666d63cfeeb85321a2f4319be9520bc01a7ce1f73bec6441ed8b1aaa905`;
the data inventory SHA-256 is
`3056bfc4dae8f95fa7504a81d7a3aae4c5eb880cb308ee1841298c84f5bac101`.
Raw EPT/LAZ data remains external and prohibited from Git.

The product grid is a north-up 1024 x 1024 raster at 4 m per pixel, centered on
the pinned KBDU reference origin. Its exact UTM 13N bounds are
478664.1393769088 E, 4430102.874940165 N through 482760.1393769088 E,
4434198.874940165 N. Pixel (0,0) is local (-2046,+2046) m; Unity axes are +X
east, +Y up, +Z north.

Observed numeric LAS class counts inside the square are: class 1 = 12,496,871;
2 = 6,170,420; 7 = 64,885; 9 = 86,536; 17 = 8,905; 18 = 3,291; 20 =
3,481; and 31 = 6. No classes 3/4/5 (vegetation) or 6 (building) are present.
Canopy/building height, canopy normal/occlusion, and building confidence are
therefore explicitly return-structure heuristics. The manifest's building
quality gate is false and supplies no per-building authoritative overrides.

Committed products are R16 DTM/DSM, RGBA8 canopy/building features, separate
RGBA8 DTM terrain normal/validity and canopy-height normal/occlusion textures,
R8 building confidence, runway-plane inputs, a fixed near-terrain OBJ, and its
error report. The final border-locked fixed mesh has 46,426 vertices and 92,000
triangles. Across all raster sample centers its mesh-to-target errors are MAE
0.16947 m, p95 0.65324 m, and maximum 10.4941 m. Outside the declared
high-relief/discontinuity outliers, they are MAE 0.10747 m, p95 0.37577 m, and
maximum 1.46116 m. This is mesh-to-raster evidence, not surveyed accuracy. The
real-world 0.05 m gate remains unsupported.

## Aerial Scenery V1 terminal status

The final inspected visual scores are near terrain 5.0, aerial landscape 5.8,
near vegetation 3.0, grove representation 3.5, aerial canopy 4.5, building
spatial fidelity 8.0, building LOD continuity 7.0, and overall environment
5.5/10. The terminal outcome is **FAIL — preserve the feature branch only**.
It is not an acceptance or photorealism claim.

The final static Editor planning report records 57 maximum estimated draw calls,
527,199 selected triangles in the default authored view, 683,452 serialized
scene-mesh triangles, 24 maximum visible materials, 182,930,546 estimated
texture bytes, two transparent material slots, and 4× MSAA. Those values pass
the configured static limits but establish no physical Quest frame time,
residency, thermal behavior, or sustained performance.

Meta XR Simulator completed one live stereo/HMD/Touch connection pass. The
aggregate result remains `BLOCKED_MISSING_RECORDED_REPLAYS` because all ten
required genuine recordings are absent: `cockpit_neutral`, `head_pan`,
`rear_view`, `viewpoint_calibration`, `control_surface_sweep`, `taxi`,
`takeoff_roll`, `rotation_climb`, `shallow_turns`, and `final_approach`.
Captures, builds, and APKs remain external; their paths and the complete gate
record are in the linked evidence document above.
