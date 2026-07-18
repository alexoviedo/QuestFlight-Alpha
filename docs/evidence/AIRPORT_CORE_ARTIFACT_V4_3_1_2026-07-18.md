# Airport-Core Artifact V4.3.1 Evidence — 2026-07-18

## Scope and source

This bounded branch starts at V4.3 SHA `c6d7cafe4c74982df556b01e9bb78d6b717ddbd5`. It changes only the runway depth presentation, terrain-integrated minor-water response, and diagnostic/validation tooling. Reservoir, buildings, vegetation, terrain geometry, lighting, aircraft, physics, inputs, and world coverage are unchanged.

External evidence root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_v4_3_1_20260718_130050`.

## Runway render path and diagnosis

The authoritative pavement is `FAA_KBDU_Runway_08_26_Authoritative`, mesh `FAA_KBDU_Runway_08_26_Authoritative_Mesh`, material `AirportCoreContinuousRunway`, shader `QuestFlight/AirportCoreV4/ContinuousRunway`. The separate authoritative marking object is `FAA_KBDU_Runway_08_26_ControlledDepthMarkings`. Terrain below is `Terrain_AirportPatch_USGS` using `QuestFlight/KBDU Terrain Pyramid`.

Static inspection proves one active pavement renderer, one active marking renderer, one pavement submesh, exact pavement/collider mesh identity, no static batch, lightmap index -1, no vertex colors, and no obsolete runway renderer. The pavement mesh has 106 vertices and 104 upward-facing, nondegenerate triangles. Unity 6000.3.8f1's internal Frame Debugger API was present but returned zero local events in all four batch views; one interactive Editor attempt produced no report and was stopped after 180 seconds. `runway_draw_events.json` records that limitation rather than inventing events.

The R0–R11 matrix kept camera, exposure, sun, fog, mesh, UVs, and resolution constant. Bands remained in R1 (constant unlit white, markings disabled), excluding pavement texture channels and markings. Comparison to the underlying terrain found 52/106 pavement vertices below terrain, with signed clearance from -0.005092144 m to +0.004401267 m. Intermittent depth ownership is the supported root cause.

The final correction adds `Offset -1, -1` to the existing single opaque pavement pass. It changes no mesh, collider, transform, texture, material count, or draw count. The analyzer uses projected runway boundaries, 30 m low-pass filtering, cubic trend removal, and a 20–300 m FFT search. Baseline R1 peak-to-trough is 0.3206; final R0 is 0.0949 and passes the explicit 0.100 threshold. Lead scores are cockpit 7.0, low/close oblique 7.0, and aerial integration 7.0.

## Minor-water proof

The committed 512² packed mask remains bound to `KBDU_TerrainPyramid.mat`; zero minor-water ribbon/bank renderers are active. Evidence cameras select nonzero points inside three source polygons from `KBDU_WaterFeatureManifestV2.json`:

| Feature | Source ID | Zone | World ENU (m) | Mask pixels | Max / mean |
| --- | --- | --- | ---: | ---: | ---: |
| Hayden Reservoir | `osm:way/39486578/geometry:0` | near | -808.6, 50.8 | 706 | 1.000 / 0.556 |
| Valmont Reservoir | `osm:way/37351163/geometry:0` | mid | 3175.8, -832.0 | 306 | 0.902 / 0.328 |
| Baseline Reservoir | `osm:way/37350851/geometry:0` | mid | 1488.3, -5168.0 | 506 | 1.000 / 0.481 |

Each feature has 3.5 m ground, 38 m oblique, and 230 m aerial false-color, final-shaded, and orthophoto-only evidence. A single bounded HLSL adjustment strengthens reflection/tint and reduces roughness while preserving the orthophoto base, feathered mask, zero geometry, zero new renderer, zero new material, and zero new draw call. Lead minor-water and terrain/water integration scores are 7.0. OSM lineage remains ODbL-1.0/© OpenStreetMap contributors as recorded in the existing manifest.

## Reservoir regression

Reservoir geometry, shader, masks, materials, and authoring are byte-unchanged. Inspected cockpit and aerial views remain 7.0/10, with no gray suppression layer, terrain spill, or observed flicker.

## Budget

V4.3.1 is zero-net-cost relative to V4.3: 165 active renderers, 62 selected draws, 503,339 selected triangles, 689,275 serialized triangles, 24 visible materials, 168.08 MiB estimated textures, two transparent slots, 12 shaders/18 variants, and 4x MSAA. These are static Editor planning values, not Quest hardware performance evidence.

## Simulator and limitations

Meta XR Simulator v201.0 made a live Meta OpenXR stereo connection with Quest 3/72 Hz, HMD, and both Touch devices, and restored the prior OpenXR/persistent configuration. The aggregate gate remains `BLOCKED_MISSING_RECORDED_REPLAYS`: all ten required VRS sessions require one-time interaction with Meta's Record panel. No replay was synthesized, copied, or renamed. Physical Quest was not run because the deterministic replay gate did not pass.

## Product validation and build

`scripts/validate_product.ps1` passed dependency, serialization, source/license, EditMode 78/78, PlayMode 9/9, 56-view machine Visual QA, static budget, and Android release-style build gates under Unity 6000.3.8f1. The scene is `Assets/Scenes/ProductionVerticalSlice.unity`, package `com.alexoviedo.t2.questflight`. The uncommitted APK is 99,745,119 bytes with SHA-256 `c9a31d53a452cd05a39d293be6fe048bf3e94cc89220152a6591d4e68ef68e59`. An earlier focused EditMode invocation ran before the standard Android/ASTC configurator and reported 77/78 solely on that configuration check; the integrated gate configured the intended target and passed 78/78 without weakening a test.

## Decision

PARTIAL — preserve and push the branch, do not merge to main. The visual correction and proof pass their bounded lead gates, but exact Frame Debugger event capture and deterministic recorded stereo replay are incomplete. This document does not claim production photorealism, Quest hardware performance, FAA approval, training credit, navigation accuracy, or final physical fidelity.
