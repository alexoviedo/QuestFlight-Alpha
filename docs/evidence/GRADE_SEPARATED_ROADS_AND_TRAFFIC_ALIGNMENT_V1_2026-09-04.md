# Grade-Separated Roads and Traffic Alignment V1 — Partial Progress Evidence

Date: 2026-09-04

Final classification: `PARTIAL_VERIFIED_SPANS_ONLY`

Human disposition: accepted as progress with visible limitations; this is not a claim that every road structure is visually finished.

## Git and scope

- Starting terrain-repair commit: `4983987f3226dbc4fdd1836fd1ecaa53cf7206e5`
- Starting `main`: `54ae8fc93a6ac752121e123347694d45f42733fd`
- Task commit: the commit containing this document, with subject `fix: align grade-separated roads and moving traffic`
- The task commit is intended to fast-forward local `main`; no remote was altered and no push is part of this acceptance.
- No terrain, imagery, road material, shader, building, tower, watercraft, vegetation, cockpit, flight-dynamics, traffic-route, speed, phase, count, or lane-assignment asset was changed.

## Root causes and bounded repair

The compiled road context discarded the OSM `bridge`, `layer`, `tunnel`, `covered`, `embankment`, `cutting`, `bridge:name`, `bridge:structure`, `lanes`, and `width` attributes. Consequently, the visible context could not distinguish upper road structures from terrain-following roads. V2 traffic independently sampled `ProductionTerrainSurfaceAsset`, added a universal 0.04 m root offset, and used yaw-only orientation. Raising only the visible road would therefore have left traffic at bare-earth height.

The repair preserves those source tags and adds one deterministic, serialized authority at:

`QuestFlight/Assets/Production/Environment/GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json`

Its SHA-256 is `1a7e3769c6767c02c63f54f8bd59f47aa203bb89ead5252dea948910b4c6fb36`. The same profile samples are consumed by:

1. `GradeSeparatedRoadV1Authoring`, which creates two serialized combined mesh batches;
2. `KbduTrafficV2Runtime`, which evaluates final lane-offset XZ, height, tangent, normal, and per-archetype contact seating;
3. focused EditMode tests and diagnostic/Visual-QA tooling.

No collider, raycast, physics query, per-vehicle object, runtime mesh generation, or per-frame bounds calculation was added. The old terrain-following imagery remains the visual approach surface and the opaque profile deck occludes only the accepted upper interval. The lower road and bare-earth terrain remain unchanged.

The final narrow correction adds CDOT structure `E-16-HW` (SH-157 ramp over US-36), which was absent from the frozen OSM bridge inventory but was retained as traffic way `41840966`. Its transition samples use the already-accepted production terrain transition patch at the south world boundary, eliminating the source mismatch that made that approach unnaturally depressed/jagged. This altered 60 E-16-HW height samples by at most 0.0907 m; all other 28 profiles remained byte-for-byte unchanged.

## Accepted profiles and reproduced coordinates

Coordinates are KBDU local tangent ENU/Unity metres (`+X` east, `+Z` north). The coordinate is the sampled bridge-interval midpoint; Y is the authored road surface. All vertical profiles use `CDOT_STRUCTURE_INVENTORY_MINIMUM_CLEARANCE` and the 2020 scene-imagery chronology recorded in the authority.

