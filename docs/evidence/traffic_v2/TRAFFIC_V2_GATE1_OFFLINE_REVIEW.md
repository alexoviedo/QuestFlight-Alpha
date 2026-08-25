# Traffic V2 Gate-1 Offline Walking-Skeleton Review

Date: 2026-08-22 MDT

Decision: **PASS SELF-CHECK — AWAITING REVIEW BEFORE UNITY**

This is the approved Gate-1 deliverable only. It is not an independent
implementation approval and grants no full-map, demand, intersection, continuous
collision, Unity, Quest performance, APK, or headset claim.

## Frozen inputs

- Accepted product source: `fda11a25eb526824d82a47330f2be80b1410bb64`
- External 19-source freeze SHA-256:
  `c7cd7b9362d01bacbeb7c23833cae65882bceaaaab767dcb00f9b8c0904e07b6`
- OSM raw response: 88,122,449 bytes, SHA-256
  `898561f20d8d64e5e2a81471b1b1a8eb437bb4629cdee68d7b8a691143549f99`
- Benchmark areas: `airport_boulevard`,
  `diagonal_highway_approach`, and `residential`.
- No network acquisition occurred.

## Implementation boundary

- `tools/traffic_v2/geospatial/compile_walking_skeleton_lanes.py` reads and
  hash-validates the immutable freeze, deduplicates exact OSM way occurrences,
  applies conservative motor-access/oneway/lane rules, and emits directed lane
  chains for the three areas.
- `tools/traffic_v2/performance/compile_walking_skeleton.py` selects real source
  lanes, authors non-wrapping origin-to-destination trips on one source way,
  creates persistent identities and smooth acceleration/cruise/braking
  keyframes, renders the overlay, and binds the artifacts.
- The previous monolithic Phase-3 compiler was not invoked or modified.

## Primary external artifacts

Root:

`traffic_system_v2_20260821_170722/gate1_walking_skeleton`

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `traffic_v2_gate1_lane_graph.json` | 585,136 | `d5a35c44134b55ae982f238c19574b86770fb89eacf18280e99a59467c333e68` |
| `traffic_v2_gate1_trajectory_manifest.json` | 131,675 | `dd687bc7de331cefc439f755de643a79bad6e54ecb2772a71efefbdddde3a469` |
| `traffic_v2_gate1_overlay.png` | 43,945 | `684732a2c8523dd6acc985ed3a983ae88f6d62960a239f457f9b733560f8c61a` |
| `traffic_v2_gate1_report.json` | 1,540 | `04fb0fc35cc79b20b7c8b06d37e305494cf61e9f85d23e113013e6a0de923306` |
| `traffic_v2_gate1_hashes.json` | 653 | `74fae72bef6f8c5d9cfbb6b4130cc86d6cedf3fbad17a49cd487e4e260711b3d` |

An independently routed second invocation under external
`gate1_walking_skeleton_rebuild` produced byte-identical copies of all five
files.

## Self-check results

- End-to-end primary compile: 1.61 seconds.
- Deterministic rebuild: 1.64 seconds.
- Directed lane chains: 886 total — airport 211, divided highway 162,
  residential 513.
- Exact duplicate OSM way occurrences deduplicated: 900.
- Timed trajectories: 12, four per area.
- Global schedule: 900 seconds, non-looping.
- Real opposing Diagonal Highway carriageway heading difference: 176.023°.
- Every selected trip binds one real frozen OSM way and has no route wrap.
- Sampled 0.5-second minimum concurrent center separation: 1,071.27 m. This is
  only a walking-skeleton sample and is not continuous swept collision proof.
- Focused tests: 3 passed in 3.40 seconds, including two-output byte-for-byte
  determinism against the real frozen input.
- Full automated suites were not run.

The full-resolution overlay was inspected. It is legible, labels every panel as
Gate-1-only, shows the selected real lane chains and direction arrows, and does
not visually conflate the opposing Diagonal Highway carriageways.

## Explicit limitations

- This selects three representative areas, not the full map.
- The lane graph has no legal intersection-turn authority yet.
- Each Gate-1 trip stays on one source way; no junction transition is claimed.
- Traffic demand and fleet distribution are not calibrated.
- Trips are deliberately serialized within each area; the separation metric is
  not evidence of realistic density or a complete collision solution.
- Vehicle model/material bindings are deferred.
- Building, water, median, bridge, and road-footprint swept containment are not
  yet validated for these trajectories.
- No Unity product file, scene, prefab, manifest, or hierarchy was modified.
- No APK was built or installed.
- No headset result exists for Traffic V2.

## Gate decision

Gate 1 has achieved its narrow objective: one bounded, deterministic, real-data
offline chain from frozen roads to directed lanes, persistent timed trajectories,
an inspectable overlay, and exact reproducible bytes in under two seconds.

Per the approved recovery plan, work stops here before Unity integration. Gate 2
requires an explicit review decision on these artifacts and limitations.
