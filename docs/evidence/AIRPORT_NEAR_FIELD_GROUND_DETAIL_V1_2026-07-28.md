# Airport Near-Field Ground Detail V1 — 2026-07-28

## Scope and source

- Repository branch: `feature/airport-tree-volumetric-v3`
- Starting SHA: `20f7e3c690d729722e033153bec9e73f80e3b7aa`
- Shipping target: standalone Quest 3, Android ARM64, Vulkan, OpenXR
- Scope: airport-operational-corridor ground depth and infrastructure only
- External downloads: none

The visual reference review showed that the simulation lacked the real
airport's near grass volume, utility corridor, uneven pavement margins, and
small operational cues. This pass does not modify terrain geometry, runway
geometry/collider/markings, buildings, trees, water, sky, aircraft, cockpit,
physics, or world coverage.

## Authored implementation

- `AirportNearFieldDetailRoot` is serialized under `AirportContext`.
- 1,000 terrain-grounded dry-prairie tufts use one 12-triangle volumetric mesh,
  one instanced draw, a 340 m camera/frustum cutoff, and fixed arrays with no
  steady-state hierarchy construction.
- One 272-triangle combined mesh represents:
  - the pilot-left/north utility corridor with poles and sagging conductors;
  - two narrow irregular runway shoulder bands outside authoritative pavement;
  - eight runway-edge lights;
  - the OSM-context windsock position;
  - six bounded taxiway signs.
- The entire layer reuses `KBDU_ContextVegetation.mat`; it adds no texture,
  unique material, transparent pass, collider, realtime shadow, or `LODGroup`.
- Grass placements reject runway, apron, road, water, paved-runway clearance,
  and grass-runway clearance regions.

## Budget

| Metric | Before near-field | After | Delta | Product ceiling |
| --- | ---: | ---: | ---: | ---: |
| Authored selected draws | 49 | 51 | +2 | 180 |
| Authored selected triangles | 488,378 | 500,650 | +12,272 | 700,000 |
| Authored materials | 9 | 10 | +1 in product inventory; reused scene material | 40 |
| Near-field static triangles | 0 | 272 | +272 | 38,000 scoped |
| Near-field grass instances | 0 | 1,000 | +1,000 | 1,023 scoped |
| Transparent slots | unchanged | unchanged | 0 | unchanged |

The full serialized-closure regression gate remains below 800,000 triangles.
These are Editor planning values and do not establish Quest GPU timing.

## Visual review

Inspected full-resolution views include:

- `04_runway_from_cockpit.png`
- `08_near_terrain.png`
- `13_takeoff_roll.png`
- `28_hero_corridor_takeoff_view.png`
- `40_pilot_left_airport_fence.png`
- live stereo `cockpit_neutral`

Observed improvement: the north utility line is now readable from the runway,
runway margins have a bounded irregular transition, and the close airport
ground is no longer entirely texture-flat. The targeted correction increased
coverage while reducing grass geometry from 25,920 to 12,000 selected
triangles and reduced the static mesh from 8,382 to 272 triangles.

Known visual limitation: the grass is a flight-distance dry-prairie cue, not a
dense ground-level lawn simulation. It deliberately disappears at 340 m and
does not extend across the 12 km world.

## Validation

| Gate | Result |
| --- | --- |
| Serialized near-field contract | PASS |
| Product environment contract | PASS — 51 draws / 500,650 triangles / 10 materials |
| EditMode | PASS — 143/143 |
| PlayMode | PASS — 9/9 |
| Android build | PASS |
| APK | 140,508,038 bytes |
| APK SHA-256 | `45e2c17e52afac7df3d658797d489b9a97c405593e664740cea1a078173fe00a` |
| Meta XR live stereo/HMD/Touch | PASS |
| Recorded simulator sessions | BLOCKED — ten genuine `.vrs` files absent |
| Aggregate Visual QA | BLOCKED — inherited scene 30/29 material planning target |
| One-command product validation | BLOCKED — pre-existing R&D source-scene closure |
| Physical Quest timing/worn review | NOT RUN |

The Visual QA material mismatch is not hidden: this pass reuses an already
active material, while the broad scene inventory remains one over its inherited
planning target. The source-closure blocker names
`Usb2BleInputMapper.cs`, `FlightTelemetry.cs`, and
`ShortPlaytestDemoPilot.cs`; none is introduced by this visual layer.

## Evidence

Artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_near_field_v1_20260728_150312`

Key paths:

- `visual_qa_consolidated\visual_qa_contact_sheet.png`
- `simulator_gate\meta_xr_simulator\visual_gate\simulator_stereo_contact_sheet.png`
- `editmode_tests_pass\unity_EditMode_test_results.xml`
- `playmode_tests_pass\unity_PlayMode_test_results.xml`
- `android_build\product_apk_manifest.json`
- `product_validation\source_dry_run\extraction_summary.md`

No capture, log, build, APK, cache, or raw external asset is committed.
