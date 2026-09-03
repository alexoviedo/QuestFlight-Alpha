# Full-map building fidelity V1

## Evidence status

This document records evidence collected on 2026-09-02 for the
`Full-Map Building Fidelity V1` milestone.

- Phase 1 implementation: **COMPLETE** at a final authored corner width of
  0.35 m and strength of 0.65.
- Phase 1 visual disposition: **ACCEPTED FOR THE CURRENT MILESTONE**. The user
  wore a Quest 3 running the exact final APK and said the result was acceptable
  for now, with further fidelity work deferred. This is deliberately modest:
  the user rejected the initial 0.18 m / 0.62 tune because they did not notice
  a change.
- Phase 1 validation: focused EditMode **PASS 12/12**, full EditMode **PASS
  369/369**, PlayMode **PASS 22/22**, deterministic authoring and bundles
  **PASS**, matched machine Visual QA **PASS**, static Quest planning budget
  **PASS**, and Android release-style build **PASS**.
- Physical performance disposition: **NOT ACCEPTED AS A PERFORMANCE GATE**.
  A formal three-run gate on the earlier 0.18/0.62 APK failed the existing
  72 Hz timing/stale-frame thresholds. The final APK has an opportunistic worn
  live snapshot, not a repeated warm/measurement/thermal A/B.
- Coverage and residency preservation: **PASS** in deterministic mesh
  comparison and the final APK's live Quest runtime contract. No represented
  building, page, protected base cell, forward slot, vertex, or triangle was
  removed.
- Product validation: **FAIL, INHERITED/PRE-EXISTING** at dependency closure
  because six unrelated `StartupShowroom*.mat` assets are unclassified.
- Simulator gate: **BLOCKED** by missing recorded replays and the Windows
  player's inability to load shipping Android AssetBundles.
- Phase 2 proper roof topology: **DEFERRED / NOT STARTED**. The user chose to
  wrap Phase 1 and tackle further enhancements later. No Phase 2 geometry or
  budget allocation is included.

`README.md` and `docs/VALIDATION.md` remain unchanged because the complete
product and formal physical-performance gates are not green. The user's
acceptance authorizes retaining this scoped improvement; it is not a claim
that the broader Quest performance baseline now passes.

## Source state and scope

- Starting branch: `main`.
- Starting commit: `98532b050f862e1420b62c4edbbf79a5bbddba7d`.
- Starting `git status --short`: empty; there were no pre-existing uncommitted
  changes.
- Shipping target remains standalone Meta Quest 3, Android ARM64, Vulkan,
  OpenXR, and single-pass stereo.
- No branch change, push, pull request, or remote mutation was made during
  implementation and evidence collection.
- Generated meshes, AssetBundles, reports, captures, logs, and APK output are
  ignored or external artifacts and are not added to Git.

The change is limited to offline full-map building authoring, the existing
building shader and material, serialized exact building-budget metadata,
focused EditMode coverage, and this evidence record. It adds no runtime mesh
generation and no per-building object, component, renderer, material property
block, or behavior.

## Audit findings and root causes

### Current building and residency contract

The current serialized database represents 57,441 buildings, not the 57,407
count in older historical evidence. It contains 576 pages, 483 of them
nonempty. Runtime residency remains a fixed 24-slot pool: a protected
contiguous 4-by-4 full-height base window plus eight connected forward pages.

The generated meshes contain 57,441 buildings, 1,354,653 vertices, and
613,389 triangles. Stable-footprint inspection found 6 three-vertex shells,
52,749 four-vertex shells, 17 five-vertex shells, 3,914 six-vertex shells, and
755 shells above six vertices. Thus 52,755 shells have at most four vertices.
Ordinary streamed buildings are dominated by reduced four-corner footprints.

### Razor-edged wall lighting

Every wall is emitted as its own duplicated four-vertex quad with one flat
geometric normal. Adjacent convex faces therefore meet at an instantaneous
normal discontinuity even though the existing wall shader already supplies
semantic facade albedo, sampled facade normals, MRAO, aperture details, and
window reflections. This flat-normal discontinuity is the confirmed Phase 1
root cause.

