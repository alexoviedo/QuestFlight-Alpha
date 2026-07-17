# Photoreal Standalone Environment V1 evidence — 2026-07-16

## Outcome

- Milestone classification: **PARTIAL / NOT COMPLETE**.
- Standalone decision: **NARROW STANDALONE QUEST SCOPE**.
- Shipping target remains self-contained Meta Quest 3: Android ARM64, Vulkan, OpenXR, 72 Hz. PCVR, Quest Link, Air Link, remote rendering, and a required PC remain out of scope.
- The scene, test, static-budget, dependency-closure, and Android build gates passed. Machine review reached **6.0/10**, below the 7.0 target, and all ten genuine Meta XR Simulator recordings remain missing. A later immutable checkpoint APK launched successfully on Quest 3, but Alex's worn review scored the environment **4/10** and rejected it. The three-run physical timing gate was therefore not started.
- This is not evidence that standalone Quest is infeasible. No timing or thermal result exists for the rebuilt APK, so neither a standalone failure nor a PCVR fallback is supported.

The milestone began from product SHA `c7393beebd97362e78ccb8c5d0579045b5fad852` on `main`. The external evidence root is:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\photoreal_environment_v1_20260716_153934`

This milestone was restricted to environment presentation and its validation. Flight physics, input, aircraft and cockpit remodeling, training systems, and JSBSim were frozen. No JSBSim change, promotion, integration, or authority change was made; the Unity prototype remains authoritative. Historical evidence was not rewritten.

## Environment implementation

### HDR sky, atmosphere, and lighting

Poly Haven **Kloppenheim 03 (Pure Sky)** was selected from the three approved candidates. Poly Haven classifies it as a medium-contrast, partly-cloudy midday pure sky. It is CC0 1.0.

- Official 4K source SHA-256: `f906a7334e5d57007fa5516bd141df2ada8fdc3d2ba0c8bdf445d64f1bbb010a`; it remains external and uncommitted.
- Committed linear-HDR 2048×1024 derivative SHA-256: `ffd28511add85d9508b51f50d3bbe4d822f4a5240800edb4f014edb75abdb1aa`.
- Android import: 1024-face cubemap, mipmapped ASTC HDR 6×6; estimated 3,757,728 bytes.
- Runtime setup: cubemap skybox exposure `0.74`, one directional sun intensity `1.00`, sky ambient intensity `0.86`, static reflection intensity `0.76`, and linear haze from 5,200 m to 20,500 m.
- The same cubemap supplies the sky, ambient environment, static scene reflection, and reservoir reflection. No realtime reflection probe, planar reflection, SSR, transparent cloud dome, volumetric cloud renderer, or per-object outdoor light was added.
- The aircraft uses a bounded projected blob shadow rather than broad realtime world shadows. Its two project-authored dependencies were added to the product dependency allowlist as individual entries, not by broadening the legacy dependency boundary.

Source and derivative details are in `QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json`.

### Continuous KBDU terrain imagery

The macro imagery is a bounded 2019 USGS/USDA NAIP Plus natural-color public-domain derivative served by `USGSNAIPPlus`. Acquisition dates are 2019-08-03 and 2019-09-19. The pinned service, raster IDs, requests, extents, processing versions, source hashes, and output hashes are recorded in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`. Raw exports and processing intermediates remain outside Git.

| Level | Coverage | Runtime image | Nominal density | SHA-256 | Android contract |
| --- | ---: | ---: | ---: | --- | --- |
| Near | 4 km × 4 km | 4096×4096 | 0.9766 m/px | `446e5c7fa13fc653de1f3e8404829c0e54b7e919fe99ebfba79d747a0b1dadea` | ASTC 6×6, mip streaming, aniso 8, bias -0.15 |
| Mid | 12 km × 12 km | 4096×4096 | 2.9297 m/px | `5e8d6edbcc5dc937c78b5fb8e4a5f8f7a06bda726b4509684e237f2b17fbdf9a` | ASTC 6×6, mip streaming, aniso 8, bias -0.15 |
| Far | 24 km × 24 km | 2048×2048 | 11.7188 m/px | `799be878d5c6be73b5596cbea70ee4e122859a528231608e40796f1020617b33` | ASTC 6×6, mip streaming, aniso 8, bias -0.10 |

