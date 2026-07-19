# Aerial Scenery Representation V1 evidence — 2026-07-18

Outcome: **FAIL — PRESERVE THE FEATURE BRANCH ONLY**. Do not merge this milestone to main and do not discard its deterministic data, geometry, source/license, or validation work.

The branch is `feature/aerial-scenery-representation-v1`, created from Built-in product authority `feature/airport-core-artifact-v4-3-1` at SHA `efb82f621a82bc9331cc45cc92294945c73b1096`. The external evidence root is:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\aerial_scenery_v1_20260718_190855`

This result is a failed visual milestone, not a claim that the deterministic pipeline or serialized integration failed. Source, license, tests, static budgets, dependency classification, and the Android build pass their bounded machine gates. Full-resolution inspection scores the environment 5.5/10, below acceptance, and all ten required Meta XR Simulator recorded replays remain missing.

## Scope and serialized representation

The milestone keeps the existing explicitly authored product scene and Built-in Forward renderer. It adds serialized Aerial Scenery V1 terrain, vegetation, building, and manifest assets; it does not add runtime hierarchy repair, runtime terrain generation, a camera-relative world, PCVR, Quest Link, Air Link, or remote rendering. The shipping architecture remains self-contained Android ARM64, Vulkan, OpenXR single-pass instanced, ASTC, IL2CPP, and 4× MSAA.

The intended representation tiers are:

- airport hero, 0–500 m: existing hero buildings and operational surfaces, detailed fixed terrain, and the closest authored vegetation;
- near, 500–1,500 m: fixed-card tree representations and true-footprint buildings;
- mid, 1,500–4,000 m: fixed grove aggregates and footprint-preserving roof/building groups;
- far, beyond 4,000 m: orthophoto, fixed DEM terrain, canopy height/normal/occlusion maps, and no ordinary far-building geometry.

This architecture is preserved because its spatial and source contracts are materially stronger than the prior centroid-grid/box presentation. It is not accepted as visually complete.

## Lidar terrain evidence

The source is USGS 3DEP work unit `CO_DRCOG_1_2020`, a United States government work/public-domain dataset. The frozen depth-8 snapshot contains 758 LAZ nodes and 22,373,242 decoded points; 18,834,395 points fall inside the exact 4,096 m square product bounds. Raw EPT/LAZ data and processing caches remain external.

The final fixed visual near-terrain mesh contains 46,426 vertices and 92,000 triangles, meeting its preferred triangle budget. It preserves the exact 4,096 m square boundary and a deterministic runway-plane blend. Mesh-to-authored-target errors over all 1,048,576 raster sample centers are:

| Population | MAE (m) | p95 (m) | Maximum (m) |
| --- | ---: | ---: | ---: |
| Global | 0.16947 | 0.65324 | 10.4941 |
| Outside declared high-relief/discontinuity outliers | 0.10747 | 0.37577 | 1.46116 |

The declared-outlier mask covers 252,865 pixels, or 24.1151% of the grid. Outside that mask the fixed geometric eligibility thresholds pass. These are mesh-to-raster reconstruction measurements, not surveyed real-world accuracy. The manifest explicitly does not establish a 0.05 m operational tolerance, navigation accuracy, current surface condition, or certified fidelity.

The airport visual patch was conformed only at its outer boundary to the fixed lidar surface; the source lidar mesh, runway mesh/collider, authoritative terrain-query asset, and physics authority remain unchanged.

Evidence:

- committed report: `QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_NearTerrain_ErrorReport.json`;
- committed integration reports: `QuestFlight/Assets/Production/Environment/AerialSceneryV1/TerrainIntegration`;
- external QA images: `lidar\qa_92k` under the evidence root.

## Vegetation representation

The vegetation geometry, source Blender file, atlas, surface texture, shader, prefab authoring, and configuration are project-authored. Distribution and the final R8 geometry/canopy ownership mask are deterministic derivatives of the shared public-domain USGS 3DEP lidar products. No external vegetation asset is used.

The serialized manifest records 22 near tree instances, 248 mid grove clusters, and 323 canopy components. Tier selection is 3 closest instances, 19 additional near instances, and 248 mid aggregates; the far tier uses bounded canopy maps rather than ordinary far tree meshes. This satisfies the source/ownership contract but does not satisfy visual quality: close cards and silhouettes remain conspicuous, grove aggregates remain visibly synthetic, and aerial canopy response lacks convincing density and variation.

## Building representation and closure

The building spatial contract covers all 27,679 bounded OSM building features exactly once:

- 9,695 buildings have authored geometry: 26 hero-core, 1,152 near, and 8,517 mid;
- 17,984 ordinary far buildings are orthophoto-only feature indices with zero replacement geometry;
- maximum footprint-area ratio is 1.100000 against the inclusive 1.100000 limit;
- lidar height overrides are zero because the source has no qualified per-building vertical-error evidence; all heights use the pinned deterministic fallback.

The standalone building prefab has 23 generated mesh dependencies and one shared material. `KBDU_BuildingRuntimeAssetInventoryV1.json` records path, byte count, SHA-256, source, and license for those 23 meshes plus the prefab: 24 assets, 6,455,715 bytes, all verified byte-for-byte. Building geometry remains an OSM ODbL-1.0 derivative with required `© OpenStreetMap contributors` attribution; Unity serialization and presentation are project-authored.

The standalone prefab's closure contains no legacy combined prefab and no apparent 4–6 km replacement mesh. Product-package closure is less clean: `ProductionEnvironmentRoot.prefab` still references the older combined context prefab. Its 20 legacy building groups are serialized inactive, but the current dependency export still contains 57 older building mesh assets, including 12 older 4–6 km LOD assets. They are not active duplicate renderers, but inactive objects do not remove their mesh dependencies from the package. Therefore this milestone does not claim that legacy building geometry is absent from the product closure.

Building evidence is under `validation\building_final` in the external evidence root. `validation\building_final\building_final_summary.md` records the exact commands and deterministic regeneration; `validation\building_final\source_license_dependency_audit.json` records the source/license and closure result.

## Inspected visual result

The lead inspected the final 1920×1080 fixed-camera set and the available live simulator stereo pair. Machine nonblank/image checks do not determine these scores.

| Category | Final score | Result |
| --- | ---: | --- |
| Near terrain | 5.0 | Fail |
| Aerial landscape | 5.8 | Fail |
| Near vegetation | 3.0 | Fail |
| Grove representation | 3.5 | Fail |
| Aerial canopy | 4.5 | Fail |
| Building spatial fidelity | 8.0 | Pass for this bounded category |
| Building LOD continuity | 7.0 | Meets the bounded category threshold |
| Overall environment | **5.5** | **Fail** |

The building footprint and transition work is worth preserving. It does not offset the low vegetation, grove, canopy, terrain, and overall scores. The final environment must not be described as photoreal, production-photoreal, complete, or physically faithful.

Final fixed-camera evidence is under:

- `validation\aerial_visual_qa_final_scaled_canopy\aerial_scenery_visual_qa.json`;
- `validation\aerial_visual_qa_final_scaled_canopy\screenshots`;
- `validation\aerial_visual_qa_final_scaled_canopy\aerial_scenery_before_after_contact_sheet.png`.

These captures remain external and must not be committed.

## Static planning budget

The final static Editor report passes its configured planning limits:

| Metric | Final value | Configured limit |
| --- | ---: | ---: |
| Maximum estimated draw calls | 57 | 120 |
| Default authored-view selected triangles | 527,199 | 600,000 visible-triangle limit |
| Serialized scene-mesh triangles | 683,452 | reported, not a headset measurement |
| Maximum visible materials | 24 | 25 |
| Estimated texture bytes | 182,930,546 | 188,743,680 |
| Transparent material slots | 2 | 3 |
| MSAA | 4× | 4× configured |

Evidence: `validation\performance_explicit\performance_budget_report.json` under the external root.

This is `STATIC_EDITOR_PLANNING_EVIDENCE_ONLY`. It establishes neither Quest GPU/CPU timing nor runtime residency, thermal behavior, stale/tear rate, comfort, or a sustained frame rate.

## Simulator, tests, and build

Meta XR Simulator v201.0 made a successful live Meta OpenXR connection using the Quest 3 / 72 Hz development profile. The live `cockpit_neutral` session passed stereo/HMD/Touch machine checks and preserved the prior OpenXR and Meta persistent configuration.

The aggregate simulator result remains **`BLOCKED_MISSING_RECORDED_REPLAYS`**. All ten required genuine VRS recordings are absent: `cockpit_neutral`, `head_pan`, `rear_view`, `viewpoint_calibration`, `control_surface_sweep`, `taxi`, `takeoff_roll`, `rotation_climb`, `shallow_turns`, and `final_approach`. No replay was synthesized, renamed, or substituted. Simulator evidence proves API-level stereo, tracking, input, and composition only; it is not Quest performance evidence.

The exact simulator records are `validation\simulator_final\meta_xr_simulator\qa_run\simulator_qa_run_report.json` and `validation\simulator_final\meta_xr_simulator\visual_gate\simulator_visual_summary.md`. The standard product machine gates also completed:

- explicit final EditMode rerun: 91/91 passed;
- explicit final PlayMode: 9/9 passed;
- standard machine Visual QA: pass with human review required;
- dependency classification: pass, with the disabled legacy-building package residual disclosed above;
- non-development Android build: pass; the APK remains an external build artifact and must not be committed.

Machine evidence is in `validation\editmode_explicit_final_rerun\unity_EditMode_test_results.xml`, `validation\playmode_explicit_final\unity_PlayMode_test_results.xml`, and `validation\build_explicit_final\unity_product_build.log`.

No physical Quest timing or thermal acceptance was run for this failed visual milestone.

## Source and license boundary

- USGS 3DEP lidar input: United States government work/public domain; raw EPT/LAZ remains external.
- Building footprints: OpenStreetMap ODbL-1.0 derivative; retain `© OpenStreetMap contributors` and the machine-readable compact derivative when distribution requires it.
- Vegetation geometry, atlas, shaders, and authoring: project-authored; distribution and handoff maps are project-authored derivatives of public-domain USGS input.
- Existing terrain, imagery, airport, water, sky, and parked-aircraft assets retain their previously recorded licenses; this milestone does not broaden those rights.
- No raw download, capture, APK, build directory, cache, or package cache belongs in Git.

Canonical source and runtime inventories are:

- `QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json`;
- `QuestFlight/Assets/Production/Environment/AerialSceneryV1/Vegetation/SOURCE_MANIFEST.json`;
- `QuestFlight/Assets/Production/Environment/AerialSceneryV1/Buildings/KBDU_BuildingSpatialContractV1.json`;
- `QuestFlight/Assets/Production/Environment/AerialSceneryV1/Buildings/KBDU_BuildingRuntimeAssetInventoryV1.json`;
- `QuestFlight/Assets/Production/Environment/AerialSceneryV1/KBDU_AerialSceneryManifestV1.asset`.

## Decision

Preserve and push the feature branch for its deterministic lidar conversion, conformed visual boundary, four-tier vegetation contracts, true-footprint building representation, source/license inventories, and validation tooling. Do not merge it to main as an accepted environment milestone.

The next bounded pass must materially improve near vegetation, grove silhouettes, aerial canopy integration, and near/aerial terrain presentation while retaining the accepted building spatial contract and remaining inside the current static limits. Re-run full-resolution inspection and all ten genuine simulator replays before requesting physical Quest timing.

This document makes no claim of FAA approval, navigation suitability, training credit, certified fidelity, production photorealism, broad compatibility, Quest performance, or physical-headset acceptance.
