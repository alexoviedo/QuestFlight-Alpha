# Product architecture

```text
ProductionWorldRoot
├── ProductionEnvironmentRoot (serialized baked environment)
│   ├── Shared-ENU near/mid/far terrain and orthophoto pyramid
│   ├── Authoritative fixed terrain surface and placement manifests (V2 branch)
│   ├── Offline-batched building, vegetation, and parked-aircraft HLODGroups
│   ├── Opaque dual-normal Boulder Reservoir plus batched secondary water (V2 branch)
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

The environment uses one east-north-up coordinate frame for the near, mid, and far terrain meshes and their public-domain orthophoto atlases. Explicit overlap bands blend the aligned pyramid levels without independent tile transforms. Shared materials, offline spatial batching, and authored LODGroups bound scene cost. These batches are not described as GPU instanced unless the actual renderer and shader variants prove it.

On Environment Reconstruction V2, `IProductionTerrainSurface` is the single query contract for height, normal, terrain zone, imagery UV, land-cover, runway/apron, water, and footprint-plane data. `ProductionPlacementManifest` serializes every grounded building, vegetation instance, parked aircraft, and represented water feature. Authoring and validation consume these assets; runtime code does not rebuild the world or repair its hierarchy.

Lighting is one directional sun plus a CC0 photographic HDR sky used for skybox, ambient lighting, and a static custom reflection. Boulder Reservoir is one opaque, dual-normal material with a static sky reflection; it has no planar reflection, realtime reflection probe, screen-space reflection, refraction, or tessellation. A bounded authored blob shadow supplies runway contact without a realtime aircraft shadow map. Volumetric clouds and screen-space atmosphere effects are excluded.

The V2 feature branch render plan fails closed above 130 draw calls, 650,000 visible triangles, 30 visible materials, 220 MiB of estimated texture memory, or three transparent material slots. Mipmapped ASTC textures, bounded effects, fixed terrain, and no steady-state runtime hierarchy construction keep the budget predictable. The 24-view Editor QA and static inventory complement—without replacing—the physical-headset gate.

Physical performance acceptance is fixed at 72 Hz and requires three distinct unrecorded runs of the same APK. Each run warms for 90 seconds and measures for 60 seconds. Only the physical Quest can establish app-PID CPU+GPU timing, stale/tear counts, thermals, controller/Guardian state, comfort, and worn-display quality; simulator or Editor timing cannot.

The product Credits/About surface must expose the KOG_THORNS CC BY 4.0 attribution and `© OpenStreetMap contributors` with the ODbL URL. A missing or hash-mismatched aircraft asset is an Editor/build failure; the runtime never downloads or reconstructs it.