The alpha audit found `Color32.a == 255` on all 1,354,653 baseline building
vertices. The building shader forwarded vertex color but consumed only RGB as
facade tint; no building authoring, shader, serialization, capture, or debug
consumer depended on alpha. Alpha was therefore safe for wall-corner metadata
without a new vertex stream or interpolator. RGB remains byte-for-byte
unchanged.

### The 233,298 versus 233,316 discrepancy

The actual baseline meshes produce a conservative top-24 page total of
233,298 building triangles, below the unchanged 235,000 ceiling. The previous
report and controller default recorded 233,316.

The 18-triangle discrepancy came from two coordinate frames in offline
tooling. Page-mesh generation compiled footprints after adding page world
offsets, while report generation recompiled page-local coordinates. The old
report therefore did not exactly describe the emitted stable-footprint
decisions. Report generation now uses the same world-offset input as mesh
generation, and serialized controller/product metadata records the measured
233,298. Geometry was not changed to reproduce the stale value, and the
ceiling was not raised.

The corrected report retains legacy bucket field names but records the actual
emitted grouping: 52,755 shells with at most four vertices, 3,931 with five or
six vertices, and 755 above six vertices.

### Roof-topology hypothesis

The ordinary compiler begins from `lod2_q`, evaluates `RoofHeight` only at
retained footprint-boundary vertices, and triangulates only those vertices. A
rectangular four-corner footprint has no interior vertex from which to form a
gable ridge, hip apex, or short ridge. The source-level topology hypothesis is
therefore confirmed.

The database contains 28,734 nominal pitched roofs: 23,656 gable and 5,078
hip. An initial safe-four-corner audit identified 10,149 raw candidates: 8,764
gable and 1,385 hip. Only 1,702 triangles of conservative selected-window
headroom remain under the unchanged 235,000 ceiling, so a global proper-roof
rollout cannot be assumed safe. The required executable topology proof, exact
per-page formulas, allocation, geometry, tests, and captures are all **NOT
RUN** because Phase 2 was deferred. No roof path changed.

## Phase 1 implementation

### Deterministic offline corner encoding

`FullMapBuildingCornerEncoding` is a pure deterministic helper exercised by
offline authoring and EditMode tests.

- For each corner it calculates normalized incoming/outgoing directions,
  signed cross, dot, and `cross / (1 + dot)`, equal to
  `tan(directionTurn / 2)`.
- Polygon signed area normalizes convexity for either source winding.
- Encoding is disabled for invalid/ambiguous polygons, effectively zero-area
  winding, concave turns, incident edges shorter than 0.8 m, duplicate points,
  direction turns below 10 degrees (half-turn tangent `0.08748866`), non-finite
  data, or unsafe magnitude above 1.75.
- `Color32.a` is split into independent four-bit fields. The low nibble is the
  wall start and the high nibble the wall end. Code 0 disables an endpoint;
  codes 1 through 14 decode as `code / 8`; code 15 is reserved.
- A 90-degree turn encodes and decodes to exactly 1.0. Maximum scalar
  quantization error is 0.0625. The non-right-corner test bounds adjacent-face
  angular disagreement below 7.1 degrees; a 90-degree corner converges within
  0.01 degree.
- Legacy all-white alpha `0xff` decodes as disabled so an old mesh fails
  visually neutral rather than appearing maximally bent.
- Start contribution is negative and end contribution positive in wall-local
  tangent convention. All four duplicated wall vertices receive the same
  packed byte, yielding zero interpolation/reconstruction error.
- RGB tint is copied byte-for-byte. Roof vertices retain alpha 255 and remain
  on the unchanged roof path.

The compiler identifier is
`full-height-forward-residency-corner-softening-compiler-v12-20260902`.

### Existing-shader analytic bevel

The existing opaque building shader decodes and applies metadata only in its
wall branch, using existing metric wall-local `detailUv.x` and serialized wall
length to form separate start/end masks.

- Final authored width: **0.35 m**.
- Final authored strength: **0.65**.
- Width cap: 20 percent of wall length, preventing two shoulders from
  consuming a short wall.
- `fwidth(detailUv.x)` anti-aliases the transition, and
  `smoothstep(0.65, 1.25, width / pixelMeters)` removes truly subpixel detail.
- Sampled micro-normal is attenuated only inside the narrow corner envelope.
  The signed precomputed turn is applied to tangent-space normal data before
  the existing tangent-to-world transform and normalization.
