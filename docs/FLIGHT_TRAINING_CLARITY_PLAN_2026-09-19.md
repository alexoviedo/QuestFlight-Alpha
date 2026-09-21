# Flight training clarity and coaching — proposed implementation plan

Status: **APPROVED September 19, 2026 — implementation in progress**, including the user's requested application-wide navigation correction.

Prepared September 19, 2026, following the user's worn feedback on the six-lesson training build. This is a refinement of [Flight Training V1](FLIGHT_TRAINING_V1_2026-09-18.md), within the same six beginner lessons. It does not add advanced AI, advanced maneuvers, a new flight model, or a new training platform.

Revision 3, September 19: the user approved implementation and requires consistent navigation throughout the simulator. The global navigation contract below supersedes the previous A-pause/B-repeat proposal. **No pilot/instructor avatar or pointing hand is included.**

## Approved application-wide navigation contract

Meta's documented conventions are left **Menu** for the app pause/menu and **B/Y** for Back/Cancel; the right Meta button remains system-owned. Sources: [controller design](https://developers.meta.com/horizon/design/controllers/), [Quest input guidance](https://developers.meta.com/horizon/resources/vrc-quest-input-1/).

- **Left Menu:** open the same app menu from Free Flight, Demo, Training, calibration or a subordinate panel. While flying it pauses coherently. At the app-menu root, Menu closes/resumes the suspended context when safe; tracking/focus recovery still requires deliberate valid input.
- **B or Y:** Back/Cancel one UI level. While flying, open the app pause menu. At its root, return to the suspended context; on the main menu, remain there. Never start Free Flight, repeat narration, reset calibration or change captions with these buttons.
- **Main menu:** an explicitly labeled option in the app menu, reachable from every mode. During an active flight/lesson, confirm ending that session; Cancel returns to the paused session. Preserve completed progress and calibration. Returning must release demonstration/training authority and runway reservations without leaving duplicate pause ownership or stale inputs.
- **Calibration:** a named app/main-menu option; Back cancels the draft. Existing calibration adjustment controls remain, but reset is a named action rather than Y. Saving or cancelling returns to the menu that opened it.
- **A / point-and-trigger:** select the focused, labeled action in menus. **X:** optional Repeat/show-current-cue shortcut during training; always the same lesson meaning, never take-control/restart. No lesson-button press is needed for normal coaching or progression.
- Menu/input ownership is centralized. One physical press performs one action; triggers used for UI must be released before aircraft input rearms. Do not consume the platform Meta button. B/Y never leak into older mode/lesson shortcuts.

Implementation begins with this shared navigation foundation, then the approved first-flight coaching milestone and six-lesson rollout. The extra navigation scope is explicitly authorized by the user's September 19 request.

## Decision

Replace the current paragraph-driven lesson delivery with a small, deterministic coaching loop:

**Identify the reference → explain/show the next action → let the learner try → observe → give a specific correction when needed → repeat with less help.**

The biggest improvement will come from connecting words to visible cockpit references and immediate actions. Shorter recordings alone will not solve missing pointers, ambiguous phase transitions, inaccessible controls, or unhelpful feedback.

The next implementation must deliver a complete improved first-flight lesson with automatic live coaching and recognizable, avatar-free references, then apply the same working approach to the remaining five lessons. Its first headset candidate must not depend on finishing six rewrites or a new progress system. Keep the aircraft, environment, shared wind, and existing native flight authority.

### What was wrong with the first proposal

1. **It preserved a confusing interaction model.** In the actual controller, X means demonstration, assistance/handback, restart or next lesson depending on state. B means repeat during flight and leave for the lesson list when paused. Preserving this because it already exists is not a usability justification.
2. **It risked becoming a sequence of interruptions.** Pausing for each instrument could replace a lecture with a click-through tutorial. Group essential orientation before flight; teach and coach during continuous practice. Do not require acknowledgements for routine corrections or every completed step.
3. **Continuous coaching was an intention, not a complete delivery requirement.** The revision specifies which conditions to observe, when to speak, how to prioritize corrections and how to stop obsolete advice. A shorter narration plus a highlight would not meet this requirement.
4. **It did not sufficiently separate a learning goal from an overall maneuver pass.** A bank-control exercise must not silently require five untaught targets to advance. During guided steps, assess the stated skill while independently monitoring the flight envelope. Later combined practice can require the full taught maneuver.
5. **It scheduled too much before proving the interaction.** Detailed skill-history storage, extensive quizzes and six full rewrites must not delay the first complete, improved lesson. Retain a short factual debrief and existing progress; defer a new analytics framework.

