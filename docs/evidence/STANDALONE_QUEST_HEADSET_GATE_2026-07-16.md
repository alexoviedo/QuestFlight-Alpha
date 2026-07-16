# Standalone Quest Headset Gate — 2026-07-16

## Result

**FAIL_MACHINE_GATE; CONTINUE STANDALONE QUEST.** The connected Quest 3 cleared the prior Guardian blocker, launched the authored `ProductionVerticalSlice`, and completed an unrecorded 90-second warmup plus 60.64-second measurement. The exact-window app telemetry narrowly missed the 72 Hz combined-timing threshold, recorded two stale frames, and could not prove zero steady-state managed allocations. No crash, ANR, OOM, Unity exception, tear, Guardian dialog, or thermal warning occurred.

This is physical-headset timing evidence for the exact APK below. It is not a pass and does not establish worn comfort, controller acceptance, final handling quality, photorealism, training suitability, or standalone infeasibility.

## Device and tested build

- Device: Meta Quest 3, serial `2G0YC5ZG8907TD`.
- Android: 14 / API 34; build display `UP1A.231005.007.A1`.
- Mode: unattended and unrecorded (`-SkipScreenRecording`), requested refresh 72 Hz.
- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`.
- Size: 74,417,736 bytes.
- SHA-256: `b7fccd8dfccb7a3e2b7f6b6d8ad7fbbd024d96401eacace6c78b7ccb438f7264`.
- Product authority: serialized production hierarchy and Unity prototype flight backend.

## Timing and safety evidence

The runtime recorder reached readiness, warmed up for 90 seconds, and captured 4,322 frames over 60.6395 seconds. Unity delivered cadence averaged 13.8877 ms with 14.5307 ms p95 and 14.8290 ms p99. That cadence series is synchronized delivery timing, not pure execution headroom, and failed the deliberately strict per-frame threshold.

The corrected app-PID VrApi slice contains 60 one-second samples bounded by the runtime measurement markers:

| Metric | Result |
| --- | ---: |
| Average / p05 FPS | 72.55 / 72 |
| Average / p95 app time | 8.461 / 9.821 ms |
| Average / p95 combined CPU/GPU | 12.391 / 13.901 ms |
| Combined samples above 13.8889 ms | 4/60 (6.67%) |
| Average / p95 GPU utilization | 71.68% / 78.05% |
| Average / p95 CPU utilization | 12.90% / 15.00% |
| Stale / tear | 2 / 0 |
| Maximum temperature / power level state | 32 C / 0 |

Direct `GD` GPU milliseconds remained zero/unavailable. Unity FrameTimingManager exposed CPU values that track synchronized cadence and no Quest GPU values. The release allocation recorder was unavailable, so the required zero steady-state allocation condition is unproven. The supported conclusion is mixed GPU/compositor pacing near the cadence budget without single-resource saturation; no broader bottleneck is claimed.

## Scene and visual evidence

- Runtime planning estimate: 42 visible draw slots, 390,822 visible triangles, 21 materials, and 91 active renderers; all remain inside the 180 / 700,000 / 40 planning limits.
- The physical mountain probe passed over 4,321 frames and 17,284 renderer samples with zero mesh, transform, renderer-set, LOD/dither, or duplicate-source violations.
- The probe reports `ProductionWorldRoot/EnvironmentRoot`, the real-data environment active, and the procedural fallback inactive.
- The lead inspected the full-resolution 6,362,129-byte stereo screenshot. Both eyes show the same coherent cockpit, NAIP-derived ground, Boulder Reservoir, and fixed mountains without a system dialog or debug-overlay takeover. Screenshot SHA-256: `842c489aa0eee3053f18a832ea53569b9280ecce25f82aaf22b4201896614581`.
- Some optional environment, water, and seat detail fields are absent from the runtime-recorder schema and therefore default to false/zero in the host summary. They are not used as proof that visible authored objects are absent; the readiness contract, mountain report, and inspected image are the bounded evidence.
- Human status is `NOT_RUN_NO_USER_AVAILABLE`; seat placement, controller operation, comfort, and worn stereo quality remain open.

## Parser correction and evidence integrity

The capture's raw log contains current-firmware product rows with app PID `6480`, `SF=1.00`, and `Fov=0`. The host parser previously assumed `Fov=2`, so the original summary failed closed while reporting VrApi unavailable. The parser now uses app PID plus `SF=1.00`, records `Fov` as telemetry, accepts both observed firmware forms, and rejects different-PID compositor rows. A synthetic regression test covers `Fov=0`, `Fov=2`, and the compositor exclusion.

The raw capture and original `FAIL_MACHINE_GATE` summary remain unchanged. A parser-only reanalysis corrects the telemetry but cannot promote or replace the attempted-run classification.

External evidence root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\standalone_recovery_20260713_143655`

Primary files beneath that root:

- `headset_gate_awake_20260716_145712/standalone_quest_gate_summary.json`
- `headset_gate_awake_20260716_145712/temporal_gate/quest_temporal_visual_gate_summary.json`
- `headset_gate_awake_20260716_145712/temporal_gate/logcat_full.txt`
- `headset_gate_awake_20260716_145712/temporal_gate/adb_screenshot_final.png`
- `headset_gate_awake_20260716_145712/temporal_gate/pulled_device_evidence/`
- `headset_gate_awake_20260716_145712_reparsed/quest_temporal_visual_gate_summary.json`

## Decision and exact next milestone

**CONTINUE STANDALONE QUEST.** Architecture, the physical stereo launch, static budgets, thermal behavior, and near-budget app telemetry remain credible. One narrow failed run does not justify reducing visual scope; scope should be narrowed only after repeatable measurements identify a specific pressure point.

The next standalone milestone is a witnessed headset acceptance pass: make managed-allocation evidence available in the release-style player, collect at least three unrecorded 90-second/60-second runs on the resulting exact APK, and have Alex verify seat/viewpoint, both Touch controllers, comfort, worn stereo/mountain stability, and Unity-prototype handling. Optimize the measured GPU/compositor pressure if the combined-time or stale-frame miss repeats; native JSBSim remains development-only until a separate worn handling and timing pass.
