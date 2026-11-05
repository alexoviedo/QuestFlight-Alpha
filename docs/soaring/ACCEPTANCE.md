# Current installed candidate

The approved menu repair is installed as **0.1.0-alpha.3 / code3**. [MENU_REPAIR_ACCEPTANCE.md](MENU_REPAIR_ACCEPTANCE.md) is authoritative for the current APK, menu/software validation and pending hardware acceptance. The alpha2 candidate, installation path and menu checklist below are historical rollback evidence, superseded by that report. Aircraft reference and scenery limitations still apply.

# Boulder soaring V2 acceptance checkpoint

**CANDIDATE_BUILT_INSTALLED_ACCEPTANCE_PARTIAL — final integrated APK built, verified and installed; remaining acceptance gates are explicit below.**

Stages01–06 are implemented on `feature/boulder-soaring-autonomous-v2`, based on the actual local HEAD `05a2f721d84030a543fd0b9bc2d3d59d2eb44274`. The source includes a provisional SGS cockpit, native SGS and PA18, physical ground launch/tow/release, thermal/ridge soaring, unpowered landing, tug return/taxi/park and seven SGS training modules. Cockpit appearance refinement is deferred at the user's request.

| Acceptance axis | Exact disposition |
|---|---|
| SOFTWARE | SGS scenario matrix16/16, full08/26 flights, seven lesson completions and lifecycle pass. EditMode827 pass/8 diagnostic skips; all8 subsequently pass in a30/30 focused run. PlayMode31/31, integrity and dependency closure pass. C172 demo7/7 and atmosphere6/6 pass; older native lesson probe2/6 remains failed. Android release packaging, payload/signature/alignment verification and installation pass. First launch/model-cache activation remains pending. |
| FIDELITY_REFERENCE | Partial, explicitly labeled reference configuration. SGS solo best-glide ratio misses the declared5% tolerance; source solo loading is unknown. Moderate-ridge sustained clearance fails; supported stronger ridge passes. No spin/operator-exact fidelity claim. |
| OPERATOR_EXACTNESS | Deferred. One consolidated list is in [DEFERRED_INPUTS.md](DEFERRED_INPUTS.md). No invented Mile High procedures or endorsement. |
| STATIC_RESOURCES | Frozen baseline fails: standard QA107/130. All130 named C172 views exactly match the retained baseline in draws, triangles and material count. Exact QA/static source inputs match; the static gate rejects failed QA. No cap, scenery or rendering reduction. |
| PHYSICAL_TIMING | Unverified. Latest Quest3 observation is Asleep/proximityfalse. Headless ARM64 native execution is valid software evidence only. No final APK graphics, per-framep95, thermal or model-cache activation acceptance. Installation and retained training progress are verified. |
| WORN_ACCEPTANCE | Not run; requires the user. Offline stereo previews do not establish tracked XR or worn handling. |

The final APK is [QuestFlight-soaring-v2.apk](C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/soaring_autonomous_v2_20261013/final_release_after_compression/QuestFlight-soaring-v2.apk) (1,816,522,220 bytes), SHA-256 `40f5d45ede6fb18c6f469a1f64bf4bfa53f380aefcf406f74c820aab2f65767c`. It is installed on Quest3 serial `2G0YC5ZG8907TD`; the installed base.apk hash matches exactly. Existing training progress remains byte-identical. The device was Asleep/proximityfalse after installation; no forced presence or Guardian bypass was used.

Earlier Unity/Gradle attempts exhausted disk. After lossless storage compression, normal `:launcher:assembleRelease` completed successfully in1m14s, including lint, from the exact Unity-generated project whose621 C++ objects and native player had compiled successfully. No diagnostic overrides or lint exclusions were used in the final build. The original Unity wrapper did not return overall success and its post-BuildPipeline compiled-shader validator was not rerun separately. Final package checks pass: ARM64-only, native ELF16KiB alignment, zipalign, APK v2 signature, exact current native libraries and18 model files, all1912 scenery files, Quest3 manifest target and disabled profiling/debugging.

