# QuestFlight approved engineering closeout — 2026-10-10

The approved session performed one supervised build attempt and preserved the previous verified APK/source evidence. The newer two rig references and paired SDK URP material are now included in the verified APK. All14 reviewed affected paths match original, candidate and immutable source archive. This resolves the build/source delivery gap; standard and stereo visual acceptance remain missing. Complete quality acceptance is not claimed. No push, public release, aircraft-parameter change or new product feature occurred.

## Deliverable and source identity

APK: `C:\QFRC20261008\QuestFlight\Builds\Android\QuestFlight-production.apk` (1816081341 bytes).

SHA256: `1704f38a7043c1512df7d0a1c2e76cfe934582b897658836ef28e5b08b6d329e`.

Installed status: New verified APK NOT installed: connected device reported stopped=false. Read-only installed APK SHA remains bd87a0dfcf447cbdd7e86ee619ae9cdfd7ccf81484e41112fe5409c3e8d7b8d2; app/data untouched.

Immutable build evidence: `C:\QFRC20261008-product-extraction-artifacts\M18-newer-material-20261010T191249Z`.

`source_identity.json` SHA256: `b0a890cfd6593fe86d0adfbdd384fcd1ec2b0fc068679aa9fe4b3539db20a61d`.

`source_changes.zip` SHA256: `de9e00198674d009e312e77d40dcfab3f1db2231045365f4a33401f9ba5dfd02`.

Verified scope: nondevelopment Android ARM64/IL2CPP, Vulkan/OpenXR, Quest3-only; native JSBSim/API2, dependency/alignment/flight-data identity, private certificate and packaged disabled development agent with empty credentials. All12 packaging checks are source/artifact-bound. No launch or physical test is inferred from packaging. Existing private certificate: `0231ace82e88f2b9b62b8bc46b3af5c726e5b97aa645e182cea14f64d77e370b`.

Original main parent: `eada000b8867dded65b6bad759abd361598f74df`. Candidate detached base: `65d35377e16da50b3a7d8d4fb6200a1860272ba3` plus retained overlay. The APK is identified by the immutable archive, not a clean Git HEAD. The approved local follow-up commit contains the reviewed Editor conversion and focused tests plus these two handoff documents; its exact final main SHA is in [the commit receipt](../.questflight_codex_audit/status/APPROVED_CLOSEOUT_commit_20261010.json). Main is the only local branch, so no branch merge/deletion is needed. No push.

The three runtime visual files remain uncommitted until required standard/stereo acceptance. Five unclear-rights mesh corrections and owner settings remain local; all were preserved. Existing mesh corrections already belonged to bd87 and were not newly invented in this session. Recovery: `C:\QFBackup20261008\M18-bd87-before-newer-material-20261010T191249Z`; old immutable evidence remains untouched.

## Completed changes and verification

- Native trusted-payload cache/atomic activation, fail-closed authority, reset/relocation/frame contracts, actual native cockpit telemetry, controls/lifecycle/persistence and authoring/provenance corrections retain their individual92-ID evidence.
- Reviewed recent native flap reset test: actual Unity Windows-native1PASS/0skip. Cached-read startup/block/retry integration:2PASS/0skip. SDK local material conversion/idempotence/source preservation:1PASS/0skip, post-import and normal restored-product render probe exit0. Current hashes still match these retained results.
- Existing785 Editor/28 PlayMode/9 GPU/83 aerial evidence was reused within its pinned scope. No full suite rerun. Current build exit: `0`; new verified artifact: `True`. Actual producer closure, reserve monitoring, packaging and installation outcomes are in [the closeout receipt](../.questflight_codex_audit/status/APPROVED_CLOSEOUT_result_20261010.json).
- The local commit excludes runtime visual assets, five held mesh assets, owner settings, generated builds/caches and the local audit package. No asset destruction or automatic-review bypass occurred.

## Remaining demonstrated failures

Standard Visual QA remains105PASS/25FAIL of130, with ceilings77draw/692871triangles/38materials and maxima86/967513/37. Two current objective shots remain892492/892498triangles. Rendering budgets require an evidence-supported correction; they are not merely owner-test deferrals.

Native yaw scenario remains0.7886204147 against0.55; producer2Pass/1Fail/9Unproven. No approved lift/stall/break/recovery reference is available. Do not tune the model or acceptance thresholds to obtain PASS. Historical physical timing failed; the current APK has no sustained performance acceptance. The owner's short free flight is informal evidence only.

