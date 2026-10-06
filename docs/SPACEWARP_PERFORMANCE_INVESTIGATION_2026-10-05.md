# SpaceWarp performance and image-quality investigation â€” October 5, 2026

> October 6 consolidation: further optimization is deferred. SpaceWarp remains OFF by default and experimental. See [the preserved future plan](SPACEWARP_DEFERRED_OPTIMIZATION_PLAN_2026-10-06.md) and [consolidation record](MAIN_CONSOLIDATION_2026-10-06.md) for current results; the chronological entries below are historical.



Status: implementation approved. Batch reuse, parked-pose/camera caching, traffic plane-test optimization and corrected measurement tooling are implemented. Sixty focused tests and the final Android diagnostic build pass. The bounded moving-flight profiling cycle is complete. CPU work is reduced, but no end-to-end FPS gain is established. SpaceWarp remains unaccepted; GPU optimization and head-turn artifact diagnosis are still outstanding.


## Correction to the first proposal



The first proposal was not a complete, proven cure for SpaceWarp. It was an investigation plan mixed with conditional optimizations. It under-explained existing failure captures, overstated the likely value of cloud-sector culling, and proposed a four-minute automatic test before establishing that its automation existed. It also left possible wearer-dependent diagnosis too late. These are planning defects, not additional confirmed rendering bugs.



The revised order is: reuse evidence now; establish whether any missing baseline capture needs a wearer **before implementing fixes**; profile independently where possible; fix measured causes; validate the result. The user does not need to remain available while builds or analysis run. No additional worn timing comparison is needed merely to reconfirm that this APK misses its deadlines.



Current device check: Quest connected, asleep, product process not running. No new worn measurement was taken. Existing release code compiles out attachment readback; its capture pass is development-only. A request file cannot enable that code in the release APK. A matching diagnostic build is needed for new attachment captures; installing an older diagnostic APK would not prove the current candidate correct. Do not hold the user waiting for such a build.



Independent checks completed during this revision:



- Recomputed the OFF/ON results from the original log/events; they exactly match the retained report.

- Recomputed all six takeoff captures in both eyes. Largest sampled strut-interior motion discrepancy relative to the cockpit is **0.2804 mm**; history generation remains 7 and reset-motion remains 0. Maximum sampled edge discontinuities range **32.44â€“81.48 color pixels** across those twelve eye/frame samples. Readback affected their cadence, so those are not normal-frame performance numbers.

- Matched **14** relevant source/configuration files against the current APK's retained source manifest, including both grove shaders and the common motion include.

- Confirmed the authored cloud field is **one renderer, one draw, 96 triangles**, not many independently culled cloud sectors. Its expensive work, if the profile confirms it, is predominantly the density sampling and overlapping pixels, not submitting thousands of cloud triangles.



Reproduction outputs are retained externally in `spacewarp_plan_review_20261005/review_checks.json`, `comparison_revalidated.json` and `takeoff_revalidated.json`. Original evidence was preserved.



## Recommendation



Treat the current SpaceWarp experience as a failed visual candidate. The user reports more stutter, aliasing and distortion with it enabled. Reduced stale-frame counters do not override that feedback. Use OFF as the quality reference; this is a recommendation, not a change to the user's saved preference.



There are two problems to separate: insufficient sustained application cadence, which the existing hardware data proves, and reconstruction artifacts, whose likely mechanisms are present in the rendering code but whose individual contributions remain unmeasured. Improving frame rate is necessary; it does not guarantee that SpaceWarp will become visually acceptable.



The best next step is one bounded profiling effort, followed by the highest-value measured fixes. Favor changes that also improve ordinary rendering. Do not restart the canopy project, increase foveation, reduce visible scenery, or keep adjusting motion-buffer heuristics without evidence. If the remaining artifacts persist with valid motion/depth and stable timing, retain SpaceWarp OFF as the release default rather than spending indefinitely on it.



## Verified baseline and evidence



- Workspace: `C:\Users\ovied\Dev\T2\T2-QuestFlight`.

- Branch: `feature/application-spacewarp-complete`; HEAD `fdcc160c23360676ca9d87aa3626009785f8e28f`. Extensive pre-existing working changes are part of the tested product; HEAD alone does not identify it.

- Unity: `6000.3.8f1`; standalone Quest 3, Vulkan/OpenXR, native JSBSim flight authority.

- Tested APK: external `spacewarp_altitude_gate_20261004/quest_candidate/QuestFlight-altitude-gated-SpaceWarp.apk`.

- SHA-256: `575ea4afe1f5c82edccfc797d85cf330aba9c47242c3322d34dbed39e16f0f0b`.

