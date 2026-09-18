# Airport road repair implementation

Status: **Core road repair built and installed on Quest 3. Focused stereo captures inspected; ready for worn review. Worn visual/performance acceptance and the broader revised traffic-behavior work remain incomplete.**

Approved scope: [road geometry and traffic contact plan](ROAD_GEOMETRY_AND_TRAFFIC_CONTACT_PLAN_2026-09-17.md).

## Implemented

- Constrained local terrain subdivision along 37 Airport Road, Airport Boulevard and Foothills Parkway source polylines. Added pavement-edge and longitudinal samples to the existing near mesh; preserved its GUID and existing texture coordinates/world projection.
- Corrected one-way lane centering, continuous offset joins, and bridge-carriageway assignment. Bridge XY correction is baked into continuous paths rather than snapping vehicles to the nearest bridge each frame.
- Baked support from visible terrain triangles and the particular road/deck owned by each route. Lower-road vehicles do not query the highest unrelated overpass. Compatible stream joins now also require matching authored lane counts and endpoint positions within 2 cm.
- Model-derived axle extents and underbody clearance for moving vehicles; lateral footprint sampling replaces reliance on one triangle's roll. The semi remains a rigid vehicle asset.
- Re-grounded all 24,347 existing parked records with static pitch/roll. Their observed XZ positions and density are unchanged.
- Regenerated bridge approach joins across the full road width. Added paved shoulder allowance where the existing source width supports it, plus a simple opaque concrete treatment for structural faces. Added edge barriers and abutment faces at the three inspected airport crossings.
- Restored the missing Foothills Parkway/BNSF railway crossing, CDOT **D-16-DG**, as two carriageway profiles. The frozen inventory had rejected these OSM ways because the lower crossing was railway, not road. Its existence, alignment and 1988 construction are supported by the existing CDOT/OSM records and inspected 2020 aerial imagery. The reconstructed deck uses an upper envelope of intact roadway/approach lidar samples across bare-earth deck-removal holes. Railway clearance remains unverified; the data explicitly stores no invented clearance value.
- Synchronized locally changed terrain faces into the existing ground-query asset used by native JSBSim. No flight-authority or aerodynamic implementation was replaced.
- Added an input-hash build gate for the contact bank, terrain, ground query and both road meshes. A failed airport contact bake cannot be published as accepted support data.

## Evidence so far

| Check | Result |
|---|---|
| Foothills sampled terrain profile: maximum disagreement with source DTM | 0.760 m before; 0.063 m after |
| Airport Road bend sampled terrain profile: maximum disagreement with source DTM | 0.481 m before; 0.052 m after |
| Accepted contact interpolation: maximum probed position error | 0.012 m; gate is 0.020 m |
| Full manifest contact coverage | 32,762 of 32,770 corridors accepted; eight explicit exceptions |
| Focused bridge footprint audit | 7,536 sampled footprints at 0.5 m stations across all six existing archetypes; zero outside the tested pavement envelopes |
| Parked vehicles intersecting the selected repaired lane envelopes | Zero in the focused near-airport audit |
| Focused visual review | Inspected Unity editor left/right renders and highway motion frames; corrected excessive vehicle tilt before the latest captures |
| Ground-query consistency | 263 sampled repaired triangles; maximum error 0.0000267 m |
| Native JSBSim ground scenarios | Three 4-second settling cases passed at sampled ground elevations; not sloped-road landing/flight-handling acceptance |
| Standard product Visual QA | 130 captures generated; hierarchy/environment/runway checks passed; overall FAIL_MACHINE_QA due resource limits |
| Android build and installation | Succeeded; 1,912 scenery files verified; installed APK SHA-256 matches the build |
| Quest performance/thermal acceptance | Pending physical-headset evidence |

The footprint audit uses the authored pavement envelopes and checks all six existing body styles. It is not a continuous all-map swept-volume proof or proof of conflict-free intersections. Editor eye-separated renders are not OpenXR simulator validation and cannot establish Quest performance.

Standard QA estimates peak 99 draws, 1,064,063 visible triangles and 41 scene materials against its existing 77/692,871/38 limits. The prior September 17 cockpit report (`cockpit_realism_20260917/standard_qa_final`) already failed those limits at 97/1,022,888/40. The historical scene-to-scene difference is +2 estimated draws, +41,175 peak visible triangles (4.0%) and +1 material; this is not a controlled road-only A/B or hardware measurement. `resource_gate_context.json` records both reports. Those limits were not increased to obtain a pass. The total includes previously accepted scenery/cockpit work and is not an isolated road-repair delta. Dedicated road captures, highway motion and standard airport parking views were inspected. The standard shot named `35_traffic_diagonal_highway` does not frame the repaired overpass, so it is not used as evidence of that repair. Focused OpenXR captures have now been inspected (see below); full replay/machine-gate and hardware acceptance remain pending.

