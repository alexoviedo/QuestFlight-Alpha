# T2 QuestFlight

T2 QuestFlight is a private, early product vertical slice for Meta Quest 3. It contains one explicitly authored C172-style cockpit/aircraft presentation, one KBDU-inspired environment, one Unity prototype flight authority, viewpoint calibration, deterministic demo/input support, and focused visual/performance validation.

## Current playable scope

- seated cockpit start on the KBDU-inspired runway;
- taxi, takeoff, climb, shallow-turn, and approach presentation loop;
- one stable baked terrain/runway/water environment;
- cockpit and external aircraft views with limited control-surface animation;
- Touch-based seat calibration and standard gamepad/deterministic input;
- standalone Quest 3 build target.

The project has no training lessons, USB2BLE/HOTAS dependency, Gaussian splats,
native JSBSim release authority, or legacy runtime scene repair. A bounded
native JSBSim package is available only behind the sole coordinator in
Editor/development builds and falls back to the authoritative Unity prototype.

## Open and validate

Use Unity `6000.3.8f1` and open `QuestFlight/Assets/Scenes/ProductionVerticalSlice.unity`.

```powershell
.\scripts\validate_product.ps1
.\scripts\run_production_visual_qa.ps1
.\scripts\run_unity_tests.ps1 -TestPlatform PlayMode
.\scripts\build_production_quest.ps1
```

The audited KOG_THORNS Cessna172 model is CC BY 4.0 and is committed through Git LFS with attribution. `scripts/sync_local_aircraft_asset.ps1` validates the frozen source hash and reproduces the canonical import when needed; a missing or mismatched aircraft is a validation/build failure.

## Status and claims

Standalone Quest 3 is the sole shipping target. The earlier PCVR recommendation was based on a visual-score precondition, not measured standalone hardware failure, and is superseded. PCVR, Quest Link, Air Link, remote rendering, Windows shipping builds, and required-PC runtime dependencies are not supported product fallbacks.

Meta XR Simulator and a Windows/OpenXR development executable or Unity Play Mode may be used only for repeatable development and visual regression. Simulator timing is not Quest performance evidence. Worn Quest frame, thermal, comfort, and controller acceptance still require a physical headset before release. See `docs/STANDALONE_QUEST_PRODUCT_REQUIREMENTS.md`.

This prototype is not FAA approved, not for navigation, does not provide legal training credit, and does not claim certified C172 fidelity, production photorealism, broad device compatibility, or final Quest performance. See `docs/KNOWN_LIMITATIONS.md`.
