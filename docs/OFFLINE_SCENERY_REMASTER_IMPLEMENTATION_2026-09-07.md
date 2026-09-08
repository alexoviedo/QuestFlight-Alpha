# Offline scenery remaster implementation — September 7, 2026

**Approved execution pass finished with no surviving runtime remaster change.** The early Android build succeeded. Flight-distance review did not demonstrate enough benefit from traffic AO or the building-normal adjustment, so all 13 affected product files were restored to the accepted pre-remaster state. This is not a completed or accepted map-wide visual upgrade. Watercraft high-detail source/rights preparation and mandatory simulator/performance acceptance remain blocked. No headset installation or request occurred.

## Latest approved execution result

- Release Android IL2CPP build **passed**; all **616** declared scenery payload files verified inside the APK. Early experimental APK SHA: `449e21c3d4cb44fc001f24b0278b3f792c8d49c94da2b23026ab793adae8df9a`. Preserved at `approved_execution/early_android_build/QuestFlight-early-validation.apk`. It contains the subsequently rejected trials and is **not the final review candidate; do not install it as the restored product**.
- Exported **1,521 Unity asset dependency records**, **274 GLB layouts/subasset contracts**, and **2,930 actual packed source paths**. The expanded on-disk inventory includes **1,365** scenery/generated assets; **256** appear independently in the early packed-source report. Streamed bundles are audited separately. These counts are not enhanced assets or proof of every indirect authoring relationship.
- Recorded **702 retained-original**, **657 deliberately-excluded**, **6 blocked** asset decisions, plus **1,873 retained terrain page records**. Six watercraft-family records remain blocked for high-detail source/alignment/rights work; existing product outputs were preserved. [Coverage summary](OFFLINE_REMASTER_COVERAGE_2026-09-07.json); full paths/hashes/consumer evidence remain in `approved_execution/coverage_decisions.json`.
- Both airport atlases contain 16 tiles. All six albedo/normal/MRAO output hashes match their source manifest. Preserve these photographic/procedural originals; no useful new source-backed treatment was demonstrated. Encoded canopy maps, masks, lights and protected geometry remain excluded.
- Ran **30 matched traffic scene captures** (five views × three times × before/after), each recording prepared near/far counts. The airport view has visible vehicles; the low highway framing and changing vegetation limit attribution. These are mono Editor captures, separate from the earlier stereo-offset archetype images. No useful AO benefit was established at scene distance; do not infer shader performance from image similarity.
- Verified **13-file restoration on a copy and then live**, including all eight meshes, unchanged traffic material, traffic shader, traffic authoring hooks, building material and building authoring source. Preserved earlier traffic continuity, canopy, watercraft and cockpit fixes. Editor-only bake records remain staged for reproducibility, with automatic product hooks removed; integration now refuses the restored shader rather than writing unused attributes.
- After restoration, **28/28 Unity tests** and **9/9 Python tests** pass. Inspected the restored QA contact sheet and full cockpit view. Standard QA remains **128/130**, with the same inherited 79/82 draw counts against 77 and 39 materials against 38; maximum visible triangles 682,066. No threshold was raised.
- BuildReport confirms the temporary compression-test JPEG is absent from the early APK. Its previously denied deletion was not retried. The file remains local and uncommitted.

There is no surviving visual change to justify a second APK or a worn test. This follows the approved plan's explicit all-rejected outcome. The accepted headset APK remains untouched. The early successful build validates the experimental package, not a new runtime/thermal result. Missing mandatory simulator evidence and inherited performance failures remain open; this pass cannot establish Quest acceptance.

The remaining larger visual discrepancies involve protected geometry/coverage or unavailable source preparation. They are not silently implemented as material work. Reopening a rejected trial requires new evidence or source content; changing the geometry protections requires a future explicit scope decision.

## Preserved baseline

Branch `feature/offline-scenery-remaster-v1` starts at `a42c87b5358a30ec97f5dee1bdb9d869b44ddeba`, preserving pre-existing local work. External HEAD/status/binary diff and SHA-verified copies of 68 dirty/untracked source files precede product edits. Affected product assets and metadata have separate original backups.

Evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/offline_scenery_remaster_v1`. Implementation-start free space was 18.96 GiB after the user received manual cleanup commands; this agent did not execute the previously rejected cleanup. Latest measurement: 13,414,404,096 bytes (12.49 GiB).

The accepted installed APK `f6c019e08b85f95a827c52e40335e95fbe7f31b7da42c02515d26b2d5e66ad26` remains unchanged. The early experimental APK described above has now been built locally; no APK was installed. Local tooling/evidence checkpoint: `e60aa5c`; no merge or push. The checkpoint preserves staged rejected experiments, not an accepted visual upgrade.

## Earlier experiments (subsequently restored)

**Traffic: eight of eight shared archetype/proxy meshes.** CPU NumPy self-ray casting uses 48 deterministic cosine-weighted rays, a 0.4 m radius and visibility bounded to 0.85–1.0. Only body/trim receive baked color alpha; glass, lamps, tires and trailer white retain 255. Positions, indices, normals, tangents, UVs, semantic RGB, layouts, vertex-buffer sizes and paired metadata are unchanged. V2 reuses the same accepted V1 assets. Routes, selection, motion and accepted popping fixes are untouched by this remaster.

The shader decodes RGB before applying alpha to base color. It uses one additional component in the existing interpolator and one color multiply, with no additional texture, sample, pass, draw, material or vertex byte. The traffic material is byte-identical to its original. There is no added strength property or per-fragment class mask. Regeneration applies Editor-only records after checking an exact geometry/semantic hash; changed geometry fails closed. This is self-baking of accepted geometry, not high-poly reconstruction. [Derivative provenance](OFFLINE_REMASTER_TRAFFIC_PROVENANCE_2026-09-07.json) records CC0 attribution and original/output hashes.

**Buildings: existing normal response 0.34 → 0.5**, in the shared material and authoritative authoring code. Textures and shader structure are unchanged. Matched facade/residential captures show small differences: facade mean absolute RGB delta 0.00391 on the 0–255 scale, maximum 7. This is a restrained trial, not evidence of a large visual gain. Motion/shimmer acceptance remains outstanding.

## Earlier processing and review outcomes

| Work | Evidence and outcome |
|---|---|
| Inventory | 1,090 scenery/generated assets; 1,233 streaming files including sidecars; content hashes/static GUID consumers. Counts are not enhanced assets. Static traversal does not certify runtime closure; pending records remain. |
| Facades | All 16 photographic albedo slices processed through isolated official Real-ESRGAN ncnn Vulkan: wrap padding, x4 inference, original 512-square output and 20% blend. All 16 before/after crops inspected and retained-original: smoothing gave no convincing improvement. No inferred albedo integrated. |
| Terrain recovery | All 1,873 pages reconstructed externally with existing City2020 pipeline: 1,872 exact accepted SHA matches, one mismatch, zero remaining download errors. Source recovery only; no shipping terrain, bundle, fallback or source manifest replaced. |
| Terrain exception | `KBDU_2020_high_016_016` remains different after fresh download/serial reprojection. Android ASTC comparison differs in 25 of 900,096 payload bytes and 296 top-mip pixels. Preserve accepted `qf_aerial_high_r08_08` bundle; original compressed texture saved externally. Cause is unproven; do not substitute old hashes or claim byte identity. |
| Airport/vegetation | Inspected hangar/terminal albedo, hero leaf, tree impostor, airport vegetation atlas and encoded canopy visual; read shader/source contracts. No changes. Encoded canopy maps are not RGB restoration inputs. Full source/consumer and flight-distance review remains incomplete. |
| Other objects | Tower alpha controls obstruction emission. Boat records include mixed CC-BY and NC-ND/private restrictions. Parked aircraft records identify exterior-only CC-BY/CC0 derivatives. Preserve originals; do not apply traffic alpha logic or assume all downloaded derivatives are cleared. Full eligibility accounting remains open. |

Backend/download/model hashes and inference logs are external. The backend is [official Real-ESRGAN ncnn Vulkan](https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan); system PyTorch is CPU-only. Logs enumerate the RTX 4060; peak GPU memory/time were not measured. Traffic baking is CPU. No Unity/package/driver upgrade occurred.

## Earlier validation

| Check | Result and limits |
|---|---|
| Python analytic/restore tests | **7/7 passed**: finite-radius AO, invalid normals, restore preflight rejecting changed outputs/corrupt backups/path escapes before writes. |
| Unity EditMode | **28/28 passed**, `final_editmode_tests.xml`: traffic invariants/idempotence/stale-geometry rejection and building checks. |
| Integrated traffic verification | **9/9 files verified**, eight meshes plus unchanged material; protected geometry/attributes and metadata match; alpha equals approved records. Current `traffic_receipt.json`. |
| Android Vulkan shader compilation | Four clear/fog × vertex/fragment stereo variants compile. Static SPIR-V counts: fragment 848→880 / 899→931; vertex 411→409 / 445→443. Fragment texture sample operations remain one. Counts are not GPU cycles or milliseconds. |
| Material review | 64 before/after Editor images: eight meshes × two distances × two eye offsets. Inspected pickup and full-archetype contacts, including current right-eye 12 m/40 m pairs. Near truck framing crops part of the vehicle; 40 m covers the silhouette. Editor review does not replace stereo simulator acceptance. |
| Standard QA | `final_qa/visual_qa_report.json`: **128/130**, inherited draws 79/82 versus 77 and scene materials 39 versus 38. Maximum visible triangles 682,066. Counts equal accepted Valmont baseline; thresholds unchanged. Still FAIL_MACHINE_QA. |
| Android scripts | Passed, `android_player_scripts/validation.txt`; not IL2CPP or APK packaging validation. |
| Texture compression | Separate one-texture Android ASTC 10×10 bundle built outside shipping outputs. |
| Simulator / Quest | Mandatory simulator coverage blocked by inherited platform-bundle/replay problems. No new hardware/thermal result; existing ~60.7 fps is not a 72 Hz pass. |

## Tooling and rollback limits

[The runner](../tools/offline_scenery_remaster.py) supports `checkpoint`, `inventory`, `process-facades`, `recover-terrain`, `process-traffic`, `integrate-traffic`, `verify-traffic` and `restore-traffic`. Output/evidence paths must be external. Integration invokes Editor apply, review and standard QA; successful Unity exit alone does not establish QA success.

`restore-traffic` preflights nine output/backup hashes and authoring records before restoring those assets and disabling baked records. Its copy-based tests do not restore the live project. It is **not complete feature rollback**: shader/authoring hooks and building response need their separately preserved originals. Do not reset the working tree; accepted pre-existing changes would be lost.

Automatic approval review rejected deletion of the temporary generated compression-test JPEG and `.meta` with `blocked by policy`. They remain unreferenced and uncommitted in `Assets/Generated/RemasterCompressionSmoke`; no alternate deletion mechanism was used. Exclude them from any checkpoint commit. This is not a capacity or implementation blocker.

## Historical remaining execution

This list records the previous checkpoint. Follow the revised plan only after its explicit approval; in particular, it proposes early local Android packaging validation before further batches.

1. Finish source/consumer/eligibility records and source-backed trials for remaining airport, vegetation and reusable-object families. Retain originals where evidence does not justify a change; do not mark unreviewed entries complete.
2. Resolve terrain enhancement eligibility and canonical cleanup/fallback hierarchy using recovered sources while preserving the accepted bundle exception. Recovery is not enhancement.
3. Finish complete rollback verification, integrated matched-view coverage and resource/payload/source-identity checks. Mandatory inherited failures remain visible.
4. Build one release-style Android candidate after the full offline queue is accounted for; make a scoped local checkpoint of permitted task changes.
5. Only then consolidate worn review, timing and thermal validation. No user/headset intervention is currently requested.