External evidence: `../T2-QuestFlight-product-artifacts/road_repair_20260917/`, including `terrain_profile_validation.json`, `authoring_report.json`, `dense_footprint_audit.json`, `coverage_exceptions.json`, and `visual_qa/`.

## Resource changes

| Resource | Before | Candidate |
|---|---:|---:|
| Near terrain triangles | 92,000 | 104,078 |
| Raised-road triangles | 6,496 | 37,412 |
| Road renderer objects | 2 | 2 |
| Road material passes when both batches are visible | 2 | 4 |
| Additional road material | — | One shared opaque concrete material; no texture |
| Contact data | Previous coarse queries | 1,080,344 samples; 23,014,234 compressed bytes |

Traffic still uses the existing instanced renderer, visibility distances, LOD and scheduling clock. Surface intersection and path correction occur offline. The new bank adds loading/memory cost; hardware timing is not yet measured. No change to accepted foliage range, High FFR or cockpit presentation is part of this repair.

## Coverage and remaining work

Eight routes with unresolved surface discontinuities or ambiguous assignments retain their previous support behavior. They are not removed, faded out, or called repaired:

- `canonical:cdot_ramps:1844:0:0:0`
- `canonical:city_street_centerlines:5218:0:0:0`
- `canonical:city_street_centerlines:6474:0:0:0`
- `canonical:city_street_centerlines:703:0:0:0`
- `canonical:county_street_centerlines:93735741:0:0:0`
- `canonical:osm_bounded_roads:way/106343361:0:0:0`
- `canonical:osm_bounded_roads:way/17015262:0:0:0`
- `canonical:osm_bounded_roads:way/41994767:0:0:0`

The revised plan's broader physical-road alias deduplication, junction reservations, curvature-dependent timing and exhaustive swept-footprint/schedule validation are **not implemented** by this core repair. Existing ambiguous junctions retain their prior schedules. Do not describe this candidate as a complete connected traffic simulation, universal no-clipping solution, or completion of every item in the approved plan.

Android packaging and installation are complete. Next acceptance work is a short guided Quest inspection, with the unresolved resource/machine-gate findings kept explicit. Any remaining visible defect in the repaired flight corridor takes precedence over broader traffic behavior work.

## Android packaging interruption and recovery

The first September 18 build completed script/native/shader compilation and reached Gradle packaging, then failed at `:launcher:lintVitalAnalyzeRelease`: extracting the scenery AAR exhausted C: disk space. The log is `road_repair_20260917/android/unity_product_build.log`. The previous APK in `QuestFlight/Builds/Android` is not this candidate and must not be installed as if it contained the road repair.

Seven obsolete Gradle scenery transforms from September 17 account for about 14 GB. Automatic approval review rejected their deletion with `blocked by policy`; explicit PowerShell commands were supplied to the user. Personal files, project assets and existing APKs were not deleted. Resume packaging after sufficient space is available; retain the failed log as evidence.

Deletion of the obsolete generated `KBDU_VisibleRoadLaneSurfaces.bytes.pending` and its paired `.meta` was also rejected. Neither is referenced by the product; remove them with the commands below before the next Unity import/build.

The user subsequently freed space. On resumption C: had 40,833,048,576 free bytes, and the retry started in `android_retry/`. The original failed build evidence is retained.

### Historical cleanup commands already supplied

These exact paths are generated caches or this repair's obsolete temporary output. They do not select personal folders, source assets or APKs. Run in PowerShell after Unity has closed:

```powershell
$cacheRoot = "$env:USERPROFILE\.gradle\caches\8.13\transforms"
@(
  '94d0f49ab132cdd4186bc19a168d672f',
  '9447499d96bb40c33eb7321b485cfe49',
  '5d26d1b70076c7c324f61bf95e56b8fd',
  '5b9924508f8942d6f4dd0d3bb07132c7',
  '56f1320ac6b1f1c3a4b7b9855236c7ff',
  '371bf38e64292a05069f2ad6162ebe4c',
  'a1b7d8296eb65cef3c7efd47fe816a5d'
) | ForEach-Object {
  Remove-Item -LiteralPath (Join-Path $cacheRoot $_) -Recurse -Force
}
Remove-Item -LiteralPath @(
  'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight\Assets\Production\Environment\TrafficV2\Data\KBDU_VisibleRoadLaneSurfaces.bytes.pending',
  'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight\Assets\Production\Environment\TrafficV2\Data\KBDU_VisibleRoadLaneSurfaces.bytes.pending.meta'
) -Force
Get-PSDrive C
```


