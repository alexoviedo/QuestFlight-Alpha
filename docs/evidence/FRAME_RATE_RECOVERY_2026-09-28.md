# Frame-rate recovery implementation evidence

Status: candidate implemented, Android release build passed and installed;
**isolated stereo-simulator validation and Quest frame-rate/thermal acceptance pending**.
User authorized the revised September 28 plan. This is a bounded foliage recovery,
not a graphics-pipeline rewrite. Reference is the accepted anti-popping image.

## Changes

- Replaced per-candidate retained-cell hash lookups with page-local generation
  stamps. Stop admission once both retained and new quotas are filled.
- Retained immutable GPU records, upload only changed source ranges, update
  ownership pixels sparsely, and skip unchanged ownership texture transfers.
  A settled stationary view no longer performs timer-only selection work.
- Added an offline-baked, component/family-aware four-patch cluster candidate.
  Two/three-card variants preserve the existing middle-to-far transition.
  Source geometry remains below 900 m and where footprint/slope/projected error,
  selection completeness or viewing elevation disqualifies replacement.
  Clusters replace children, not add another canopy layer. Original trunks,
  upper crowns, coverage, 1.8 km radius and 393,216-triangle ceiling remain.
- Reuse 36 templates, each with 25 directions (including uphill views), across
  the map. Two 2048-square ASTC 6x6 atlases with mips cost approximately 4.75 MiB
  of texture storage. This excludes metadata, buffers and source-page residency.
  Each cluster is two triangles; at most one additional batched draw/material.
- Map audit: 1,296 hash-validated pages, 12,407,662 source cells;
  1,641,419 eligible four-cell groups (6,565,676 cells), 5,841,986 cells retain
  geometry before view-dependent qualification. The external coverage manifest
  records every page and source hash. This is potential eligibility, not a
  simultaneous draw count or a reduction of all resident source records.
- Page hashing/decoding/cluster fitting now runs as one bounded pure-data worker;
  the main thread publishes completed pages only if still wanted. No runtime
  GameObjects, transforms or product hierarchy are created.
- Input traces are buffered, bounded to 256 KiB and flushed once per second or
  at a 16 KiB buffer threshold, pause, disable or quit. This removes per-event
  file opens and metadata queries; it is not a measured main bottleneck.
- Added timed diagnostic profile `grove_source_geometry` for comparing the
  original geometry with clusters using the same executable and CPU correction.
  It restores the prior flag and keeps original ownership/fallback foliage.

## Evidence so far

External artifact root: sibling
`T2-QuestFlight-product-artifacts/frame_recovery_20260928/`.

- Reference APK preserved at `reference/QuestFlight-pre-recovery.apk`;
  original SHA-256 `b8d91aa05f72496da6126d9546768d451159eb3e1b21c8d3bae55539bc98aa5d`.
- `final_validation/cpu_final/fixed.json`: **81/81 source GPU-record hashes
  exactly match the reference**, including order and coverage. Mean desktop
  selection duration 9.985 → 7.812 ms; payload 5.275 → 4.682 MB/selection.
  These are editor observations, not Quest execution time or FPS gains.
- `cluster_middle/fixed.json`: branch triangles average 235,300 → 171,095
  at airport station 0; 393,216 → 348,483 at mountain station 6;
  392,937 → 326,689 at station 7. The cluster adds CPU work and texture samples;
  these reductions do not establish a net frame-rate gain.
- 81-view common-source coverage checks passed. Additional moving/banking
  sequences enforce the existing cap and sky-view rejection. Retained complete
  cluster groups preserve transition coverage across head turns.
- Live representation-switch check passed: restoring source geometry produces
  exact original GPU records and identical ownership bytes; re-enabling restores
  clusters. Page-seam/outer-map and release-of-ownership checks are included.
  A separate empty-sky switch/return check also passed after invalidating cached
  source records when leaving compacted submission.
- Inspected pilot images in both eyes at mountain/airport stations show retained
  nearby foliage and no obvious new canopy panels in the reviewed views.
  These captures are not physical-headset acceptance.
- Connected reference observation did not obtain a stable tracked/focused run;
  device subsequently reported asleep. Excluded from performance comparisons.
