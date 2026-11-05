**Menu and instruction workflow audit — proposed fixes, 15 October 2026**

This is a planning document. No implementation, scene, asset, build, installation, or acceptance status was changed by this audit. Implementation requires the user's approval.

The reported problem is substantiated by the code: the ground release test really disconnects the rope; reconnect is exposed only after all checks, and only in one operation phase. After reconnection, another release does not invalidate `Ready` or `CrewReady`. Clearance does not verify the rope connection. The interface can consequently say the crew and rope are ready and authorize a launch that cannot tow the glider. Simply removing the release instruction would leave the underlying state defects in place.

Scope: current local working tree on `feature/boulder-soaring-autonomous-v2`, HEAD `05a2f721d84030a543fd0b9bc2d3d59d2eb44274`; all 33 page enum values (32 named pages plus `None`, which hosts lesson views), 108 named command IDs, seven shared lesson-slot IDs, all seven SGS lessons and six C172 lessons, their evaluators, launch/tow lifecycle, input handover, setup, recovery, history, and instruction presentation. Existing scene ownership and native flight authority are retained.

Sixteen navigation/training/operation/recovery source files were compared with `final_android_incremental/source_identity.json` for the preserved alpha.4 candidate: all sixteen matched. This binds the principal findings to that candidate's recorded source. It is not a new on-device reproduction or a full APK/source equivalence check.

Evidence classes used below: **D** = defect established from current source/content; **C** = failure conditional on a particular state, whose occurrence needs a runtime test; **U** = a concrete usability/completeness gap, not a claim that every use of the route fails. Priorities: **P0** blocks a journey or permits an invalid transition; **P1** gives misleading instructions, loses context, or makes recovery unnecessarily difficult; **P2** improves clarity and diagnostics. Each proposed fix includes its acceptance condition.

The earlier [menu acceptance](MENU_REPAIR_ACCEPTANCE.md) remains historical evidence of its tests and installed candidate. Its statement that no menu implementation remained is not a sufficient completeness claim for these workflows. The ground-route test ends after reaching the operation screen; the lesson route tests reach briefings. The complete-flight verifier invokes reconnect/clearance directly and reads the release phase programmatically. Those are useful tests, but do not establish that a person can follow the displayed instructions from start to finish. See [menu flow tests](../../QuestFlight/Assets/Editor/Tests/ProductMenuRepairFlowTests.cs:82) and [native flight verifier](../../QuestFlight/Assets/Editor/SoaringCompleteFlightVerification.cs:92).

**Capability/status matrix**

| Area | What already exists and should be reused | Audit result |
|---|---|---|
| Home and aircraft selection | Direct C172/SGS entry, aircraft hubs, native preparation, explicit airborne/ground starts | Keep; fix failed preparation and loss of caller context |
| Menu navigation | Shared dispatch, pointer/buttons, Back stack, selection focus, modal cockpit interaction exclusion | Keep; extend state/context and action-result handling |
| Ground preparation | Real controls, physical release, explicit pair restaging, launch clearance | Incomplete state reconciliation; disconnected launch is allowed |
| Tow and release | Native pair, real rope, release area, manual release | Lesson cue and out-of-order recovery are incomplete |
| Lesson access | Seven SGS and six C172 entries, pagination, briefing/practice/debrief | Entry coverage exists; interrupted and complete journeys are not established |
| Safety and recovery | Native failure stops, recovery holds, explicit restart | Glider restart can leave an unlaunchable aircraft; some holds have no matching screen |
| Help/setup | Guides, instrument pages, calibration, bindings, preview | Wrong/current-step content, reverse navigation defect, and paused-control contradictions |
| Settings/history | Persisted settings, guest separation, summaries and recommendations | Caption/assistance conflicts, C172 advice leaks into SGS review, replay is not actual replay |
| Verification | Prior software, native, visual, Android and install evidence | Preserve; add instruction-led journey evidence before claiming this audit resolved |

**Proposed navigation and operation rules**

Keep the existing Home layout: **Fly C172**, **Fly SGS sailplane**, **Lessons**, **Settings**, **Help / setup**, **Flight history**. Keep distinct SGS choices: **Ground aerotow**, **Airborne gliding practice**, **Soaring lessons**. These starts must state their aircraft, location, ground/airborne condition and exercise before launch.

Use **Launch preparation** as the single name for the ground workflow. Use **Flight status** after departure. All lesson text, menus, HUD cues and references must resolve these same action names. Do not keep competing names such as “Soaring operation,” “Soaring setup,” and “Launch / soaring actions” for the same destination.

In an active flight, the pause menu provides **Resume flight**, **Current lesson / next action** when applicable, **Launch preparation / Flight status**, **Seat position**, **Help and map**, **Settings**, and **End session**. Supporting pages return to their actual caller and preserve focus. If the user opened Menu from the lesson catalog, briefing, preparation, or debrief, the first action is **Return to lessons**, **Return to briefing**, **Continue preparation**, or **Return to results** respectively. None of these starts or restarts flight implicitly. A disabled Resume must never be the only apparent way to continue.

Proposed ground sequence, using existing authored objects and native controls:

1. Choose runway/reference condition. Arrive in a stopped preparation state with a visible **next check**, completion list, and **Rope: attached/disconnected/broken** read from the actual pair.
2. Perform control, combined-brake and trim checks while simulation is running and launch is prohibited. State clearly which checks require movement and which require returning to neutral. Show why a check is not being recorded, including movement above the stationary threshold.
3. Perform the deliberate release test. Immediately show: **“Release test recorded. Rope disconnected. Finish the remaining checks, then open Menu → Launch preparation → Reconnect and prepare launch.”** If remaining checks exist, list them. Do not imply the aircraft can launch yet.
4. **Reconnect and prepare launch** explicitly discloses that the simulator crew restages both aircraft. Complete the native reset, verify a connected pair, invalidate stale clearance, and update the displayed controls/rope status together. Do not reconnect in flight or silently relocate aircraft.
5. Show readiness items separately: checks complete, rope connected, pair valid, traffic clear, controls centered, brakes closed. Give **Return to cockpit** for items that require physical controls. Never tell the user to manipulate disabled cockpit controls while the menu is open.
6. If traffic is holding the launch, explain that the menu pauses traffic too. Provide **Return to cockpit to wait** with a live hold/clear indicator. Simulation, aircraft and traffic use their normal clocks; do not advance only traffic behind a paused flight. Resolve the reservation conflict described in F04 before promising waiting will help.
7. After the pilot closes brakes and centers controls, **Clear and begin launch** verifies all prerequisites again, including actual rope/pair state and control freshness. Complete the normal release-to-neutral input handover before enabling tug launch power. A successful click must produce an unmistakable launch state; a rejection preserves preparation and names the remaining condition.
8. Releasing or breaking the rope before takeoff invalidates launch authorization immediately. While rolling, show the appropriate stop/control instruction; once stopped, expose **Restart launch preparation**, **Review what happened**, and **End session**. Do not label a disconnected glider “Ready.”
9. During tow, show the release-area cue in both free flight and lessons. An early release changes the lesson to a truthful recoverable outcome; it cannot leave the pilot being asked to maintain an attached-tow condition that is now impossible.
10. After landing/stopping, show **Flight summary**, **Prepare another ground launch**, and **Home**. Preparing another launch is an explicit new/restaged operation, with no self-taxi or automatic reattachment implied.

**Issue ledger and exact changes**

**F01 — P0 / D: The release-check journey is not self-explanatory and static checklists can undo readiness.**

Trigger: start ground aerotow or preparation/aerotow/abort lessons; follow “pull the red release,” then seek the next action. Reconnect is absent until every control bit is recorded. The general SGS checklist still says “check red release” after reconnection and even when airborne. Relevant code: [operation checks/instructions](../../QuestFlight/Assets/Scripts/Flight/Soaring/SoaringOperationSession.cs:25), [operation menu](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Soaring.cs:49), [static checklist](../../QuestFlight/Assets/Scripts/Product/ProductFlightExperience.Finish.cs:61).

Change: implement the sequence above. Display the physical rope state independently from checklist completion. Distinguish a historical release test from a current release command. Ground-check instructions disappear once completed; airborne references explain release without instructing another preflight check. Show the future reconnect action with its prerequisites, rather than making it appear without explanation.

Acceptance: follow the text with checks in different orders, including release first and last. The next legal action is always visible or explicitly explained. Reopening the checklist in Ready, Tow, or Soaring must not instruct the user to repeat the ground release test.

**F02 — P0 / D: Clearance can be granted with a disconnected rope.**

Trigger: complete checks/reconnect, return to the cockpit, release again, close brakes and request clearance. `Ready`/`CrewReady` remain true; neither clearance method checks rope attachment. [Clearance](../../QuestFlight/Assets/Scripts/Flight/Backends/FlightDynamicsCoordinator.SoaringOperation.cs:30), [state guard](../../QuestFlight/Assets/Scripts/Flight/Soaring/SoaringOperationSession.cs:51).

Change: invalidate readiness on any disconnect/break, pair fault, or replacement of either native session. The authoritative launch transaction must check a valid current pair, connected rope, appropriate stopped launch state, completed checks, crew status, traffic, and current controls. Readiness display and dispatch must use that same result. A boolean crew flag cannot substitute for current attachment evidence.

Acceptance: release in every prelaunch phase, including immediately after reconnect and between clearance request and execution. No disconnected pair can receive launch power. The menu names **Rope disconnected** and offers a valid stopped preparation route.

**F03 — P0 / D: Early ground separation leaves the operation in Launch, and abort availability uses the wrong boundary.**

`Observe` handles an unattached pair only after `hadAirborne`; otherwise the operation can remain Launch. The tug independently becomes Separated. A tug take-up timeout also does not become a useful operation/menu outcome. The abort button is shown only for operation phase Launch, although the backend uses physical weight-on-wheels/speed/authorization conditions. [Operation observation](../../QuestFlight/Assets/Scripts/Flight/Soaring/SoaringOperationSession.cs:57), [tug transitions](../../QuestFlight/Assets/Scripts/Flight/Soaring/TowPilotController.cs:18), [abort menu](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Soaring.cs:62).

Change: reconcile operation phase with rope and tug launch outcome every native step. Separate stopping, stopped/restartable, airborne separation, and hard fault. Drive abort visibility and its reason from the same physical predicate as dispatch. Preserve the physical release in all appropriate flight conditions; do not lock the rope on to make a lesson pass.

Acceptance: release at rest, during slack take-up, during the low-speed roll, and just after liftoff; exercise weak-link/tug-abort/timeout paths. No stale Launch state, repeated impossible tow instruction, or missing valid stopped restart remains.

**F04 — P0 / C; P1 / U: Traffic waiting can be an unproductive loop, including a reservation conflict.**

The menu pauses time yet asks the user to wait by resuming, then revisit the same page. More seriously, a powered actor in LineUp/Takeoff can make `SoaringRunwayClear` false while `PlayerProtectsRunway` stops that same actor because the pair is staged. This is a conditional mutual block; the audit has not reproduced its occurrence with current authored traffic initialization. [Traffic clearance](../../QuestFlight/Assets/Scripts/Environment/AmbientTrafficDirector.cs:23), [player reservation](../../QuestFlight/Assets/Scripts/Environment/AmbientTrafficDirector.cs:129), [blocked traffic](../../QuestFlight/Assets/Scripts/Environment/AmbientTrafficDirector.cs:245).

Change: distinguish existing runway occupancy from a new reservation. Existing traffic must be able to clear safely before the new pair is authorized; if staging would obstruct it, keep the pair in a declared preparation state and provide an explicit retry/restage outcome. Show the actual holding reason and a live cockpit readiness cue. Do not claim waiting resolves unavailable traffic data or a reservation deadlock.

