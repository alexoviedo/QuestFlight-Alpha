# Standalone Quest rendering architecture

## Shipping contract

QuestFlight ships only as a self-contained Meta Quest 3 application: Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced stereo, Linear color, ASTC textures, and 4x MSAA. PCVR, Link, Air Link, Windows shipping builds, remote rendering, and required-PC dependencies are excluded. A Windows/OpenXR process is allowed only as a Meta XR Simulator development host.

## Static planning budget

The authored production scene must remain within these conservative hard planning targets:

- no more than 120 estimated draw calls;
- no more than 500,000 visible triangles;
- no more than 30 visible materials;
- no more than 220 MiB estimated texture memory;
- no more than two transparent material slots;
- shared environment materials with offline spatial batching and authored LODGroups;
- controlled shader-keyword combinations;
- bounded transparent overdraw;
- no steady-state managed allocation in flight.

`scripts/run_quest_performance_budget.ps1` combines the standard 24-view Visual QA maxima with an authored-scene inventory. It emits `performance_budget_report.json/.md`, material/shader, texture-memory, and LOD/culling reports, plus qualitative overdraw images. These are planning and regression signals, not Quest timing evidence. Offline-batched renderers are not counted or described as GPU instanced unless their renderer settings and compiled shader variants establish that capability.

## Environment strategy

Near, mid, and far terrain use bounded, mipmapped public-domain orthophoto atlases in the same east-north-up coordinate frame. Explicit world-space overlap bands blend adjacent levels, while shared nonrepeating microdetail fades with distance. The far horizon is immutable, world-anchored, and free of runtime LOD crossfade, dithering, billboard behavior, or camera-relative scaling. Runway markings use controlled-depth opaque geometry. Sparse airport, context, and vegetation assets use shared materials, offline spatial batching, and authored LODGroups.

The atmosphere uses one CC0 photographic HDR sky and one directional sun. The same static sky environment supplies ambient lighting and the custom reflection used by Boulder Reservoir. The reservoir is one bounded opaque material with two low-frequency normal layers and shoreline/depth control. Realtime reflection probes, planar reflections, SSR, refraction, tessellation, volumetric clouds, and screen-space atmosphere effects are excluded. A bounded blob shadow grounds the aircraft on the runway without enabling a realtime aircraft or cockpit shadow map.

The cockpit uses bounded AO, static reflection and material tuning, with no realtime cockpit shadow map or screen-space AO. OpenXR alone owns the tracked Main Camera. Neither render optimization nor simulator replay may write that camera or pivot the aircraft around it.

## Hardware acceptance

Only `scripts/run_standalone_quest_gate.ps1` on a physical Quest can establish sustained frame cadence, CPU/GPU behavior, stale/tear counts, thermal state, controller/Guardian state, or worn comfort and display acceptance. Acceptance requires three distinct unrecorded runs at 72 Hz against one immutable APK. Each run uses a 90-second warmup and a 60-second measurement, and every run must satisfy: app-PID CPU+GPU average no greater than 12.8 ms, p95 no greater than 13.889 ms, no more than 5% of samples over 13.889 ms, no more than two stale frames, zero tears, no thermal warning, and no crash.

The hardware gate's fallback runtime draw estimate counts visible, non-null MeshRenderer and SkinnedMeshRenderer material-submesh slots before batching; its triangle estimate counts triangle indices for those same rendered slots. Meta XR Simulator establishes OpenXR stereo, tracking, input replay, and composition behavior only. Simulator and Editor timing must never be represented as Quest hardware evidence.
