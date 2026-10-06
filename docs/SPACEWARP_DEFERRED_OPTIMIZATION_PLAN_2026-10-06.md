# Deferred SpaceWarp optimization plan

Status: deferred by the user on October 6, 2026. Preserve the implementation on main; do not implement this plan without renewed approval. SpaceWarp remains opt-in. `ProductSpaceWarpController.Requested` defaults the saved preference to zero; existing explicit user preferences remain respected.

## Preserved result

Canonical development APK `cloud_optimization_20261005/quest_volume_resume_20261006/QuestFlight-cloud-volume.apk`, SHA-256 `588fbfbabb27d9a6ef8aa530f98501fab0d86b6eabe8bbbf9500fd3062d90b82`, built successfully and verified against the installed Quest APK. All 1,912 scenery payload files verified. Full working-source identity accompanies the APK; the old HEAD alone does not describe it.

October 6 unattended 60-second demo windows on that APK:

| Configuration | App FPS | Mean GPU aggregate | p95 GPU aggregate |
|---|---:|---:|---:|
| Optimized cloud volume, SpaceWarp ON | 30.51 | 27.05 ms | 31.40 ms |
| Atlas reference, SpaceWarp ON | 29.11 | 28.78 ms | 31.47 ms |
| Optimized cloud volume, SpaceWarp OFF | 35.59 | 23.20 ms | 27.27 ms |

Evidence: external `cloud_optimization_20261005/hardware_recheck_20261006.json`. Temperatures differed (40–41, 45–46, 46–47 C respectively). GPU values are one-second VrApi aggregates, not per-frame percentiles. Readiness passed and no focus-loss markers were detected during the windows. These results do not establish native 72 FPS, stable SpaceWarp 36 FPS, thermal acceptance, or head-turn visual acceptance.

Keep hardware-filtered 64x32x64 density sampling enabled and tested empty-space skipping disabled. Cached density and approximate top-light transmittance already exist; there is no secondary sunlight march or expensive procedural-noise generation to remove. Color and motion use 48/24/12-step LOD and early termination. The motion pass uses weighted cloud depth, not proxy-box depth. Compiler removal of unused lighting must be checked before claiming duplicate lighting cost.

## Why the earlier proposal was too optimistic

An older clouds-disabled diagnostic reached 35.98 FPS on average but had a 39.8 ms delivered-frame p95. It establishes substantial cloud cost, not stable cadence. A half-width/half-height color target shades one-quarter as many pixels, but does not quarter all cloud cost: motion rendering, depth handling, reconstruction, bandwidth and foveation remain relevant. Stable timing also does not guarantee acceptable transparent-object extrapolation.

Subjective planning estimates, not measured probabilities: worthwhile cloud-only gain without an obvious downgrade 70%; cloud-only sustained representative 36 FPS 30%; cloud plus demonstrated pipeline savings achieving sustained representative 36 FPS 50%; also obtaining acceptable SpaceWarp image quality 35%. Revise these estimates after measurements; do not present them as guarantees.

## Approved later only: finite sequence

1. Reuse current diagnostic automation for alternating volume/ clouds-off windows, same route and headset placement, comparable warm-up and temperatures. Capture a GPU-pass breakdown and CPU scheduling trace separately from timing. Determine color-cloud, cloud-motion, transfer/resolve and critical CPU costs. Reject invalid tracking/focus windows. Do not build another benchmark framework. A clouds-off run is diagnostic only.
2. Inspect intermediate color and depth consumers first. `ProductForwardRenderer.asset` forces intermediate color; `ProductQuestURP.asset` requests depth. Remove a requirement only if the frame graph proves the transfer avoidable. Preserve cockpit shadows, procedural scenery depth, XR submission, eye resolution and 4x MSAA. Changing a setting without eliminating work is not a win. Do not remove camera motion flags: native previous-object matrices require them. Do not globally enable SRP batching: legacy per-draw shader uniforms are incompatible.
3. If the measured remaining cloud budget can close the deficit, implement cloud-only half-resolution rendering in the existing renderer feature. Add per-eye single-sample color/opacity targets and depth-aware reconstruction. Preserve density, lighting, coverage, wind, sampling budgets and interior transition. Prevent duplicate normal transparent rendering. Composite after opaque/procedural scenery and before later transparent effects. Obtain current opaque depth at the correct point; do not assume the current depth-copy schedule suffices. Clip rays against it without unnecessarily shifting their sample lattice. Handle reversed Z, Vulkan orientation, viewport, stereo, foveation, resize and lifetime correctly. Keep full-resolution reference/fallback and edge fallback if necessary. Update shader/authoring generators with the implementation. No temporal accumulation in this iteration.
4. Initially preserve the cloud motion/depth pass (previous captures: 420x440 single-sample versus 1680x1760 4x color). Do not blindly reuse color-pass depth in an earlier XR pass. Separate lighting-free density evaluation only where compiled-code evidence supports benefit. RG16F motion is optional only if measured bandwidth warrants it; check forward flight and near cockpit geometry before accepting loss of Z-motion precision.
5. If still close to target, fix ONE demonstrated complementary bottleneck: CPU submission or attachment traffic, not another broad tree/traffic rewrite. Do not increase foveation, reduce cloud count, globally lower resolution, or reduce MSAA under this plan. Do not repeat the rejected occupancy approach.
6. Validate focused shader/resource contracts and inspected stereo views (below/beside/inside clouds, terrain/cockpit intersections, both eyes), alternating physical-Quest timings and a warmed sustained route. Target approximately 35.8–36 FPS, under 1% missed application deadlines using appropriate frame-level evidence, useful CPU/GPU margin and no progressive thermal deterioration. GPU per-frame p95 near 25 ms is a provisional engineering target, not a guarantee; aggregate p95 is insufficient. Only after independent checks request one final worn head-turn/bank/cloud-entry/strut/instrument check.

Stop after the cloud candidate and one evidence-supported complementary fix if timing or visual gates still fail. Retain independently beneficial optimizations, leave SpaceWarp off by default, and return to frame pacing, startup/navigation reliability, training clarity, audio transitions and demo/recovery quality. Temporal reconstruction or new cloud representations require a separate decision.

## References

- Meta SpaceWarp limitations: https://developers.meta.com/vr/documentation/spatial-sdk/os-app-spacewarp/
- Unity untethered XR optimization: https://docs.unity.com/en-us/engine/6000.0/manual/xr/graphics/untethered-device-optimization
- Meta draw-call profiling: https://developers.meta.com/vr/documentation/unreal/ts-renderdoc-drawcall/
- Meta OpenXR settings: https://developers.meta.com/vr/documentation/unity/unity-openxr-settings-quest/

## Validation limitations retained on main

Selected-volume focused tests previously passed 57/57. Standard visual QA produced 130 captures with inherited machine-gate failures unchanged from reference; inspected pattern and sunward views showed no identified new silhouette/lighting regression. Selected-candidate Meta XR Simulator stereo inspection remains NOT RUN. SpaceWarp is preserved experimental work, not a release/performance acceptance claim. Consolidation does not erase these limitations.
