# Canopy distance LOD — September 10, 2026

The user reports a huge improvement from the map-wide canopy on Quest. Its 520–640 m transition makes that improvement arrive too late. This candidate extends useful range while retaining the accepted branch artwork and footprint data. **Implementation is complete; Quest performance and final appearance acceptance are not.**

## Implemented candidate

| Control | Behavior |
| --- | --- |
| Near composition | Same six cards at the same heights, scale and UVs through 520 m; three lower cards fade out over 520–720 m |
| Middle composition | Three original upper cards; the third fades out over 1,000–1,250 m |
| Far composition | Two original upper cards, including the oblique card visible from above; no enlarged leaf texture |
| Outer transition | Full distance coverage through 1,600 m, fading to original trees over 1,600–1,800 m, subject to the allocation cap |
| Geometry cap | 393,216 grove triangles total, unchanged from the short-range renderer's maximum |
| Submission | Up to three procedural draws, one per submesh tier; 12/6/4 triangles per selected cell |
| Allocation | Up to one quarter reserved for far coverage when needed; unused reservation returns to nearer tiers; oversubscribed tier edges fade over 48 m |
| Residency | At most 49 hash-verified pages; near pages load first, with bounded surrounding residency for head turns |
| Visibility | Both-eye frustum union, angular guard, block rejection and cell rejection at view boundaries; selection/draw from the authored camera's pre-cull callback |
| Buffers | Up to 98,304 × 48-byte GPU records (4,718,592 bytes); 1,764² R8 ownership map (3,111,696 bytes) |
| Original trees | Trunks retained; original crowns remain wherever replacement cells are missing, not selected or fading |

The maximum cell count follows the cheapest tier; it cannot be filled entirely with near-tier geometry. The same shared mesh now has three index subsets. Redundant cards fade before switching subsets; their positions, texture coordinates and leaf scale do not change. The near shading/artwork is preserved. No raw imagery or new artwork was added; existing source/license records continue to apply.

When a tier hits its quota, original-tree fallback can occur before the nominal distance transition. The requested range is therefore not a guarantee that every grove receives every tier at 1.8 km. This needs inspection during actual flight, especially in dense western views.

Selection caches decoded cell attributes and rotations. Linear 16 m distance bins replace the full candidate sort. Blocks wholly inside either eye's guarded frustum skip redundant individual plane tests. Block bounds were corrected to enclose the full conservative cell bounds. The original asynchronous snapshot/residency fix remains, and streaming continues while diagnostic flight is paused.

The QA camera now copies the authored tracked camera's layer mask. This is an Editor evidence correction, not a change to the OpenXR camera. The existing pale cockpit obstruction remains in Editor renders and is not claimed fixed.

## Findings about work outside the pilot's view

- `AirportCloseTreeLodRenderer.DrawTier` already compacts actual mesh bounds against both eye frusta before submitting tree batches. Its retained selection is deliberately stable across head movement.
- `KbduTrafficV2Runtime` already rejects invisible corridor bounds using both eye frusta and rejects out-of-range vehicle positions before costly terrain/road sampling. Preserve schedules and residency to avoid the earlier traffic regressions.
- `AircraftCenteredBuildingClipmapController` uses authored MeshRenderer page slots and keeps a connected base window plus a forward extension. Its residency count does not demonstrate that all pages are rasterized. Partially visible combined pages may still submit hidden geometry; its actual cost has not been measured here.
- The cloud shader uses `ZTest LEqual` and rejects the unwanted shell surface before its ray march. Its combined bounds and transparent overdraw remain possible profiling targets. No measured saving is attributed to them.
- The new grove already rejected offscreen cells. Its short hard range was the direct cause of the reported late appearance. There is no evidence supporting a blanket claim that deleting offscreen scenery will fund the extension.

## Verification

Artifact root: `../T2-QuestFlight-product-artifacts/map_branch_lod_20260910/`.

