# DSM, Building, and Canopy Pipeline

## Scope

This document covers the deterministic Workstream B asset-authoring pipeline for
`PhotorealAerialSurfaceV1`. It produces compact lidar derivatives,
OSM-footprint building surface meshes, a fixed bare-earth terrain mesh, and a
terrain-map canopy ownership contract under:

`QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1`

The pipeline is an offline authoring toolchain. It does not construct, repair,
or search for product hierarchy at runtime. It does not edit the product scene
or prefab. Integration must bind the generated assets to the explicitly
authored and serialized product hierarchy, with exactly one enabled XR Origin,
tracked Main Camera, aircraft simulation root, and flight authority. The Main
Camera remains OpenXR-owned.

The generated semantic roots are exactly:

- `ProductionBuildingSurfaceLayer`
- `ProductionCanopySurfaceLayer`

The source baseline for this milestone is commit
`197283fc4f17ae1f78db57c6fcc4e27dc995cd66`.

## Source data and rights

The canonical lidar authority is:

`QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json`

It records the bounded USGS 3D Elevation Program dataset
`CO_DRCOG_1_2020`, a United States Government work/public-domain source. The
lidar raster grid is 1024 by 1024 pixels at 4 meters per pixel, covering an
exact 4096-meter square in UTM zone 13N (`EPSG:32613`). The serialized product,
orthophoto, OSM geometry, and runway use a local WGS84 azimuthal-equidistant
(AEQD) frame. Both use east on X, up on Y, and north on Z, but they are not the
same horizontal coordinate system: their separation reaches 7.444086 meters at
the edge of the lidar square. Every cross-source lookup therefore uses the
pinned pyproj transform rather than treating the two local grids as identical.

The source EPT snapshot contains 758 LAZ data files at the pinned depth and
inventory hash. Raw EPT metadata, hierarchy files, LAZ files, source archives,
and downloads stay in an external artifact root. They are prohibited from Git.
The committed lidar products are compact, source- and license-recorded
derivatives only.

`tools/aerial_surface/fetch_lidar.py` prints the pinned fetch plan by default.
Download and offline verification are optional source-recovery operations and
must use a path outside the repository:

```powershell
python -B tools\aerial_surface\fetch_lidar.py
python -B tools\aerial_surface\fetch_lidar.py --verify --artifact-root 'D:\QuestFlightArtifacts\lidar\CO_DRCOG_1_2020'
```

If the external snapshot must be reconstructed, record a fixed retrieval time
instead of allowing an unstated timestamp:

```powershell
python -B tools\aerial_surface\fetch_lidar.py --download --artifact-root 'D:\QuestFlightArtifacts\lidar\CO_DRCOG_1_2020' --workers 12 --retrieved-utc 'YYYY-MM-DDTHH:MM:SSZ'
python -B tools\aerial_surface\fetch_lidar.py --verify --artifact-root 'D:\QuestFlightArtifacts\lidar\CO_DRCOG_1_2020'
```

The normal Workstream B build does not require the raw snapshot. It verifies
and reuses the pinned public-domain 4-meter DTM, DSM, surface-feature, normal,
occlusion, and confidence derivatives already recorded by the canonical lidar
manifest.

Building footprints come from the committed OpenStreetMap-derived reference
context and retain their separate ODbL-1.0 attribution to OpenStreetMap
contributors. Raw OSM extracts are not committed. The context, placement
recipe, coverage report, and active AirportCore replacement-footprint record
are all hashed into the building manifest.

## Deterministic generation order

Run all commands from the repository root. The generators use canonical JSON,
fixed PNG settings, sorted identities, stable path-derived asset GUIDs, and
fixed topology rules. A complete Workstream B regeneration is:

```powershell
python -B tools\aerial_surface\build_dtm_dsm.py
python -B tools\aerial_surface\classify_surface.py
python -B tools\aerial_surface\build_building_surfaces.py
python -B tools\aerial_surface\build_canopy_surfaces.py
python -B tools\aerial_surface\build_product_terrain.py
```

Do not pass `--no-meta` when producing assets for Unity. That switch exists for
isolated tests only.

