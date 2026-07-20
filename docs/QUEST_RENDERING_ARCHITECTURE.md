# Standalone Quest rendering architecture

## Shipping contract

QuestFlight ships only as a self-contained Meta Quest 3 application: Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced stereo, Linear color, ASTC textures, and 4x MSAA. PCVR, Link, Air Link, Windows shipping builds, remote rendering, and required-PC dependencies are excluded. A Windows/OpenXR process is allowed only as a Meta XR Simulator development host.

## Static planning budget

The authored production scene must remain within these conservative hard planning targets:

- no more than 120 estimated draw calls;
- no more than 600,000 visible triangles;
- no more than 25 visible materials;
- no more than 180 MiB estimated texture memory;
- no more than three transparent material slots;
- shared environment materials with offline spatial batching and fixed, explicitly validated representation tiers;
- controlled shader-keyword combinations;
- bounded transparent overdraw;
- no steady-state managed allocation in flight.

`scripts/run_quest_performance_budget.ps1` combines the standard 56-view Visual QA maxima with an authored-scene inventory. It emits `performance_budget_report.json/.md`, material/shader, texture-memory, and LOD/culling reports, plus qualitative overdraw images. These are planning and regression signals, not Quest timing evidence. Offline-batched renderers are not counted or described as GPU instanced unless their renderer settings and compiled shader variants establish that capability.

## Environment strategy

Near, mid, and far terrain use bounded, mipmapped public-domain orthophoto atlases in the same east-north-up coordinate frame. Recovery quadrants and a 12 km fallback are selected and blended in world space; their mip chains remain resident because custom-UV texture streaming is not yet proven. The far horizon is immutable, world-anchored, and free of runtime LOD crossfade, dithering, billboard behavior, or camera-relative scaling. Runway markings use controlled-depth opaque geometry. Context buildings use fixed closed footprint shells with no scale-changing runtime LOD, near vegetation uses two offline batches, and Tier 2/3 canopy is terrain-owned with no additional draw pass.

The atmosphere uses one CC0 photographic HDR sky and one directional sun. On the recovery branch water is intentionally orthophoto-only: the reservoir and minor-water renderer/material bindings are inactive, and the licensed source assets are retained only for later polish. Realtime reflection probes, planar reflections, SSR, refraction, tessellation, volumetric clouds, and screen-space atmosphere effects are excluded. A bounded blob shadow grounds the aircraft on the runway without enabling a realtime aircraft or cockpit shadow map.

The cockpit uses bounded AO, static reflection and material tuning, with no realtime cockpit shadow map or screen-space AO. OpenXR alone owns the tracked Main Camera. Neither render optimization nor simulator replay may write that camera or pivot the aircraft around it.

## Hardware acceptance

Only `scripts/run_standalone_quest_gate.ps1` on a physical Quest can establish sustained frame cadence, CPU/GPU behavior, stale/tear counts, thermal state, controller/Guardian state, or worn comfort and display acceptance. Acceptance requires three distinct unrecorded runs at 72 Hz against one immutable APK. Each run uses a 90-second warmup and a 60-second measurement, and every run must satisfy: app-PID CPU+GPU average no greater than 12.8 ms, p95 no greater than 13.889 ms, no more than 5% of samples over 13.889 ms, no more than two stale frames, zero tears, no thermal warning, and no crash.

The hardware gate's fallback runtime draw estimate counts visible, non-null MeshRenderer and SkinnedMeshRenderer material-submesh slots before batching; its triangle estimate counts triangle indices for those same rendered slots. Meta XR Simulator establishes OpenXR stereo, tracking, input replay, and composition behavior only. Simulator and Editor timing must never be represented as Quest hardware evidence.

The recovery branch has a short post-change worn smoke but no physical timing/thermal/stale/tear/allocation measurement. Its 7/10 human smoke score applies only to visual acceptance of the exact validated APK and cannot inherit or establish any hardware-performance result.
