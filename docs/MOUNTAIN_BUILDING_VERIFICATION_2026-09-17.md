# Mountain building verification — 2026-09-17

## Verdict

Several conspicuously tall mountain building representations are incorrect. Their recorded building heights are modest; the excessive visible height comes from disagreement between their absolute base elevations and the rendered terrain. Keep the real footprints and buildings. Correct grounding and validate the elevation sources rather than deleting mountain buildings or indiscriminately clamping their heights.

The initial investigation was read-only. The user subsequently authorized a correction; implementation and validation status are recorded below.

## Evidence

Inspected the existing `tree_realism_20260915/review/screenshots/trees_6_-70_left.png` capture: conspicuous tall rectangular walls and a very thin, extreme vertical extrusion are visible in the western mountain landscape. This establishes the visual defect; individual pixels were not formally matched to IDs.

Parsed the current building database and the current serialized `KBDU_ProductionTerrainSurfaceV2.asset`. Compared individual source records retrieved directly from City and County GIS on September 17 with USGS Elevation Point Query Service ground samples. Inspected downloaded Esri World Imagery aerial views at two suspect locations; these show ordinary small buildings among trees, not high-rises. Aerial photographs alone do not establish precise building heights; those come from the source records.

| Product ID / source | Latitude, longitude | Source building height | Approximate model eave above local terrain | Finding |
|---|---|---:|---:|---|
| 56221 / County OBJECTID 36242 | 40.040509, -105.359907 | 19 ft / 5.8 m | 599.5 m | County base elevation conflicts dramatically with USGS ground and the aerial setting. |
| 51727 / County OBJECTID 24189 | 40.094173, -105.313093 | 20 ft / 6.1 m | 106.8 m | County base elevation is about 98 m above the independent USGS ground sample. |
| 11519 / City BLDG8031 | 40.058533, -105.300526 | 20 ft / 6.1 m | 52.0 m | Source base approximately agrees with USGS; coarse product terrain is about 44 m too low at this point. |

Model values are calculated from the current authored base plus height minus the bilinearly sampled serialized terrain at the mean footprint vertex position. They exclude pitched-roof rise and the buried foundation skirt; they are not measurements of a rendered pixel silhouette. These three points lie outside the terrain transition bands, so no transition-patch approximation is needed.

Independent ground checks, metres above sea level:

| Product ID | GIS base | USGS EPQS ground | Product ground |
|---|---:|---:|---:|
| 56221 | 2560.32 | 1945.60 | 1966.55 |
| 51727 | 2112.26 | 2013.85 | 2011.61 |
| 11519 | 1953.16 | 1951.63 | 1907.25 |

USGS responses report 1 m resolution. This comparison is sufficient to identify gross inconsistencies, not a survey-grade vertical-datum certification.

An initial scan of 16,400 building components west of local X = -4000 m flagged 91 whose authored base is more than 20 m above the retained terrain source. That is a screening count, not 91 individually verified incorrect buildings: transition patches, sloping footprints, and source accuracy require individual handling. It does not justify deleting those records.

## Mechanism

1. `tools/full_map_buildings/build_full_map_building_database.py` trusts positive source building heights after converting feet to metres. It separately takes `GROUNDELEV` (County `BaseElevat`) whenever numeric and greater than 4000, converts it from feet, and subtracts the local elevation origin. There is no terrain-consistency check on that base.
2. `FullMapBuildingClipmapAssetAuthoring.cs`, around line 1256, places the wall top at `base_y_dm * 0.1 + height_dm * 0.1`.
3. `TerrainSealedFoundationHeight`, around line 1346, extends each wall down to the lower of the authored base and the local terrain, with a 1.6 m embed. This successfully closes gaps but turns excessive base offsets into enormous vertical walls.
4. The serialized outer terrain has 400 m sample spacing, and the middle terrain has 150 m spacing. On mountain slopes, a plausible real-world base can be tens of metres above this simplified terrain. The City example demonstrates this second cause; source-base errors alone do not explain everything.

Thus there are two inputs to correct: bad absolute source elevations and legitimate elevations that do not fit the simplified rendered terrain. The wall sealing logic amplifies both into tall buildings.

## Recommended follow-up, not implemented

- Audit base-to-terrain residuals across the map, using the exact rendered terrain including transition patches and the full footprint, not only its centre.
- Preserve trustworthy above-ground building heights and footprints. Reconcile ordinary buildings to the rendered terrain with bounded foundation exposure; use a separate slope/terrain treatment for footprints that cannot fit without a tall pedestal or burial.
- Flag gross source-elevation outliers against independent terrain before accepting them. Do not assume that every GIS field is correct.
- Preserve verified tall landmarks and the separate semantic mast/stack replacements documented in `docs/evidence/SEMANTIC_TOWER_REPLACEMENT_V1_2026-09-03.md`.
- Check the corrected mountain sites from pilot-visible stereo views and validate the build. This should be an offline geometry/placement correction, not a new runtime system.

