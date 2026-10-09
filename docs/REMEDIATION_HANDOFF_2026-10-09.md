# QuestFlight remediation engineering checkpoint — 2026-10-09

The corrected private Quest3 APK was built, inspected and installed in place. This checkpoint preserves the engineering work on local `main`; it does not certify release quality. The owner reported a very short free-flight test looked good. Installed APK readback confirmed the exact current artifact and unchanged sandbox inode; the observation supplies no sustained timing, stereo, comfort or formal scenario evidence.

**Accounting:** 92 findings, 18 work items, 25 independently assessed gates. 37 findings verified, 26 implemented awaiting acceptance, 2 externally blocked, 26 individually justified nonshipping deferrals, 1 proven inapplicable. Strict tracking exits1 for the open findings; normal schema/count tracking passes.

Full changed-file lists, test references, actual disposition reasons, remaining uncertainty and owners for every original ID are in [the status snapshot](REMEDIATION_STATUS_2026-10-09.json). The complete local evidence ledger remains `.questflight_codex_audit/status/remediation-ledger.json`; the audit input, raw logs, captures, builds and recovery archives stay outside Git.

## Implemented work and actual validation

- Trusted APK flight-data identity, validated staging/atomic cache publication and coherent fail-closed native readiness; fresh-flight/reset versus recovery relocation; wind and coordinate contracts; native-driven cockpit state.
- Guided physical control takeover, interrupted seat-save recovery, input/lifecycle ownership, caption timing; bounded rendering/history/geometry fixes; isolated authoring, geospatial and dependency/provenance validation; truthful QA and process/artifact identity.
- Unity6000.3.8f1 current retained integration:785 Editor tests,28 PlayMode tests and9 GPU fixture checks passed. Full aerial suite83 checks and default validator passed. These cover their actual scopes and are not headset performance/stereo evidence.
- Latest focused native/Unity acceptance:7 Editor tests and1 PlayMode test passed, zero skips. Three scenario permutations and three actual authored menu/new-flight cycles matched fresh native state and first120steps. Actual Demonstration/Assisting controls at72/90/120 fixture rates passed accumulated takeover,1.25s hold, smooth guidance reacquisition, free-flight and reconnect. The first new menu fixture counted disabled auxiliary cameras and failed; the corrected enabled/active predicate matches the existing authored contract. Original failure evidence is retained.
- A real child-process regression found13 absent environment names restored as present empty by the simulator wrapper. Explicit null removal corrected the defect;9 process/restoration controls passed. The unhandled-crash control rejected a timeout124; it did not prove a fast crash exit. Stub sentinels are not stereo images.
- Current nondevelopment Android build passed, then12 exact packaging checks,20 development-agent parser negative controls and native merge/strip replay passed. Same-private-certificate install-r preserved sandbox inode13256112. Isolated Windows development BuildOnly also succeeded with the same recorded build-source identity; runtime/stereo was not executed.

APK: `C:/QFRC20261008/QuestFlight/Builds/Android/QuestFlight-production.apk`, SHA256 `bd87a0dfcf447cbdd7e86ee619ae9cdfd7ccf81484e41112fe5409c3e8d7b8d2`, 1816082873bytes. Certificate `0231ace82e88f2b9b62b8bc46b3af5c726e5b97aa645e182cea14f64d77e370b`. Source archive identity `08610ca2e0761b4d8682687356baaade2120cd70c1a72ab50eb5bcc6c58bab0b`; build/inspection receipt `.questflight_codex_audit/status/M18-NV10-successful-artifact-results.json`. Android ARM64/Vulkan/OpenXR, Quest3-only, API2,16KiB native alignment, native JSBSim required; development agent disabled and tokens absent.

**Source boundary:** the APK is bound to its immutable pre-commit source archive. Subsequent simulator-tooling, tests and documentation changes do not change its runtime inputs. Five corrected mesh derivatives below remain local because source rights are unresolved; the Git checkpoint therefore cannot be presented as the complete clean source tree of this APK. Tests and provenance for those local repairs intentionally retain their true expectations. Preserve the local overlay/recovery archives for exact reproduction; a clean checkout without it has not passed the current complete asset tests. The pre-existing local DevAgent server-address edit is also preserved outside the commit; only token sanitization enters the index.

## Remaining demonstrated failures and blockers

- **Engineering:** standard QA130shots=105passing/25failing; unchanged ceilings77draws/692871triangles/38materials, maxima86/967513/37. Submission diagnostics did not justify a narrow arbitrary geometry deletion. Historical physical timing failed; current APK sustained timing remains unproven. Current native yaw fixture remains0.7886204147 versus0.55 ceiling; producer2Pass/1Fail/9Unproven. Do not tune thresholds or aircraft coefficients to obtain PASS.
- **Engineering acceptance:** actual Android startup/cache storage-full/interruption/force-stop, reset/relocation/scenario and lifecycle evidence remains incomplete. Editor QA exit controls and invalid-environment-before-save process controls still need actual isolated Unity execution. QF-ED-003 seven controls are prepared but uncompiled/unexecuted; no PASS claimed.
- **Tools/inputs:** a supported state-safe Meta simulator runtime/stereo route and `cockpit_neutral.vrs` recording are missing. Original LAZ plus pinned laspy/lazrs and exact frozen normalized OSM inputs are needed for full reconstruction. Build tools themselves are available and the current Android/Windows builds succeeded.
- **External reference/account:** approved lift/stall/break/recovery reference data, exact source permissions/terms and historical credential authenticity/revocation evidence require the named source/service owners. Private scope excludes public-store publication cases; it does not clear asset rights. Formal entitled-store users are outside private scope; actual private OS-user isolation was not thereby tested.
- **Physical owner:** worn controls/readability/audio/comfort, valid tracked startup and stereo observation, supported controller samples and sustained72/36 cadence/memory/thermal routes remain unverified. Brief free flight cannot substitute for them.

