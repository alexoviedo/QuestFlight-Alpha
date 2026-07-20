# Airport-Core Photoreal Benchmark V4.2 — 2026-07-18

## Decision

**FAIL VISUAL STAGE 1 — preserve `feature/airport-core-photoreal-v4-2`, do not merge or scale.** V4.2 completes the bounded static packing and geometry closure recommended by V4.1. Structural validation, dependency closure, tests, static planning budgets, machine image-integrity checks, and Android build pass. Full-resolution art review remains approximately 4.0/10 against the required 7.5/10; therefore Stage 2, Meta XR Simulator, physical Quest timing, and mainline merge were not entered.

This is not a claim of photorealism, AAA fidelity, FAA approval, navigation accuracy, training credit, stereo quality, comfort, or Quest hardware performance.

Artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_photoreal_v4_2_20260718_014806`

## Source and frozen scope

- Source branch/SHA: `feature/airport-core-photoreal-v4-1` at `e7dc206748cf88ec4748f736184d44fcd4ae1a40`.
- Working branch: `feature/airport-core-photoreal-v4-2`.
- Unity: `6000.3.8f1`.
- No new asset, material, shader, lighting-color, water, vegetation, model, physics, input, simulator, or world-coverage work was performed.
- Main remained unchanged.

Four strict workstreams were retained for ownership: A audited the geometry contributor and protected terrain regions; B reviewed the one-atlas lighting contract; C remained frozen because no water/vegetation art change was authorized; D owned validation, full-resolution review, budgets, Android build, provenance closure, and delivery. The lead owned integration and the visual decision.

## V4.2 implementation

The existing 76,800-triangle, 5 m airport visual mesh remains intact as source. A separate deterministic derivative is active only for rendering:

- 15,314 triangles, a reduction of 61,486;
- 20 m base cells outside protected areas;
- exact 5 m samples on the outer boundary, runway corridor, terminal/apron/vegetation bounds, and fuel-service bounds;
- automatic refinement whenever source-plane deviation would exceed 1.6 m;
- measured maximum unrefined deviation 1.59000015 m;
- conforming transition fans plus nonmanifold, inverted, degenerate, boundary, protected-height, and provenance validation;
- unchanged `ProductionTerrainSurfaceAsset`, colliders, runway, and physics queries.

The accepted bake uses terminal and hangar LOD0 only, 18 probes, one 512² non-directional color atlas, zero directional atlases, finite texels, zero non-finite bake diagnostics, Trilight ambient input, ASTC HDR Android import, mipmaps, and streaming. Its estimated Android texture cost is 157,782 bytes.

The Android player initially exposed one isolated compile error because `Light.lightmapBakeType` is Editor-only. The runtime benchmark validator now guards only that unsupported Editor assertion with `UNITY_EDITOR`; the fail-closed editor check remains and has dedicated regression coverage. The final Android build passes.

## Provenance

No external asset was added. The adaptive terrain derives from the existing public-domain USGS 3DEP product mesh. The GI parameters, lightmap, `LightingData.asset`, and manifests are project-authored or Unity-generated from already rights-cleared scene assets. Exact hashes are recorded in `docs/ASSET_LICENSES.md`, the terrain report, and `Assets/Scenes/ProductionVerticalSlice/BAKE_MANIFEST.json`. No raw imagery, source archive, bake cache, capture, build, or APK is committed.

Product dependency closure explicitly classifies the two benchmark runtime scripts and two generated bake assets by exact path: 223 approved dependencies, five Unity-package dependencies, zero rejected dependencies, and zero unknown dependencies.

## Full-resolution Stage-1 review

All 56 original 1280×720 frames were inspected at full resolution. The final one-command validation recaptured the same 56 images; SHA-256 comparison found 56/56 byte-identical, so the inspected findings apply exactly to the final capture. Machine checks prove nonblank images and serialized/view-budget contracts, not subjective art quality.

| Category | V4.2 | Required | Finding |
| --- | ---: | ---: | --- |
| Executive terminal | 5.5 | 8.0 | Specific facade, sign, awnings, and railings exist; wall textures are stretched and material response remains flat |
| Type 2 hangar row | 5.0 | 8.0 | Recognizable T-row silhouette and door divisions; roof/corrugation response aliases and the structure still reads synthetic |
| Material quality | 4.0 | 8.0 | Shared atlases are bounded, but close views lack convincing scale, roughness, weathering, and microcontrast |
| Lighting coherence | 5.0 | 8.0 | Direction and contact are coherent enough for structure; ambient response and object-to-orthophoto matching remain disconnected |
| Clean-plate integration | 4.0 | 8.0 | Bounded replacement exists, but the ground is soft and photographed aircraft/shadow ghosts remain conspicuous |
| Runway | 3.0 | 7.0 | Dominant alternating macro slabs and repeated surface blocks remain; not production quality |
| Water | 2.5 | 7.0 | Reservoir reflection exists, but a large opaque gray suppression polygon blankets the shoreline/adjacent land; minor water is a flat ribbon |
| Vegetation | 4.0 | 7.0 | Three-tree/shrub cluster supplies scale, but repetition, sparse canopy, hard trunks, and procedural form remain obvious |
| **Overall** | **4.0** | **7.5** | **FAIL; no Stage-2 replication** |

Supporting full-scene observations: sky/cloud photography remains the strongest environmental element; mountains are stable but faceted; orthophoto terrain is credible from altitude but soft and streaked at oblique angles; generic gray context buildings and bright parked-aircraft silhouettes break cohesion; cockpit readability is serviceable but not part of this art remediation.

Best evidence:

- `visual_qa/airport_core_after_contact_sheet.png`;
- `visual_qa/airport_core_before_contact_sheet.png`;
- `visual_qa/visual_qa_contact_sheet.png`;
- `visual_qa/screenshots` (56 inspected full-resolution PNGs);
- `visual_qa/visual_qa_report.json`.

## Static budgets

| Scope / metric | Result | Ceiling | Gate |
| --- | ---: | ---: | --- |
| ProductIntegrity serialized renderers | 171 | 180 | PASS |
| ProductIntegrity serialized triangles | 698,328 | 700,000 | PASS — 1,672 margin |
| Maximum selected-LOD draws | 63 | 120 | PASS |
| Maximum selected-LOD triangles | 506,254 | 600,000 | PASS |
| Visible materials | 25 | 25 | PASS — no margin |
| Estimated texture memory | 176,438,238 B / 168.26 MiB | 180 MiB | PASS |
| Transparent slots | 2 | 3 | PASS |
| MSAA | 4x | 4x | PASS |

The texture report conservatively estimates 26 texture subassets without a `TextureImporter`. ProductIntegrity, selected-LOD, and environment-only triangle values have different scopes and must not be compared as if they were the same measurement. No static value is Quest hardware timing.

## Validation and build

| Gate | Result | Evidence |
| --- | --- | --- |
| One V4.2 integration/GPU bake | PASS; one finite 512² atlas | `packing_bake` |
| Airport-core structural validator | PASS; 15 renderers / 77,282 tris / 3 materials | `airport_core_structural` |
| Product dependency closure | PASS; ready=true, zero blockers | `validate_product_clean_final` and `docs/extraction/reports` |
| ProductIntegrity | PASS; 573 assets / 323 scene objects / 171 renderers / 698,328 tris / 32 materials | `product_integrity` and final product validation |
| EditMode | PASS 71/71 | `editmode_clean_final` and final product validation |
| PlayMode | PASS 9/9 | `playmode` and final product validation |
| Machine Visual QA | PASS image/integrity only; 56/56 | `visual_qa` and byte-identical final recapture |
| Lead visual Stage 1 | FAIL; 4.0/10 | inspected full-resolution images above |
| Static Quest planning budget | PASS, headset measurement required | `performance_budget` |
| `validate_product.ps1` | PASS end to end | `validate_product_clean_final` |
| Android release-style build | PASS | `validate_product_clean_final/build` |
| Meta XR Simulator | NOT RUN — lead visual Stage 1 failed | none |
| Physical Quest timing/human review | NOT RUN — lead visual Stage 1 failed | none |

Final external APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`, 100,094,165 bytes, SHA-256 `24ae6262085b2b54091681ece03ad1e72311c971aec0486b1c4b377b2c2f8e98`, package `com.alexoviedo.t2.questflight`, Android ARM64/Vulkan/OpenXR/IL2CPP release-style configuration. It was not installed or measured on a headset and is not committed.

## Delivery and exact next milestone

This result is branch-only. Main must remain unchanged, and Stage 2 must not copy the current art. The exact next standalone milestone is **Airport-Core V4.3 runway and water artifact correction**, using only the existing authoritative runway geometry, runway alignment/collider tests, Boulder Reservoir shoreline mesh, water masks/normals, orthophoto, 56-view cameras, and current static budgets.

It must address exactly three defects: (1) replace the alternating runway macro slabs with a continuous nonrepeating surface while preserving geometry/markings/collider tolerances; (2) eliminate the opaque gray reservoir suppression polygon and restore a natural land/water shoreline transition; and (3) replace the flat minor-water ribbon with a terrain-integrated mask. Do not repeat reference research, download assets, remodel buildings, expand coverage, start Stage 2, alter physics/input/flight systems, or involve Alex/headset until the same full-resolution Stage-1 subset passes its runway/water thresholds and all static gates remain green.