Acceptance: test each occupied/clear runway, lineup, takeoff, landing, rollout and opposing-exit case on both runway directions. Every legitimate hold either clears through normal simulation or provides an explicit actionable failure. No scenery/traffic disabling to force a pass.

**F05 — P1 / D: Landed and aborted instructions promise actions their page does not contain.**

The Landed cue promises debrief/crew preparation; the operation page exposes only Resume and Back in Landed/Aborted. Finishing is elsewhere, and reconnect is restricted to CrewRehook. [Terminal instructions](../../QuestFlight/Assets/Scripts/Flight/Soaring/SoaringOperationSession.cs:36), [menu choices](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Soaring.cs:58).

Change: provide terminal-state actions directly, with **Stop aircraft first** when still rolling, then **View summary**, **Prepare another ground launch**, **Home**. Distinguish successful landing from launch abort and safety failure. Restart creates an explicit fresh operation; it does not reuse stale check flags accidentally.

Acceptance: complete landing, pilot abort, traffic abort and preparation failure. Every screen offers a valid next step matching its text, without finding a hidden Home path.

**F06 — P0 / D: “Restart at runway” can produce an unlaunchable glider.**

The shared recovery restart places the SGS on a C172 route checkpoint, with propulsion off but without preparing a tug/rope/operation. The glider then cannot launch. Its operation page incorrectly describes the no-operation state as an airborne checkpoint. A separate operation safety hold can set time to zero without opening navigation; the free-flight operation cue is hidden when time is zero, so the immediate stopped-state explanation can disappear. [Recovery restart](../../QuestFlight/Assets/Scripts/Product/ProductMapBoundaryRecovery.cs:225), [no-operation message](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Soaring.cs:41), [operation hold](../../QuestFlight/Assets/Scripts/Flight/Backends/FlightDynamicsCoordinator.SoaringOperation.cs:59).

Change: replace glider recovery choices with explicit **Restart ground aerotow preparation**, **Restart this lesson/checkpoint**, or **Start airborne practice**, as applicable, plus Home. Each choice must use the existing correct native preparation path. Keep C172 runway restart aircraft-specific. Automatically present a dedicated safety-hold result page explaining what happened and which restart is available; a frozen simulation must not require the user to discover that Menu contains an explanation.

Acceptance: all SGS recovery choices result in either a valid stopped tow preparation or a labeled airborne/lesson checkpoint. Never a supposedly flyable untowed ground glider. Hard fault remains held until a verified new preparation succeeds.

**F07 — P0 / D: Menu opened over a lesson catalog, briefing or results loses the way back.**

`OpenMenu` clears routes and shows App for all Training states. `CanResume` rejects non-flying states other than Paused. Root Back deliberately leaves App open. A briefing therefore loses its briefing actions; a debrief loses its results/Continue action. “Restart” from the catalog is offered even though `BeginExercise` refuses Menu state. [Open/Back/Resume](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:352), [resume guard](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Routes.cs:96), [exercise guard](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.cs:401).

Change: preserve a typed caller context including lesson state, selected lesson, page, focus and pagination. Return to non-flight lesson content must close only the overlay, without resuming native time or resetting the exercise. Hide or disable restart with an accurate reason when no attempt exists.

Acceptance: Menu → settings/help → Back → return at Catalog, Briefing, Paused, Debrief and Blocked. The original content and choices return without starting/restarting/ending a lesson.

**F08 — P0 / D: Interrupting lesson preparation can trap the user.**

Training update returns while navigation is open. Menu can open during Preparing, but Resume rejects Preparing and `BeginExercise` also rejects it. Preparation cannot finish while the overlay blocks its update. Selecting the misleading Restart action may incidentally unblock it by closing the overlay before its restart handler refuses the state; that is not a truthful Continue path. [Training update/preparation](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.cs:445).

Change: make preparation an explicit pending transition with **Continue preparation** and **Cancel to briefing**. Preserve its fade/reset phase and pause ownership across Menu, app focus loss and controller interruption. A native failure changes it to an actionable error rather than an infinite preparing screen. Cancellation must clear pending preparation and fades without starting a flight.

Acceptance: interrupt before native reset, during settling and during fade-in, with Menu, focus loss and suspension. Continue finishes once; Cancel returns once; no black-screen or paused-state deadlock, duplicate reset, or accidental flight start.

**F09 — P0 / D: “Continue this flight manually” does not validate whether it is continuable.**

Every SGS debrief shows Continue, including unsuccessful outcomes. The coordinator checks only aircraft/mode/debrief, then unsuppresses input and sets time to one; it does not reject recovery hold, invalid/disabled native state, or an unsafe terminal outcome. [Continue](../../QuestFlight/Assets/Scripts/Product/ProductFlightModeCoordinator.cs:72), [results choices](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:942).

Change: define a continuable-outcome predicate used by display and execution. Permit verified valid continuation with an appropriate operation state; reject hard native faults, crash outcomes and held recovery. Provide restart/summary/Home alternatives and explain unavailable continuation. Preserve the native flight and input-release barrier when continuing.

Acceptance: successful preparation, tow release and glide results can continue when valid; hard landing, out-of-area hold and native failure cannot escape their hold by choosing Continue.

**F10 — P1 / D: Resuming assistance below its limit repeats the same pause, and control instructions disagree with ownership.**

Assistance pauses below 120 m AGL, but generic Resume restores Assisting and immediately pauses again. A completed demonstration also offers Resume demonstration even though its text says start fresh practice. In SGS guidance, `touchFlightControlsAvailable` is false, so the assigned airbrake thumbstick is unavailable despite the usual control assignment; the physical lever has a separate path. [Assistance](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.Soaring.cs:53), [Touch gate](../../QuestFlight/Assets/Scripts/Input/ProductControlInput.cs:552).

