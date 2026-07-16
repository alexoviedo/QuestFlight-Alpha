# Validation

Run from the repository root:

```powershell
.\scripts\validate_product.ps1
.\scripts\run_unity_tests.ps1 -TestPlatform EditMode
.\scripts\run_unity_tests.ps1 -TestPlatform PlayMode
.\scripts\run_production_visual_qa.ps1
.\scripts\run_quest_performance_budget.ps1
.\scripts\build_production_quest.ps1
.\scripts\run_standalone_quest_gate.ps1
```

`validate_product.ps1` is the one-command product gate and includes dependency, serialization, reference, EditMode, PlayMode, standard Visual QA, static render-budget, and Android build checks. `run_quest_performance_budget.ps1` writes the planning report plus material/shader, texture-memory, LOD/culling, and qualitative overdraw evidence.

`run_standalone_quest_gate.ps1` is the one-command physical Quest gate. It detects one authorized Quest 3, builds when needed, installs and launches the production APK, reports possible Horizon/Guardian or controller dialogs, waits for readiness, preserves the exact 90-second warmup and 60-second measurement, and collects Unity frame series, VrApi CPU/GPU/stale/tear fields, thermal state, logcat, screenshots, and product evidence. It always writes both summaries before returning and exits nonzero for an attempted `FAIL_*` temporal result; explicit blocked/not-run results remain preserved and distinct from the machine-pass/human-witness-required result. Use `-Unattended` only when no human witness is available; that mode cannot promote comfort, controller, Guardian, or worn-display acceptance.

The Quest VrApi parser identifies samples by product PID and `SF=1.00`. It records `Fov` as telemetry rather than using it as an identity discriminator because supported Quest firmware has emitted product rows with both `Fov=0` and `Fov=2`. `validate_product.ps1` runs a regression fixture covering both forms and rejects a different-PID compositor row.

Editor and Meta XR Simulator gates prove dependency, serialization, budget, image-integrity, stereo API, tracking, input, composition, and build contracts within their stated limits. They do not prove comfort, handling fidelity, sustained Quest frame timing, thermals, or worn visual quality. Those claims require the physical headset.
