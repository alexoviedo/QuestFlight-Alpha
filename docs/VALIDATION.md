# Validation

Run from the repository root:

```powershell
.\scripts\validate_product.ps1
.\scripts\run_unity_tests.ps1 -TestPlatform EditMode
.\scripts\run_unity_tests.ps1 -TestPlatform PlayMode
.\scripts\run_production_visual_qa.ps1
.\scripts\run_quest_performance_budget.ps1
.\scripts\run_simulator_visual_gate.ps1
.\scripts\build_production_quest.ps1
.\scripts\run_standalone_quest_gate.ps1
```

`validate_product.ps1` is the one-command product gate and includes dependency, serialization, reference, EditMode, PlayMode, standard Visual QA, static render-budget, and Android build checks. The current feature candidate captures 81 Visual-QA views; every full-resolution image requires inspection. Historical milestones below retain their original capture counts. `run_quest_performance_budget.ps1` writes the planning report plus material/shader, texture-memory, LOD/culling, and qualitative overdraw evidence. The accepted-baseline hard planning ceilings are 120 estimated draw calls, 600,000 selected-LOD visible triangles, 25 visible materials, 180 MiB estimated texture memory, and three transparent material slots. Historical and feature-scoped values below are evidence only.

## Airport Parked Aircraft V1

- focused parked-aircraft authoring: PASS for 17 placements, seven powered
  aircraft, ten sailplanes, exact source hashes, minimum-bound grounding, and
  one shared opaque material;
- EditMode: PASS 125/125;
- PlayMode: PASS 9/9;
- product integrity and dependency closure: PASS;
- production Visual QA: PASS 92/92 with
  `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; the three new full-resolution
  aircraft views and contact sheet were inspected;
- camera-aware geometry/draw/material gate: PASS at 56/75 draws,
  638,407/650,000 visible triangles, 29/29 visible materials, and three/three
  transparent slots;
- aggregate static performance classification:
  `FAIL_STATIC_QUEST_BUDGET` only at 211.25/210 MiB conservative texture
  residency. The two new texture-free derivatives add no texture residency; the
  excess belongs to the accepted airport hero-corridor source and is retained
  without weakening the gate;
- Android release-style build: PASS, 135,844,488 bytes, SHA-256
  `23894176073b694e63fedb1f244fdedb24faee8bd6eb592824d369d3d8584c59`;
- physical Quest worn/timing gate: NOT RUN for this APK.

Full evidence is in
`docs/evidence/AIRPORT_PARKED_AIRCRAFT_V1_2026-07-24.md`.

## Rounded Canopy Volume V1 closure

The bounded candidate on `feature/rounded-canopy-volume-v1` starts from tagged
source `82fb39c5c84f4a96b321b53242ecd46348d6d244`. Its machine delivery gates pass;
recorded simulator replay and physical Quest acceptance remain incomplete.

- vegetation source/contract validator: PASS; focused Python: PASS 9/9;
- T0/T1: unchanged at 22 instances, 12,860 triangles, two draws;
- Tier 2: PASS for 42 fixed-world, non-camera-facing grove clusters, 540
  triangles each / 22,680 total, in one `DrawMeshInstanced` batch and one shared
  material; 30 clusters are within 1.5 km;
- axis contract: PASS for explicit Blender Z-up to Unity Y-up
  `Quaternion.Euler(-90, 0, 0)` conversion, right-multiplied into each matrix;
- no new texture, material, transparent slot, shader, or shader variant; no
  per-tree GameObject/`Update`, crossfade, dither, or camera-facing billboard;
- EditMode: PASS 114/114; PlayMode: PASS 9/9; one-command product validation and
  dependency closure: PASS;
- production Visual QA: `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; the
  lead inspected the full-resolution canopy and unrelated regression frames;
- static planning budget: PASS at 54 draws, 624,086 selected triangles, 774,139
  serialized triangles, 26 visible / 27 active materials, 211,849,528 estimated
  texture bytes, three transparent slots, 13 shaders / 18 variants, and 4x MSAA;
- Meta XR Simulator: live Quest-3-profile connection, nonblank stereo, HMD, and
  both Touch devices PASS with both eyes inspected; aggregate
  `BLOCKED_MISSING_RECORDED_REPLAYS` because all ten `.vrs` files are absent;