Change: distinguish completed demo, assistance-limit pause and ordinary pause. Offer **Take manual control**, **Start fresh practice**, or restart as applicable; do not advertise resume into a state that immediately rejects itself. Keep intentionally pilot-owned SGS airbrakes/release available through every advertised supported input method during assistance, or explicitly restrict the UI to the actual supported method. Define ownership per channel.

Acceptance: assistance altitude-limit and demo-completion screens have a working primary action; Touch thumbstick, physical lever and supported controller mapping agree with the displayed owner and instructions.

**F11 — P0 / D: The aerotow lesson tells the pilot to wait for a cue that its live instruction path does not show.**

The lesson says “Wait for the reference release cue.” Operation ReleaseReady text is used by free-flight presentation and the operation menu, but lesson presentation substitutes static stage text plus evaluator feedback. It does not present the live operation cue. [Aerotow asset](../../QuestFlight/Assets/Production/SoaringV1/Training/02_aerotow.asset:100), [lesson presentation](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.Soaring.cs:38).

Change: use one operation-status presentation for free flight and lessons, with priority for safety stop, separation, launch state, release availability, then routine exercise instruction. Show **Release area reached — check separation; release when ready** during the appropriate native condition. Keep this distinguishable from an automatic or mandatory release.

Acceptance: reach the release area through the actual lesson and see the cue without opening Menu or reading a debug phase. Leaving the area or disconnecting updates it truthfully.

**F12 — P1 / D: “Current step / lesson help” can show the wrong information.**

For SGS, `CurrentExplanation` returns the reference at `orientationIndex`, initially Airspeed, regardless of the current task. ExplainCurrent only changes prompt state. A pilot stuck on crew reconnect can receive airspeed reference text instead. For C172, the page also prefers the current cockpit-reference explanation rather than a complete task/remaining-condition view. [CurrentExplanation](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.cs:101), [Explanation page](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:900).

Change: show current task, exact instruction, current completion/progress conditions, what is missing, and the next available action. Offer **Show relevant control/instrument** separately. Map every stage to relevant reference(s), not the last orientation index. Keep help accessible when captions are off.

Acceptance: inspect help at every stage, including preparation, reconnect, launch, release, thermal exit and landing. It describes that stage and returns to the exact caller without restarting it.

**F13 — P1 / D: Previous instrument moves forward in the SGS guide.**

Previous uses the ten-entry C172 array length before modulo the eight-entry SGS count: `(index + 10 - 1) % 8` is effectively Next. [Previous handler](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Polish.cs:169).

Change: use the active aircraft's count for both directions and normalize the index on aircraft changes. Keep general instrument help and lesson orientation consistent.

Acceptance: visit every reference forward and backward for both aircraft, including wraparound. Next then Previous returns to the same reference.

**F14 — P0 / D: Turning “Narration captions” off hides operational instructions and urgent text.**

The setting controls the visibility of the entire instruction text object, including text-only SGS instructions and urgent cues. Urgency changes color but does not override caption visibility. [Instruction visibility](../../QuestFlight/Assets/Scripts/Product/ProductUiInstruction.cs:30), [SGS presentation](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.Soaring.cs:74).

Change: separate narration captions, optional routine coaching and operational/safety status. Never hide current ownership, required next action, launch hold, disconnected rope, release-area cue or stopped/faulted state through a narration preference. Give the setting a precise description of what it affects.

Acceptance: complete the ground workflow with captions off and voice muted. The user still receives every required operation cue and can find complete lesson instructions. Urgent stop information remains visible at every display setting.

**F15 — P1 / D: “Try without prompts” still displays routine instructions and can award the corresponding completion flag.**

SGS `WithoutPrompts` removes evaluator feedback, not `Stage.instruction`. Shared presentation continues to show the stage instruction, and initial preparation speaks the stage narration when present. Independent completion is nevertheless derived from the WithoutPrompts flag. [Presentation](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.Soaring.cs:38), [initial narration](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.cs:610), [result classification](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.cs:896).

Change: define the option as **Practice without routine coaching**. Hide routine instructional prompts/narration while preserving the objective and operational/safety information. Explicit help requests must update the attempt's coaching-use status consistently. Results describe what was actually enabled; none imply real training credit.

Acceptance: compare guided and unprompted runs at first stage, stage changes and repeats. Routine prompts follow the selected mode, essential cues remain, and result labels reflect assistance/help actually used.

**F16 — P0 / D: Irreversible flight events can leave lesson goals impossible, and several instructions do not match evaluator conditions.**

Releasing before Tow position completes leaves that stage requiring `Line.state == Taut`, with no airborne reconnection possible. Landing before a preceding airborne stage completes likewise leaves an airborne predicate pending. Timeout is then the only lesson-driven result. Additional mismatches: release success does not require the promised release-area condition; the slip step says close airbrakes/recover but scores the slip without either; thermal gain can pass without remaining in/circling the source; outward ridge turn accumulates absolute heading changes rather than a completed reversal. [SGS evaluator](../../QuestFlight/Assets/Scripts/Product/Training/SoaringTrainingProgram.cs:56).

Change: give each stage explicit entry, success, failure and no-longer-achievable outcomes. Separate release/landing events from continuous dwell conditions. Immediately explain an early separation or missed approach segment and offer a valid manual continuation, explicit retry checkpoint or end, as appropriate. Align evaluator predicates and displayed simulator exercise wording; do not widen tolerances merely to obtain a pass. Use event history where the instructions require a sequence.

Acceptance: early/late release, rope break, early touchdown, missed stage window, thermal energy outside the source, partial ridge reversals and slip-with-brakes-open produce truthful results rather than a false pass or impossible pending objective. Normal complete lesson journeys still pass.

**F17 — P1 / D/U: SGS spatial instructions have no matching navigation references.**

Thermal/ridge/circuit lessons refer to a source, inside downwind and distances west of “the reference,” but every SGS stage has `targetEnu == 0`; the shared map draws lesson targets only for nonzero targets, and the coach hides all markers for SGS. Instrument-reference actions likewise do not highlight SGS controls. [Map target](../../QuestFlight/Assets/Scripts/Product/ProductFlightExperience.Review.cs:120), [SGS coach exclusion](../../QuestFlight/Assets/Scripts/Product/Training/TrainingCoach.cs:80).

