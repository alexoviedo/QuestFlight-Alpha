# Full-Map Building Roof Topology V1 — 2026-09-02

Classification: `VISUALLY_ACCEPTED_HARDWARE_GATE_PENDING`

This milestone adds deterministic proper low-poly roof topology to a selected
subset of safe gable and hip buildings. The rollout is selective, not global.
It preserves the accepted Phase 1 corner softening, every represented building,
the 4x4 protected base plus eight forward pages, the existing renderer/material
pool, and the unchanged 235,000 selected-building-triangle ceiling.

## Starting state and Phase 1 sanity checks

- Starting branch: `main`
- Starting commit: `78bad8100ce06f947c558a13188b9b6fb66024bb`
  (`feat: add full-map building corner softening`)
- Starting worktree: clean
- Phase 2 branch: `feature/full-map-building-roof-topology-v1`
- Phase 1 focused parent/current diff: the building database was unchanged;
  the controller change only reconciled its stale 233,316 allowance to the
  generated 233,298 maximum. The database remained 57,441 buildings in 576
  pages (483 nonempty), and the controller remained 16 protected base slots
  plus eight connected forward slots.
- Phase 1 static sanity audit: PASS. The command explicitly consumed
  `...\candidate_tuned_035\visual_qa\visual_qa_report.json`; the resulting JSON
  recorded that exact path and report-derived maxima of 51 draws and 381,713
  visible triangles.

Artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_building_roof_topology_v1_20260903_025908`

## Confirmed root cause

The legacy compiler creates roof vertices only at compiled footprint-boundary
points. It evaluates `RoofHeight` at those points and ear-clips the same boundary.
For an ordinary rectangle, both gable and hip height functions evaluate to zero
at all four corners; no interior ridge or apex exists to receive the intended
rise.

An executable failing-before regression used a 12 m x 8 m rectangle with 2 m
rise. The legacy gable result reached 0 m rather than 2 m and failed as expected.
Artifact: `legacy_roof_failure\results.xml`. The final test asserts this legacy
behavior for both identifiers and verifies that the new helper reaches the full
rise.

## Geometry and eligibility

`FullMapBuildingRoofTopology` is a pure deterministic editor helper. It first
normalizes winding to counter-clockwise and accepts only positive-rise gable or
hip roofs with a simple, convex, four-corner stable footprint. It rejects exact
landmarks and any footprint with:

- area below 6 m2;
- an edge shorter than 1.5 m;
- a corner turn outside 65–115 degrees;
- opposite edges differing by more than 12 degrees from parallel;
- opposite edge-length ratio above 1.25;
- aspect ratio above 6;
- an ambiguous gable axis below aspect ratio 1.12;
- non-finite, degenerate, or self-intersecting geometry.

Rejected or budget-unselected buildings keep the previous safe boundary roof.

Proper gables contain two ridge endpoints at wall top plus intended rise, four
upward roof triangles, and two outward triangular gable infills: six triangles,
or +4 over the old rectangle. Gable infills use the existing facade material and
metric UV stream. Facade role 3 suppresses procedural windows and doors, and the
existing color-alpha corner metadata is explicitly disabled on the infill.

Compact hips contain one center apex and four roof triangles (+2). Elongated hips
use an equal-pitch short ridge, two trapezoidal side fields, and two triangular end
fields: six roof triangles (+4). All new roof vertices retain the existing roof
semantic flag and use their world XZ positions for the unchanged orthophoto
projection. There is no overhang, runtime generation, roof LOD, new material,
texture, sample, keyword, renderer, draw, transparency, or representation swap.

Generated watertight metrics remain zero for maximum wall/roof and vertical-corner
seams. Terrain-sealed regular-wall vertices remain 485,514. Roof-sealed wall and
gable vertices are 515,424.

## Feasibility and deterministic allocation

The broad uncommitted visual prototype corrected every safe candidate. It was
visibly better in the facade-oblique, residential-low, and broader residential
views, but its conservative 24-page maximum was 261,348, or 26,348 above the
unchanged ceiling. It therefore could not ship globally without new measured
budget authority.

The final editor-time greedy allocator sorts candidates using the current
Visual-QA building targets, the authored `KbduExtendedReservoirLesson` route,
residential class, area, height, intended rise, semantic/material confidence,
triangle cost, and stable source/component/building identity. Distances are
quantized to centimeters. Each tentative admission recomputes the sum of the 24
heaviest pages and is retained only at or below 235,000. Input order does not
affect the result.

Allocation report:

`QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_RoofTopologyAllocationV1.json`

| Metric | Count |
|---|---:|
| Nominal gables | 23,656 |
| Nominal hips | 5,078 |
| Positive-rise pitched roofs | 28,734 |
| Safe gables | 10,685 |
| Safe compact hips | 659 |
| Safe short-ridge hips | 1,683 |
| All safe roofs | 13,027 |
| Selected gables | 4,985 |
| Selected compact hips | 298 |
| Selected short-ridge hips | 679 |
| Selected roofs | 5,962 |
| Safe candidates rejected only by budget | 7,065 |
| Ambiguous/unsupported nominal pitched roofs | 15,707 |

The allocation selects 45.77% of safe candidates and 20.75% of all nominal
pitched roofs. It retains 1,198 of 1,415 safe roofs near the airport and 1,314 of
1,531 near the guided route. Within the 1 km acceptance-view radius it selects
97 airport roofs, 102 CU/downtown roofs, 354 residential roofs, 170 North Boulder
roofs, and one reservoir roof.

| Budget metric | Phase 1 | All safe | Final selected |
|---|---:|---:|---:|
| Map-wide added triangles | 0 | 50,790 | 23,252 |
| Conservative 24-page maximum | 233,298 | 261,348 | 235,000 |
| Margin under unchanged ceiling | 1,702 | -26,348 | 0 |

The map-wide triangle delta is larger than the 1,702 maximum-selected delta
because many corrected roofs live outside the conservative 24 heaviest pages.

## Baseline versus final product contracts

| Metric | Phase 1 | Phase 2 final | Result |
|---|---:|---:|---|
| Represented buildings | 57,441 | 57,441 | unchanged |
| Pages / nonempty pages | 576 / 483 | 576 / 483 | unchanged |
| Active building slots | 24 | 24 | unchanged |
| Generated vertices | 1,354,653 | 1,424,409 | +69,756 roof/infill vertices |
| Generated triangles | 613,389 | 636,641 | +23,252 roof/infill triangles |
| Maximum selected building triangles | 233,298 | 235,000 | ceiling unchanged |
| Maximum building draws | 24 | 24 | unchanged |
| Building materials | 1 opaque | 1 opaque | unchanged |
| Building-owned textures | 4 | 4 | unchanged |
| Building-owned estimated texture memory | 8,197,344 B | 8,197,344 B | unchanged |
| Building transparent slots | 0 | 0 | unchanged |
| Building shader texture sample sites | 6 | 6 | unchanged |
| Corner softening | 0.35 m / 0.65 | 0.35 m / 0.65 | unchanged |
| Building bundle payload | 30,737,388 B | 32,141,694 B | +1,404,306 B (+4.57%) |
| Complete building bundle tree | 30,931,861 B | 32,336,113 B | +1,404,252 B |
| Release-style APK | 227,552,479 B | 228,469,171 B | +916,692 B (+0.40%) |

The final static scene audit reports 51 maximum estimated draws, 384,747 maximum
visible triangles, 19 maximum visible materials, 296,322,278 estimated texture
bytes, and 18 scene transparent slots. All pass their current executable limits.
Those are whole-scene planning values, not physical Quest timing evidence.

## Visual evidence

Matched Phase 1 baseline directory:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_building_fidelity_v1_20260902_173655\candidate_tuned_035\visual_qa\screenshots`

Final Phase 2 directory:

`<artifact-root>\final_visual_qa\screenshots`

The following 1920 x 1080 pairs were inspected at full resolution:

- `pasv1_33_buildings_residential_low.png`
- `pasv1_12_building_facade.png`
- `pasv1_30_buildings_residential.png`
- `pasv1_11_building_roof_topdown.png`
- `pasv1_08_buildings_500m.png`
- `pasv1_29_buildings_cu_downtown.png`
- `pasv1_31_buildings_north_boulder.png`
- `pasv1_32_buildings_cu_facade.png`

Residential-low and facade-oblique show clear ridge, gable-end, compact-hip, and
short-ridge massing improvements. The broader residential view shows pitched
silhouettes across ordinary buildings. The 500 m airport/commercial view remains
appropriately dominated by flat roofs. No floating roofs, open seams, inverted
faces, broken facades, gable apertures, or roof-image discontinuities were found.
World-XZ roof imagery remains aligned, and Phase 1 corners remain restrained.

