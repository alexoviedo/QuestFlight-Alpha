# Standalone Quest Project Cleanup V1 — 2026-08-07

## Scope and source

- Repository: `C:\Users\ovied\Dev\T2\T2-QuestFlight`
- Accepted functional source: `cadf9af572e226cba80bb6798837ae8ff60c21f6`
- Accepted tag: `quest-mixed-input-flight-stability-v1`
- Runway checkpoint: `36308963a384ea25997587c3e40996c4126bb2c7`
- Cleanup branch: `integration/standalone-quest-cleanup-v1`
- `origin/main` at audit: `f26cb45c743a2d019d8e57a262f3550c1c8a63fe`

This checkpoint preserves the user-accepted mixed Meta OVR hand/Touch interaction,
stable scripted takeoff, cockpit state, scenery, and KBDU runway presentation. It
does not introduce a new product feature or claim release readiness.

## Safety and repository audit

Before cleanup, all refs and dirty worktrees were preserved under:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\project_cleanup_20260807_111548`

The preservation set includes a verified all-refs Git bundle, binary patches,
untracked-file archives, and a complete archive of the dirty Meta harness
worktree. SHA-256 values and sizes are recorded in `preservation_manifest.md`.
`git lfs fsck` passed. `git fsck` found no corruption; only unreachable recovery
objects were reported. No stash existed.

## Validation-hygiene corrections

- Added truthful dependency-closure classifications for the already-shipping
  four-pedal assembly, isolated compass, and Meta OVR interaction components.
- Updated cockpit tests to the authored 12-instrument shared batch, isolated
  magnetic compass, and 18 represented pedal/rudder/brake targets.
- Kept the disabled incomplete three-pedal source as an explicit frozen source
  exception while requiring every other cockpit renderer to be enabled.
- Fixed Main Camera tests and budget tooling to count only the one enabled,
  active, tagged OpenXR camera; standard disabled OVR eye-camera objects remain
  allowed.
- Fixed the static-budget mesh counter for skinned renderers.
- Added the isolated compass to cockpit visibility ownership and disabled its
  motion-vector output, along with the four-pedal assembly.
- Directed dependency-closure output to the requested artifact directory so
  validation no longer rewrites tracked evidence.
- Made canopy heatmap QA use a temporary material clone, eliminating tracked
  terrain-material drift.
- Made Android builds snapshot and restore the exact XR preloaded settings
  subassets, eliminating tracked `ProjectSettings.asset` drift.

## Results

| Gate | Result | Evidence |
|---|---|---|
| EditMode | PASS — 248/248 | `editmode_final/unity_EditMode_test_results.xml` |
| PlayMode | PASS — 9/9 | `playmode_final/unity_PlayMode_test_results.xml` |
| Dependency closure | PASS — ready, 372 approved product dependencies | `dependency_closure_candidate/final_product_closure.json` |
| Full-resolution Visual QA | Images inspected; 109/109 nonblank with no magenta fallback | `visual_qa_final/visual_qa_contact_sheet.png` |
| Static Quest planning budget | **FAIL** | `performance_budget_measured/performance_budget_report.md` |
| Android ARM64/Vulkan/OpenXR build | PASS | `build_final/product_apk_manifest.json` |

The Visual QA wrapper correctly returns failure because the Unity report includes
the static-budget failure. The images themselves are coherent and the QA run no
longer mutates source assets. This distinction is intentional and is not a
relaxation of acceptance criteria.

## Static budget blocker

The measured integrated scene is above the existing planning ceilings:

| Metric | Measured | Ceiling |
|---|---:|---:|
| Selected draw calls | 77 | 75 |
| Visible triangles | 675,833 | 650,000 |
| Visible/active materials | 36 | 29 |
| Estimated texture memory | 267.60 MiB | 210.00 MiB |
| Transparent material slots | 17 | 3 |

The report also records 116 active renderers, 20 shaders, 24 material keyword
combinations, 68 textures, 39 LOD groups, zero LOD crossfades, 4x MSAA, and the
required Android ARM64/Vulkan/ASTC/IL2CPP configuration. Twenty-four texture
subassets lack a directly inspectable `TextureImporter`, so their memory is
conservatively estimated.

This is accumulated product-scene debt, not evidence of a regression from the
cleanup-only changes. Thresholds were not raised and tests were not weakened.
The branch must not be described as meeting the static Quest budget until a
separately bounded optimization milestone passes the existing gate and physical
Quest timing is measured.

## APK

- Package: `com.alexoviedo.t2.questflight`
- Scene: `Assets/Scenes/ProductionVerticalSlice.unity`
- Development build: no
- Native JSBSim enabled: no; the Unity prototype remains authoritative
- Bytes: `174594517`
- SHA-256: `af1b26bd3cdac89e67a1b020a900aead68849f122c648554879720fae9ba5252`

The APK is external build output and is not committed. The latest worn review
accepted the mixed hand/Touch interaction and runway appearance, but no timing,
thermal, stale-frame, tear, allocation, comfort, FAA, navigation, training-credit,
or broad compatibility claim is made for this exact APK.

## Delivery decision

The cleanup and validation corrections are safe to checkpoint and push on the
integration branch. They are not merged to `main` while the established static
budget gate remains red. The exact next milestone is a bounded static-scene
budget reconciliation: first replace conservative estimates with importer-backed
measurements, then remove or consolidate the largest verified material,
transparent-slot, and texture offenders without changing the accepted visual or
interaction behavior.