### DTM, DSM, and nDSM

`build_dtm_dsm.py` verifies the source-manifest size and SHA-256 records for the
existing DTM and DSM, then emits:

- `Lidar/KBDU_AerialSurface_nDSM_R16.png`
- `Lidar/KBDU_AerialSurface_DtmDsmManifestV1.json`

The normalized surface height is computed exactly as
`max(DSM_R16_code - DTM_R16_code, 0)`. The R16 code scale is 0.05 meters. The
current committed generation reports zero negative deltas, a maximum nDSM
height of 34.6 meters, and a quantization maximum absolute error of 0.05
meters. These are raster derivation metrics, not a surveyed vertical-accuracy
claim.

Neither DSM nor nDSM is ever used to displace the product terrain. DSM is an
upper-return surface containing roofs, tree crowns, vehicles, fill, and noise;
nDSM is only its height above the DTM. Treating either as a ground-height field
would raise terrain beneath objects, duplicate separately owned buildings and
trees, create block/cliff silhouettes at the four-meter raster scale, and risk
breaking runway, foundation, road, and shoreline continuity. Only the DTM is
the terrain-height authority. DSM/nDSM is restricted to bounded building-height
attribution, semantic eligibility evidence, and aligned canopy-map shading.

### Surface classification

`classify_surface.py` preserves the source return-structure channels in a
single linear R8 bitfield:

- bit 0: valid surface return;
- bit 1: nDSM at least 1.5 meters;
- bit 2: canopy-height heuristic present;
- bit 3: building-height heuristic present;
- bit 4: heuristic classification, not a source LAS class;
- bit 5: DTM or DSM fill flag present.

It emits:

- `Lidar/KBDU_AerialSurface_SurfaceClass_R8.png`
- `Lidar/KBDU_AerialSurface_ClassificationManifestV1.json`

The bounded source has no LAS vegetation classes 3, 4, or 5 and no LAS
building class 6 inside the exact product bounds. The canopy and building
channels are therefore non-authoritative return-structure inferences. They
must not be described as surveyed classifications. The current classification
contract records zero building/canopy overlap pixels.

## Building surface layer

`build_building_surfaces.py` compiles every non-hero OSM footprint in the
0-to-4-kilometer ownership radius. The complete current denominator and result
are:

| Band | Original footprints | Hero exclusions | Non-hero denominator | Represented | Coverage |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0-2 km | 1,831 | 2 | 1,829 | 1,829 | 100% |
| 2-4 km | 7,863 | 0 | 7,863 | 7,863 | 100% |

The two active AirportCore replacements are feature indices 3121 and 3137
(OSM IDs 77434544 and 78054463). They cover 1,953.75 square meters in total.
They are excluded from `ProductionBuildingSurfaceLayer`, remain owned by
`ProductionHeroObjectLayer`, and are counted separately rather than removed
from the coverage evidence. The shared footprint exclusion mask includes both
building-layer and hero-building footprints so canopy cannot claim either.

No building geometry is authored beyond 4 kilometers by this layer. The far
region remains the orthophoto/terrain representation.

### Heights and placement

Terrain base elevation comes from the pinned DTM where the product grid has
support, with the existing deterministic placement recipe retained as the
fallback. Footprints remain in product AEQD; their sample points are transformed
to origin-relative UTM only for the DTM/DSM lookup. Lidar-derived building
height is allowed only after the configured support, confidence, and pixel-count
gates. Even when selected, it remains a non-authoritative heuristic because
there is no source LAS building class and no surveyed truth/error study. The
current build uses the lidar heuristic for 28 of 9,692 non-hero footprints and
records the deterministic fallback for all others.

Lidar never supplies or expands the building plan shape. OSM owns the footprint
geometry and stable product-AEQD placement; the bounded lidar lookup may assign
only base/roof elevation after its gates pass. This prevents DSM roof returns,
trees, and neighboring clutter from inflating a building into a raster-sized
box or shifting it away from its mapped semantic feature.

### Geometry and simplification