One shared terrain material uses continuous KBDU local-ENU coordinates. Smooth overlap bands cover 1,650–2,000 m and 5,000–6,000 m; near/mid microdetail fades before the far range. The three-level estimated Android mip-chain cost is 22,420,784 bytes. Machine contracts found all levels, mipmaps, continuous mapping, overlaps, and a far texture. Fixed-camera inspection no longer showed the former gross photo/generated-color ring, but strict review still found soft low-oblique imagery and insufficiently convincing transition/detail continuity. The subjective terrain-continuity score was 6.8/10 and ground was 5.7/10, so the visual seam/quality objective is not accepted.

### Mountain and horizon decision

The product retains **Option A**, the single immutable USGS-derived far DEM mesh, now using the aligned far NAIP layer and the coherent sky/haze model. The mesh remains world anchored with 63,841 vertices and 89,280 triangles, offline normals and skirts, one renderer, no `LODGroup`, no crossfade/dither, no billboard, no camera-relative scaling, and no runtime rebuilding. The machine sweep preserved mesh and transform identity at all 25 headings from -180° through 180°.

Option B, a same-data 360° horizon panorama, was retained only as an external source-shape comparison. The two source-only A/B images are recorded under `workstream_a/mountain_ab_reference`; they are not integrated stereo evidence. Because the genuine simulator recordings could not be produced, the required integrated Meta XR Simulator A/B comparison remains incomplete. The strict mountain score was 6.3/10, with residual faceting and synthetic ridge treatment. `docs/MOUNTAIN_HORIZON_V2_DECISION.md` records the decision and evidence boundary.

### Boulder Reservoir

The reservoir keeps the OSM-derived, triangulated shoreline and stable terrain separation. One 252-triangle opaque `ZWrite` surface uses one renderer and one shared material with:

- project-authored 512×512 broad-wave and ripple normal maps;
- a 512×512 artistic distance-to-shore depth, shore-band, and roughness mask;
- dual normal scrolling, bounded Fresnel, selected-sky cubemap reflection, one sun-specular term, depth color, and a shore-light band;
- one pass, four texture samples per fragment, no transparency slot, no SSR, planar camera, refraction camera, realtime probe, tessellation, or per-frame mesh deformation.

The three maps estimate to 475,296 Android ASTC 6×6 bytes. The mask is explicitly not surveyed bathymetry. Water became recognizably reflective and calmer, but the simplified polygon shoreline and limited depth transition remain visible; strict review scored it 6.2/10 rather than the required 7.0. Provenance and exact asset hashes are in `tools/kbdu_reservoir_water/asset_manifest.json`.

### Airport and world scale cues

- 2,500 bounded OSM footprint extrusions are offline spatially combined into 23 building LOD groups.
- The project-owned airport kit supplies three hangar variants, a fuel/service structure, fencing/signage, and ramp props using shared materials and non-crossfaded LODs.
- 246 deterministic vegetation cues, including six project-authored airport-perimeter cottonwoods, plus 24 shrub companions are offline combined into eight context LOD groups; additional authored airport clusters are serialized separately.
- Three exterior-only static parked aircraft are combined per LOD into one group and share one opaque material. They are adapted from KOG_THORNS' Cessna172 under CC BY 4.0; interiors, glass, animation, original materials/textures, and branding presentation were removed. They are scale cues, not flyable aircraft.

OSM-derived objects are visual context, not surveyed structures or a current aeronautical database. The context uses shared materials, offline batching, and non-crossfaded LOD groups. Nine materials have Unity's instancing flag enabled, but actual GPU instancing is not claimed for a custom shader without compiled-variant evidence; the milestone's broad GPU-instancing requirement therefore remains a documented deviation. Strict review found the airport kit and vegetation visibly simplified, scoring airport/world detail 4.7/10 and vegetation/world scale 4.2/10.

## Visual QA

The final machine gate is `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`: 24/24 deterministic images were nonblank, scene and hierarchy contracts passed, the mountain identity sweep passed, and the maximum per-view estimates remained inside the static limits. The lead inspected every one of the 24 full-resolution 1280×720 V3 images. Machine PASS establishes image integrity and scene contracts, not visual quality.

Evidence:

- report: `final_validate_v3/visual_qa/visual_qa_report.json`;
- full-resolution images: `final_validate_v3/visual_qa/screenshots`;
- contact sheet: `final_validate_v3/visual_qa/visual_qa_contact_sheet.png`;
- before/after sheet: `final_visual_qa_v6/visual_qa_before_after.png`.

The final strict independent scores are:

| Category | Baseline | Final | Target | Result |
| --- | ---: | ---: | ---: | --- |
| Sky / clouds | sky 3.5; clouds 1.0 | 7.8 | 8.0 | Miss |
| Lighting | sunlight 4.0; atmosphere 3.0 | 5.8 | — | Review only |
| Terrain continuity | ring boundaries 3.0 | 6.8 | 8.0 | Miss |
| Ground | density consistency 4.0 | 5.7 | 7.0 | Miss |
| Mountains | 5.0 | 6.3 | 7.0 | Miss |
| Water | 3.0 | 6.2 | 7.0 | Miss |
| Airport / world detail | airport 3.5 | 4.7 | 6.5 | Miss |
| Vegetation / world scale | vegetation 3.0 | 4.2 | 6.5 | Miss |
| Cockpit | — | 6.1 | — | Regression review |
| Runway | 5.5 | 5.5 | — | Regression review |
| Aircraft exterior | — | 7.2 | — | Regression review |
| Overall environment | 4.4 | **6.0** | **7.0** | **Miss** |

The result is a material improvement over baseline, especially in sky, removal of the gross generated-color perimeter, reservoir response, and world context. It does not support a claim of literal photorealism or completion of the milestone's subjective acceptance target.

## Static rendering and memory budget

These are Editor/static planning estimates. They do not establish Quest CPU, GPU, frame, residency, thermal, or worn-display performance.

| Metric | Baseline | Final | Delta | Limit | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Maximum estimated visible draw calls | 57 | 102 | +45 | 120 | Pass |
| Maximum estimated visible triangles | 388,870 | 468,139 | +79,269 | 500,000 | Pass |
| Maximum visible materials | 19 | 23 | +4 | 30 | Pass |
| Estimated texture bytes | 157,555,880 | 172,347,648 | +14,791,768 | 230,686,720 (220 MiB) | Pass |
| Textures | 35 | 41 | +6 | — | Reported |
| Shaders | 7 | 11 | +4 | — | Reported |
| Shader/keyword combinations | 14 | 18 | +4 | — | Reported; not compiled variant count |
| Transparent material slots | 1 | 2 | +1 | 2 | Pass |
| LOD groups | 14 | 46 | +32 | — | 0 crossfade groups |

The final active-scene material inventory contains 29 distinct materials across 160 renderer slots, while the maximum visible material count used for the acceptance budget is 23. The configuration retained 4× MSAA, ARM64-only IL2CPP, Vulkan-only Android graphics, ASTC, single-pass-instanced OpenXR, multithreaded rendering, mipmaps, and streaming mipmaps. The final classification is `PASS_STATIC_QUEST_BUDGET_HEADSET_MEASUREMENT_REQUIRED`.

Evidence: `final_validate_v3/performance_budget/performance_budget_report.json`, `texture_memory_report.json`, `material_shader_report.json`, `lod_culling_report.json`, and `overdraw_captures`.

## Meta XR Simulator

Meta XR Simulator `201.0.0.25.633:0` was installed and exercised with the Quest 3 profile and a requested 72 Hz development target. A fresh live run connected through the Meta OpenXR runtime with stereo HMD and Touch state. The player requested the runtime through `XR_RUNTIME_JSON`, but the v201 frontend also changed HKLM64 from the Oculus runtime to the simulator runtime and stored the prior value in `PreviousActiveRuntime`. The corrected runner detected that mutation, allowed Meta's installed elevated deactivation only after matching the exact prior runtime and unchanged HKLM32 view, then verified that Oculus was restored and `PreviousActiveRuntime` removed. Meta persistent configuration was restored, and the shipping Android configuration was not changed. Final fields: `activeRuntimeMutationDetected=true`, `activeRuntimeRestorationStrategy=MetaPairedDeactivationScript`, `activeRuntimePreserved=true`, and `simulatorStateRestorationPassed=true`.