- Foundation sealing can tilt serialized wall tangents vertically, so the
  shader reconstructs a horizontal wall tangent and expresses it in the
  existing tangent/bitangent basis before applying the turn.
- A draw-uniform outer branch makes zero width or strength an exact no-op for
  controlled profiling. It is outside derivative-dependent work and adds no
  keyword variant.
- The mathematical edge reaches the shared approximate outward bisector;
  strength controls the shoulder, not silhouette displacement.

The initial 0.18 m / 0.62 tune with a
`smoothstep(0.75, 1.50, ...)` projected fade passed machine checks but was too
subtle in a worn Quest check. Width and fade retention were increased in
measured iterations, finishing at 0.35 m / 0.65 after direct user acceptance.
The 20-percent short-wall cap and derivative fade remain in place.

Phase 1 adds only ALU/derivative work and two ordinary material floats. It adds
no triangle, vertex, vertex stream, interpolator, trigonometric fragment
operation, texture, texture sample site, keyword, variant, pass, blend mode,
clip/discard, dither, renderer, material, transparent slot, or draw. The roof
branch, world-XZ orthophoto projection, facade apertures, windows, reflections,
and facade texture paths remain intact.

## Baseline-versus-final contract

| Metric | Baseline | Final Phase 1 | Evidence/status |
| --- | ---: | ---: | --- |
| Represented buildings | 57,441 | 57,441 | PASS, report/mesh/live runtime |
| Total pages | 576 | 576 | PASS |
| Nonempty pages | 483 | 483 | PASS |
| Active building renderer slots | 24 | 24 | PASS, serialized plus live Quest contract |
| Protected base / forward extension | 4x4 / 8 | 4x4 / 8 | PASS, unchanged selection tests |
| Generated page meshes | 576 | 576 | PASS |
| Generated vertices | 1,354,653 | 1,354,653 | PASS |
| Generated triangles | 613,389 | 613,389 | PASS |
| Maximum selected building triangles | 233,298 | 233,298 | PASS; ceiling remains 235,000 |
| Building renderer/draw slots | 24 | 24 | No renderer or draw added |
| Full-scene maximum estimated draws | 51 | 51 | PASS static budget and machine Visual QA |
| Building materials | 1 opaque | 1 opaque | No material added |
| Full-scene visible / active distinct materials | 19 / 38 | 19 / 38 | PASS; planning limit remains 38 |
| Building-owned textures | 4 | 4 | No texture added |
| Building-owned estimated texture memory | 8,197,344 bytes | 8,197,344 bytes | Assets unchanged |
| Full-scene active textures | 77 | 77 | PASS static inventory |
| Full-scene estimated active texture memory | 296,322,278 bytes | 296,322,278 bytes | Under 307,507,088-byte limit |
| Building transparent slots | 0 | 0 | No transparency added |
| Full-scene transparent slots | 18 | 18 | PASS; no Phase 1 slot added |
| Building shader texture samples | Not reliably hardware-profiled | No source sample site added | Exact hardware sample cost not claimed |
| Generated bundle payload | 30,337,292 bytes | 30,737,388 bytes | +400,096 bytes (+1.32%) from nonuniform alpha |
| Complete generated bundle tree | 30,531,729 bytes / 584 files | 30,931,861 bytes / 584 files | External inventory; 144 payload bundles |

Baseline and final index SHA-256 values are identical:
`b0e394b11cfb95646dd33823e13797274e64b1505a02c8a37cf41252657d9ca3`.
The complete vertex stream excluding color alpha is also identical:
`d884fb15a4ef07895aa4745cb08fb06845134a1891938690d7ea8f1791ae7808`.
This proves that positions, indices, normals, tangents, UVs, parameters, RGB,
topology, selection, and counts were preserved; only alpha metadata changed.

The final meshes have 242,757 wall quads / 971,028 wall vertices and 383,625
roof vertices. All roof alpha remains 255; no wall uses the legacy `0xff`
sentinel. There are 230,740 enabled shared endpoints in each endpoint
direction, and 239,496 wall quads have at least one encoded corner. Repeated
generation produced identical encoded output.

## Static full-scene budget

Baseline, the first candidate, and final 0.35 candidate all returned
`PASS_STATIC_QUEST_BUDGET_HEADSET_MEASUREMENT_REQUIRED`.

