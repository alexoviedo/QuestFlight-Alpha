# GPU cost reduction while preserving the accepted image

> Execution authority: [the revised unified remaster plan](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md) supersedes any next-step ordering or intermediate headset requests below. The revised sequence is now explicitly approved. This document remains historical evidence.

**Status: first bounded shader candidate implemented; sequencing revised with user approval September 7.** See [implementation and evidence](GPU_COST_REDUCTION_IMPLEMENTATION_2026-09-07.md). Resume the bounded Valmont visual milestone under existing resource ceilings, with performance checks alongside it. Further optimization is not a prerequisite to beginning that local comparison. Full performance acceptance remains required before broad rollout. The investigation sequence below is retained as background, superseded by the [approved visual milestone](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md).

**Headset follow-up:** completed [baseline/candidate/baseline measurements](GPU_WORN_COMPARISON_2026-09-07.md). Candidate delivered interval improves modestly in this sequence, but all runs fail acceptance; differing views and one candidate run prevent a quantified GPU-saving claim. No additional art budget is allocated.

The recommended first implementation is a small shader change that avoids computing colors subsequently overwritten in the canopy and terrain paths, provided compiled Android shaders confirm that this work survives compilation. Clouds are the strongest measured feature-cost lead and the next investigation priority. Neither opportunity has a measured saving yet; the available evidence cannot support a promised percentage or a claim that these changes alone will reach 72 Hz.

## What the evidence establishes

The reference is installed APK `e04a5ef0fa9f62ed54c976d27cc512c312686bb272d93a2fb26df956aefb68c5`, including the accepted traffic recovery and far-canopy UV repair. Keep the additional canopy-selection prototype off throughout optimization comparisons.

| Evidence | Observation | Implication and limitation |
|---|---|---|
| September 7 physical Quest retry, production settings | 17.381 ms mean delivered interval (~57.5 fps), 24.609 ms p95; GPU utilization 93.0%, CPU 32.8%; 1,085 stale frames | Significant rendering pressure; no established spare budget. Direct GPU duration is unavailable. VrApi CPU&GPU 31.230 ms is a combined metric, not GPU milliseconds. |
| Earlier same-APK cloud isolation | Clouds off: App mean 16.405 → 12.309 ms; CPU&GPU 37.889 → 33.369 ms; delivered interval 19.832 → 19.265 ms | Cloud volumes warrant investigation. These differences are not direct GPU savings, and disabling clouds is not an acceptable shipping change. |
| Earlier traffic isolation | Traffic off: delivered interval 19.832 → 16.703 ms; later conservative traffic rejection measurably reduced Editor CPU preparation | Traffic has already received a substantial CPU optimization. Do not count it again as a prospective GPU saving. |
| Same older APK with temporary medium foveation | Delivered interval 18.108 → 14.624 ms; both runs failed acceptance | Useful evidence of sensitivity to rendering workload, but peripheral resolution changes violate a strict image-preservation requirement. Excluded from this plan. |
| Inspected current physical screenshot | Both eyes are tilted upward: sky and cockpit dominate, with a narrow terrain/horizon region | Existing physical runs cannot rank terrain, canopy and buildings for representative forward/downward flight views. |

Sources: [current build and corrected physical results](CANOPY_UV_REPAIR_2026-09-07.md), [cloud/traffic isolation](PERFORMANCE_RECOVERY_2026-09-06.md), [foveation diagnostic](CANOPY_BENCHMARK_PROGRESS_2026-09-06.md), [inspected physical image](../../T2-QuestFlight-current-evidence/canopy_uv_repair_20260907/quest_retry/adb_screenshot_final.png). These are diagnostic runs, not controlled worn A/B acceptance. The corrected log shows no thermal warning; that does not establish sustained thermal acceptance.

## Ordered work after approval

### 1. Establish a representative cost ledger and check compiled work

Start offline: inspect compiled Android/Vulkan variants for the source findings below and identify the actual shaders/materials in a captured frame. Record texture instructions, branches and available compiler statistics. Source expressions alone cannot establish executed GPU work: the compiler may already remove or move them. If it does, close that opportunity without an unnecessary rewrite.

Then obtain physical measurements covering airport approach with canopy/traffic, reservoir with water, terrain-dominated cruise, and cloud-heavy flight. Use the same authored route, repeatable aircraft state, tracked view, resolution, 4x MSAA, refresh rate and production settings. Log view poses, clocks, thermal state, APK identity and exact measurement windows. Never position the OpenXR Main Camera manually. Collect captures separately from unrecorded timing windows.

First verify whether available device instrumentation can provide reliable GPU duration and pass costs. If it cannot, label that limitation and use repeated matched feature-isolation runs to rank whole-frame effects; do not rename combined timings as GPU time. Alternate baseline/candidate order to expose warmup and drift. Existing cloud/vegetation/building/water isolation is diagnostic only; building isolation retains streaming CPU work, and texture-tier isolation does not establish aerial-array cost.

The ledger must include instanced traffic and vegetation as well as ordinary renderers. Existing QA draw estimates omit instanced traffic and are not hardware draw counts. Output: ranked measured costs, uncertainty and a decision on which source candidate to implement first. Most preparation is offline; group the necessary worn checks into one prepared session.

### 2. Remove overwritten shader work without changing the resulting image