Standard Visual QA produced 125/125 required shots and classified
`PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`.

## Commands and results

| Command | Result | Evidence |
|---|---|---|
| Focused parent/current database and controller diff plus parsed counts | PASS | Phase 1 did not change coverage or residency |
| `run_quest_performance_budget.ps1 -SkipVisualQa -VisualQaReport <Phase1 report>` | PASS | `phase1_sanity\performance_budget` |
| Direct Unity failing-before legacy roof regression | EXPECTED FAIL | `legacy_roof_failure\results.xml` |
| `RebuildGeometryAndBundlesBatch` broad prototype | PASS, prototype over budget | `prototype_authoring\rebuild_geometry_bundles.log` |
| `run_production_visual_qa.ps1 ... -Width 1920 -Height 1080` broad prototype | PASS machine / inspected | `prototype_visual_qa` |
| `RebuildGeometryAndBundlesBatch` final v14 selective geometry/bundles | PASS | `selective_authoring_v14_first\rebuild_geometry_bundles.log` |
| `RefreshSerializedPageMetadataBatch` | PASS | `selective_metadata_refresh\refresh.log` |
| Direct targeted `FullMapBuildingClipmapEditModeTests` | PASS 19/19 | `final_targeted\results.xml` |
| `run_unity_tests.ps1 -TestPlatform EditMode` | PASS 376/376 | `final_full_editmode` |
| `run_unity_tests.ps1 -TestPlatform PlayMode` | PASS 22/22 | `final_full_playmode` |
| `run_production_visual_qa.ps1 ... -Width 1920 -Height 1080` final | PASS machine / inspected | `final_visual_qa` |
| `run_quest_performance_budget.ps1 -SkipVisualQa -VisualQaReport <final report>` | PASS | `final_performance_budget`; JSON records the exact final report path |
| `validate_product.ps1` | FAIL, inherited/pre-existing | `final_product_validation`; frozen source closure still contains two excluded and two unclassified legacy dependencies; roof paths were not implicated |
| `run_simulator_visual_gate.ps1` | BLOCKED/FAIL orchestration | `final_simulator_gate`; ten VRS recordings are absent and scratch copying also exhausted available disk before a player run |
| `build_production_quest.ps1` | PASS | `final_android_build` |
| `run_standalone_quest_gate.ps1 -ApkPath <exact final APK> -RunCount 3` | BLOCKED after run 1 | `final_physical_quest_gate`; a Headset Power/Guardian/controller dialog prevented readiness, so no measurement was accepted and runs 2–3 were not attempted |

## Android and physical Quest evidence

- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk` (ignored)
- Bytes: 228,469,171
- SHA-256: `a740ba3e49cd51396a53c7609caa6134061b043c719e5021465fa6d7194d303b`
- Build evidence: `<artifact-root>\final_android_build\product_apk_manifest.json`
- Shipping preflights: canonical Library PASS, maximum path 257/259 PASS,
  StreamingAssets transaction/restore PASS.

The formal gate installed this exact immutable APK on authorized Quest 3 serial
`2G0YC5ZG8907TD`. Run 1 detected a controller-required and/or Guardian system
dialog and classified `BLOCKED_HUMAN_ACTION_REQUIRED`. No crash, ANR, OOM, or
Unity exception was observed, but runtime readiness and app-PID timing markers
were absent. This attempt is not performance evidence. A valid retry requires
dismissing the Headset Power panel with Cancel, waking both Touch controllers,
completing Guardian, confirming the production cockpit is visible, and then
running all three unrecorded warm/measured sessions against the same APK.

## Known limitations

- This is a selective rollout: 7,065 otherwise-safe candidates retain the legacy
  topology solely because the unchanged conservative budget cannot admit them.
- Ambiguous, irregular, non-four-corner, exact-landmark, and unsupported roofs
  deliberately retain the safe fallback.
- No roof overhangs are included.
- Formal physical Quest frame, stale/tear, thermal, and worn comfort acceptance
  remains pending because the attempted gate was blocked by an in-headset dialog.
- The standard simulator gate remains unavailable for this milestone; no simulator
  timing claim is made.
- `docs/VALIDATION.md` is not updated while the formal hardware gate is pending.
