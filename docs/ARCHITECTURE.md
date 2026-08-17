# Product architecture

```text
ProductionWorldRoot
├── ProductionEnvironmentRoot (serialized baked environment)
│   ├── Product-AEQD near/mid/far terrain and aligned orthophoto pyramid
│   ├── Fixed DTM-only bare-earth terrain and semantic ownership manifests
│   ├── OSM-footprint building batches, authored hero objects, and parked-aircraft HLOD
│   ├── Terrain-owned far canopy, one aircraft-centered three-draw vegetation clipmap, and orthophoto-only water
│   ├── Bounded instanced runway-side grass and one combined airport-detail mesh
│   ├── Bounded aircraft/runway blob shadow
│   └── One-batch, bounded VDB-derived KBDU cloud-volume field
├── ProductionInputRoot
├── AircraftSimulationRoot (one native JSBSim authority, CG pivot)
│   ├── CenterOfGravityReference
│   └── AircraftVisualRoot
│       ├── ImportedAircraftExterior
│       ├── ImportedCockpitInterior
│       ├── AnimatedControlSurfaces
│       └── PilotSeatAnchor
│           └── UserViewCalibrationOffset
│               └── XR Origin
│                   ├── Camera Offset / Main Camera
│                   ├── Left Touch Controller
│                   └── Right Touch Controller
└── ProductionLightingRoot
```

`AirportNearFieldDetailRoot` is an explicitly authored child of
`AirportContext`. Its 1,000 deterministic grass placements are submitted by one
allocation-free `DrawMeshInstanced` source and culled per camera at 340 m. A
second, single-submesh renderer combines the north utility corridor, narrow
runway shoulders, runway-edge lights, one source-located windsock, and bounded
taxiway signs. Both sources reuse the existing opaque context material, cast no
realtime shadows, add no colliders or `LODGroup`, and never construct hierarchy
at runtime.

The hierarchy exists before frame zero. OpenXR owns tracked camera motion. Calibration modifies only `UserViewCalibrationOffset`. Input produces a neutral control snapshot consumed by one `FlightDynamicsCoordinator`; its sole backend is the embedded native JSBSim 1.3.1 C172P derivative. The coordinator prepares the integrity-checked native payload, acquires one authority lease, and fails closed on preparation, initialization, ABI, non-finite-state, reset, or step failure. There is no alternate Unity flight model or runtime fallback. Environment geometry, runway, water, immutable far terrain, and presentation effects are authored assets rather than runtime reconstruction.

`FullMapBuildingClipmapV1.2` replaces the former coverage/detail representation
switch with one serialized 4x4 stable closed-shell pool. Offline tools compile
all 57,407 official City/County footprint components into 576 one-kilometre
pages. Every represented building keeps walls and a roof from all viewing
distances; a page never changes geometry merely because it contains the
aircraft. Landmarks retain exact footprints. Ordinary footprints are simplified
offline under explicit area, inflation, centroid-shift, and boundary-error
ceilings, and the same ring authors both walls and roof so the shell cannot
separate.
The aircraft simulation frame—not the camera—selects the visible window, while
prediction prefetches only. PageKey ownership retains 12 slots on a one-axis
shift and nine on a diagonal shift; entering pages load before one atomic commit.
CRC-indexed LZ4 Android AssetBundles remain resident while referenced and are
released only with `UnloadAsync(true)` after detachment. Runtime code creates
neither meshes nor hierarchy. One opaque material uses three 16-slice ASTC
texture arrays for metre-scaled facades. County Assessor exterior-wall records
drive authoritative assignments where available; parcel construction records
and deterministic land-use/style rules are confidence-ranked fallbacks.
Openings are shader masks with complete metre-sized bays, floor/eave bounds, a
single inferred front door, and class-specific layouts; glass is an opaque
reflective response and cannot sample the brick/siding channel. Roofs retain
the existing world-projected orthophoto representation. The authored airport
hero corridor remains a separate authority and is excluded from the database.

The shipping architecture is standalone Quest 3 only: Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced stereo, ASTC, and 4x MSAA. A Windows/OpenXR process may exist only as a development host for Meta XR Simulator and is never a shipping profile or required runtime dependency. The simulator establishes stereo/tracking/input/composition behavior, not mobile GPU or thermal performance.