| Source finding verified in this review | Proposed bounded change | Preservation requirement |
|---|---|---|
| [Semantic Surface](../QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1/Materials/ProductionSemanticSurface.shader), `frag`: samples orthophoto into `topColor` before the building/canopy branch; canopy later overwrites `topColor` unconditionally | Evaluate orthophoto color only for the building path. Consider material specialization only if the compiled shared branch still incurs cost. | Preserve canopy colors, normals, AO, lighting, alpha coverage and the repaired far-card UVs; retain diagnostic behavior. |
| Same shader: computes foliage-detail texture modulation before the foliage-card branch replaces `crownColor` | Evaluate that detail path only for non-card canopy geometry | Keep detail on volumetric canopy; preserve atlas-based leaf/card shading exactly. Preserve derivatives when reorganizing texture sampling. |
| Same shader, `SampleOrthophoto`: samples the near image before returning recovery color outside the near blend region | Skip the unused near sample outside its contribution region | Keep the full overlap blend and explicit texture gradients. |
| [Terrain Pyramid](../QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader), `frag`: computes near/mid/far `macro`, then replaces it unconditionally when `_ClipmapEnabled` is active | Make clipmap and fallback color evaluation mutually exclusive | Preserve fallback behavior, page-edge blending, gradients, clean plates and canopy integration. Do not remove the global clipmap fallback needed for incomplete coverage. |
| Same terrain shader: calculates water ripple trigonometry/normal before the water reflection branch | Move water-only work into the existing branch if compilation has not already done so | Preserve the existing water mask threshold, glints, reflection and shoreline behavior. |

These are source-proven unused-result opportunities in specific paths, **not proven device inefficiencies**. Implement independently, compare compiled output and retain only changes with repeatable benefit and unchanged imagery. No new texture assets or geometry should be necessary. Do not multiply nominal sample counts by screen size to invent savings.

### 3. Reduce cloud integration work that contributes nothing

The [cloud shader](../QuestFlight/Assets/Production/Environment/CloudsV1/BoulderCloudShell.shader) has a maximum of 48 march steps, each sampling two atlas slices: up to 96 density texture fetches per surviving fragment. It already rejects the inappropriate box surface, exits at low transmittance, and uses precomputed light transmittance. Its small mesh/draw count therefore does not establish low rendering cost.

Investigate in this order:

1. Reuse the already computed local ray origin/direction inside the march instead of repeating world-to-local transformation for every sample, if the compiler has not already simplified it. Verify floating-point differences against image captures.
2. Skip density fetches where the existing vertical shape is exactly zero. Preserve the original sample positions, step length, termination rules and lighting. Measure whether the branch saves work in real views; the zero-height regions may be too small to matter.
3. Only if cloud cost remains dominant, investigate conservative empty-space information derived from the existing atlas. It must account for bilinear filtering, interpolation between slices and both density archetypes. Skip only samples proven unable to contribute. Preserve the original march lattice: moving the ray start or changing step spacing can change the cloud image. Include the extra lookup/memory cost in the comparison.

The third item is an explicitly gated experiment, not an established optimization. Do not reduce march steps, density, cloud population, rendering resolution or fly-through behavior. Do not assume `Cull Off` means both faces execute full integration: the shader already rejects the unwanted surface.

### 4. Address remaining measured costs, then resume the art upgrade

Reprofile after each retained change. Pursue cockpit transparency, vegetation overdraw or building shading only if representative device evidence ranks them highly. Their presence alone does not prove they are the bottleneck. Any visibility rejection must be conservative across both eyes and preserve moving traffic, head turns and page/LOD transitions.

Handle the existing 79/82 draw estimates against 77 and 39-material warning against 38 as separate open QA issues. Aircraft mesh consolidation must respect animated controls, propeller pivots and visibility groups; fewer submissions are not automatically less GPU work, and larger batches can weaken culling. Do not raise limits or combine that work into the first shader experiment.

## Acceptance and rollback

- Freeze feature populations, source imagery, textures, LOD distances, alpha cutoff, MSAA, resolution and foveation. No feature removal, lower-resolution rendering, increased alpha rejection, shorter visibility distances or renderer migration belongs to this fidelity-preserving scope.
- For every candidate, compare the same settled stereo poses, standard Visual QA and inspected Meta XR Simulator stereo captures. Include canopy silhouettes/atlas frames, near/far transitions, terrain page boundaries, water glints, cloud exterior/interior views and cockpit readability. Compare image differences and investigate every unexplained change; a changed-pixel count alone is not quality acceptance.
- Inspect temporal headset behavior: traffic continuity, peripheral detail, cloud motion/parallax and head turns. Retain the far-canopy render regression. Do not accept a GPU improvement caused by missing scenery, invalid tracking or a less demanding view.
- Validate the Android ARM64/Vulkan/OpenXR build and all scenery payloads. Keep native JSBSim, serialized hierarchy and camera ownership intact. Each candidate is independently reversible; reject visual regressions or savings indistinguishable from run variance.
- Final acceptance retains the existing three distinct unrecorded physical runs on one immutable APK, each 90 seconds warmup plus 60 seconds measurement: CPU&GPU mean ≤12.8 ms, p95 ≤13.889 ms, ≤5% samples over 13.889 ms, ≤2 stale frames, zero tears, and no thermal warning or crash. Report direct GPU timings separately if available. Existing GC and other runtime failures remain open until independently resolved.
- Record worn visual acceptance separately and perform the representative longer flight/thermal soak before release. Editor and simulator timings cannot establish Quest performance.

**Approval scope recommended:** measurement/compiled-shader verification, then the individually validated unused-work changes in step 2 and the first two cloud investigations in step 3. Reassess measured headroom before authorizing a larger cloud empty-space implementation or resuming additional visual density. No quantified saving is promised before those results exist.
