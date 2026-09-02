# Map Boundary Recovery V1 — 2026-09-02

## Outcome

QuestFlight Free Flight now detects approach to the finite authored terrain
boundary before native terrain sampling can fail. It fades the compositor to
black, delegates one inward reset to the sole native JSBSim authority, verifies
that the native simulation advances, restores manual input, and fades back in.

The operator flew the boundary behavior on a physical Quest 3 and reported that
the change worked correctly. No Demo route, flight-control mapping, XR/camera
transform, or runtime hierarchy construction changed.

## Product contract

- Exactly one serialized `ProductMapBoundaryRecovery` is authored on
  `ProductionInputRoot`.
- Recovery is active only in Free Flight after native JSBSim initialization.
- The component uses serialized references to the flight-mode coordinator,
  `ProductControlInput`, the sole `FlightDynamicsCoordinator`, and the authored
  terrain surface.
- Runtime code does not discover or construct hierarchy and does not write the
  aircraft, XR Origin, camera, seat, or calibration transforms.
- Touch overrides are released and product flight input is suppressed only
  while the recovery owns the fade/reset sequence.
- A failed native reset or post-reset progression check remains faded and fails
  closed rather than selecting a fallback flight authority.

## Validation

- Static diff, JSON, paired-meta, and forbidden-runtime-API checks: passed.
- Focused map-boundary EditMode tests: 4/4 passed.
- Complete EditMode suite after release-gate corrections: 364/364 passed.
- Complete PlayMode suite: 22/22 passed.
- Product integrity: passed.
- Standard machine Visual QA: 124/124 captures passed. The contact sheet and
  representative cockpit/scenery views were inspected during the accepted
  candidate round. The later release-gate corrections changed only an editor
  path constant and Unity-object equality used by validation; they did not
  alter rendering.
- Non-development Android ARM64/Vulkan/OpenXR build: passed.
- Native JSBSim required and UnityPrototype fallback forbidden: confirmed by
  the build manifest.
- Physical Quest 3 boundary-recovery test: operator passed.
- Final APK installation and launch: passed; the installed base APK hash matched
  the release candidate and no startup fatal was found in logcat.

Final APK:

```text
C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight\Builds\Android\QuestFlight-production.apk
bytes=227064959
sha256=3d6bd54604291ca5701d746393b92558ae11ae13c3a6574c57d418b5d763af8d
applicationId=com.alexoviedo.t2.questflight
```

Primary external evidence:

```text
C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\map_boundary_recovery_20260902T182709Z
C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\map_boundary_release_20260902T204410Z
```

## Release-gate corrections

The first complete release run exposed two test-only blockers:

1. The new build refresh used a literal product-prefab path containing the
   historical namespace string `QuestFlightLab`, which correctly tripped the
   existing build-entry-point source guard. The build now reuses the accepted
   authoritative prefab-path constant instead of duplicating the path.
2. The environment contract used CLR `ReferenceEquals` for two Unity texture
   wrappers. The water-specific test and serialized GUIDs proved the same
   underlying texture asset was bound, while the check failed only after an
   earlier prefab load/unload in the complete suite. The contract now uses
   Unity's native object equality, still requiring the same underlying texture.

The corrective complete run passed without weakening or skipping a test.

## Limits

- This is one Quest 3 and the accepted KBDU standalone product configuration.
- The headset acceptance is a functional boundary-recovery observation, not a
  long-duration performance, thermal, comfort, broad-compatibility, certified
  fidelity, FAA-credit, or public-store-readiness claim.
