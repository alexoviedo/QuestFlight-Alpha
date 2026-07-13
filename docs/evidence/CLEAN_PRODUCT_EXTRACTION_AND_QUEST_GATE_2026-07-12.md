# Clean Product Extraction and Quest Gate — 2026-07-12

## Outcome

- Milestone classification: **PARTIAL**
- Product decision: **C — PIVOT TO PCVR**
- Reason: the clean standalone product architecture, tests, QA harness, and APK build pass, but the one permitted visual pass improved the lead score only from 4/10 to 5/10. The required 6/10 machine precondition was not reached, so worn Quest launches and performance claims were intentionally not attempted.

This is not a headset PASS and not evidence of training suitability, photorealism, or final Quest performance.

## Frozen R&D source

- Repository: `https://github.com/alexoviedo/T2-QuestFlightLab`
- Commit: `cd4131fa7732a6db2d7387921dfef33e2c206970`
- Immutable tag: `product-extraction-source-2026-07-12`
- Tag was pushed to `origin` and peels to the exact source commit.
- The R&D worktree remained clean and was not modified by extraction.

## Clean product repository

- Local root: `C:\Users\ovied\Dev\T2\T2-QuestFlight`
- Unity project: `C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight`
- Private remote: `https://github.com/alexoviedo/T2-QuestFlight`
- Product/package: `T2-QuestFlight` / `com.alexoviedo.t2.questflight`
- Product scene: `Assets/Scenes/ProductionVerticalSlice.unity`

## Workstreams

1. Deterministic extraction and dependency/provenance manifest.
2. Minimal Unity project, serialized scene, package/settings configuration, and build.
3. Aircraft and third-party rights audit.
4. Lead-owned Visual QA, integration, Quest-gate decision, evidence, and delivery.

## Extraction and dependency closure

- Deterministic source inventory: 338 files / 43,775,309 bytes.
- Missing allowlist entries: 0.
- Destination conflicts: 0.
- Forbidden copied files: 0.
- Hash errors: 0.
- Repeat extraction: 338/338 unchanged.
- Extractor tests: 9/9 PASS.
- Every copied Unity asset retained its paired `.meta`; current product Assets contain 195 metas, zero missing pairs, and zero duplicate GUIDs.

The frozen source scene honestly reported one rejected dependency (`Usb2BleInputMapper`) and two unclassified dependencies (`FlightTelemetry`, `ShortPlaytestDemoPilot`). The product scene/prefabs were explicitly reauthored without those references. Final Unity `AssetDatabase.GetDependencies` result:

- total dependencies: 133;
- approved product dependencies: 128;
- Unity/package dependencies: 5;
- conditional local dependencies: 0;
- rejected dependencies: 0;
- unknown dependencies: 0;
- ready: true.

Excluded product systems include `InputLab`, runtime first-view repair/bootstrap, procedural KBDU construction, splats, training/debug overlays, USB2BLE/HOTAS, JSBSim native/sidecar/backend selection, duplicate runway/water repair paths, and legacy demo/telemetry components.

## Product integrity and architecture

`ProductIntegrityValidator` PASS:

- one enabled XR Origin and one Main Camera;
- tracked/OpenXR-owned camera; no seat or camera aircraft pivot;
- one serialized aircraft simulation root and one Unity prototype flight authority;
- serialized production world/environment/runway/aircraft hierarchy;
- no missing scene, prefab, script, material, mesh, texture, or component reference;
- no duplicate product roots or excluded legacy component;
- 195 assets, 241 scene objects, 135 renderers, 441,359 total scene triangles, and 28 total scene materials.

The runtime hierarchy is serialized before frame zero. Seat calibration modifies only the seat-relative calibration transform. The aircraft rotates around its CG. The Unity prototype backend remains the sole authority.

## Aircraft and asset rights

The KOG_THORNS Cessna 172 source is explicitly licensed CC BY 4.0 and is committed through Git LFS with attribution.

- GLB bytes: 26,962,216.
- SHA-256: `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`.
- Original source: `https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4`.

The three Poly Haven textures are CC0. FAA/USGS source data is public domain; the bounded OSM derivative remains ODbL 1.0 with `© OpenStreetMap contributors` attribution. Exactly five nonignored raster/model binaries exist (one GLB, three JPG, one PNG); all have source, license, hash, paired meta, and LFS records. Public commercial release still requires Cessna/Textron branding/no-endorsement review because CC BY does not grant trademark rights.

## Unity configuration

- Unity `6000.3.8f1`.
- Android ARM64 + IL2CPP.
- Vulkan only, ASTC, multithreaded rendering, Linear color, 4x MSAA.
- OpenXR + Meta Quest feature, XR Plug-in Management, Input System, XR Core Utils, and glTFast.
- Exactly one enabled build scene.
- Gaussian splat and native JSBSim packages are absent.
- Final build log contains no JSBSim 16 KB-alignment warning.

## Visual QA and targeted pass

