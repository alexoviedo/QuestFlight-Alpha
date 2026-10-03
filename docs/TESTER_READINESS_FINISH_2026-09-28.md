# Bounded tester-readiness finish

Approved September 28. Extend existing product systems; no flight-dynamics or renderer rewrite.

## Completion checklist

- [x] Performance preservation: retain current recovery candidate, record user's successful headset check, inspect current stereo build and compare flight-view draw estimates. Physical frame/thermal acceptance remains separate; no unsupported FPS claim.
- [x] Controls: bounded left/right adjustment, raw/processed rudder and brake previews, standard/gentler/left-stick presets, safe flight-axis/brake assignments, reset, updated help and release gates. Left-stick steering is not a rewrite of physical cockpit grabbing.
- [x] First session: direct controls/help access, clear activity expectations, readable status and ownership.
- [x] Debriefs: evidence-based observations and next action, distinguish assistance/recovery and avoid sink-rate-only grading.
- [x] Reliability/presentation: controller-loss pause, suspension recovery, save-failure visibility/retry, smooth audio priority and consistent settings behavior.
- [x] Map: existing demo route, lesson objective and bounded recent flown path (256 points, separate from scenery rendering).
- [x] Instrument help: reuse ten authored cockpit references outside lessons; close highlights when leaving the guide.
- [x] Ownership: explicit current pilot and handover instructions using existing interface. Corrected Touch rudder/trim/brake availability after taking over the demo.
- [x] Reports: bounded recent frame intervals (360 samples), active objective and settings. Explicitly CPU-observed intervals, not GPU timing.
- [x] Audio: inspected existing assets/mix; retained existing speech ducking and added a short release hold between phrases. Regenerated 16 Michael cues with mapping-neutral steering/trim wording and retained GUIDs. Physical listening acceptance remains human.
- [x] Focused behavior checks (9/9), inspected visual QA and live stereo simulator captures; Android release APK built and its 1,912 scenery files verified. Inherited standard-QA warnings and unavailable recorded replay coverage are documented in the evidence report. Installation awaits ADB connection.

User reports latest recovery build looked good and did not stutter much. This is positive subjective evidence, not measured frame/thermal acceptance. A fresh user's unassisted session and final speaker/headphone listening review require people; do not fabricate those results or request them before delivery.

Deferred: new aircraft/scenery/effects, multiplayer, arbitrary HOTAS discovery/bindings and aerodynamic changes.

Completion audit: all approved software priorities and extra polish ideas are implemented. Ready for user acceptance testing; human usability/listening and physical Quest timing/thermal acceptance are not claimed completed. Final APK SHA-256: `4a6df271fc1aecafaf1cfe65c705cf6d47a322f38630e516345bfdbea8da0efc`.

## Later headset acceptance — one focused session

1. Open Controls from the app menu. Adjust response left/right, try the left-stick steering preset and combined Touch brakes, inspect the live preview, then restore the preferred settings. Verify hand-specific prompts match the assignment.
2. Open Cockpit guide, step through references and return. Check that its highlight disappears when leaving.
3. Fly one short lesson or circuit. Pause to inspect the current objective/recent path on the map. End the flight and check that the observed behavior and next-practice advice are useful and truthful.
4. Pause/resume and briefly lose controller tracking while seated safely. Flight should pause and require deliberate resumption; no held input should leak through the menu.
5. Listen to a lesson and a demo handover: clear Michael narration, appropriate control-ownership wording and no distracting rise in the engine bed between adjacent phrases.

These are acceptance checks for the delivered candidate, not claims of completed human testing.
