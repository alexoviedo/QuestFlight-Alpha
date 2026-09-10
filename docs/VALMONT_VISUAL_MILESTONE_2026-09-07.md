# Valmont canopy and building milestone

> Execution authority: [the revised unified remaster plan](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md) supersedes any next-step ordering or intermediate headset requests below. The revised sequence is now explicitly approved. This document remains historical evidence.

## September 7 quick worn headset check

Installed and verified candidate `f6c019e08b85f95a827c52e40335e95fbe7f31b7da42c02515d26b2d5e66ad26` on Quest `2G0YC5ZG8907TD`. After the user confirmed the headset was on, completed one production native-JSBSim demo run: 90 seconds warmup and 60.01 seconds measurement, without screen recording or feature isolation. The user explicitly reported **“No new defects noticed”** when asked about tree/traffic popping, missing buildings, cockpit clarity and other visual defects.

Mean delivered interval **16.486 ms (~60.7 fps)**; p95 **22.643 ms**. All 60 exact-window app-PID VrApi samples were collected over 59.004 seconds at 72 Hz. VrApi App mean was 14.343 ms; CPU&GPU mean/p95 were 30.120/37.413 ms, with 848 stale frames and zero tears. Direct GPU duration remains unavailable. Frame/GC and transparency gates still fail; 162,411 managed-object creations were recorded. This single unpaired run establishes no isolated saving or performance neutrality.

Readiness/native authority and mountain invariants passed. No crash, ANR, OOM, Unity exception or thermal warning occurred; maximum reported temperature was 35°C. This short run does not establish sustained thermal acceptance. Inspected both-eye warmup and final screenshots: cockpit/sky dominate, with runway/scenery at the edges. These captures do not establish close visual coverage of the Valmont patch. Seat calibration and simulator gates remain open. Raw machine metadata retains its original pending-human status; this paragraph records the later user feedback separately.

The candidate remains installed; the app was explicitly stopped and its PID is absent. Capture requests were removed, stay-awake setting `2` restored, and foveation unchanged. [Physical test evidence](../../T2-QuestFlight-current-evidence/valmont_visual_20260907/headset_quick_test/quest_temporal_visual_gate_summary.md). Installation statements below describe the preceding offline milestone.

Implements the approved decision to resume a bounded visual comparison while retaining the GPU shader candidate. This is a local candidate, not a completed visual upgrade or Quest performance acceptance.

## Authored changes

The existing environment prefab enables additional far-tier eligibility for existing crowns within the inspected City2020 comparison crop: local X −884 to −135 m, Z −914 to −494 m. Tree placements, near/middle/far population caps, seven-draw ceiling, materials, transition timing and residency rules remain unchanged. Extra local eligibility can displace other far crowns; the comparison exports every selected ID to measure that tradeoff.

The building compiler preserves the recorded perimeter for large flat buildings in that same crop. Seven source records qualify; six actually change. Heights, facade parameters and roof classifications are untouched. Roof classification in this database includes inference and is not treated as measured roof detail.

| Source record | Previous vertices | Candidate vertices | Added triangles |
|---|---:|---:|---:|
| 13765 | 6 | 11 | 15 |
| 18159 | 6 | 6 | 0 |
| 39433 | 6 | 12 | 18 |
| 2275 | 6 | 12 | 18 |
| 22103 | 6 | 18 | 36 |
| 2276 | 6 | 13 | 21 |
| 39459 | 18 | 41 | 69 |

The prominent complex at record 39459 now retains its 41-vertex recorded outline instead of the simplified 18-vertex shell. Its recorded 2.7 m height is unchanged; this work does not establish that height's accuracy.

Only building page (11,11) changes: 4,629 → 4,806 triangles. The conservative heaviest-24-page ceiling remains 235,000. All pitched-roof allocations are unchanged. These geometry counts do not establish GPU-time neutrality.

