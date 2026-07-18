# Airport-Core Photoreal Benchmark V4 — 2026-07-17

## Decision

**FAIL — preserve `feature/airport-core-photoreal-v4` and stop.** Stage 1 failed its second and final visual/budget run. Stage 2, Meta XR Simulator, physical Quest, and mainline merge were not entered. This record does not claim photorealism, training fidelity, navigation accuracy, FAA approval, or standalone Quest performance.

Artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_photoreal_v4_20260717_122642`

## Source and scope

- Stable source: `feature/environment-reconstruction-v2` at `eaccabc5e2cb9ccdcef30f825c69a013b83cea75`.
- Rejected V3 reference: `feature/environment-art-lighting-v3` at `46ffc235113532ff5c4204c9b3b0a11e5932b2ce`.
- Selective V3 reuse: QA-adapter-only commit `332f9aeb5503bf4818db87ae85483afbe3a6831e`, cherry-picked as `015444e742dddc1affa141b23808114f4211c3f7`, plus reviewed reference/tool concepts. No V3 broad scene, terrain, vegetation, water, HLOD, relaxed budget, or evidence result was carried forward automatically.
- Benchmark: one independently toggleable serialized root around the executive terminal, adjacent apron, Type 2 row 3354, authored props, runway edge, vegetation cluster, Boulder Reservoir, and one minor-water prototype. No full-airport or full-map rollout.

Workstream A owned references and Blender/building output; B owned atlases, clean plate, lighting, reflections, and runway; C owned bounded water/vegetation candidates; D owned QA, budget semantics, integration, and closure. The lead inspected every full-resolution image and owned the decision.

## References and assets

The external six-source reference board and receipts are under `workstream_a/reference_board`; the contact sheet is `workstream_a/reference_contact_sheet.png`. Copyrighted reference pixels were not committed. Official City facts, external photo handling, footprints, and uncertainty are documented in `docs/KBDU_AIRPORT_CORE_REFERENCE.md`.

Blender 5.1.2 generated project-owned sources and isolated LOD GLBs. Structural validation found no nonmanifold/inverted geometry violations:

| Model | LOD0 | LOD1 | LOD2 |
| --- | ---: | ---: | ---: |
| Executive terminal | 20,172 | 8,592 | 2,364 |
| Type 2 T-hangar row | 25,492 | 10,244 | 3,444 |

Two shared 2048² 4×4 PBR atlases use the five Poly Haven CC0 sources recorded in `Materials/SOURCE_MANIFEST.json` plus project-authored tiles. Raw sources remain external. The first QA run exposed a generator defect: Pillow top-origin atlas rows disagreed with Blender/Unity bottom-origin UVs. The one permitted targeted visual correction maps `image_y = size - uv_y - tile_size`; a regression test proves tile zero occupies the runtime bottom-left. No second art pass followed.

The clean plate is bounded to replacement footprints and derives from the existing public-domain NAIP atlas, with OSM-derived bounds. It removes photographed replacements without changing distant terrain, but final review found it soft and visually disconnected. It is not accepted.

## Lighting, runway, water, and vegetation

- Lighting profile: azimuth 152°, elevation 72°, confidence 0.436 (LOW); sky rotation -64.123°; one fixed sun; 20 serialized probes; zero baked reflection probes.
- Bake: two directional 2048 lightmap atlas pairs were written. The bake logged six non-finite indirect/environment/directionality values. The batch then ended non-green on a transient reservoir reference-identity check; later path-based structural validation passed the current binding. Outputs and hashes are preserved in `Assets/Scenes/ProductionVerticalSlice/BAKE_MANIFEST.json`, but the lighting is not accepted.
- Vegetation mismatch: the atlas generator used azimuth 135° / elevation 56.934°, not the final low-confidence 152° / 72° profile.
- Runway: authoritative geometry/collider/alignment were preserved, but the intended continuous material did not remove the visible large macro-block pattern in integrated views.
- Reservoir: one bounded opaque renderer and authoritative shoreline were preserved, but dense zebra-like normal striping and a hard olive shore remain. No SSR, planar reflection, refraction camera, or tessellation was introduced.
- Minor water: terrain-mask prototype only; its report explicitly marks it non-georeferenced and requiring benchmark UV binding.
- Vegetation: project-owned three-tree/shrub cluster with LODs and an alpha-tested atlas; it remains visibly procedural and its lighting mismatch prevents acceptance.

## Stage-1 visual review

All 56 final 1280×720 images were inspected at full resolution. Machine checks found every image nonblank and every individual view within its draw/triangle checks; machine integrity is not subjective quality.

| Category | Attempt 1 | Final attempt 2 | Required | Final finding |
| --- | ---: | ---: | ---: | --- |
| Executive terminal | 2.0 | 5.0 | 8.0 | Specific silhouette/signage/balcony, but washed-out brick/metal and flat integration |
| Type 2 hangar row | 2.0 | 4.5 | 8.0 | Correcter gabled/T-row form, but blown white response, repetition, and artifacts |
| Material quality | 1.0 | 4.0 | 8.0 | UV-origin bug fixed; exposure and baked response remain wrong |
| Lighting coherence | 2.0 | 3.0 | 8.0 | Strong yellow/green cast, disconnected objects, non-finite bake warnings |
| Clean-plate integration | 3.0 | 3.5 | 8.0 | Bounded replacement exists, but ground is blurred and mismatched |
| Runway | 3.0 | 3.0 | 7.0 | Large alternating/macro blocks remain |
| Water | 2.0 | 2.0 | 7.0 | Directional striping and hard olive shoreline |
| Vegetation | 3.5 | 4.0 | 7.0 | Better than absence, still card/procedural and lighting-mismatched |
| **Overall** | **2.5** | **4.0** | **7.5** | **FAIL; no scale-up** |

Evidence paths:

- final standard contact sheet: `stage1_visual_qa_attempt2/visual_qa_contact_sheet.png`;
- before sheet: `stage1_visual_qa_attempt2/airport_core_before_contact_sheet.png`;
- after sheet: `stage1_visual_qa_attempt2/airport_core_after_contact_sheet.png`;
- full-resolution images: `stage1_visual_qa_attempt2/screenshots`;
- report: `stage1_visual_qa_attempt2/visual_qa_report.json`.

## Static budgets

The V2 baseline used the former pre-LOD estimator, while V4 uses corrected selected-LOD camera semantics; those per-view values are not an apples-to-apples improvement claim. Comparable aggregate inventory grew from 171 to 198 active renderers, 683,589 to 758,268 active mesh triangles, 42 to 61 textures, and 39 to 50 LODGroups. Estimated texture memory grew from 172,971,456 to 267,867,042 bytes (+94,895,586 bytes / 90.50 MiB).

| Final V4 metric | Result | Ceiling | Gate |
| --- | ---: | ---: | --- |
| Selected-LOD estimated draws | 66 | 120 | PASS |
| Selected-LOD visible triangles | 572,280 | 600,000 | PASS |
| Visible materials | 27 | 25 | FAIL |
| Estimated texture memory | 267,867,042 B / 255.46 MiB | 180 MiB | FAIL |
| Lightmap share | 89,478,488 B / 85.33 MiB | included above | informational |
| Transparent slots | 2 | 3 | PASS |
| MSAA | 4× | 4× | PASS |

The aggregate ProductIntegrity inventory separately counts inactive A/B content and every serialized LOD: 207 renderers / 797,050 triangles / 34 materials. It fails the prebuild 180-renderer and 700,000-triangle ceilings. Editor planning evidence is not Quest hardware timing.

## Validation

| Gate | Result | Evidence |
| --- | --- | --- |
| Workstream-B atlas regression | PASS 3/3 | `stage1_atlas_origin_correction` and local unit-test output |
| V4 structural validator | PASS; 27 renderers / 74,730 tris / 3 mats | `stage1_structural_after_atlas` |
| EditMode | FAIL 67/68; sole aggregate ProductIntegrity failure | `stage1_editmode_after_atlas_retry` |
| PlayMode | PASS 9/9 | `stage1_playmode_final` |
| Final Visual QA | FAIL; 56/56 nonblank, 27/25 materials, lead 4.0/10 | `stage1_visual_qa_attempt2` |
| Static Quest planning budget | FAIL; materials and texture memory | `stage1_performance_final` |
| `validate_product.ps1` | FAIL at ProductIntegrity | `stage1_product_validation_final` |
| Android Quest build | BLOCKED before Android compilation by ProductIntegrity; no APK | `stage1_quest_build_final` |
| Meta XR Simulator | NOT RUN — Stage 1 failed | none |
| Physical Quest | NOT RUN — Stage 1 failed; no current APK | none |

The EditMode run predates the final rebake. A post-bake structural validation and later PlayMode run exist; no claim is made that the entire EditMode suite ran after the bake.

## Delivery and next bounded milestone

The feature branch is preserved and main remains unchanged. The art pipeline is not proved at the requested quality, so Stage 2 scaling would amplify bad lighting, materials, and memory cost.

The only recommended next milestone is **Airport-Core V4.1 color/lighting and runtime-budget remediation**, bounded to the existing benchmark and existing assets. It must (1) remove the global yellow/green ground-lighting mismatch and produce a warning-free bake, (2) bring terminal/hangar material response and runway/reservoir presentation to their thresholds, and (3) reduce visible materials, lightmap/texture memory, and aggregate serialized closure below the existing gates. Reuse the current reference board, Blender models, corrected atlases, clean-plate masks, serialized root, validators, and 56-view camera set. Do not download assets, expand building coverage, repeat reference/model work, start Stage 2, or involve Alex/headset until the machine Stage-1 gate passes.
