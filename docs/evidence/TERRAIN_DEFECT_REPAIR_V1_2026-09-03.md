# Terrain Defect Repair V1 Evidence — 2026-09-03

## Result

`FIXED_ALL_REPRODUCED_TERRAIN_DEFECTS`

The reproduced defects were two independent tier-interface failures. No evidence
of interior coarse faceting remained after the seams and topology were corrected,
so no global smoothing, global densification, imagery, shader, material, or
unrelated-scene change was made.

## Repository and scope

- Starting commit: `54ae8fc93a6ac752121e123347694d45f42733fd`
- Starting branch: `main`
- Starting status: clean
- Feature branch: `feature/terrain-defect-repair-v1`
- Starting commit summary: `54ae8fc docs: record semantic tower headset acceptance`
- External artifact root:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-terrain-artifacts\terrain_defect_repair_v1_20260903_163202`
- `AGENTS.md` and `docs/VALIDATION.md` were read before diagnosis.
- No headset-performance claim is made. The repair was diagnosed programmatically,
  then checked with full-resolution mono/stereo editor captures. A physical Quest
  run was unnecessary for this static topology correction.

## Reproduced defects and classification

All coordinates are product ENU metres (X=east, Z=north). Screen bounds are
approximate pixel bounds in the 1920x1080 baseline frames.

| Defect | Camera position -> target; FOV | Baseline screen bounds | Owner and exact interval | Classification and proof |
|---|---|---|---|---|
| West mountain sky hole | `(-5480,355,-4140) -> (-6000,235,-4640); 48` | approximately `x=560..1005, y=398..568` in `mountain_hole_west_oblique.png` | `Terrain_Mid12km_USGS` triangle 1280 and `Terrain_Far24km_Immutable_USGS` triangle 32039 at `X=-6000, Z=-4650` | `TIER_SEAM` + `TOPOLOGY_OR_CULLING`. Middle height was 204.100006 m and far height 274.700012 m: a 70.600006 m disagreement. The legacy far internal skirt was only 24 m deep. The opening remained when tiers were isolated and with double-sided rendering, proving missing coverage rather than back-face-only culling. It aligns exactly with the west ±6 km boundary. |
| East flat-area step/overlap | `(1840,32,-650) -> (2000,-5,-810); 50` | approximately `x=0..720, y=545..805` in `near_mid_east_worst_inside.png` | `Terrain_Near4km_USGS` triangle 24522 and `Terrain_Mid12km_USGS` triangle 11397 at `X=2000, Z=-810` | `TIER_SEAM`. Near height was -2.958889 m and middle height -7.750000 m: a 4.791112 m overlap/disagreement. It remained in tier-isolated and double-sided diagnostics and aligns with the nominal east ±2 km tier mask. The actual near mesh continues to ±2048 m. |
| Near/middle corner stair artifacts | same near/middle control cameras; wireframe variants | the four square-corner regions around the preceding step | middle open-edge components at `[-2100,-1950]x[-2100,-1950]`, `[1950,2100]x[-2100,-1950]`, `[-2100,-1950]x[1950,2100]`, and `[1950,2100]x[1950,2100]` | `TOPOLOGY_OR_CULLING` + `TIER_SEAM`. Each was a five-point, maximum-degree-four non-loop component produced by diagonal corner flaps/T-junctions in the legacy square mask. They align with the ±2 km transition corners. |

Baseline critical triangle coordinates are preserved in
`baseline_final/terrain_critical_triangles.csv`. Tier-only, double-sided, terrain-only,
and wireframe captures are under `baseline_gpu/captures/`.

## Repair

- The near mesh and every source triangle away from the joins are unchanged.
- The middle generator removes only legacy cells whose triangle centroid lies
  inside the square radius 2100 m, removes all four corner flaps, and creates one
  deterministic zipper strip from the exact near outer loop at ±2048 m to the
  retained middle loop at ±2100 m.
- The existing far authoring path removes the obsolete inner skirt and first two
  far grid rings, then creates one deterministic zipper strip from the exact
  middle loop at ±6000 m to retained far terrain at ±6200 m.
- Both strips are authored into their existing terrain mesh/render draw. They add
  no renderer and explicitly close all four sides and corners.
- Four bounded, serialized triangle-query patches reproduce the visible mesh
  heights and interpolated normals over square radii 2000..2100 m and 6000..6200 m.
  The fourth is a 0.011 m-wide edge selector at ±6000 m so the exact shared edge
  returns the visible middle-mesh normal.
- Mesh asset GUIDs, world extent, materials, textures, shaders, and runtime object
  hierarchy are unchanged. The authoring command was repeated and SHA-256 hashes
  for both meshes, the query asset, and the environment prefab were identical.

Declared repair bounds:

- near/middle query and geometry repair: square annulus, radius 2000..2100 m;
- middle/far geometry and query repair: square annulus, radius 6000..6200 m;
- no height or normal change is permitted outside those annuli.

## Machine topology and seam report

Primary reports:

- baseline: `baseline_final/terrain_diagnostic_report.json`
- candidate: `candidate_final/terrain_diagnostic_report.json`
- sample-level seams: corresponding `terrain_seam_samples.csv`
- critical triangles: corresponding `terrain_critical_triangles.csv`

| Metric | Baseline | Candidate |
|---|---:|---:|
| Airport vertices / triangles | 11,618 / 18,834 | unchanged |
| Near vertices / triangles | 46,426 / 92,000 | unchanged |
| Middle vertices / triangles | 6,240 / 11,752 | 6,682 / 12,194 |
| Far vertices / triangles | 63,841 / 89,280 | 47,112 / 87,184 |
| Nonfinite vertices | 0 | 0 |
| Zero-area visible triangles | 0 | 0 |
| Downward/reversed visible triangles | 0 | 0 |
| Middle open components | 6 (2 expected loops + 4 malformed corner components) | 2 expected shared-boundary loops |
| Far vertical triangles | 2,880 (inner + outer skirts) | 1,920 (outer world skirt only) |
| Unexpected open loops/components | 4 | 0 |
| Welded non-manifold edges | 0 | 0 |
| Top-down uncovered cells | 0 | 0 |

Renderer bounds were unchanged: airport X ±800/Z ±300, near XZ ±2048,
middle XZ ±6000 with Y -65.200012..628.5, and far XZ ±12000 with
Y -133..973.066162 m.

| Shared edge | Baseline max / p95 / RMS | Candidate max / p95 / RMS | Samples over 1 mm |
|---|---|---|---:|
| near/middle | 4.791112 / 1.483620 / 0.538242 m at nominal ±2000 | 0.000003815 / 0.000001907 / 0.000000665 m at rendered ±2048 | 1,595 -> 0 |
| middle/far ±6000 | 70.600006 / 15.141602 / 7.245785 m | 0.000061035 / 0.000003815 / 0.000004542 m | 9,480 -> 0 |

The final worst shared-edge disagreement is 0.0611 mm, below the 1 mm gate.
There is no internal vertical skirt, overlap flicker, z-fighting, or sky-visible
opening in the inspected candidate views.

## Preservation and grounding

The starting-HEAD and candidate query assets were sampled using the same 12,619
point deterministic set. The detailed result is
`candidate_extended/regression_comparison.txt`.

- 7,727 samples outside the declared annuli: maximum height delta 0 m; maximum
  normal-vector delta 0; no coverage or terrain-zone changes.
- 1,016 reservoir surface/shore mesh control vertices: maximum height and normal
  deltas 0.
- runway 08 end, runway 26 end, and midpoint: maximum height and normal deltas 0.
- 26 grounded renderer samples within 400 m of a repaired tier boundary: maximum
  terrain-height and minimum-clearance deltas 0. None lies inside a repair annulus,
  so no in-region grounded root required movement.
- 13 parked-aircraft/watercraft protected source and generated-file hashes: 0 changes.
- Original grid layers, runway data, reservoir geometry, imagery mapping, and
  24 km world extent are unchanged. No road/traffic route or movement file changed.

## Resource delta

- Terrain triangles: middle +442, far -2,096, net -1,654.
- Terrain vertices: middle +442, far -16,729, net -16,287.
- Authored environment: 71 renderers and 13 materials before/after; no draw,
  material, texture, or shader increase.
- Baked environment triangle count: 631,141 -> 629,487.
- Query asset file size: 4,963,974 -> 5,706,613 bytes (+742,639 bytes) for 7,513
  bounded serialized query triangles.
- Focused Visual-QA peak: 24 estimated draws, 298,391 selected visible triangles.

## Captures and inspection

Matched 1920x1080 production captures:

- baseline: `baseline_gpu/captures/production/`
- candidate: `candidate_final/captures/production/`
- exact hole: `mountain_hole_west_inside.png`,
  `mountain_hole_west_oblique.png`
- stair/flat defect: `near_mid_east_worst_inside.png`,
  `near_mid_east_worst_outside.png`
- joins/sweep controls: `near_mid_join_control.png`, `mid_far_join_control.png`
- unaffected control: `unaffected_near_control.png`

The full-resolution matched images and wireframe/tier-isolated/double-sided
variants were inspected. Candidate results show no sky-visible hole, straight
geometric discontinuity, z-fighting, or internal vertical skirt.

The standard focused Visual-QA result is under `visual_qa_final/`:

- classification: `PASS_TERRAIN_DEFECT_REPAIR_V1_MACHINE_QA_PHYSICAL_QUEST_NOT_RUN`;
- 8/8 full-resolution frames passed, including inspected 3840x1080 left/right
  stereo pairs for both repaired joins;
- existing mountain identity sweep passed at all 25 headings;
- contact sheet: `visual_qa_final/visual_qa_contact_sheet.png`.

## Commands and test results

- Deterministic repair authoring:
  `Unity.exe -batchmode -nographics -quit -projectPath <project> -executeMethod QuestFlight.Editor.TerrainDefectRepairV1Authoring.RunBatch`
  — PASS; repeated output hashes identical.
- Focused repair tests:
  `Unity.exe -batchmode -nographics -projectPath <project> -runTests -testPlatform EditMode -testFilter QuestFlight.EditorTests.TerrainDefectRepairV1EditModeTests`
  — PASS 3/3 (`tests_terrain_final/results.xml`).
- Existing terrain boundary tests:
  `... -testFilter QuestFlight.EditorTests.StandaloneEnvironmentBoundaryEditorTests`
  — PASS 3/3 (`tests_boundary/results.xml`).
- Focused standard Visual QA:
  `Unity.exe -batchmode -quit -projectPath <project> -executeMethod QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunTerrainDefectRepairV1VisualQa`
  — PASS 8/8 plus 25-heading mountain sweep (`visual_qa_final/visual_qa_report.json`).
- `git diff --check` — PASS.

Per request, the full combined product validation is deferred to the subsequent
grade-separated-road task. Nothing was pushed and no remote was altered.