Mesh regeneration now updates existing assets instead of deleting their directory. All 576 existing mesh `.meta` hashes remained unchanged. Serialized page metadata is refreshed on the existing authored hierarchy after regeneration.

## Reference and provenance

Inspected the [existing City2020 aerial reference](../../T2-QuestFlight-current-evidence/visual_fidelity_audit_20260906/references/canopy_source2020.jpg). Geometry derives from the existing [building database provenance](../QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_FullMapBuildingDatabaseV1_PROVENANCE.json) and authored canopy placement data; no new external asset, guessed building height, raw orthophoto import or texture allocation was introduced. Generated meshes, bundles, captures and APKs remain local build evidence.

## Validation

Building geometry/bundle regeneration passed. All **38 focused tree/building tests pass**, including the new spatial/type/size boundary test. Three initial failures were stale expected compiler identifiers and exact totals; the revised totals correspond to the audited +177 triangles and +413 duplicated mesh vertices. Existing roof-seal, corner metadata and conservative budget assertions remain in place.

Inspected all six matched benchmark eye images at 100/300/500 m local camera heights, the standard QA contact sheet, the earlier GPU-candidate render and the aerial source. The improvement is modest: fuller local canopy and more faithful large-building perimeters. Thin near trees, simplified heights/roofs and ground-image appearance remain visible limitations. [Interactive before/after, both eyes and source](../../T2-QuestFlight-current-evidence/valmont_visual_20260907/comparison.html).

Selected patch crowns increase **210/203/157 → 337/384/392**. Global near/middle/far/shrub counts are identical at each matched pose. The [ID displacement audit](../../T2-QuestFlight-current-evidence/valmont_visual_20260907/canopy_displacement.json) finds 128/181/235 displaced far-tier records; none projects inside the first two camera frames. One projects inside the 500 m frame, near the upper-right edge (normalized 0.965/0.984, approximately 1,998 m depth). This tests three settled poses, not continuous motion or arbitrary head turns.

Standard Visual QA is **128/130**. The existing external-control/approach draw failures (79/82 versus 77) and scene material overage (39 versus 38) remain. No new threshold failure appears. Editor images are not Meta XR Simulator replay or physical Quest evidence.

The production Android build and **all 616 scenery payload checks pass**. Preserved [candidate APK](../../T2-QuestFlight-current-evidence/valmont_visual_20260907/build/QuestFlight-valmont-candidate.apk), 1,514,039,703 bytes, SHA-256 `f6c019e08b85f95a827c52e40335e95fbe7f31b7da42c02515d26b2d5e66ad26`. The [build manifest and source identity](../../T2-QuestFlight-current-evidence/valmont_visual_20260907/build/product_apk_manifest.json) record the complete candidate. It is not installed on the headset.

The isolated Windows Meta XR Simulator development build succeeded and the live attempt produced inspected, nonblank stereo images with 60 mm eye separation. **The simulator gate failed.** The development player copied Android terrain/building bundles; runtime logs explicitly reject their build target. The capture shows the startup menu, not the Valmont benchmark. Runtime-identification and mountain-identity checks also fail. The ten required replay recordings remain absent. Therefore this attempt establishes neither scenery validation nor a simulator replay pass. [Session evidence](../../T2-QuestFlight-current-evidence/valmont_visual_20260907/meta_xr_simulator/qa_run/sessions/cockpit_neutral/cockpit_neutral_session_summary.md).

The temporary development project was removed, simulator processes exited, persistent simulator configuration was restored, and the registered OpenXR runtime was preserved. Shipping Android settings and the physical headset were unchanged.

The previously open frame/GC, draw/material, simulator replay and sustained thermal gates remain open. Next repair the development-only simulator bundle preparation and obtain a scenery-view capture; consolidate worn temporal and performance checks around this combined candidate. Do not expand canopy eligibility beyond the bounded crop or count its fixed resource ceilings as measured GPU headroom.