The integrated simulator result is **`BLOCKED_MISSING_RECORDED_REPLAYS`**. Windows computer control could not connect through the host sandbox-policy handoff, so the simulator recording UI could not be operated. No file was fabricated, renamed, or substituted. All required genuine `.vrs` recordings remain missing:

`cockpit_neutral`, `head_pan`, `rear_view`, `viewpoint_calibration`, `control_surface_sweep`, `taxi`, `takeoff_roll`, `rotation_climb`, `shallow_turns`, and `final_approach`.

The live stereo contact sheet exists at `meta_xr_simulator/visual_gate/simulator_stereo_contact_sheet.png`; setup and gate records are `meta_xr_simulator/setup/meta_xr_simulator_setup_report.json`, `meta_xr_simulator/qa_run/simulator_qa_run_report.json`, and `meta_xr_simulator/visual_gate/simulator_visual_report.json`. This is API/stereo/tracking/composition evidence only. No simulator timing is reported as Quest performance.

## Validation and Android build

- Integrated product validation V3: **PASS**. The dependency closure had zero missing allowlist entries after individually approving the two project-authored blob-shadow dependencies.
- EditMode: **51/51 passed**, 0 failed, 0 skipped, including the single-pass-stereo shader contract.
- PlayMode: **9/9 passed**, 0 failed, 0 skipped.
- 24-view machine Visual QA: **PASS**, with subjective and headset review still required.
- Static Quest budget: **PASS planning-only**.
- Android product build: **PASS**, non-development APK.

Primary final records are under `final_validate_v3`; the strict before/after review sheet remains under `final_visual_qa_v6`.

The exact rebuilt APK is external build output and must not be committed:

- path: `C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight\Builds\Android\QuestFlight-production.apk`;
- size: 88,638,717 bytes;
- SHA-256: `d77199d2a9343bf2618b9d46767e8a6463094628185e68bb2638b07293f98907`.

## Physical Quest and human review

Quest 3 serial `2G0YC5ZG8907TD` was connected for a later checkpoint acceptance attempt. The exact immutable APK was 88,644,741 bytes with SHA-256 `b51b135797e5f9abaa9af13ac85edb8bff9e51ef2c1d2b154b605191619a6d0e`, built from checkpoint `ba83df09aee699db9a4cdd19bb41ba4ec6742cee`. It installed successfully, acquired PID `13135`, reported the production readiness marker, and showed no active Guardian/controller dialog.

Alex's worn Quest 3 review is authoritative and rejected the checkpoint at **4/10**:

- the default eye position remained slightly too close and required a very small physical step backward;
- mountain ridge boundaries showed aliasing;
- ground shimmer, visible resolution transitions, and geometric near terrain remained;
- the water no longer flickered or detached, but was present in only one place and looked synthetic, hard-edged, and without believable depth;
- the runway did not obviously float but remained unrealistic;
- immediate airport buildings were mostly positioned reasonably but were low quality, while much surrounding context was absent;
- parked aircraft had incorrect positions, approximately 90-degree orientation errors, and ground penetration;
- other objects clipped or sat incorrectly, repeated trees exposed one low-poly source, and much of the map remained flat and empty;
- viewpoint calibration was not tested because the expected interaction was not clearly presented;
- no significant discomfort or nausea occurred.

The three-run 90-second-warmup/60-second-measurement gate was intentionally not started after the visual rejection. No FPS, CPU/GPU timing, stale/tear, thermal, or allocation acceptance is attributed to this APK. Machine-launch evidence is under `checkpoint_acceptance_20260717_004626`; the rejected-checkpoint preservation bundle and follow-on record are under `environment_reconstruction_v2_20260717_015625`.

## Remaining blockers and next milestone

The decision is **NARROW STANDALONE QUEST SCOPE**, not STOP and not PCVR. The checkpoint is preserved but must not merge to main. The next bounded standalone milestone is Environment Reconstruction V2: one authoritative terrain/placement system, corrected grounded airport objects, complete multiscale nearby context, terrain-integrated water, and Quest-bounded HLOD within the current KBDU area. Alex must not be asked to repeat this baseline checklist before meaningful changes are integrated.

Until those steps pass, this milestone must not be described as complete, photoreal, Quest-performance accepted, comfortable, FAA approved, certified, or eligible for training credit.