- Standard visual QA produced 130/130 captures and passed the authored
  hierarchy/environment and 25-heading mountain sweep checks. It remains
  `FAIL_MACHINE_QA`: inherited scene-material target failure (41 vs 38), several
  estimated draw/triangle budget failures, and external-view contact-shadow
  checks. The reference had the same categories. Maximum estimated triangles
  fell 1,068,460 to 978,018; maximum draws increased 99 to 100 for the cluster
  batch. These are offline estimates, not measured device work. The runtime
  material counter now also includes the cluster material when actually drawn.
- First Android release build passed, including Vulkan cluster-shader compilation
  and all 1,912 scenery-payload identity checks (`android_build/`). A final build
  follows the empty-view diagnostic safeguard; the first APK is superseded for
  delivery. Native ARM64, Vulkan, OpenXR and non-development settings are retained.
- Final Android build also passed and verified all 1,912 scenery files.
  APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`, 1,804,008,209 bytes;
  SHA-256 `f204af2fdaf1d6ad53c2b4459da6fbf9673ddc49974505597eb40bc88c6a4590`.
  Installed on Quest serial `2G0YC5ZG8907TD`; the installed `base.apk` hash matches.
  Evidence: `android_final/` and `installed_candidate.json`.

## Remaining validation and completion audit

| Approved step | State |
| --- | --- |
| Bounded bottleneck attribution | Code/log attribution and reference preservation complete; valid tracked device attribution still pending. |
| Remove added CPU/upload work | Implemented; 81 exact source-record comparisons and focused continuity checks pass. |
| Bounded cluster candidate | Implemented and visually inspected offline; shader compiled for Vulkan. |
| Net-cost acceptance gate | Not complete. Editor geometry reduction does not satisfy the Quest GPU/frame-time gate. |
| Eligible map support | All 1,296 pages audited, bounded reusable assets and fallback authored in the candidate; performance promotion remains conditional. |
| Combine justified secondary changes and validate | Buffered tracing and worker decode implemented; Android build/install complete. Isolated simulator and final hardware validation remain. |

Do not describe every approved priority as accepted or this milestone as finished.
The candidate is installed for subsequent evaluation, not promoted as a proven
performance release. No further visual/physics features have been added.

Disk free space after Android packaging is approximately 10.6 GiB. The isolated
Windows validation needs a temporary project, its own Library and a development
player. Automatic approval review rejected removal of three obsolete generated
September 22 simulator players with `blocked by policy`. The user has been given
exact PowerShell commands to reclaim approximately 9 GiB; no deletion occurred.
This blocks the isolated stereo build, not the already completed Android build.

After cleanup, run the existing simulator runner with
`QFL_META_XR_SIMULATOR_AERIAL_SOURCES` set to the retained
`offline_scenery_remaster_v1/terrain_recovery/pages` directory,
`QFL_META_XR_SIMULATOR_WEST_EXTENT=12096` and
`QFL_META_XR_SIMULATOR_TREE_VIEW=1`, using the `cockpit_neutral` session and
a short isolated development scratch path. The development-only scenery builder
now permits that west extent, covering the actual authored mountain station.
Inspect both eyes and runtime errors; do not substitute the old Windows player.

## Gates and scope decisions

The small reusable cluster is an **evaluation candidate**, not an accepted
performance win. The authored candidate build enables it for final measurement;
the same-build diagnostic can select source geometry if it costs more. Map-wide
eligibility does not mean every grove or every view is replaced.

The naive unique 32 m / 25-view atlas design would require approximately 8.5 GiB
for one color set across eligible map groups, before normal/depth/padding costs.
It was rejected before large generation. The reusable bounded candidate avoids
that allocation. Detailed source pages are deliberately retained for close views
and immediate fallback; per-cell selection has not been replaced with a new
hierarchical/GPU culling architecture.

Cloud raymarch changes, terrain occlusion, compute culling, a depth prepass,
dynamic resolution, stronger FFR, Application SpaceWarp and URP migration remain
conditional alternatives from the approved inventory. Current evidence does not
justify adding their costs or visual risks to this candidate. The simplified-3D
cluster fallback is conditional on this candidate failing its gate; it has not
been added as a second competing renderer.

Keep 72 Hz, Medium FFR, 4x MSAA and resolution unchanged. Do not declare recovered
FPS, a 20% foliage GPU saving, sustainable thermals or release readiness until a
representative tracked Quest run supports it. If the cluster loses its net-cost
comparison, retain the CPU/streaming correction and disable the cluster rather
than calling fewer triangles success.