| Full-scene static metric | Baseline | Final 0.35 |
| --- | ---: | ---: |
| Maximum estimated active draws | 51 / 77 ceiling | 51 / 77 |
| Maximum estimated visible triangles | 381,713 / 692,871 | 381,713 / 692,871 |
| Visible material count | 19 | 19 |
| Active distinct material count | 38 / 38 planning limit | 38 / 38 |
| Active texture count | 77 | 77 |
| Estimated active texture memory | 296,322,278 / 307,507,088 bytes | same |
| Transparent material slots | 18 / 18 planning limit | 18 / 18 |
| Active renderers | 122 | 122 |
| Serialized scene mesh triangles | 642,104 | 642,104 |
| Shader assets / keyword combinations | 22 / 27 | 22 / 27 |

Final report: `<artifact-root>\candidate_tuned_035\performance_budget`.

## Visual evidence and tuning disposition

Baseline and final machine Visual QA each produced 125 full-resolution
captures at 1920 by 1080 using matched deterministic camera transforms. The
final candidate returned `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`;
all 125 shots passed machine checks with 51 maximum estimated draws and
381,713 maximum estimated visible triangles.

The initial 0.18/0.62 candidate preserved facade, window, roof, and silhouette
paths in inspected captures, but its corner-only response was too small for the
user to notice while wearing the headset. It was therefore visually rejected
despite passing technical checks. The final 0.35/0.65 APK was built, installed,
and worn; the user accepted it as adequate for this milestone and deferred
further enhancement. This does not claim the effect is dramatic or that real
bevel geometry was added.

The batch report records `NOT_RUN_NO_USER_AVAILABLE` because batch Visual QA
cannot infer a wearer. Separate live Quest evidence and the direct user
statement provide the human visual disposition; neither promotes the failed
formal physical timing gate to a pass.

Matched full-resolution directories:

- Baseline: `<artifact-root>\baseline\visual_qa\screenshots`.
- Initial 0.18/0.62 candidate:
  `<artifact-root>\candidate\visual_qa\screenshots`.
- Final 0.35/0.65 candidate:
  `<artifact-root>\candidate_tuned_035\visual_qa\screenshots`.

Most revealing matched files within those directories:

- `pasv1_08_buildings_500m.png`
- `pasv1_11_building_roof_topdown.png`
- `pasv1_12_building_facade.png`
- `pasv1_29_buildings_cu_downtown.png`
- `pasv1_30_buildings_residential.png`
- `pasv1_31_buildings_north_boulder.png`
- `pasv1_32_buildings_cu_facade.png`
- `pasv1_33_buildings_residential_low.png`
- `30b_takeoff_left_buildings.png`

Derived tuning crops are under `<artifact-root>\candidate\visual_inspection`
and `<artifact-root>\candidate_tuned\visual_inspection`. They do not replace
inspection of the original-resolution captures.

## Test and validation commands