## Asset provenance

The following derivatives use the existing pinned USGS public-domain lidar and OSM ODbL road records, with the existing CDOT inventory and 2020 aerial imagery as bridge reference. Original data/license records remain in the corresponding scenery/traffic manifests. No new raw download or vehicle model was introduced.

- `Assets/Production/Environment/PhotorealAerialSurfaceV1/Terrain/Native/KBDU_ProductAeqd_BareEarth_Fixed.asset`: locally subdivided derivative; generator `tools/kbdu_traffic/repair_airport_road_terrain.py`.
- `Assets/Production/Environment/GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Near_V1.asset` and `KBDU_GradeSeparatedRoads_Outer_V1.asset`: generated from the shared road authority by `GradeSeparatedRoadV1Authoring`; input/version hashes are in the contact-bank source record.
- `Assets/Production/Environment/GradeSeparatedRoadsV1/Generated/KBDU_BridgeConcrete.mat` and `Assets/Shaders/BridgeConcrete.shader`: project-authored opaque material/shader, no external texture or model.
- `Assets/Production/Environment/TrafficV2/Data/KBDU_VisibleRoadLaneSurfaces.bytes`: project-authored sampled derivative; `KBDU_VisibleRoadLaneSurfaces_Source.json` records input hashes, coverage, tolerance, licensing and known limitations.
- Existing parked records retain their original detection/source rights and receive only project-authored support poses. Existing vehicle derivatives remain the recorded CC0 models.

The railway addition is reproducible with `tools/kbdu_traffic/prepare_road_repair_bridges.py` and the pinned external inventory/OSM files. External raw imagery, source downloads, builds and captures remain outside the repository.

## Installed candidate — September 18

- APK SHA-256: `44a65ff31c25c48e675057df2f44bb98750e14a5af9fc4036742ab0c0fadacde`.
- Size: 1792273481 bytes; Android ARM64/Vulkan/OpenXR production build.
- Exact installed `base.apk` hash verified on Quest `2G0YC5ZG8907TD`; package update time `2026-09-18 13:18:49`.
- Evidence: `android_retry/product_apk_manifest.json`, `device_installation.json`, `installed_apk_sha256.txt` and `device_startup.log`.
- The airport-road inspection start added afterward to `MetaXrSimulatorQaRuntime` is inside its Windows-player-only conditional block. It is diagnostic code excluded from Android; the installed product assets/runtime remain those recorded in the APK source snapshot.

## Focused OpenXR inspection and handoff

The isolated Windows QA player compiled successfully. Inspected both-eye forward/motion captures and side-window captures from a native-JSBSim airborne start at local (-1100, 120, -650), heading 300 degrees, 80 knots. The highway/vehicles are visible through the left exterior view; cockpit pillars and panel occlude portions, so these images do not replace the closer geometric/contact evidence. Android launch remains normal Free Flight, not the diagnostic airborne start.

Stereo poses (60 mm separation), OpenXR camera ownership, both-eye image consistency, authored hierarchy, CG pivot, native initialization and missing-reference checks passed. **The overall simulator machine gate did not pass:** its mountain probe reported terrain renderer visibility changes. Mesh identities and transforms stayed immutable. Source inspection shows capture begins before flight-mode selection, while `ProductFlightModeMenuView` hides/restores world renderers using `forceRenderingOff` during that transition. This is a capture-window/menu-transition issue to resolve separately; the failure is retained, not suppressed or presented as a clean overall pass. No recorded replay exists, so orchestration also records `BLOCKED_MISSING_RECORDINGS`. Simulator runtime registry and persistent configuration were restored.

Evidence: `simulator/meta_xr_simulator/qa_run/`; the first forward run is preserved separately in `simulator_forward_evidence/`. The temporary QA project copy was removed by the existing runner. Windows-only lower-panel bright speckles were visible in captures; this is not evidence of an Android cockpit regression and requires headset observation if also visible there. No Quest timing/thermal claim is made.

The installed build is ready for a short user review: Free Flight, then inspect **Foothills Parkway west of KBDU, including the railway overpass immediately south of Diagonal Highway**, and the **Airport Road bend southwest of the airport**. Look for remaining pavement spikes, vehicles sinking/floating, sideways jumps and unsupported bridge lanes. The existing eight outer-route exceptions and unfinished broader scheduling/deduplication work remain listed above; this handoff does not mark the entire comprehensive plan complete.