Visible environment geometry uses one product-local WGS84 AEQD east-north-up coordinate frame for the near, mid, and far terrain meshes and aligned orthophoto atlases. Lidar data textures retain their source-relative UTM 13N raster frame and are sampled only through the pinned transform described below; treating the two numeric grids as identical is invalid. Explicit overlap bands blend the aligned imagery levels without independent tile transforms. Shared materials and offline spatial batching bound scene cost. The accepted `standalone-scenery-baseline-v1` context-building shells deliberately use no runtime scale-changing LODGroups; no batch is described as GPU instanced unless its renderer and shader variants prove it.

On Environment Reconstruction V2, `IProductionTerrainSurface` is the single query contract for height, normal, terrain zone, imagery UV, land-cover, runway/apron, water, and footprint-plane data. `ProductionPlacementManifest` serializes every grounded building, vegetation instance, parked aircraft, and represented water feature. Authoring and validation consume these assets; runtime code does not rebuild the world or repair its hierarchy.

Lighting is one directional sun plus a CC0 photographic HDR sky used for skybox and ambient lighting. The current feature candidate adds static, imagery-aware global and bounded airport reflection textures without a runtime probe render; this foundation is not yet visually accepted. Reservoir and minor-water renderers/material bindings remain inactive and the photographed orthophoto is the only water presentation. A bounded authored blob shadow supplies runway contact without a realtime aircraft shadow map. `BoulderCloudFieldRoot` contains eight explicitly authored fair-weather cumulus volumes in one premultiplied-transparent 96-triangle, one-material render batch. A bounded object-space raymarch samples a project-owned VDB-derived density-slice atlas with a fixed maximum of 48 steps, trilinear slice interpolation, early exit, shaded tops/bases, and smooth fly-through extinction. The volumes neither face the camera nor use a cloud camera, particles, transparent cards, temporal reprojection, realtime cloud shadows, or runtime hierarchy construction. This is a deliberately sparse Quest representation rather than a full-screen meteorological volume renderer.

The accepted-baseline planning gate fails closed above 120 draw calls, 600,000 visible triangles, 25 visible materials, 180 MiB of estimated texture memory, or three transparent material slots. Mipmapped ASTC textures, bounded effects, fixed terrain, and no steady-state runtime hierarchy construction keep the budget predictable. Visual-QA capture counts are milestone-specific (81 views on the current feature candidate); Editor QA and static inventory complement—without replacing—the physical-headset gate.

Physical performance acceptance is fixed at 72 Hz and requires three distinct unrecorded runs of the same APK. Each run warms for 90 seconds and measures for 60 seconds. Only the physical Quest can establish app-PID CPU+GPU timing, stale/tear counts, thermals, controller/Guardian state, comfort, and worn-display quality; simulator or Editor timing cannot.

On `feature/airport-core-photoreal-v4`, `AirportCoreBenchmarkRoot` is a serialized, independently toggleable A/B overlay inside an approximately 700 m square airport-core bound. `AirportCoreBenchmarkManifest` binds the two replacement source features, shared material atlases, clean plate, lighting profile, runway, reservoir, vegetation, and source records; `AirportCoreBenchmarkValidator` fails closed on serialized structural contracts. The overlay does not reconstruct or repair the scene at runtime. Its current structural subset is 27 renderers, 74,730 serialized triangles, and three materials, but the complete scene fails V4 visual, material, texture-memory, aggregate integrity, and build gates. The branch is therefore preserved for bounded rework and is not scaled or merged.

On `feature/airport-core-photoreal-v4-2`, the same authored benchmark uses a deterministic visual-only derivative of the V2 airport terrain. It preserves exact 5 m samples on the outer boundary, runway corridor, terminal/apron/vegetation bounds, and fuel-service bounds; refines any remaining cell above the 1.6 m source-plane deviation limit; and uses conforming transition fans. The authoritative `ProductionTerrainSurfaceAsset`, colliders, runway geometry, and physics queries remain unchanged. Static GI is limited to terminal and hangar LOD0 and serialized as one finite 512² non-directional atlas with 18 probes. These changes close static geometry and bake contracts but do not make the art visually accepted.

On `feature/airport-core-artifact-v4-3`, Boulder Reservoir remains one opaque authoritative mesh but no longer relies on a rectangular terrain-suppression layer. Its shader combines the retained shore/depth/roughness mask, dual normals, static environment reflection, and the existing mid-zone orthophoto sampled in the terrain world frame. Minor OSM-derived water is rasterized offline into one packed near/mid RGBA terrain mask; the old surface and bank ribbon renderers are absent, so minor water adds no renderer, material, or draw call. The runway retains its exact authoritative mesh, grade, collider, and marking geometry and uses one runway-local material. These systems are structurally valid and cheaper than V4.2, but the runway and minor-water lead visual gates remain failed; the branch is not accepted or merged.

