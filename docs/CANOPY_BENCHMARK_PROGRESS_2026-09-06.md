# Quest measurements and first canopy experiment

**September 7 follow-up:** the later [UV investigation](CANOPY_UV_REPAIR_2026-09-07.md) found that the shader collapsed far-impostor U coordinates to one atlas column. The selection/image counts below remain valid historical observations, but their weak visual response cannot establish the value of correctly rendered additional crowns. Reevaluate the prototype after the UV repair.

The installed recovery APK is still `1ac40cbe785ecc5c349fb20d7727db71605a80f79cad616e543a2d90ef026792`. No new shipping visual setting, asset or selection policy was installed in this step. The new source changes are Editor-only investigation and review tooling. The canopy experiment is opt-in, unsaved, geographically bounded and excluded from Android compilation.

## Physical measurements

The previous tracking blockage cleared sufficiently to complete two 90-second warmup / 60-second measurement runs on the physical Quest. Both used the installed APK, all scenery enabled, no recording and no human witness.

| Measurement | Production settings | Temporary medium foveation |
|---|---:|---:|
| Mean delivered interval | 18.108 ms | 14.624 ms |
| p95 delivered interval | 25.721 ms | 19.004 ms |
| VrApi App mean | 16.596 ms | 12.015 ms |
| VrApi CPU&GPU mean | 32.508 ms | 25.989 ms |
| GPU utilization mean | 94.4% | 88.6% |
| Reported stale frames | 1,228 | 392 |
| Reported tears | 0 | 0 |
| Maximum reported temperature | 42°C | 43°C |

Both **fail** the existing frame/GC gate. Both have 60 app-PID VrApi samples, native JSBSim active, passing mountain invariants, no crash/ANR/OOM/Unity exception and no reported thermal warning. This does not establish thermal or worn visual acceptance. The temporary override is confirmed by product-PID `Fov=2` telemetry. The inspected two-eye screenshots show slightly different cockpit framing and both are sky-heavy; the table is diagnostic evidence, not a controlled pose-matched A/B or proof of the incremental traffic optimization's savings. Direct GPU milliseconds remain unavailable.

The foveation override returned to its original empty value, power automation and the previous stay-awake setting were restored, the request files were removed and the app was stopped. Medium foveation is **not** enabled in shipping settings. It needs a matched repeat and worn instrument/peripheral-image inspection before consideration.

[Production result](../../T2-QuestFlight-current-evidence/benchmark_progress_20260906/installed_production/quest_temporal_visual_gate_summary.md) · [medium-foveation result](../../T2-QuestFlight-current-evidence/benchmark_progress_20260906/medium_foveation/quest_temporal_visual_gate_summary.md).

## Canopy findings and bounded experiment

Reinspected the actual City2020 Valmont photograph, source placement database, current settled views, tree selection code, the eight-view impostor atlas and its shader/import settings. The reference crop is local X −884..−135 m, Z −914..−494 m. The audit counts existing authored trees, not inferred new placements.

The patch contains 754 authored crowns; 392 enter the active set in these three views. The existing selector allows only the page's first far-sample population to use far proxies. Additional near-page candidates are ineligible for that tier, so middle-tier rejection can remove them entirely even though they are resident. This is a proven selection limitation; it does not by itself explain every visible canopy discrepancy.

The Editor prototype allows those extra candidates inside this patch to compete for the existing global far slots. It leaves positions, source imagery, mesh/material bindings, residency and global selection limits unchanged. It does not increase the far population or silently extend the shipping policy.

| Camera local Y (not verified AGL) | Selected patch crowns, baseline | Prototype | Global far population, both |
|---|---:|---:|---:|
| 100 m | 210 | 337 | 3,792 |
| 300 m | 203 | 384 | 3,792 |
| 500 m | 157 | 392 | 3,792 |

Global near/middle/shrub populations also match exactly at each pose. The review generator rejects mismatched tier populations. Equal geometry and draw populations do not prove equal GPU cost; changed screen coverage can change fragment work. Extra local selections displace other far selections, whose coverage and temporal continuity still require investigation.

**Result: do not promote this prototype yet.** Inspected before/after images show little visual gain despite the larger selection count. At 1280×720, the left-eye images differ by more than two RGB code values in 0/410/608 pixels at the three heights. These counts include both added and displaced coverage; they are not a visual-quality score. Only three newly selected tree bases project inside the 100 m view, versus 92/231 at 300/500 m. Selection alone is insufficient for the desired canopy/roof integration. Next investigate actual far-proxy projected shape, scale, opacity/mips and terrain/roof occlusion before changing geometry or adding more selected trees. The atlas already preserves mip coverage, so mip loss is not assumed to be the cause.

[Interactive before/after review](../../T2-QuestFlight-current-evidence/benchmark_progress_20260906/canopy_review.html) · [baseline audit](../../T2-QuestFlight-current-evidence/benchmark_progress_20260906/canopy_baseline/canopy_summary.csv) · [prototype audit](../../T2-QuestFlight-current-evidence/benchmark_progress_20260906/canopy_candidate/canopy_summary.csv). Captures, photographs and generated review files remain outside Git. Original photographic/source rights records remain in the visual-fidelity audit and the existing canopy source manifest; no new binary asset was created.

## Validation and remaining work

The Editor audit compiled and completed successfully. Each baseline/prototype set contains six passing settled Editor captures; both candidate eye views and comparison imagery were inspected. These are not Meta XR Simulator replay captures. The fresh standard QA remains 128/130 with the existing 79/82 draw failures and the 39-material warning. Expanded ledgers identify numerous separate exterior-aircraft Body_MAT meshes in the draw failures, and identify `pasv1_18_cockpit_side_window` as the 39-material view. Animated controls, propeller presentation and visibility groups must be accounted for before any mesh consolidation; no such consolidation was performed.

The installed product build/payload validation from the recovery remains applicable to the unchanged shipping code and assets. No new Android build is claimed for these Editor-only experiments. The plan still needs actual frame/thermal acceptance, draw/material recovery, simulator replay evidence and a canopy/building candidate with a substantial inspected visual gain.