Change: author simulator reference areas and route/target information from the same definitions used by the evaluator: named reference origin, runway/direction, thermal practice area, ridge boundary/open side, and circuit regions. Show these on the existing map; add optional guided-mode markers only where budgets permit. Provide real SGS cockpit highlights or label the page honestly as a text reference. Distinguish field-relative height, pressure altitude and terrain clearance in instructions.

Acceptance: every location-dependent instruction can be located from the visible map/reference without source coordinates. Guided landmarks agree with evaluator volumes. Unprompted mode hides optional aids consistently. No claim that these are operator-approved real procedures.

**F18 — P1 / D: Bindings and checklist text can prescribe the wrong or unavailable control.**

C172 taxi/takeoff text hardcodes the right thumbstick although users can select left steering; `MapInstruction` exists but has no callers. Brake instructions can contradict combined-brakes settings. The C172 reference/checklist tells users to verify fuel/ignition positions while the controller diagram calls these selectors inactive. SGS diagnostic binding summaries still describe trim/thumbstick and toe-brake assignments as if powered. [Binding helper](../../QuestFlight/Assets/Scripts/Product/ProductComfortPreferences.cs:19), [taxi instructions](../../QuestFlight/Assets/Production/Training/01_taxi.asset:81), [checklist](../../QuestFlight/Assets/Scripts/Product/ProductFlightExperience.Finish.cs:65).

Change: resolve controller/hand/brake instructions through an aircraft/input capability model in both text and narration. Replace hardcoded handedness with “assigned steering stick” plus the actual hand. Mark modeled observations separately from interactive actions, and explicitly say when a prepared checkpoint supplies an inactive control's setting. Do not require unavailable controls as a way to advance.

Acceptance: repeat control/checklist journeys under standard/left-steering/reversed/combined presets and each supported input device. No instruction points at a different mapping or requires a noninteractive selector.

**F19 — P0 / C; P1 / D: Input handover can leave the aircraft moving while the user's controls remain disarmed.**

Resume restores time immediately, then requires release of buttons, both sticks, triggers and grips. The explicit “release menu inputs” prompt is shown only in free flight. Training can continue displaying its lesson while input remains locked; airborne starts/clearance also need a clear handover boundary. Holding a normal cockpit grip is enough to delay arming. [Release gate](../../QuestFlight/Assets/Scripts/Input/ProductControlInput.cs:474), [Resume](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:473), [handover prompt](../../QuestFlight/Assets/Scripts/Product/Training/ProductTrainingController.Soaring.cs:62).

Change: use one visible **Release inputs to take control** handover state for free flight, training, demo handback and launch. Apply the same focus and required-controller readiness policy to initial starts and Resume. State which inputs remain held. Keep the manual transition paused until the required neutral edge is observed, then resume exactly once. Preserve lever positions and the one-event tow-release latch. Reacquire grip explicitly. Do not discard simulation time or spoof tracking/focus.

Acceptance: hold A, trigger, grip and each stick through Resume/start/handback; no hidden control lock, unintended release, launch acceleration or lesson timer advancement occurs before the acknowledged handover. Controller loss and app focus loss produce the same recoverable state.

**F20 — P1 / D/U: Quick setup invites actions that the modal menu disables and cannot distinguish skipped from completed setup.**

The modal interaction scope disables cockpit interactables. SGS guide steps still say grip/move the stick and trim, but the SGS guide has no movement verification. C172 checks observe controller motions separately rather than actual cockpit input. Live input preview shows raw axes while the actual lever/cockpit controls remain paused; its text still suggests grabbing the trim lever. Back at guide step zero calls Finish; Skip and Finish set the same seen flag. The first-use flag is loaded but never launches a guide. [Guide](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Polish.cs:408), [input preview](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Polish.cs:302), [interaction scope](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Routes.cs:24), [guide finish](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:408).

Change: distinguish a paused **Read controls guide**, **Raw input preview**, and **Practice controls**. Raw preview must label measured inputs versus frozen aircraft outputs and avoid asking for disabled cockpit interaction. A practice button closes the modal into a defined stopped-control exercise, with real input feedback and a clear return action. Avoid interpreting practice/preview trigger motions as unintended menu selections. Keep Skip, Exit and Completed distinct; Back at the first step returns to the caller without claiming completion. Offer first-use setup visibly and non-obstructively rather than leaving an unused automatic-guide flag.

Acceptance: every “move/grab” instruction either has an active input exercise or is clearly marked explanation only. Back/Skip never falsely records completed checks; setup can always be revisited.

**F21 — P1 / U; D for draft cancellation: Seat adjustment is unnecessarily hidden during a flight and Menu discards its draft.**

Aircraft hubs have Seat setup, but the flight pause menu requires the detour through Settings → Quick start → seat step. `OpenMenu` cancels an open calibration and clears the route even if Menu was pressed accidentally. [Calibration/menu](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:352), [pause choices](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:800).

Change: expose **Seat position** directly from the pause menu and Help. Maintain a draft with explicit **Save**, **Discard changes**, **Continue editing** when leaving a modified draft. Labels must distinguish seat offset from XR-runtime recenter. Return to the exact flight/lesson context and preserve pause. Do not write the tracked camera or move aircraft around the seat.

Acceptance: adjust during ground launch, tow pause, airborne lesson and briefing; save/cancel/Menu/back/focus loss produce the declared draft outcome and exact caller. Native aircraft and XR tracking ownership remain unchanged.

**F22 — P1 / D: Visible actions can report success even when their handler refuses them.**

Dispatch assumes success unless a handler calls Reject. Several commands return void or simply refresh on failure; demo rejoin/return is one example. A restart offered in a non-restartable lesson state is another. Confirm clears its pending action before executing; failure can leave an active confirmation page with no pending action. [Dispatch/confirmation](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Routes.cs:178), [demo menu handlers](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.cs:632).