On `feature/aerial-scenery-representation-v1`, one explicitly serialized `AerialSceneryRoot` replaces the active sparse-scatter representation without runtime construction. Tier 0/1 vegetation is compiled offline into two fixed-orientation heterogeneous batches; Tier 2 uses one fixed upper-canopy aggregate batch; Tier 3 contributes canopy height/normal/occlusion through the existing terrain draw. No tree owns a GameObject or `Update`, no ordinary billboard rotates toward either eye, and no LOD crossfade/dither is used. The product root disables five legacy vegetation roots and twenty legacy building groups.

The same root binds a 92,000-triangle fixed lidar near mesh and a conformed airport patch in the shared ENU frame. Context buildings use exact OSM footprints near the airport, footprint-union simplification in the middle tier, and orthophoto-only representation for 17,984 far records. Lidar building heights fail the declared confidence gate and therefore produce zero height overrides; deterministic fallback heights remain authoritative. The disabled groups still reside in the combined context-prefab package closure because that prefab also owns non-building context, so package-size removal is not claimed.

This branch remains a failed visual benchmark despite structural and budget validity. The bounded Tier-2 aggregate does not yet read as natural canopy, near vegetation is sparse, and static stills do not prove an invisible building LOD transition. Its representation is evidence for the data/serialization architecture, not production photorealism or final Quest scenery.

On `feature/aerial-scenery-recovery-v2`, the active context representation covers all 27,679 bounded OSM source features exactly once: 2,875 fixed closed shells (26 hero, 1,152 near, 1,697 mid) and 24,804 ordinary features represented by orthophoto only. Sixteen shell renderers share one opaque material, total 46,005 triangles, and use no runtime LODGroup or per-building update. Tier 0/1 vegetation contains 22 deterministic placements (3/19) in two draws, 12,860 triangles, and one alpha-clipped material. Tier 2 owns no geometry or draw; Tier 2/3 canopy response comes from the orthophoto plus lidar height/normal/occlusion maps within the local 4,096 m square coverage.

The same recovery adds four 2048² hot-zone ground quadrants at 1.984375 m/texel and one 4096² 12 km fallback at 2.9296875 m/texel, derived from the official public-domain 2019 USGS/USDA NAIP Plus source. World-space selection blends the original near atlas through the quadrants and fallback without runtime hierarchy changes. Mip chains remain resident because a correct custom-UV streaming contract has not been proven. This removes gross repetition at normal flight distances but does not restore source detail at taxi range. This representation is now the tagged standalone scenery development baseline. Machine review remains 5.3/10 while the prior worn smoke was approximately 7/10; neither score is a photorealism, public-release, or final performance claim.

On `feature/full-map-aerial-clipmap-v1`, one serialized
`AircraftCenteredAerialClipmapController` upgrades the complete 24 km square
without making sharpness airport-relative. Offline authoring converts the
official City of Boulder 2020 CC0 imagery into 1,296 high pages at 0.333 m/texel,
576 medium pages at 1.0 m/texel, and one 5.859 m/texel global fallback. Android
AssetBundles are self-contained build inputs; the runtime performs no network
request. The aircraft simulation root selects fixed 3x3 high and medium page
windows, preloads into staging `Texture2DArray` objects, and atomically swaps a
complete window. The Main Camera and XR hierarchy are never written or used as
spatial authority. Active plus staging arrays and the global image consume a
bounded 23,889,952-byte (22.78 MiB) ASTC planning footprint. The accepted fixed
pyramid remains visible during startup, then its explicit mip requests are
released only after all new tiers are ready. This feature changes imagery
selection, not terrain geometry, collision, buildings, vegetation, water,
flight physics, or the authored environment hierarchy.

On `feature/photoreal-aerial-surface-v1`, the product uses a hybrid semantic
surface rather than applying lidar DSM/nDSM to one raised ground mesh.
`ProductionBareEarthTerrain` is sampled only from the 3DEP DTM;
`ProductionBuildingSurfaceLayer` contains 9,692 OSM-footprint buildings in 16
offline batches / 128,092 triangles; `ProductionCanopySurfaceLayer` is a
fail-closed zero-geometry ownership root; and existing authored airport objects
own the hero layer. Ordinary canopy outside the 250 m hero radius modifies the
existing terrain draw through height, density, normal, occlusion, and ownership
maps. The 63,308 terrain-map pixels plus 144 hero-owned pixels account for all
63,452 eligible pixels exactly once. A 29-component / 1,960-triangle raised
candidate is recorded only as rejected evidence because its blocks and closing
skirts read as false ground relief.

