# Airport-Core Artifact V4.3 — 2026-07-18

## Scope and source

V4.3 was created from `feature/airport-core-photoreal-v4-2` at `949931049c6e67acbca182a31dbbc5806d6e0102`. It changed only the runway presentation, Boulder Reservoir integration, and minor-water representation. Buildings, vegetation, terrain geometry, lighting, aircraft, cockpit, physics, input, and world coverage were frozen.

Artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_v4_3_20260718_115842`.

## Root causes and corrections

### Runway

The V4.2 single runway material still exposed segmented mesh/lightmap inputs as alternating macro illumination slabs. V4.3 removes the feature-mask texture, lightmap path, per-segment UV dependence, and obsolete serialized binding. The material now derives a continuous runway-local coordinate and analytic rubber, repair, joint, edge, and roughness terms while preserving the exact authoritative mesh, grade, collider, and markings. A fixed runway-local shading frame was the one allowed corrective rerun.

Result: structural contracts and gaps pass, but broad bands remain visible in close and aerial views. Lead score 5.5/10; required 7/10 not met.

### Boulder Reservoir

The gray rectangle was not a separate water renderer. It came from the terrain shader's reservoir-suppression mask across the mask's rectangular extent. V4.3 removes that include, property, material binding, and dependency. The retained single opaque reservoir material samples existing public-domain NAIP mid imagery in the same world frame and combines it with the retained depth/shore/roughness mask, dual normal maps, static environment reflection, Fresnel, and bounded sun response.

Result: the rectangular gray blanket is absent, adjacent land is not covered, and cockpit/aerial views read as water. Lead score 7.0/10.

### Minor water

The legacy OSM-derived `KBDU_OtherWaterFeatures_Surface_V2` and `KBDU_OtherWaterFeatures_ShoreBank_V2` objects produced flat segmented ribbons. Both serialized objects were removed. A deterministic offline authoring step rasterizes the existing source geometry into one 512² packed RGBA mask: near water/bank in RG and mid water/bank in BA. The existing terrain material consumes it in globally continuous near/mid coordinates and retains the orthophoto as the color base. Far water remains orthophoto-only.

Result: 9,053 serialized triangles are removed and minor water adds no renderer, draw call, or material. The dedicated visual camera did not prove a visible representative feature, so visual acceptance remains 5.5/10 rather than inferred from structural tests.

## Provenance

No external asset was downloaded. Runway textures remain Poly Haven `asphalt_02` CC0 derivatives already recorded in `AirportCoreV4/Runway/SOURCE_MANIFEST.json`. Reservoir imagery reuses the committed public-domain NAIP derivative. Reservoir and minor-water masks/shaders are project-authored derivatives of existing recorded OSM/project sources. Exact hashes are pinned in the current runway and water manifests.

## Before/after lead review

| Category | V4.2 | V4.3 | Required | Result |
| --- | ---: | ---: | ---: | --- |
| Runway cockpit | 4.5 | 6.0 | 7.0 | fail |
| Runway close/aerial | 4.0 | 5.5 | 7.0 | fail |
| Reservoir cockpit | 3.5 | 7.0 | 7.0 | pass |
| Reservoir aerial | 3.0 | 7.0 | 7.0 | pass |
| Minor water | 4.0 | 5.5 | 7.0 | fail: insufficient visual proof |
| Terrain/water integration | 3.5 | 6.5 | 7.0 | fail |

Full-resolution evidence and exact V4.2/V4.3 contact sheets are under the artifact root. Machine Visual QA passed image-integrity and structural checks; that does not convert the subjective lead result into a pass.

## Budget delta

| Metric | V4.2 | V4.3 | Delta |
| --- | ---: | ---: | ---: |
| Active renderers | 166 | 165 | -1 |
| Selected-LOD draws | 63 | 62 | -1 |
| Selected-LOD triangles | 506,254 | 503,339 | -2,915 |
| Serialized product triangles | 698,328 | 689,275 | -9,053 |
| Maximum visible materials | 25 | 24 | -1 |
| Estimated texture memory | 168.26 MiB | 168.08 MiB | -0.19 MiB |
| Transparent slots | 2 | 2 | 0 |
| Shaders / keyword combinations | 13 / 19 | 12 / 18 | -1 / -1 |

These are static Editor planning values, not physical Quest timing evidence.

## Validation

- Focused V4.3 EditMode tests: 4/4 PASS.
- Full EditMode: 75/75 PASS.
- Full PlayMode: 9/9 PASS.
- Product validation/dependency closure: PASS.
- Production Visual QA: 56/56 machine PASS; lead visual FAIL.
- Static Quest budget: PASS.
- Android release-style build: PASS.
- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`, 99,744,675 bytes, SHA-256 `96e6bf0161b204e9d114e45200ea308e42bd8effaac1bdf5d072b1636ee69594`.
- Meta XR Simulator: NOT RUN; the lead gate failed.
- Physical Quest smoke/timing: NOT RUN; the lead gate failed.

## Decision and limitation

Decision: **FAIL — preserve and stop**. V4.3 is cheaper and technically coherent, and its reservoir correction is successful, but it does not meet the all-three-system visual requirement. It is branch evidence only and must not merge to main. No overall photorealism, Quest performance, training credit, navigation accuracy, or final fidelity is claimed.