Change: return an explicit result from every command: completed, pending, rejected with reason. Use the same availability predicate for rendering and execution, rechecked at execution. Only log success after the expected state is reached. A failed confirmation retains a valid retry or returns to its source with a reason; never show a confirm button for an empty pending action. Check readiness before changing control ownership.

Acceptance: intentionally fail preparation, demo rejoin, seat save, history save, lesson restart and native transition. Each tells the user what failed and offers a real next action; logs and visible state agree.

**F23 — P1 / D/U: Destructive exits and confirmations are inconsistent, and confirmation names can describe the wrong activity.**

Restart/activities receive confirmation, but the paused lesson's **Lesson list** can discard the attempt directly. Generic action 64 is titled “circuit practice” even when the SGS button launches Ridge soaring. Finish summary uses “Replace this session?” despite ending rather than replacing. [Confirmation list/title](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Routes.cs:211), [activities](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Polish.cs:368).

Change: classify commands by effect, not numerical ID. Confirm abandoning an active attempt consistently; simple Back through help must never abandon it. Store the actual selected aircraft/activity/lesson/runway with the pending command. Use **End**, **Restart**, or **Replace** with the exact selected name, current-attempt consequence and saved-progress behavior. Cancel restores page/focus and original native state.

Acceptance: test lesson-list exit, Back, Home, restart, fresh flight, recommended lesson and summary. Confirmation text always matches the resulting destination; Cancel causes no reset, assistance change or progress loss.

**F24 — P0 / C, established guard gap: Native startup failure can strand Home without its Retry action.**

The mode coordinator returns early while navigation is open, before its native-initialization error check. Navigation opens Home at startup. Home exposes Retry only when the mode is Blocked, while flight-selection guards merely say preparing/unavailable. A failed native startup can therefore remain Selection with disabled flight buttons and no Retry. [Mode update](../../QuestFlight/Assets/Scripts/Product/ProductFlightModeCoordinator.cs:192), [Home rendering](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Routes.cs:296).

Change: make preparation status/error observation independent of whether a menu is open. Show explicit Preparing, Ready, or Failed with the actual failure reason and **Retry preparation**, **Help/report**, **Settings**. Returning Home must not clear a persistent native failure or claim readiness. Retry remains fail-closed through native JSBSim.

Acceptance: inject failed backend/data/presentation preparation on cold start and aircraft switch with Home open. The failure becomes visible, retry is reachable and either genuinely succeeds or remains an explained failure.

**F25 — P2 / D/U: Some settings advertise an action at a limit but appear unresponsive.**

A/select increases clamped settings only; at maximum it does nothing. Voice/aircraft volume zero can be reached, but restoring audio with the same select gesture at other limits is not self-evident. Numeric settings depend on lateral stick movement despite point-and-trigger being advertised as a full menu interaction method. Control hints also retain powered-aircraft wording in some SGS contexts. [Setting adjustment](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Settings.cs:42).

Change: offer explicit decrement/increment controls (or a focused adjustment row with equivalent pointer targets), current value, and minimum/maximum feedback. Provide a direct mute/unmute action if audio is muted. Use aircraft-specific hints. Keep current saved values, limits and performance profile; do not reintroduce experimental rendering.

Acceptance: every setting can be increased, decreased and reset using pointer alone and buttons/stick alone. Endpoints give clear feedback. No setting silently changes unrelated control ownership or flight state.

**F26 — P1 / D: SGS summaries can recommend powered-aircraft procedures and speeds.**

The shared review calculates advice without an aircraft branch: taxi/power advice, 65 kt approach advice and powered go-around wording can appear in an SGS record. Correct aircraft-specific recommended lesson IDs do not fix the displayed advice. [Review policy](../../QuestFlight/Assets/Scripts/Product/ProductFlightExperience.Review.cs:76).

Change: choose review rules and wording by the record's aircraft/program and exercise, not the currently selected aircraft. SGS review uses measured observations and supported unpowered practice guidance; no powered escape or C172 approach target. Preserve old records but label legacy advice where its origin is ambiguous rather than silently treating it as validated SGS instruction.

Acceptance: short SGS observation, successful landing, off-runway touchdown, unusual approach and post-thermal climb records never prescribe engine power or a powered go-around. C172 review remains appropriate to its own lessons.

**F27 — P1 / U: Summary does not offer a repeat of the activity just flown.**

“Fly again · free flight” always starts generic free flight, so a ground aerotow is replaced with airborne practice. The qualifier is present, but there is no equivalent repeat-ground-launch choice. The pilot must return to Home and reconstruct the launch selection. [Summary](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Polish.cs:388).

Change: offer **Repeat this activity** only when its aircraft/start/lesson can be reproduced, plus explicitly labeled **New free flight** or **Airborne practice**. Store a session recipe for that purpose; do not infer a ground/airborne recipe from an old record lacking that information. As part of this extension, bind summary/history actions, maps and recommendations to their selected record ID rather than relying on a shared mutable history cursor. The audit does not claim a currently reachable summary/history record mismatch.

Acceptance: finish ground tow, airborne practice, lesson and demo; browse older history; return to summary and invoke each action. The preview, map, recommendation and launched activity refer to the same record, or state why replay is unavailable.

**F28 — P1 / D/U: Navigation context and naming are inconsistent across Menu, Back and nested pages.**

Menu always reopens the root and clears the stack, unlike ordinary Back. Map Back captions use a small fixed set rather than the actual caller. Several controls for disabled actions remain focusable with grey text but no persistent explanation until selected/focused. No-operation soaring pages offer only Back even when their text sends users to Home. [Route stack](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Routes.cs:61), [Map caption](../../QuestFlight/Assets/Scripts/Product/ProductAppNavigation.Polish.cs:354).

