# KBDU Public-Domain Orthophoto Pipeline

This pipeline creates a three-level, Quest-oriented macro ground pyramid from the official USGS `USGSNAIPPlus` ImageServer. It uses only the pinned 2019 USDA-FSA NAIP catalog rasters intersecting the 24 km KBDU product extent. No Google, commercial basemap, raw archive, or unclear-license pixels are used.

## Outputs

- Near zone: 4 km x 4 km, 4096 x 4096 (`0.9765625 m/pixel` nominal).
- Mid zone: 12 km x 12 km, 4096 x 4096 (`2.9296875 m/pixel` nominal).
- Far zone: 24 km x 24 km, 2048 x 2048 (`11.71875 m/pixel` nominal).
- Android import: ASTC 6x6, mipmaps, texture streaming, trilinear filtering, anisotropy 8, clamp; near/mid/far mip biases are `-0.15` / `-0.15` / `-0.10`.
- Estimated combined Android ASTC mip memory: `22,420,784` bytes.
- Shared square-ENU shader overlaps: 350 m near-to-mid and 1000 m mid-to-far.

The repository stores only optimized JPEG derivatives and `kbdu_orthophoto_manifest.json`. Lossless exports, fetch receipts, processing intermediates, package caches, and previews must remain under an external artifact directory.

## Rebuild

```powershell
$root = "C:\external\standalone_recovery\kbdu_orthophoto"
python -m venv "$root\venv"
& "$root\venv\Scripts\python.exe" -m pip install -r tools\kbdu_orthophoto\requirements.txt
$py = "$root\venv\Scripts\python.exe"

& $py -B tools\kbdu_orthophoto\fetch_naip_imagery.py --raw-dir "$root\raw\naip_2019"
& $py -B tools\kbdu_orthophoto\reproject_crop_imagery.py --raw-dir "$root\raw\naip_2019" --work-dir "$root\work"
& $py -B tools\kbdu_orthophoto\color_normalize_imagery.py --work-dir "$root\work"
& $py -B tools\kbdu_orthophoto\build_orthophoto_atlas.py --raw-dir "$root\raw\naip_2019" --work-dir "$root\work"
& $py -B tools\kbdu_orthophoto\build_terrain_pyramid_contract.py `
  --heatmap "$root\work\terrain_lod_heatmap.png" `
  --overlap-contact-sheet "$root\work\orthophoto_overlap_diagnostics.jpg"
& $py -B tools\kbdu_orthophoto\build_context_assets.py
& $py -B tools\kbdu_orthophoto\validate_orthophoto_assets.py --report "$root\validation.json"
& $py -B -m unittest discover -s tools\kbdu_orthophoto\tests -v
```

Then author only the reusable Unity assets (never the product scene) with:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" -batchmode -nographics -quit `
  -projectPath "$PWD\QuestFlight" `
  -executeMethod QuestFlight.Editor.KbduPhotorealTerrainAssetAuthoring.RunBatch
```

The fetcher refuses any raw directory inside the Git repository. It verifies the official service credit, the exact 25 source raster IDs, both pinned 2019 acquisition dates, requested dimensions, and every service-returned extent. See `INTEGRATION.md` for the lead-owned serialized scene integration.

## Rejected overlap regression

The first development export used an invalid ArcGIS parameter name. ArcGIS therefore applied its default `adjustAspectRatio=true`; because a metric-square ENU envelope is not square in WGS84 degree units near 40 degrees north, the service enlarged latitude and adjacent tile rows overlapped by about 480 pixels. That atlas was rejected before integration.

The corrected fetch sends `adjustAspectRatio=false`, requires the returned extent to match the requested extent within `1e-9` degrees, and searches every assembled seam for a repeated 5-37.5% border strip. A repeated strip with mean absolute error below `6` fails the pipeline.

## Density, overlap, and context evidence

`kbdu_ground_texel_density_audit.json` fails when an adjacent density ratio exceeds 4x, an overlap is absent, an import contract is missing, or the far texture is absent. `kbdu_terrain_pyramid_contract.json` is the exact material/UV contract. The external overlap contact sheet compares each inner level against the same central footprint in its outer level and includes a 5x difference view.

The optional context step derives 23 spatially bounded building batches from all 2,500 pinned OSM footprints and eight opaque vegetation batches from 240 deterministic road/water cues plus six project-authored airport-perimeter cottonwoods. The Unity authorer emits two non-crossfaded LODs, a three-material serialized prefab (buildings, vegetation, and parked-aircraft scenery), and no scene changes.

## License and use boundary

The National Map describes its map-service data as free and public domain. Courtesy credit is retained as `USGS, USDA, The National Map: Orthoimagery`. This 2019 imagery is a macro visual layer only: it is not current airport data, an aeronautical database, or navigation evidence. OSM context remains attributed under ODbL. Simulator review does not establish Quest performance.
