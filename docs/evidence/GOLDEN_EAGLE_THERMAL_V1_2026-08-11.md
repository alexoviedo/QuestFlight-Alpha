# Golden Eagle Thermal V1 — 2026-08-11

## Scope and source

- Source branch: `feature/golden-eagle-thermal-v1`
- Source checkpoint: `4bdd7a2111b5f62f4d38aafccc5c5c1c447a5a9f`
- Safety tag: `pre-jsbsim-authority-and-eagle-20260811`
- Product target: standalone Quest 3, Android ARM64, Vulkan, OpenXR
- Source model: `Hawk Lp Rigged` by Sherkiz from Poly Pizza, CC BY 4.0
- Catalog: <https://poly.pizza/m/RkN6MEbP6g>
- Raw-source SHA-256: `32febb3138447862039b77d67c62ed02f6b028362532755d239856ee4fee25b8`

The raw 866,000-byte GLB remains outside Git. Blender 5.1.2 and
`tools/bird/author_golden_eagle.py` produce the committed project-owned source
and runtime derivatives. Exact source, derivative hashes, attribution, and
change notices are pinned in
`Assets/Production/Environment/Wildlife/SOURCE_MANIFEST.json`.

## Representation and behavior

One explicitly serialized bird uses one 10,036-triangle skinned renderer, one
opaque material, one 1024-square Android ASTC 6x6 atlas, and the source `Fly`
animation. Realtime shadows, transparency, colliders, rigidbodies, runtime
hierarchy construction, and per-eye billboard selection are absent.

`GoldenEagleThermalSoaring` follows a deterministic 105 m orbit at 515 m in the
single authored `KBDU_SunlitFieldThermal01` field. Its 16 m/s orbit, 18-degree
bank, predominantly gliding animation, and restrained flap interval are a
bounded soaring cue rather than an ecological simulation.

The shared thermal is centered at Unity ENU `(1700, 0, 2600)` on the authored
climb-to-reservoir corridor. It uses a 70 m core, 240 m outer radius, 40–1100 m
vertical envelope, and a smooth peak 2.5 m/s updraft. The bird and flight
coordinator serialize the same profile GUID.

## Flight-authority decision

The release product continues to serialize `UnityPrototype` as requested
authority. That backend has no wind model, so release builds show the bird but
do not claim an aerodynamic thermal. Development builds compiled with
`QUESTFLIGHT_ENABLE_NATIVE_JSBSIM` sample the shared profile at the authoritative
aircraft position and submit the corresponding negative-NED vertical wind
through the existing JSBSim atmosphere ABI before each fixed step.

This split is deliberate. Native evidence currently records 3 passes, 0 fails,
and 9 unproven scenarios. It does not satisfy the unchanged native promotion
gate, so no release-authority promotion is claimed.

## Validation

- EditMode: **PASS 308/308**.
- PlayMode: **PASS 10/10**.
- Final product dependency closure: **PASS**, 394 approved dependencies, zero
  rejected, and zero unknown. The separate frozen R&D source dry-run retains its
  historical excluded/unclassified findings and is not the product closure.
- Wildlife Visual QA: both dedicated views pass. The close view is 10 estimated
  draws / 141,276 visible triangles; the route-context view is 10 / 143,840.
- Aggregate Visual QA: **FAIL inherited planning ceilings**, with maximum 80
  draws, 675,865 visible triangles, and 38 scene materials against stale generic
  targets of 75 / 650,000 / 29. The wildlife views themselves pass.
- Environment serialized contract: 53 renderers, 504,094 triangles, 12
  materials.
- Meta XR Simulator: `BLOCKED_MISSING_RECORDED_REPLAYS`; no genuine VRS replay
  set was available. No simulator performance claim is made.
- Release Android build: **PASS**, 175,246,444 bytes, SHA-256
  `5d705682983ee6845bad8af1c2686eb82658538fc1b1bbad2059d26e312f430d`.
- Native development build: **PASS**, 301,770,701 bytes, SHA-256
  `fac5d6ab050a6db6e8fed9a4ddde3c31ab726a8af0ad8a7fcd9c9c4ac3c3de13`.
- Quest install: **PASS** for the native development APK. Unattended cold launch
  produced `BLOCKED_STARTUP_INPUT_FOCUS_ANR` while the headset was not worn;
  therefore no worn visual, thermal-feel, frame-time, or thermal-hardware
  acceptance is claimed.

## Evidence and limitations

Artifact root:
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\golden_eagle_thermal_v1_20260811_152145`

The close visual reads as a low-poly brown soaring raptor and is suitable as a
route cue, but the source anatomy is a generic hawk proxy rather than a surveyed
Golden Eagle specimen. There is one bird and one deterministic fair-weather
thermal; no flocking, live weather, random thermals, bird collision, strike
model, ecological population claim, FAA approval, navigation claim, training
credit, certified fidelity, or Quest performance acceptance is asserted.
