# Aerial Scenery Offline Toolchain

This directory contains deterministic, offline authoring tools for Aerial
Scenery V1. Nothing here is a product runtime dependency. Network source data,
package caches, and processing intermediates must remain under an external
artifact root.

The pinned 112-package Windows environment is described by `environment.yml`
and frozen in `conda-lock-win-64.txt`. It includes the verified PDAL, GDAL,
rasterio, shapely, laspy/lazrs, NumPy/SciPy/Pillow, and trimesh authoring stack.
The local installation used for the KBDU lidar derivatives was created with
micromamba 2.3.2 under:

`T2-QuestFlight-product-artifacts/aerial_scenery_v1_20260718_190855/toolchain`

See `lidar/README.md` for the bounded USGS 3DEP workflow. The generated assets
are self-contained PNG/JSON/OBJ derivatives; none of these authoring tools are
shipped to Quest. See `docs/TOOLCHAIN.md` for verified versions and commands.
