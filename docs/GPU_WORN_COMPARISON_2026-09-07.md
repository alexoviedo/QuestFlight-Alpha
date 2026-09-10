# Physical Quest comparisons

## September 8 — vegetation draw culling: A1 / B1 / B2 / A2

**Completed worn comparison: no new visual defects reported; recovered GPU headroom is not established.** The user confirmed readiness, wore Quest `2G0YC5ZG8907TD`, and reported **“No new defects noticed”** after checking head turns, peripheral tree disappearance/popping and differences between eyes in the candidate. This is positive visual feedback for the observed session, not full-map or performance acceptance.

Each run used the existing authored native-JSBSim demo route, production settings, 90 seconds warmup and 60 seconds measurement, without screen recording or a running Unity Editor. All four runs passed readiness and mountain invariants, with 60/60/61/60 exact-window app-PID VrApi samples spanning approximately 59/59/60/59 seconds at 72 Hz. Direct GPU duration is unavailable. All four fail the existing frame/GC/transparency gates.

| Measurement | Baseline A1 | Candidate B1 | Candidate B2 | Baseline A2 |
|---|---:|---:|---:|---:|
| Mean delivered interval (ms) | 16.849 | 16.880 | 16.752 | 17.043 |
| p95 delivered interval (ms) | 24.097 | 23.591 | 23.711 | 23.901 |
| VrApi App mean (ms) | 15.224 | 14.995 | 14.966 | 15.038 |
| VrApi CPU&GPU mean (ms) | 30.700 | 30.576 | 30.563 | 31.333 |
| VrApi CPU&GPU p95 (ms) | 39.934 | 38.523 | 39.470 | 40.709 |
| Stale frames | 966 | 970 | 931 | 1,024 |
| Maximum reported temperature (°C) | 36 | 41 | 45 | 46 |

The candidate's two-run mean delivered interval is approximately 0.130 ms below the two-run baseline mean. However, B1 is slightly slower than A1, baseline variation is approximately 0.193 ms, temperature rose across the sequence, and head views were not identical. This is not a demonstrated repeatable material speedup or isolated GPU saving. All CPU&GPU samples exceed the existing 13.889 ms limit. No crash, ANR, OOM, Unity exception, tear or thermal warning was recorded; these short runs do not establish sustained thermal acceptance.

Inspected the both-eye final screenshots from all four runs. Cockpit, sky and reservoir dominate; the head view shifts between runs, with A2 showing more of the left window/water. They establish neither a registered visual comparison nor dense-canopy route coverage. User feedback supplements these images but does not replace missing simulator evidence. The runtime draw audit is a partial material/submesh estimate, not the actual compacted tree-batch count; do not infer on-device draw savings from it. The earlier eight pixel-identical Editor comparisons remain separate evidence of reduced submitted geometry.

Baseline APK SHA-256: `f6c019e08b85f95a827c52e40335e95fbe7f31b7da42c02515d26b2d5e66ad26`. Candidate SHA-256: `d6043097a04a7379139ddcfee6cdf9e99940252baa1f54539383a2ab759ca81f`. Verified the installed baseline before A1, the installed candidate after B1, and the restored baseline during A2. The accepted baseline remains installed; the app is stopped and its PID is absent, capture requests are removed by the runner, stay-awake setting `2` is restored, and foveation was not changed. No product source was changed during this comparison.

The culling source/APK remains an experimental implementation with useful offline submission savings and positive worn visual feedback. It is not promoted to accepted performance headroom. The existing absolute gate failures make a full acceptance soak premature; no third candidate acceptance run, longer thermal soak or full-map route acceptance is claimed. This bounded four-run comparison closes this diagnostic session, not the overall upgrade. Do not request another identical worn comparison without a specific new diagnostic purpose.

[Machine-readable comparison](../../T2-QuestFlight-product-artifacts/vegetation_diagnosis_20260908/worn_comparison/comparison.json), [human feedback](../../T2-QuestFlight-product-artifacts/vegetation_diagnosis_20260908/worn_comparison/human_feedback.json), [A1](../../T2-QuestFlight-product-artifacts/vegetation_diagnosis_20260908/worn_comparison/A1/quest_temporal_visual_gate_summary.md), [B1](../../T2-QuestFlight-product-artifacts/vegetation_diagnosis_20260908/worn_comparison/B1/quest_temporal_visual_gate_summary.md), [B2](../../T2-QuestFlight-product-artifacts/vegetation_diagnosis_20260908/worn_comparison/B2/quest_temporal_visual_gate_summary.md), [A2](../../T2-QuestFlight-product-artifacts/vegetation_diagnosis_20260908/worn_comparison/A2/quest_temporal_visual_gate_summary.md). Original raw reports retain `PENDING_HUMAN_HEADSET_WITNESS`; the later user feedback is recorded separately without rewriting them.

## September 7 — shader baseline / candidate / baseline

> Execution authority: [the revised unified remaster plan](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md) supersedes any next-step ordering or intermediate headset requests below. The revised sequence is now explicitly approved. This document remains historical evidence.

After the user confirmed the headset was awake and ready, completed an A/B/A comparison on Quest `2G0YC5ZG8907TD`. Each run used the authored native-JSBSim demo route, production settings, 90 seconds warmup and 60 seconds measurement, without screen recording. Tracking/readiness and mountain invariants passed in all three runs. There were 60 exact-window product-PID VrApi samples per run, spanning approximately 59 seconds at 72 Hz. No Unity Editor ran during capture.

