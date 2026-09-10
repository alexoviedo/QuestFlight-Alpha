# Visual-upgrade performance recovery

Subsequent execution is recorded in [Quest measurements and first canopy experiment](CANOPY_BENCHMARK_PROGRESS_2026-09-06.md). The later physical runs completed after the tracking blockage described here; both still failed the frame/GC gate. The user has visually accepted the recovery changes. The remaining findings below are retained as the recovery-stage evidence, not a claim that those issues are now resolved.

This implements the approved performance/traffic prerequisite of the visual upgrade. It does not establish 72 Hz acceptance or complete the canopy/building art work. Evidence is retained outside Git under [performance_recovery_20260906](../../T2-QuestFlight-current-evidence/performance_recovery_20260906/).

## Implemented changes and exact causes

- Traffic previously prepared vehicles on roads outside the view, including terrain queries and instance submissions. Conservative corridor bounds now reject a corridor only when it is outside both eye frusta. Scheduling remains deterministic; visible cars retain their identities. Bounds include the full authored height range and a lateral guard. A missing valid bound fails open.
- Building page selection repeatedly allocated candidate objects and ran ordered LINQ searches. Reused value-type candidates and direct neighbour selection preserve all 2,904 tested ordered selections. Editor selection time fell from 0.288 to 0.095 ms. Allocation-byte instrumentation was unavailable; zero bytes is not claimed.
- Diagnostic isolation used renderer enable flags that streaming could overwrite and missed instanced systems. It now suppresses rendering independently of streaming and explicitly disables traffic/vegetation instancers. Cloud-only isolation retains the photographic sky and atmosphere. Building isolation retains streaming CPU work; reduced texture quality does not prove aerial-array cost.
- Bridge profiles sharing a road centerline disagreed in height. Nearest-profile selection could select a buried surface, and clamped projections extended a flat road surface beyond profile endpoints. Sampling now rejects those endpoint extensions and selects the upper applicable road or terrain surface.
- Removing the false extensions exposed two actual authored approach seams: 0.514 m and 0.941 m. The authoring pipeline reconciles the continuing approaches using a bounded upward grade envelope, retaining terrain-matched endpoints and bridge crowns. Only two of 29 profiles changed. The regenerated geometry has 6,496 triangles against the unchanged 6,500 limit, with two road draws and maximum grade 5.8572%.

The road authority's new canonical LF SHA-256 is `e06ee50d3de7f404489ceb4c2e48dc988e68a549ec8bc1a44726eee7d8da0d84`. The checksum change reflects deliberate authored content changes; validation and the previously accepted LF repair remain enforced. Existing mesh GUIDs are retained. Source and license provenance remains the authority's recorded CDOT data (CC BY-SA 4.0) and OpenStreetMap data (ODbL 1.0); the modified near-road mesh is a local derivation through `build_grade_separated_road_authority.py` and `GradeSeparatedRoadV1Authoring.cs`. No new downloaded asset was introduced.

## Offline validation

[Focused Unity results](../../T2-QuestFlight-current-evidence/performance_recovery_20260906/final_regression_tests.xml): 48/48 passed across traffic continuity, bridge geometry, capture requests and building selection. Two Python approach-reconciliation tests pass, including endpoint preservation and idempotence.

The head-turn regression retained 21,422 visible vehicle positions exactly over 36 poses while avoiding 60,915 offscreen submissions. It is an Editor projection test; it does not substitute for physical stereo inspection.

| Editor traffic preparation pose | Without rejection | With rejection |
|---|---:|---:|
| Airport, 300 m | 14.645 ms | 2.346 ms |
| Reservoir, 300 m | 8.559 ms | 1.097 ms |
| Southwest, 500 m | 19.353 ms | 5.589 ms |

These are CPU workload measurements, not Quest frame-time savings. [Final QA artifacts](../../T2-QuestFlight-current-evidence/performance_recovery_20260906/final_qa/) contain the paired measurements, 130 standard captures, six settled Editor benchmark images, 15 full-map traffic captures, bridge stills and temporal sequences.

Standard QA remains **128/130**: external control sweep estimates 79 draws and approach 82 against 77. It also reports 39 scene materials against a planning target of 38. These limits were not raised. The draw ledger omits instanced traffic and is not a complete device draw count. Settled Editor pairs and full-map traffic machine checks pass. Inspected settled pairs, approach and bridge imagery show the corrected surface relationship, but independent traffic corridors still produce overlapping vehicles at some junctions and visible endpoint dither. Traffic is not yet visually accepted. Editor aerial fallback is not Android imagery parity; paired Editor images are not Meta XR Simulator replay captures.

## Physical diagnostics before the new traffic culling build

All rows used diagnostic APK `55696c92a1d9ea21a8e535598284081a261b9c80954094365ac34fce711370f8`, an unworn resting headset, 90-second warmup and 60-second measurement. They are diagnostic observations, not a matched worn baseline or thermal acceptance.

| Scope | Mean delivered interval | VrApi App mean | VrApi CPU&GPU mean | Reported stale frames |
|---|---:|---:|---:|---:|
| Production | 19.832 ms | 16.405 ms | 37.889 ms | 1,456 |
| Cloud volumes off | 19.265 ms | 12.309 ms | 33.369 ms | 1,412 |
| Traffic off | 16.703 ms | 14.438 ms | 24.234 ms | 1,010 |

Production telemetry was recovered from the complete device log after Unity's Android tooling interrupted host ADB capture. The other runs completed normally. All fail the frame/GC gate; no headroom is allocated from them. The sky-dominated resting pose limits scene-cost interpretation. Cloud removal leaves the photographic sky's cloud appearance. VrApi CPU&GPU is reported as emitted and is not treated as direct GPU duration.

## Remaining acceptance

The final Android build and all 616 scenery payload checks passed. Candidate APK SHA-256 **`1ac40cbe785ecc5c349fb20d7727db71605a80f79cad616e543a2d90ef026792`**, **1,514,040,935 bytes**, is installed on Quest `2G0YC5ZG8907TD`; the installed `base.apk` hash matches. [Build manifest](../../T2-QuestFlight-current-evidence/performance_recovery_20260906/final_build/product_apk_manifest.json) and [installed hash](../../T2-QuestFlight-current-evidence/performance_recovery_20260906/final_build/installed_apk_sha256.txt) retain the exact identity. The source archive predates the subsequent host-only readiness-wait fix and documentation update; the product source is unchanged since the build.

The [attempted final physical run](../../T2-QuestFlight-current-evidence/performance_recovery_20260906/final_production/) never reached readiness. Its inspected stereo screenshot shows the system dialog **“Finding position in room”**; Android activity evidence identifies Guardian retaining focus. No candidate frame-time result exists. The run was stopped after readiness timed out, diagnostic request removal and power-automation restoration completed, and the previous stay-awake setting `2` was restored. The host now skips subsequent warmup/measurement waits when readiness fails. A brief user tracking/boundary recovery is needed before retrying the same installed APK.

Product foveation settings remain unchanged. The new optional host foveation override was not exercised because readiness failed; it is diagnostic tooling, not a shipping optimization or measured improvement. Worn traffic review, Meta XR Simulator stereo replay acceptance, draw/material budget recovery and the prescribed three-run Quest frame/thermal gate remain open. The next art work remains a bounded canopy/building/ground comparison at unchanged resource caps, after measured performance headroom exists.
