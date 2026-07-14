# Product architecture

```text
ProductionWorldRoot
├── ProductionEnvironmentRoot (serialized baked environment)
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

The hierarchy exists before frame zero. OpenXR owns tracked camera motion. Calibration modifies only `UserViewCalibrationOffset`. Input produces a neutral control snapshot consumed by one `FlightDynamicsCoordinator`; `UnityPrototypeFlightBackend` is the sole serialized authority. Environment geometry, runway, water, and immutable far terrain are authored assets rather than runtime reconstruction.

The shipping architecture is standalone Quest 3 only: Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced stereo, ASTC, and 4x MSAA. A Windows/OpenXR process may exist only as a development host for Meta XR Simulator and is never a shipping profile or required runtime dependency. The simulator establishes stereo/tracking/input/composition behavior, not mobile GPU or thermal performance.

Quest render planning is fail-closed at 180 draw calls, 700,000 visible triangles, and 40 visible materials. Shared materials, instancing-compatible environment renderers, mipmapped ASTC textures, bounded opaque water, fixed far terrain, and no steady-state runtime hierarchy construction keep the budget predictable. Qualitative Editor overdraw and static memory reports complement—without replacing—the physical-headset gate.

The product Credits/About surface must expose the KOG_THORNS CC BY 4.0 attribution and `© OpenStreetMap contributors` with the ODbL URL. A missing or hash-mismatched aircraft asset is an Editor/build failure; the runtime never downloads or reconstructs it.