Change: document and implement one rule: Menu opens pause/context without destroying the return route; Back pops one level; a root pause requires explicit Resume; Home ends/replaces only through the appropriate confirmation. Show a short breadcrumb and dynamic **Back to [caller]**. Explain relevant disabled actions inline with a real recovery link, and omit irrelevant actions. No-operation flight status must say whether the glider is actually airborne, grounded or failed, with suitable onward choices.

Acceptance: crawl all contexts below, including repeated Menu presses, nested Help/Settings/Map and input switching. Every reachable nonterminal screen has a truthful Back/continue/end route; no caller gets an unrelated label or destructive fallback.

**F29 — P2 / D/U: Issue reporting lacks the menu state needed to diagnose these failures.**

Reports include mode/objective and aircraft state, but not current navigation route, focused action, last rejection, pending transition, physical tow state or operation prerequisites; non-training SGS report phase is sourced from the demo pilot. [Issue report](../../QuestFlight/Assets/Scripts/Product/ProductFlightExperience.cs:374).

Change: include a bounded recent menu-action trace, actual aircraft/program/operation phase, page/return context, rejected condition, input owner and pending transition in local reports. Keep the existing local-only behavior and report ID. Report save failure must be explicit. Do not upload automatically.

Acceptance: record each principal failure and verify the saved local report identifies the actual screen, requested action, reason and rope/native state without requiring the user to reconstruct the journey.

**F30 — P0 acceptance gap: Existing tests do not prove instruction-led workflow completion.**

Change: add the journey and graph checks below using the real dispatch path, authored scene and native authority. Do not satisfy menu acceptance by setting operation phases, control-check bits, completion flags or lesson states directly. Fault injection is permitted for named negative cases, clearly separate from normal journey evidence. Keep lower-level native/evaluator tests as supporting evidence.

Acceptance: every issue above has a failing reproduction before its fix and a passing check afterward; every journey reaches its declared outcome using exposed actions and supported control inputs. Report software/visual/Android/hardware results separately. A rendered button or a SUCCESS log is never the sole pass condition.

**Complete route/context coverage to implement and verify**

| Existing page(s) / hosted view | Entry and required return/continuation | Fixes |
|---|---|---|
| Home | Cold start, end, cancelled preparation; aircraft/help/settings/history/retry | F24, F28 |
| Aircraft, C172Setup, GliderSetup | Home/lesson chooser; preserve selected intent and return | F07, F23, F28 |
| Soaring | SGS hub, live pause, training pause; phase-correct next action | F01–F06, F11, F19 |
| None / lesson Menu | Aircraft lessons, cancelled briefing; pagination, Home | F07, F23 |
| None / Briefing | Any of 13 lessons; references/practice/demo where supported/no-coaching/back | F07, F12, F15 |
| None / Preparing | Start/retry/transition; continue/cancel/failure | F08, F19, F24 |
| None / Practice, Demonstration, Assisting | Live native session; pause/help/valid control transfer | F09–F11, F14–F19 |
| None / Paused, Debrief, Blocked | Automatic or manual stop; correct resume/results/retry/exit | F06–F10, F22–F24 |
| App | Menu/focus/controller pause; exact caller or explicit Resume | F07–F10, F19, F21, F28 |
| ConfirmMain, ConfirmAction | End/restart/replace; exact action or lossless Cancel | F22, F23, F28 |
| Explanation, Orientation | Current lesson/help/briefing; correct task and reference navigation | F12, F13, F17 |
| Help, Tools | Home/pause; topic and actual caller | F18, F21, F28 |
| Settings, Graphics, Comfort, Captions | Home or flight; settings usable with either input method | F14, F25, F28 |
| Controls, Bindings, InputPreview, ControlDiagram | Settings/help; accurate aircraft bindings, safe preview and return | F10, F18–F20, F25 |
| QuickStart, Calibration | Home/hub/help/pause; meaningful completion/draft behavior | F20, F21, F28 |
| Guest | Settings; Home-only switch, accurate result retention, cancel | F23, F28; preserve existing owner isolation |
| Briefing (local), Map, Kneeboard, Checklist, InstrumentHelp | Help/tools/history/summary; accurate reference and caller | F01, F12, F13, F17, F18, F27, F28 |
| Activities | C172 hub/local briefing/tools; exact activity and confirmation | F16, F23 |
| History, Summary | Home/tools/completion; correct record, map, recommendation, retry-save | F26, F27 |
| Report | Help/tools/error; local result and exact caller | F22, F29 |

Cross each applicable page with C172/SGS, selection/ground/airborne, training state, valid/failed native preparation, focus/tracking loss, pending transitions and guest/owner state. Test supported contexts rather than forcing nonsensical page/aircraft combinations. Enumerate the legal transitions and command prerequisites in one shared description, then validate rendering and dispatch against it. Retain existing action IDs where appropriate; compatibility aliases must not create a second navigation system.

**Lesson-specific work and end-to-end outcomes**

| Lesson | Required correction/verification |
|---|---|
| SGS preparation | Visible per-check progress, physical release/disconnected state, reconnect, close brakes, truthful completion and valid manual continuation |
| SGS normal aerotow | Correct ordering: checks → reconnect → close brakes/center → clearance/start; live release cue; early separation outcome; no wait for an impossible attached state |
| SGS glide/energy | Active mapping and trim help; explicit airbrake closure and slip/recovery sequence; truthful demonstration/assistance controls |
| SGS thermal | Locate the defined source; explain measured energy condition; distinguish centering/gain/exit; a valid end or manual continuation |
| SGS ridge | Locate windward/open side; score a real outward reversal; explicit exit region/clearance feedback; continuation/failed-margin outcome |
| SGS circuit/landing | Map origin, downwind/base/final regions and height references; early touchdown/missed gate outcome; rollout/stop, summary or new launch |
| SGS ground abort | Checks/reconnect/clearance ordering; immediately accessible low-speed abort; stopping progress; stopped result and restart preparation |
| C172 cockpit/taxi | Dynamic steering/brake instructions, actual controls vs prepared inactive systems, stop target, interruption and retry |
| C172 first flight | All reference pages and current-step help; demonstration completion, handback and no-coaching behavior |
| C172 climbs/descents/trim | Binding-aware trim instruction, exact current step, pause/resume and assistance outcome |
| C172 takeoff | Binding-aware rudder; declared prepared setup; roll/rotation/departure instructions and gate feedback; failure/retry |
| C172 pattern/approach | Map targets and decision outcomes; correct confirmation name; approach completion vs go-around remains explicit |
| C172 landing/go-around | Both declared success branches, current phase help, interrupted approach, failed touchdown and valid retry |

