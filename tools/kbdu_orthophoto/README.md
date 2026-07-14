# KBDU Public-Domain Orthophoto Pipeline

This pipeline creates two bounded, Quest-oriented macro ground textures from the official USGS `USGSNAIPPlus` ImageServer. It uses only the pinned 2019 USDA-FSA NAIP catalog rasters intersecting KBDU. No Google, commercial basemap, raw archive, or unclear-license pixels are used.

## Outputs

- Near zone: 4 km x 4 km, 4096 x 4096 (`0.9765625 m/pixel` nominal).
- Mid zone: 12 km x 12 km, 2048 x 2048 (`5.859375 m/pixel` nominal).
- Android import: ASTC 6x6, mipmaps, texture streaming, trilinear filtering, anisotropy 4, clamp.
- Estimated combined Android ASTC mip memory: `12,459,248` bytes.

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
& $py -B tools\kbdu_orthophoto\validate_orthophoto_assets.py --report "$root\validation.json"
& $py -B -m unittest discover -s tools\kbdu_orthophoto\tests -v
```

The fetcher refuses any raw directory inside the Git repository. It verifies the official service credit, the exact nine source raster IDs, acquisition lineage, requested dimensions, and every service-returned extent.

## Rejected overlap regression

The first development export used an invalid ArcGIS parameter name. ArcGIS therefore applied its default `adjustAspectRatio=true`; because a metric-square ENU envelope is not square in WGS84 degree units near 40 degrees north, the service enlarged latitude and adjacent tile rows overlapped by about 480 pixels. That atlas was rejected before integration.

The corrected fetch sends `adjustAspectRatio=false`, requires the returned extent to match the requested extent within `1e-9` degrees, and searches every assembled seam for a repeated 5-37.5% border strip. A repeated strip with mean absolute error below `6` fails the pipeline.

## License and use boundary

The National Map describes its map-service data as free and public domain. Courtesy credit is retained as `USGS, USDA, The National Map: Orthoimagery`. This 2019 imagery is a macro visual layer only: it is not current airport data, an aeronautical database, or navigation evidence. Simulator review does not establish Quest performance.