## Consolidated remaining acceptance

| Group | Exact missing check / shortest next action | Owner |
|---|---|---|
| Current APK update | After the owner ends the flight, establish stopped state and run `pwsh -NoProfile -File .questflight_codex_audit/status/install_approved_closeout_20261010.ps1`; same private certificate, install-r, inode and exact installed-hash readback. The script refuses an unstopped app. | Owner ends flight; Codex/build engineer |
| Android native/startup | Current exact APK missing-library/invalid-data/storage-full/interrupted activation/read-permission/retry; dirty/fresh reset, relocation and same-frame terrain contact comparisons. Preserve prefs and fail closed. | Codex/native QA |
| Controls/lifecycle | Android interrupted seat save, force-stop/relaunch, focus/pause/reconnect and supported-control ownership. Worn ergonomics remains separate. | Codex/QA + owner |
| Editor/tooling | `python -B .questflight_codex_audit/status/run_ed003_post_material_restored_process_control.py --case probe_inspect_only --execute`; complete actual authorer workflow/full-resolution traffic review. Do not repeat unchanged failing objective shots. | Codex/QA + traffic reviewer |
| Standard/stereo | Use retained contribution tables to choose the smallest budget fix; then affected standard shots and inspected both-eye views. State-safe simulator route and `cockpit_neutral.vrs` remain missing. | Rendering QA + runtime/recording custodian |
| Source/model/account | Exact LAZ, pinned laspy/lazrs, frozen normalized OSM; approved lift/stall/break/recovery envelope; exact asset terms and historical credential revocation/status. Private use does not establish rights or revocation. | Source/flight-model/service owners |
| Physical | Prepared18-case worn controls/readability/audio/comfort session, three first-use testers; separate45-minute Native72 and eligible36/72 memory/cadence/thermal routes. Confirm normal lighting/tracked Home/Guardian; never bypass safety. | Owner + QA operator |

The [owner session](../.questflight_codex_audit/status/OWNER_HEADSET_SESSION.md) retains exact cases and identifies the prepared current-artifact helper. Its default local-input check passed with no device calls. Install the new APK before executing it; the old bd87 helper is historical. No sustained route or new capture was run here.

## Accounting and preservation

92 original findings /18 work items /25 independent gates:39fixed_and_verified,24implemented_pending_validation,2blocked_external,26deferred_nonshipping_with_justification,1not_applicable_proven. Gates2pass(R02/R07),2fail(R13/R14),18unverified,3not applicable(R20/R24/R25). No dispositions were changed to improve counts. Normal tracking output: [approved tracking result](../.questflight_codex_audit/status/APPROVED_CLOSEOUT_TRACKING_20261010.txt).

[Status export](REMEDIATION_STATUS_2026-10-09.json) is bound to [the ledger](../.questflight_codex_audit/status/remediation-ledger.json), preserving all92 evidence/acceptance mappings and25 assessments. [CONTINUE_NEXT.md](../.questflight_codex_audit/status/CONTINUE_NEXT.md) records one consistent closed state. Cleanup: No file deletion needed or performed. Main is the only local branch; retain candidate/diagnostic caches, all source/acceptance/recovery artifacts and held assets. Earlier rejected duplicate-APK deletion not retried.. Current free space: 23.45GiB. No cleanup is needed to retain the artifact; prior rejected deletion was not retried. Do not execute old hash-guarded cleanup commands against changed outputs.

## All original finding dispositions