The lidar rasters retain origin-relative UTM zone 13N coordinates while product
geometry, OSM, orthophoto, and Unity world coordinates remain in the KBDU-local
WGS84 AEQD frame. Their deltas differ by up to 7.444086 m. Offline generation
uses an exact AEQD↔UTM transform; the runtime sampling affine stays within
0.002037 m and rotates source normals into product axes. A coordinate-preserving
editor compiler bypasses Unity's generic OBJ handedness conversion, so the
serialized terrain and building native meshes use identity transforms without
runtime repair. This architecture is specified in
`docs/AERIAL_SURFACE_ARCHITECTURE.md` and
`docs/DSM_BUILDING_CANOPY_PIPELINE.md`.

The same branch uses a fixed seven-texture City of Boulder 2020 CC0 pyramid
(0.48828125 m/texel near, 1.46484375 m/texel pattern base, 5.859375 m/texel
far) with explicit mip-residency ownership. Its scoped static planning result is
53 selected draws, 601,406 selected triangles, 26 visible materials,
211,849,528 estimated texture bytes, three transparent slots, and 4x MSAA. The
original machine gates and Android build passed, but the first lead review
remained below its visual bar. A later worn smoke of the exact APK scored 8/10
and the commit became the tagged development baseline
`photoreal-aerial-surface-v1`. That tag is not a literal-photorealism, release,
or final Quest-performance claim.

On `feature/rounded-canopy-volume-v1`, the tagged Photoreal Aerial Surface V1
terrain-map representation remains active and owns the broad aligned canopy
response. A bounded additive Tier-2 layer submits 42 project-authored
`AerialGroveCluster_T2` matrices through one `DrawMeshInstanced` call and the
existing shared vegetation material. Each cluster has 540 triangles; the layer
adds 22,680 triangles and one draw while adding no texture, material,
transparent slot, shader, or shader variant. Thirty clusters lie within 1.5 km.

The matrices are fixed in world orientation and do not select or rotate per
eye. The editor authorer explicitly right-multiplies
`Quaternion.Euler(-90, 0, 0)` to convert the Blender Z-up mesh to Unity Y-up
without changing ENU placement or yaw. T0/T1 remain the existing 22 instances,
12,860 triangles, and two draws; total vegetation is 35,540 triangles, three
draws, and one material. No ordinary tree owns a runtime GameObject or
`Update`, and no LOD crossfade, dither, or camera-facing billboard is used.

Lidar/CHM supplies only placement, height, and density evidence. It never
displaces the DTM terrain and does not establish botanical or surveyed truth.
The live Meta XR stereo/HMD/Touch check passes, but recorded motion/transition
replays and physical Quest timing remain unproven.

On `feature/airport-core-artifact-v4-3-1`, deterministic R0–R11 isolation proves that runway bands persist with constant unlit pavement and no markings. The runway has one pavement renderer and one marking renderer, no lightmap, no vertex colors, no duplicate, and an exact pavement/collider mesh identity; 52 of 106 pavement vertices nevertheless intersect the terrain over a signed clearance range of -5.09 to +4.40 mm. The existing opaque pavement pass therefore uses a bounded depth bias (`Offset -1, -1`) without changing geometry, physics, transforms, materials, textures, or draw count. Minor-water evidence cameras prove three OSM-backed nonzero mask components at ground, oblique, and aerial ranges; the architecture remains terrain-integrated and zero-draw-call. Reservoir assets are unchanged.

The final V4 camera-aware estimator selects one renderer per `LODGroup` and respects the camera mask, frustum, and `forceRenderingOff`. Its per-view results are distinct from the aggregate ProductIntegrity inventory, which intentionally counts inactive A/B content and every serialized LOD. On V4.2 the scopes report 506,254 maximum selected-LOD triangles versus 698,328 serialized aggregate triangles. Neither estimator is physical Quest timing evidence.

The product Credits/About surface must expose the KOG_THORNS CC BY 4.0 attribution and `© OpenStreetMap contributors` with the ODbL URL. A missing or hash-mismatched aircraft asset is an Editor/build failure; the runtime never downloads or reconstructs it.

## Worn Visual Runtime V1