| Command or gate | Status | Observed result/evidence |
| --- | --- | --- |
| `git status --short` before editing | PASS | Empty output |
| `git rev-parse HEAD` | PASS | `98532b050f862e1420b62c4edbbf79a5bbddba7d` |
| `git branch --show-current` | PASS | `main` |
| Baseline focused EditMode command below | PASS | 7/7, 15.61 s; `<artifact-root>\baseline\targeted_editmode\results.xml` |
| Final 0.35 focused FullMapBuildingClipmap EditMode command below | PASS | 12/12, 15.4209965 s; `<artifact-root>\candidate_tuned_035\targeted_editmode\results.xml` |
| `.\scripts\run_unity_tests.ps1 -TestPlatform EditMode` | PASS | 369/369, 237.7218483 s; `<artifact-root>\candidate_tuned_035\full_editmode\unity_EditMode_test_results.xml` |
| `.\scripts\run_unity_tests.ps1 -TestPlatform PlayMode` | PASS | 22/22, 25.998358 s; `<artifact-root>\candidate_tuned_035\playmode\unity_PlayMode_test_results.xml` |
| Offline `RebuildGeometryAndBundlesBatch` command below | PASS | Geometry plus 144 bundle payloads rebuilt; log says `GEOMETRY/BUNDLES SUCCESS` |
| Offline `RefreshSerializedPageMetadataBatch` command below | PASS | Serialized maximum refreshed to 233,298 |
| Deterministic mesh contract comparison | PASS | Counts and invariant hashes match baseline; only alpha differs |
| `.\scripts\run_production_visual_qa.ps1 ...` | PASS with headset requirement | `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`, 125/125 at 1920x1080; `<artifact-root>\candidate_tuned_035\visual_qa` |
| `.\scripts\run_quest_performance_budget.ps1 ...` | PASS with headset requirement | `PASS_STATIC_QUEST_BUDGET_HEADSET_MEASUREMENT_REQUIRED`; `<artifact-root>\candidate_tuned_035\performance_budget` |
| `.\scripts\validate_product.ps1 -ArtifactDir <artifact-root>\candidate\product_validation` | FAIL, inherited/pre-existing | Dependency closure stopped on six unrelated unclassified `StartupShowroom*.mat` assets; building paths were not implicated |
| `.\scripts\run_simulator_visual_gate.ps1 -ArtifactRoot <artifact-root>\candidate\simulator -RebuildPlayer` | BLOCKED | Player build passed, but ten VRS replays were absent; live Windows session rejected Android aerial/building bundles |
| `.\scripts\build_production_quest.ps1 ...` | PASS | Release-style APK 227,552,479 bytes; SHA-256 below |
| Initial `.\scripts\run_standalone_quest_gate.ps1 ... -RunCount 3 -Unattended` | FAIL, attempted | `FAIL_THREE_RUN_QUEST_GATE`; exact earlier APK immutable across runs |
| Final 0.35 exact-APK worn live assessment | ACCEPTED VISUALLY / NOT A FORMAL TIMING GATE | Installed hash matched, runtime contract passed, user accepted current result |
| Final 0.35 repeated formal standalone Quest gate | NOT RUN | Live snapshot does not replace repeated warm/measurement/thermal protocol |
| Phase 2 tests, allocation, authoring, captures, and hardware budget | NOT RUN | Deferred by scope decision |

Focused EditMode invocation:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe' `
  -batchmode `
  -projectPath 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight' `
  -runTests `
  -testPlatform editmode `
  -testFilter QuestFlight.Editor.Tests.FullMapBuildingClipmapEditModeTests `
  -testResults '<artifact-directory>\results.xml' `
  -logFile '<artifact-directory>\unity.log'
```

Offline authoring invocations:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight' `
  -executeMethod QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.RebuildGeometryAndBundlesBatch `
  -logFile '<artifact-root>\candidate\authoring\final_rebuild_geometry_bundles.log'

& 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight' `
  -executeMethod QuestFlight.Editor.FullMapBuildingClipmapAssetAuthoring.RefreshSerializedPageMetadataBatch `
  -logFile '<artifact-root>\candidate\authoring\refresh_serialized_metadata.log'
```

Final Visual QA, static budget, and Android build:

```powershell
.\scripts\run_production_visual_qa.ps1 `
  -ArtifactDir '<artifact-root>\candidate_tuned_035\visual_qa' `
  -Width 1920 -Height 1080 -TimeoutSeconds 900

.\scripts\run_quest_performance_budget.ps1 `
  -ArtifactDir '<artifact-root>\candidate_tuned_035\performance_budget' `
  -SkipVisualQa `
  -VisualQaReport '<artifact-root>\candidate_tuned_035\visual_qa\visual_qa_report.json' `
  -TimeoutSeconds 900

.\scripts\build_production_quest.ps1 `
  -ArtifactDir '<artifact-root>\candidate_tuned_035\android_build' `
  -TimeoutSeconds 1800
```

Attempted formal physical gate:

```powershell
.\scripts\run_standalone_quest_gate.ps1 `
  -ArtifactDir '<artifact-root>\candidate\physical_quest' `
  -ApkPath '.\QuestFlight\Builds\Android\QuestFlight-production.apk' `
  -RunCount 3 -Unattended
```

## Android build and exact final Quest deployment

The final 0.35/0.65 release-style Android build passed.

- APK path:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight\Builds\Android\QuestFlight-production.apk`.
- Size: 227,552,479 bytes.
- SHA-256:
  `243af4bec4fe316be32ccb3fc7565b4942bda819295ef5cdf98b09cefafa0f12`.