**Result: a modest preliminary improvement, not accepted GPU headroom.** The candidate was faster than both baseline runs, but only one candidate run exists and head views were not identical. Direct GPU duration remains unavailable. All three runs fail the existing frame/GC gate.

| Measurement | Baseline A1 | Candidate B1 | Baseline A2 |
|---|---:|---:|---:|
| Mean delivered interval | 17.078 ms | 16.657 ms | 17.363 ms |
| Approximate fps from mean interval | 58.6 | 60.0 | 57.6 |
| p95 delivered interval | 24.242 ms | 22.768 ms | 23.671 ms |
| VrApi App mean | 15.042 ms | 14.506 ms | 15.191 ms |
| VrApi CPU&GPU mean | 30.724 ms | 30.312 ms | 31.523 ms |
| VrApi CPU&GPU p95 | 38.290 ms | 36.802 ms | 39.770 ms |
| GPU utilization | 92.3% | 92.0% | 93.4% |
| Reported stale frames | 988 | 907 | 1,029 |
| Reported tears | 0 | 0 | 0 |
| Maximum reported temperature | 37°C | 41°C | 44°C |

All CPU&GPU samples exceeded 13.889 ms. No crash, ANR, OOM, Unity exception or thermal warning occurred; thermalservice status and PLS remained zero. These short runs do not establish sustained thermal acceptance. Managed-object creations remain substantial (157,795 / 159,293 / 155,195).

Baseline SHA-256: `e04a5ef0fa9f62ed54c976d27cc512c312686bb272d93a2fb26df956aefb68c5`. Candidate SHA-256: `b69fa41aa1dffeafb57ad90d35942640a1d8d7a8831e69669544002e7cc84fd0`. Verified the baseline installation before A1, the candidate installation after B1, and the restored baseline after A2. The candidate is the previously validated Android build, with no additional product changes during this test.

## Interpretation and limitations

The candidate's mean delivered interval is 0.421–0.705 ms below the two baselines, with fewer reported stale frames and lower p95 timings. This supports further investigation of the bounded shader candidate. It does not establish an isolated GPU saving, statistical repeatability, or sufficient headroom for additional visual density. VrApi CPU&GPU must not be described as GPU milliseconds.

Inspected both-eye warmup images for A1/B1 and final images for all three runs. Startup views include the cockpit and runway; final views include the cockpit, reservoir and sky. A1 ends looking farther toward the left window, B1 is intermediate, and A2 is more forward. These visible differences, changing frame contents and rising device temperature limit precise attribution. End-of-run partial triangle estimates also differ (567,393 / 474,193 / 479,441); these are incomplete renderer audit snapshots, not whole-window hardware workloads. No scene-cost isolation or representative multi-route GPU ledger was completed here.

The user wore the headset for the requested comparison and explicitly reported **“No new defects noticed”** in the candidate compared with the first run, when asked about clouds, missing scenery, traffic popping and cockpit clarity. This is a positive initial worn visual check for the observed route. Original machine metadata remains `PENDING_HUMAN_HEADSET_WITNESS`; this document records the later feedback without rewriting raw evidence. Seat calibration was not completed by this capture. Full temporal/seat acceptance and required simulator replay recordings remain open. This A/B/A sequence is not three acceptance runs on one immutable candidate APK.

The original B1 summary's automatic baseline-comparison subsection incorrectly read the host summary JSON as runtime evidence and displayed zero baseline values. Preserved that original report and reparsed B1 against A1's `pulled_device_evidence` directory. The corrected comparison reports the actual 17.078 ms baseline and −0.421 ms delivered-interval delta. The table above uses each original run's valid runtime/VrApi sections; the erroneous automatic comparison is not used. No capture timing was changed by reparsing.

## Device state and next step

The accepted baseline is restored and verified on the headset; the app is stopped, capture requests are removed, stay-awake setting `2` is restored and foveation is unchanged. The user was told the timed tests are complete and the headset can be removed. The candidate remains a reversible source/build experiment with positive initial visual feedback, pending more controlled performance attribution and full acceptance.

**Approved sequencing update:** resume a bounded Valmont canopy/building benchmark now, using existing geometry, population, draw and material ceilings. Retain the shader candidate and its positive initial visual feedback; do not spend another headset session solely to establish its small performance delta. Preserve source building outlines and improve coverage of already authored crowns before broader visual expansion. Measure the combined milestone against the baseline, including displaced canopy coverage. Timebox targeted profiling when it can resolve a specific budget decision. Existing frame/GC, draw/material, simulator replay and sustained thermal gates remain open; unchanged resource caps do not establish unchanged GPU time or shipping acceptance.

[Machine-readable comparison](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/worn_comparison.json) · [A1](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/worn_baseline_1/quest_temporal_visual_gate_summary.md) · [B1](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/worn_candidate_1/quest_temporal_visual_gate_summary.md) · [corrected B1 baseline parsing](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/worn_candidate_1_reparsed/quest_temporal_visual_gate_summary.md) · [A2](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/worn_baseline_2/quest_temporal_visual_gate_summary.md).