- Android release-style build: PASS, 133,992,887 bytes, SHA-256
  `0d31c39f113c3ed5edf286259f35f25cf18852b0129445621b79bf32999851d9`;
- physical Quest worn/timing acceptance: NOT RUN for this APK.

Static and Meta XR Simulator evidence is not standalone Quest performance. The
prior source APK's 8/10 worn score is not revalidated or attributed to this
build. Full evidence is in
`docs/evidence/ROUNDED_CANOPY_VOLUME_V1_2026-07-20.md`.

## Photoreal Aerial Surface V1 semantic-alignment closure

The original machine closure on `feature/photoreal-aerial-surface-v1`, sourced
from main `197283fc4f17ae1f78db57c6fcc4e27dc995cd66`, was partial because its
full-resolution art remained below the original lead target. A subsequent worn
smoke of the exact APK scored 8/10; commit
`82fb39c5c84f4a96b321b53242ecd46348d6d244` is now the accepted development
baseline tagged `photoreal-aerial-surface-v1`. Neither result is release or
physical performance acceptance.

- aerial-surface Python unit suite: PASS 40/40; deterministic semantic validator:
  PASS;
- exact horizontal-frame contract: PASS at `3.026798e-9` m exact round-trip
  residual and `0.002037` m runtime-affine residual; generic OBJ runtime
  importing is bypassed;
- bare-earth terrain: PASS at 46,426 vertices / 92,000 triangles, with
  outside-high-variation MAE/p95/max of 0.101071/0.357723/1.387371 m,
  `4.528938e-7` m runway-plane residual, and 0 m airport-boundary disagreement
  over 4,404 probes;
- building semantics: PASS for 9,692 non-hero OSM footprints, 16 native batches,
  128,092 triangles, two separately owned hero footprints, and no mirrored or
  cell-sized HLOD representation;
- canopy ownership: PASS for zero raised meshes/triangles/draws, 63,308
  terrain-map pixels plus 144 hero-owned pixels, and 100% exact assignment. The
  deterministic 29-component / 1,960-triangle raised candidate is inactive and
  rejected after inspected block/cliff silhouettes;
- ProductIntegrity and product dependency closure: PASS; full EditMode: PASS
  114/114; PlayMode: PASS 9/9;
- production Visual QA: `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`, 81/81
  captures. Every full-resolution r7 image, the affected final-validation-r3
  seam/semantic frames, and both live-stereo eyes were inspected. Lead overall
  aerial scenery improved from 5.5 to 6.8/10 but remains below the original
  8/10 merge target; machine nonblank and structural checks do not override the
  art-quality shortfall;
- scoped static planning budget: PASS at 53 selected draws, 601,406 selected
  triangles, 751,459 serialized scene triangles, 26 visible materials,
  211,849,528 estimated texture bytes, three transparent slots, 13 shaders / 18
  keyword combinations, and 4x MSAA;
- Meta XR Simulator 201.0: live Quest-3-profile 72 Hz stereo/HMD/both-Touch
  session PASS with scene-consistent nonblank eyes; aggregate
  `BLOCKED_MISSING_RECORDED_REPLAYS` for all ten absent genuine `.vrs` files;
- Android release-style build: PASS, application
  `com.alexoviedo.t2.questflight`, 134,254,889 bytes, SHA-256
  `37ae3ce45f6acde21d181d9708dfb884b22a3599f17321628ca81c7fe5310d11`;
- physical Quest hardware timing/worn acceptance: NOT RUN for this APK.

