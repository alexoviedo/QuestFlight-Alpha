# Semantic Tower Replacement V1 — 2026-09-03

## Decision

`VISUALLY_ACCEPTED_HARDWARE_GATE_PENDING`

Two directly verified vertical structures are now authored under one serialized
`SemanticTowerRoot`: an FCC/OSM-confirmed guyed communications mast and an
FAA/OSM-confirmed industrial stack. Exact county building component `52188:0`
was removed because it incorrectly owned the stack footprint; its semantic
replacement is present at the same authority point. The communications site's
real equipment shelter (`45081`) is preserved. Machine visual QA passes, the
exact APK is installed and running on Quest 3, and the operator's short worn
observation is pending. This milestone does not claim physical timing, thermal,
or broad visual-fidelity acceptance.

## Starting state and artifact boundary

- Starting branch: `main`.
- Starting commit: `4e2e01ea9a52c6766e9e0f83ca6aed8548d7ac19`
  (`feat: add aligned reservoir watercraft`).
- Working branch: `feature/semantic-tower-replacement-v1`.
- External evidence root:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\semantic_tower_replacement_v1_20260903_175919`.
- Raw sources, downloaded authority data, orthophoto tiles, working Blender
  data, captures, logs, and APKs remain external and uncommitted.

## Authority and chronology

The existing product WGS84 AEQD transform and production terrain surface are
reused. No runtime geospatial parsing or placement occurs.

| Authority | Identity/date | SHA-256 or item ID |
|---|---|---|
| FAA Daily Digital Obstacle File | acquired `2026-09-03T18:00:15Z`; archive has no explicit edition field | `a8f9fa77d817b0b0584334211fa03207004db1287cd13ef27869b56f6ac632a4` |
| FCC ASR weekly complete registration database | source date `2026-08-30`; acquired `2026-09-03T18:05:23Z` | `a8c7593d91ce348b8d0b1d91cb5ddefe3b6587d8bfa8e2eeb1b3d01e3cdbf848` |
| OpenStreetMap Overpass vertical-structure extract | OSM base `2026-05-31T22:37:44Z`; acquired `2026-09-03T18:09:33Z` | `31e28a3edb2ead1accf4bef252f73dbf4fd8c88ec8dce073476e14c7709eed40` |
| City of Boulder 2020 Pictometry, 9-inch | spring/summer 2020; CC0 1.0 | ArcGIS item `22b7c93a74394696824258f807f345ba` |

The reproducible inventory records 91 FAA obstacles inside the exact 24 km
extent, 101 with the 500 m safety margin, and 25/26 relevant vertical
candidates at least approximately 15 m AGL. It also records 1,995 OSM vertical
elements, of which 26 overlap product building shells. Orthophoto chronology,
record type, and exact footprint ownership reduced that broad set to two
accepted structures and one exact shell exclusion. The inventory is
`authority\semantic_tower_candidate_inventory.json` in the external root.

## Accepted inventory

| Stable ID | Semantic type | Position authority | Height | Marking | Lighting | Building action |
|---|---|---|---:|---|---|---|
| `fcc-1024061-guyed-lattice` | guyed lattice communications mast | FCC ASR `1024061`, OSM node `8710525705`, 2020 imagery | 50.9 m AGL | aviation orange/white | FCC red-night system; top flashing plus two opposite midpoint steady fixtures; shipping daytime emitters off | preserve imagery-visible equipment shelter `45081`; no shell removed |
| `faa-08-000121-industrial-stack` | simple tapered industrial stack | FAA `08-000121`, OSM node `8747467531`, 2020 imagery | 372 ft / 113.3856 m AGL | none | FAA code `N`; unlit | remove exact county component `52188:0`, source `COUNTY:1N7026090001:26017` |

Accepted count by requested family:

- self-supporting lattice communications tower: 0;
- guyed mast: 1;
- cellular monopole: 0;
- meteorological tower: 0;
- water tower/tank: 0;
- other verified type: 1 industrial stack.

One red-only obstruction-light system and three opaque fixtures are authored.
Active shipping emitters are red 0, white 0, dual 0 because the product has a
fixed daytime presentation and the FCC requirement is red-night. An
editor-only diagnostic capture exercises the synchronized shader animation.
Fixture levels are a documented AC-consistent approximation; no Unity lights,
transparent flares, scripts per fixture, or per-eye randomness are used.

## Rejected and preserved candidates

- The remaining 24 FAA vertical candidates did not establish an exact false
  building-shell ownership transfer through matching position, type, 2020
  imagery, and chronology.
- Twenty-six OSM/building overlaps were examined. Most are legitimate base
  buildings, storage tanks, silos, or communications-site support buildings;
  broad deletion was rejected.
- Thirty-three extreme height/aspect components, largely named University of
  Colorado high-rise parts and legitimate industrial structures, are
  explicitly preserved.
- FAA `08-006048` is near the communications site but approximately 190 m from
  FCC ASR `1024061`; it is recorded only as nearby/unverified and is not used as
  position authority.
- No meteorological or elevated water-tower candidate passed exact-shell and
  2020 chronology checks, so no asset-driven structure was invented.

## Model sources and derivatives

| Source | Creator/license | Original SHA-256 | Result |
|---|---|---|---|
| Low Poly Cell Phone Tower, BlendSwap 27860 | papapig, CC0 1.0 | `4b740989bde14c363d7dcc641fb0b6d09a20c5a541d802a035a76d9913fea0bc` | sector panels used in communications LOD0 |
| Radio tower, Sketchfab UID `4b3c733d8bfd4ee2b91dcbcd02670686` | Gunnar Correa, CC BY 4.0 | `2b63a15017e6a014a7a1adecc41d66efab0dd8de4a85a62268480d0b2b9f2ef2` | modular lattice silhouette used in both LODs |
| Meteo Tower download | unused | `8e84358fc4966384d9a47e26c36f43b2d7975cb1695fd75d16dd840957bc6010` | rejected; file identifies BlendSwap 93545, not requested 22657, and no accepted met site exists |
| WaterTower.blend | unused | `656dcd01e4b7a04f352830bc124ca2060684ecdb64b89514e009219899778570` | rejected; no accepted water-tower site |
| 3d-model-watertower.fbx | unused | `2302629b06ae9c2cae84c94f29b5d46badb1549101d0469786520b82fcce18cc` | rejected; no accepted water-tower site |

Committed offline derivatives:

- `SemanticLatticeTowerV1.fbx` —
  `07909ff1cae1d3e652715c7d42ea3d1f27372e606fd485fa31c5822796ab41df`;
- `SemanticIndustrialStackV1.fbx` —
  `3e8ccc535454171a3f3d8ac5b661a72aef88516dc17782fe491720732fc5a266`.

Both derivatives are grounded, one-submesh meshes with LOD0/LOD1, vertex-color
surface semantics, no imported textures, animation, cameras, or source
materials. Unity triangulation is 3,920 selected LOD0 triangles total and 1,928
LOD1 triangles total. One shared opaque material/shader is used by four
renderers in two geographic LOD/culling groups. This bounded two-site structure
avoids a 24 km always-visible combined bound while retaining one serialized
root and no per-tower runtime behavior.

## Baseline versus final resource evidence

| Measure | Starting HEAD | Final candidate | Delta |
|---|---:|---:|---:|
| Represented ordinary building shells | 57,441 | 57,440 | -1 exact ownership transfer |
| Replacement semantic structures | 0 | 2 | +2 |
| Building base triangles, all pages | 613,389 | 613,373 | -16 |
| Building triangles after selected proper roofs | 636,641 | 636,625 | -16 |
| Semantic-tower selected LOD0 triangles | 0 | 3,920 | +3,920 |
| Semantic-tower total serialized LOD triangles | 0 | 5,848 | +5,848 |
| Whole-scene serialized triangles | 646,863 reconstructed from exact deltas | 652,695 measured | +5,832 net |
| Maximum measured standard Visual-QA draws | no same-run immutable baseline report | 53 | feature adds at most 2 visible draws at a site |
| Maximum measured standard Visual-QA visible triangles | no same-run immutable baseline report | 384,747 | bounded below 692,871 target |
| Active scene materials | 38 reconstructed from exact new material delta | 39 | +1 |
| Maximum visible materials | 19 reconstructed from exact new material delta | 20 | +1 |
| Textures / estimated texture bytes | 77 / 296,322,278 | 77 / 296,322,278 | 0 / 0 |
| Transparent material slots | 18 | 18 | 0 |
| APK | no immutable starting-HEAD APK was present | 228,823,760 bytes | final identity only |

The feature adds no texture, transparency, collider, rigidbody, realtime shadow,
reflection probe, Unity `Light`, or per-tower material instance. The final
static report classifies `PASS_STATIC_QUEST_BUDGET_HEADSET_MEASUREMENT_REQUIRED`.

## Visual evidence

All targeted captures are 1920x1080 and passed machine checks. Full-resolution
inspection found the lattice silhouette open, both structures grounded, the
industrial false shell absent, the real communications shelter retained, and
the 33 protected high-rise components intact.

- Authority overlay:
  `visual_qa\semantic_towers_authority_overhead.png`.
- Matched before/after:
  `validation\semantic_tower_visual_qa_final_candidate\screenshots\semantic_tower_false_building_before.png`
  and `semantic_tower_false_building_after.png`.
- Communications site and flight-distance views:
  `semantic_tower_communications_site.png`,
  `semantic_tower_pattern_altitude.png`, and
  `semantic_tower_03_lattice_stereo.png` in the same screenshot directory.
- Red-night diagnostic:
  `semantic_tower_light_night_diagnostic.png`.
- Targeted contact sheet:
  `validation\semantic_tower_visual_qa_final_candidate\visual_qa_contact_sheet.png`.
- Complete 130-view report:
  `final_manual\visual_qa\visual_qa_report.json`.

The matched defect is specifically a 6.5 m generic county shell incorrectly
covering a 113.4 m stack footprint. It is an exact semantic-ownership error,
not a claim that every visually extreme narrow building was false.

## Validation ledger

| Command/gate | Result | Evidence/qualification |
|---|---|---|
| Focused Semantic Tower EditMode | PASS, 7/7 | `validation\semantic_tower_editmode_final_focused.xml` |
| Targeted FullMap building tests | PASS, 19/19 | `validation\full_map_building_targeted_final.xml` |
| Flight-mode authoring idempotency after renderer integration | PASS, 1/1 | `validation\flight_modes_idempotency_after_tower_integration.xml` |
| Full EditMode, run once | FAIL, 387/389 | one initial feature integration failure was corrected and its focused rerun passed; remaining failure is inherited reservoir evidence hash drift (`a164597...` recorded versus unchanged placements SHA `eb8e333...`) |
| Full PlayMode, run once | PASS, 22/22 | `final_manual\playmode\unity_PlayMode_test_results.xml` |
| Targeted tower Visual QA | PASS, 9/9 | `validation\semantic_tower_visual_qa_final_candidate\visual_qa_report.json` |
| Production Visual QA, 1920x1080, run once | PASS, 130/130 machine views | `final_manual\visual_qa\visual_qa_report.json`; human exact-APK review pending |
| Static performance budget using exact final Visual-QA report | PASS | 53/77 draws, 384,747/692,871 visible triangles, 20/38 visible materials, 18/18 transparent slots |
| One-command product validation, run once | BLOCKED | feature dependency classification was corrected and its targeted exporter rerun passes; wrapper remains blocked by six inherited `StartupShowroom*.mat` unknown dependencies and therefore did not duplicate downstream gates |
| Android release-style build, run once | PASS | non-development Android ARM64/Vulkan/OpenXR build; native JSBSim required and UnityPrototype forbidden |
| Exact APK install and unattended launch | PASS | Quest 3 `2G0YC5ZG8907TD`; package PID `23393`; no app-scoped fatal error observed |
| Short worn visual check | NOT RUN / operator observation pending | exact installed APK is active; no result is inferred from Editor captures |
| Formal three-session Quest timing/thermal gate | NOT RUN | explicitly out of scope for this small content milestone |

Exact APK:

- external path:
  `final_manual\build\QuestFlight-production.apk`;
- bytes: `228823760`;
- SHA-256:
  `14a6bfd342bb945e5625f47be51317f5a51f0532597f6481fb98408f22609bb9`;
- package: `com.alexoviedo.t2.questflight`, version `0.1.0-alpha.1`.

## Remaining limitations

- The two accepted structures are deliberately a focused correction, not a
  complete infrastructure census.
- The red fixture arrangement is a low-cost data-driven approximation of the
  FCC-required family. Shipping daytime keeps the red-only system off.
- Physical Quest frame-time, thermal behavior, stale/tear counts, and sustained
  visibility are not measured by this milestone.
- The full validation wrapper retains pre-existing StartupShowroom dependency
  debt, and the full EditMode suite retains pre-existing reservoir evidence
  hash drift. Neither failure implicates tower authority, geometry, lighting,
  scene ownership, or the exact APK build.
- No immutable same-run starting-HEAD performance/APK artifact existed, so
  reconstructed baseline values are labelled rather than represented as a
  measured baseline run.
