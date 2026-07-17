# T2 QuestFlight

T2 QuestFlight is a private, early product vertical slice for standalone Meta Quest 3. It contains one explicitly authored C172-style cockpit/aircraft presentation, one KBDU-inspired environment, one Unity prototype flight authority, viewpoint calibration, deterministic demo/input support, and focused visual/performance validation.

## Current playable scope

- seated cockpit start on the KBDU-inspired runway;
- taxi, takeoff, climb, shallow-turn, and approach presentation loop;
- one stable baked terrain/runway/water environment with a shared-ENU near/mid/far orthophoto pyramid;
- one CC0 photographic HDR sky, a single directional sun, and a bounded opaque reservoir treatment;
- cockpit and external aircraft views with limited control-surface animation;
- Touch-based seat calibration and standard gamepad/deterministic input;
- standalone Quest 3 build target.

The project has no training lessons, USB2BLE/HOTAS dependency, Gaussian splats,
native release flight authority, or legacy runtime scene repair. Native
flight-dynamics work is frozen and out of scope for this environment milestone;
the Unity prototype remains authoritative.

## Open and validate

Use Unity `6000.3.8f1` and open `QuestFlight/Assets/Scenes/ProductionVerticalSlice.unity`.

```powershell
.\scripts\validate_product.ps1
.\scripts\run_production_visual_qa.ps1
.\scripts\run_quest_performance_budget.ps1
.\scripts\run_simulator_visual_gate.ps1
.\scripts\run_unity_tests.ps1 -TestPlatform PlayMode
.\scripts\build_production_quest.ps1
.\scripts\run_standalone_quest_gate.ps1
```

The audited KOG_THORNS Cessna172 model is CC BY 4.0 and is committed through Git LFS with attribution. `scripts/sync_local_aircraft_asset.ps1` validates the frozen source hash and reproduces the canonical import when needed; a missing or mismatched aircraft is a validation/build failure.

## Status and claims

Standalone Quest 3 is the sole shipping target. The earlier PCVR recommendation was based on a visual-score precondition, not measured standalone hardware failure, and is superseded. PCVR, Quest Link, Air Link, remote rendering, Windows shipping builds, and required-PC runtime dependencies are not supported product fallbacks.

Meta XR Simulator and a Windows/OpenXR development executable or Unity Play Mode may be used only for repeatable development and visual regression. Simulator timing is not Quest performance evidence. Physical acceptance is a 72 Hz gate using three distinct unrecorded Quest runs, each with a 90-second warmup and 60-second measurement window. Worn frame cadence, thermals, comfort, controller/Guardian state, and display acceptance still require the physical headset before release. See `docs/STANDALONE_QUEST_PRODUCT_REQUIREMENTS.md` and `docs/VALIDATION.md`.

This prototype is not FAA approved, not for navigation, does not provide legal training credit, and does not claim certified C172 fidelity, production photorealism, broad device compatibility, or final Quest performance. See `docs/KNOWN_LIMITATIONS.md`.
