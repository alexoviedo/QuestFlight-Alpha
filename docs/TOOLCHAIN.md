# Offline Scenery Toolchain

The Aerial Scenery V1 authoring environment is installed under the external
product-artifact root. It is not a Unity package, Android dependency, or Quest
runtime component. Package caches and environments must never be copied into
the repository.

## Frozen Windows environment

- Platform: `win-64`
- Installer: micromamba 2.3.2
- Micromamba archive SHA-256: `c90484d65d84b88edffa8a9746f26a7cfd9c323f9a634dbf7b03fe7ce386dae1`
- Declarative spec: `tools/scenery/environment.yml`
- Direct dependency pins: `tools/scenery/requirements-lock.txt`
- Concise host and direct-package versions: `tools/scenery/tool_versions.json`
- Reproducible 112-package explicit lock: `tools/scenery/conda-lock-win-64.txt`
- Environment/source verification record: `tools/scenery/toolchain.json`

Verified direct tools and libraries are Python 3.12.13, NumPy 2.3.5, SciPy
1.16.3, Pillow 11.3.0, pyproj 3.7.2, laspy 2.6.1, lazrs 0.8.1, PDAL CLI/core
2.10.0 with Python binding 3.5.3, GDAL 3.12.3, rasterio 1.5.0, shapely 2.1.2,
trimesh 4.12.2, and pytest 8.4.2. PDAL and laspy both decoded the same frozen
4,415-point LAZ sample. Rasterio exposed 190 drivers; a Shapely operation and a
12-face trimesh box were also exercised.

Recreate the environment outside the repository:

```powershell
micromamba create -p <external-prefix> -f tools/scenery/conda-lock-win-64.txt
micromamba run -p <external-prefix> pdal --version
micromamba run -p <external-prefix> gdalinfo --version
```

`gltfpack` and `meshoptimizer` are not installed: conda-forge returned no
win-64 entry during the pinned setup, and the selected fixed-terrain deliverable
is OBJ rather than glTF. No mesh-optimization result is claimed.

## Lidar workflow

The bounded USGS snapshot and all intermediates stay under the external
artifact root. From the repository root, use the pinned environment's Python:

```powershell
python tools/scenery/lidar/ept_snapshot.py --output <external-snapshot>
python tools/scenery/lidar/derive_surfaces.py --snapshot <external-snapshot> --output QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar --write-unity-meta
python tools/scenery/lidar/build_near_terrain.py --qa-output <external-qa> --write-unity-meta
python tools/scenery/lidar/validate_lidar.py --qa-output <external-qa>
pytest tools/scenery/lidar/tests
```

The tools author data only. They do not construct or repair the product scene,
XR hierarchy, aircraft, flight authority, or backend.