- External artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts`.

- Evidence: `spacewarp_worn_comparison_20261005/worn_logcat.txt`, `events.jsonl`, thermal/battery snapshots, `comparison_verified.json`, `analyze_comparison.py`, `RESULTS.md`.

- Fourteen relevant current implementation/configuration files were hash-checked against the retained APK source manifest, including the SpaceWarp controller, render feature, render submission, common motion include, cloud shader/controller, trees, grove shaders/renderer, traffic, renderer/URP assets and embedded XR depth/motion pass. They match. This is not a hash verification of every build input.



Two roughly eight-minute worn demo runs were recorded, OFF then ON. Analysis uses flight seconds 90â€“450, 360 one-second VrApi aggregates per run, restricted to each application's PID. Compositor type independently confirms OFF versus application SpaceWarp. Both runs report `Fov=2`. The altitude gate activated at 5,500.2 ft MSL; it did not oscillate during the analyzed window. The end-of-run pause is excluded.



| Metric | SpaceWarp OFF | SpaceWarp ON |

|---|---:|---:|

| Target application rate / display rate | 72 / 72 Hz | 36 / 72 Hz |

| Mean application FPS | 39.60 | 32.51 |

| Mean application GPU time | 20.47 ms | 24.66 ms |

| p95 of **one-second GPU aggregates** | 24.90 ms | 30.99 ms |

| GPU aggregates above the mode's application interval | 355/360 above 13.89 ms | 80/360 above 27.78 ms |

| Mean reported stale count per sample | 36.59 | 9.66 |

| Mean render-thread-start to GPU-completion wall time | 47.18 ms | 55.30 ms |



The ON run exceeds its GPU interval in 22.2% of sampled seconds and misses 36 FPS overall. Its mean GPU time leaves only 3.12 ms against the theoretical interval; its sampled p95 exceeds that interval by 3.21 ms. There is no demonstrated dependable budget for additional visual features. OFF also falls far short of native 72 FPS.



The mean GPU difference is +4.19 ms with ON, but it is not an isolated measurement of SpaceWarp overhead: the second run was warmer, clocks varied, and gaze/aircraft positions were not continuously recorded. Battery-temperature ranges were 36â€“47 C and 47â€“50 C; sampled Android thermal status and VrApi PLS remained zero. No reported throttling is not a sustained thermal PASS. No exact per-frame p95 or missed-display-frame percentage can be derived from these aggregate samples.



`App` is application GPU time. `GD` is boundary-rendering GPU time. `CPU&GPU` includes render-thread-start through GPU completion, so it must not be called CPU execution time or added to `App`. The wall-time gap warrants CPU/render submission and scheduling investigation, but does not establish a particular CPU hotspot. [Meta metric definitions](https://developers.meta.com/vr/documentation/unreal/ts-logcat-stats/).



## Findings and likely bottlenecks



### 1. Missed application deadlines â€” confirmed



The measured workload frequently cannot supply SpaceWarp's required 36 real frames per second. Frame synthesis is not a replacement for meeting that cadence. This explains at least part of the stutter; it is insufficient to attribute every visible distortion. [Meta missed-frame guidance](https://developers.meta.com/vr/documentation/unity/os-missed-frames/).



Current release capture/readback is not automatically running: `ProductSpaceWarpRenderFeature.AddRenderPasses` only enqueues its diagnostic capture pass for debug builds. There is no evidence that the previous profiler-enabled-build regression has returned. No repeated altitude gating or app error was found in the analyzed interval.



### 2. Extra motion/depth work â€” confirmed code path; exact cost unknown



`Assets/Scripts/Rendering/ProductSpaceWarpRenderFeature.cs:41` uses provider-owned XR motion/depth attachments and submits procedural scenery plus a transparent renderer list. `ProductRenderSubmission` runs both color and motion submissions when SpaceWarp is active. The embedded `Packages/com.unity.render-pipelines.universal/Runtime/Passes/XRDepthMotionPass.cs:69` also renders opaque motion/depth with renderer-list instancing and dynamic batching disabled.



The disabled renderer-list instancing was introduced for legacy previous-matrix correctness. It is not permission to turn it on globally. Procedural trees and traffic already use instanced submissions through a separate path. The motion pass is legitimately needed; simply dropping it or emitting zero motion would be a visual regression.



### 3. Clouds â€” GPU sampling candidate; sector-culling proposal demoted



`Assets/Production/Environment/CloudsV1/BoulderCloudShell.shader` performs density ray marching for color and again for `XRMotionVectors`. Existing sampling LOD already reduces 48 steps to 24/12. The compiler may remove unused lighting calculations from the motion variant; source duplication is not proof that every color calculation executes twice.



`Assets/Scripts/Environment/Clouds/BoulderCloudField.cs:174` assigns a 48,000 Ã— 20,000 Ã— 48,000 m box in the wind-update path. However, `Assets/Production/Environment/ProductionEnvironmentRoot.prefab:6260` contains only one cloud renderer and records 96 triangles/one draw. `BoulderCloudField.TryValidate` explicitly requires one batch. Tightening that whole-field bound would not independently reject its eight formations while any remain visible. Splitting it would add draws and alter the authoring contract. Remove cloud-sector culling from the first fixes unless a real capture establishes worthwhile savings. GPU clipping already rejects offscreen primitives.



The cloud color is blended at low opacity, whereas motion coverage clips below `_CloudMotionOpacity` (default 0.5) and uses a representative density-weighted depth. Some cloud-edge color consequently has background motion/depth. This is a concrete approximation to inspect, not proof that changing the threshold will fix the reported distortion.



### 4. Tree/traffic packing and grove uploads â€” concrete repeated work; significance unmeasured



- `AirportCloseTreeLodRenderer.cs:698` already caches preparation within the frame. Nevertheless, `DrawTier` at line 1502 still tests/copies instances and `SubmitTreeBatch` at line 1580 packs properties for each pass. Do not misdiagnose this as all tree selection running twice.

- `KbduTrafficV2Runtime.cs:1173` copies instance/tint arrays and repacks previous matrices in `SubmitChunks`, again for both passes. Visibility preparation is already shared.

- `MapBranchGroveRenderer.cs:387` uploads the complete 1,764 Ã— 1,764 R8 ownership texture when ownership changes: 3,111,696 bytes, about 2.97 MiB, even when the change is localized. Selection, buffer uploads and ownership upload counters already exist. Correlate these with spikes during head turns and page crossings before redesigning anything.

- `ProductDeformedMeshHistory` writes previous vertices to UV4 both during upload and completion. `ProductRigidRendererHistory` writes property blocks every frame. These are secondary candidates; small cockpit meshes may make their absolute cost negligible.



### 5. Render-target traffic â€” promising low-scope configuration candidate



`Assets/Production/Rendering/ProductForwardRenderer.asset:71` sets intermediate color mode to **Always**. The active custom render feature therefore requests an intermediate color target in the embedded URP RenderGraph implementation. `ProductQuestURP.asset:22` also requests a depth texture. This may impose avoidable copies/resolves and memory traffic on a tile-based GPU. Measure the actual frame graph before changing it: switching to Auto alone might not remove the intermediate if another dependency still requires it.



No first-party shader depth-texture consumer was found by the targeted shader search, but provider depth submission and render-feature requirements still need explicit tracing. The ordinary camera depth texture and the required XR motion/depth attachment are not interchangeable.



The project already uses Vulkan/OpenXR, forward rendering, RenderGraph, 4Ã— MSAA, no HDR and no camera post-processing. Unity recommends avoiding unnecessary texture copies on untethered XR. These settings are an audit target, not a reason to remove required depth or lower anti-aliasing. [Unity untethered XR guidance](https://docs.unity3d.com/6000.0/Documentation/Manual/xr-untethered-device-optimization.html).



### 6. Reconstruction limits and coverage â€” likely artifact mechanism; exact current defect not isolated



An earlier captured Quest frame has a 420 Ã— 440 motion/depth buffer per eye against 1,680 Ã— 1,760 color. That is four color pixels per motion pixel in each direction. This capture predates the current APK and establishes a known pipeline characteristic, not a current failing-frame capture.



Thin struts, foliage cutouts, distant building edges and cloud/terrain overlap are challenging at that resolution. The grove shaders use alpha coverage and derivative-dependent filtering; these need comparison between the color and lower-resolution motion passes. A color pixel can contain multiple visible surfaces but the submitted motion/depth sample cannot describe all of them. Meta documents thin-edge and transparency artifacts. [SpaceWarp technical explanation](https://developers.meta.com/vr/documentation/spatial-sdk/os-app-spacewarp/), [Meta artifact examples](https://developers.meta.com/vr/documentation/unreal/unreal-sample-app-spacewarp/).



A source-color screenshot does not show the compositor's generated frame. Existing captures cannot prove that the current headset view is distortion-free. We must distinguish bad motion data, mismatched coverage, and unavoidable disocclusion artifacts from ordinary material aliasing. Previously corrected tracking-space motion and the accepted ground/altitude gate must remain intact. The rejected strut coverage-expansion experiment should not be repeated without new evidence.



The six actual failing takeoff captures are stronger evidence than the first proposal conveyed: source-color struts were straight, their sampled interior motion agreed with the cockpit to within 0.281 mm, and edge neighborhoods contained large foreground/background motion jumps. This argues against another blanket aircraft-matrix fix for that historical event. It supports a reconstruction/edge-coverage mechanism without proving the cause of every new airborne artifact. The later altitude gate avoids that takeoff condition; it does not repair reconstruction over mountains or foliage.



For foliage specifically, `MapBranchGrove.shader:163` evaluates derivative-dependent alpha coverage inside the lower-resolution motion pass; `MapBranchGroveCluster.shader:266` obtains motion coverage by running the color fragment logic. Both inherit `AlphaToMask On`. Reusing the same function at a different resolution does not guarantee matching texture mip selection or sample coverage. Current attachment MSAA/sample count must be recorded before blaming alpha-to-coverage. This is the first targeted coverage test, not authorization to replace it with an arbitrary cutoff.



### 7. Measurement tooling defect â€” confirmed, not a rendering cause



`scripts/run_quest_temporal_visual_gate.ps1:255` labels `GD` as GPU duration, and later text treats `CPU&GPU` as execution workload. Correct these semantics before using that runner for optimization decisions. Preserve raw fields and existing failures. Do not lower acceptance thresholds to make results pass. The comparison above used `App` correctly and is unaffected by this parser defect.



## Proposed execution after approval



### A. Resolve any wearer-dependent baseline need first, then profile autonomously



1. Preserve the current APK/hash, source manifest, settings and logs. Use a separate scoped external diagnostic directory. Keep normal rendering as the visual reference. The existing timing and takeoff evidence has already been rechecked; do not ask the user to repeat it.

2. Correct the parser labels/derived gates described above, with a small fixture from the existing VrApi rows. Separate GPU work, actual CPU work, scheduling waits, delivered cadence and aggregate-versus-frame statistics. Treat historical reports as historical; do not rewrite them as newly passed.

3. Prepare one current-source diagnostic build only if required for GPU/CPU attribution and matching attachment captures. Disable automatic profiler connection and readback; request each explicitly. Verify parity with the release before interpreting it. Reuse the existing expiring request in `QuestTemporalVisualGateRecorder`; only extend it if existing moving-demo execution cannot supply the necessary data. Do not build a four-block benchmark harness as a prerequisite. No new production camera, flight authority or tracked head-pose override.

4. Use two short moving native-flight scenarios: airport-area buildings/clouds and the dense foothills. Both must be safely airborne above the existing gate, with matched start state, time, wind, route and native ownership. Reuse native initialization and existing demo controls. The paused observation fixture is unsuitable because `Time.timeScale == 0` disables SpaceWarp. Log actual pose, altitude, time scale, gate/submission state, tracking/focus, render scale, foveation, clocks and temperature.

5. Attempt unattended collection first on the connected physical Quest with valid tracking and wake state. A stationary headset can reveal GPU/submission costs while the aircraft moves. If it sleeps or loses a valid XR session, invalidate that window rather than misrepresenting it. It cannot establish head-turn visual acceptance. Any temporary wake/diagnostic state must be restored afterward. If a remaining artifact cannot be captured without a wearer, collect the bounded baseline head-turn sequence below **before changing render behavior**, at the user's next available approved session. Other independent profiling can continue meanwhile.

6. Collect one representative GPU frame/pass breakdown and a short CPU scheduling/sample trace per scene, plus clean release-like timing windows without simultaneous recording/readback/profiling. Use device-supported GPU tooling if available; otherwise use existing scene-isolation profiles for timing attribution and the existing motion/depth captures for correctness. Record profiler overhead and unavailable counters.

7. Attribute opaque color, XR motion/depth, cloud color/motion, procedural submissions, shadow work, copies/resolves and uploads. Use existing counters before adding allocation-free markers. Reuse `clouds_off`, `vegetation_off`, `traffic_off` or `buildings_off` only for the suspect subsystem; do not run a large Cartesian test matrix. Isolation changes are diagnostic and never shipping optimizations. A faster isolated scene is not visual acceptance.



Deliverable: a ranked table of measured milliseconds, thread/pass ownership and artifact evidence. The three missing facts are (1) current cloud versus scenery/motion GPU cost, (2) current CPU work versus scheduling waits, and (3) current head-turn color/motion/depth correspondence. If a suspected path is cheap, skip its optimization. Do not promise that the observed 4.19 ms difference can all be recovered. An earlier six-second simpleperf trace points into traffic/tree submission, but was collected from the rejected profiler-enabled build; its percentages are not current release timings.



### B. Apply the smallest measured optimizations, preserving visible output



Prioritize by measured cost and implementation risk. These are the concrete candidate changes, not an instruction to implement all of them regardless of findings:



| Priority candidate | Exact intended change | Required protection / proof |

|---|---|---|

| Unnecessary render-target copies | Set renderer intermediate mode to Auto if the graph supports direct rendering; remove only a proven-unused ordinary depth copy/consumer request. Update authoring to preserve settings on regeneration. | XR provider depth/motion retained; correct target orientation, stereo viewport, foveation, MSAA resolve, shadows and procedural ordering; verify a pass/copy actually disappears. |

| Tree/traffic CPU submission | Build immutable per-frame visible chunks, current/previous matrices and property arrays once; reuse them for color and motion. | Same per-eye conservative visibility, IDs, LOD/fades, previous-frame transforms and instance counts. Avoid shared mutable property blocks being overwritten before deferred submission. No shorter view distances or head-turn guard reduction. |

| Cloud motion cost | Inspect compiled motion variant, then specialize only unnecessary work; preserve required density, coverage and representative-depth calculations. | Source deletion alone is not a gain if the compiler already removes it. Reducing steps, baking clouds or changing opacity is a separate visual tradeoff, not an assumed free optimization. |

| Grove upload spikes | Coalesce updates that are identical/redundant; if full uploads dominate, implement a measured dirty-region/staging-copy path compatible with Unity and Vulkan. | Preserve atomic ownership/color/motion agreement and page handoff. Measure driver waits and added copy costs; partial update machinery is not automatically faster. No canopy holes, density loss or return of popping. |

| Renderer-list draw overhead | Only if dominant, batch compatible static objects separately from legacy dynamic-history objects. | Per-instance previous matrices, stereo and shader variants must be correct. Do not globally re-enable the instancing path that previously corrupted history. |



Small history/property-block optimizations are optional only if the profile shows meaningful cost. Leave native physics frequency, input, training, audio and accepted scenery unchanged unless new evidence directly implicates them.



Implementation anchors and specific invariants:



- **Render-target path:** `ProductForwardRenderer.asset:71`, `ProductQuestURP.asset:22`, `ProductSpaceWarpAuthoring.cs:36`, and `UniversalRendererRenderGraph.cs:303`/`:620`. Auto plus a proven-unneeded ordinary depth request must be considered together: the current graph retains intermediates for either reason. Keep `XRDepthMotionPass.ImportXRMotionColorAndDepth` intact. Before/after graph must show the removed copy; changing an asset value alone is not proof of a gain.

- **Tree batches:** `AirportCloseTreeLodRenderer.DrawTier:1502` and `SubmitTreeBatch:1580`. Move per-instance final admission and property packing into the existing once-per-frame prepared result, then submit the exact same chunks to both passes. Preserve the camera/projection/generation invalidation at line 714. Do not move camera-dependent work to an earlier pose merely to cache more aggressively.

- **Traffic batches:** `KbduTrafficV2Runtime.SubmitChunks:1173` and the corresponding legacy `KbduTrafficRuntime` path. Cache fully packed current/previous transforms and tint chunks per frame/camera; preserve vehicle identity and the actual presented history. The `PrepareVisibleInstances` cache already exists at line 782 and must not be advertised as new work.

- **Cloud motion:** `BoulderCloudShell.shader:477` through its output at line 525, and generator `tools/rendering/port_spacewarp_shaders.py:194`. Inspect the compiled fragment variant and profiler first. Only remove work that actually executes and is unnecessary to density/representative depth. Do not remove all cloud motion or treat a lower opacity threshold as a universal fix.

- **Foliage motion:** `MapBranchGrove.shader:156`, `MapBranchGroveCluster.shader:191`/`:266`, and generator `port_spacewarp_shaders.py:321`. If captures demonstrate mismatched coverage, explicitly separate material opacity evaluation from target-specific sample coverage, derive the motion footprint from the actual color/motion descriptors, and validate both eyes during fades and head turns. The required mask/depth policy must be selected from the failing capture; it is not established yet. Do not claim this conditional shader change is already a proven cure.

- **Grove upload:** `MapBranchGroveRenderer.cs:387`. Measure its existing `OwnershipUploadTotalMs`/`UploadedBytes` counters first. A 3 MiB upload is a fact; a material frame stall is not yet established.



### C. Resolve remaining artifacts with data, separately from speed



1. Capture synchronized color, motion and depth for both eyes on the current candidate, with actual frame interval and motion history. Capture while moving; exclude readback frames from timing results.

2. Check known static-building reprojection, aircraft rigid motion, cloud wind, foliage alpha coverage and history resets. Confirm correct coordinate frame, NDC sign, reversed depth, eye slice, current/previous transforms and reset sequence against the retained implementation. Reopen those contracts only on a demonstrated discrepancy.

3. Compare color-visible edges/holes with lower-resolution motion coverage, especially the cloud-opacity boundary and distant foliage/building silhouettes. If source color is stable but generated imagery is not, don't blur the original textures as a substitute for fixing motion coverage.

4. Apply a focused shader/history correction only to demonstrated bad data. Do not zero vectors, suppress scenery motion, inflate strut masks blindly or force a foreground depth across unrelated background pixels. Repeat the local capture and stable-timing comparison after the correction.

5. If the buffers are correct and stable cadence still produces objectionable reconstruction, record the limitation and stop pursuing SpaceWarp as the preferred mode. The ordinary renderer retains the useful optimizations. Increasing motion-buffer resolution requires provider support and another performance tradeoff; it is not a free setting assumed available here.



### D. Minimum checks and a bounded baseline capture if needed



Run focused batch/history/coverage checks and relevant shader/build gates. Inspect stereo captures for changed render paths as required by AGENTS.md; simulator output is visual evidence only. Build one identified standalone candidate after the selected changes, not a new APK for every speculative setting.



If valid unworn device execution works, use it for the profiling and repeated timing work. Only head movement and subjective visual quality ultimately require the user. Any wearer-dependent baseline diagnosis belongs in phase A, before render fixes. Final subjective acceptance of a changed APK is a separate later check, not something the user must remain available for today.



Proposed baseline worn sequence, **not a request to perform it now**; aim for 90 seconds of useful observation and no more than three minutes wearing including setup:



- Have the matching diagnostic build, active-flight scenario above 5,500 ft MSL and capture requests ready before asking the user to wear it. If preparing this is not possible during their availability, continue independent work and defer the capture; do not install an obsolete diagnostic merely because it is ready.

- Use one automatic flight through the affected view with SpaceWarp confirmed active. The existing OFF/ON timing comparison need not be repeated. Brief instructions before wearing: look forward at the horizon for 30 seconds, then slowly turn left/right for 45 seconds while the aircraft flies automatically, then hold the affected view for 15 seconds. No manual flying, menus or chat replies during the capture.

- Collect a clean timing/CPU window first, then a few request-only color/motion/depth frames during the motion phase. Mark and exclude readback frames from performance analysis. If an in-headset end cue cannot be prepared without a new build, arrange a fixed audible timer before wearing; do not rely on a chat message the user cannot see.

- Restore temporary diagnostics and the prior preference afterward. Request one report only after completion: location of distortion and whether it was visible during the capture. Abort an invalid XR/tracking run rather than extending the session silently.



Final candidate acceptance: no return of strut distortion, facade flicker, foliage popping or cockpit/text degradation; stable 36 real frames / 72 display Hz with ON. Target actual per-frame GPU and critical CPU work p95 no more than about 25 ms (roughly 10% engineering margin below 27.78 ms), alongside measured cadence/missed frames. This is a proposed optimization target, not proof of quality or a replacement for existing release gates. One-second aggregates must remain labeled as such if finer data is unavailable. A short worn capture is diagnosis, not a long-duration thermal certification or acceptance of a subsequent APK.



## Alternatives considered



- **More streaming / GTA-style world sectors:** this project already streams scenery and uses page ownership, distant proxies and LOD. Streaming can fix I/O/memory spikes, but does not remove the visible per-frame cloud/motion workload. Pursue only if the trace shows stalls at page crossings.

- **More aggressive culling:** retain conservative stereo culling and predictive coverage that fixed tree popping. Cloud sector subdivision is demoted because the entire authored field is only one draw/96 triangles. Generic cockpit/terrain occlusion or a new GPU occlusion system needs proof that occluded drawing dominates before adding complexity.

- **More foveation / dynamic resolution / less MSAA:** useful tradeoff tools, but they change quality; increasing them first conflicts with clear instruments and the present aliasing complaint. Current comparison already used the same foveation. The earlier FFR-OFF experiment did not remove distortion.

- **SRP Batcher / GPU Resident Drawer:** current legacy shaders and property blocks are not automatically eligible. Enabling a checkbox or rewriting every shader is not the fastest demonstrated fix. Consider narrowly only after draw-call attribution.

- **Cockpit in a separate compositor layer:** may help suitable flat UI; a full 3D cockpit with occlusion, stereo parallax and moving instruments is not a small equivalent replacement. Do not introduce another camera stack to conceal reconstruction problems.

- **Baked cloud impostors / another canopy representation:** potentially useful if those passes dominate after simpler fixes, but they change appearance and reopen substantial accepted work. Not part of this first implementation.

- **Higher refresh rate, forced clocks, physics-rate changes:** not a substitute for reducing measured work. Higher display rates tighten the deadline; clocks already ran near the observed upper range. No demonstrated native simulation bottleneck justifies physics changes.



## Decision and stopping rule



Do one profile-driven optimization cycle, not another series of speculative visual workarounds. Keep changes only when they produce repeatable improvements beyond measurement noise and preserve appearance. If correct-buffer, stable-cadence SpaceWarp is still visibly worse, recommend OFF and continue improving normal rendering. Do not claim every artifact can be fixed within the current hardware budget.



The current review covers the renderer, motion/history paths, clouds, foliage/groves, traffic submission, pipeline configuration, diagnostic tooling, relevant native timing and existing captures/logs. It is not an exhaustive performance review of every first-party subsystem. Exact per-pass GPU costs, current CPU hotspots, head-turn frame pacing and the source of each current artifact remain to be measured after approval.





## Approved implementation progress â€” October 5, 2026



External evidence root: `T2-QuestFlight-product-artifacts/spacewarp_optimization_20261005`.

The pre-edit files were copied and SHA-256 verified under `before`; the existing altitude-gated APK and its source identity were retained. No branches, working changes, source assets or build caches were removed.



Implemented candidate:



- Corrected the VrApi parser: `App` is the application GPU duration; `GD` is boundary GPU time. `CPU&GPU` is a wall-time interval including waits, not independent CPU execution time. Reports identify one-second aggregates and explicitly say CPU execution timing is unavailable. Existing failures/thresholds were not relaxed. Parser regression suite PASS, including distinct nonzero App/GD values.

- Tree final culling/packing and traffic current/previous-matrix/tint packing now produce separately owned, pooled chunks once per existing prepared frame/camera. Color and XR motion reuse those chunks. Tree view/projection/history invalidation, visibility guards, distances, fades, identities and presented vehicle history are retained. Legacy parked/context traffic also reuses property arrays between passes.

- Added allocation-free profiler markers for tree/traffic packing and color/motion procedural submissions. These support attribution; marker presence is not evidence of a frame-rate gain.

- Request-only diagnostic attachment metadata now includes actual color/motion/depth sample counts/formats, frame interval and time scale. Capture remains development-only and does not automatically read back frames.



Focused checks: 49/49 EditMode tests PASS in `editmode_verified`, including multi-chunk boundaries, rejected offscreen trees, independent property storage, tree fades/impostor frames, every previous vehicle matrix row, empty/reduced submissions, storage reuse, and existing SpaceWarp motion/altitude contracts. An earlier new test fixture mistakenly enabled an unauthored traffic component and failed its expected authored-asset validation; the fixture was corrected to stay inactive. The skipped historical strut renderer-list capture requires its external diagnostic directory and is not counted as passed.



A warmed Editor microbenchmark measured 0.0494 ms for the 2,050-vehicle packing operation removed from a repeated pass, excluding native draw submission. This is small on this workstation and is **not Quest timing**. It does not establish that batching is the dominant remaining bottleneck or solve the GPU deficit. Retain this as a candidate until matched device measurements support it; do not extrapolate the old debug-profile percentages.



The attempted unattended baseline is INVALID: the system boundary dialog prevented product execution. Its log contains system VrApi rows, not a qualifying product window. The expiring diagnostic request was removed, the app was stopped, and normal proximity/sleep behavior was restored (`baseline_production/restoration.txt`). The user has been asked only to clear that dialog if available; no new flight/head-turn test is requested while builds run.



Conditional work intentionally not applied without the required evidence:



| Item | Current decision / prerequisite |

|---|---|

| Intermediate targets / ordinary depth copy | No settings change. Need actual render-graph/pass evidence that a copy can be removed while retaining provider depth/motion. |

| Cloud motion specialization | No shader change. Need GPU attribution and compiled-variant evidence; source-level lighting expressions may already be eliminated. |

| Grove ownership uploads | No streaming rewrite. Need existing upload counters/driver timing to demonstrate a stall. |

| Alpha coverage / remaining artifacts | No heuristic mask change. Need current-source moving color/depth/motion captures, now with sample descriptors, before selecting a correction. |

| Static renderer-list batching | No global instancing toggle. Must preserve accepted per-object history and establish a measured gain. |

| Hardware cadence / quality acceptance | Pending valid Quest execution and later subjective head-turn acceptance. Earlier user rejection of SpaceWarp ON remains authoritative. |



These are evidence gates from the approved plan, not completed optimizations. Do not mark the overall SpaceWarp repair complete merely because the batch candidate compiles or builds.





Local validation update: the standard Visual QA runner produced 130/130 captures. Its overall result is **FAIL_MACHINE_QA**, matching the retained altitude-gated reference: resource estimates exceed the existing planning thresholds in some views, and two ground views fail the runway-contact-shadow check. All 130 per-shot draw/triangle/material estimates and failure/warning results are identical to that reference. The failure list was not weakened. Seventy-eight PNGs match byte-for-byte; the inspected highway/Valmont traffic images are pixel-identical and the inspected mountain image differs on approximately 0.001% of pixels. These static checks do not establish Quest performance or synthesized-frame quality. Comparison evidence: `visual_qa/baseline_comparison.json`.





Stereo validation: built a fresh isolated Meta XR Simulator player and inspected both-eye captures. The first mountain fixture exceeded that player's rebuilt imagery coverage and failed with target-incompatible bundle loads; retained under `stereo_outside_built_coverage`. Repeated the live capture at the supported airport without another build: both eyes are populated and consistent, cockpit/runway presentation is intact, and the session has no runtime errors. The machine session still reports `mountainIdentityImmutable=false`; the full simulator gate remains **BLOCKED_MISSING_RECORDED_REPLAYS**, not PASS. This is limited stereo smoke evidence, not validation of SpaceWarp synthesis or mountain motion. Runtime/persistent configuration was restored, shipping Android configuration was unchanged, and the generated temporary project copy was removed by the existing runner.



## Moving-flight evidence and final CPU candidate

After room tracking was restored, the authored demo ran with native JSBSim, timeScale=1 and the 5,500 ft gate active. Earlier startup/paused samples are excluded. Unity Android build/QA logs explicitly report terminating the other SDK's ADB server; this interrupted continuous logcat, not the on-device flight. Completed temporal reports and bounded snapshots recover the measurement windows. Do not run further Unity Android tooling during device profiling.

- `baseline_production_162257/active_flight_cpu.data`: 3,333 CPU-cycle samples in eight seconds of active flight before readback. Traffic V2 preparation is 21.77% inclusive, parked/context preparation 15.40%, and corridor plane testing 9.85% (nested within V2). These are CPU-cycle proportions, not additive frame milliseconds; some platform XR library symbols are unavailable.
- Parked/context traffic now caches immutable parked poses and reads the camera position once per preparation. Every authored pose matches its former TRS calculation exactly.
- Corridor culling now evaluates the same six-plane/AABB test without repeated native array marshalling. Bounds, eye union and invalid-bounds fallback are unchanged; a conservative 1 mm numerical margin retains boundary objects. A deterministic 16,384-case comparison across map-scale positions, orientations and bounds matches Unity, with separate boundary tests. All 60 focused tests PASS (`editmode_plane_cache`).
- Grove CPU counters over 1,869 measured frames average: gather 0.995 ms, fill 1.206 ms, buffer-upload calls 0.046 ms, ownership-upload calls 0.129 ms per frame. Deferred GPU/driver cost is not measured by these counters. They do not support prioritizing a streaming rewrite or claiming a large upload stall.

Matched 90-150 second demo windows on diagnostic APK `b9b74259c23ab056ab8ff749a84016a85e9ef219e3e6acc06bbb9ecf89fddd12`:

| Diagnostic scene | App FPS | Mean GPU aggregate | p95 GPU aggregate | Aggregates over 27.78 ms |
|---|---:|---:|---:|---:|
| Full scene | 31.14 | 26.22 ms | 32.51 ms | 17/60 |
| Cloud drawing temporarily disabled | 35.98 | 18.81 ms | 21.61 ms | 0/60 |

SpaceWarp submission is confirmed throughout both windows. Temperatures were 47-48 C and 48 C respectively. GPU numbers are 60 one-second VrApi aggregates, **not per-frame p95 or isolated pass timings**. FrameTimingManager returned no valid GPU timings. Fixed-view route timing is matched; identical gaze/cloud drift and sustained thermal behavior are not proven. Clouds-off is diagnostic only, restored at restart. Evidence: `unattended_comparison.json`, raw logs and temporal reports.

Cloud rendering is now a demonstrated GPU priority. This does not justify disabling shipping clouds, reducing visual quality, or assuming unused source lighting survives shader compilation. No cloud shader/asset, coverage threshold, foveation, MSAA, scenery range, motion-history policy or flight authority changed in this cycle.

Current-source moving capture `current_capture`: 1,680x1,760 4-sample color and 420x440 single-sample motion/depth in both eyes; history 8, no reset, timeScale=1. All attachments are finite. Raw sky reprojection agrees within 0.000062 NDC in both eyes. Both color images were inspected; resting controllers obscure part of the upper view. This does not reproduce a head turn, establish every foliage/cloud edge correct, or overturn the user's SpaceWarp artifact report.

Final diagnostic APK: `quest_final_candidate/QuestFlight-SpaceWarp-optimized-candidate.apk`, SHA-256 `b357e74b82989b3c4eaf704de631838599b0a0d211ab054f9862c2e51ebfeb80`. Full working-source identity/archive and build log accompany it; HEAD alone is insufficient. Payload verification: 1,912/1,912 files. Native JSBSim remains required. Automatic profiling/debugger connection is disabled. This development diagnostic candidate is not a published or headset-accepted release.

The user removed only generated `meta_xr_simulator/player` after automatic cleanup approval was rejected. Its prior inventory was 3,217,755,221 bytes; captures/reports and source remain. No source assets, personal files, branches or stashes were deleted.


## Final measured outcome and remaining work

Final candidate `b357e74b...` completed a 60.00-second production window with 1,850/1,850 frames confirming application SpaceWarp submission. Result: **30.83 FPS**, mean GPU aggregate **27.02 ms**, p95 GPU aggregate **31.67 ms**, 22/60 GPU aggregates above 27.78 ms, temperatures 48-50 C. The initial diagnostic production window was 31.14 FPS. The intermediate parked-cache-only candidate was 30.99 FPS. These are **not an end-to-end performance improvement**, and the candidate must not be promoted as the SpaceWarp solution.

The final eight-second CPU profile contains 3,031 samples, zero lost: parked/context preparation fell from 15.40% to 4.85% of sampled cycles; the corridor-culling path is 8.77% inclusive versus 9.85% previously. Overall sampled events fell from 17.367 to 14.614 billion cycles, but differing CPU clocks, scene timing and sampling make that unsuitable as a guaranteed frame-time reduction. The final profile includes the new plane-test function, proving the optimized code executed. Repeated comparable runs are still needed before claiming a reproducible overall gain. Controller visibility also differs between the early attachment screenshots and final screenshot, so captures are not pixel-matched A/B evidence.

Final captures (`final_capture`) were inspected in both eyes: finite attachments, unchanged descriptors, valid sky reconstruction; no subjective head-turn acceptance. The final 130-view standard QA run has identical resource estimates and per-view failures to the earlier candidate, with 85 byte-identical PNGs. Its inherited overall **FAIL_MACHINE_QA** remains, and the earlier simulator gate remains blocked by missing replay coverage. Source recheck confirms all 3,022 changed Unity input hashes match the final APK identity (`final_build_input_recheck.json`). No merge, commit or release was made.

Remaining, explicitly **not implemented or accepted**:

1. **Cloud GPU optimization is the highest-value next step.** Investigate its executed texture/ray-march cost and preserve the same density, sampling lattice, lighting and motion depth. The inspected shader performs two atlas fetches per density sample. A 64x32x64 Texture3D could replace the atlas-address calculation and manual slice interpolation with hardware volume filtering, but the current Android atlas is ASTC-compressed. Uncompressed volume bandwidth, precision and filtering can differ; this is a new measured A/B candidate, not an assumed free win. Author it offline from verified density values, retain the atlas reference, validate density/image equivalence and actual Quest GPU timings before accepting it. Unity's [Texture3D API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/texture3d) supports offline population/upload; API availability is not a performance claim.
2. **Ordinary depth/intermediate copies:** still need actual frame-graph consumer evidence before changing the URP configuration. No depth/motion attachment was disabled.
3. **Head-turn reconstruction artifacts:** unresolved. The captures validate static-head moving-flight sky math, not the reported head-turn failure or every cloud/foliage silhouette. No heuristic coverage change is justified by these frames. Final worn validation remains necessary when the user is available; no further wearing was requested in this cycle.
4. **Acceptance:** current native-72/SpaceWarp cadence targets and inherited machine QA failures are not passed. Preserve OFF as the recommended quality reference; the saved user preference was not changed.

Device restoration is complete: prior APK SHA-256 `575ea4afe1f5c82edccfc797d85cf330aba9c47242c3322d34dbed39e16f0f0b` reinstalled and verified on-device. Product stopped, request files removed and normal proximity/sleep restored. The new candidate remains saved externally for further work, not silently substituted for an accepted build. Approximately 2.16 GiB disk space remains after the user's generated-player cleanup and final packaging.


## Cloud optimization implementation candidate — October 5, 2026

Approved bounded follow-up is in progress, **not a measured performance fix**. External evidence: `T2-QuestFlight-product-artifacts/cloud_optimization_20261005`. Exact pre-edit files are hash-verified under `before`; all previous local changes remain.

Implemented offline 64x32x64 RGBA32 density volume, conservative 16x8x16 occupancy volume, and shared color/XR-motion sampling helper. Volume coordinates preserve the atlas texel-center convention. Occupancy bounds both density archetypes, includes the complete interpolation footprint plus a numerical halo, and skips only samples below the original body threshold. The occupied-ray sample lattice and opacity-weighted motion depth remain unchanged. Occupancy is deliberately disabled on the ASTC atlas path because its compression can differ from the uncompressed derivative. Both volumes are serialized, not CPU-readable, have source/license entries, and allocate no runtime hierarchy. New diagnostic profiles: `clouds_atlas`, `clouds_volume`, `clouds_volume_skip`; prior material values restore afterward. Shipping defaults remain atlas/skip-off until Quest measurements justify promotion. The shader migration generator preserves the new shared implementation.

Validation so far:
- First focused run: 56/57; failed missing serialized density reference. Authoring now reloads persistent asset handles after native asset import.
- Corrected focused run: **57/57 PASS**, `editmode_verified/unity_EditMode_test_results.xml`. Includes all-voxel atlas orientation, 100,000 conservative occupancy samples, serialized texture contracts and existing cloud/SpaceWarp tests.
- Shader visual fixture: 24 rendered images (four viewpoints, two eye offsets, three paths), `cloud_visual_qa`. Volume+skipping is pixel-identical to volume-only in all eight comparisons. Atlas/volume differences average 0.026–1.278 byte values/channel with localized maxima up to 34/255, so they are NOT byte-identical representations. Inspected below/above/inside/distant views retain silhouettes and lighting. These are Editor shader-fixture captures, NOT Meta XR Simulator or Quest acceptance.
- Canonical Android build preflight: PASS library isolation, path length and authored StreamingAssets. APK build and hardware timing: **NOT RUN**; only about 2 GB free, insufficient safe packaging headroom alongside a roughly 1.84 GB APK plus intermediates.

Automatic cleanup review rejected deletion of two superseded diagnostic APKs. Exact commands were provided to the user; current accepted and latest candidate APKs remain. No files were deleted by this work. Required next work after space is freed: build diagnostic APK, verify payload/hash, measure atlas/volume/volume+skip with the same demo and ON/OFF conditions, retain only measured wins, assess conditional cloud-only reduced resolution if insufficient, then standard visual QA and inspected stereo validation for the selected candidate. Do not claim 36 FPS, smooth head-turn synthesis, completion, installation or user acceptance from these Editor checks.

## Cloud candidate continuation — cleanup, packaging and first Quest measurement

External evidence remains `cloud_optimization_20261005`. The user completed cleanup. The diagnostic APK was built through Gradle, signature/ZIP/scenery-payload checked and installed: SHA-256 `db67936975c77b062d9cccd6c628618649932f007fdaa3ebb69c8e1209cefc34`, 1,837,787,414 bytes. Canonical Unity build completion is **BLOCKED by disk space**, not PASS: Unity failed copying the complete Gradle APK to `Builds/Android` after packaging. The later canonical retry also failed that final copy, even after lossless cache compression; it produced `quest_verified/QuestFlight-cloud-packaged.apk` (SHA-256 `a1a56c9056199fe8bd362f3d9754208e6c021a3c2ac9776357632229f547100a`). Neither is a release or headset-accepted optimization. The installed diagnostic's exact working-source archive is under `quest_candidate`.

One valid 60.015-second moving-demo window completed on the volume-only profile (`baseline_clouds_volume_on_182043`): **32.57 FPS**, 42.08 ms delivered-frame p95, mean GPU one-second aggregate **25.65 ms**, p95 aggregate **30.27 ms**, 14/60 aggregates over 27.78 ms, 44–46 C. All 1,955 recorded frames confirmed SpaceWarp submission. This misses stable half-rate delivery. It is **not a proven improvement** without the matched reference. FrameTimingManager provided no valid GPU timing; aggregate p95 is not per-frame GPU p95.

The first atlas window is excluded (`INVALID_WINDOW.txt`): focus/tracking changed just before measurement, and SpaceWarp was inactive for all 2,537 measured frames. A screenshot was also taken during that diagnostic window to inspect the foreground. The volume+skipping launch was blocked by a recurring Guardian dialog before the demo started and was stopped; no timing claim is made for it. Requests were removed and the saved SpaceWarp preference restored after each run. Further device comparisons require stable room tracking and foreground focus; do not bypass those conditions.

Lossless NTFS compression, with scoped path/reparse-point checks, preserved all content while compressing generated Bee Android artifacts and Gradle intermediates. The first two operations recovered approximately 4.34 GB in allocated storage combined, but rebuild regeneration consumed the headroom. Compression of imported `Library/Artifacts` was also started; its external log records the outcome. No source assets, personal files, branches or stashes were deleted. SDK-generated `RuntimeActionBindings.json` and its paired meta left by the failed build were identified in Meta's `OVRDumpBuildInfo` pre/post-build callbacks and preserved outside the project before retrying preflight; validation was not weakened.

Still incomplete: matched atlas/volume+skip and ON/OFF measurements; selecting a measured winner; conditional cloud-only reduced-resolution assessment if these gains are insufficient; canonical build completion; standard Visual QA and inspected stereo simulator validation for the selected candidate; worn acceptance. Shipping material defaults remain atlas with skipping disabled. Do not present this continuation as a finished SpaceWarp performance repair.

Restoration verified: prior reference APK `575ea4afe1f5c82edccfc797d85cf330aba9c47242c3322d34dbed39e16f0f0b` is installed again; product stopped, diagnostic requests removed, normal proximity restored. Import-cache compression completed: 2,363,399,208 logical bytes occupy 1,321,548,665 bytes, approximately 1.04 GB recovered without content changes. Approximately 2.71 GB free remains. No further build retry is justified until sufficient packaging headroom is available. Canonical build and selected-candidate visual gates remain incomplete; the prepared diagnostic and measured results are preserved.


## Unattended cloud comparison and headset power tooling

The valid sequential comparison used diagnostic APK `db67936975c77b062d9cccd6c628618649932f007fdaa3ebb69c8e1209cefc34`, the same authored demo 90–150 second window, no screenshots in the timed window, and a stationary unworn headset. Six completed windows contain no logged focus-loss/pause/fatal markers. This is diagnostic evidence, not worn or sustained thermal acceptance.

| Cloud sampler | SpaceWarp ON FPS | OFF FPS | ON mean GPU aggregate | OFF mean GPU aggregate |
|---|---:|---:|---:|---:|
| Atlas reference | 32.13 | 35.46 | 24.89 ms | 22.71 ms |
| Hardware volume | 33.74 | 38.09 | 24.00 ms | 21.84 ms |
| Volume with empty skipping | 31.01 | 33.47 | 27.16 ms | 24.84 ms |

Volume-only is the leading candidate: approximately 5% higher delivered FPS ON and 7.4% OFF in these windows. Empty skipping is rejected for promotion: slower in both modes. Temperatures increased across runs (47–54 C); this is not a controlled thermal acceptance or proof of stable 36 FPS. GPU values are app-PID VrApi one-second aggregates, not per-frame GPU timings. Full evidence: `unattended_comparison.json`, `measurement_window_review.json`, raw run logs. Shipping material is still atlas/skip-off pending selected-candidate validation.

The recurring headset interruption was partly caused by test tooling. Installed MQDH source maps proximity override to `prox_close` and restoration of physical proximity to `automation_disable`; the old scripts sent `prox_far` and `automation_enable` between runs. `scripts/quest_proximity_state.ps1` now parses the actual device override, rejects unknown output, and selects restoration. Both temporal and grove visibility runners preserve prior state and support `-KeepHeadsetAwake` for a comparison batch, with before/after dumps. Parsing/mapping checks and PowerShell syntax checks PASS. Consecutive unattended runs succeeded without another operator intervention after tracking was restored. This prevents unnecessary sleep transitions; it cannot guarantee room relocalization or replace valid tracking. Guardian and product camera ownership are unchanged. See `docs/QUEST_UNATTENDED_TESTING.md`.

Cloud-only lower resolution was assessed against the current pipeline: the cloud shell is a normal `UniversalForward` transparent renderer (`Transparent-20`) and its separate `XRMotionVectors` pass writes opacity-selected depth. `ProductSpaceWarpRenderFeature` submits the transparent motion renderer list directly into the XR targets. There is no existing cloud-only offscreen color/composite pass. A correct reduced-resolution path therefore needs a separate stereo array target, explicit cloud-only filtering, depth-aware composition before other transparents, correct viewport/foveation handling, and preserved full-resolution motion/depth semantics. Simply lowering the entire eye target or substituting an upscaled color pass would not preserve cockpit/scenery detail or validate synthesized silhouettes. No reduced-resolution rendering has been implemented or measured; it remains a conditional follow-up, not a claimed saving.

Standard Visual QA produced 130 captures in `visual_qa_reference`, retaining atlas selection. Overall FAIL_MACHINE_QA is inherited: all per-view resource estimates, warnings and failures match the previous reference; 84 PNGs match byte-for-byte. Two cloud images were inspected. This does not validate a promoted volume candidate. The required current-candidate stereo simulator gate remains NOT RUN.

Canonical build attempt `quest_completed` exhausted disk during Gradle packaging. The verified build process was stopped and Unity exited failed; no canonical PASS or release APK is claimed. Lossless NTFS compression of generated caches preserved their content and recovered space, but another rebuild must wait for sufficient headroom and removal of the known SDK-generated RuntimeActionBindings JSON/meta residue. Those files are backed up externally; their pre/post-build origin was verified in Meta's OVRDumpBuildInfo. Exact deletion was rejected with “blocked by policy”; no policy configuration was changed or bypassed. The error does not identify its enforcement layer. User cleanup commands cover only that residue and two superseded APKs; the current diagnostic and prior reference remain preserved. No source/personal files, branches or stashes were deleted.

Warmer atlas ON repeat `baseline_clouds_atlas_on_195418`: 30.70 FPS, mean GPU aggregate 26.953 ms, p95 aggregate 32.75 ms, 28/60 over27.78ms, 51–52 C, 1,843/1,843 SpaceWarp submissions. This supports carrying volume-only forward, but is not a thermal acceptance or a stable36 claim. The device reference is being restored while packaging remains blocked.

Final device readback: installed reference SHA-256 `575ea4afe1f5c82edccfc797d85cf330aba9c47242c3322d34dbed39e16f0f0b` verified. App stopped; collectors removed requests and restored the saved SpaceWarp preference. After the complete batch, physical proximity restored and read back as `DISABLED` (`power_final.txt`). No further headset action requested. Canonical packaging and selected-candidate validation remain blocked/incomplete, not accepted.


## Selected volume validation and build continuation

User cleanup supplied18.0GB free and removed the SDK residue. Material now selects volume filtering1/empty skipping0; `BoulderCloudAssetAuthoring.BuildMaterial` reproduces that selection. Existing source work is preserved. `quest_volume_selected/source_identity.json` matches both changed inputs and records the complete working tree snapshot.

Selected-volume standard QA generated130 captures. Its overall FAIL_MACHINE_QA and all per-view resource estimates, warnings, pass/fail and errors exactly match the atlas reference (`visual_qa_volume/reference_comparison.json`). Pattern and sunward cloud images were inspected; no silhouette/lighting regression identified. Python image analysis exits2 for the inherited overall failure; do not call this a full PASS.

Canonical Android preflight PASS, native compilation completed, then Gradle failed `mergeDebugJniLibFolders` after exhausting disk. Cache compression recovered headroom without deletion; generated Gradle continuation reused the compiled player but failed `compressDebugAssets` when disk filled again. No new complete APK exists. Logs: `quest_volume_selected/unity_product_build.log`, `gradle_resume.log`. A fresh SDK binding residue was recreated; exact user cleanup requested. No retry is justified without substantially more free space. Plan about20GB available for the remaining packaging plus isolated stereo validation; this is headroom guidance, not a measured guarantee. Current-candidate stereo remains NOT RUN. Reference APK remains installed; no headset action requested.

External collector audit also found growing whitespace in saved PlayerPrefs XML caused by Windows newline conversion through adb shell. All8 entries and values match the original exactly (`preference_whitespace_review.json`). The external collector now writes LF only. No shipping code or preference values changed. The installed reference is not debuggable, so no device preference rewrite was attempted. Measurement windows start after the90second demo warmup; this finding is disclosed rather than silently altering prior evidence.


## October6 build recovery

User supplied21.5GiB free by removing the complete Library cache. Current authored build-input hashes still matched the saved candidate. Recreated Library with inherited lossless NTFS compression; fresh Android import PASS. The user removed the SDK residue and canonical preflight PASS. Fresh production ASTC import/native compilation progressed, but the native linker failed with Windows exception0xC0000006, an in-page I/O error, rather than a source/compiler diagnostic. No matching disk/NTFS/resource-exhaustion System event was found in the inspected20minute window. Do not infer a hardware fault or prove compression as the cause from this exception alone.

Replayed the exact generated clang response command with the completed object files in isolation: exit0; llvm-readelf confirms ELF64/AArch64 shared object. Output remains compressed. An attempted directory-only compression adjustment initially used an incorrect relative path and did not change the native output directory; Library's compression flag was restored immediately. No source contents were changed. The successful isolated retry therefore does NOT demonstrate that disabling compression fixed the error. Preserve the native cache; no need to recompile all objects.

No complete new APK or headset install in this continuation. Evidence: `quest_volume_recovery_20261006/unity_import.log`, `unity_product_build.log`, `native_link_retry.log`, `native_link_command.rsp`, `native_link_elf_header.txt`.

New scoped cleanup attempts for the superseded traffic-cache and profile diagnostic APKs were rejected with “blocked by policy.” Prepared `cleanup_superseded_20261006.ps1` for user execution, with expected hashes, source-archive checks, retained APK checks, reparse-path rejection and inventory. It includes those two APKs, the previously identified two superseded APKs, and the SDK JSON/meta recreated by the failed build; SDK backups are verified under `failed_sdk_20261006`. Approximate recoverable APK size7.36GB. The current diagnostic and reference remain preserved. Cleanup is pending, not completed; no policy was overridden. Afterward retry canonical build using the preserved Library, save/verify the APK before removing packaging duplicates, perform required stereo validation, then install on the connected Quest. Earlier measured gains and inherited QA limitations remain unchanged.