The lead inspected all 16 baseline and all 16 final full-resolution images individually.

| Area | Baseline | Final | Result |
| --- | ---: | ---: | --- |
| Default viewpoint | 6 | 6 | Plausible machine geometry; worn confirmation absent. |
| Cockpit | 4 | 5 | Gauges/readability improved with bounded AO and no realtime shadows; still flat. |
| Mountains | 4 | 4 | Machine-immutable but coarse/synthetic. |
| Ground | 3 | 5 | Unique field-scale variation visible; still stylized. |
| Water | 2 | 4 | Stable depth/shore/color response; still visibly synthetic. |
| Runway | 4 | 4 | Correct alignment; surface/context remains flat. |
| Aircraft exterior | 7 | 7 | Complete and readable. |
| Control animation | 6 | 6 | Deflections visible. |
| **Overall** | **4** | **5** | Below the required 6/10 gate. |

The single allowed pass addressed only cockpit readability/depth, macro-ground variation, and reservoir depth/shore/light response. It added no realtime cockpit shadow map, animated water, camera-relative material state, hierarchy, or additional draw pass.

Final machine QA:

- 16/16 views rendered; classification `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`.
- default eye local position: `(-0.28, 0.94, -0.18)` m;
- eye-to-panel distance: 0.497 m;
- maximum view estimate: 87 draws / 346,876 visible triangles / 19 visible materials;
- runway-to-terrain maximum gap: 0.004676 m;
- marking-to-runway maximum gap: 0.001000 m;
- collision-surface disagreement: 0.000000 m;
- water/terrain separation: 0.180000 m;
- mountain sweep: PASS across 25 headings, one mesh/transform/renderer identity, 0 LOD groups.

Artifacts:

- baseline contact sheet: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\extraction_20260712_095221\visual_qa_baseline_clean\visual_qa_contact_sheet.png`;
- final contact sheet: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\extraction_20260712_095221\visual_qa_postpass_clean\visual_qa_contact_sheet.png`;
- final 16 images and lead review: `...\visual_qa_postpass_clean\screenshots\` and `...\visual_qa_postpass_clean\lead_visual_review.md`;
- final clean-main aggregate validation: `...\final_clean_main_validation\`.

## APK

- Path: `C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight\Builds\Android\QuestFlight-production.apk`.
- Size: 59,547,040 bytes.
- Final clean-main validation SHA-256: `ec8af4baaff72a0b6403dc4d0cf3b7a5bc1d2e895714d94d7fdbb8e0fa523a37`.
- APK/build artifacts remain ignored and are not committed.

## Automated results

- Extractor: 9/9 PASS.
- KBDU data pipeline: 8/8 PASS.
- KBDU source/license/schema/geometry/budget validator: PASS.
- Aircraft rights/hash validator: PASS.
- Product project configurator: PASS.
- Product integrity validator: PASS.
- Dependency closure: PASS (0 rejected / 0 unknown).
- Unity EditMode: 12/12 PASS.
- Unity PlayMode: 20/20 PASS.
- Production Visual QA: 16/16 machine checks PASS; lead quality 5/10.
- Android Quest APK: PASS.

One build-only defect was found and fixed: a PlayMode file lacked the player-exclusion guard used by the other test files, causing IL2CPP to seek `nunit.framework`. The guarded test still passes in PlayMode and the production APK then builds successfully.

## Worn Quest and performance gate

The worn gate was **not started**. This was an explicit precondition failure, not a headset crash or an inferred visual PASS.

| Cold launch | Result | Reason |
| --- | --- | --- |
| 1 | NOT RUN | Machine visual score 5/10, below 6/10 precondition. |
| 2 | NOT RUN | Machine visual score 5/10, below 6/10 precondition. |
| 3 / timing run | NOT RUN | No 90-second warmup, 60-second sample, or human witness was authorized after precondition failure. |

Human scores for cockpit, mountains, ground, water, runway, aircraft, overall, comfort, and physical feel: **NOT COLLECTED**.

CPU app time, GPU time, delivered intervals, stale/tear counts, thermal state, crash/ANR/OOM state, and the p95/over-budget thresholds: **NOT MEASURED ON HEADSET**. Static draw/triangle/material estimates above are not substituted for hardware timing.

## Decision and next milestone

Choose **C — PIVOT TO PCVR**. The clean repository is worth retaining, but standalone Quest 3 did not clear the visual threshold even before the worn performance gate. A second standalone art pass would violate this milestone and would not address the clear gap between the licensed aircraft and the environment.

Exact next milestone: **PCVR Visual Ceiling Gate V1** in this clean repository. Add a Windows x64 OpenXR/Link build profile without changing the serialized authority hierarchy; replace the synthetic macro ground with a verified public-domain orthophoto-derived production crop, upgrade the far-mountain and runway/airport material presentation, then require a lead-reviewed 16-view score of at least 7/10 before one three-launch worn Quest Link/Air Link test at 90 Hz. Decide PCVR continuation only from that visual and worn timing evidence.