Each represented footprint becomes an explicit closed prism with roof, floor,
and wall triangles. A deterministic, error-bounded footprint simplification
reduces the selected-scene topology while preserving source identity. Every
simplified footprint must satisfy:

- source-area ratio from 0.90 through 1.05;
- maximum inflation of 5%;
- sampled symmetric Hausdorff error no greater than 10 meters.

The manifest records per-build area, Hausdorff, and vertex-reduction
statistics. The current result reduces 67,156 source footprint points to
41,715 points (37.8834%), with area-ratio bounds 0.90 to 1.05 and sampled
Hausdorff maximum 10 meters.

The generated layer is 128,092 closed-surface triangles and 83,430 expanded
vertices, below the hard 130,000-triangle ceiling. It is emitted as a compact
semantic binary plus 16 populated, 2-kilometer coordinate-preserving OBJ source
batches under `Buildings/Generated`. An editor-only compiler converts those
sources to non-readable Unity-native mesh assets under `Buildings/Native`.
Runtime dependencies reference only those native meshes, avoiding the generic
Unity OBJ importer's X-axis handedness conversion. Batch names follow:

`ProductionBuildingSurfaceLayer_{xn002|xn001|xp000|xp001}_{zn002|zn001|zp000|zp001}.obj`

Each OBJ has one `o` statement and one `g` statement matching its filename
stem. It contains no `mtllib`, `usemtl`, or embedded material. Lead integration
assigns one existing opaque shared material.

The building outputs are:

- `Buildings/ProductionBuildingSurfaceLayer.bytes`
- `Buildings/ProductionBuildingSurfaceLayer_FootprintMask_R8.png`
- `Buildings/ProductionBuildingSurfaceLayer_Manifest.json`
- `Buildings/Generated/ProductionBuildingSurfaceLayer_*.obj`

The manifest schema is
`questflight.photoreal-aerial-surface-v1.production-building-surface-layer.v1`.

## Canopy surface ownership

Canopy ownership is radial and mutually exclusive after building, operational,
reviewed-false-positive, and hero exclusions:

| Distance from product origin | Owner and representation |
| --- | --- |
| 0-250 m | Existing hero trees; excluded from the ordinary terrain-map handoff |
| 250-1500 m | Terrain canopy maps plus selected fixed-world Tier-2 grove clusters |
| Beyond 1500 m | Terrain canopy-map representation; 12 bounded Tier-2 clusters remain where the deterministic distribution selects them |

`ProductionCanopySurfaceLayer` is now a fail-closed ownership contract, not a
raised surface layer. Its active geometry mask, component table, run table,
mesh-batch list, triangle count, and vertex count are all zero. Every eligible
ordinary canopy pixel outside the existing hero radius is assigned to the
terrain-map handoff. The handoff excludes building footprints, active
hero-building footprints, operational masks, and the exact reviewed
false-positive components before assignment.

The exact ownership textures are:

- `Canopy/ProductionCanopySurfaceLayer_GeometryOwnership_R8.png`
- `Canopy/ProductionCanopySurfaceLayer_TerrainMapHandoff_R8.png`

The zero-valued geometry texture is an explicit fail-closed assertion, not an
invitation to infer geometry. The terrain-map texture is sampled in the lidar
UTM raster frame only after the product-AEQD-to-UTM transform; its horizontal
normal components are rotated back into product axes. It adds no renderer, no
material, and no draw beyond the existing terrain draw.

Rounded Canopy Volume V1 does not change that semantic ownership or generate a
raised terrain layer. It adds 42 project-authored `AerialGroveCluster_T2`
instances at deterministic semantic locations, 30 within 1.5 km. Each source
cluster has 540 triangles; one fixed-world `DrawMeshInstanced` batch therefore
adds 22,680 triangles and one draw. The existing terrain-map response remains
beneath every cluster. The matrix authorer explicitly right-multiplies
`Quaternion.Euler(-90, 0, 0)` to convert Blender Z-up mesh vertices to Unity
Y-up while retaining product ENU placement and yaw.

