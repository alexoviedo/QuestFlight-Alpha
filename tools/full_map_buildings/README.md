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

Validate the committed compact derivative and its worst sampled 4 x 4 runtime window:

```powershell
python tools/full_map_buildings/validate_full_map_buildings.py `
  --database QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1.json `
  --report "<artifact-root>\full_map_building_validation.json"
```