Exact compiled source inputs are preserved in `final_android_recovered/source_identity.json`, `source_changes.zip` and `source_changes.patch`; source identity SHA-256 is `d742d181d3501c6fc692753fcf5d5bde0163b497e18c4209b95df16b12e9c81d`. Model payload ledger SHA-256 is `0af74469d7c77b6d487d097fef4847633a649e588a9da3fee6d5d79902005ca7`. [FINAL_ARTIFACT_IDENTITY.json](C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/soaring_autonomous_v2_20261013/FINAL_ARTIFACT_IDENTITY.json) records the complete source/native/model/file hashes and verification reports. Later edits are handoff documentation; disabled DevAgent metadata is also documented in the source comparison. The earlier QA snapshot differs from compiled inputs only in documented build-generated URP prefilter/disabled DevAgent metadata and documentation; no flight code, model or authored scene change occurred during packaging recovery.

Measured resource evidence:

- Standard C172 views: maxima80 estimated draws,967513 visible triangles,37 materials. Frozen limits remain77/692871/38; texture limit307507088bytes and18 transparent slots are unchanged.
- Provisional SGS geometry:7614 triangles, three cockpit renderers, two materials; rope96 triangles/one renderer. No added texture assets. Representative offline SGS views reach34 estimated draws,375067 triangles,15 materials and4 transparent slots; this is view-specific, not an all-mode GPU result.
- Pair-step managed allocation measured zero. Current82-node/10Hz wake covers the8s history; production coupling120Hz versus480Hz reference differs at most1.73m/1.61degrees. No silently discarded coupled time.
- Headless Quest native mean step times: C1720.018601ms, SGS0.010316ms, tug0.014011ms,600 steps each. These are uncontrolled individual native-step means, not paired product CPU per rendered frame orp95 acceptance.
- Actual rendered CPU/GPU costs, deadlines, resident textures/render targets/native memory, all-mode residency and sustained thermals remain unmeasured. Incremental planning ceilings are not claimed met.

Four C172 native lesson-driver checks remain failures: first-flight final altitude stage/terrain exit, and three neutral-checkpoint guards. All six calm8-second checkpoint replays exactly match the original ABI2 state and diagnostics, and the C172 reset wind construction is unchanged. This bounds non-regression; it does not convert the lesson failures into passes. No product retuning or tolerance relaxation was used.

Offline left/right SGS views were inspected for normal seating, leans, rear/control/gauge views, moving tow, release and bright sky. Formal simulator recordings are absent. A fresh live simulator copy alone is2.68GB before its Library/player; storage blocked that attempt. Further simulator work is deferred following the user's immediate wrap-up request. Standard visual QA and final focused source review are retained separately.

Evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/soaring_autonomous_v2_20261013`.
Key evidence: `pair_weather_loading_v2`, `complete_flight_history82_final`, `native_lifecycle_final`, `native_lessons_initial` plus `native_glide_lesson_v2`, `final_current_product_v2`, `remaining_diagnostic_tests`, `final_closure`, `final_visual_exact`, `final_static_exact`, `final_static_nonregression.json`, `android_native_smoke_nominal`, and the preserved failed Android attempts plus `final_release_after_compression` and `device_upgrade_final`. `FINAL_ARTIFACT_IDENTITY.json` binds the preserved source snapshot, model payload and native files; its final APK field identifies the verified installed candidate.

All donor derivatives, restricted overlays, original downloads, rollback assets and paired.meta/GUIDs remain local and uncommitted. Source plus dirty inputs are archived externally; ignored authored scenery is hash-inventoried and remains preserved in the local projects. Public-clone completeness is not claimed. No merge, push, branch deletion or checkpoint commit was made.

Short headset checklist for the installed candidate:

1. Wear the Quest, complete normal Guardian/controller prompts, confirm genuine focus and both controllers, then calibrate the seat.
2. Select **Aircraft → SGS2-33A reference → Soaring setup / ground launch**. Check stick/rudder, trim, combined airbrake/wheel-brake lever and red release; complete crew reconnect and clearance.
3. Fly tow/release (`A` or pull the red handle), a supported soaring lesson and an unpowered landing. Check pause/resume and C172 return; record comfort/readability/control issues.
4. Run matched baseline/candidate physical timing and thermal checks with `-RespectPhysicalPresence`. Never use virtual proximity, forced presence or Guardian bypass.

Resume remaining acceptance using [CONTINUE_AFTER_STORAGE.md](CONTINUE_AFTER_STORAGE.md). No further build is required to try the installed candidate.