The grove overlay is not camera-facing, does not crossfade or dither, creates no
per-tree runtime GameObject or `Update`, and adds no texture, material,
transparent slot, shader, or shader variant. T0/T1 remain unchanged at 12,860
triangles and two draws; total vegetation is 35,540 triangles, three draws, and
one shared material.

The final deterministic generation reports:

- zero raised canopy OBJ/native mesh batches, zero active raised triangles and
  vertices, zero vertical skirts, and zero canopy-geometry draws;
- 144 eligible pixels retained by the existing 0-250 meter hero owner;
- 63,308 eligible non-hero pixels assigned to the terrain-map handoff; and
- 63,452 of 63,452 post-building/review-exclusion source pixels assigned exactly
  once, for 100% combined source coverage.

The generator still measures the former raised representation for diagnostic
evidence without emitting it. That rejected candidate retained 29 connected
components and would have emitted 1,960 top-grid triangles. Full-resolution
lead review rejected it because coarse raised patches read as artificial blocks
and their terrain-closing edges formed cliff-like silhouettes. Those metrics
remain in the manifest with `active: false`; no candidate OBJ or native mesh is
permitted in product dependency closure.

The compiler also excludes two exact, signature-pinned false-positive source
components identified by full-resolution review: managed rows/yard and one
institutional roof. Together they remove 1,280 ownership pixels (20,480 square
meters), of which 1,009 pixels (16,144 square meters) were source-eligible.
There is deliberately no global imagery-likelihood threshold: the tested
threshold also removed real canopy. Exclusions fail closed if component shape,
position, source support, or hash changes.

The canopy manifest schema is
`questflight.photoreal-aerial-surface-v1.production-canopy-surface-layer.v1`.
It must assert terrain-map-only product policy, zero geometry ownership and
topology, exact handoff assignment, reviewed exclusions, stable hashes, and the
inactive status of the rejected candidate.

That schema continues to govern semantic ownership. The separate Aerial
Scenery V1 vegetation contract and `SOURCE_MANIFEST.json` govern the additive
rounded-grove presentation, its public-domain 3DEP-derived distribution, and
its project-authored geometry. The final vegetation source manifest is 9,197
bytes with SHA-256
`7358f7d5870d0ed9cc7b12d00baa39e35f5a7b87498dd0e88cc352a3ba505f32`.

## Ownership and exclusion contract

The top-level Workstream B manifest uses schema
`questflight.photoreal-aerial-surface-v1.manifest.v1`. It records the hashed
stage manifests, exact semantic owners, exclusions, radial handoffs, source
and license records, selected triangle totals, and known limitations. The
validator fails closed on missing or overlapping ownership, a stale referenced
hash, an unexpected schema or semantic root, an unpaired Unity metadata file,
or a budget/coverage error.

Its committed path is
`PhotorealAerialSurfaceV1/KBDU_AerialSurface_OwnershipExclusionManifestV1.json`.

Ownership priority is:

1. active hero buildings and existing hero trees;
2. `ProductionBuildingSurfaceLayer` inside 4 kilometers;
3. terrain canopy-map handoff for every eligible ordinary canopy pixel outside
   the hero radius;
4. terrain/orthophoto alone where no semantic owner applies.

This contract prevents the same footprint or canopy source pixel from being
claimed by two runtime representations.

## Product-AEQD bare-earth terrain

`build_product_terrain.py` preserves the accepted fixed terrain topology but
emits it in the product AEQD frame. It transforms every height query to UTM13N
before bilinear DTM sampling and evaluates runway preservation in product
coordinates. The runtime derivative has 46,426 vertices, exactly 92,000
triangles, and an exact `[-2048, 2048]` meter border on both horizontal axes.

Outside explicitly reported source cells whose internal elevation range exceeds
1.5 meters, reconstruction mean absolute error is 0.101071 meters, p95 is
0.357723 meters, and maximum is 1.387371 meters. The reconstructed runway plane
differs from its authored input by at most `4.528938e-7` meters. These are
mesh-to-source-raster reconstruction metrics, not surveyed real-world accuracy.

