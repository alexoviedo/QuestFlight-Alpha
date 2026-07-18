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