| ID | Work item | Disposition |
|---|---|---|
| FN-001 | M02 | fixed_and_verified |
| FN-002 | M03 | fixed_and_verified |
| FN-007 | M04 | implemented_pending_validation |
| QF-REL-001 | M01 | fixed_and_verified |
| QF-REL-002 | M01 | fixed_and_verified |
| FN-029 | M14 | not_applicable_proven |
| QF-VAL-001 | M18 | implemented_pending_validation |
| QF-VAL-002 | M18 | implemented_pending_validation |
| FN-003 | M05 | fixed_and_verified |
| FN-004 | M05 | fixed_and_verified |
| FN-005 | M02 | implemented_pending_validation |
| FN-006 | M03 | fixed_and_verified |
| FN-009 | M05 | fixed_and_verified |
| FN-013 | M06 | fixed_and_verified |
| FN-014 | M11 | implemented_pending_validation |
| FN-015 | M06 | fixed_and_verified |
| FN-018 | M07 | fixed_and_verified |
| FN-019 | M08 | fixed_and_verified |
| FN-020 | M12 | fixed_and_verified |
| FN-021 | M08 | fixed_and_verified |
| FN-022 | M07 | fixed_and_verified |
| FN-024 | M01 | fixed_and_verified |
| FN-025 | M09 | fixed_and_verified |
| FN-026 | M09 | implemented_pending_validation |
| QF-ED-001 | M11 | implemented_pending_validation |
| QF-ED-002 | M09 | fixed_and_verified |
| QF-ED-003 | M07 | implemented_pending_validation |
| QF-ED-004 | M09 | fixed_and_verified |
| QF-ED-005 | M09 | fixed_and_verified |
| QF-ED-006 | M09 | fixed_and_verified |
| QF-REL-003 | M07 | fixed_and_verified |
| QF-REL-004 | M07 | fixed_and_verified |
| QF-REL-006 | M01 | fixed_and_verified |
| QF-REL-007 | M01 | fixed_and_verified |
| QF-REL-008 | M01 | fixed_and_verified |
| QF-REL-009 | M08 | implemented_pending_validation |
| QF-REL-010 | M07 | fixed_and_verified |
| QF-REL-011 | M08 | fixed_and_verified |
| QF-REL-012 | M09 | fixed_and_verified |
| QF-REL-013 | M09 | implemented_pending_validation |
| QF-REL-014 | M10 | fixed_and_verified |
| QF-REL-015 | M10 | implemented_pending_validation |
| QF-REL-016 | M10 | fixed_and_verified |
| QF-RT-001 | M02 | fixed_and_verified |
| QF-RT-002 | M06 | implemented_pending_validation |
| RE-001 | M11 | implemented_pending_validation |
| RE-003 | M07 | fixed_and_verified |
| RE-005 | M13 | implemented_pending_validation |
| FN-008 | M05 | blocked_external |
| FN-010 | M05 | fixed_and_verified |
| FN-016 | M05 | implemented_pending_validation |
| QF-REL-005 | M14 | fixed_and_verified |
| RE-007 | M14 | implemented_pending_validation |
| RE-008 | M14 | implemented_pending_validation |
| FN-023 | M08 | fixed_and_verified |
| FN-027 | M15 | implemented_pending_validation |
| FN-028 | M15 | implemented_pending_validation |
| FN-030 | M17 | deferred_nonshipping_with_justification |
| QF-ED-007 | M12 | fixed_and_verified |
| QF-SMP-001 | M17 | deferred_nonshipping_with_justification |
| QF-SMP-003 | M17 | deferred_nonshipping_with_justification |
| QF-SMP-005 | M17 | deferred_nonshipping_with_justification |
| QF-URP-001 | M17 | deferred_nonshipping_with_justification |
| QF-URP-002 | M17 | deferred_nonshipping_with_justification |
| QF-URP-003 | M17 | deferred_nonshipping_with_justification |
| QF-URP-004 | M17 | deferred_nonshipping_with_justification |
| QF-URP-005 | M17 | deferred_nonshipping_with_justification |
| QF-URP-007 | M17 | deferred_nonshipping_with_justification |
| QF-URP-009 | M17 | deferred_nonshipping_with_justification |
| RE-002 | M13 | implemented_pending_validation |
| RE-009 | M15 | implemented_pending_validation |
| RE-URP-001 | M17 | deferred_nonshipping_with_justification |
| RE-URP-002 | M17 | deferred_nonshipping_with_justification |
| RE-URP-003 | M17 | deferred_nonshipping_with_justification |
| RE-URP-004 | M17 | deferred_nonshipping_with_justification |
| RE-URP-005 | M17 | deferred_nonshipping_with_justification |
| RE-URP-006 | M17 | deferred_nonshipping_with_justification |
| RE-URP-007 | M17 | deferred_nonshipping_with_justification |
| RE-URP-008 | M17 | deferred_nonshipping_with_justification |
| RE-URP-009 | M17 | deferred_nonshipping_with_justification |
| RE-URP-010 | M17 | deferred_nonshipping_with_justification |
| FN-011 | M16 | blocked_external |
| FN-012 | M08 | fixed_and_verified |
| FN-017 | M16 | implemented_pending_validation |
| QF-DOC-001 | M08 | fixed_and_verified |
| QF-SMP-002 | M17 | deferred_nonshipping_with_justification |
| QF-SMP-004 | M17 | deferred_nonshipping_with_justification |
| QF-URP-006 | M17 | deferred_nonshipping_with_justification |
| QF-URP-008 | M17 | deferred_nonshipping_with_justification |
| QF-URP-014 | M17 | deferred_nonshipping_with_justification |
| RE-004 | M16 | implemented_pending_validation |
| RE-006 | M15 | implemented_pending_validation |

