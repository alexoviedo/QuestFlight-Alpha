# Flight lesson quality audit and rewrite

Status: implemented, focused checks and Android build passed, installed and launched on Quest 3; worn acceptance pending. This revises the existing six lessons, not a complete private-pilot syllabus.

## Findings corrected

- The ground coach unconditionally issued the same Yoke cue during roll and pitch checks, hiding the specific instruction. It now follows observed movement progress with distinct phase text and narration. A completed control check advances without waiting for redundant speech.
- Turn completion allowed 25 degrees of bank at the target heading. It now requires a nearly level rollout with controlled altitude and speed.
- Climb level-off was not requested until the target altitude was reached. The transition now anticipates the altitude using a bounded climb-rate allowance. Descent also anticipates level-off.
- The debrief reported seconds in target, conflating different skills. It now reports observed completion, a demonstrated skill, a specific next-practice action, assistance status and a reflection question.
- All 37 stage scripts and 12 cockpit-reference explanations were rewritten. The canonical copy, including success and retry feedback, is in `QuestFlight/Assets/Production/Training/LessonTeachingContent.json`; `VoiceScripts.json` supplies the matching offline recordings.

## Research and decisions

FAA instruction uses explanation, demonstration, supervised performance and evaluation. Assessment should identify observed behavior, give a concrete improvement and invite self-assessment. This deterministic coach implements the observable parts of that method.

- [FAA instructor techniques, chapter 9](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/aviation_instructors_handbook/11_aih_chapter_9.pdf)
- [FAA assessment, chapter 6](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/aviation_instructors_handbook/08_aih_chapter_6.pdf)
- [FAA basic flight maneuvers, chapter 3](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/airplane_handbook/04_afh_ch3.pdf): outside references with brief instrument checks; establish attitude and power before trim.
- [C172P POH](https://www.befa.org/wp-content/uploads/2019/12/POH-Cessna-172P.pdf) and [BEFA C172P checklist](https://www.befa.org/wp-content/uploads/2019/11/C172P-Checklist-rev-A-2017-12.pdf): variant-specific configuration and speed references. These are not universal settings for every C172 or operating condition.
- [Boulder procedures](https://bouldercolorado.gov/services/airport-traffic-pattern): 6,300 feet MSL, downwind south of Jay Road, glider awareness and no straight-in arrival to runway 8. The landing checkpoint is explicitly practice already on final, not an arrival clearance.
- [MSFS objectives](https://www.flightsimulator.com/accessibility/): summarize the current task and necessary inputs.
- [X-Plane Mobile manual](https://www.x-plane.com/manuals/mobile/): contextual instructor messages identify features and guide actions. Original QuestFlight scripts adopt these teaching principles without copying proprietary lessons.

## Boundaries

Accepted UI, marker geometry, cockpit anchors, menu controls, native trims, routes, weather and flight authority are preserved. No new maneuver or advanced AI is introduced. This is not complete preflight, run-up, density-altitude planning, crosswind or ATC training. Mixture/carb-heat checkpoint presets are identified as presets, not universal real-world settings. The existing route is simplified and not validated against every local geographic restriction.

Internal stability dwell prevents accidental completion; it is not a proficiency score. The simulator cannot evaluate visual scanning, understanding or judgment merely from aircraft state. Human instructor review and headset acceptance remain necessary before making stronger educational claims. Narration uses the existing offline synthetic voice, not a new studio recording.

## Validation

Nine focused behavior checks passed (`lesson_quality_20260921/targeted_final_results.xml`), including students who have already leveled at the assigned climb/descent altitude. Inspected the new briefing, control-phase prompt and debrief in editor pilot-eye captures; actual headset acceptance remains pending. Checkpoint/route/trim values were compared with HEAD and preserved across all six assets. The initial test wrapper ignored its filter and started unrelated tests; that run was stopped and was not a passing full-suite validation. Android build evidence is stored alongside the focused results.

Read the full [rewritten scripts](FLIGHT_LESSON_SCRIPTS_2026-09-21.md). Regenerate narration with `scripts/generate_training_voice.ps1`, then apply `ProductLessonTeachingAuthoring.RunBatch` to update serialized content without regenerating UI, routes or flight trims.

## Quest candidate

Final package SHA-256: `ca506708a535587b13843350b5b3a5ea321e20efaf13ec484e04df23af57203b`. Installation succeeded on the connected Quest 3; the installed package hash matches. Launch was requested successfully. Evidence: `lesson_quality_20260921/android_final/product_apk_manifest.json` and `lesson_quality_20260921/quest_install.json` in the external product-artifacts directory. A final whitespace-only cleanup does not alter the built behavior.

Start with **Flight lessons → Cockpit and taxi**. Check that each remaining yoke motion is explicit, roll and pitch are clearly distinguished, and repeat returns to the appropriate stage instruction. Then review the climb/level-off and turn/rollout coaching and the practical debrief. This final headset review has not yet been performed; no new hardware performance or educational proficiency claim is made.
