# Product architecture

```text
ProductionWorldRoot
├── ProductionEnvironmentRoot (serialized baked environment)
│   ├── Product-AEQD near/mid/far terrain and aligned orthophoto pyramid
│   ├── Fixed DTM-only bare-earth terrain and semantic ownership manifests
│   ├── OSM-footprint building batches, authored hero objects, and parked-aircraft HLOD
│   ├── Terrain-owned canopy shading, one fixed-world Tier-2 grove batch, and orthophoto-only water
│   └── Bounded aircraft/runway blob shadow
├── ProductionInputRoot
├── AircraftSimulationRoot (one Unity prototype authority, CG pivot)
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

The hierarchy exists before frame zero. OpenXR owns tracked camera motion. Calibration modifies only `UserViewCalibrationOffset`. Input produces a neutral control snapshot consumed by one `FlightDynamicsCoordinator`; `UnityPrototypeFlightBackend` is the sole serialized authority. Environment geometry, runway, water, immutable far terrain, and presentation effects are authored assets rather than runtime reconstruction.

The shipping architecture is standalone Quest 3 only: Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced stereo, ASTC, and 4x MSAA. A Windows/OpenXR process may exist only as a development host for Meta XR Simulator and is never a shipping profile or required runtime dependency. The simulator establishes stereo/tracking/input/composition behavior, not mobile GPU or thermal performance.

Visible environment geometry uses one product-local WGS84 AEQD east-north-up coordinate frame for the near, mid, and far terrain meshes and aligned orthophoto atlases. Lidar data textures retain their source-relative UTM 13N raster frame and are sampled only through the pinned transform described below; treating the two numeric grids as identical is invalid. Explicit overlap bands blend the aligned imagery levels without independent tile transforms. Shared materials and offline spatial batching bound scene cost. The accepted `standalone-scenery-baseline-v1` context-building shells deliberately use no runtime scale-changing LODGroups; no batch is described as GPU instanced unless its renderer and shader variants prove it.

On Environment Reconstruction V2, `IProductionTerrainSurface` is the single query contract for height, normal, terrain zone, imagery UV, land-cover, runway/apron, water, and footprint-plane data. `ProductionPlacementManifest` serializes every grounded building, vegetation instance, parked aircraft, and represented water feature. Authoring and validation consume these assets; runtime code does not rebuild the world or repair its hierarchy.

Lighting is one directional sun plus a CC0 photographic HDR sky used for skybox and ambient lighting. The current feature candidate adds static, imagery-aware global and bounded airport reflection textures without a runtime probe render; this foundation is not yet visually accepted. Reservoir and minor-water renderers/material bindings remain inactive and the photographed orthophoto is the only water presentation. A bounded authored blob shadow supplies runway contact without a realtime aircraft shadow map. Volumetric clouds and screen-space atmosphere effects are excluded.

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
