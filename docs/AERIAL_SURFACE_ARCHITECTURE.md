# Aerial Surface Architecture

## Product contract

The standalone Quest 3 product uses a fixed, offline-compiled hybrid semantic
surface. It does not deform one ground mesh to imitate buildings and trees, and
it does not perform geospatial reconstruction at runtime.

All visible layers share the product-local WGS84 azimuthal-equidistant (AEQD)
frame centered on KBDU:

- `ProductionBareEarthTerrain` contains only bare-earth elevation;
- `ProductionBuildingSurfaceLayer` contains OSM-footprint building prisms;
- `ProductionCanopySurfaceLayer` is a fail-closed ownership contract with zero
  raised meshes; ordinary canopy is represented in the existing terrain draw;
- `ProductionHeroObjectLayer` owns the airport hero assets and near vegetation;
- the terrain shader owns all eligible ordinary canopy outside the hero radius.

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
the aligned terrain-map response. Visually insignificant or untrusted elevated
returns remain in the orthophoto instead of being exaggerated into geometry.

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
   rotate horizontal normal components back into product axes; no canopy mesh
   is converted or instantiated.

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
- ordinary canopy at every distance outside the 250-metre hero radius is folded
  into the existing terrain draw using height, density, normal, occlusion, and
  ownership maps;
- active raised-canopy cost is zero meshes, zero triangles, and zero additional
  canopy-geometry draws;
- ordinary trees and context buildings are not individual runtime GameObjects.

There are no realtime reflection probes, terrain reconstruction jobs, runtime
geospatial downloads, transparent canopy passes, or per-object update loops.
Static planning budgets do not establish physical Quest frame time or thermals.

The committed partition assigns 63,308 eligible non-hero canopy pixels to the
terrain-map handoff and preserves 144 eligible pixels for the existing hero
owner: all 63,452 eligible pixels are assigned exactly once. A deterministic
29-component candidate that would have emitted 1,960 triangles is recorded only
as rejected evidence. Full-resolution review found its raised top grids produced
block-like patches and terrain-closing cliff silhouettes, so it cannot enter the
active product dependency closure.

## Validation and claim limits

Generation fails closed on source hashes, frame round trips, native/source mesh
bounds, ownership overlap, building coverage, canopy evidence, terrain error,
material/draw ceilings, and product dependency closure. Full-resolution Visual
QA and stereo simulator images still require inspection after every integrated
regeneration.

This visual reconstruction is not a survey, is not for navigation, and does not
establish FAA approval, training credit, literal photorealism, final physical
fidelity, or Quest hardware performance.
