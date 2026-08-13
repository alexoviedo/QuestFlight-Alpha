# Native JSBSim sole-authority gate

## Authority decision

Native JSBSim 1.3.1 is the only permitted QuestFlight product flight authority.
The product no longer contains a UnityPrototype flight backend, serialized
prototype physics component, selectable prototype build mode, or runtime
fallback. This policy applies equally to Editor, development, and release
builds.

The serialized product prefab requests the native backend. The sole
`FlightDynamicsCoordinator` prepares the pinned payload and creates only
`JSBSimNativeDevelopmentBackend` (the historical class name is retained for
serialized/code continuity). Native model-integrity, ABI, initialization,
non-finite-state, reset, or step failure makes the authority unavailable and
stops simulation; it never changes the aircraft model.

## Pinned native boundary

The embedded package is
`QuestFlight/Packages/com.alexoviedo.questflight.jsbsim`. It contains:

- JSBSim 1.3.1 native libraries for Windows x86_64 development and Android
  ARM64 product builds;
- the versioned project C ABI wrapper;
- the private `qfl-c172p-private-fidelity-v2` C172P derivative and bounded
  runtime data;
- build-time StreamingAssets injection and runtime SHA-256 validation;
- source, model, binary, and license records.

The Android libraries were built using NDK 27.2.12479018 and have verified
16 KiB ELF LOAD alignment. No R&D scene, sidecar service, alternate authority,
debug UI, or runtime hierarchy repair is imported by this boundary.

## Current evidence

The native-only scenario suite executed all 12 scenarios on 2026-08-13. It
reported zero failures and zero errors: beta/yaw damping, coordinated turn, and
ground steering passed every currently defined bound; nine scenarios remain
honestly `Unproven` where an independently sourced, condition-specific C172P
acceptance envelope is not yet pinned. The power-off trace recorded an engine-off
mean sink of approximately 260 ft/min at approximately 72 kt, replacing the
retired prototype's nonphysical vertical-plunge path. That trace is regression
evidence, not a best-glide fidelity claim.

Final post-removal EditMode tests passed 319/319. PlayMode and the Android build
must pass for each candidate. Physical Quest handling, 72 Hz timing, thermal,
stale/tear, and allocation acceptance remain hardware gates for the exact APK.

## Claims boundary

Making native JSBSim the sole authority is an architecture and integrity
decision, not certification. Nine scenario envelopes and exact-APK physical
acceptance remain incomplete. The project does not claim certified C172
fidelity, FAA approval, navigation suitability, or legal training credit.

The private aircraft derivative has a separate GPL declaration plus an upstream
non-sale statement. The owner has authorized private personal use; this record
does not claim public or commercial redistribution rights.
