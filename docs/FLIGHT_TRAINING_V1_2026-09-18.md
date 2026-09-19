# Flight Training V1 — approved implementation

Scope approved September 18, 2026: six short C172 simulator-practice lessons, using the existing authored Boulder scene, cockpit, shared wind, native JSBSim authority, and Quest controls. No advanced maneuvers, aircraft/physics overhaul, radio/ATC system, instructor avatar, cloud service, multiplayer, or new scenery is included.

## What the pilot uses

Choose **X — Flight Lessons** on the existing startup menu. Use the left stick to choose a lesson and A to read its briefing. All lessons are available immediately; the default selection is **Level flight and gentle turns**.

| Lesson | Practice | Start and outcome |
|---|---|---|
| Cockpit and taxi | Yoke, rudder, brakes, slow taxi, hold short | Stopped on taxiway Alpha; stop before runway 08 |
| Level flight and gentle turns | Outside/instrument scan, level flight, coordinated shallow turns, rollout | Airborne near 7000 ft MSL; sustained stable intervals |
| Climbs, descents and trim | Power/attitude, level-off, descent, trim relief | Airborne near 7000 ft; climb to 7300, return to 7000, trim |
| Takeoff and departure | Available configuration checks, centerline, rotation, climb | Stopped on runway 08; straight-ahead departure through 6000 ft |
| Pattern and approach decisions | Downwind, configuration, base, final stability, go-around choice | North-side 08 downwind at 6300 ft, south of Jay Road |
| Landing and go-around | Final, flare/touchdown/rollout, or a staged go-around | Final approach checkpoint; controlled rollout or clean climb |

The first-flight lesson has an optional 30-second level-flight/shallow-turn demonstration. Demonstration and assistance are explicitly labeled and spoken. Practice does not turn guidance back on when the pilot releases the yoke. Right Touch thumbstick rudder remains available in manual training with the existing focus, tracking and neutral-rearm checks.

| Context | Controls |
|---|---|
| Lesson selection | Left stick: select; A: briefing |
| Briefing | A: practice; B: lesson list; Y: repeat; X: demonstration where offered |
| Flying | A: pause; B: repeat instruction; X: request assistance / take control; Y: captions |
| Paused | A: resume; X: restart exercise; B: lessons; Y: repeat instruction |
| Debrief | A: retry; B: lessons; X: next lesson |

The existing left Menu button still opens seat calibration. Training pauses on calibration, focus/presence loss and tracked-controller loss; resuming is deliberate. Ground/low-altitude help pauses the exercise instead of attempting a low-altitude hold. Restart uses the lesson checkpoint through the native reset API and a short comfort fade.

## Implementation boundaries

- `ProductTrainingController` is serialized once on the existing input root. The lesson catalog, two small text panels and one voice source are authored into the world prefab. Runtime code creates no product hierarchy or cameras and never writes flight/XR/seat transforms.
- The existing coordinator remains the sole flight authority. Its new read-only post-step event catches brief contact transitions; ordinary evaluation runs at about 10 Hz. Presentation refreshes at 5 Hz. There are no new lights, render targets, per-frame files or unbounded telemetry lists.
- Available flight/control channels drive condition-based progress. Dwell windows and short-excursion tolerance prevent instant or jitter-driven completion. Ground settling is distinguished from landing. Touchdown captures pre-contact sink and runway position; unstable approaches prompt go-around. Go-around stages require intermediate flap settings when appropriate.
- Independent completion and assisted completion are distinct. Local JSON records completion, attempt count and the latest factual debrief per lesson. Completion is never downgraded by a later unsuccessful attempt. No progression locks, accounts or network service.
- Airport lessons use the existing traffic director's runway-protection path. Traffic remains visible and frozen while a lesson is paused; soaring traffic is retained. Runway protection is released in the lesson list/debrief.
- The shared atmospheric model remains active. Lesson starts/targets are authored; there is no special wind-free physics mode.
- Airborne starts are calibrated offline through the actual native reset/control/wind path. Neutral untrimmed starts initially produced unacceptable pitch transients; the authoring tool solves initial trim/power against short native runs and checks the resulting checkpoint. This changes initial settings, not flight dynamics. No hidden runtime autopilot is used for manual practice.
- Existing source guards for `FlightDynamicsCoordinator` and `ProductControlInput` are repinned only for the approved observer, Training rudder eligibility and lesson-specific reset routing. Existing scenic guidance/recovery sources remain unchanged.

## Content and limitations

