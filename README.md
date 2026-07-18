# T2 QuestFlight

T2 QuestFlight is a private, early product vertical slice for standalone Meta Quest 3. It contains one explicitly authored C172-style cockpit/aircraft presentation, one KBDU-inspired environment, one Unity prototype flight authority, viewpoint calibration, deterministic demo/input support, and focused visual/performance validation.

## Current playable scope

- seated cockpit start on the KBDU-inspired runway;
- taxi, takeoff, climb, shallow-turn, and approach presentation loop;
- one stable baked terrain/runway/water environment with a shared-ENU near/mid/far orthophoto pyramid;
- on `feature/environment-reconstruction-v2`, an experimental authoritative terrain sampler and placement manifest with complete bounded building coverage, sparse mask-driven vegetation, additional water features, and offline HLOD;
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

Environment Reconstruction V2 is a preserved **PARTIAL** feature branch, not a mainline or release acceptance. Its static machine gates and Android build pass, but lead review scores the integrated imagery at about 5.2/10. The runway pattern, non-reservoir water ribbons, procedural building/vegetation quality, and terrain outliers block simulator and headset promotion.

Airport-Core Photoreal Benchmark V4 is preserved on `feature/airport-core-photoreal-v4` as a **failed, unscaled benchmark**, not a mainline or release candidate. It adds a bounded, independently toggleable terminal/hangar benchmark and reproducible art sources, but the second and final Stage-1 review scored only 4.0/10. Lighting/ground color, runway, reservoir, clean-plate blending, vegetation, material count, texture memory, aggregate integrity, and Android preflight all remain blocking. Stage 2, Meta XR Simulator, and physical Quest testing were deliberately not entered. See `docs/evidence/AIRPORT_CORE_PHOTOREAL_BENCHMARK_V4_2026-07-17.md`.

V4.2 is preserved on `feature/airport-core-photoreal-v4-2` as a **technically green but visually failed branch checkpoint**. It replaces only the visual airport patch with a protected, error-bounded 15,314-triangle derivative, accepts one finite 512² non-directional lightmap, restores full product validation and an Android build, and remains within the current static planning ceilings. Full-resolution review still scores the benchmark about 4.0/10: the runway macro slabs, opaque gray reservoir suppression area, flat minor water, generic context buildings, and procedural vegetation remain far below the 7.5/10 Stage-1 bar. Stage 2, simulator, headset timing, and mainline merge were not entered. See `docs/evidence/AIRPORT_CORE_PHOTOREAL_BENCHMARK_V4_2_2026-07-18.md`.