Do not alter aircraft physics just to make a menu test pass. If a correct instruction-led scenario exposes a physics defect, preserve the failure and scope its dependent fix/evidence explicitly. Independent route and content work continues.

**Implementation order after approval**

1. **Reproductions and action contract:** capture the reported release sequence plus F02/F07/F08/F11/F14/F24; add authoritative command results, availability and caller-context handling (F07–F09, F19, F22–F24, F28). This creates a dependable base for all other menu fixes.
2. **Complete launch/recovery lifecycle:** implement F01–F06 and terminal outcomes. Reconcile the existing operation/tug/rope owners; do not create a parallel launch system. Validate both runway directions and traffic-negative cases before marking launch ready.
3. **Lesson instruction contract:** implement F10–F18, especially cue visibility and impossible-goal outcomes. Update both serialized lesson assets and the authoring code that regenerates them, so a rebuild cannot restore obsolete text or conditions. Regenerate affected narration/caption text where a spoken instruction is now wrong.
4. **Setup, references, review and consistency:** implement F20/F21/F25–F29, using existing panels and authored markers. Preserve calibration, owner/guest progress and history migrations; apply aircraft-specific content throughout.
5. **Integrated verification and candidate:** run the graph/journey tests, relevant existing regression tests, native scenarios, inspected stereo simulator captures and standard Visual QA; build one integrated Android ARM64/Vulkan/OpenXR candidate. Preserve exact APK/source/model-payload hashes. Install only within the then-current user authorization. Physical Quest acceptance remains separately reported.

No scenery/render-quality reduction, budget increase, synthetic presence, Guardian bypass, camera manipulation or alternative flight authority is part of this plan. Reuse serialized UI objects; any additional presentation asset must stay within the actual frozen budgets and receive source/license records where required. No old remote pin replaces local HEAD, and no user files or rollback assets are removed.

**Acceptance suite and definition of done**

The suite must execute real menu actions using button/stick and Meta pointer paths, with normal release edges. Native scenarios may supply supported control-input trajectories, but must not bypass menu-owned reconnect, launch or lesson transitions. Snapshot before/after page, aircraft/program, native session/reset version, operation/tow state, input ownership, pause owner, pending action, and progress outcome. Assert the intended state transition, not merely action dispatch.

| Journey group | Required cases and pass criteria |
|---|---|
| Ground-to-landing journey | Both runway references: Home → SGS → ground checks → physical release → reconnect → ready → launch → tow → release → unpowered flight → landing/stop → summary → next launch. Displayed instructions alone identify every next action. |
| Incorrect-order recovery | Release before/after checks, after reconnect, after clearance and during rollout; repeat controls; open brakes after readiness; skip/miss a check. Every case either recovers explicitly or ends truthfully. |
| Traffic/launch faults | Occupied parallel strip, lineup ownership conflict, traffic arrival after clearance, pair/tug fault, weak-link break, slack timeout. No false clear and no unexplained endless wait. |
| All 13 lessons | Briefing → optional references → guided practice → complete result; no-coaching variant; supported demo; retry; abandon/cancel. Validate each authored stage and both C172 landing/go-around branches. |
| Lesson interruptions | Menu/settings/help/calibration and focus/controller interruption at Catalog, Briefing, every Preparing phase, Practice, Assisting, Paused and Debrief. Exact caller restored; pending work advances once. |
| Unrecoverable goals | Early release/landing, missed circuit window, invalid native state, unsafe touchdown, leaving supported area. Immediate truthful result with valid continuation/restart/end options. |
| Input handover | Hold each select/grip/trigger/stick through transitions; switch pointer/buttons; press Menu repeatedly; wake/reconnect controllers. No menu input leaks into flight and no disarmed manual flight silently advances. |
| Help/settings | Forward/back through both aircraft guides; correct current-stage help; every setting changed both ways with pointer and stick; captions off/mute/maximum text/contrast. Essential status stays visible. |
| Drafts and confirmations | Seat save failure/cancel/draft exit; every destructive command confirm/cancel/failure. No wrong activity title, empty pending confirmation or silent replacement. |
| History/guest/report | Empty/full/legacy history, older record vs summary, replay recipe, aircraft-specific advice, save failure/retry, guest switch, local report. No owner-progress mixing and no record/action mismatch. |
| Presentation | Inspected left/right simulator captures at all changed screens and longest/error text, plus dynamic transition inspection. Button labels/colliders visible, focus clear, text legible, no cockpit/menu interaction overlap. |
| Android/Quest | Final build and manifest checks; installed version/hash if authorized; worn/focused headset smoke journey and separate real timing/thermal measurement where available. Software/simulator evidence is never substituted for Quest acceptance. |

Completion means every ledger item has an implementation disposition and evidence, every legal route has a valid onward/return/end path, every instruction names a reachable action in the current state, and no blocked prerequisite is presented as success. Where hardware behavior cannot be exercised autonomously, mark that specific acceptance item unmeasured; do not replace it with a general “all menus complete” claim.

The eventual short human check is: verify the candidate version; complete SGS ground checks/reconnect/takeoff without developer guidance; pause/resume and return from a lesson briefing/results; use Previous in the SGS instrument guide and test trim/brake/release help; stop/land and start another ground launch. This is a final usability check, not a substitute for the software journeys above.