The design moves forward only if the learner can fly while receiving relevant guidance without operating a lesson interface every few seconds. More polished prerecorded paragraphs would fail this milestone.

## 1. What the current project actually does

Read-only audit: branch `feature/flight-training-v1`, training implementation commit `ec764a25`. Inspected current scripts, authored lesson definitions, voice scripts/WAV durations, and the existing stereo capture at `../T2-QuestFlight-product-artifacts/flight_training_20260918/ui_practice_verified/cockpit_neutral_stereo.png`. No new simulator session was needed for this assessment.

| Finding | Evidence and consequence |
|---|---|
| Briefings are dense | The six existing briefing clips last approximately 20–25 seconds. They combine objectives, controls, locations and restrictions before practice begins. The problem is information density and lack of interaction, not duration alone. |
| The first flying task assumes prior knowledge | `ProductTrainingAuthoring.cs` immediately asks for 7,000 feet, 90 knots, heading 090 true, wings level and small corrections. The default lesson selection bypasses cockpit familiarization. |
| There is no direct visual reference | The inspected stereo capture shows a paragraph, feedback and button legend above the panel. No instrument is identified visually. `TrainingStage` has no reference/anchor metadata. |
| Speech and progression are independent | `AdvanceStage()` starts the next narration; `Speak()` stops whatever was playing. There is no normal-speech completion gate or priority queue. A qualifying state can advance the lesson before the current instruction finishes. This is a code-supported failure path, not a claim that every cue was interrupted in the user's session. |
| Some steps can pass while being explained | Brake-check narration is about 5.8 seconds with a two-second qualifying dwell. Takeoff-configuration narration is about 11.1 seconds with a two-second dwell. The checkpoint may already satisfy the conditions. |
| Corrections are mostly text | `TrainingEvaluator` supplies messages such as returning to a target heading, but ordinary corrective feedback is not voiced. B repeats the entire current stage recording. |
| Demonstration does not teach individual references | First flight has a fixed 30-second level/turn demonstration. It does not synchronize a sequence of instrument pointers and explanations to individual demonstrated actions. |
| Instruction and indication need an explicit contract | The directional gyro currently uses true heading; the magnetic compass subtracts magnetic variation. Evaluation uses native calibrated airspeed and MSL altitude, while the cockpit has indicated-air-data channels. These are different definitions; their numerical agreement must be checked, not assumed. |
| Input descriptions need verification | Taxi says to press toe brakes rather than naming the Quest input. The current uncommitted input change adds Touch trigger brakes. Trim has a gamepad D-pad path, while the inspected Touch cockpit target enum has no trim-wheel target. A visible animated control is not proof of a usable Touch interaction. |
| Face-button meanings change across states | `ReadControls()` maps A/B/X/Y separately in briefing, flight, pause and debrief. `ToggleHelp()` also changes X from airborne assistance to pausing near the ground. These are materially different outcomes behind a generic help label. |
| Useful UI machinery already exists | The startup menu handles pointable hover/select events and visible button feedback. Training can reuse that interaction pattern, with explicitly authored lesson actions; it does not need an avatar or a replacement XR interaction stack. |
| Practice outcomes are coarse | Progress records independent/assisted lesson completion and a short debrief. They do not distinguish finding a reference, guided practice, and repeating the task with reduced help. |

The existing automated/native/build checks establish implementation behavior. They do not establish that a beginner understands the instructions. The user's feedback is direct evidence that instructional acceptance has not been achieved.

## 2. Research and what to borrow

### Real flight instruction

