# Cockpit Completion V3 — 2026-07-30

## Scope and source

- Repository: `C:\Users\ovied\Dev\T2\T2-QuestFlight`
- Branch: `feature/airport-tree-volumetric-v3`
- Starting HEAD: `28aa3f5daef45878a09247caf866eb46f87a48ca`
- Product prefab:
  `QuestFlight/Assets/Production/Prefabs/ProductionC172AircraftRig.prefab`
- Imported C172: KOG_THORNS, CC BY 4.0, SHA-256
  `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`
- Runtime hierarchy construction: prohibited and not used.

This milestone addressed every positively represented cockpit item identified
in the cockpit inventory. It did not fabricate controls or gauges that the
imported model does not contain as independently usable parts.

## Issue-by-issue result

| Item | Result | Implementation or precise blocker |
|---|---|---|
| Pilot yoke | Implemented + Touch | Authoritative aileron/elevator; either-hand direct/aim grab. |
| Copilot yoke | Implemented | Mirrors authoritative aileron/elevator. |
| Throttle | Implemented + Touch | Authoritative throttle with bounded push/pull travel. |
| Mixture | Implemented + Touch | Authoritative mixture with bounded push/pull travel. |
| ALT/BAT master | Implemented + Touch | Deterministic electrical state. Source is one combined part, so independent halves are not possible. |
| Primer | Implemented + Touch | Deterministic momentary state and safe release. |
| Ignition/start key | Implemented + Touch | OFF/R/L/BOTH/START state and momentary starter. |
| Avionics master | Implemented + Touch | Deterministic avionics master state. |
| Flap selector | Implemented + Touch | Writes through ProductControlInput; actual flap uses a ten-second actuator. |
| Fuel selector | Implemented + Touch | OFF/BOTH/LEFT/RIGHT deterministic selector and fuel burn. |
| Rudder/toe-brake pedals | Attempted; blocked | Mesh audit found only three dominant pedal-sized assemblies, not four complete independent rudder/toe-brake parts. No missing geometry was fabricated. |
| Elevator trim | Attempted; blocked | No independently identified operable source geometry. |
| Parking brake | Attempted; blocked | No independently identified operable source geometry. |
| Airspeed | Implemented | Authoritative IAS in the single opaque instrument batch. |
| Vertical speed | Implemented | Authoritative VSI in the same batch. |
| Altimeter | Implemented | Pressure altitude, authored barometer, three altitude pointers and setting pointer. |
| Directional gyro | Implemented | Heading card driven by authoritative heading. |
| Flap indicator | Implemented | Driven by actual deterministic actuator position. |
| Brake-pressure indicator | Implemented | Driven by left/right brake-pressure model. |
| Attitude indicator | Attempted; blocked | Pitch/bank authority exists, but source art is not safely separable. A false overlay was removed after full-resolution inspection. |
| Turn/slip | Attempted; blocked | Turn/slip presentation authority exists, but a complete separable face/airplane/ball set is not positively identified. |
| Magnetic compass | Attempted; blocked | Magnetic heading exists, but the represented source is a clipped strip, not a separable rotating disc. |
| Clock | Attempted; blocked | UTC exists; the prior candidate seed was proven to overlap attitude/turn artwork and was removed. |
| Tachometer | Attempted; blocked | RPM exists; face identity, units, calibration, and separable pointer are not proven. |
| Fuel gauges/flow | Attempted; blocked | Fuel state exists; face identities, units, and separable pointers are not proven. |
| Oil gauges | Attempted; blocked | Oil state exists; face identities, units, and separable pointers are not proven. |
| Volts/amps | Attempted; blocked | Electrical state exists; face identities, units, and separable pointers are not proven. |
| Vacuum | Attempted; blocked | Vacuum state exists; face identity, units, and separable pointer are not proven. |
| CHT/EGT | Attempted; blocked | No authoritative thermal inputs and no positive face identity. |
| GPS | Implemented | Live KBDU departure presentation in one glyph batch. |
| COM1/COM2 | Implemented | Readable deterministic frequencies in the same glyph batch. |
| Transponder | Implemented | 1200 ALT in the same glyph batch. |
| NAV/DME/audio | Attempted; blocked | No independent proven aperture/authority; no invented values. |
| Lighting switches | Attempted; blocked | Merged paddle mesh and no lighting-system authority. |
| Circuit breakers | Attempted; blocked | Merged geometry and no fault/load system authority. |
| Alternate static | Attempted; blocked | No independently identified geometry or pitot-static failure authority. |
| Carb heat/cabin heat/cabin air | Attempted; blocked | No complete independent parts or corresponding product-system authority. |
| Vents | Attempted; blocked | No independent operable geometry or airflow authority. |
| Doors/latches/windows/seats/belts | Attempted; blocked | Imported interior does not provide complete independently operable parts. |
| Cabin glass/reflections | Preserved | Existing accepted bounded reflection presentation retained. |
| Instrument lenses | Preserved | Existing accepted instrument-lens response retained. |
| Cockpit shading/cowl light enclosure | Preserved | Existing accepted stable shading and footwell light enclosure retained. |

## Visual corrections made during inspection

The first generated analog candidate was rejected during full-resolution
inspection because the authoring tool clamped millimetre-scale imported dial
radii to 12 mm and created oversized overlays. The final generator uses the
measured aperture radius. A false clock candidate and a false full-disc
magnetic-compass candidate were removed. A dedicated neutralized meter mesh
zeros UV4 on every source vertex, eliminating accidental atlas sampling. The
GPS font was reduced to a readable 12-column layout.

Best inspected cockpit image:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\cockpit_completion_v3_20260730_201438\visual_qa_final\screenshots\03_instrument_yoke.png`

Final contact sheet:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\cockpit_completion_v3_20260730_201438\visual_qa_final\visual_qa_contact_sheet.png`

## Validation

- EditMode: **PASS 239/239**.
- PlayMode: **PASS 9/9**.
- Product dependency closure: **PASS**, 365 dependencies, 360 project
  allowlisted, five Unity packages, zero local/rejected/unknown.
- Production Visual QA: all generated images passed nonblank/image checks and
  affected full-resolution cockpit images were inspected. Aggregate script
  result remains blocked only by the inherited 31/29 scene-material planning
  limit.
- Meta XR Simulator: live Quest 3 profile, single-pass stereo, HMD, left Touch,
  and right Touch **PASS**. Both full-resolution eyes were inspected.
  Aggregate replay classification is `BLOCKED_MISSING_RECORDED_REPLAYS`
  because the ten genuine `.vrs` files are absent.
- Android ARM64/Vulkan/OpenXR release-style build: **PASS**.
- APK: 141,179,223 bytes.
- APK SHA-256:
  `0aea18b2e82807ebd7811fd9bc732cb17596be396476120533c8dfcde61d7533`.
- Physical Quest worn interaction/performance acceptance: **PENDING**.

## Static budget delta

Compared with the immediately preceding cockpit baseline:

- selected draw calls: 63 → 63;
- scene materials: 31 → 31;
- active materials: 30 → 30;
- transparent slots: 7 → 7;
- selected triangles: 646,458 → 646,853 (+395, +0.06%);
- estimated textures: 223,496,210 → 212,598,416 bytes (-10.39 MiB).

The inherited product scene still fails its 29-material and three-transparent
slot planning targets. This milestone neither caused nor conceals that
existing scene-budget failure. Simulator and Editor evidence are not Quest
hardware timing or thermal evidence.

## Artifact root

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\cockpit_completion_v3_20260730_201438`
