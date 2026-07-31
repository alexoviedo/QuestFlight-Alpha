# Cockpit Dynamics Completion V2 — 2026-07-30

## Evidence status

This record describes the cockpit implementation and the validation evidence
available on 2026-07-30. It is a development milestone record, not a release,
certification, training-credit, photorealism, or Quest performance claim.

- Repository branch: `feature/airport-tree-volumetric-v3`
- Source HEAD at evidence capture:
  `20f7e3c690d729722e033153bec9e73f80e3b7aa`
- Cockpit implementation commit:
  `5535ce4bda168b4aa92bdc3eae777be9088a31ea`
- Product prefab:
  `QuestFlight/Assets/Production/Prefabs/ProductionC172AircraftRig.prefab`
- Frozen C172 source:
  `QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb`
- Frozen source SHA256:
  `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`

The source branch contains other intentional work in progress. This document
does not classify or validate unrelated changes.

## Implemented authority and presentation

`ProductionCockpitSystemsController` is the one serialized adapter from the
single `FlightDynamicsCoordinator`. It owns the existing cockpit presentation
snapshot and the subordinate allocation-free
`CockpitSystemsAuthorityV2Model`; it does not create another flight authority.

SystemsV2 truthfully exposes:

- deterministic scenario UTC;
- flight-control commands;
- engine RPM;
- battery-master, alternator-master, avionics-master, ignition/start, primer,
  fuel-selector, and flap-selector command positions;
- main-bus, avionics-bus, and starter-motor power only when external
  electrical-source state is authoritative, or both electrical masters prove
  the buses off.

The serialized presentation implements:

- pilot and copilot yoke response;
- throttle and mixture travel;
- exterior aileron, elevator, flap, rudder, and bounded nose-steering response;
- the positively identified IAS/KNOTS pointer;
- the positively identified principal vertical-speed pointer.

The V2 instrument adapter can expose pitch/bank, true heading, and scenario
UTC, but those channels do not activate additional gauges without separately
proven geometry, calibration, and serialized bindings.

## Touch interaction

The product prefab contains one `CockpitTouchInteractionV2Controller` and one
`CockpitTouchInputV2Bridge`.

The positively represented Touch targets are:

- pilot yoke grab → authoritative aileron/elevator input;
- throttle grab → authoritative throttle input;
- mixture grab → authoritative mixture input.

The controller uses the serialized OpenXR Touch origins, a fixed-size raycast
buffer, and a bounded command queue. The bridge arbitrates into the existing
`ProductControlInput` through the sole `FlightDynamicsCoordinator`; it does not
write visual transforms. Unsupported controls have no Touch binding.

The static Touch audit completed for this milestone verifies the bounded target
allowlist and serialized-reference contract. This is code/EditMode evidence,
not worn interaction evidence.

## Fail-closed exclusions

The completion does not synthesize missing aircraft-system data.

- The altimeter remains static: pressure altitude, barometric setting, and a
  complete separable needle/drum set are not all available. MSL altitude is not
  relabeled as pressure altitude.
- The attitude indicator, directional gyro, and clock remain static despite
  available pitch/bank, true heading, and scenario UTC because their separable
  topology and/or absolute registration are not proven.
- The magnetic compass, flap-position indicator, and brake-pressure gauge
  remain static because magnetic variation, actual flap position, and measured
  brake pressure are unavailable.
- Fuel, oil, electrical measurement, vacuum, tachometer, `MACH/AS`,
  `GYRO/LOC`, duplicate VSI, and unlabeled/fantasy faces remain static unless
  positive identity, units, calibration, and authority are all proven.
- The avionics adapter exposes electrical power only. GPS, COM, DME,
  transponder, and audio content remain unavailable. Existing readable static
  panels are preserved rather than covered with invented live text.
- Unrepresented controls and incomplete merged geometry remain noninteractive.

## Source-manifest result

`tools/cockpit_dynamics/cockpit_representation_manifest_v2.json` parses and
records exactly **11 implemented targets**. Represented candidates remain
separate from the implemented allowlist and retain explicit blockers.

The manifest also records:

- the consolidated SystemsV2 authority boundary;
- exact authoritative, conditional, and unavailable channels;
- the three Touch targets;
- the static/blank fail-closed policy;
- zero invented data.

## Validation completed

| Check | Result | Scope |
|---|---:|---|
| ProductIntegrity | **PASS** | 1,210 assets; 379 scene objects; 104 renderers; 799,363/800,000 integrity triangles; 29 integrity materials |
| EditMode | **PASS — 225/225** | Current cockpit/source/integration contracts included |
| PlayMode | **PASS — 9/9** | Current product PlayMode suite |
| Cockpit Python tests | **PASS — 3/3** | Representation manifest and tooling |
| Current Unity dependency closure | **PASS — ready=True** | 355 of 360 dependencies accepted; five package dependencies; zero unknown; zero rejected |
| Source representation manifest | **PASS** | JSON parsed; 11 implemented targets |
| Android ARM64 build | **PASS** | Release-style IL2CPP/Vulkan APK |

The dependency-closure result is:

- accepted product/source items: **355/360**;
- package dependencies: **5**;
- unknown dependencies: **0**;
- rejected dependencies: **0**;
- ready: **True**.

The aggregate `validate_product.ps1` command is nevertheless **not a pass**.
Its separate pinned R&D-source dry-run closure (source commit
`cd4131fa7732a6db2d7387921dfef33e2c206970`) still reports one excluded legacy
dependency (`Usb2BleInputMapper`) and two unclassified dependencies
(`FlightTelemetry` and `ShortPlaytestDemoPilot`). The current Unity product
dependency closure above is a different check and passed. The aggregate command
also propagated the Visual QA material-budget failure described below.

## Visibility-list regression

During analog-instrument integration, the aircraft visibility profile's
serialized `interiorRenderers` list could be damaged if the new combined
instrument renderer was treated as a replacement list instead of an appended
entry. The integration was corrected to:

1. read the existing non-null serialized interior-renderer list;
2. preserve every unrelated renderer and its ordering;
3. remove only a duplicate occurrence of the target renderer;
4. append the one combined dynamic analog renderer;
5. regenerate and validate the corresponding geometry-derived directional
   visibility stream.

This fixed the visibility-list regression without adding a transparent pass or
reintroducing runtime hierarchy repair.

## Visual QA and static budget

The standard production Visual QA capture completed 103 shot records against
97 required views. Every recorded shot passed the nonblank and giant-overlay
checks. The lead inspected the contact sheet plus the full-resolution default,
calibrated, instrument/yoke, and engine-cowl cockpit views. The cockpit
remained readable, its static panel content remained visible, and the
integrated IAS/VSI presentation did not reintroduce the prior black/gray
display regression.

The aggregate machine result remains **FAIL_MACHINE_QA** because the complete
scene contains 31 visible materials against the current planning target of 29.
The cockpit completion added no renderer, material, texture, draw call, or
triangle cost. The complete-scene figures exactly match the recorded
pre-completion baseline:

| Metric | Before | After | Target | Result |
|---|---:|---:|---:|---|
| Selected draws | 63 | 63 | <= 75 | PASS |
| Visible triangles | 646,458 | 646,458 | <= 650,000 | PASS |
| Visible materials | 31 | 31 | <= 29 | PRE-EXISTING FAIL |
| Estimated textures | 213.14 MiB | 213.14 MiB | <= 210 MiB | PRE-EXISTING FAIL |
| Transparent material slots | 7 | 7 | <= 3 | PRE-EXISTING FAIL |

The Touch path adds three small trigger colliders and no render cost. Static
inspection plus the allocation-focused EditMode tests found no steady-state
managed allocation in the new systems, analog, or Touch paths.

Evidence:

- `cockpit_dynamics_completion_v2_20260730_155732/full_validation_retry/visual_qa/visual_qa_report.json`
- `cockpit_dynamics_completion_v2_20260730_155732/full_validation_retry/visual_qa/visual_qa_contact_sheet.png`
- `cockpit_dynamics_completion_v2_20260730_155732/performance_budget/performance_budget_report.json`
- `cockpit_dynamics_completion_v2_20260730_155732/touch_input_v2_static_audit.md`

## Simulator result

The Meta XR Simulator V201.0 audit completed after correcting an
encoding-dependent Windows PowerShell parser failure in the gate wrapper.
The wrapper preserved the active OpenXR runtime, restored Meta's persistent
configuration, and did not change Android shipping configuration.

Classification:
**BLOCKED_MISSING_RECORDED_REPLAYS**.

All ten required `.vrs` session recordings are absent, so no current stereo
contact sheet or live replay result is claimed. This is an evidence-coverage
blocker, not a passed stereo gate and not Quest hardware performance evidence.

Evidence:

- `cockpit_dynamics_completion_v2_20260730_155732/simulator_gate_retry/meta_xr_simulator/visual_gate/simulator_visual_report.json`
- `cockpit_dynamics_completion_v2_20260730_155732/simulator_gate_retry/meta_xr_simulator/qa_run/simulator_qa_run_report.json`

## Android build

The release-style Android build completed successfully:

- Unity: `6000.3.8f1`;
- scene: `Assets/Scenes/ProductionVerticalSlice.unity`;
- package: `com.alexoviedo.t2.questflight`;
- size: **141,116,697 bytes (134.58 MiB)**;
- SHA256:
  `4e8da976e16344a8e04eaa706d8791a00d45fcd78d391f037d6f48d9457befff`;
- development build: **False**.

The APK remains an excluded build artifact and is not committed.

Evidence:

- `cockpit_dynamics_completion_v2_20260730_155732/android_build_retry/product_apk_manifest.json`

## Acceptance still required

The following gates remain incomplete and are therefore **not passed**:

- recorded/replayed Meta XR Simulator stereo coverage and full-resolution
  inspection for the exact candidate;
- recovery of the pre-existing complete-scene material, texture, and
  transparent-slot budget overruns;
- physical Quest timing/thermal measurement;
- worn Touch interaction, comfort, readability, and visual acceptance.

The completed Android build, EditMode, PlayMode, dependency, and visual
inspection evidence does not replace those missing gates. Simulator evidence
must never be presented as Quest hardware performance.
