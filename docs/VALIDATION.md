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

`validate_product.ps1` is the one-command product gate and includes dependency, serialization, reference, EditMode, PlayMode, standard Visual QA, static render-budget, and Android build checks. The standard Visual QA is a 24-view capture set and requires inspection of every full-resolution image. `run_quest_performance_budget.ps1` writes the planning report plus material/shader, texture-memory, LOD/culling, and qualitative overdraw evidence. On the V2 feature branch its hard planning ceilings are 130 estimated draw calls, 650,000 visible triangles, 30 visible materials, 220 MiB estimated texture memory, and three transparent material slots.

The 2026-07-17 V2 closure passed `validate_product.ps1`, 61 EditMode tests, 9 PlayMode tests, 24/24 machine image checks, the static budget gate, and an Android release build. The separate reconstruction evidence adapter failed closed because it selected stale intermediate reports; see the V2 evidence document. Machine passes are not visual acceptance: lead inspection remained below 7/10, so simulator replay and physical Quest timing were deliberately not run.

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