The candidate on `fix/worn-visual-runtime-v1` replaces the former 42 repeated
Tier-2 cues with 886 deterministic semantic groves compiled offline from the
aligned canopy, orthophoto, DTM, and ownership masks. All 143 groves inside the
650 m near-detail radius use 696 grounded trees. Each tree has one tapered
three-sided trunk, three tapered branches, and twelve fixed-world foliage cards
selected from a project-owned sixteen-cell leaf-and-twig atlas. The cards are
not camera-facing and use one shared opaque-budget, alpha-tested material with
4x-MSAA alpha-to-coverage. Medium groves retain inexpensive rounded aggregate
shells, and the terrain canopy map owns the remaining far response. Four fixed
native batches contain 58,018 triangles; there are no runtime callbacks,
per-tree GameObjects, crossfades, camera-relative rotations, or per-eye choices.

The 9,692 accepted OSM footprint shells remain 16 fixed batches and 128,092
triangles. Offline authoring now emits per-wall vertices, physical metre-scale
UV coordinates, a stable entrance wall, and semantic residential, commercial,
industrial, and civic classification. One opaque shared shader derives coherent
category palettes, floor/module scale, corner margins, entrance-only doors,
and cubemap-backed window glint without adding a renderer, material, texture,
transparent pass, or runtime update. These remain inferred context shells, not
surveyed hero architecture.

The full cabin-glass mesh owns one single-pass finite-volume reflection material.
For an eye inside the authored cabin bounds, it samples a static cubemap baked
from the actual imported panel, gauges, yokes, seats, doors, headliner, and window
openings; exterior views retain the existing environment cubemap. A broad
instrument-cover mesh uses the same geometry-derived cabin reflection, while the
redundant single-lens shell is disabled. Cockpit depth is geometry-bound to the
existing opaque interior surfaces: twenty renderers carry four offline raycast
visibility channels (+Z, +X, -Z, -X) in additional vertex streams. The stable
single-pass shader interpolates those channels using aircraft-local sun direction
while preserving imported base, normal, metallic/roughness, emission, and AO.

The accepted local-shadow layer uses one explicitly authored, disabled
orthographic camera and one disabled 120-triangle cabin-frame proxy. During the
aircraft lighting controller's late update, the proxy is enabled for one
offscreen draw into a 512-square `R16G16_SFloat` depth/depth-squared target and
immediately disabled. Hardware-filtered mip moments create a continuous
world-locked penumbra in one receiver lookup; the same map and transform are
sampled by both eyes. There is no Main Camera dependency, runtime hierarchy
construction, additional light, Unity shadow cascade, projector, transparent
overlay, or per-eye orientation. Instrument emission remains outside shadow
attenuation. The rejected free-floating overlay and the earlier sparse-PCF edge
remain absent. This preserves one XR Origin, one tracked Main Camera, one flight
authority, and standalone Android/OpenXR ownership.

## Bounded road traffic

`KBDU_TrafficRoot` is one explicitly serialized child of
`ProductionEnvironmentRoot`. Its manifest is compiled offline from the bounded
OpenStreetMap road network, the existing terrain surface, and 27,948
source-visible parked-vehicle observations recovered from the native City of
Boulder 2020 nine-inch imagery. The offline scan searches a 100 m source
envelope around every one of 27,679 OSM buildings in the 12 km product area,
plus the developed Boulder Reservoir parking envelope. Candidates on roads or
highways, aeroways, water, and building footprints are rejected. Runtime code
never creates traffic GameObjects, modifies the product hierarchy, invokes
physics, searches for a road, or runs the detector.

One `KbduTrafficRuntime` parses the compact manifest once and fills preallocated
matrix and paint arrays during camera pre-cull. The parked authority is sorted
into 378 fixed 500 m cells; only cells intersecting the 4.5 km hysteretic
visibility envelope are visited, then allocation-free max-heaps retain at most
64 detailed and 1,800 far-proxy vehicles. Five optimized CC0 passenger
archetypes—sedan, hatchback, crossover/SUV, pickup, and cargo van—and one
semi-trailer archetype share one opaque instanced shader/material. Three far
batches share two project-owned proxy meshes. Exclusive vertex-color classes
preserve body, glass, tire, wheel/trim, headlamp, tail-lamp, and trailer
response; eight market-calibrated paint tints add variety without material
clones.

There are no serialized traffic renderers, per-car components, colliders,
rigidbodies, transparent windows, realtime vehicle shadows, wheel animations,
or per-car `Update` calls. Seven hundred eighty-eight moving records follow 361
terrain-sampled directional lane routes under the fixed-step collision-safe
controller. Every parked record retains its native-source position and recovered
long axis. At most nine opaque instanced draws and 129,312 vehicle triangles are
submitted. The fleet uses one 128-square ASTC detail texture, one material, no
transparent slot, and zero steady-state managed allocation by design.

