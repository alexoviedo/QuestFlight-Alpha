Current checkpoint: approved menu repair is implemented, built and installed as0.1.0-alpha.3/code3. See [MENU_REPAIR_ACCEPTANCE.md](MENU_REPAIR_ACCEPTANCE.md) for exact current acceptance and APK/source hashes. Worn Quest acceptance remains pending. Earlier checkpoint text below is historical.

# Approved menu repair

User approved every fix M01–M17 on 2026-10-14. Implementation and complete runnable validation are authorized.

Status: IN_PROGRESS. Source backup: external menu_repair_20261014/menu_source_before.zip.

Checklist: M01 selected-aircraft readiness; M02 routes; M03 launch actions; M04 stable activities; M05 capabilities; M06 aircraft help; M07 settings/audio; M08 pointer; M09 input handoff; M10 resume; M11 action results; M12 caller/focus; M13 confirmations; M14 history; M15 calibration; M16 layout; M17 end-to-end tests and visual/build evidence.

Implementation checkpoint: M01-M15 source changes in place; M16 adaptive row/hit-target layout added. Compile smoke passed. Page/action audit initially 38/39: QuickStart uses explicit Skip guide, now covered as an exit. Six real player-loop flow cases added through ProcessMenuInput and dispatcher. Full validation continues.

Full regression checkpoint:882 EditMode tests =873 PASS,1 obsolete Summary Back expectation,8 baseline diagnostic skips. All6 real player-loop menu flows PASS, including both physical tow starts and all7 SGS lesson briefings. Updated Summary expectation to Home. Added4 broader flows: all6 C172 lessons/references; guest/guide/calibration/history/reports; already-paused lesson resume; all settings. Shared input now supports horizontal gamepad adjustment and held gamepad A protection, native loading/failure Home has Retry, saved history carries aircraft/lesson identity. New targeted suite running before visual/build stages.

Latest checkpoint: 50/50 expanded menu tests, 118/118 affected regressions and 31/31 PlayMode PASS. Inspected 112 menu previews, repaired displayed SGS references and bindings. Final pause visibility uses a material-controlled depth comparison on the existing TMP mobile shader, retaining its stereo and matching motion/depth pass, plus depth-independent surfaces; cockpit SDK interactables now have authored serialized menu ownership, preserving their previous states on close. Final combined validation/build continues. Prior installed APK is still unchanged.

## Implemented behavior

| Fix | Concrete behavior |
|---|---|
| M01 | Launch readiness checks the selected aircraft presentation; SGS no longer waits for the disabled C172 cockpit. |
| M02 | Home exposes Fly C172, Fly SGS, Lessons, Settings, Help and History; aircraft hubs have usable Back paths. |
| M03 | Soaring launch actions and lesson restart occupy distinct authored button slots; duplicate actions fail explicitly. |
| M04 | Activities resolve stable aircraft-specific lesson IDs instead of C172 numeric indexes. |
| M05 | SGS offers supported ground-launch, soaring and lesson routes; unsupported powered demos and experimental rendering are not advertised as available. |
| M06 | SGS briefings, checklists, instrument and control references describe the sailplane, tow release and separate airbrake/trim. |
| M07 | Controls and audio reflect the selected aircraft; SGS aircraft audio observes the saved volume preference. |
| M08 | Authored SDK ray/poke buttons subscribe correctly and share the same action dispatcher as controller selection. |
| M09 | Menu input must rearm after release; returning to flight requires neutral buttons, sticks and triggers to avoid accidental flight actions. |
| M10 | Explicit Resume handles already-paused lessons and verifies native/focus/recovery conditions. |
| M11 | Rejected actions explain why; native preparation failures retain Home, Help, Settings and Retry. |
| M12 | Back restores the calling page, lesson pagination and focused action; it does not silently resume flight. |
| M13 | Replacing, restarting or ending an active session requires a visible confirmation; cancel clears pending actions. |
| M14 | New history records carry aircraft and stable lesson identity; ambiguous legacy records remain preserved without guessing. |
| M15 | Calibration saves/cancels only seat-relative changes; recenter requests the running XR subsystem and does not write the tracked camera. |
| M16 | Shared adaptive layout accounts for wrapped labels and matching hit targets; modal visuals overlay cockpit geometry and cockpit interactables yield to menus. |
| M17 | Real player-loop and SDK event tests cover routes, transitions, lessons, settings, confirmations, calibration, history and resume; final stereo/build receipts remain separate from hardware acceptance. |

## Navigation contract

Home -> Fly SGS -> SGS setup -> Ground launch / soaring -> Runway 08 or 26 -> Flight.
Home -> Fly SGS -> SGS setup -> Lessons -> paginated lesson list -> briefing -> start.
Home -> Fly C172 -> C172 setup -> supported flight/demo/lesson choices.
While flying, Menu pauses -> Settings / Map and references / SGS launch actions / End flight / Home.
Every nested page returns to its actual caller. Back from the pause root keeps flight paused; Resume is explicit.

## Evidence limits

Menu/software acceptance does not establish aircraft fidelity, Quest frame timing or human acceptance. Previous scenery budget failures and native fidelity limitations remain separately tracked; this repair does not raise caps or reduce scenery/quality to conceal them. Sleeping or unfocused Quest logs are not valid worn-headset performance evidence.

Validation checkpoint21:17Z: final main and route preview captures112+168 generated; inspected pause, SGS hub, ground launch, lesson pagination and references. Live OpenXR simulator generated45 images per aircraft (90 total), XR active/stereo true, one authored hierarchy and no missing components; machine gate remains FAIL for mountain identity across showroom visibility changes. Standard QA107/130 with23 retained failures;129 non-selector resource rows exactly match previous candidate. Home selector remains30 draws,99,488 triangles,10 materials; no budgets raised. Final label/scope checks, coverage/closure/static and Android build continue.

21:20Z final validation: final label/scope2/2 PASS; inspected corrected SGS stick label in both eyes. Integrity PASS (5,179 assets; one authored hierarchy), render coverage PASS with no shader errors, closure PASS (753 approved,0 rejected,0 unknown). Static gate correctly rejects retained failed standard QA; no waiver or resource-limit increase. Diff whitespace check PASS. Integrated Android release build running externally under menu_repair_20261014/final_android (session80781). No runtime changes planned after source capture.
