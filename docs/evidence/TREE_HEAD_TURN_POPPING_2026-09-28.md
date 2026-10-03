# Tree head-turn popping correction — 2026-09-28

## Confirmed cause

`MapBranchGroveRenderer.Select` rebuilt nearest-first tier allocations from the
current eye frustums. At dense mountain stations the view exceeded the existing
393,216-triangle cap. A head turn therefore evicted already visible cells and
changed their 48 m budget-edge opacity ramp, even at a stationary aircraft
position. The same per-cell opacity drives the ownership texture that suppresses
individual-tree foliage beneath replacement groves, so changing the allocation
also changed the appearance of the underlying crowns.

The original renderer was exercised against its actual authored pages at all nine
observation starts, with nine sequential yaw samples per station. Among cells
inside both consecutive views, the baseline recorded 13,212 removals and 35,299
opacity changes (repeated observations, not unique map objects). Fifteen views
were affected, at the two dense mountain stations. The worst single transition
removed 3,965 still-visible cells and another changed 4,843 cell opacities.

Individual trees already selected their persistent LODs independently of head
orientation. Their draw culling, however, had only a 0.25 m bounds margin and no
angular margin for late XR pose prediction. That margin becomes very small at
mountain viewing distances. This was a secondary edge-clipping risk; the measured
mass removals above came from grove allocation.

## Narrow fix

- Retain previously selected grove cells still eligible in the expanded stereo
  view before admitting new ones into spare budget. Preserve distant-tier
  reservation when admitting cells. Distance-tier changes remain bounded.
- Remove opacity changes driven by a view-dependent budget cutoff. Outer-distance
  and newly loaded-page fades remain.
- Add a modest angular culling guard to individual trees, and slightly enlarge
  the existing grove guard. Both eyes retain their union; offscreen residency is
  still distinct from rendering.
- Retain the 393,216 grove triangle cap, 49-page residency bound, three grove
  draws, existing tree budgets, textures, meshes and 1.8 km grove outer distance.
  No new material or rendering pass, no camera writes and no physics changes.
- Selection retains prior admissions, so a crowded view can defer new detail
  until capacity becomes available. It does not guarantee every eligible cell
  can be rendered at once. Translation-related LOD/page transitions remain and
  are distinct from the reproduced stationary head-turn defect.

## Evidence

Artifacts: sibling `T2-QuestFlight-product-artifacts/tree_head_turn_20260928`.

- Fixed replay of the 81-view selection sequence: **zero removals and zero
  opacity changes among already visible overlapping cells**, across 2,389,874
  cell observations. Original maximum triangle cap preserved.
- Additional 216 moving/banking selections stay within triangle/instance caps;
  looking at the sky releases grove submissions at all nine stations.
- Existing focused distance-fade, far-tier allocation, distant coverage,
  bank/reversal/jitter and mesh-bounds/both-eye culling checks pass.
- 18 sequential pilot-position stereo captures generated. Mountain canopy
  structure and left/right views inspected. These are offline cameras, not
  tracked-headset or Quest timing evidence.
- Standard Visual QA: 130 captures completed; inherited material-count failure
  remains **41 versus target 38**, unchanged from the previous candidate.
- Android production build succeeded; all 1,912 scenery payload files verified.
  The APK contains ARM64 native JSBSim and OpenXR libraries.
- Installed on Quest `2G0YC5ZG8907TD`; on-device APK SHA-256 matches the candidate:
  `b8d91aa05f72496da6126d9546768d451159eb3e1b21c8d3bae55539bc98aa5d`.
  Package size: 1,802,352,106 bytes. Build manifest and `device_install.json` are
  retained in the artifact directory. No headset launch or worn test requested.
- After Android packaging, only the offline capture fixture, a Windows-only
  simulator observation-start fixture and this evidence record changed. Neither
  fixture changes the Android product or writes a tracked camera pose.
- Isolated Meta XR Simulator player built successfully; inspected its left/right
  composite. All 14 explicit hierarchy, stereo, tracking and environment checks
  passed, but the session **failed its overall machine gate** because mountain
  aerial clipmap bundles were Android-targeted and could not load in the Windows
  development player. The resulting forward view is largely sky/cockpit with
  fallback ground imagery; it is not sufficient visual acceptance of mountain
  canopy continuity. The 18 offline pilot captures and focused selection replay
  above remain the foliage evidence. No simulator-performance claim is made.
- The simulator orchestrator also reports `BLOCKED_MISSING_RECORDINGS`: this was
  a live smoke session, not a recorded head-motion replay. Its temporary project
  was removed, and shipping Android configuration and the system XR runtime were
  preserved. Reports and stereo captures are under `meta_xr_simulator/qa_run`.

No worn headset test or sustained Quest frame/thermal acceptance is claimed.

## Reproduction

Set `QFL_TREE_TURN_ROOT` to an artifact directory and invoke Unity batch method
`QuestFlightLab.Editor.TreeHeadTurnReview.RunAcceptance`. It runs the continuity
and cap checks plus the focused stereo review. `RunBaseline` records rather than
asserts overlap continuity, for comparison with the original implementation.