## One prepared owner session and exact continuation

Use `.questflight_codex_audit/status/OWNER_HEADSET_SESSION.md` for the single18-case session, three first-use testers, separate worn scenery passes and two separate45-minute sustained routes. The full `HEADSET_CHECKS_FOR_LATER.md` and native matrix retain every criterion/repetition. Preserve settings/data; no uninstall, clear-data, Guardian bypass or public signing.

After normal lighting, reacquired tracking, Guardian acknowledgment and genuinely tracked Home are confirmed, the engineer may run:

```powershell
python -B .questflight_codex_audit/status/run_bd87_native72_capture.py --execute --normal-lighting-tracked-home-confirmed
python -B .questflight_codex_audit/tools/check_tracking.py
python -B .questflight_codex_audit/tools/check_tracking.py --strict
```

The helper prints the exact owned-root `--restore` command before mutations and must restore all seven state actions. It supplies short diagnostics only, not sustained/worn acceptance. User free-flight success does not establish current room/sensor state for unattended execution.

QA engineer: inspect `.questflight_codex_audit/status/M18-ED003-process-controls-prepared.json`; first create only its owned fresh clone, exact namespace and guarded inputs. `python -B .questflight_codex_audit/status/run_ed003_process_control.py --case probe_positive` is a validation/print command; append `--execute` only after setup and review. It is not yet directly executable without the clone. Four retained authoring-wrapper controls remain unprepared. No live Unity/Gradle/device test job is being left running.

## Cleanup and preserved local work

Completed guarded cleanup removed110 generated external cache groups while preserving141 deployment/firmware hashes, retired a completed diagnostic APK after recovery checks, and removed original regenerable Bee output. After builds closed, Gradle8.13 transforms cleanup removed20,899 generated descendants /7,024,204,647logical bytes; pinned modules-2 inputs remain. Approximately41.5GiB now free. No source, authored assets, GUIDs, accepted/historical evidence, personal files or installed applications were removed.

Only `main` exists locally; there are no stale local branches to delete. The detached candidate worktree retains the current inspected APK and useful build cache. Removing it would also erase a duplicate APK whose deletion was automatically rejected, so it is retained. The separate guarded owner command is in `.questflight_codex_audit/status/DISK_CLEANUP_FOR_OWNER.md`; no owner cleanup is currently required. Do not retry the rejected deletion through another tool or a parent.

Local mesh corrections excluded from the Git checkpoint:

- `QuestFlight/Assets/Production/Environment/AmbientAircraftV1/Models/SuperCub_LOD0_Body.asset`
- `QuestFlight/Assets/Production/Environment/AmbientAircraftV1/Models/SuperCub_LOD1_Body.asset`
- `QuestFlight/Assets/Production/Environment/AmbientAircraftV1/Models/SuperCub_LOD2_Body.asset`
- `QuestFlight/Assets/Production/Environment/ReservoirWatercraftV1/Generated/ReservoirWatercraft_dry_storage_LOD0.asset`
- `QuestFlight/Assets/Production/Environment/ReservoirWatercraftV1/Generated/ReservoirWatercraft_open_water_LOD0.asset`

This follows the current `AGENTS.md`: “Keep restricted or unclear-rights aircraft assets local and uncommitted.” Their bytes and paired GUIDs are preserved, with surgical repair/source records and external recovery archives. Source owner clearance is required before including the complete current derivative set in Git.

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
| FN-003 | M05 | implemented_pending_validation |
| FN-004 | M05 | fixed_and_verified |
| FN-005 | M02 | implemented_pending_validation |
| FN-006 | M03 | fixed_and_verified |
| FN-009 | M05 | fixed_and_verified |
| FN-013 | M06 | fixed_and_verified |
| FN-014 | M11 | implemented_pending_validation |
| FN-015 | M06 | implemented_pending_validation |
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

| Gate | Current assessment |
|---|---|
| R01 | unverified |
| R02 | pass |
| R03 | unverified |
| R04 | unverified |
| R05 | unverified |
| R06 | unverified |
| R07 | pass |
| R08 | unverified |
| R09 | unverified |
| R10 | unverified |
| R11 | unverified |
| R12 | unverified |
| R13 | fail |
| R14 | fail |
| R15 | unverified |
| R16 | unverified |
| R17 | unverified |
| R18 | unverified |
| R19 | unverified |
| R20 | not applicable |
| R21 | unverified |
| R22 | unverified |
| R23 | unverified |
| R24 | not applicable |
| R25 | not applicable |

No full quality/release acceptance is claimed. Open P1 findings are FN-007, QF-VAL-001 and QF-VAL-002. Full source/evidence acceptance remains an explicit continuation, with current paths and owners in the JSON snapshot and local CONTINUE_NEXT.md.