References: [FAA Airplane Flying Handbook](https://www.faa.gov/regulations_policies/handbooks_manuals/aviation/airplane_handbook), [Cessna 172P POH, manufacturer document hosted by BEFA](https://www.befa.org/wp-content/uploads/2019/12/POH-Cessna-172P.pdf), and [Boulder's published traffic pattern](https://bouldercolorado.gov/services/airport-traffic-pattern). The retired R&D training scaffold supplied conceptual history only; none of its runtime systems is imported.

C172P reference speeds inform the cues. Scoring uses the native calibrated-airspeed channel and broad simulator tolerances, not a certified IAS calibration or proficiency standard. Heading cues use the currently modeled true-heading indication. A simulation reset onto final is not presented as a real-world straight-in arrival procedure. Unmodeled checklist items and radio clearance are not scored as verified. These exercises provide no training credit.

Forty-four offline narration clips use original scripts and one playback source. Source text, reproduction script and provenance are recorded in `Assets/Production/Training/SOURCE.md` and `VoiceScripts.json`.

## Validation and acceptance

Evidence directory: `../T2-QuestFlight-product-artifacts/flight_training_20260918/`.

Required focused checks: evaluator positive/negative cases, persistence, manual/pause ownership, right-stick regression tests, original guidance integration guards; native checkpoint checks and complete first-flight guidance/evaluator run; inspected lesson/menu/cockpit presentation; Android build and installation verification. Desktop/native timing is not Quest timing.

Implementation/build evidence and the final worn-check result are recorded below when obtained. User acceptance of lesson readability, coaching pace and controller ergonomics remains a separate final check.

### Implementation evidence

- `tests_final3.xml`: **28/28 passed**, covering sustained evaluation, manual/assisted ownership, pause/resume controls, brief touchdown detection, go-around configuration, local progress, lesson switching, native retry, authored references and the existing right-stick/guidance regression guards.
- `native_training.json`: all six native checkpoints remained finite and manageable during their eight-second checks. The first-flight reference guidance completed all five evaluation stages in 58.1 simulated seconds. This is not an end-to-end pilot test of every lesson.
- `standard_selector_final/`: standard selector QA passed; the stereo capture was inspected, including the available **Flight Lessons / Six guided exercises** entry.
- `ui_practice_verified/`: live OpenXR simulator checks passed with no reported errors; stereo inspection confirmed the complete cue fits both eyes above the panel, with the primary gauges visible. The initial overlap and outer-eye clipping were corrected before this capture. `ui_menu/` and `ui_briefing/` contain the inspected list and briefing views.
- Hidden Windows QA players use a development-only focus exception to capture an active lesson. Android focus/presence/tracking pause behavior is unchanged. The terrain stability witness starts from the restored flight scene after the showroom transition; it does not mistake the intentional showroom visibility switch for a terrain defect.
- The product integrity allowlist includes only the new approved product curriculum directories and named editor tools, alongside the existing approved guidance files. Legacy R&D training remains excluded.
- All 44 narration clips have matching original scripts and `.meta` files. No new model, scenery, camera, light, network dependency or flight authority was introduced.

`android_ready/`: the Android ARM64/Vulkan/OpenXR product build succeeded, including product integrity, native compilation and verification of all 1,912 authored scenery payload files. APK size: 1,794,705,245 bytes; SHA-256: `25a96ee4b5ef7ed3af7d8a3c8cc495ff8c7d983713dfc14287623403c0d9db2d`. Build-source identity and the complete source change archive are preserved beside the manifest. This is a local training candidate, not a new public release.

Installed successfully on the connected Quest 3 and launched. The SHA-256 of the installed `base.apk` matches the candidate exactly (`quest_install.json`). Worn lesson acceptance remains pending. The completed temporary simulator project was removed after preserving the build and captures; generated SDK bookkeeping files were moved out of the source tree into the evidence directory.

Headset acceptance remains pending. Start with **X — Flight Lessons → Level flight and gentle turns → A practice**. Confirm the spoken instruction and readable cue, manual yoke/right-stick rudder, pause/resume, explicit assistance/handback and retry. Then use the taxi lesson to check ground coaching and hold-short behavior. No Quest frame-time or thermal acceptance is claimed from the desktop checks.

Build prerequisite corrected: the promoted road-authority JSON was normalized to LF by Git, while the road-contact provenance file still recorded its CRLF SHA-256. Replacing LF with CRLF reproduced the old expected hash exactly; all six other mesh/bank/terrain/manifest fingerprints matched. Only the provenance fingerprint was updated to the committed LF bytes. No road geometry, paths, collision data or scenery was regenerated.
