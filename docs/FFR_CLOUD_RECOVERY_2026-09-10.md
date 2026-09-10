# Foveation and cloud recovery — September 10, 2026

The user approved implementation and subsequently wore the longer-range canopy build for a brief High/Medium foveation check. They explicitly reported that High looked great with no noticeable quality loss and requested no further Medium testing. **High is the selected candidate setting. Performance acceptance remains open.**

## Implemented

- The existing authored `ProductionVerticalSliceRoot` applies serialized High fixed foveation after the XR display starts, reapplies it on application resume, and logs requested/actual levels. Dynamic adjustment is explicitly off. No hierarchy or camera is created or moved.
- Android Meta XR Subsampled Layout is enabled through the existing OpenXR feature. Built-in rendering, Legacy FFR, Vulkan, ARM64 and the one tracked XR camera remain in use.
- Grove selection now exposes cumulative, allocation-free CPU timing counters for gathering, filling, ComputeBuffer upload and ownership-texture upload, plus selection count and uploaded bytes. The temporal recorder captures these counters. Differences between samples identify work during the observed interval; upload durations include possible driver waits and are not GPU execution times.
- A cloud sampling LOD shader experiment interpolates maximum budgets from 48 toward 24/12 samples using cloud-surface distance and conservative projected size. Both eyes use the same selection inputs. Near/inside formations retain full sampling; integration spans the whole ray and optical depth uses actual sample spacing. It was initially disabled; **the subsequently approved `2688dc3e…` development candidate enables it**, with performance/temporal acceptance still open.
- The host temporal runner now allows at least 120 seconds to observe the start of its 90-second warmup's measurement window, even when the readiness timeout is shortened. The earlier 30-second readiness option incorrectly limited that wait to 60 seconds.

## Evidence and limitations

Artifact root: `../T2-QuestFlight-product-artifacts/ffr_cloud_recovery_20260910/`.

- Offline authored configuration, existing cloud contracts and the grove batch checks pass, including all 1,296 page hashes/records and corruption rejection. The initial configuration check used Medium; the subsequent user feedback changed the serialized level and its assertion to High before the Android build.
- All 12 requested Android Vulkan shader variants compiled.
- Twenty offset-eye Editor cloud images cover near, middle, far, reverse and inside views with LOD off/on. Inspected images retain volumetric shapes; the inside pair is pixel-identical. Other views change 421–13,816 pixels, with maximum channel differences up to 42. These are real shading differences, not a claim of exact preservation or temporal acceptance. `offline/image_differences.json` retains all measurements.
- Standard QA produced 130/130 captures with hierarchy/environment/single-XR checks passing. **26 cost failures remain**, with maxima of 86 estimated draws and 1,026,876 triangles, matching the longer-range candidate's existing maxima. Limits were not raised.
- Simulator audit remains `BLOCKED_MISSING_RECORDED_REPLAYS`; runtime configuration was preserved. Offset-eye Editor captures are not a substitute for tracked replay acceptance.

## Quick worn check

The running package was the preserved longer-range `cc877d4e…` APK, not the new build. Runtime system overrides tested High and Medium without changing canopy geometry or cloud sampling. The first attempt was interrupted by a headset reboot; the retry confirmed the cockpit was visible before switching levels. The user then approved High's appearance.

`quick_worn_retry/logcat.txt` retains 26 High app-PID VrApi samples before the screenshot interval, averaging **47.92 FPS**. There is no retained matched Off window; the start marker rotated out of the system log. The quick moving-route observation therefore establishes neither a quantified High speedup nor 72 Hz acceptance. The earlier approximate 49 FPS included four screenshot-interval samples; exclude those for the timing summary. High was confirmed by Fov=3. The diagnostic override was restored to its original empty value and the request file removed after the retry.

Earlier unattended screening is invalid: the first run used the insufficient host warmup timeout; another failed to acquire an app PID; the High log stream was interrupted during Editor activity and was recovered before retrying. The subsequent headset reboot further interrupted evidence. Preserve those attempts but do not combine them into a successful benchmark.

## Completed post-reboot measurements and revised priorities

The Android build passed with all 1,912 packaged scenery files verified. APK SHA-256: `b615df4c7eae9aad2ce3c5b19f88e820c5a79008a7d7533947b247ec6432b325`; 1,995,895,676 bytes. The six modified runtime/settings/shader input hashes matched the build snapshot when `android_build/final_source_check.json` was written. Subsequent CPU preparation edits are not in this APK. This package is installed and its on-device hash was verified. The exact preceding longer-range `cc877d4e…` APK was pulled and preserved as `long_range_reference.apk` before installation.

The initial launch was blocked by **Finding position in room**. Following the user's reboot, `reboot_high/` and `reboot_clouds_off/` both completed their 90-second warmup and 60-second measurement. Startup confirms actual High, dynamic foveation off, subsampled layout enabled, tracked XR readiness and native JSBSim. Both have valid app-PID telemetry, but neither passes performance acceptance or has a new human witness.

