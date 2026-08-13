# T2 QuestFlight

T2 QuestFlight is a private alpha vertical slice for standalone Meta Quest 3. It contains one explicitly authored C172-style cockpit/aircraft presentation, one KBDU-inspired environment, one native JSBSim C172P flight authority, viewpoint calibration, mixed Meta hand/Touch interaction, and a guided reservoir demonstration route.

## Alpha distribution

The headset-accepted private alpha is tagged `v0.1.0-alpha.1`. Friends should
install it through the private Meta Alpha release-channel invitation, which
does not require GitHub, developer mode, a PC, or ADB. The private GitHub
prerelease remains a maintainer fallback. Tester and maintainer instructions
are in [`docs/ALPHA_INSTALLATION.md`](docs/ALPHA_INSTALLATION.md), and the
release-signing/channel procedure is in
[`docs/META_ALPHA_DISTRIBUTION.md`](docs/META_ALPHA_DISTRIBUTION.md).

## Current playable scope

- seated cockpit start on the KBDU-inspired runway;
- taxi, takeoff, climb, shallow-turn, and approach presentation loop;
- one stable baked terrain/runway environment; the current feature candidate retains fixed bare-earth geometry and adds a self-contained aircraft-centered City of Boulder 2020 aerial clipmap, so 0.333 m/texel high and 1.0 m/texel medium detail can follow the aircraft across the complete 24 km square;
- on the current feature candidate, 9,692 OSM-footprint context buildings in 16 offline batches, existing hero vegetation, terrain-map canopy shading, and one bounded fixed-world Tier-2 grove batch for rounded canopy volume;
- one CC0 photographic HDR sky and a single directional sun; reservoir and minor-water effects are deliberately deferred to an orthophoto-only presentation;
- cockpit and external aircraft views with limited control-surface animation;
- mixed Meta hand/Touch cockpit interaction, Touch flight controls, and standard
  gamepad support;
- a guided takeoff, reservoir overflight, return, approach, and recovery loop;
- standalone Quest 3 build target.

The project has no USB2BLE/HOTAS dependency, Gaussian splats, UnityPrototype
flight dynamics, or legacy runtime scene repair. Native JSBSim is the sole
product flight authority in Editor, development, and release builds. Missing
or invalid native code/data fails closed rather than changing the aircraft
model at runtime.

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

Aerial Scenery Representation V1 is preserved on `feature/aerial-scenery-representation-v1` as a **failed visual benchmark**, not a mainline or release candidate. It validates a bounded public-domain 3DEP pipeline, replaces active legacy scenery with three vegetation batches and footprint-preserving building HLOD, passes the static Quest planning budget and Android build, and removes the catastrophic giant-grove artifact found during iteration. Full-resolution lead review still scores the integrated result about **5.5/10**: near trees remain sparse, Tier-2 canopy reads as dark blobs instead of groves, low-oblique terrain remains soft, and building transition continuity is not demonstrated. Meta XR live stereo/HMD/Touch smoke passes, but all ten deterministic recordings are absent. See `docs/evidence/AERIAL_SCENERY_REPRESENTATION_V1_2026-07-18.md`.

The Aerial Scenery Recovery V2 result is the accepted **standalone scenery development baseline** recorded by tag `standalone-scenery-baseline-v1`. It removes the widespread floating-roof and scale-changing context-building path, replaces synthetic Tier-2 canopy geometry with terrain-owned canopy shading, reallocates official public-domain 2019 USGS/USDA NAIP imagery for normal flight distances, and defers water effects to the orthophoto. Product validation, 98 EditMode tests, 9 PlayMode tests, the 56-view machine capture, static planning budget, and Android build pass. A worn smoke scored 7/10 and accepted the seat and building-stability corrections and orthophoto-only water. This is a coherent development baseline, not a photorealism, release, training, or final Quest-performance claim. See `docs/evidence/STANDALONE_SCENERY_BASELINE_V1_2026-07-20.md`.

Photoreal Aerial Surface V1 is the accepted standalone Quest development baseline. It fixes the proven AEQD/UTM and generic-OBJ handedness errors, keeps lidar DSM/nDSM out of terrain displacement, compiles 9,692 OSM-footprint buildings in the correct product frame, and assigns all ordinary canopy to an aligned zero-draw terrain-map response. The rejected raised-canopy candidate remains inactive because it produced block/cliff silhouettes. Product validation, 114 EditMode tests, 9 PlayMode tests, 81/81 machine QA captures, the scoped static budget, live Meta XR stereo, and Android build pass. A worn smoke of the exact APK scored 8/10, confirmed aligned and grounded buildings, consistent ground sharpness, and no noticed stereo instability or discomfort. Rounded canopy volume and final physical Quest timing remain incomplete; this is not a literal-photorealism or release claim. See `docs/evidence/PHOTOREAL_AERIAL_SURFACE_V1_2026-07-20.md`.

Rounded Canopy Volume V1 is a bounded machine-valid delivery candidate built from the tagged Photoreal Aerial Surface V1 source. It retains the aligned terrain canopy map and adds 42 fixed-world, non-camera-facing project-authored grove clusters in one instanced draw: 22,680 added triangles, no new material, texture, transparent slot, or shader variant. Product validation, 114 EditMode tests, 9 PlayMode tests, the scoped static budget, live Meta XR stereo, and Android build pass. All ten recorded simulator replays remain absent, and the exact APK has not been worn or physically timed; the prior 8/10 worn score is not attributed to this build. See `docs/evidence/ROUNDED_CANOPY_VOLUME_V1_2026-07-20.md`.

Airport-Core Photoreal Benchmark V4 is preserved on `feature/airport-core-photoreal-v4` as a **failed, unscaled benchmark**, not a mainline or release candidate. It adds a bounded, independently toggleable terminal/hangar benchmark and reproducible art sources, but the second and final Stage-1 review scored only 4.0/10. Lighting/ground color, runway, reservoir, clean-plate blending, vegetation, material count, texture memory, aggregate integrity, and Android preflight all remain blocking. Stage 2, Meta XR Simulator, and physical Quest testing were deliberately not entered. See `docs/evidence/AIRPORT_CORE_PHOTOREAL_BENCHMARK_V4_2026-07-17.md`.

V4.2 is preserved on `feature/airport-core-photoreal-v4-2` as a **technically green but visually failed branch checkpoint**. It replaces only the visual airport patch with a protected, error-bounded 15,314-triangle derivative, accepts one finite 512² non-directional lightmap, restores full product validation and an Android build, and remains within the current static planning ceilings. Full-resolution review still scores the benchmark about 4.0/10: the runway macro slabs, opaque gray reservoir suppression area, flat minor water, generic context buildings, and procedural vegetation remain far below the 7.5/10 Stage-1 bar. Stage 2, simulator, headset timing, and mainline merge were not entered. See `docs/evidence/AIRPORT_CORE_PHOTOREAL_BENCHMARK_V4_2_2026-07-18.md`.