The complete record is
`docs/evidence/PHOTOREAL_AERIAL_SURFACE_V1_2026-07-20.md`; external logs and
captures are under
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\photoreal_aerial_surface_v1_20260720_114230`.

## Standalone Scenery Baseline V1

The accepted source is `feature/aerial-scenery-recovery-v2` at `b976314c147d2319a3d4021f534f6d22483a17d8`. It is integrated as the tagged standalone scenery development baseline. This establishes current development authority; it does not claim release readiness or final visual/performance acceptance.

- building recovery: PASS 9/9 Python and 4/4 scoped Unity tests; 2,875 fixed closed shells, 24,804 orthophoto-only records, 16 renderers, 46,005 triangles, one opaque shared material, and zero runtime building LODGroups;
- vegetation recovery: PASS 7/7 Python and 5/5 scoped Unity tests; 22 Tier 0/1 placements, two draws, 12,860 triangles, one alpha-clipped material, and zero Tier-2 geometry/draws;
- ground imagery contract: PASS 7/7 Python tests; official public-domain 2019 USGS/USDA NAIP Plus source, four 2048² hot quadrants plus one 4096² 12 km fallback;
- water defer and full EditMode: PASS 98/98; active water mode is `OrthophotoOnly` and reservoir/minor-water renderer/material bindings are inactive;
- PlayMode: PASS 9/9;
- one-command `validate_product.ps1`: PASS, including product integrity, dependency closure, EditMode, PlayMode, 56-view machine Visual QA, static budget, and Android build;
- machine Visual QA: `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; all 56 production frames and 20 dedicated aerial frames were inspected at full resolution;
- lead visual review: **PARTIAL at 5.3/10**; structural defects improved, but building art, near vegetation/grove volume, and low-oblique image sharpness remain below acceptance;
- static planning budget: PASS at 55 draws, 534,976 visible triangles, 24 visible materials, 176.77 MiB estimated texture residency, two transparent slots, 11 shaders / 17 keyword combinations, and 4x MSAA;
- Android release-style integration build: PASS, application `com.alexoviedo.t2.questflight`, 106,789,598 bytes, SHA-256 `5d2ab8fdc18f379bb9fcefe4e1858b911ad51f1821cb7f2763afe566d8919208`;
- Meta XR Simulator v201.0: live Quest 3 profile stereo/HMD/both-Touch session PASS and both full-resolution eyes inspected; aggregate `BLOCKED_MISSING_RECORDED_REPLAYS` for all ten absent `.vrs` files;
- physical Quest: the prior source-branch worn visual smoke was **PASS/PARTIAL at 7/10**; seat correction, absence of floating/size-changing buildings, visible canopy cue, and orthophoto-only water were accepted. Right-side imagery over the reservoir remained blurry while the left view looked decent. A separate unattended integration-candidate run on the exact APK above was `FAIL_MACHINE_GATE`: 72.5333 average FPS, 13.117833 ms combined average, 13.7545 ms combined p95, 1/60 (1.6667%) samples over 13.8889 ms, zero stale, zero tears, 31 °C, no thermal warning, no crash/ANR/OOM/Unity exception, and zero observed managed collections over 4,322 frames. The failures were combined average above 12.8 ms and 509,437 reported visible triangles above 500,000. Direct GPU milliseconds and human evidence were unavailable; this single run is not final three-run acceptance.

The validation summary's `PASS_1_REJECTED_2_UNKNOWN` frozen-source field is an expected fail-closed R&D extraction fixture. The product dependency closure itself is `PASS`; no rejected or unknown R&D dependency was promoted into the product.

See `docs/evidence/STANDALONE_SCENERY_BASELINE_V1_2026-07-20.md`, `docs/evidence/AERIAL_SCENERY_RECOVERY_V2_2026-07-19.md`, and their recorded external artifact roots.

## Aerial Scenery Representation V1 closure

The branch gate is **FAIL — preserve and stop** because visual targets were not met. Structural validation is deliberately reported separately:

- official USGS 3DEP source/provenance and lidar raster validation: PASS;
- fixed terrain compilation: PASS at 46,426 vertices / 92,000 triangles, with the declared outside-outlier error gate passing;
- vegetation pipeline: PASS 5/5 Python tests and static validator; 270 source placements compile to three draws, 17,324 selected triangles, and one material;
- building spatial pipeline: PASS 7/7 Python tests; 9,695 authored records and 17,984 far orthophoto-only records, with zero unqualified lidar height overrides;
- Unity EditMode: PASS 91/91 after restoring the Android/ASTC build-target state used by the product configurator; PlayMode: PASS 9/9;
- product validation/dependency closure: PASS; Android release-style build: PASS;
- static Quest planning gate: PASS at 57 draws, 527,199 selected triangles, 24 visible materials, 182,930,546 estimated texture bytes, two transparent slots, and 4x MSAA;
- fixed-camera machine image checks: PASS, but lead visual review: **FAIL at about 5.5/10** against 7.5/10;
- Meta XR Simulator: one live Quest 3 stereo/HMD/both-Touch smoke PASS; aggregate `BLOCKED_MISSING_RECORDED_REPLAYS` for ten missing sessions;
- physical Quest: NOT RUN by milestone instruction.