The OBJ is an authoring source only. The editor-only native mesh compiler uses
shared source vertices and area-weighted normals, preserves exact topology and
bounds, marks the result non-readable, and binds the identity-transformed native
asset to `Terrain_Near4km_USGS`.

## Validation

Run the Python unit and contract validation after generation:

```powershell
python -B -m unittest discover -s tools\aerial_surface\tests -v
python -B tools\aerial_surface\validate_aerial_surface.py
```

The validator must verify, at minimum:

- every referenced input and output byte count and SHA-256;
- the pinned 1024-by-1024, 4-meter affine grid and data encodings;
- exact class-mask exclusivity and heuristic-source disclosure;
- 100% non-hero building coverage in 0-2 km and at least 98% in 2-4 km;
- the two exact hero-building exclusions and their separate owner;
- simplification area, inflation, Hausdorff, topology, and 130,000-triangle bounds;
- all 16 building OBJ names, one-object/one-group structure, and no embedded material;
- disjoint building, hero-tree, zero-valued terrain-relief geometry, and terrain-map ownership;
- zero active canopy mesh batches, components, runs, vertices, triangles, skirts,
  and additional draws;
- all 63,308 eligible ordinary pixels in the terrain-map handoff, all 144 hero
  pixels preserved, and exact 100% assignment;
- the inactive, deterministic 29-component/1,960-triangle rejected candidate;
- exact canopy geometry-ownership and terrain-map-handoff texture names;
- exactly 42 fixed-world Tier-2 grove matrices, 540 triangles per source
  cluster, one instanced draw, the explicit Z-up-to-Y-up matrix, and no
  camera-facing/crossfade/per-tree runtime path;
- exact AEQD/UTM round trips, affine runtime residual, and data-texture sampling vectors;
- product terrain topology, bounds, reconstruction error, and runway preservation;
- native/source bounds and centroids with no generic OBJ runtime dependency; and
- canonical JSON, stable rerun output, and paired Unity `.meta` files.

After lead integration changes the serialized product visuals, run the standard
product gates:

```powershell
.\scripts\run_unity_tests.ps1 -TestPlatform EditMode
.\scripts\run_unity_tests.ps1 -TestPlatform PlayMode
.\scripts\validate_product.ps1
```

`validate_product.ps1` includes product integrity, dependency closure,
serialization, standard Visual QA, static render-budget checks, and the Android
build. Every full-resolution Visual QA image and the stereo Meta XR Simulator
captures still require human inspection. The simulator is development and
visual-regression evidence only; it is not physical Quest timing or thermal
evidence.

## Unity asset and GUID discipline

Every committed generated asset has a paired `.meta` file. Keep the asset and
metadata file together and preserve established GUIDs when moving or replacing
an asset. Workstream B generators may author their owned subfolder and asset
metadata but must not rewrite the shared
`PhotorealAerialSurfaceV1` root metadata. Its integration-owned folder GUID is
`b0fe91d928504e3e89264a3c377ebf12`.

Data textures are imported as linear, uncompressed, no-mip textures. Generated
building OBJ importers must not search for or create materials, animation, or
colliders; the semantic canopy-surface policy emits no OBJ assets. The separate
project-authored vegetation family is exported from its recorded Blender source
and committed through the repository's Git LFS rules.
Do not hand-edit generated assets or their manifests; change the deterministic
tool and regenerate. Do not commit `Library`, `Temp`, `Obj`, `Logs`, `Builds`,
APKs, captures, package caches, raw downloads, source archives, or assets with
unclear rights.

## Evidence and claim limits

These assets are not for navigation. The lidar source has no surveyed semantic
truth study for the inferred building/canopy channels and no demonstrated
vertical-error fraction for the selected building heights. OSM footprints are
not an architectural survey. Static triangle, byte, draw, and texture budgets
are planning evidence only.

Generation and static validation do not establish FAA approval, training
credit, certified fidelity, production photorealism, broad compatibility, or
Quest performance. Standalone Quest 3 remains the sole shipping target as a
self-contained Android ARM64, Vulkan, OpenXR application. Final frame-time,
thermal, comfort, and worn-display acceptance require physical Quest 3
evidence when hardware is available.
