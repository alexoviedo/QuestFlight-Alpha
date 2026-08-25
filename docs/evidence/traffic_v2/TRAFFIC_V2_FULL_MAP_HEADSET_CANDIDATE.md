# Traffic V2 Full-Map Headset Candidate — 2026-08-24

## Result

The sparse 12-trip Traffic V2 walking skeleton and the earlier OSM-only full-map candidate were superseded by a continuous, tiled full-map candidate compiled from the completed canonical-selection checkpoint. The exact APK was installed on the connected Quest 3, launched successfully, and remained the active resumed application without an observed app-scoped fatal error. The user subsequently judged the worn result acceptable for the time being.

This is a useful development checkpoint, not final Traffic V2, visual, source-gate, or Quest-performance acceptance. The final Phase-3 reference, approval, and normative-output stages did not complete, so the product data remains explicitly marked `PENDING_PHASE3_SOURCE_GATE`.

## Frozen source and product data

- Accepted repository source: `fda11a25eb526824d82a47330f2be80b1410bb64`.
- Final source freeze SHA-256: `6f643c3d44d115c64b9b2c9b2bbf84cb331979fdc7598ed11b0d2e34377db097`.
- Candidate source mode: `CANONICAL_SELECTION_CHECKPOINT_WITH_DISCLOSED_LANE_DEFAULTS`.
- Canonical-selection input: 169,123,972 bytes, SHA-256 `b5406f6c680d06872a6cb864c39771d167d265ed1e0ff786c9b3c8b0b192f241`.
- Product manifest: 14,309,553 bytes, SHA-256 `e45e9fb4284780a643fc54f20a7da1ff25754174376bb6cb24cef9e3c4205209`.
- Product source/license record: 2,640 bytes, SHA-256 `6023d38c8f1b5903777e34e2d065ba46b19cffa628694d0c0a6197258ebd6af1`.
- Corridor evidence: 26,037,439 bytes, SHA-256 `867a69d1441be1cd1c26bdd793db945ea92e0aeb391d48db9fb18d736ccc7903`.
- Hash index: 892 bytes, SHA-256 `7493f5d095cf35fc8356e1e466d9e2a61f8a3c176e4585c0e03217e63e5d75df`.

The durable external candidate now contains the exact canonical-selection input at `canonical_checkpoint_6f643c3d/candidate_input/canonical_road_universe.geojsonl`; the file is no longer dependent on disposable compiler staging.

## Implemented contract

- 32,770 emitted corridors covering 5,515.911609 corridor-km across the bounded +/-12 km map.
- 508 occupied 1 km spatial-index tiles.
- Continuous deterministic departure streams; no 900-second schedule end and no global zero-traffic interval.
- 10,780.403 estimated simultaneous map-wide movers at the disclosed 0.35 headset-development demand scale and headway bounds.
- 2,879 corridors directly bound to 376 frozen CDOT or County count observations.
- 19,786 emitted corridors without canonical lane totals use the disclosed development defaults of one lane for directional roads or two for bidirectional roads.
- 1,260 source records shorter than 12 m are omitted and disclosed rather than silently converted into traffic corridors.
- Camera-local tiled preparation, 1 km near LOD, stable deterministic identities, 2,048 instances per render group, and exact 1,023-instance draw chunking.
- Conservative authored maximum of 18 Traffic V2 draws.
- No runtime hierarchy construction, per-vehicle GameObjects, or Main Camera writes.
- Traffic V1 retains its accepted parked field while its moving simulation is disabled; Traffic V2 is the sole moving-traffic presenter.

## Focused verification

- Traffic V2 Unity EditMode contract suite: 5 passed, 0 failed.
- Python full-map compiler/scheduler suite: 6 passed, 0 failed.
- Deterministic rebuild: all five full-map products were byte-identical.
- Focused visual QA: 15/15 1280x720 captures completed with zero dropped instances.

Prepared instance ranges across the five areas and times 0, 60, and 120 seconds were:

- Airport Boulevard: 65-90 near and 2,411-2,459 far.
- Diagonal Highway: 143-151 near and 1,770-1,802 far.
- Downtown: 213-238 near and 2,904-2,936 far.
- Residential: 254-268 near and 2,839-2,852 far.
- Map periphery: 4-6 near and 509-526 far.

Representative full-resolution frames were inspected after rerunning with a graphics device. The first attempted visual command incorrectly used `-nographics`, produced a NullGfxDevice failure, and is not treated as evidence. The accepted focused visual-QA JSON is 8,433 bytes, SHA-256 `1aa82b7cdf6fa94682c640b1081ba438a46dd33d0f6f2bec32db42adfc57801b`.

## Android build and headset result

- Package: `com.alexoviedo.t2.questflight`.
- Version: `0.1.0-alpha.1` (`versionCode=1`).
- Build result: succeeded with 0 errors and 15 warnings.
- Configuration: non-development Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced, Linear color, and 4x MSAA.
- Signing: Android debug certificate, suitable only for this local development candidate.
- Native JSBSim required; UnityPrototype flight dynamics disallowed.
- APK: 226,246,922 bytes.
- APK SHA-256: `93217f2371b8b051ce1cc8a98f1b121a4d952b29cd64d94cf65a558803b3fe52`.
- Build evidence: 991 bytes, SHA-256 `3a84a4be5363e6d315fc33d986343424cd1658b7fa6bec574bb55a92aac3d13d`.
- `adb install -r -d` returned `Success`; `UnityPlayerGameActivity` launched and remained active/resumed during the observed interval.
- The previous APK, SHA-256 `c41338033e8deacd0c460de148c962bc564cfe1bd0c1c2d63657123a175a0bed`, remains in the external rollback directory.

External retained root:

`traffic_system_v2_20260821_170722/full_map_headset_candidate/canonical_checkpoint_6f643c3d`

## Acceptance boundary and remaining work

- The canonical-selection checkpoint is useful and authority-conflated, but it is not the final approved Phase-3 normative universe. Unknown class/access/correspondence records and explicit exclusions remain outside this development adapter.
- Lane defaults on 19,786 corridors and the 1,260 short-record omissions must be resolved or explicitly accepted by the later source gate.
- Corridors spawn and despawn vehicles at endpoints. Legal intersection turns, controls, queues, collision interaction, and signal timing remain deferred.
- Focused Editor captures and a successful worn smoke test do not establish sustained Quest frame time, thermal behavior, stereo quality, full-map density acceptance, or collision-free behavior.
- The standard product Visual QA retains six previously recorded views above the frozen 77-draw ceiling; those failures are not waived.
- Final Traffic V2 acceptance still requires completed source adjudication, regenerated exact product data, collision/semantic validation, inspected stereo/headset evidence, and measured Quest performance and thermal results.

## Development decision

The canonical-checkpoint APK is the current usable Traffic V2 development baseline and is acceptable for continued work. It must remain labelled as a headset candidate with `PENDING_PHASE3_SOURCE_GATE`; it is not a release or final Traffic V2 acceptance claim.