The first integrated Tier-2 experiment produced oversized branch geometry. One bounded correction replaced it with 248 fixed 8–24 m aggregate cells; that removes the catastrophic artifact but still looks like sparse dark blobs and does not pass canopy quality. Machine nonblank checks are not visual acceptance. See `docs/evidence/AERIAL_SCENERY_REPRESENTATION_V1_2026-07-18.md`.

The 2026-07-17 V2 closure passed `validate_product.ps1`, 61 EditMode tests, 9 PlayMode tests, 24/24 machine image checks, the static budget gate, and an Android release build. The separate reconstruction evidence adapter failed closed because it selected stale intermediate reports; see the V2 evidence document. Machine passes are not visual acceptance: lead inspection remained below 7/10, so simulator replay and physical Quest timing were deliberately not run.

## Airport-Core V4.2 static packing and geometry closure

V4.2 is a branch-only technical checkpoint, not visual acceptance or a release candidate:

- structural benchmark validator: PASS; 15 renderers / 77,282 triangles / 3 materials, 15,314-triangle adaptive visual terrain, and one accepted lightmap atlas;
- product dependency closure: PASS; 223 approved project dependencies, five Unity-package dependencies, zero unknown/rejected blockers;
- ProductIntegrity: PASS at 171 serialized renderers / 698,328 serialized triangles / 32 materials;
- EditMode: PASS 71/71; PlayMode: PASS 9/9;
- 56-view machine Visual QA: PASS image/integrity checks at 63 maximum draws / 506,254 maximum selected-LOD triangles / 25 visible materials; all 56 full-resolution frames were inspected;
- lead visual review: FAIL at approximately 4.0/10 against the 7.5/10 Stage-1 requirement;
- static Quest planning budget: PASS at 63/120 draws, 506,254/600,000 selected-LOD triangles, 25/25 visible materials, 168.26/180 MiB estimated textures, two/three transparent slots, and 4x MSAA;
- Android release-style APK: PASS, 100,094,165 bytes, SHA-256 `24ae6262085b2b54091681ece03ad1e72311c971aec0486b1c4b377b2c2f8e98`;
- Stage 2, Meta XR Simulator, and physical Quest: NOT RUN because the subjective Stage-1 gate failed.

The aggregate and camera-aware triangle values have different scopes and must not be interchanged. Static/Editor passes do not establish Quest frame timing, thermals, comfort, stereo stability, or worn image quality.

## Airport-Core V4.3 artifact correction closure

V4.3 is a fail-closed branch result: implementation/build integrity passes, but the mandatory subjective lead gate does not.

- exact source: V4.2 `949931049c6e67acbca182a31dbbc5806d6e0102`;
- focused V4.3 tests: PASS 4/4; full EditMode: PASS 75/75; PlayMode: PASS 9/9;
- one-command `validate_product.ps1`: PASS, including dependency closure, serialization, 56-view machine Visual QA, static audit, and Android build;
- lead visual review: reservoir 7.0/10 PASS; runway 5.5/10 FAIL because broad bands remain; minor water 5.5/10 FAIL because the terrain-mask result lacks decision-quality visual proof;
- static budget: PASS at 62 draws, 503,339 selected-LOD triangles, 24 visible materials, 168.08 MiB estimated textures, two transparent slots, and 4x MSAA;
- Android APK: 99,744,675 bytes, SHA-256 `96e6bf0161b204e9d114e45200ea308e42bd8effaac1bdf5d072b1636ee69594`;
- Meta XR Simulator and physical Quest: NOT RUN because the lead gate did not pass.

The machine Visual QA classification means its images are nonblank and structural contracts are intact; it does not override the inspected visual failure.

## Airport-Core V4.3.1 diagnostic closure

V4.3.1 is a branch-only partial result. The bounded art defects pass lead inspection, while deterministic simulator replay remains blocked:

- deterministic R0–R11 runway isolation: PASS; R1 constant-unlit/no-markings retains the defect and proves it is not a texture, marking, lightmap, vertex-color, or duplicate-renderer artifact;
- mesh/terrain correlation: 52/106 pavement vertices below terrain, signed clearance -0.005092144 to +0.004401267 m;
- final runway band analyzer: PASS at 0.0949 normalized peak-to-trough against a documented 0.100 threshold (baseline 0.3206);
- focused V4.3.1 EditMode tests: included in full EditMode PASS 78/78; PlayMode PASS 9/9;
- minor-water evidence: PASS for Hayden, Valmont, and Baseline Reservoir OSM components using false-color, final-shaded, and orthophoto-only captures at ground, oblique, and aerial ranges;
- lead visual scores: runway cockpit/close/aerial 7.0; minor water 7.0; terrain/water integration 7.0; unchanged reservoir cockpit/aerial 7.0;
- static planning budget unchanged from V4.3: 62 draws, 503,339 selected triangles, 689,275 serialized triangles, 24 visible materials, 168.08 MiB textures, two transparent slots, 12 shaders/18 variants, and 4x MSAA;
- Frame Debugger: BLOCKED because Unity 6000.3.8f1 returns zero local events through its batch API; static renderer/pass state and the isolation matrix remain conclusive, but are not mislabeled as Frame Debugger events;
- Meta XR Simulator: live Meta OpenXR stereo/HMD/both-Touch session PASS; replay aggregate `BLOCKED_MISSING_RECORDED_REPLAYS` until the ten genuine interactive recordings are created;
- one-command product validation and Android release-style build: PASS; APK 99,745,119 bytes, SHA-256 `c9a31d53a452cd05a39d293be6fe048bf3e94cc89220152a6591d4e68ef68e59`;
- physical Quest: NOT RUN because deterministic simulator replay did not pass.

See `docs/evidence/AIRPORT_CORE_ARTIFACT_V4_3_1_2026-07-18.md` for paths and complete evidence.

## Airport-Core V4 Stage-1 closure

The final V4 closure is a fail-closed feature-branch result, not acceptance:

- structural benchmark validator: PASS, 27 renderers / 74,730 triangles / 3 materials;
- relevant offline atlas regression: PASS, including bottom-left runtime UV origin;
- EditMode: 67/68; only `FullProductIntegrityGatePasses` fails at 207/180 aggregate renderers and 797,050/700,000 aggregate triangles;
- PlayMode: 9/9 PASS;
- 56-view Visual QA: `FAIL_MACHINE_QA`; every image is nonblank, but the global scene reaches 27/25 materials and lead visual review is 4.0/10;
- camera-aware static audit: 66/120 draws and 572,280/600,000 selected-LOD triangles pass; 27/25 materials and 267,867,042/188,743,680 estimated texture bytes fail;
- `validate_product.ps1`: FAIL at ProductIntegrity;
- Android build: BLOCKED by the same prebuild ProductIntegrity gate; no APK was produced;
- Stage 2, Meta XR Simulator, and physical Quest: NOT RUN because Stage 1 did not pass.

The V2 baseline report predates the selected-LOD estimator correction, so its per-view draw/triangle numbers are not compared numerically with V4. Aggregate scene inventory and texture estimates are reported separately in the V4 evidence document.

## Meta XR Simulator replay gate

Create each simulator recording once through the real Meta XR Simulator recording UI:

```powershell
.\scripts\run_meta_xr_simulator_qa.ps1 -ArtifactRoot $artifacts -RecordSession cockpit_neutral
.\scripts\run_simulator_visual_gate.ps1 -ArtifactRoot $artifacts
```

The gate requires ten genuine replay files named exactly `cockpit_neutral`, `head_pan`, `rear_view`, `viewpoint_calibration`, `control_surface_sweep`, `taxi`, `takeoff_roll`, `rotation_climb`, `shallow_turns`, and `final_approach`. Record every named interaction; do not synthesize a VRS file, rename an unrelated recording, duplicate a smoke capture, or present an unrecorded session as replay evidence. The aggregate gate remains blocked when any genuine recording is absent.

The simulator runner snapshots both OpenXR registry views and Meta's persistent data. Meta v201 may activate its machine runtime when the frontend starts even though the player uses `XR_RUNTIME_JSON`; the runner accepts Meta's paired elevated deactivation only when `PreviousActiveRuntime` exactly matches the pre-run 64-bit runtime and the 32-bit runtime is unchanged. It verifies the restored values afterward. State-restoration failure takes precedence over missing recordings and cannot be converted into success by `-NoFailOnBlocked`.

Meta XR Simulator evidence covers OpenXR API-level stereo, tracking, controller/input replay, and composition. Windows or simulator frame timing is never standalone Quest GPU, CPU, stale/tear, thermal, comfort, or worn-display evidence.