## KBDU airport perimeter fence

`AirportPerimeterFenceRoot` is one explicitly serialized child of
`ProductionEnvironmentRoot/AirportContext`. Four fixed project-authored meshes
follow the bounded OSM aerodrome perimeter and preserve the mixed wildlife,
black chain-link, and weathered west-hangar character found in City planning
evidence. The perimeter is a visual approximation, not surveyed security data.

Terrain-conformed panels, bounded physical rails/posts, continuous distance UVs,
and vertex metadata let one dedicated project-authored material render green
privacy slats with a galvanized diamond pattern. The complete 4.27 km perimeter
is 252 triangles and at most four draws. It adds one material but no texture,
collider, realtime shadow, LOD group, runtime callback, or hierarchy
construction.

## Golden Eagle and shared thermal

`ProductionEnvironmentRoot/WildlifeRoot` owns one serialized
`GoldenEagleThermalRoot`. The bird has one skinned renderer. Its first lesson
encounter follows an authored fixed-world cubic path at bounded airspeed, then
joins a deterministic orbit around a shared `ThermalFieldProfile`. It uses
explicit flap, soar, bank, and glide-transition clips; runtime code does not
construct, discover, repair, or camera-parent hierarchy.

The release coordinator samples the thermal at the authoritative aircraft
position and submits vertical wind through the existing JSBSim atmosphere ABI.
This preserves one flight authority and does not layer a Unity force onto the
native aircraft model.

## Full-map projected building roofs

Ordinary full-map building shells retain the stable 576-page, 16-renderer
geometry architecture. Their shared opaque shader uses the existing semantic
roof vertex flag to sample the terrain orthophoto vertically in world space;
facades continue to use the procedural facade atlas. Terrain and roofs share
`AerialClipmapSampling.cginc`, fixed fallback mappings, and the aircraft-centered
3x3 high/medium runtime page arrays. One controller commits both material
bindings atomically. This adds no renderer, draw, material, transparent slot,
runtime texture, or hierarchy object. Airport hero buildings remain on authored
materials because their clean-plate imagery no longer contains source roofs.

## Full-map watertight building shells

The stable full-map shell compiler seals each ordinary building against both
its roof and authoritative terrain. Wall-top endpoints are the exact shared
roof-perimeter vertices, including pitched-roof height, rather than a separate
flat wall cap. Each wall-bottom endpoint samples
`KBDU_ProductionTerrainSurfaceV2` at its footprint coordinate and extends 1.60
m below that surface as a hidden foundation skirt. The 576-page geometry and
16-renderer runtime pool are unchanged.

The compiler removes consecutive duplicate ring points, uses local-origin
double-precision area tests, and accepts a roof only when exactly `n - 2`
triangles cover every perimeter vertex. A post-compile mesh audit rejects any
roof-wall mismatch over 1 mm, terrain-unsealed wall bottom, or unshared vertical
wall corner. This is offline authoring; no runtime repair or hierarchy creation
is introduced.

## Full-map vegetation clipmap V2

The 24 km square vegetation representation is compiled offline from the 2020
orthophoto, authoritative full-map building footprints, and the production
terrain height source. It stores quantized crown/trunk placements in 576 fixed
1 km pages. The player does not classify imagery or create hierarchy: one
serialized `AirportCloseTreeLodRenderer` draws the existing 344/122/2-triangle
tree tiers through one shared material and bounded instanced batches.

Vegetation shares the building clipmap's grid, committed/prefetched pages, and
stable aircraft direction, but building mesh residency no longer limits far-tree
visibility. A stable 96-page aircraft-oriented envelope extends 10 km forward,
7 km laterally, and 3 km rearward. Every retained page contributes the same 144
deterministic far samples; committed and prefetched building pages are mandatory
members. A one-page exit halo prevents boundary churn, and head turns cannot
evict forward coverage. Up to 24/240/4,875 visible instances use the
near/middle/far tiers in at most seven draws and 50,694 transition-inclusive
triangles. Projected
screen size and two-second hysteresis control only true mesh-tier changes; there
is no culled-to-visible or page-entry fade. The rejected four opaque grove hulls
remain outside the product dependency closure, and the orthophoto remains the
sub-pixel canopy authority. No tree owns a GameObject or `Update` method.