## Sources and retained evidence

- [City of Boulder building footprints](https://gis.bouldercolorado.gov/ags_svr3/rest/services/pw/BuildingFootprints/MapServer/0), queried for BLDG15171, BLDG15170 and BLDG8031.
- [Boulder County building and structure footprints](https://services.arcgis.com/jIL9msH9OI208GCb/ArcGIS/rest/services/BoulderColorado/FeatureServer/1), queried for OBJECTID 36242, 24189 and 39347.
- [USGS EPQS example at the extreme extrusion](https://epqs.nationalmap.gov/v1/json?x=-105.3599067&y=40.0405092&units=Meters&wkid=4326&includeDate=false).
- [Esri World Imagery service](https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer), exported 1000-square images centred on 40.039893/-105.298349 and 40.040509/-105.359907. Imagery acquisition date was not established; it is corroborating visual context, not the project's 2020 temporal authority.
- External, uncommitted evidence: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/mountain_building_verification/` contains City/County query responses, three USGS responses, and the two inspected aerial images. The initial screening table is `../mountain_building_audit.json`.

## Authorized correction

The editor mesh compiler now reconciles each building datum with the exact serialized rendered terrain, including transition patches. It samples the footprint perimeter at up to 5 m intervals plus its centre, preserves a level roof and the recorded above-ground height, and bounds ordinary foundation offsets around the median terrain height. It preserves a source base already within the permitted range. Exceptionally steep footprints use the lowest datum that keeps the level eave at least 0.5 m above the highest sampled ground; some downhill foundation exposure is unavoidable without altering terrain or redesigning those buildings.

This is an offline mesh correction. No added runtime logic, draw passes, colliders, building removal, terrain deformation, or footprint resizing. Verified semantic tower exclusions remain intact. Cache identity now includes the serialized terrain asset so a terrain change invalidates stale building bundles. Existing asset GUIDs are preserved.

Evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/building_grounding_20260917`.

- Final geometry rebuild: PASS. `grounding_final.json` covers 57,440 buildings; 7,123 datums shift by more than 0.1 m. No final eave is below the sampled footprint corners. Original source records remain untouched.
- Compilation metrics match the prior compiler: all 155 exact landmarks retained; 235,000 maximum active-window triangles; unchanged footprint error metrics; zero measured roof/wall and vertical-corner seam gaps.
- The extreme County building 56221 is lowered 592.32 m; 51727 is lowered 98.93 m. Heights remain 5.8 m and 6.1 m. Their eaves are now approximately 8.2 m and 8.7 m above the lowest footprint corner, including foundation exposure.
- Steep-site limitation: 11519 now has a level eave about 15.6 m above its lowest corner, instead of approximately 52 m above its centre terrain. Some retaining-wall exposure remains on steep, coarse terrain; this change does not claim survey-accurate terrain or foundations.
- Building EditMode suite: 23/23 PASS (`building_tests_final.xml`). Initial new fixture tests lacked the terrain layer's required stable ID; the fixtures were corrected and the entire building suite rerun successfully.
- Inspected both eyes of western diagnostic `trees_6_-70` and the foothill `trees_3_-70_left` capture. The extreme sky-reaching spike and excessive hillside blocks are absent, and surrounding building footprints remain visible. These diagnostic views hide the aircraft and are not cockpit visibility evidence.
- Inspected live OpenXR simulator stereo at the native foothill start. Both eyes are nonblank and scene/material consistency passes. This reuses the existing simulator executable with newly compiled Windows building bundles; `simulator/geometry_preview_identity.json` records that boundary. No new Windows runtime build is claimed. The existing mountain-identity machine check remains failed; no Quest timing claim is made.
- Standard visual QA: 130/130 captures, contact sheet inspected, same 26 failing view IDs as the September 15 final pass, no new failing IDs. Max estimates remain 97 draws / 1,026,792 triangles / 41 materials. Overall QA remains failed on the existing thresholds; none were waived.
- Initial Android packaging failed because the disk filled during Gradle's lint AAR extraction. The incremental retry succeeded with sufficient free space. The interrupted build's two empty generated RuntimeActionBindings files were preserved outside StreamingAssets to restore the established payload allowlist; they were not discarded.


- Final Android build: PASS; all 1,912 authored payloads verified against the APK. The package includes 144 v17 building bundles and only ARM64 native libraries. SHA-256: `606d6feaaa9fe6ab1e759bd21504de075660e442b435429ac39b5256afed1d96`. Evidence: `android_retry/product_apk_manifest.json` and `android_retry/package_audit.json`.
- Physical Quest timing/thermal and worn visual acceptance have not been run for this correction. Mesh counts are unchanged; that is not a measured GPU-performance result.
- Device delivery: installed successfully on Quest 2G0YC5ZG8907TD; installed base APK SHA-256 matches the built candidate exactly. Normal application launch issued. Evidence: `android_retry/device_install.json`.