The FAA instructor handbook describes explaining the task and intended result, demonstrating it, supervising learner practice, and evaluating it. It also emphasizes clear transfer of controls and combining outside references with instrument indications. These are documented instructor methods, not observations of every individual CFI. Our adaptation is explicit teaching phases, immediate practice, an outside scan with brief instrument cross-checks, and unmistakable control ownership. [FAA, Techniques of Flight Instruction, pp. 9-5–9-10](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/aviation_instructors_handbook/11_aih_chapter_9.pdf)

FAA assessment guidance calls for factual, specific feedback and practical suggestions for improvement. It separates different assessment purposes and warns that a comprehensive assessment need not be long. We should give one evidenced success and one useful next practice action, rather than a vague score or an exhaustive list of deviations. [FAA, Assessment, pp. 6-1–6-3](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/aviation_instructors_handbook/08_aih_chapter_6.pdf)

### Established simulator approaches

| Source | Documented approach | Application here |
|---|---|---|
| Microsoft Flight Simulator | Its official beginner guide directs learners to C152 lessons with a virtual instructor covering taxi, takeoff, flight maneuvers and landing before free flight. | Keep a coherent beginner sequence and recognizable trainer cockpit. Do not expand into career systems or advanced ratings. [Official guide](https://www.flightsimulator.com/guide/what-should-i-do-first-in-microsoft-flight-simulator/) |
| X-Plane 12 | Flight School includes basic C172 tutorials. Saved situations support repeatable practice from a selected aircraft/environment state. | Offer safe, authored retry points for the part being learned, using our native reset path. Do not require a second computer or build a general replay system. [Official manual, Flight School and Reusable Situations](https://www.x-plane.com/manuals/desktop/) |
| FS Academy | Its VFR support page documents real-time objectives and a go-around lesson problem when learners act during the explanation. It explicitly identifies the need to distinguish discussion from execution. | Display the current action and clearly distinguish explanation from practice. Handle early valid actions gracefully instead of trapping the learner behind dialogue timing. [Official VFR support](https://www.fsacademy.co.uk/support-vfr) |

These sources support the design choices; they do not prove learning effectiveness or Quest performance for our implementation. Competitor feature descriptions are not a substitute for inspecting our own learner experience.

For the revised lesson UI, Meta documents controller-ray targeting and trigger selection as standard interaction mappings. This supports reusing recognizable point-and-select buttons for paused choices. It does not justify requiring menu interaction while the learner is flying. [Meta input mappings](https://developers.meta.com/horizon/design/interactions-input-mappings/)

## 3. The proposed student experience

### A. Avatar-free pointers that identify the exact reference

- Use a thin, high-contrast bracket around the real gauge bezel or control, a short leader ending at the relevant reading index/handle, and a plain label beside it. This is a teaching annotation attached to the cockpit, not an instructor body, hand or a laser from an imaginary seat.
- Separate the **instructor annotation**, which appears automatically, from the **learner's controller ray**, which selects paused menu items. No student pointing or button press is needed to receive a live reference cue.
- Keep gauge needles, numerals and the outside view unobscured. Use shape and text as well as color; do not make red/green recognition necessary. Show the actual reading location, not merely a glowing whole instrument panel.
- Explain its purpose and the relevant reading separately: first what it is, then where to read it, then what value matters now. Teach terms such as heading, bank, trim and slip indicator immediately before they are needed.
- Use explicit authored anchors for the real panel layout, not assumed C172 stock positions. Several instruments are batched into one mesh; derive anchors from existing layout pivots/bounds during editor authoring, without splitting the instrument renderer.
- Anchors follow the cockpit through head movement and banking, with coherent depth in both eyes. Use ordinary depth testing and a small authored surface offset to avoid z-fighting; do not use an always-on-top outline through the panel, fuselage or runway terrain.
- If the reference is outside the current view, give its plain-language relative location. A small directional cue is allowed during paused orientation/requested help; do not cover the windshield with off-screen arrows while flying. Head pose may help place a cue, but is not proof the learner saw or understood it.
- In the initial paused orientation, use only a few useful reference/reading checks. Identification must not actuate the real control. Do not require a quiz at every stage. X restores the current annotation with its short spoken cue.
- One reference is active at a time. Keep it through the relevant explanation/correction, then fade it after the response interval or recognized action; do not infer comprehension from the fade. Routine manual practice returns attention outside. Labels must remain legible with the currently accepted foveation settings.

### B. Separate learning, watching, doing and checking

Use plain labels: **Explain — paused**, **Watch — instructor controls**, **Your turn — you control**, and **Try without prompts**.

Group the references needed for the next short exercise into a skippable, learner-paced orientation before flying. Long explanations and reading checks happen there, or when the learner explicitly requests paused help. Live cues normally contain one immediate action and an observable result. An initial authoring target is roughly 4–8 seconds for ordinary action prompts; this is a product target, not a research-derived universal rule. Do not fragment a time-critical procedure into slow confirmations.

Once practice starts, successful actions advance naturally. There is no routine A/X acknowledgement, automatic pause for every new cue, or need to navigate a panel while flying. Pause only at the learner's request, a clearly announced exercise boundary requiring explanation/reset, or existing safety/focus/tracking conditions. Brief demonstrations use explicit handover and existing guidance; they do not expand into a new automated landing/taxi system.

In practice, the learner controls all aircraft axes. Focusing the lesson on roll does not silently add altitude hold, disable wind, or excuse a dangerous flight state. Monitor the full safety envelope while coaching one learning objective at a time. Separate that envelope from the step's success predicate: wings-level practice should assess the taught attitude skill, with other parameters protected by appropriate bounds, rather than silently demand the old five-condition level-flight score. Combine precision targets only after they have been taught. When workload becomes excessive, offer a pause or explicitly selected existing assistance.

The initial entry should recommend familiarization for new users, with a visible skip option. Keep every lesson available; no forced completion locks. A learner jumping directly to a lesson receives only the prerequisite references needed for that lesson.

### C. Example: replace the first-flight paragraph

The exact spoken script will be authored against the current panel. The intended sequence is:

| Moment | Instruction/presentation | Response |
|---|---|---|
| Short paused orientation | Identify the yoke, outside attitude reference and the few instrument references used in this exercise. Present each reference separately within this one preparation block, including how to read the heading index. | Student can revisit an item or select Ready. No aircraft drift while learning locations. |
| Short demonstration | Explain one small roll correction while existing guidance performs it. Highlight the relevant reference at the same moment. | Student watches the control/result relationship. |
| Handover | Clearly announce the proposed handover; use a visible **Take control** choice at the paused boundary. | After deliberate selection and safe input rearming, manual control starts with a manageable native checkpoint. |
| Initial practice | “Keep the wings level using the outside horizon.” | Observe; allow quiet practice. Coach sustained wrong-direction or excessive input specifically. |
| Brief instrument cross-check | Reidentify the already-taught heading indicator/index with one short live cue, then direct attention outside. | Practice continues; the student does not need to press anything. |
| Shallow-turn practice | Establish the already-explained coordinated shallow turn. Cue rollout as the target approaches; do not wait until the target is passed. | Feedback follows bank, direction, trend and rollout state. |
| Combine taught references | Prompt brief altitude/airspeed cross-checks already introduced in orientation. | Gradually build the outside/instrument scan. Detailed re-explanation remains available through pause/help. |
| Reduced-help repeat | Repeat a taught gentle turn and level interval with pointers removed and routine commentary quiet. | Record demonstrated manual performance and any help requested. |

Teach control-neutralization, coordination and scan during the relevant preparation/demonstration. The table is a delivery example, not a replacement aircraft operating procedure.

### D. Continuous observation and proactive corrective coaching

**Live coaching is automatic and on by default for guided practice. The learner never has to press B or X to receive a correction.** Observe the aircraft at the existing approximately 10 Hz cadence, plus the native event path for contact-sensitive conditions. Observation is continuous throughout practice; speaking is event-driven. Continuous feedback must not become uninterrupted talking.

| Observed situation | Required coaching behavior |
|---|---|
| Sustained wrong turn direction or increasing bank beyond the taught shallow turn | Identify the specific error and give the phase-appropriate roll correction; highlight the reference if useful. Verify input/sign conventions against the actual native/cockpit channels. |
| Approaching the target heading | Cue the taught rollout in time, using turn trend and available response time. Do not wait for a target overshoot or an audio timer. |
| Altitude or airspeed trending outside the exercise range | Select a correction consistent with the maneuver, pitch, power and trend. Do not prescribe pitch or throttle solely from the sign of an altitude/speed error. |
| Sustained poor coordination | Refer to the already-taught slip indication and its appropriate rudder correction. Do not chatter about tiny transient ball motion or use geometric sideslip as an unexplained substitute for the displayed ball. |
| Taxi too fast, approaching the stop location, wrong configuration or unstable approach | Give the relevant speed/stop/configuration/go-around cue at the point it is useful. Time-critical events outrank routine narration. |
| Learner makes the right correction and the aircraft is recovering | Withdraw obsolete advice. Give a brief useful acknowledgement when warranted, then allow recovery time. |
| Stable performance | Leave the compact current objective/target visible and allow quiet flying. Cue only a new phase or useful milestone. |
| Repeated ineffective response or no progress | Restate once in simpler action language and reidentify the reference; offer A pause/help if the difficulty persists. Never silently take control or repeatedly reset the student. |

Arbitrate one actionable correction at a time: immediate safety, time-critical maneuver event, current learning objective, then minor precision. Suppress lower-priority chatter until the important issue resolves. A stale instruction is discarded rather than spoken later from a backlog.

Initial tuning values for **nonurgent** errors: require roughly 1–2 seconds of persistent evidence, then allow roughly 3–5 seconds for a response; avoid repeating the same wording within about 8 seconds. These are provisional coaching defaults, not aircraft limits. Deteriorating safety, rapid changes and time-critical phase events bypass those delays. Use trends and hysteresis to prevent turbulence-driven left/right or climb/descend contradictions. Do not apply a global speech lockout that could delay rotation or go-around.

Keep the live presentation to a compact objective/relevant target and the current optional caption, with clear control ownership. It is not a dashboard of every error. Hints and normal progress require no learner acknowledgement.

Offer **Guided practice** and **Try without prompts** as explicit choices at an exercise boundary. The reduced-help attempt retains safety guidance and on-demand help; requesting routine guidance again returns it to a guided attempt. Record control assistance separately from verbal/visual coaching. Never manufacture an independent pass after intervention.

### E. Make speech reliable

- One voice source, with a small priority scheduler: safety/control ownership, time-critical maneuver cue, ordinary instruction, optional explanation.
- Ordinary progression must not chop off a useful phrase. Explanation/identification steps wait for learner readiness; manual evaluation begins in the intended practice phase.
- Rotation, go-around and other urgent events must not wait behind an explanation or a fixed audio timer. Preempt and discard obsolete cues.
- If the student legitimately completes an action early, recognize the current aircraft state and issue the next relevant cue. Do not insist they undo a correct action so that a script can catch up.
- X repeats only the current relevant cue and restores its pointer. If the aircraft state has changed, repeat the current correction/objective rather than an obsolete instruction. While paused, X repeats the displayed explanation. Repeated requests must not stack recordings or suppress urgent guidance.
- Keep synchronized optional captions, adjustable instructor volume and restrained engine-audio ducking. Preserve warning audibility. Reuse offline speech generation; do not add a voice service or replace the narrator before fixing delivery.

### F. One app-wide navigation contract

This approved revision supersedes the former training-only A-pause/B-repeat proposal. Meta documents left Menu for app menus and B/Y for Back; the right Meta button remains system-owned.

| Input | Consistent behavior throughout the app |
|---|---|
| Left Menu | Open the paused app menu in Free Flight, Demo, training and startup. At the app-menu root, return to the suspended context. |
| B or Y | Back/cancel one level. In flight, open the paused app menu. At that menu's root, resume. At the main menu, stay there. Never reset the seat or repeat narration. |
| Point + trigger, or left stick + A | Select visibly named actions. A has no unrelated flight-management shortcut. |
| X | Optional repeat/show the current lesson cue. Outside lessons it has no lesson action. |
| Right Meta button | Reserved for the system. |

Every mode has a named **Main menu** choice. Ending an active flight/session requires an explicit confirmation; completed progress and saved calibration remain intact. Seat calibration is a named app-menu choice with Save, Cancel, Recenter and Reset buttons. B/Y cancel unsaved changes. Sticks adjust the seat while calibration is open rather than navigating the menu.

Training choices are labeled: Start guided practice, Find cockpit references, Watch demonstration where supported, Try without prompts, Resume, Explain/show current step, Restart exercise, Lesson list and eligible instructor assistance. Returning from a completed demonstration starts a fresh manual practice attempt rather than resuming demonstration guidance. Help explanations do not take aircraft control; eligible hold assistance is a separate named action.

A central input owner consumes menu events. Paused panels suppress aircraft input, including Touch trigger brakes; button/trigger/stick release is required before manual input rearms. An action cannot select the next screen or grab/brake the aircraft using the same held press. Focus loss pauses active flight, and restoring focus never silently resumes it. No runtime XR or environment construction is introduced.

Sources: [Meta controller design](https://developers.meta.com/horizon/design/controllers/), [Meta app-menu requirement](https://developers.meta.com/horizon/resources/vrc-quest-input-1/), [Meta input mappings](https://developers.meta.com/horizon/design/interactions-input-mappings/).

## 4. Apply it to all six existing lessons

| Lesson | Required change |
|---|---|
| Cockpit and taxi | Teach one reachable control at a time, including the actual Touch brake binding. Identify the next taxiway connector/hold-short location in the pilot's view. Show only the next relevant ground reference; remove dependence on an unexplained hidden waypoint. |
| Level flight and gentle turns | Deliver the complete sequence above. Synchronize the existing demonstration to teaching steps and use state-based rollout cues. |
| Climbs, descents and trim | Identify throttle, attitude, airspeed, altitude, vertical-speed and trim references before each use. Separate establishing an attitude/power combination, recognizing the result, leveling, and trimming. Explain the limitations of a controller without force feedback; evaluate input relief rather than claim measured yoke force. |
| Takeoff and departure | Do configuration teaching while stopped. Treat a preconfigured item as a check, not proof the student operated it. Once rolling, keep speech brief and time-sensitive; no quiz, reading panel or acknowledgement during rotation. |
| Pattern and approach decisions | Identify runway, downwind, abeam and next turn using the actual airport view. Teach the geometry before asking for headings. Optional temporary next-reference markers support guided practice; remove them for the reduced-help repeat. Accept valid maneuver geometry within the existing safe training area rather than require an invisible exact point. |
| Landing and go-around | Explain the sight picture and procedure before the moving approach. Use short cues during final/flare, and immediate prioritized go-around guidance when needed. Retry from a safe authored approach checkpoint; do not restart the whole curriculum. |

For each lesson, author a finite coaching-coverage table: monitored condition, reliable input/indication, expected trend, cue, priority, clearance/recovery condition and retry/help response. Each routine maneuver in these six lessons needs coverage; a coach implemented only for heading error does not satisfy this plan. This coaching operates in lesson practice, not as a new free-flight copilot. Unmodeled situations receive an honest pause/retry path rather than invented instructional certainty.

Landmark markers must respect cockpit occlusion, work in both eyes, and remain attached to their actual reference. Do not draw a permanent tunnel through the sky or project arrows through the airplane. Keep existing airport traffic/runway protection.

## 5. Correctness prerequisites within this scope

Before recording revised instructions, produce one small reference/control table: spoken name, authored location, active input, displayed units/basis, authoritative state, and completion evidence.

1. **Teach the display the learner sees.** Use cockpit indicated values for reading/holding an indicated target. Keep native physical state for terrain clearance, runway/contact and other safety checks. Confirm altitude-setting and airspeed semantics; don't substitute MSL or CAS just because the numbers often look close.
2. **Choose one heading reference.** For this milestone use the existing heading indicator and its currently modeled true-heading basis consistently for commands, displayed targets and grading. Explicitly explain this simulator convention once in orientation; do not describe the magnetic compass as an interchangeable reference or imply a real trainer normally uses a true-heading gyro. Converting the avionics to a different basis is separate work.
3. **Verify the actual Quest control path.** Preserve the in-progress trigger-brake change. Check each required action from Touch input to the applied native channel. If an existing lesson requires an unavailable action, include only the narrow binding needed to make that action reachable. Do not tell the user to grab a noninteractive trim wheel. Do not mark a modeled-but-preset item as student-operated.
4. **Keep checkpoints honest.** Already-correct configuration can be acknowledged as checked. A control drill needs evidence of the requested action; a stable-flight drill can legitimately begin stable. Never require arbitrary control movement to defeat an automatic pass. A later manual repeat provides stronger evidence than spawn state alone.
5. **Do not invent comprehension.** Correct control performance is evidence of that performance, not proof of all underlying knowledge. Use a few brief reference/reading checks while paused and an optional factual self-assessment at debrief. No speech recognition or automated interpretation of a student's reasoning.

## 6. Small implementation, bounded performance

Extend the existing lesson data and controller rather than replace them. Add short teaching steps with reference ID, prompt, expected action/outcome, phase, readiness/completion condition, correction choices and optional retry checkpoint. Separate the current learning objective from maneuver-wide evaluation and safety monitoring. Reuse existing evaluator rules where appropriate and preserve native contact observation; do not keep unsuitable stage predicates solely to minimize edits.

Add one serialized cockpit-reference catalog and a small preauthored set of reusable markers/labels. Author anchors in the existing product prefab. Runtime can show, position and hide these authored teaching elements; it must not construct XR, aircraft, environment or backend hierarchies. Never write the tracked camera or aircraft transform.

Reuse the current approximately 10 Hz evaluation cadence, one audio source, fonts/materials and existing pause/reset mechanisms. Share one coherent coaching decision between speech, captions and annotations. Update text when its content changes. Bound cue history and local progress data; avoid per-frame discovery, allocations and growing flight logs. Add no extra camera, render texture, realtime light, full-screen outline effect, aircraft model, instructor/pilot avatar, animated pointing hand or network dependency.

Preserve the combined cockpit instrument meshes. A small unlit reference marker is preferable to a second rendering pass over the panel. Hide teaching geometry outside training and between relevant cues. Asset/voice provenance and paired `.meta` files remain required.

Performance acceptance is an observed comparison against the accepted training build under matched conditions. The design is intended to add little work, but no millisecond or Quest-framerate gain is claimed before measurement.

## 7. Approved implementation order

This approval covers improving all six lessons. Delivery has two meaningful milestones rather than a headset interruption for every small change.

1. **Resolve the first lesson's reference/input contract.** Author its actual cockpit anchors and consistent displayed targets. Establish the shared Menu/Back/Select contract and paused point-and-select controls, including trigger/input isolation. Inventory remaining lesson actions without letting unrelated cockpit work delay first flight.
2. **Deliver the complete first-flight coaching loop.** Include short grouped orientation, optional synchronized demonstration, deliberate handover, continuous automatic feedback, avatar-free pointers, current-cue repeat and a reduced-help repeat. Rewrite its stage predicates as needed. Add only a short factual debrief; no general skill analytics framework.
3. **First headset milestone: prove the new teaching experience.** Perform the focused checks and required narrow stereo/build validation below, then install an identifiable first-flight candidate. It must already demonstrate specific corrections and recovery behavior without developer coaching. Do not wait for the other five rewrites, but do not ask the user to test an unfinished pointer-only prototype. This test judges first flight and shared UI, not acceptance of all six lessons.
4. **Roll the proven pattern through the other five lessons.** Complete the required control mappings, finite coaching coverage, ground/airport references, safe retry points and affected narration. Apply lessons learned from the first milestone; do not repeat the initial UI mistake across six scripts.
5. **Consolidated completion.** Preserve existing saved progress and add only the attempt-level guidance/assistance facts needed for an honest debrief. Complete targeted checks for the newly converted lesson types, build/install, and use one consolidated final worn check. No separate headset ceremony for every gauge, recording or lesson. Deeper per-skill histories remain deferred.

## 8. Minimum meaningful validation and acceptance

The established repository process still applies. Reuse existing scenery/physics evidence; do not launch another map-wide visual campaign.

- Focused logic checks: normal speech completion versus urgent interruption; stale/duplicate cue suppression; pre-satisfied and early-action cases; wrong-direction/recovery feedback; reading/units and Touch-action agreement; manual/demonstration ownership; pause/tracking loss; retry and preservation of saved progress. Test input consumption and trigger release across menu/resume/handback so one physical press never performs two actions.
- Validate any new checkpoint through the native path. No flight-model overhaul or broad physics retest is part of this work.
- Inspect required stereo simulator captures and standard Visual QA for a representative instrument pointer, paused help panel and outside landmark reference. Check both eyes, cockpit occlusion, the actual reading index, captions, and central/peripheral readability. Keep the capture set narrow; use original captures as the baseline.
- Android ARM64/Vulkan/OpenXR build validation and verified installation. Brief final hardware timing observation checks for regression; simulator timing is not Quest timing and a brief run does not establish sustained thermal acceptance.
- First worn learner check: start first flight without external coaching. The learner can identify the named reference, understand the next action, know when to begin, hear a useful automatic correction after a deliberate small error, repeat just that instruction, and use paused help without memorizing changing face-button meanings. The annotation remains attached and legible when the learner turns their head.
- Consolidated final check: exercise the newly converted taxi reference/control and a time-critical takeoff/go-around transition as well. Retain prior first-flight evidence unless changes invalidate it.

Before the first headset invitation, use a small scripted evidence matrix through the actual coach to demonstrate: wrong-direction input produces the intended correction; a stable interval is quiet; successful recovery clears stale advice; an approaching target gets a timely transition cue; urgent events interrupt normally; and menus never leak trigger input to the aircraft. Assertions about state alone are insufficient for audio presentation—inspect the corresponding short spoken/caption sequence. These focused checks replace repeated speculative headset tests, not the required final learner check.

Release criteria: no required reference is unidentified; no multi-paragraph reading while flying; no routine cue is cut off by progression; urgent cues are timely; pointers do not hide the reading; no action depends on an unavailable controller binding; no hidden assistance; no unexplained invisible gate blocks progress. Ordinary flying and correction do not require lesson-button presses. Menu, Back and Select keep their meanings, paused actions are visibly named, and teaching a simple skill does not secretly grade untaught precision targets. Reduced-help completion must reflect actual manual performance. The final user can explain what improved without needing developer directions.

The new delivery is not accepted merely because tests pass or a guided reference flight completes. Worn instructional clarity is a separate acceptance result.

## 9. Ideas considered and priority

| Idea | Decision |
|---|---|
| Avatar-free instrument/control annotations, concise audio and synchronized captions | Approved: directly addresses the reported confusion. |
| Continuous state-aware coaching with prioritized speech, recovery recognition and quiet practice | Core deliverable. Stage-start recordings and static error text alone do not qualify. |
| App-wide Menu/Back and visibly named paused actions | Core deliverable. Reserve B/Y for Back and X for optional repeat; isolate menu selection from aircraft input. |
| Grouped orientation, a few reference checks, current-step help and safe subexercise retry | Approved, limited to the six lessons; avoid repeated midflight pauses/quizzes. |
| Small next-landmark aid for taxi/pattern | Implement for lessons that otherwise depend on unidentified world references. |
| Short factual debrief and attempt-level guidance/assistance facts | Reuse existing progress; defer detailed skill histories and a new scoring framework. |
| More natural voice/alternate voices and voice commands | Defer. They do not resolve missing references or bad timing; speech recognition adds input and reliability scope. |
| Pilot/instructor avatar, body or animated pointing hand | Excluded explicitly. Neither coaching nor pointers depends on one. |
| Full flight replay, 3D flight-path analysis, instructor station, large ground-school library | Defer. Useful potential additions, but unnecessary to correct the present teaching failure. |
| AI conversation, dynamic lesson generation, ATC, exams/certification, advanced maneuvers | Excluded from this approval scope. |

**Approval scope:** improve delivery and usability of the existing six lessons through continuous deterministic coaching, avatar-free annotations, simplified lesson controls and the narrow reference/input corrections necessary to complete them. Implementation is authorized by the September 19 request, including the app-wide navigation correction. First flight is the first learner acceptance milestone; the shared coach and short script revisions may be prepared across all six lessons without further headset interruptions. Runtime and authored asset changes are now in progress; build, visual and worn acceptance must be recorded separately.
