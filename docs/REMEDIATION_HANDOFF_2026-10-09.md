# QuestFlight remediation handoff - 2026-10-11

Closed the approved 60-minute planar-feasibility batch after 7.1 minutes, before the 15-minute admission cutoff. No owned job remains. Full quality acceptance is incomplete.

## Current result

2026-10-11 approved P admission rejected before minute 15: nine scoped meshes offer only 3,885 optimistic exact-planar interior-vertex triangle savings per failing shot. Even allowing exact collinear boundary removal yields only 14,353-14,359, below every required reduction 39,737-274,642. Nine diagnostic fixtures pass; six distinct source payloads match immutable QA and candidate. No production edits or validation rerun; all 23 failures remain. B was not performed because side-window triangle prerequisite failed. This does not prove all fidelity-preserving optimization impossible or make fidelity reduction mandatory.

The strict bound preserves every boundary vertex. The relaxed bound also allows removal of exactly collinear boundary vertices. Both weld equal positions across attribute seams and ignore holes, shading, UV and baked-visibility restrictions, so they overestimate available savings. The actual product was not changed. No image, stereo, Android or hardware PASS is inferred.

[Per-shot result](../.questflight_codex_audit/status/M18-planar-20261011/result.json) | [Diagnostic and nine fixtures](../.questflight_codex_audit/status/M18-planar-20261011/planar-admission.json) | [Immutable source match](../.questflight_codex_audit/status/M18-planar-20261011/source-provenance.json) | [Tracking receipt](../.questflight_codex_audit/status/M18-planar-20261011/tracking.txt)

## Retained implementation and artifact

The prior exterior batch still improves standard QA from 105/130 to 107/130: control sweep 78 -> 63 draws, approach 86 -> 71, triangles unchanged. Its ten focused tests and three saved-prefab reruns remain evidence. All 14 scoped files match the prior QA snapshot. New build and stereo acceptance of that correction are deferred by the owner; no repeated QA was necessary for this read-only mesh analysis.

Preserved APK `C:\QFRC20261008\QuestFlight\Builds\Android\QuestFlight-production.apk`; SHA-256 `1704f38a7043c1512df7d0a1c2e76cfe934582b897658836ef28e5b08b6d329e` rechecked unchanged. It excludes the prior exterior-batch code/prefab/mesh. Source identity `b0a890cfd6593fe86d0adfbdd384fcd1ec2b0fc068679aa9fe4b3539db20a61d`; source archive SHA-256 `de9e00198674d009e312e77d40dcfab3f1db2231045365f4a33401f9ba5dfd02`. Device state was not queried. The historical installed bd87 APK observation is not a current device observation.

Original main HEAD `6e4a0b6cebc782ea83f4bfcd4e4c6be46c1a7acd`; candidate detached HEAD `65d35377e16da50b3a7d8d4fb6200a1860272ba3` plus existing overlay. No commit, source reset, package change, build, installation or cleanup. Native JSBSim authority, OpenXR camera ownership, authored hierarchy, source assets, GUIDs and user work remain preserved.

## All 23 remaining visual-budget failures

All remain engineering failures. The following optimistic savings were NOT implemented. Each row retains its cause groups and uncertainty in the linked result. The side-window also remains three draws over budget. Thresholds remain 77 draws / 692,871 triangles / 38 materials.