- All 1,296 page hashes/records and corruption rejection pass; authored renderer, camera binding, disabled obsolete overlays and valid observation positions pass.
- Focused checks cover extended coverage, exact submesh index counts, a crowded allocation within the original triangle cap, reserved far coverage, zero grove selection when looking away from terrain, page seams/edges and ownership reset.
- The actual asynchronous UnityWebRequest iterator passes interrupted window changes with 49 resident pages at both remote positions and no failures: `streaming_final.log`, 671 Editor updates.
- `final_review/` contains 36 paired-eye Editor views at nine authored pilot stations. Counts span 19,853–63,108 cells and 138,082–393,216 grove triangles. Ten views are allocation-limited. Farthest selected centers span 1,615.9–1,800 m; this is not a full-opacity visibility guarantee.
- Final Editor selection time spans 4.83–14.52 ms, including buffer uploads. The initial full-sort version took 8.65–35.77 ms in the earlier paired views. This is development-machine evidence, not a Quest CPU/GPU result.
- Inspected normal and diagnostic pilot images. A far-tier-only highlight proves actual foliage pixels beyond 1.25 km: 196 left-eye / 203 right-eye magenta pixels in station 0's side view; the normal images contain zero highlight pixels. These are offset Editor stereo captures, not tracked simulator or headset acceptance.
- Standard QA completed 130/130 images without capture errors. Hierarchy, environment and single-XR checks pass. **26 views fail unchanged cost estimates**, versus five in the short-range reference. Maximum estimates: 1,026,876 triangles and 86 draws. These estimates include conservative costs elsewhere in the scene, but the increase must not be dismissed as preexisting.
- The fresh simulator audit reports `BLOCKED_MISSING_RECORDED_REPLAYS`; it produced no tracked session images. Android configuration and active OpenXR runtime were preserved. No simulator pass is claimed.

`candidate_summary.json` retains the view statistics, exact failing views and pixel counts. Normal pilot images remain limited by the existing Editor cockpit obstruction. They do not establish final distant density, head-turn behavior or frame/thermal acceptance.

## Build, device and next gate

The retained local short-range reference is `canopy_640m_reference.apk`, SHA-256 `832285c9880bd666aa161425acc70bc110e2e5b3e3220184072c83d151bc4378`. It differs from the last verified installed `a12e1760…` package only in the corrected authored observation positions; the earlier runtime/artwork/source invariance record is retained. The user's observed package is not assigned a new hash without device verification.

The longer-range Android candidate built successfully at 18:13:49 UTC. SHA-256: `cc877d4e7685b93d659c9c2d0503d67c1dfafd60b87c19c0b5350a2b04a5bd0c`; size 1,995,889,240 bytes. All 1,912 packaged scenery files verified. The final renderer, shader, authoring, mesh, telemetry, environment prefab and scene hashes match the captured build inputs (`android_build/final_source_check.json`). Build success does not override the unpassed visual/performance gates.

USB recovered and both exact packages were tested while worn. The user explicitly preferred the longer-range candidate: “The last one looked so much better.” This establishes the visual target, not a defect-free or performance acceptance claim. The short-range reference was restored afterward and its on-device SHA-256 verified as `a12e1760ec5fdc1b03620d5312bf903a828f994a79e570147f35b2e2a1396d16`. The longer-range source and APK remain preserved.

## Completed worn comparison and performance recovery

Evidence: `map_branch_lod_20260910/quest_profile/reference/` and `candidate/`. Each run used 90 seconds warmup and 60 seconds measurement, 60 app-PID VrApi samples, normal production rendering and the demo route, without screen recording or foveation overrides.

| Metric | Short range | Longer range |
| --- | ---: | ---: |
| Delivered mean frame ms | 18.31 | 23.29 |
| Delivered p95 frame ms | 25.99 | 37.42 |
| VrApi mean FPS | 54.55 | 42.80 |
| GPU utilization mean | 91.93% | 89.12% |
| Stale frames | 1,273 | 2,020 |
| Maximum reported temperature | 44 C | 47 C |

Both fail the existing performance gates. No crash, ANR, OOM, tears or thermal warning was recorded. Head motion differed and the candidate ran second and warmer; these are not identical-pose causal measurements. Direct GPU duration was unavailable; VrApi CPU&GPU must not be relabeled GPU time. The automatic candidate baseline comparison incorrectly selected a summary JSON as raw baseline evidence and reports zero values; ignore that comparison. The table uses each run's own valid summary metrics.