- Manifest/log:
  `<artifact-root>\candidate_tuned_035\android_build\product_apk_manifest.json`
  and `unity_product_build.log`.
- The Full Map Building Clipmap shader compiled for Vulkan with 48 internal
  programs / 16 unique programs. The build logged existing potential-
  uninitialized sampling warnings but no building shader compile error.

The installed Quest `base.apk` hash was pulled from the device and matched the
built APK exactly. In the worn live session the app remained foreground, the
runtime contract reported `buildings=57,441/24/233,298`, and ten page shifts
were observed. Nine retained 23 pages and entered 1; one retained 21 and
entered 3. The fixed 24-page window remained populated. This is direct final-
APK coverage/residency evidence.

## Physical Quest evidence and performance boundary

### Formal earlier-candidate attempt

The formal three-run gate used the earlier 0.18/0.62 APK, 227,552,747 bytes,
SHA-256
`745dce9795dda8be84e37e6ac3483f562a5cfd374b87915ca032c508fe53629a`.
The APK was immutable across all three unrecorded runs, but the overall result
was `FAIL_THREE_RUN_QUEST_GATE`.

| Run | Context | VrApi FPS avg | App avg | CPU&GPU avg / p95 | GPU util avg | Stale / tears | Max temp | Draws / visible tris / materials |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | Startup showroom, not building flight | 45.90 | 3.58 ms | 26.77 / 28.90 ms | 30% | 1,713 / 0 | 43 C | 27 / 62,062 / 7 |
| 2 | Flight; 24-page shifts active | 44.72 | 17.54 ms | 42.51 / 44.48 ms | 85% | 1,795 / 0 | 43 C | 58 / 616,401 / 29 |
| 3 | Flight | 48.80 | 17.64 ms | 39.81 / 44.95 ms | 92% | 1,528 / 0 | 44 C | 55 / 499,630 / 28 |

No crash, ANR, OOM, building-load error, or shader error was observed. The
runs failed timing/stale thresholds and broader runtime checks including
mountain renderer-state and scene-wide transparent-slot conditions. The gate
did not contain a controlled corner-softening-on/off A/B, so it does not
isolate Phase 1 shader cost.

Historical Quest diagnostics before this milestone also ran near 40--50 FPS:
an August 31 diagnostic reported 40.97 FPS, CPU&GPU 40.89 ms average / 46.10 ms
p95, 2,937 stale frames, 6 tears, and 42 C; an August 29 diagnostic reported
39.52 FPS, CPU&GPU 37.69 / 39.92 ms, 4,815 stale frames, no tears, and 45 C.
These support that the absolute cadence problem predates Phase 1, but they used
different APKs, building revisions, and/or protocols. They are not an exact
baseline and cannot prove zero incremental Phase 1 cost.

### Final 0.35/0.65 live worn snapshot

The exact final APK was sampled opportunistically while the user assessed it.
The 35 valid app-PID, SF=1.00 VrApi rows span about 34 seconds and report 45.31
FPS average, 13.79 ms app average / 17.27 ms p95, 38.44 ms CPU&GPU average /
43.98 ms p95, 69.5% / 82.0% average/p95 GPU utilization, 1,089 stale frames,
4 tears, and maximum reported temperature 37 C. The session validates exact
APK identity, foreground state, building contract, and page shifting.

This snapshot is **supporting visual/runtime evidence only**. It lacks the
required warmup, repeated measurement windows, thermal soak, controlled A/B,
and formal report, so its timing is not a Quest performance pass. Remaining
physical work for a future performance claim is a three-run warm/measurement/
thermal gate on the final APK plus a controlled width/strength-zero A/B if the
incremental shader delta must be isolated.

Final live evidence:

`<artifact-root>\candidate\physical_quest\live_visual_tuned_0p35_0p65_20260902T192152`

Formal attempt evidence:

`<artifact-root>\candidate\physical_quest\standalone_quest_gate_summary.md`

## Product-validation and simulator limitations

`validate_product.ps1` passed preceding policy, extractor, KBDU, provenance,
aircraft, configurator, and integrity work, then stopped in dependency closure
because these six pre-existing unrelated assets are unclassified:

- `Assets/Production/Materials/StartupShowroomAccent.mat`
- `Assets/Production/Materials/StartupShowroomBackdrop.mat`
- `Assets/Production/Materials/StartupShowroomOverlay.mat`
- `Assets/Production/Materials/StartupShowroomShell.mat`
- `Assets/Production/Materials/StartupShowroomSurface.mat`
- `Assets/Production/Materials/StartupShowroomText.mat`

No full-map building asset appears in the unknown or rejected list. This is an
inherited/pre-existing failure, not a Phase 1 pass and not a reason to alter
unrelated showroom scope.

The simulator wrapper built its Windows player and preserved machine OpenXR
configuration, but classified the run `BLOCKED_MISSING_RECORDED_REPLAYS`: all
ten required VRS sessions were absent. One live OpenXR session connected with
stereo/HMD/Touch evidence, but the Windows player rejected the Android-targeted
aerial and building AssetBundles. The building window was unavailable, and the
captured startup view is not Phase 1 building evidence. Simulator output also
cannot establish Quest GPU, thermal, stale/tear, or frame timing.

Reports:

- `<artifact-root>\candidate\product_validation\dependency_closure\allowlist_comparison.json`
- `<artifact-root>\candidate\simulator\meta_xr_simulator\qa_run\simulator_qa_run_report.json`
- `<artifact-root>\candidate\simulator\meta_xr_simulator\visual_gate\simulator_visual_summary.md`

## Artifact locations

External artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_building_fidelity_v1_20260902_173655`

Stable pointer:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_building_fidelity_v1_active_root.txt`

Key groups:

- Baseline tests, mesh contract, captures, and static budget:
  `<artifact-root>\baseline`.
- Authoring, deterministic comparison, first-candidate validation, product and
  simulator attempts, and formal Quest attempt: `<artifact-root>\candidate`.
- Intermediate tuned captures/tests: `<artifact-root>\candidate_tuned`.
- Final 0.35 tests, Visual QA, static budget, and Android build:
  `<artifact-root>\candidate_tuned_035`.

## Known limitations

- The analytic bevel changes lighting normals, not the physical silhouette.
  Extreme silhouette close-ups remain sharp by design.
- Concave, ambiguous, nearly collinear, short-edge, and pathological acute
  corners retain their old flat-face shading.
- The final 0.35 m width exceeds the initial 0.12--0.20 m exploration range
  because 0.18 m was not perceptible to the user. The short-wall cap and
  subpixel fade constrain the wider response.
- No controlled physical Quest shader-on/off A/B or hardware instruction/
  sample profile was completed. No incremental frame-cost claim is made.
- The formal physical Quest timing/stale-frame gate is not green. Historical
  evidence suggests the absolute limitation is inherited, but exact causal
  isolation remains future work.
- Product validation and simulator have the unrelated blockers above. Neither
  is misreported as a pass.
- Buildings remain low-poly approximations. This milestone does not claim
  surveyed architectural truth, photorealism, certified fidelity, training
  credit, or accepted Quest frame performance.
- Phase 2 is independently unimplemented and unaccepted.

## Files changed

- `QuestFlight/Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs`:
  deterministic corner codec, wall-alpha authoring, final material defaults,
  compiler version, report coordinate correction, and triangle reconciliation.
- `QuestFlight/Assets/Shaders/FullMapBuildingClipmap.shader`: wall-only analytic
  corner-normal adjustment using existing interpolants and texture samples.
- `QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/Materials/FullMapBuildingClipmap.mat`:
  final 0.35 m width and 0.65 strength.
- `QuestFlight/Assets/Scripts/Environment/AircraftCenteredBuildingClipmapController.cs`:
  exact 233,298 selected-triangle default.
- `QuestFlight/Assets/Editor/Tests/FullMapBuildingClipmapEditModeTests.cs`:
  corner math, suppression, direction, convergence, quantization,
  interpolation, RGB, determinism, geometry/resource counts, shader paths,
  and product-contract regressions.
- `QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/KBDU_StableShellCompilationV1.json`:
  rebuilt v12 report aligned to world-offset emitted meshes.
- `QuestFlight/Assets/Production/Environment/ProductionEnvironmentRoot.prefab`:
  refreshed exact building-triangle metadata.
- `docs/evidence/FULL_MAP_BUILDING_FIDELITY_V1_2026-09-02.md`: this record.

All paired Unity `.meta` files and existing GUIDs are preserved. No new binary
source asset was required.
