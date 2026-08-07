# Quest mixed-input and flight-stability milestone — 2026-08-06

## Scope

This checkpoint preserves the first product candidate in this recovery line for
which the operator confirmed that both Meta hand tracking and Quest Touch
controllers work in the authored cockpit. It also contains the evidence-driven
correction for the autonomous takeoff yaw/route regression discovered during
the same hardware review.

This is a development milestone, not a release, performance-acceptance, flight
model fidelity, FAA, navigation, or training claim.

## Accepted hardware result

On 2026-08-06, after installing the exact APK recorded below on the connected
Quest 3, the operator reported:

> The controllers, both hand and quest seem to be working now.

The accepted product interaction route uses Meta OVR concurrent interaction,
single-owner constrained grabs, and the existing serialized product input
authority. It does not construct or replace the XR, camera, aircraft, or flight
hierarchy at runtime.

## Flight regression cause and correction

Behavior-neutral telemetry on the physical Quest proved two independent
causes rather than attributing the route defect to controller arbitration:

1. Development builds could silently select the unpromoted native JSBSim
   backend. That backend developed severe yaw motion after the applied rudder
   had returned to zero.
2. The deterministic demo used open-loop taxi/takeoff rudder commands, which
   also displaced the authoritative Unity prototype from the runway.

The checkpoint therefore makes native JSBSim an explicit development-build
opt-in and replaces the runway-phase open-loop rudder schedule with bounded
heading and cross-track feedback. The Unity prototype remains authoritative.

The corrected physical-Quest autonomous route recorded approximately zero
takeoff heading deviation, a maximum takeoff runway offset of 0.00021 m, and a
maximum takeoff rudder command below 0.000001. The later intentional demo turn
remained active.

Detailed machine evidence is stored outside Git at:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\flight_cause_isolation_20260806\root_cause_and_fix_report.md`

## Immutable APK identity

- Package: `com.alexoviedo.t2.questflight`
- Scene: `Assets/Scenes/ProductionVerticalSlice.unity`
- Build type: release-style Android build
- Size: 173,856,086 bytes
- SHA256: `c5399628f61255843be02fa1489a27b33d6c54a0e3038c8b1aa5241f18326fe7`
- Installation: successful on the connected Quest 3

The APK and build outputs remain ignored and are not committed.

## Validation boundary

- Product integrity passed as part of the Android build.
- The release-style Android build passed.
- The corrected autonomous route was measured on the physical Quest.
- Hand tracking and Quest controller operation were accepted by the operator
  in the worn test.
- Broad EditMode, PlayMode, stereo Visual QA, and long-duration performance or
  thermal acceptance were not rerun for this narrow checkpoint.
