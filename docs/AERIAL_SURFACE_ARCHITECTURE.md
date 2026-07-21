# Aerial Surface Architecture

## Product contract

The standalone Quest 3 product uses a fixed, offline-compiled hybrid semantic
surface. It does not deform one ground mesh to imitate buildings and trees, and
it does not perform geospatial reconstruction at runtime.

All visible layers share the product-local WGS84 azimuthal-equidistant (AEQD)
frame centered on KBDU:

- `ProductionBareEarthTerrain` contains only bare-earth elevation;
- `ProductionBuildingSurfaceLayer` contains OSM-footprint building prisms;
- `ProductionCanopySurfaceLayer` remains a fail-closed semantic ownership
  contract with zero terrain-relief meshes; ordinary canopy is represented in
  the existing terrain draw;
- `ProductionHeroObjectLayer` owns the airport hero assets and near vegetation;
- the terrain shader owns all eligible ordinary canopy outside the hero radius;
- one separately authored, bounded Tier-2 grove batch adds rounded volume at 42
  selected semantic locations without changing terrain height or ownership.

The hierarchy is explicitly authored and serialized. No runtime bootstrap or
repair code constructs these layers.

## Why the pipeline is hybrid

The 3DEP source is strong elevation evidence but its committed source classes do
not provide reliable building and vegetation semantics throughout the product
area. Treating every raised lidar return as generic relief therefore puts roofs,
trees, vehicles, and noise into the wrong visual category.

The compiler instead assigns each source a narrow authority:

| Source | Authority | Not used as |
| --- | --- | --- |
| USGS 3DEP DTM | bare-earth height and surface normals | building/tree footprint truth |
| USGS 3DEP DSM/nDSM | bounded height evidence and canopy response | unconditional object classification |
| OpenStreetMap | building footprint and identity | surveyed height or architectural detail |
| Legal orthophoto | roof/canopy/ground appearance and visual alignment | elevation or navigation truth |
| Authored airport assets | close-range hero representation | broad context reconstruction |

Buildings are therefore placed from OSM footprints and use lidar height only
when the recorded confidence gates pass. The lidar canopy eligibility mask
removes building/airport ownership and a small reviewed set of stable
false-positive source components, then hands all eligible ordinary canopy to
the aligned terrain-map response. Rounded Canopy Volume V1 samples that same
bounded evidence to place 42 representative grove clusters; it does not convert
the height field into generic relief. Visually insignificant or untrusted
elevated returns remain in the orthophoto instead of being exaggerated into
geometry.

Raw DSM or nDSM is never applied as terrain displacement. A DSM records the
upper returned surface, so it mixes roofs, tree crowns, vehicles, fill, and
noise with the ground. Applying it to a continuous ground mesh would raise the
terrain beneath semantic objects, duplicate buildings and trees, create hard
cliffs around coarse raster cells, and compromise runway/foundation continuity.
The terrain height authority is the DTM alone. DSM/nDSM is restricted to
bounded building-height attribution, canopy eligibility, and canopy-map
shading after the relevant semantic masks and coordinate transforms pass.

## Horizontal-frame alignment

The source lidar rasters retain an origin-relative UTM zone 13N grid. The
orthophoto, OSM geometry, runway references, and Unity world use the local AEQD
frame. Those frames differ by several metres near the edge of the four-kilometre
lidar square, which is visually significant at the source's four-metre pixel
spacing.

The offline pipeline records one exact pyproj frame contract and a deterministic
affine runtime approximation. It applies the contract in three places:

1. bare-earth mesh vertices stay on a crack-free product-AEQD grid while their
   heights are sampled at the corresponding UTM raster coordinates;
2. OSM footprint points are transformed into UTM only for lidar height and mask
   lookup, then emitted unchanged in product AEQD;
3. terrain/canopy data textures apply the inverse affine before sampling and
   rotate horizontal normal components back into product axes; and
4. Tier-2 grove centers use the validated product-space distribution, while the
   project-authored source mesh is explicitly converted from Blender Z-up to
   Unity Y-up with a right-multiplied `Quaternion.Euler(-90, 0, 0)` matrix.

Unity's generic OBJ importer changes handedness. Product meshes are compiled by
an editor-only native-mesh authorer that reads the documented `+X east, +Y up,
+Z north` coordinates directly. The serialized runtime objects use identity
transforms and do not depend on the source OBJ importers.

## Quest representation budget

The architecture spends geometry only where it changes a pilot-visible
silhouette:

- the bare-earth mesh is fixed and error-bounded, with no tessellation or
  camera-relative morphing;
- context buildings are offline spatial batches with one shared opaque material;
- ordinary canopy outside the 250-metre hero radius remains folded into the
  existing terrain draw using height, density, normal, occlusion, and ownership
  maps;
- the bounded Tier-2 volume cue uses 42 fixed-world, non-camera-facing instances
  of one 540-triangle project-authored cluster in one `DrawMeshInstanced` call;
- T0/T1 remain 12,860 triangles in two draws, so total vegetation is 35,540
  triangles, three draws, and one material;
- ordinary trees and context buildings are not individual runtime GameObjects.

There are no realtime reflection probes, terrain reconstruction jobs, runtime
geospatial downloads, alpha-blended canopy passes, camera-facing grove
billboards, LOD crossfades, or per-object update loops. Rounded Canopy Volume V1
adds no texture, material, transparent slot, shader, or shader variant. Static
planning budgets do not establish physical Quest frame time or thermals.

The committed partition assigns 63,308 eligible non-hero canopy pixels to the
terrain-map handoff and preserves 144 eligible pixels for the existing hero
owner: all 63,452 eligible pixels are assigned exactly once. A deterministic
29-component candidate that would have emitted 1,960 triangles is recorded only
as rejected evidence. Full-resolution review found its raised top grids produced
block-like patches and terrain-closing cliff silhouettes, so it cannot enter the
active product dependency closure.

The rounded-grove overlay does not alter or carve that ownership map. It is an
additive presentation layer over selected validated semantic regions. Earlier
external-only ellipsoid, dense-lobe, and raster-shell experiments were rejected
because they read as rocks/avocados, faceted rocks, or raised plates/ledges;
none is included in the product dependency closure.

## Validation and claim limits

Generation fails closed on source hashes, frame round trips, native/source mesh
bounds, ownership overlap, building coverage, canopy evidence, terrain error,
material/draw ceilings, and product dependency closure. Full-resolution Visual
QA and stereo simulator images still require inspection after every integrated
regeneration.

The rounded candidate passes its vegetation validator, 9/9 focused Python
tests, 114/114 EditMode tests, 9/9 PlayMode tests, product validation, inspected
machine Visual QA, scoped static budget, live stereo/HMD/Touch check, and
Android build. The aggregate simulator gate remains
`BLOCKED_MISSING_RECORDED_REPLAYS`, and the exact APK has not been worn or timed
on Quest 3.

This visual reconstruction is not a survey, is not for navigation, and does not
establish FAA approval, training credit, literal photorealism, final physical
fidelity, or Quest hardware performance.