| Metric | High, normal clouds | High, cloud volumes disabled |
| --- | ---: | ---: |
| Delivered mean / p95 ms | 17.171 / 28.656 | 16.040 / 26.487 |
| VrApi mean FPS | 57.98 | 62.52 |
| VrApi App mean ms | 13.498 | 12.221 |
| Mean GPU / CPU utilization | 84.4% / 39.0% | 82.9% / 39.7% |
| Stale frames | 1,187 | 953 |
| Maximum reported temperature | 40 C | 42 C |

Direct GPU milliseconds are unavailable. The approximately 1.13 ms delivered-interval difference is a diagnostic signal, not an isolated cloud saving or an upper bound. Inspected warmup and final stereo screenshots have materially different cockpit occlusion/head position and are sky-heavy. GPU clocks also differ: production samples include 545/640 MHz, while cloud-off samples use 640 MHz. These runs cannot predict banked pilot-view gains. The photographic sky remains in both profiles. Do not repeat FFR Off or Medium: the user explicitly prioritizes forward progress with visually accepted High.

`reboot_high/grove_phase_summary.json` records 909 selections, 15.15 selections/second, and 79.83 MiB/s uploaded. Per-selection means: gather 2.319 ms, fill 4.980 ms, buffer upload 0.236 ms, ownership upload 0.394 ms. Preparation therefore occupies about 111 ms of CPU time per second, or 1.9 ms per delivered frame at the observed cadence. It is intermittent work, not 7.3 ms every frame. Removing half would remove about 0.95 ms/frame-equivalent CPU work; this is arithmetic, not a forecast or a GPU/frame-time saving. Upload averages do not justify a buffer-system rewrite ahead of preparation work. High GPU utilization still makes a CPU-only cure unlikely.

The source now caches per-page fade/ownership offsets, retains page references in candidates, and avoids redundant distance-fade arithmetic outside the transition. Selection geometry, membership and ranges are unchanged. `cpu_recovery_qa/` verifies authored High/subsampled settings, cloud contracts, grove checks and shader compilation; standard QA generated 130 captures and remains classified FAIL_MACHINE_QA. Android build and physical timing of these CPU edits remain outstanding. No saving is yet assigned to them.

## Next implementation package

1. **Finish the existing CPU preparation change; preserve High and subsampled layout.** Do not expand into a buffer architecture or lower selection frequency blindly. Aircraft translation, head turns and fading still require timely updates.
2. **Make selection refresh respond to roll.** Current frustum calculation uses complete stereo view matrices, but its movement-triggered invalidation compares position and forward direction only. Pure roll can rotate the view boundary without changing either, leaving it stale until the timer/another trigger. Track complete orientation with a conservative guard; verify left/right banks and quick reversals. This is a correctness fix, not a promised performance gain.
3. **Screen the already implemented 48/24/12 cloud sampling LOD as one bounded candidate.** Keep full sampling near/inside and for large projected formations, preserve full ray integration, and inspect temporal/stereo cloud edges. Generic view culling is low priority: eight proxy boxes total only 96 triangles, and offscreen portions are already clipped. Sampling LOD has offline evidence but is still disabled in production and has no measured Quest saving. The newest cloud-off result supports a modest experiment, not a multi-millisecond promise or a cloud rewrite. Retain the current shader path if the candidate does not improve timing sufficiently to justify its shading differences.
4. **If the candidate still misses the budget, target foliage GPU work before inventing another coverage shape.** Use one ordinary foliage-visible pilot run with the existing vegetation isolation to establish remaining cost; do not launch another broad benchmark matrix. Preserve the accepted visible range and small leaf scale. Prioritize cheaper unresolved interior layers according to projected size while retaining upper silhouette cards. A useful distant canopy must not be the first thing removed by a nearest-first budget. No new atlas, connected-canopy detection pass or global geometry rewrite is needed to evaluate this direction. Its GPU benefit is not yet quantified.

Banking affects what is visible, but roll angle alone does not prove that the opposite side is hidden. Both-eye frusta already account for bank when refreshed. A later conservative cockpit/airframe occlusion test could reject blocks wholly hidden from both eyes, allowing for pilot lean and prediction; only measured rejected work can justify it. Do not blanket-disable the high side, drop whole rear quadrants, or create a hard hole beneath the aircraft. Prefer actual visible projected size for detail and an aircraft-heading bias for streaming/budget ties. A cardioid is at most a residency/priority heuristic, not the immediate GPU optimization. Unity's built-in baked occlusion is not a turnkey moving-cockpit solution: dynamic objects cannot act as its occluders. [Unity occlusion guidance](https://docs.unity3d.com/6000.0/Documentation/Manual/OcclusionCulling.html).

Use existing ordinary pilot views for level flight, left/right banks, head turns and recovery; compare timing with consistent pose and recorded clocks, keeping captures outside timing windows. Batch offline QA and one Android build. Do not ask for a worn FFR comparison. Final stereo and sustained frame/thermal acceptance remain physical-headset gates. The current 17.17 ms mean needs about 3.28 ms less delivered interval to reach 13.89 ms, with substantial slow-frame recovery as well; the proposed small changes are not yet evidence that this gap will close.

