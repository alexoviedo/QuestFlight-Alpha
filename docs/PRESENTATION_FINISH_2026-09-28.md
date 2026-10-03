# Approved presentation finish

Bounded extension of current authored product; no renderer or flight-physics rewrite.

- [x] Timed Michael subtitles, persistent lesson objective, separate caption sizing/background.
- [x] Touch diagram, graphical response/input preview, focused explanations.
- [x] Optional yoke toggle, default off, pause/tracking-loss release.
- [x] Startup/error/recovery copy, accurate brake wording, resilient history backup.
- [x] Visible report identifiers, tester guide and reproducible packaging script.
- [x] Short showcase reusing authored arrival and native landing, relevant narration.
- [x] Observable cockpit-state checklist; manual checks never automatically passed.
- [x] Direct recommended exercise from debrief/history.
- [x] Audio finishing and capture-speckle investigation (limits below).
- [x] Consistent hints, disabled states and readable history timestamps.
- [x] Focused validation, inspected stereo/standard visual QA, Android build and checksum-verified tester package (QA limitations recorded in evidence).

Completion audit: all approved implementation priorities and polish ideas in this bounded presentation finish are implemented. The material-speckle investigation is complete but inconclusive; it is not claimed as a fixed defect. Hardware acceptance remains pending. Final APK SHA-256: `d20a7922f36f73220cbee97f40d5842e73378bcc1ab32f551447b5295a7342b1`. Tester package: sibling `T2-QuestFlight-product-artifacts/presentation_finish_20260928/tester_package/`.

Hardware readability, controls, listening and sustained frame/thermal acceptance wait until the user's headset is available. Do not claim those passed from desktop evidence.

## Implemented behavior

Narration uses offline Kokoro token timings authenticated against the actual PCM recording. All 107 clips have matching hashes, without runtime speech inference. The title retains the current lesson objective while the preceding sentence finishes. Menu text and caption settings have separate controls; caption size reaches 160%, with solid or subtly transparent backing. Largest-caption layout measures the title, phrase and hint independently.

The controller diagram and response curve extend the existing authored menu. They render only while their paused page is open; no added flight-view scenery workload. Toggle-to-hold is an explicit option, off by default, and affects only the yoke. A second press releases it; pause, focus loss and tracking loss release capture and require release before reacquisition.

The checklist reports only available cockpit-system state. It separately asks the pilot to verify manual checks; no simulated checklist completion or certification claim. History stores a recommended lesson and a recoverable prior save. Empty navigation actions are visibly disabled and skipped by thumbstick focus. Report filenames and displayed IDs match; reports remain local until USB export.

Short showcase is an explicit airborne arrival, not an accelerated full tour. It reuses the current route, native flight authority, handover, runway reservation and landing/exit logic. Michael explains the airborne start. Both runway directions completed native scenarios in 321–371 seconds; existing full-tour behavior remains available.

The audio audit checks hashes, phrase bounds, finite PCM and clipping. Existing speech-priority mixing is retained. The new introduction uses the existing Michael voice and mono 24 kHz compressed import settings. Listening acceptance remains pending.

Colored speckling was visible in older desktop stereo captures around small cockpit labels. An isolated current editor inspection with original, diffuse-only and base-color-only materials did not reproduce that colored speckling. These inspection variants were not saved. This does not establish a Quest defect or a verified fix; compare the final stereo capture and retain a hardware observation item rather than changing the cockpit materials speculatively.

See [tester guide](TESTER_GUIDE.md) and [evidence](evidence/PRESENTATION_FINISH_2026-09-28.md). Package creation: `scripts/package_tester_candidate.ps1 -Destination <external-artifact-directory>`.
