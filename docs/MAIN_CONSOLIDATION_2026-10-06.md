# Main consolidation — October 6, 2026

User requested preservation of all recent progress, deferral of further SpaceWarp optimization, consolidation onto main, and evidence-based cleanup. This is not a new public release or a claim of SpaceWarp acceptance.

## Preserved scope

- Embedded URP 17.3.0 integration and its upstream license/third-party notices, product color/motion shaders, authoring and renderer contracts.
- SpaceWarp history, sky/cockpit/moving-object motion corrections, altitude gating, opt-in UI and diagnostic tooling.
- Building facade filtering fixes, prepared scenery/traffic submissions, cloud hardware-volume sampling, and unattended Quest power-state restoration.
- Cloud occupancy experiment retained disabled as a reproducible diagnostic, not promoted as a performance win.
- Source/licensing records for new binary assets: `SPACEWARP_BINARY_ASSET_PROVENANCE_2026-10-06.json`.
- Deferred work: `SPACEWARP_DEFERRED_OPTIMIZATION_PLAN_2026-10-06.md`.

SpaceWarp already defaults OFF (`PlayerPrefs.GetInt("QuestFlight.SpaceWarp", 0)`). Existing explicit preferences are preserved. No forced settings migration or new cloud-resolution implementation is part of consolidation.

## Baseline and recovery

Initial main, origin/main and feature/application-spacewarp-complete all pointed to `fdcc160c23360676ca9d87aa3626009785f8e28f`; recent work was uncommitted. One worktree, no stashes, no remote feature branch. Recovery records live in the external `T2-QuestFlight-product-artifacts/main_consolidation_20261006` directory: full Git bundle, staged/unstaged patches, working files, authored/local-only Assets and LFS payload archive, and SHA-256 verification inventory. Required source data outside the repo is retained in place. Existing canonical and prior reference APKs are retained.

The local-only DevAgentSettings server-address adjustment is workstation configuration, not a product improvement; preserve it locally outside the consolidation commit. The debugger remains disabled.

## Validation and limits

The existing canonical Android build passed with 1,912 scenery files verified; saved and installed APK SHA-256 `588fbfbabb27d9a6ef8aa530f98501fab0d86b6eabe8bbbf9500fd3062d90b82`. Compare source identities before relying on this build. The complete APK source snapshot remains beside it under `cloud_optimization_20261005/quest_volume_resume_20261006`.

October 6 hardware timings and limitations are recorded in the deferred plan. Stable 36 FPS, native 72 FPS and head-turn visual acceptance remain unproven. Existing standard Visual QA has inherited machine failures; selected-cloud stereo simulator inspection remains NOT RUN. This consolidation preserves experimental work at the user's explicit request; it does not turn these results into PASS. Focused consolidation test output and final Git identities are recorded externally.

## Cleanup decisions

Consolidation verification: 54 focused EditMode checks passed in the first run, with one evidence-dependent strut check skipped. Supplying its external evidence directory and rerunning that check passed: 55 selected checks passed across the two runs. The verified backup contains 18,894 file entries. Comparing the saved APK input identity against the working files found only documentation differences; no runtime implementation was changed during consolidation. The uncommitted, disabled DevAgent address remains a local configuration difference from the committed tree.

| Item | Action and reason |
|---|---|
| Merged feature branch | Safe local deletion only after its tip is included in main and the normal origin push is verified. No force deletion. |
| October 3 and 4 generated simulator `player` directories | Confirmed absent at final inspection. These were superseded Windows executables/data, approximately 5.99 GiB combined in the earlier inventory. Sibling captures/reports remain. No additional deletion or reclaimed-space claim is made for this turn. |
| Current Unity Library and native/Gradle caches | Keep: deleting these repeatedly forced expensive reimports/recompilation and renewed peak disk pressure. |
| Current APK and known-good references | Keep for testing and rollback. |
| Source archives, raw/model/imagery inputs, recovery backup, evidence | Keep: unique or useful; not disposable merely because old or ignored. |
| Embedded package files and serialized Unity assets | Keep: package/scene dependencies are not safely disproven by a text search. No speculative dead-asset purge. |
| ESP32 caches | Earlier user cleanup removed only generated `.embuild` environments; ESP32 source/history was preserved. Not part of this repository commit. |

Deletion attempts blocked by automatic approval review must be performed by the user via the provided exact-path script. Eligibility is not a claim that deletion succeeded. Final cleanup outcome and free-space measurement are recorded externally.