Ranked next experiments, preserving the accepted range and branch appearance:

1. **Skip Low; screen High and Medium together.** Android enables `MetaXRFoveationFeature`, OpenXR uses Legacy (0), and the project uses the Built-in pipeline. Application code never requests a level; installed Meta SDK 205 initializes it to zero. Use the existing host diagnostic override on the retained longer-range APK for Off/High/Medium/Off screening, with verified runtime state, comparable tracked pilot views, unchanged scene/settings and clocks/temperature recorded. High tests the stronger setting's benefit quickly; Medium is the initial product candidate. Do not spend a build or worn approval cycle on each level. Low is a fallback only if Medium harms useful peripheral detail. Stop tuning levels if High produces little improvement beyond measurement variation; it cannot solve CPU/geometry costs. Keep dynamic adjustment off while isolating levels. Consider a dynamic cap only after static settings demonstrate a benefit and the maximum level is visually acceptable. The final Off repeat detects drift; repeat only ambiguous comparisons. Existing controls avoid building a new benchmark framework.

   The [earlier medium test](CANOPY_BENCHMARK_PROGRESS_2026-09-06.md) measured delivered cadence 18.108 to 14.624 ms, a roughly 19% reduction on an older APK. Both failed, views were sky-heavy and differed, and the headset was unworn. This is evidence to prioritize Medium, not a predicted saving for the current foliage scene. Current 23.29 ms mean cadence must reach 13.89 ms for 72 Hz, roughly a 40% cadence reduction plus slow-frame recovery; this is not a measured GPU-cost reduction requirement.

   **Evaluate subsampled layout with the selected FFR level.** `MetaXRSubsampledLayout Android` is currently disabled. Meta recommends this Vulkan companion feature to reduce memory bandwidth and peripheral artifacts. Verify the installed SDK and swapchain support, then compare a build changing this feature alone against the same fixed level. Do not attribute the combined benefit to foveation alone. [Meta implementation guidance](https://developers.meta.com/horizon/documentation/unity/unity-fixed-foveated-rendering/), [FFR tradeoffs](https://developers.meta.com/horizon/essentials/fixed-foveated-rendering/). No renderer migration is required to investigate this supported path.
2. **Separate canopy CPU and upload costs.** Instrument candidate gathering, allocation, GPU record filling, ComputeBuffer.SetData and ownership texture upload independently, including update frequency and bytes. Current selection timing combines all of them. The same buffer and 3.11 MB ownership texture are rewritten each selection; in-flight synchronization is a hypothesis, not an established bottleneck. Apply buffer reuse, fewer uploads or update scheduling only when measurements support them and head-turn coverage remains intact.
3. **Cloud sampling LOD is the first substantial shader experiment if current isolation confirms its cost.** The [older isolation](PERFORMANCE_RECOVERY_2026-09-06.md) changed VrApi App mean 16.405 to 12.309 ms with cloud volumes disabled, but delivered cadence only changed 19.832 to 19.265 ms. This does not establish 4 ms of recoverable GPU time. Refresh the existing cloud-off diagnostic on the current APK in a cloud-visible ordinary pilot view, using the selected FFR level so that its residual cost is relevant. Captures belong outside timing windows. Disabling clouds is diagnostic only; little remaining cloud cost means move directly to the measured canopy bottleneck.

   Source/history audit: V4 uses eight cloud proxy boxes in one mesh, 64 vertices/96 triangles, one renderer/material/draw. Unity performs renderer-bound culling and GPU clipping; the shader depth-tests, rejects the unwanted entry/exit surface and exits at low transmittance. It has a maximum of 48 march samples, with a 60 m minimum step, but **no camera-distance or projected-size sampling LOD**. The existing coordinate-arithmetic optimization is already implemented. Splitting the one mesh into eight renderers mostly avoids a few offscreen proxy vertices; fully offscreen boxes already generate no cloud fragments. No evidence supports expecting major savings from this split. The late opacity multiplication also leaves fully hidden exterior clouds doing integration before rejection; early rejection at exactly zero exterior contribution is a narrow opportunity during the interior transition, not an explanation of ordinary exterior flight cost.

   Prototype 48/24/12 maximum sample tiers, retaining the existing near/inside behavior and using projected size plus distance to the cloud surface to select cheaper distant sampling. These are deliberately strong experimental settings, not accepted quality values or a prediction of proportional frame-time savings. Keep world-space density, cloud placement, silhouette scale, lighting and optical-depth compensation for actual sample spacing. Make selection consistent between eyes and transitions stable; inspect motion, sunlit edges, undersides and fly-through before keeping a tier. Do not prematurely stop integration halfway through the cloud to meet a sample cap. No new cloud renderer or buffer/compositing architecture is necessary for this first experiment.

   [V4 acceptance/history](evidence/VOLUMETRIC_CLOUD_FIELD_V4_2026-08-14.md) explicitly records why camera-facing cloud cards were rejected and why the volumes were accepted. A return to those cards is not an untried shortcut. Savings from cloud sampling and foveation overlap; measure the retained combination rather than adding isolated deltas.

   Existing trees, groves and traffic already reject both-eye offscreen bounds. Measure foliage overdraw and partially visible building pages before rewriting culling or subdividing buildings. Broad aerial views expose large amounts of scenery, limiting occlusion opportunities compared with enclosed games. Loaded/simulated objects are not necessarily submitted/rasterized work. Do not cull using aircraft-forward alone or fixed cockpit masks: the pilot can turn and lean, and each eye sees a different window edge.
4. **Validate the winning combination.** Preserve the 1.6–1.8 km nominal transition, leaf scale and accepted composition. Inspect stereo pilot imagery, run standard QA and Android build checks, then measure the physical candidate. No additional worn test today. Resting-headset experiments may diagnose costs but cannot establish final frame, thermal or peripheral-clarity acceptance. Further density or distance reductions require evidence that less visible work cannot fund the target.

Flight-simulator priorities: preserve instrument readability during eye-only glances, runway and traffic recognition, horizon stability, stereo consistency and the accepted grove range. Fixed foveation follows the center of the rendered view, not eye gaze; a seated pilot does not always look through that center. High is eligible only if its additional saving is material and those uses remain clear. Protect useful distant silhouettes; if LOD work is subsequently justified, simplify unresolved internal detail according to projected size instead of shortening visible range. Screen-space LOD is a conditional implementation direction, not a claim that arbitrary density reductions preserve the accepted image.

### Different scenery and cockpit foveation

High scenery / Medium cockpit is technically plausible with separately controlled rendering targets/passes and correct composition, but **not a per-object switch in this project's Built-in + Legacy FFR path**. Setting the global level between two Unity cameras does not establish two independently foveated regions in their shared eye images. [Meta describes Legacy versus per-pass SRP control](https://developers.meta.com/horizon/documentation/unity/unity-openxr-settings/); per-pass enable/disable is not proof that arbitrary Medium/High strengths are available independently on our current backend. A separate rendering path needs support verification and includes additional target, bandwidth, composition and stereo/depth work. No pipeline migration belongs in the immediate recovery experiment.

A smaller alternative is High scene FFR with selected readable instrument faces/text presented through unfoveated compositor layers. This protects suitable panel content, not automatically the entire three-dimensional cockpit. Existing moving needles, bezels, glass, hands/yoke occlusion and head-translation parallax still need correct presentation; a flat overlay can introduce depth errors. [Meta's compositor-layer guide](https://developers.meta.com/horizon/documentation/unity/unity-ovroverlay/) documents ordering, shapes and bandwidth tradeoffs; [FFR guidance](https://developers.meta.com/horizon/essentials/fixed-foveated-rendering/) notes that compositor layers are unaffected. Investigate only if High beats Medium materially and cockpit readability is the remaining obstacle. Sharp gauges do not repair degraded peripheral runway or traffic recognition.

No runtime optimization or device setting was changed by this critique. The measured baseline already misses 72 Hz, so merely recovering the candidate's added cost would not complete performance acceptance. The revised plan prioritizes one bounded hardware screening pass, then the largest supported improvement; it does not authorize weeks of speculative profiling or a new renderer.