## Physical Quest gate

`run_standalone_quest_gate.ps1` is the one-command physical Quest gate. Its default `-RunCount 3` performs three distinct unrecorded measurements against one immutable APK at 72 Hz. It detects one authorized Quest 3, builds when needed, installs and launches the production APK, reports possible Horizon/Guardian or controller dialogs, waits for readiness, then gives every run an exact 90-second warmup and 60-second measurement. If Quest intercepts launch before the app receives a PID, the gate still preserves a fail-closed temporal summary and identifies controller-required/Guardian evidence instead of throwing away the attempt. Screen recording must remain off during those temporal windows.

Every run must satisfy all of the following: at least 55 timestamped app-PID `SF=1.00` VrApi rows spanning at least 54.0 seconds of the 60-second window; app-PID VrApi CPU+GPU average no greater than 12.8 ms; CPU+GPU p95 no greater than 13.889 ms; no more than 5% of samples over 13.889 ms; no more than two stale frames; zero tears; no thermal warning; no crash; and runtime budgets including no more than two transparent material slots. The script also collects Unity cadence/workload series, static budgets, steady-state allocation evidence, thermal state, logcat, screenshots outside the timing windows, and product evidence. It always writes both summaries before returning and exits nonzero for an attempted `FAIL_*` temporal result; explicit blocked/not-run results remain preserved and distinct from a machine pass. Use `-Unattended` only when no human witness is available; that mode cannot promote comfort, controller, Guardian, or worn-display acceptance.

The Quest VrApi parser identifies samples by a verified product PID and `SF=1.00`; `SF=1.00` alone is never allowed to infer app identity. It records `Fov` as telemetry rather than using it as an identity discriminator because supported Quest firmware has emitted product rows with both `Fov=0` and `Fov=2`. `validate_product.ps1` runs regression fixtures covering both forms, rejects different-PID and compositor-only rows, and distinguishes the Quest controller-required launch dialog from Guardian while treating either as a blocking system dialog.

Editor and Meta XR Simulator gates prove dependency, serialization, budget, image-integrity, stereo API, tracking, input, composition, and build contracts within their stated limits. They do not prove comfort, handling fidelity, sustained Quest frame timing, thermals, or worn visual quality. Those claims require the physical headset.

## Quest Visual Fidelity V1 closure

The bounded canopy/facade/cockpit pass closes its machine gate:

- aerial-surface Python tests: PASS 22/22;
- vegetation Python tests: PASS 9/9; vegetation asset validation: PASS;
- Unity EditMode: PASS 114/114; PlayMode: PASS 9/9;
- production Visual QA: PASS 81/81 with classification `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; affected full-resolution frames were inspected;
- static Quest planning: PASS at 54 draws, 624,086 selected triangles, 774,139 serialized triangles, 26 visible materials, 211,849,528 estimated texture bytes, three transparent slots, 13 shaders/18 variants, and 4x MSAA;
- Meta XR live Quest-3-profile stereo/HMD/both Touch: PASS; aggregate `BLOCKED_MISSING_RECORDED_REPLAYS` for ten absent genuine `.vrs` sessions;
- Android release-style build: PASS, 133,529,570-byte APK, SHA-256 `584821b6022a5e583b971d80df04579c23f450989b125acc0ef2eb0011cc7e6f`.

The earlier worn 8/10 smoke used a different 133,992,887-byte APK with SHA-256 `0d31c39f113c3ed5edf286259f35f25cf18852b0129445621b79bf32999851d9`. It is retained as direction-setting evidence only; no worn score, comfort result, or physical Quest timing is claimed for the final binary. See `docs/evidence/QUEST_VISUAL_FIDELITY_V1_2026-07-20.md`.

## Worn Visual Runtime V1 merge-candidate closure

The exact source candidate closes its pre-worn machine gate:

- canopy/building generators compile and emit deterministic manifests and hashes;
- aerial-surface Python tests: PASS 22/22; deterministic surface validator: PASS;
- Unity EditMode: PASS 116/116; PlayMode: PASS 9/9;
- full `validate_product.ps1`: PASS, including product integrity, provenance, dependency closure, source policy, allocation fixtures, and static performance checks;
- production Visual QA: PASS 81/81 with classification `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; the affected canopy, facade, cockpit-glass, and geometry-bound cockpit-depth frames were inspected at full resolution;
- static Quest planning: PASS at 55 selected draws, 632,320 selected triangles, 798,719 serialized mesh triangles, 26 maximum visible / 27 active materials, 211,849,528 estimated texture bytes, three transparent slots, 12 shaders/17 keyword combinations, and 4x MSAA;
- Meta XR Simulator 201.0 live Quest-3-profile stereo/HMD/both Touch: PASS for `cockpit_neutral`; aggregate status remains `BLOCKED_MISSING_RECORDED_REPLAYS` for the ten absent genuine `.vrs` sessions;
- Android release-style build: PASS, 135,905,162-byte APK, SHA-256 `36216609302a851d85db3255ea0f99dfadfaacb1cdc9324826416eb64d84e5d3`.

