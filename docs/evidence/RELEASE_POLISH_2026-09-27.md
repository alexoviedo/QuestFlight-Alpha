# Release polish — 2026-09-27

## Scope delivered

All six priorities and five extra polish ideas extend the existing authored product. No new flight authority, runtime scene bootstrap, AI service, controller remapping framework, or rendering pipeline was introduced.

| Approved item | Implementation |
|---|---|
| P1 Setup and controls | Nine short setup steps with paused hand-motion, rudder and brake checks; adjustable Touch deadzone/response, yoke pitch/roll travel and trim; comfort preset and default restoration. External-controller trim is saved independently. Full axis endpoints and release gates remain. Existing Menu/A/B/Y assignments remain. |
| P2 Briefing | Boulder/runway information, wind sampled at the actual simulator position/time, approximate activity duration, and an optional north-up map generated offline from existing terrain and landmark coordinates. |
| P3 Physical feedback | Existing switch/flap/ground/touchdown audio retained and coordinated with bounded haptics; flap-selector clicks added. No camera shake or extra sound simulation. |
| P4 Ending | Demo completion waits for the final narration; free flights can finish through the pause menu. Summary offers another flight, activities or main menu. |
| P5 Accessibility | 100/110/120% menu and caption text, high contrast, separate menu-sound and vibration preferences. Existing larger-caption preference migrates. Traffic calls have captions and yield to teaching/narration. |
| P6 Feedback | Application version/build identifier; issue categories record simulator position, mode and phase locally. Latest 20 reports retained. USB export script provided; nothing uploaded. |
| E1 Radio | 24 offline Kokoro Michael calls, filtered offline for radio tone. Three actual powered actors, both runways, hold-short/departure/final/clear states. COM1 must be powered and on 122.725 MHz. Calls yield to instruction, narration and its reading hold; stale calls are discarded. |
| E2 Kneeboard | On-demand map, lesson reference speeds, short practical checklist and current lesson objective. Uses the existing pause interface. |
| E3 Activities | Landing practice, circuit practice and sightseeing reuse the existing lesson checkpoints/routes. Every selection starts a fresh session. |
| E4 History | Latest 20 flights: duration/distance, endpoint, outcome, assistance/recovery and practice suggestion. Guest history is session-only and does not mutate owner records. Normal lesson placement is excluded from flight distance/recovery statistics. |
| E5 Cockpit | Existing yoke, pedal and interior materials refined for roughness, normal strength and baked occlusion. Source textures, geometry and GUIDs retained; no extra cockpit texture samples or passes. |

## Evidence and limits

- Focused EditMode suite: **3 passed, 0 failed**. Covers monotonic full-throw response, all new pages having an exit, radio coverage for each powered actor/runway/phase, and bounded guest history with unchanged owner records.
- Authored pilot-seat stereo pairs: 32 captures, including large-text/high-contrast views. Inspected controls, map, tools, briefing, kneeboard and cockpit materials. First-pass map material/layout defects were corrected before the final candidate.
- Standard visual audit: **existing material-count failure remains: 41 versus planning target 38**, unchanged from the previous cockpit build. This is not a Quest timing result.
- Live OpenXR session: all 14 machine checks passed, with inspected left/right controls, map, readability and radio-caption captures. The enclosing replay report remains `BLOCKED_MISSING_RECORDINGS` because no prerecorded VRS session was supplied; the live session passed. This is not a replay-suite pass or a Quest performance claim.
- Android production package: succeeded; all 1,912 authored scenery payload files verified. Native JSBSim remains required and the Unity flight fallback remains forbidden.
- Headset comfort, haptics, listening and thermal/frame acceptance remain user testing. Do not infer hardware performance from desktop captures.

## Finding the additions

Menu → Flight tools / settings opens the common tools page. Controls and comfort, readability, quick start and guest settings are under Settings. Briefing/map, kneeboard, activities, history, finish-flight and issue reporting are in Flight tools.

Radio uses the simulated COM1 active frequency and avionics power. It is deliberately quiet while the teaching/narration panel is present, including its reading hold. No fake tower or clearances are used.

Issue export: run scripts/export_headset_feedback.ps1 with the headset connected. Reports remain local until explicitly copied over USB.

## Sources

- Existing authored C172 lessons provide the displayed practice speeds and activity starts.
- City of Boulder airport master-plan inventory identifies the non-towered airport and 122.725 MHz advisory frequency: https://bouldercolorado.gov/sites/default/files/2021-02/3-airport-master-planinventory.pdf
- City operating information: https://bouldercolorado.gov/government/departments/airport
- Radio generation/source hashes/license: Assets/Production/Training/Voice/TRAFFIC_KOKORO_SOURCE_MANIFEST.json; Kokoro 82M, pinned revision, Apache-2.0.
- Generated map/material provenance: Assets/Production/UI/ReleasePolish/SOURCE.md.

## Build / device record

Production APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`

- Packaged: 2026-09-27 19:40 UTC.
- Size: 1,802,347,574 bytes.
- SHA-256: `b567d20a2af26d824ee618ecd274ec8e6967feccb1957cf72f06eef9f87599cb`.
- Manifest/source snapshot: sibling product-artifacts directory, `release_polish_20260927/android_build/product_apk_manifest.json`.
- Standalone Android ARM64/Vulkan/OpenXR production configuration retained.
- ADB reported no attached device at completion. This candidate is built and ready to install; it has not been installed or accepted on the physical headset.
- After packaging, only this evidence record and generated-map provenance wording were corrected; the authoring-tool change affects its source-note text only, not runtime behavior or assets in the candidate.

## One integrated headset check

1. Open Menu → Flight tools / settings → Settings. Run Quick start; try controls/comfort and restore defaults, then check 120% text and contrast.
2. Open briefing/map and kneeboard. Start a fresh landing or circuit activity and confirm the existing instruction remains clear.
3. In free flight, with avionics on and COM1 active at 122.725 MHz, listen for sparse traffic calls when a nearby powered aircraft reaches a relevant phase. Instruction/narration has priority; silence during teaching is intentional.
4. Finish a flight through Flight tools and inspect its summary/history. Demo should finish after its final narration. Check a guest flight leaves owner history unchanged.
5. Check restrained switch/flap/touchdown feedback, cockpit material appearance and overall comfort. Mark an issue through Flight tools if needed; export remains a separate USB action.

Completion self-check: **6/6 priorities and 5/5 extra polish ideas implemented and included in the APK.** Physical-headset acceptance remains pending.
