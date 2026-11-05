# SGS geometry/control candidate acceptance, 2026-10-15

**0.1.0-alpha.4/code4 is built, preserved and installed on Quest3.** The installed APK hash equals the preserved candidate and the existing training-save hash is unchanged. The revised airframe/control implementation and all runnable software/visual/package checks are complete for this repair. Worn handling, measured fidelity and actual Quest frame/memory/thermal acceptance remain open.

Candidate: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/sgs_visual_repair_20261015/final_android_incremental/QuestFlight-sgs-threeview-alpha4.apk`.

APK:1,816,561,220bytes. SHA256 `78f63129011297ed65b8e85ea9f8c8e541d768d0ffb2848f0be9528233ad4d28`.

External evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/sgs_visual_repair_20261015`.

| Category | Exact status | Evidence / boundary |
| --- | --- | --- |
| Reference geometry | PASS scoped reconstruction checks; exact replica unverified | Simons1996 Schweizer-supplied three-view, closed compound nose/canopy,51ft span,60/30in chords,1.33deg dihedral,4-to1deg incidence,93in tail span. True UIUC NACA43012A wing/aileron coordinates. Tail8% symmetric section is an estimate, exact designation deferred. `geometry_verification/geometry_checks.json` and inspected three-view plot. |
| Cockpit/control software | PASS software and nominal geometry checks | Following grab anchors, inverse lever arcs, regrab ownership, focus/pause/tracking release, guarded one-shot tow release.68/68 focused,31/31 PlayMode,16/16 locked geometry/control tests. Counts overlap; not cumulative independent tests. |
| Enclosure | PASS geometric witness only |512/512 rays from nominal eye intersect body/glazing. This does not establish measured cockpit dimensions, head clearance or human comfort. |
| Product integrity | PASS scoped validators | Explicit serialized hierarchy, native-only authority, dependency closure and shader audits. OpenXR camera remains tracked; authored SGS seat pose lowered0.52m into enclosure. No native mass/CG/contact/flight-model changes. |
| SGS component resources | PASS frozen component caps |7,921/8,000 triangles,3 renderers,2 materials,0 added textures. No global quality/scenery cuts or cap increases. Shader GPU cost and resident memory unmeasured. |
| Standard scene resources | FAIL retained baseline |107/130 pass; all130 draw/triangle/material rows exactly unchanged from alpha3. Maxima80 draws/967,513 estimated visible triangles/37 visible materials. `standard_resource_comparison.json`. No waiver or full-scene resource-compliance claim. |
| Offline visual QA | CAPTURES INSPECTED; global acceptance remains partial |18 final SGS images/9 stereo pairs in `unity_locked/screenshots`; standard130 named views/resource results in `standard_visual_locked`. |
| Live SGS simulator | PASS scoped14/14; final stereo inspected | Session51d775ed204946ab94d97505a32a9afc, errors[]. Final neutral stereo/control eye captures witnessed in `final_stereo_inspection.json`.259/259 runtime source/asset entries match canonical inputs. Development simulator is not Quest performance or formal replay. |
| Android release package | PASS | Normal release assembly with lint enabled. Quest3-only/targetSdk34, ARM64, Vulkan/OpenXR configuration; native/ELF/ZIP16KiB alignment, signature, zipalign, exact model/native payload and profiling/debug-off checks pass.1912 authored scenery files and3284 generated player asset entries match packaged bytes. Postbuild shader coverage106materials/errors[]. |
| Installation / training data | PASS exact bytes | `device_install_verification.json`: version4/alpha4, single APK, installed hash exact, save hash unchanged. No uninstall, clear-data or downgrade. |
| Quest frame time, memory and thermals | UNMEASURED | `mWakefulness=Asleep` after installation. No presence spoof, Guardian bypass or sleeping-device timing acceptance. First launch/native model-cache/flight handling on a focused headset remains unverified. |
| Aircraft fidelity / human acceptance | PARTIAL / NOT RUN | Reference interpretation, not measured or photorealistic replica. Worn lever reach, seat comfort and visual acceptance pending. Existing SGS glide/ridge and C172 lesson-probe failures remain open; this repair does not change FDM. Full simulator mountain-identity/replay acceptance remains separate from the passing focused SGS session. |

## Build recovery and preserved identities

Unity compiled/generated the player but its simultaneous temporary-file peak exhausted host disk. Earlier IL2CPP disk/memory and subsequent Gradle lint-AAR failures are preserved in their attempt directories; they are not successful Unity-wrapper builds. After Unity exited and released its temporary files, normal `:launcher:assembleRelease` succeeded in1m35s with lint enabled, one host worker and a2GiB JVM heap. No lint skip, content change, player tuning or reduced quality was used. A separate Android shader coverage audit passed after packaging. `product_apk_manifest.json` records this recovery accurately.

`final_android_incremental/CANDIDATE_IDENTITY.json` binds the exact APK and32 evidence-file hashes. It includes the exact prebuild Git HEAD/311-file dirty-source archive, model/reference ledger, native payload identity, package/install checks, simulator receipts, tests and static resources.3625 required ignored authored scenery inputs are hash-bound separately in the source identity; raw assets are not duplicated into the source ZIP. All compiled source/authored inputs remain byte-identical after build/audit; progress documentation changed afterward and is preserved separately. The prebuild archive intentionally retains the documentation state at build time.

`MODEL_AND_REFERENCE_HASHES.json` binds the current model inputs, editable Blender file, authored meshes/shaders and references. `preserved_meta_guids.json` confirms12/12 prior metadata bytes unchanged. Original donor GLB SHA256: `96becbb7d6e2e6b47850faa1b2a9429268258e77aa88ec9ba2063551b37ffe07`. Native/model payload identity: `0af74469d7c77b6d487d097fef4847633a649e588a9da3fee6d5d79902005ca7`. Training-save SHA256 before/after: `70f9d055ae54ebc9303e63b3412b809b450b1f56a80dcafda47aeded195ef040`.

Raw downloads, prior APKs and rollback assets remain preserved. Reference drawings and generated aircraft assets remain local and uncommitted pending distribution clearance. Simulator executable hash alone is not a complete player identity; source/asset comparison and player data are retained.

Automatic approval review rejected assistant removal of `C:/QFMenuSim20261014/Library` with **blocked by policy**. The user subsequently ran guarded cleanup; the external cache is confirmed absent. Source, built simulator player, captures and rollback were preserved. Generated canonical caches/symbols were compressed losslessly; no assistant deletion bypass was used. Host storage is no longer blocking this candidate.

## Short headset checklist

1. Open alpha4 and recalibrate the SGS seat. Inspect the enclosed forward/side canopy without leaning through it.
2. Grab, move, release and regrab blue brake and green trim through full travel; verify red tow release acts once and remains reachable.
3. Enter soaring, start a tow, release, land unpowered, and confirm Pause/Resume/Back recover correctly.

This human checklist does not replace separately measured Quest timing/thermal acceptance. See SGS_VISUAL_REFERENCES.md and DEFERRED_INPUTS.md for reference limits and missing measurements. No further implementation/build/package tasks remain in the current repair; retained native/resource/full-simulator failures are still explicit acceptance blockers.