The machine pass establishes a test-ready merge candidate, not worn visual,
comfort, frame-time, stale/tear, thermal, or release acceptance. Those claims
remain withheld until the exact APK is inspected or measured on the physical
Quest 3. See `docs/evidence/WORN_VISUAL_RUNTIME_V1_2026-07-21.md`.

## Cockpit reflection and directional-visibility closure

The final cockpit candidate closes its machine gate:

- deterministic geometry-baked cabin reflection and 20 directional-visibility stream contracts: PASS;
- Unity EditMode: PASS 117/117; PlayMode: PASS 9/9;
- full `validate_product.ps1`: PASS, including product integrity, provenance, dependency closure, Visual QA, static budget, and Android build;
- production Visual QA: PASS 81/81 with classification `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; cockpit glass, instrument cover, and stable-shading frames were inspected at full resolution;
- static Quest planning: PASS at 55 maximum selected draws, 628,444 selected triangles, 793,823 serialized mesh triangles, 27 materials, 211,911,484 estimated texture bytes, three transparent slots, 12 shaders/17 keyword combinations, and 4x MSAA;
- Meta XR Simulator 201.0 live Quest-3-profile stereo/HMD/both Touch: PASS for `cockpit_neutral`; aggregate status remains `BLOCKED_MISSING_RECORDED_REPLAYS` for the ten absent genuine `.vrs` sessions;
- Android release-style build: PASS. Exact APK identity is recorded in `docs/evidence/COCKPIT_REFLECTION_DIRECTIONAL_VISIBILITY_V1_2026-07-22.md`.

The visibility stream adds no renderer, draw call, material, texture lookup,
runtime raycast, or shadow map. These checks establish a worn-test candidate,
not physical Quest timing, comfort, or subjective optical acceptance.

## Cockpit local-shadow closure

Cockpit Local Shadow V1 replaces the rejected flickering realtime-shadow attempt
and the visibly jagged sparse-PCF prototype with one authored aircraft-local
filtered two-moment shadow map:

- Unity EditMode: PASS 118/118; PlayMode: PASS 9/9;
- production Visual QA: PASS 85/85, with full-resolution cockpit and directional
  shadow views inspected;
- static Quest planning: PASS at 55 maximum draws, 628,768 selected triangles,
  29 visible materials, 212,069,266 estimated texture bytes, three transparent
  slots, 13 shaders/18 keyword combinations, and 4x MSAA;
- Meta XR Simulator 201.0 live stereo/HMD/both Touch: the
  `cockpit_neutral` session passes with both eyes nonblank, scene-consistent,
  and one authored product hierarchy; aggregate replay status remains
  `BLOCKED_MISSING_RECORDINGS`;
- Android release-style build: PASS, 136,203,589-byte APK, SHA-256
  `4dc479af8b961909ddbac0249b863c040ebf95e04796437aa26bcc2ac9cf6057`;
- exact-APK worn visual gate: PASS for continuous edge, no sawtooth boundary,
  plausible direction/strength, no flicker or stereo mismatch, and readable
  instruments.

These checks establish visual acceptance of the bounded shadow implementation.
They do not establish long-duration Quest timing, thermal, or allocation
acceptance.
# Airport hero corridor validation

Run `AirportHeroCorridorEditModeTests` and the standard production Visual QA. Dedicated shots `25_hero_east_hangar_rows` through `28_hero_corridor_takeoff_view` use fixed surveyed airport-local elevations; combined renderer bounds must not be treated as ground elevation.

## Bounded road-traffic validation

Traffic V1 validation on Unity 6000.3.8f1:

- `python tools/kbdu_traffic/validate_traffic_manifest.py`: PASS; 27,948
  source-visible parked vehicles, 361 lane routes, 788 moving vehicles, 26,225
  terrain-sampled route points, exact source/runtime ID parity, and 378
  contiguous 500 m parked-vehicle cells;
- the native City 2020 scan searched every one of 27,679 OSM building contexts;
  8,754 buildings contain accepted source-visible vehicles, 18,925 contain no
  supported visible vehicle, and no synthetic coverage filler was added;
- semantic rejection counts from the complete scan were 1,567 road/highway,
  3,762 water, 2,865 building-footprint, 19 aeroway, and 609 without building
  context; 16 additional detections outside the 12 km product imagery were
  excluded during final compilation;
- EditMode: PASS 133/133, including traffic ownership, source-evidence,
  full-map extent, spatial-cell continuity, mesh, LOD hysteresis, lane-width,
  and hierarchy tests;
- PlayMode: PASS 9/9;
- production Visual QA: PASS 97/97 with
  `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; full-resolution traffic views
  32–36 were inspected. The native-source detector contact sheet shows
  source-center/long-axis agreement across dense parking rows; product views
  show the recovered reservoir and airport parking together with existing
  moving traffic;
