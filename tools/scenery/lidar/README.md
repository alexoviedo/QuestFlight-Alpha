# KBDU USGS 3DEP lidar pipeline

This pipeline snapshots a bounded set of public USGS 3DEP Entwine Point Tile
nodes outside Git, then emits small deterministic product derivatives. The
source item covering KBDU is `CO_DRCOG_1_2020`.

The frozen selection is a 4,096 m square centered on the FAA KBDU reference
point. EPT depth 8 has a nominal 2.0693359375 m selection resolution and feeds
4 m product pixels. Raw EPT metadata, hierarchy pages, and 758 LAZ nodes stay in
the external artifact root.

Typical commands from the repository root:

```powershell
$python = '<external-prefix>\python.exe'
& $python tools/scenery/lidar/ept_snapshot.py --output '<artifact-root>\lidar\raw\CO_DRCOG_1_2020_depth8_kbdu4096'
& $python tools/scenery/lidar/derive_surfaces.py --snapshot '<artifact-root>\lidar\raw\CO_DRCOG_1_2020_depth8_kbdu4096' --output QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar --write-unity-meta
& $python tools/scenery/lidar/build_near_terrain.py --qa-output '<artifact-root>\lidar\qa' --write-unity-meta
& $python tools/scenery/lidar/validate_lidar.py --qa-output '<artifact-root>\lidar\qa'
```

The DRCOG item has no vegetation or building classifications at KBDU. Canopy
and building channels are therefore conservative return-structure heuristics,
not surveyed classifications. Multi-return elevated points seed canopy;
coherent planar, predominantly single-return elevated regions seed building
height. The product manifest fails closed for authoritative building overrides.

The fixed near mesh is authored offline as a border-locked, zone-adaptive OBJ
with forced primary-runway blend coordinates and one hard-capped error-driven
cell-refinement pass. All-pixel and declared high-relief outlier metrics remain
in the adjacent error report. It is not integrated into a Unity prefab by these
tools. The 0.05 m operational real-world accuracy request remains unsupported:
EPT/STAC omit vertical CRS and achieved accuracy metrics; the separately pinned
WESM association does not replace a surveyed local runway truth study.

DTM terrain normals/validity and canopy-height-derived normals/occlusion are
separate RGBA8 assets. The latter remains heuristic because the source subset
contains no LAS vegetation classes.