| Shot | Required reduction | Strict upper bound | Relaxed upper bound | Excess even after relaxed bound | Disposition |
|---|---:|---:|---:|---:|---|
| 01_default_cockpit | 110,708 | 3,885 | 14,353 | 96,355 | Unresolved; planar route insufficient |
| 02_calibrated_cockpit | 109,048 | 3,885 | 14,353 | 94,695 | Unresolved; planar route insufficient |
| 03_instrument_yoke | 47,875 | 3,885 | 14,353 | 33,522 | Unresolved; planar route insufficient |
| 04_runway_from_cockpit | 74,979 | 3,885 | 14,353 | 60,626 | Unresolved; planar route insufficient |
| 04b_cockpit_engine_cowl | 70,313 | 3,885 | 14,353 | 55,960 | Unresolved; planar route insufficient |
| 17_sky_horizon_forward | 110,044 | 3,885 | 14,353 | 95,691 | Unresolved; planar route insufficient |
| cloud_01_ground_scattered | 274,642 | 3,885 | 14,359 | 260,283 | Unresolved; planar route insufficient |
| 40_pilot_left_airport_fence | 196,514 | 3,885 | 14,359 | 182,155 | Unresolved; planar route insufficient |
| pasv1_22_cockpit_windshield | 108,516 | 3,885 | 14,353 | 94,163 | Unresolved; planar route insufficient |
| pasv1_18_cockpit_side_window | 214,182 | 3,885 | 14,359 | 199,823 | Unresolved; planar route insufficient |
| pasv1_23_instrument_glass | 47,875 | 3,885 | 14,353 | 33,522 | Unresolved; planar route insufficient |
| pasv1_28_magnetic_compass_000 | 39,737 | 3,885 | 14,353 | 25,384 | Unresolved; planar route insufficient |
| pasv1_28_magnetic_compass_090 | 39,737 | 3,885 | 14,353 | 25,384 | Unresolved; planar route insufficient |
| pasv1_28_magnetic_compass_180 | 39,737 | 3,885 | 14,353 | 25,384 | Unresolved; planar route insufficient |
| pasv1_28_magnetic_compass_270 | 39,737 | 3,885 | 14,353 | 25,384 | Unresolved; planar route insufficient |
| pasv1_28_magnetic_compass_359 | 39,737 | 3,885 | 14,353 | 25,384 | Unresolved; planar route insufficient |
| pasv1_28_magnetic_compass_001 | 39,737 | 3,885 | 14,353 | 25,384 | Unresolved; planar route insufficient |
| pasv1_24_cockpit_ao_only | 66,285 | 3,885 | 14,353 | 51,932 | Unresolved; planar route insufficient |
| pasv1_26_cockpit_local_shadow_off | 66,285 | 3,885 | 14,353 | 51,932 | Unresolved; planar route insufficient |
| pasv1_20_cockpit_shadow_candidate | 66,285 | 3,885 | 14,353 | 51,932 | Unresolved; planar route insufficient |
| pasv1_27_cockpit_shadow_heading_090 | 171,688 | 3,885 | 14,353 | 157,335 | Unresolved; planar route insufficient |
| pasv1_27_cockpit_shadow_heading_180 | 185,013 | 3,885 | 14,353 | 170,660 | Unresolved; planar route insufficient |
| pasv1_27_cockpit_shadow_heading_270 | 146,632 | 3,885 | 14,353 | 132,279 | Unresolved; planar route insufficient |

## Remaining work and owners

- Engineering: a different, separately justified optimization is required for the 23 triangle failures. Do not rerun the planar, duplicate-face or insufficient grove-bounds investigations. Do not implement B alone. No sufficient fidelity-preserving fix or production-fix command is established. Density, coarse LOD or decimation changes require an explicit quality tradeoff; this result does not prove such a tradeoff unavoidable.
- Other engineering failures remain: two objective views at 892,492/892,498 triangles; native yaw 0.7886204147 versus 0.55 with 2 Pass / 1 Fail / 9 Unproven; historical physical timing failure. These are not all headset-observation gaps.
- Delivery owner: prior exterior correction build/stereo delivery remains deferred. When resumed, safe preflight is `pwsh -File scripts/build_production_quest.ps1 -ProjectPath C:/QFRC20261008/QuestFlight -PreflightOnly`. Preserve the verified APK/archive and establish fresh disk/source guards before any build. Do not replay expired build runbooks.
- QA and owner: inspected current-source stereo, worn checks and sustained performance/thermal routes remain at their existing scopes. The owner headset session was not changed because its artifact and procedure were unchanged.
- External reference/account owners: approved lift/stall/break/recovery reference, exact rights, historical credential evidence and remaining named inputs remain unresolved as individually recorded.

## Accounting

Tracking passed: exactly 92 findings, 18 work items, 25 gates. All individual finding/gate statuses retained: 39 verified / 24 pending validation / 2 externally blocked / 26 justified deferrals / 1 not applicable. Gates: 2 pass / 2 fail / 18 unverified / 3 not applicable. R13 and R14 remain failed.

[Ledger](../.questflight_codex_audit/status/remediation-ledger.json) | [Status export](REMEDIATION_STATUS_2026-10-09.json) | [Checkpoint](../.questflight_codex_audit/status/CONTINUE_NEXT.md)

## All92 original finding dispositions

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

## All25 independently retained gate assessments

| Gate | Assessment |
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
