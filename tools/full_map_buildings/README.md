# Full-map building clipmap

This offline pipeline replaces the airport-centered context-building cutoff with a
24 km x 24 km aircraft-centered building clipmap. City of Boulder active building
footprints are the primary horizontal and height source. The 2020 aerial imagery
remains the temporal authority, so features with a known post-2020 build year are
excluded from the runtime derivative.

Raw ArcGIS responses stay outside Git. The committed compact database contains
quantized local ENU exterior rings and semantic attributes needed to reproduce the
runtime meshes. Unity compiles 1 km page meshes into Android AssetBundles. The
product prefab owns a fixed 4 x 4 renderer pool; runtime code only swaps mesh assets
and never creates product hierarchy.

Run from the repository root:

```powershell
python tools/full_map_buildings/build_full_map_building_database.py `
  --raw-dir "<external-artifact-root>\raw" `
  --output QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json `
  --report "<external-artifact-root>\building_database_report.json"
```

Unity compilation is performed by
`QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.BuildAndroidBundlesAndAuthorProductBatch`.

Facade semantics are a second deterministic offline stage. It spatially joins
the compact building centroids to the official Boulder County parcel service,
then joins parcel account numbers to the County Assessor building table. Raw
parcel and assessor files stay in the external artifact root:

```powershell
python tools/full_map_buildings/enrich_building_facades.py `
  --database QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json `
  --city-footprints "<artifact-root>\raw\city_building_footprints.geojson" `
  --county-footprints "<artifact-root>\raw\county_building_footprints.geojson" `
  --assessor-buildings "<artifact-root>\raw\Buildings.csv" `
  --parcel-cache "<artifact-root>\raw\boulder_county_parcels.json" `
  --manifest QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingFacadeManifestV1.json
```

The shared 16-slice facade array is regenerated from pinned Poly Haven CC0
albedo, OpenGL-normal, and ARM sources. Physical source dimensions are recorded
per slice and sampled in metres by the runtime shader:

```powershell
python tools/full_map_buildings/prepare_facade_material_arrays.py `
  --raw-dir "<artifact-root>\raw\polyhaven" `
  --output-dir QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/Materials
```

The database records a confidence level for every assignment: assessor exterior
wall evidence, parcel construction evidence, or deterministic semantic inference.
Inferred assignments are never represented as surveyed architectural truth.

The official-footprint build deliberately retains its broad airport hero
exclusion. After facade enrichment, restore the pinned non-residential OSM
footprints inside the KBDU aerodrome boundary and the exact 12-building
industrial corridor south/right of the serialized Runway 08 departure path.
This post-process excludes the two exact hand-authored replacements and updates
the database provenance and facade inventory atomically:

```powershell
python tools/full_map_buildings/apply_airport_gap_fill.py `
  --database QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json `
  --provenance QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1_PROVENANCE.json `
  --facade-manifest QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingFacadeManifestV2.json `
  --report "<artifact-root>\airport_gap_fill_report.json"
```

Validate the committed compact derivative and its worst sampled 4 x 4 runtime window:

```powershell
python tools/full_map_buildings/validate_full_map_buildings.py `
  --database QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json `
  --report "<artifact-root>\full_map_building_validation.json"
```
