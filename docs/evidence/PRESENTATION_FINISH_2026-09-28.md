# Presentation finish evidence

Artifact root: sibling `T2-QuestFlight-product-artifacts/presentation_finish_20260928/`.

## Completed checks

- Focused EditMode suite: **20 passed, 0 failed**. Caption clock hold/repeat, independent caption sizing, corrupt-history backup recovery, optional yoke toggle lifecycle, existing touch interactions and previous release/readiness contracts.
- Final settings/readiness rerun: **9 passed, 0 failed**, including independence when no caption preference has previously been saved. Empty pages now place focus on their first available action.
- Native short showcase: runway 08 completed in **371.19 s**, touchdown sink **154.42 ft/min**; runway 26 completed in **320.79 s**, sink **116.75 ft/min**. Both ended `complete_clear_of_runway`, with no scenario failure. CSV and summary files in `short_showcase/`. This is native desktop scenario evidence, not hardware timing or real-aircraft fidelity certification.
- Audio audit: **107 recordings**, matching shipped-audio/source/timing SHA-256 values, valid phrase boundaries, no nonfinite or clipped voice PCM. `audio_audit.json` records loop metrics too. No claim of listening acceptance.
- Standard visual QA: **130 captures**, authored hierarchy/environment contracts pass. Machine report retains **21 flagged shots** and **42 materials vs the older 38 target**, identical counts and material finding to the previous tester-readiness candidate. These are not described as a clean machine-QA pass.
- New interface inspection: **56 captures**, both eyes at normal and large text sizes, including 160% captions. Corrected initially tiny diagram labels, excessive preview height and insufficient caption footer spacing. The UI capture runner still reports inherited draw/triangle planning-threshold warnings; screenshot inspection and those machine flags are distinct.
- Material investigation: original, diffuse-only and base-color-only editor captures plus a renderer/material inventory. Did not reproduce the colored label speckling seen in older live desktop stereo output. No speculative product shader/material change made from this inconclusive observation.
- First successful live stereo retry: OpenXR machine gate passed, 0.060 m eye separation, new menu and actual 160% caption captures inspected. The wrapper still reports `BLOCKED_MISSING_RECORDINGS` because recorded `.vrs` replay inputs are absent; live capture is not replay coverage. Small colored cockpit-label speckles remain visible in the live desktop player, so hardware investigation is still open.
- Stereo inspection found the new caption background at queue 3510 covered the existing progress strip at queue 3501. Corrected the background material/shader default to queue 3500, before the strip and text. The Android build in progress was intentionally stopped so the final candidate includes this correction. Final stereo evidence is recorded separately from the superseded captures.

- Final stereo build (`interface_stereo_final`): completed successfully, live OpenXR session passed. Inspected solid captions in both eyes and translucent captions; the progress strip is now visible and the actual 160% layout fits above the panel. The wrapper still identifies missing recorded replay inputs, not a live-session failure. Existing colored cockpit-label speckles remain an open hardware observation.

## Completed packaging

Final standalone Android build succeeded. The packaged scenery verifier passed all **1,912 authored files**. `android_final/product_apk_manifest.json` records the source identity and build; APK size is **1,804,144,361 bytes**, SHA-256 `d20a7922f36f73220cbee97f40d5842e73378bcc1ab32f551447b5295a7342b1`. `tester_package/` contains the matching verified APK, tester guide, feedback export script, README and candidate manifest. No headset installation, listening acceptance, controller usability acceptance or sustained physical frame/thermal measurements have been performed for this candidate.

The initial live-stereo build crashed while another Unity validation process was active. The second process logged D3D11 allocation failures (`0x8007000E`). Neither crash is counted as a test pass. The settings retry succeeded without graphics; subsequent Unity work is sequential. The user freed additional disk space. The failed isolated development project was removed by the runner's verified temporary-copy cleanup; original source and previous APK were preserved.

## Scope and resource impact

Authored menu diagrams/curve are inactive outside their paused pages. Caption data is offline text metadata and pre-recorded audio; no runtime AI/service dependency, no new scenery renderers or flight-dynamics fallback. Existing Quest rendering/performance settings are retained. The source repository contains earlier approved uncommitted work; this delivery does not silently reset or merge it.