| Span ID | Midpoint X, Z, Y (m) | Clearance (m) | Max grade |
|---|---:|---:|---:|
| `KBDU-GSR-D-15-AZ-OSM-17020444` | -2763.2, -4335.2, 25.77 | 4.9784 | 1.9061% |
| `KBDU-GSR-D-15-BO-OSM-41994784` | -2712.8, -4336.5, 24.50 | 5.0292 | 1.7364% |
| `KBDU-GSR-D-16-CU-OSM-43250432` | -1593.5, -1824.3, -2.97 | 5.1308 | 2.7637% |
| `KBDU-GSR-D-16-CU-OSM-43250434` | -1605.6, -1829.2, -2.97 | 5.1308 | 2.7837% |
| `KBDU-GSR-D-16-CX-OSM-43417681` | -1517.7, -2200.8, -1.74 | 6.6294 | 4.0370% |
| `KBDU-GSR-D-16-CX-OSM-43417686` | -1531.3, -2209.1, -1.74 | 6.6294 | 4.0100% |
| `KBDU-GSR-D-16-DF-OSM-43098445` | -1605.0, -122.6, 12.12 | 5.1308 | 3.4900% |
| `KBDU-GSR-D-16-DF-OSM-43098448` | -1592.4, -117.6, 12.12 | 5.1308 | 3.5799% |
| `KBDU-GSR-D-16-DL-OSM-43098883` | -1546.3, 325.7, 11.48 | 5.2070 | 2.5944% |
| `KBDU-GSR-E-16-FB-OSM-41840658` | -717.7, -5872.4, 23.70 | 4.8514 | 3.0257% |
| `KBDU-GSR-E-16-FB-OSM-41840720` | -732.9, -5863.5, 23.70 | 4.8514 | 2.9500% |
| `KBDU-GSR-E-16-HW-OSM-41840966` | -534.5, -5980.5, 23.28 | 4.7752 | 3.1718% |
| `KBDU-GSR-E-16-HX-OSM-41841019` | -473.2, -5845.3, 22.90 | 5.4864 | 3.5628% |
| `KBDU-GSR-E-16-IB-OSM-40112456` | 8944.6, -11884.1, 31.63 | 5.3848 | 2.6400% |
| `KBDU-GSR-E-16-IB-OSM-40112476` | 8925.7, -11903.8, 31.63 | 5.3848 | 2.8937% |
| `KBDU-GSR-E-16-IB-OSM-630063729` | 8931.9, -11899.5, 31.63 | 5.3848 | 2.6500% |
| `KBDU-GSR-E-16-IB-OSM-630063797` | 8939.2, -11889.9, 31.63 | 5.3848 | 2.6000% |
| `KBDU-GSR-E-16-KZ-OSM-172691044` | 5178.9, -9061.2, 66.13 | 5.1054 | 2.6800% |
| `KBDU-GSR-E-16-KZ-OSM-172691046` | 5160.6, -9051.5, 66.13 | 5.1054 | 2.7584% |
| `KBDU-GSR-E-16-QI-OSM-427829910` | 8432.2, -11631.4, 29.50 | 5.6642 | 3.7300% |
| `KBDU-GSR-E-16-QI-OSM-52134594` | 8452.2, -11609.1, 29.50 | 5.6642 | 3.9897% |
| `KBDU-GSR-E-16-QI-OSM-52134598` | 8442.3, -11591.4, 29.50 | 5.6642 | 3.9100% |
| `KBDU-GSR-E-16-QT-OSM-41443111` | 1132.8, -6984.2, 46.86 | 5.1308 | 5.8572% |
| `KBDU-GSR-E-16-QU-OSM-46218442` | 6757.1, -10401.5, 61.01 | 5.0800 | 2.8364% |
| `KBDU-GSR-NWP-05.95B-OSM-172094965` | 11609.9, -8970.8, -12.68 | 5.3848 | 3.4300% |
| `KBDU-GSR-NWP-05.95B-OSM-172094968` | 11610.2, -8946.9, -12.68 | 5.3848 | 3.5465% |
| `KBDU-GSR-NWP-06.25A-OSM-172094966` | 11169.1, -8993.9, -5.91 | 5.5372 | 3.8647% |
| `KBDU-GSR-NWP-06.25B-OSM-172094967` | 11121.6, -8973.9, -6.51 | 4.9022 | 3.4963% |
| `KBDU-GSR-NWP-06.98-OSM-180720982` | 9979.0, -9181.8, 3.65 | 5.2324 | 1.2574% |

Authority-wide affected XZ bounds are `[-2789.0346, 11954.4405] x [-11999.0, 686.333]` m. No surface samples outside the 29 declared profiles were rewritten.

For the final E-16-HW profile specifically:

- profile bounds: X `[-536.9377, -335.0680]`, Z `[-6191.7300, -5644.1275]` m;
- profile length: 674.9855 m; elevated interval: `[300.0, 374.9855]` m;
- crown: 23.2774 m; endpoints: 13.763 m and 19.704 m;
- CDOT published minimum clearance: 4.7752 m; modeled underside clearance: 4.8752 m (0.10 m conservative margin);
- deck thickness: 0.60 m; surface width: 7.0 m; maximum grade: 3.1718% / 1.8167 degrees;
- upper OSM way: `41840966`; approach ways: `41840963`, `41840964`, `17014937`, `41840965`; lower ways: `130922338`, `318130765`.

## Rejected candidates

The frozen inventory contained 130 OSM upper-bridge ways. Twenty-eight ways became the original 17 accepted structures; E-16-HW was then added from the retained traffic centerline and CDOT evidence. The other 102 OSM ways were deliberately rejected rather than guessed:

- 79 `NO_UNAMBIGUOUS_LOWER_ROAD`: `16981899, 41279056, 41320235, 41746779, 41746786, 41746789, 41841409, 41841773, 41869481, 41981295, 41981337, 41983901, 41983909, 42240635, 42240637, 42240701, 43007288, 43007616, 43007618, 43007619, 43007633, 43007639, 43007778, 43007907, 43008291, 43100961, 43100963, 43176579, 43176616, 43176641, 43176674, 43176756, 43176776, 43176779, 43176838, 43176847, 43176862, 43176895, 43177025, 43181539, 43181560, 43182317, 43195346, 43196057, 43196122, 43196128, 43196721, 43337067, 43359992, 43417343, 43417357, 43417479, 43417514, 45979010, 46145114, 46145973, 52134577, 52134579, 87899629, 89781461, 90291033, 90291034, 190798898, 222081036, 327113094, 398067391, 521418476, 550937634, 630087855, 630087862, 630087895, 806265924, 810382282, 810382284, 823194966, 858818900, 914035430, 914035432, 1293770612`.
- 23 `NO_UNAMBIGUOUS_LOWER_ROAD` plus `NO_MATCHING_CDOT_STRUCTURE_WITHIN_100M`: `17017789, 45978891, 45980170, 46094639, 46195614, 46195616, 46195628, 46211155, 46226171, 46331124, 47105229, 194216322, 230780922, 258987299, 326944103, 420432639, 420432640, 566231379, 628621452, 896372369, 1113429628, 1211964988, 1491374435`.

Exact source tags, bounds, nearby CDOT records, and route candidates are retained externally in `baseline/grade_separated_road_inventory_v1.json` under the artifact root below.

## Traffic contact and invariance

The six active serialized vehicle meshes all have their lowest vertex at local Y = 0, so the former universal 0.04 m lift was removed. Serialized root-to-road offsets are `[0, 0, 0, 0, 0, 0]` m. Serialized contact half-lengths, in archetype order, are `[2.300, 2.175, 2.350, 2.675, 2.525, 8.100]` m and match the current mesh Z extents within 1 mm.

The runtime evaluates rear/center/front road samples over the contact half-length, seats the root on the support chord without allowing the center sample to penetrate, and derives bounded 3D orientation from the shared surface. Focused tests bound pitch to 3.5 degrees; the maximum authored profile grade is 5.8572% (3.353 degrees). No independent traffic curve exists.

The focused regression fixture verifies that outside accepted intervals XZ, distance, phase, speed, route identity, vehicle count, active-instance ordering, and lower-road ownership remain unchanged. Ordinary-road Y changes only by the proven removal of the incorrect 0.04 m offset. Deck vertex and traffic evaluation share the authority and agree within the serialized 0.01 m tolerance. The existing instanced draw/culling path remains in use and no runtime allocation/physics path was added.

## Resource delta

Final authoring report: `candidate/authoring_e16_hw_transition_aligned/grade_separated_road_authoring_report.json`

- authority profiles: 29 across 18 CDOT structures;
- serialized combined renderers/draws: 2 / +2;
- triangles: +6,454 (near 3,480; outer 2,974);
- materials: +0 (existing `KBDU_TerrainPyramid.mat` reused);
- textures: +0;
- transparent materials: +0;
- runtime components: +0;
- per-bridge GameObjects/scripts: 0;
- runtime mesh generation, raycasts, colliders, and physics queries: 0.

The +2 draw result exceeds the original “no more than two draws” target but exceeds the approximate 5,000-triangle preference by 1,454 triangles. It is accepted only as partial progress, not as final optimization acceptance.

## Captures and human review

External artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-grade-separated-roads-artifacts\grade_separated_roads_v1_20260903_185322`

Final matched still/sequence report:

`candidate/visual_qa_e16_hw_transition_aligned/grade_separated_road_visual_qa.json`

It contains full-resolution matched baseline/candidate stills for:

- `01_obvious_47th_overpass`
- `02_foothills_over_diagonal`
- `03_overhead_upper_lower_alignment`
- `04_low_oblique_clearance`
- `07_lower_and_upper_traffic`
- `08_ordinary_graded_road_contact`
- `09_unaffected_at_grade_crossing`
- `10_unaffected_ordinary_route`
- `11_cdot_e16_hw_unmodeled_ramp`
- `12_cdot_e16_pq_unmodeled_emma`
- `13_cdot_e16_zj_unmodeled_ramps`

The exact final E-16-HW still is `candidate/visual_qa_e16_hw_transition_aligned/stills/candidate/11_cdot_e16_hw_unmodeled_ramp.png`; the paired baseline is in the adjacent `stills/baseline` directory. The final image shows the ramp elevated over both US-36 carriageways with the lower highway retained and no obvious duplicate ground-level deck.

Moving matched sequences are:

- `candidate/visual_qa_e16_hw_transition_aligned/sequences/{baseline,candidate}/05_sedan_approach_span_departure/` (archetype 0, route `canonical:city_street_centerlines:7116:0:0:0`);
- `candidate/visual_qa_e16_hw_transition_aligned/sequences/{baseline,candidate}/06_longest_truck_approach_span_departure/` (archetype 5, route `canonical:city_street_centerlines:2952:0:0:0`).

The exact release-style APK was installed on Quest serial `2G0YC5ZG8907TD` with `adb install -r`, launched as `com.alexoviedo.t2.questflight/com.unity3d.player.UnityPlayerGameActivity`, and observed running as PID 19720. The operator's headset judgment was: still far from perfect, but acceptable as progress. No dedicated scripted moving capture exists for E-16-HW itself, and the headset judgment is therefore recorded as `ACCEPTED_AS_PROGRESS_WITH_VISIBLE_LIMITATIONS`, not a clean visual acceptance.

## Commands and results

- `python -m py_compile tools/kbdu_environment/process_kbdu_environment.py tools/kbdu_traffic/build_grade_separated_road_authority.py tools/kbdu_traffic/inventory_grade_separated_roads.py` — passed.
- `python -m unittest tools.kbdu_environment.tests.test_kbdu_pipeline -v` — 9 passed, 0 failed.
- Focused Unity EditMode result `candidate/focused-e16-hw-transition-aligned.xml` — 7 passed, 0 failed, 0 skipped, 7.4149 s.
- Deterministic authority regeneration — exact SHA-256 match `1a7e3769c6767c02c63f54f8bd59f47aa203bb89ead5252dea948910b4c6fb36`.
- Final serialized authoring — 29 profiles, 2 renderers, 6,454 triangles, no new material/texture/runtime component.
- Final targeted Visual QA — full-resolution matched stills plus sedan/truck moving sequences inspected; E-16-HW improved but human review records remaining imperfection.
- Production release-style Android build — succeeded; package `com.alexoviedo.t2.questflight`; development build false; native JSBSim required; APK size 229,000,111 bytes; SHA-256 `d8c2c34e9db4d8c5ac7bfa4b37f7d0e8aeac5587e102436669cf657a24cd5761`.
- Exact-APK headset smoke — install and launch succeeded; human accepted partial progress with limitations.
- `python -m pytest ...` was unavailable because the local Python installation has no `pytest`; the same test module passed through its native `unittest` runner.

The expensive full EditMode, full PlayMode, complete product validation, and repeated Android builds were intentionally not run after the operator prioritized early headset review and accepted this as partial progress. This milestone must not be represented as `FIXED_OVERPASSES_AND_TRAFFIC_ALIGNMENT`.

## Changed source and serialized product files

- `tools/kbdu_environment/process_kbdu_environment.py`
- `tools/kbdu_environment/tests/test_kbdu_pipeline.py`
- `tools/kbdu_traffic/inventory_grade_separated_roads.py`
- `tools/kbdu_traffic/build_grade_separated_road_authority.py`
- `QuestFlight/Assets/Scripts/Environment/KbduGradeSeparatedRoadAuthorityV1.cs` and `.meta`
- `QuestFlight/Assets/Scripts/Environment/KbduTrafficV2Runtime.cs`
- `QuestFlight/Assets/Editor/GradeSeparatedRoadV1Authoring.cs` and `.meta`
- `QuestFlight/Assets/Editor/Tests/GradeSeparatedRoadDiagnosticRunner.cs` and `.meta`
- `QuestFlight/Assets/Editor/Tests/GradeSeparatedRoadV1EditModeTests.cs` and `.meta`
- `QuestFlight/Assets/Editor/Tests/GradeSeparatedRoadVisualQaBatchRunner.cs` and `.meta`
- `QuestFlight/Assets/Production/Environment/GradeSeparatedRoadsV1/` (authority data, two generated meshes, and paired `.meta` files)
- `QuestFlight/Assets/Scenes/ProductionVerticalSlice.unity`
- this evidence document.

## Known limitations retained for follow-up

- This is a bounded partial milestone, not a complete reconstruction of every bridge, ramp, fascia, guardrail, or approach earthwork.
- Some profile/approach silhouettes remain visually simplified and were explicitly judged “far from perfect.”
- The 102 rejected source ways remain terrain-following because upper/lower ownership or matching vertical authority was not proven.
- Diagnostic views identify additional public-record structures such as E-16-PQ and E-16-ZJ, but they were not accepted into this authority without an unambiguous route/chronology fit.
- E-16-HW has a final matched still and headset observation but no dedicated scripted vehicle traversal sequence.
- Full combined regression and performance acceptance remain future work.

These limitations are the reason for the final classification `PARTIAL_VERIFIED_SPANS_ONLY`.
