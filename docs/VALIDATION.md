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

`validate_product.ps1` is the one-command product gate and includes dependency, serialization, reference, EditMode, PlayMode, standard Visual QA, static render-budget, and Android build checks. The current standard Visual QA is a 56-view capture set and requires inspection of every full-resolution image. `run_quest_performance_budget.ps1` writes the planning report plus material/shader, texture-memory, LOD/culling, and qualitative overdraw evidence. On V4.2 the hard planning ceilings are 120 estimated draw calls, 600,000 selected-LOD visible triangles, 25 visible materials, 180 MiB estimated texture memory, and three transparent material slots. The V2 ceilings below are retained as historical branch evidence, not current V4.2 acceptance values.

## Aerial Scenery Recovery V2 closure

The recovery branch is **PARTIAL — preserve and push, do not merge**. Exact source is `feature/aerial-scenery-representation-v1` at `6ff04fc9a1dc636030e28ed759b31539ee637396`.

- building recovery: PASS 9/9 Python and 4/4 scoped Unity tests; 2,875 fixed closed shells, 24,804 orthophoto-only records, 16 renderers, 46,005 triangles, one opaque shared material, and zero runtime building LODGroups;
- vegetation recovery: PASS 7/7 Python and 5/5 scoped Unity tests; 22 Tier 0/1 placements, two draws, 12,860 triangles, one alpha-clipped material, and zero Tier-2 geometry/draws;
- ground imagery contract: PASS 7/7 Python tests; official public-domain 2019 USGS/USDA NAIP Plus source, four 2048² hot quadrants plus one 4096² 12 km fallback;
- water defer and full EditMode: PASS 98/98; active water mode is `OrthophotoOnly` and reservoir/minor-water renderer/material bindings are inactive;
- PlayMode: PASS 9/9;
- one-command `validate_product.ps1`: PASS, including product integrity, dependency closure, EditMode, PlayMode, 56-view machine Visual QA, static budget, and Android build;
- machine Visual QA: `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; all 56 production frames and 20 dedicated aerial frames were inspected at full resolution;
- lead visual review: **PARTIAL at 5.3/10**; structural defects improved, but building art, near vegetation/grove volume, and low-oblique image sharpness remain below acceptance;
- static planning budget: PASS at 55 draws, 534,976 visible triangles, 24 visible materials, 176.77 MiB estimated texture residency, two transparent slots, 11 shaders / 17 keyword combinations, and 4x MSAA;
- Android release-style build: PASS, application `com.alexoviedo.t2.questflight`, 106,789,590 bytes, SHA-256 `e97292bc1e060caf7e924998faa0c4c3939a36835cb1beb67e8a2b5a74ff9933`;
- Meta XR Simulator v201.0: live Quest 3 profile stereo/HMD/both-Touch session PASS and both full-resolution eyes inspected; aggregate `BLOCKED_MISSING_RECORDED_REPLAYS` for all ten absent `.vrs` files;
- physical Quest: short worn visual smoke **PASS/PARTIAL at 7/10** on the exact APK above; seat correction, absence of floating/size-changing buildings, visible canopy cue, and orthophoto-only water were accepted. Right-side imagery over the reservoir remained blurry while the left view looked decent. The 90+60-second timing/thermal/stale/tear/allocation gate was **NOT RUN**.

The validation summary's `PASS_1_REJECTED_2_UNKNOWN` frozen-source field is an expected fail-closed R&D extraction fixture. The product dependency closure itself is `PASS`; no rejected or unknown R&D dependency was promoted into the product.

See `docs/evidence/AERIAL_SCENERY_RECOVERY_V2_2026-07-19.md` and the external closure artifact root recorded there.

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
