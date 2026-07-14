# Standalone Quest rendering architecture

## Shipping contract

QuestFlight ships only as a self-contained Meta Quest 3 application: Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced stereo, Linear color, ASTC textures, and 4x MSAA. PCVR, Link, Air Link, Windows shipping builds, remote rendering, and required-PC dependencies are excluded. A Windows/OpenXR process is allowed only as a Meta XR Simulator development host.

## Static planning budget

The authored production scene must remain within these conservative view targets:

- no more than 180 estimated draw calls;
- no more than 700,000 visible triangles;
- no more than 40 visible materials;
- shared and instancing-compatible environment materials;
- controlled shader-keyword combinations;
- bounded transparent overdraw;
- no steady-state managed allocation in flight.

`scripts/run_quest_performance_budget.ps1` combines the standard 16-view Visual QA maxima with an authored-scene inventory. It emits `performance_budget_report.json/.md`, material/shader, texture-memory, and LOD/culling reports, plus qualitative overdraw images. These are planning and regression signals, not Quest timing evidence.

## Environment strategy

Near and mid terrain use bounded, mipmapped macro imagery blended with shared world-space microdetail. The far horizon is immutable, world-anchored, and free of runtime LOD crossfade, dithering, billboard behavior, or camera-relative scaling. Runway markings use controlled-depth opaque geometry. Boulder Reservoir uses one bounded Quest-safe material without SSR or refraction. Vegetation remains sparse and clustered, with shared materials and instancing where the authored assets support it.

The cockpit uses bounded AO, static reflection and material tuning, with no realtime cockpit shadow map or screen-space AO. OpenXR alone owns the tracked Main Camera. Neither render optimization nor simulator replay may write that camera or pivot the aircraft around it.

## Hardware acceptance

Only `scripts/run_standalone_quest_gate.ps1` on a physical Quest can establish sustained frame cadence, CPU/GPU behavior, stale/tear counts, thermal state, controller/Guardian state, or worn comfort and display acceptance. Its fallback runtime draw estimate counts visible, non-null MeshRenderer and SkinnedMeshRenderer material-submesh slots before batching or instancing; its triangle estimate counts triangle indices for those same rendered slots. Simulator and Editor evidence must remain labeled accordingly.