## Approved package implementation

The user approved the revised package. The canopy preparation cache is retained. Selection now compares complete camera orientation at approximately the previous 2.56-degree movement threshold, including pure roll; it still uses both-eye bounds and the existing timer/translation triggers. A runtime-callback regression exercises stationary selection reuse, left/right pure roll, reversal, return to level, triangle-cap preservation and sub-threshold jitter. No bank-side cutoff or cardioid is introduced.

The authored cloud material now enables sampling LOD. A `clouds_full_sampling` diagnostic temporarily restores full sampling in the same APK and restores the authored value afterward; its logs record actual LOD state. The diagnostic changes neither foveation nor canopy detail. Offline validation checks material restoration. Sampling budgets are maximums, not guaranteed sample counts: the existing 60 m minimum spacing and early transmittance exit can already terminate a ray sooner.

`banked_sampling_qa/` completed successfully: grove regression and cloud contracts pass, 12 requested Vulkan shader variants compile, and 20 cloud images plus 18 banked pilot images were produced. Inspected cloud images retain volume shapes; the banked pilot images show foliage but retain the existing pale/black Editor cockpit obstruction, limiting visual acceptance. These are offset-eye Editor images, not tracked simulator captures. Standard QA produced 130/130 images with no capture errors, passing hierarchy/environment/single-XR checks and retaining exactly 26 cost failures (86 maximum estimated draws; 1,026,876 maximum estimated triangles). Bank-view grove submission stays below 376,365 triangles with selected centers reaching approximately 1.8 km; this is not a visibility or GPU acceptance claim. Host VrApi parser regression checks pass. `banked_sampling_qa/validation_summary.json` retains the counts. Android build and device results follow below when available.

### Built and installed candidate; bounded device evaluation

Android build passed, with all 1,912 packaged scenery files verified. APK SHA-256 **`2688dc3edc99ccbba652060b85205eb2b81285837e7911ecf114b4b5c9b0f9ce`**, 1,995,899,672 bytes, built 2026-09-10T21:05:20Z. `banked_sampling_build/final_source_check.json` verifies all seven relevant runtime/material/shader/settings inputs against the captured build snapshot. `installed_apk_sha256.txt` confirms the same hash on Quest. The previous `b615df4c…` package is preserved as `high_full_sampling_b615_reference.apk`.

Both new runs completed 90-second warmup and 60-second measurement with tracked-XR readiness, native JSBSim, actual High and subsampled layout. `banked_sampling_device/` logs actual cloud LOD=1; `banked_full_sampling_device/` logs actual LOD=0. No FFR Off or Medium trial was run.

| Metric | Candidate, cloud LOD | Same APK, full cloud sampling diagnostic |
| --- | ---: | ---: |
| Delivered mean / p95 / p99 ms | 17.645 / 27.901 / 31.537 | 19.358 / 27.078 / 29.979 |
| VrApi mean FPS | 56.62 | 51.87 |
| GPU utilization mean | 88.7% | 89.9% |
| Stale frames | 1,212 | 1,467 |
| Audited visible triangles | 475,925 | 773,597 |
| Maximum reported temperature | 37 C | 40 C |

**This is not a valid isolated cloud speedup comparison.** Inspected startup/final images differ materially: the candidate looks across the runway with little cockpit, while the full-sampling run includes the aircraft wing and substantially more scenery. GPU clocks also differ (599/640 MHz versus 640 MHz). Both views are resting-headset diagnostics, not a correctly seated pilot witness. Consequently, neither the apparent 1.71 ms difference nor the difference from the earlier 17.17 ms run establishes an overall optimization gain. Both still fail 72 Hz/frame/allocation acceptance. No crash, ANR, OOM, tear or thermal warning was recorded; direct GPU milliseconds remain unavailable.

The CPU phase evidence is more specific: candidate gather/fill/buffer-upload/ownership-upload means are 2.781/1.455/0.242/0.432 ms per selection, at 14.99 selections/second. Earlier fill was 4.980 ms at 15.15 selections/second. Accounting for selected workload inferred from uploaded record bytes, average selection size is 44,414 versus 50,253 cells, and filling costs about **32.76 versus 99.10 ns per selected cell** (approximately 67% lower observed normalized cost). This is evidence of reduced CPU preparation cost, with differing views/clocks still noted; it is not a 67% frame/GPU improvement. `cpu_phase_workload_comparison.json` retains the calculation.

The candidate remains installed for further development; High and sampling LOD are its authored settings. The full-sampling diagnostic restores the original material, the host removes its request, and the app was explicitly force-stopped after verification so the next ordinary launch loads authored production settings. System foveation override is empty. **No additional unattended runs are justified with these inconsistent non-pilot views.** Further projected-size foliage changes remain contingent on a representative foliage-visible measurement, not these unmatched results. Tracked simulator, seated stereo/temporal appearance and sustained physical frame/thermal acceptance remain open. No final performance acceptance or measured cloud saving is claimed.