- Meta XR Simulator: live Quest 3 profile stereo/HMD/both Touch PASS with
  consistent nonblank eyes; aggregate
  `BLOCKED_MISSING_RECORDED_REPLAYS` for the ten absent genuine `.vrs` files;
- Android ARM64/Vulkan/OpenXR build: PASS, 137,099,870 bytes, SHA-256
  `529f2a715585f4b11d212b34b2f5c8690751681fbca809ff59c371cf9d18778b`;
- unattended Quest install: PASS; launch smoke
  `BLOCKED_HUMAN_SYSTEM_DIALOG` because Horizon required awake Touch
  controllers before starting the application, so no product PID or hardware
  timing is claimed;
- traffic contract: at most nine opaque instanced draws and 129,312 selected
  vehicle triangles; 64 near-detail and 1,800 far-proxy instances; no added
  material, texture, shader, transparent slot, realtime shadow, collider,
  rigidbody, or per-vehicle GameObject;
- aggregate static audit: draw calls 56/75, visible triangles 638,446/650,000,
  materials 29/29, transparent slots 3/3, and 4x MSAA pass. Texture residency is
  the inherited failure at 221,506,450/220,200,960 bytes; the pre-traffic
  airport-hero report has the identical texture estimate.

The APK and all captures remain external artifacts and are not committed.

## Airport perimeter fence validation

- `python -B tools/kbdu_airport_fence/validate_airport_fence_manifest.py
  --repo-root .`: PASS; four sections, 4,273.191 m, four-draw maximum, no new
  materials or textures.
- Product integrity: PASS at 799,839/800,000 serialized triangles.
- EditMode: PASS 136/136, including single-root ownership, four fixed
  renderers, one reused material, 104 selected/serialized triangles, no LOD
  groups, no physics, and fence vertex-data contracts.
- PlayMode: PASS 9/9.
- Production Visual QA: 100/100 image-integrity passes. Dedicated views
  `37_airport_south_security_fence`, `38_airport_west_hangar_fence`, and
  `39_airport_fence_overview` were inspected at full resolution.
- The aggregate Visual QA script still reports `FAIL_MACHINE_QA` against its
  inherited 600,000-triangle/25-material generic thresholds. The accepted
  product limits pass at 638,538/650,000 visible triangles and 29/29 materials.
- Static planning: 60/75 draws, 638,538/650,000 visible triangles, 29/29
  materials, 3/3 transparent slots, and 4x MSAA pass. The inherited
  221,506,450/220,200,960-byte texture overage remains; the fence adds no
  texture residency.
- Meta XR Simulator v201: live Quest 3 stereo/HMD/both Touch connection PASS;
  aggregate status `BLOCKED_MISSING_RECORDED_REPLAYS`.
- Android ARM64/Vulkan/OpenXR build: PASS; 137,116,114 bytes, SHA-256
  `5abf73962f72c6a6675d2a0e3ac83e6ba96976fda940753a6adfb4c5e45f377f`.

These are development and regression results. They do not establish physical
Quest frame timing, thermal behavior, or surveyed airport-security fidelity.