## Independent release gates

| Gate | Assessment | Missing check / scope |
|---|---|---|
| R01 | unverified | Finish targeted imported/binary/source/license inspection accounting for required inputs; do not repeat the original audit. |
| R02 | pass | No further wrapper/source-identity blocker for this exact artifact. Identical-byte repeat-build reproducibility, public signing and hardware acceptance are not claimed. |
| R03 | unverified | Bind current verified APK runtime authority/provider/graphics/IL2CPP diagnostics to exact installed bytes; native CLI and packaging proof remain their own scopes. |
| R04 | unverified | Complete current-artifact cold/warm/fault/recovery cases, including unexecuted storage and interrupted-activation faults; no fallback. |
| R05 | unverified | Execute source-bound current Android product dirty/fresh/wind/relocation comparisons and retain unchanged-state evidence. |
| R06 | unverified | Complete current target coordinate/contact scenarios and inspected stereo landing/terrain views. |
| R07 | pass | Runtime cardinality, SDK transitions and tracked-pose/seat comfort remain under R03/R09/R10; this static PASS does not accept the player or headset. |
| R08 | unverified | Complete current target native-value scenarios and worn cockpit control/display inspection; retain unresolved reference limits. |
| R09 | unverified | Physical QF-HW-05/08/09/10 across supported modalities and ownership transitions. |
| R10 | unverified | Physical QF-HW-07/12/13, save interruption/restart/update and seat recalibration under a controlled test profile. |
| R11 | unverified | Complete applicable isolated clean-authoring regeneration and exact current Android aerial/building streaming load checks; retain missing original source blockers. |
| R12 | unverified | Supply exact missing historical inputs and genuine required human full-resolution approval; no invented approval or changed historical accepted pins. |
| R13 | fail | Resolve justified measured remaining budget defect without raising ceilings, then standard Visual QA and inspected both-eye simulator/worn-headset captures. |
| R14 | fail | Complete current corrected Native72 exact-artifact mode/cadence diagnosis, then uninstrumented45-minute physical route at72 app/72 display with actual AppSW off. |
| R15 | unverified | Physical QF-HW-03/04/17 with distinct requested/eligible/active/submitted states and inspected stereo/hand/transparent motion. |
| R16 | unverified | Actual current45-minute memory/loading/thermal route and bounded paired native/input/hidden-world attribution, with calibrated allocation controls. |
| R17 | unverified | Physical listening/readout/caption exercise, seated comfort and QF-HW-11 first-use sessions. |
| R18 | unverified | Complete current artifact producer/consumer evaluation including missing/stale/partial evidence negatives; supported overdraw or explicit unavailable must never become a fabricated PASS. |
| R19 | unverified | Current installed-artifact runtime checks remain separate; public distribution needs owner-approved signing/store metadata/file-policy checks if scope later changes. |
| R20 | not applicable | Reopen exact model/derivative/imagery/audio/font/code grants and source/notice obligations before any public distribution. |
| R21 | unverified | Development service owner resolves token status/rotation; document actual permission/data handling and any applicable privacy/deletion obligations. |
| R22 | unverified | Complete current supported private install/update/launch/recovery/support journey; avoid uninstall/clear-data or claims of unobservable preference-byte restoration. |
| R23 | unverified | Reconcile existing per-batch reviews and restored-input accounting by exact identities/methods; do not re-audit or equate test/build success to content review. |
| R24 | not applicable | Reopen original per-ID regressions before any Surface Cache/on-tile/SSAO/decal/sample activation. |
| R25 | not applicable | Reassess only on actual product scope change; applicable multi-OS-user journey remains separate. |
