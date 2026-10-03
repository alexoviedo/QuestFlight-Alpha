# QuestFlight demo-readiness plan — complete flight, clean audio and contextual narration

Status: **Integrated Android candidate built; Kokoro Michael (`am_michael`) selected by the user after audition.** The 103-clip candidate is authored into the product with local, prerecorded playback. Seven complete native flight scenarios pass. Physical Quest acceptance and the open checks below remain pending. The user explicitly requested build-only delivery; do not install or launch on Quest until requested. The installed September 21 lesson rewrite still awaits the user's later headset review.

## Implementation evidence — September 21

- Android release build succeeded September 22 UTC (September 21 local). Candidate: `demo_completion_20260921/quest_build/QuestFlight-Michael-demo-candidate.apk`, 1,799,434,382 bytes; SHA-256 `49e40fabc3b0b1ff8164164377cf909f385f60181a1c8b79293c69f6d58f6028`. Build validation verified all 1,912 authored scenery payload files; native JSBSim remains required. Source identity/archive and package manifest are retained beside the candidate. **Not installed**, per user instruction.

- Generated and integrated 93 lesson recordings and 10 demo recordings with Michael, at 24 kHz mono. Preserved existing clip GUIDs; staged generation, pinned model revision and source/license records are retained. Transcript checks are an aid, not listening acceptance. Clip manifests explicitly retain pending listening status.
- Replaced the idle loop's 600–612-second source excerpt with 490–502 seconds from the same documented recording. A browser audition is available. Absence of speech is **not accepted yet**: transcription missed the reported faint voices in the previous clip, so listening is required.
- Added demo-only native control guidance, flare, ground-contact rollout, authored taxi exit and stopped completion. Training guidance remains separate. Added explicit manual takeover, safe-corridor rejoin, early return and runway reservation/go-around handling.
- Seven full native scenarios completed landing and runway exit: ordinary wind, two wind timing offsets, runway 26 with crosswind, occupied-runway go-around, banked manual takeover/rejoin, and early return. Last pre-contact descent rates were approximately 143–155 ft/min. Traces and summaries are in `demo_completion_20260921/full_flight_*`. These are isolated native scenarios, not a Quest runtime or thermal acceptance claim.
- Added contextual tour captions/recordings using the existing instructor source; separate voice/aircraft volume and speech ducking leave warning sounds unducked. Tour facts require sustained pilot-facing visibility and terrain line-of-sight checks. Scene geometry and tracked-camera ownership are unchanged.
- Inspected six Editor presentation captures: pause-menu labels and tour text fit in both eyes. The report remains **FAIL** for scene-wide draw/triangle estimates above its configured limits. Editor captures also contain exterior/stereo-shader artifacts; they do not establish runtime visual acceptance. Subsequent Meta XR Simulator live departure captures were inspected: cockpit and scene render consistently in both eyes, and the log records the tour introduction. The simulator report remains blocked for missing replay recordings and fails its immutable-terrain check across the showroom-to-flight visibility transition. No new full-runtime or Quest acceptance is claimed.

Open acceptance: compressed audio listening and intelligibility, actual runtime narration relevance/interruption, tracking-loss/pause behavior, repeated or unsupported handbacks, route/local-procedure review, and physical Quest frame/thermal acceptance. Do not describe the implementation as all-weather autoland, an official airport route, or a fully accepted demonstration until those gates are resolved. The seven native traces do not cover every scenario listed below.


## Delivery decision

Deliver one integrated candidate with clean engine audio, reliable native-physics landing/exit/stop, explicit user/demo handoff, a feasible scenic tour and contextual Kokoro narration. Kokoro runs only on the development computer to create packaged recordings. No speech-service account, runtime internet connection or model on Quest is required. Use the same instructor voice for the existing lessons and the new tour.

The fastest sequence remains: fix source audio and landing first; establish takeover/rejoin and route behavior; finish contextual narration and mix; install one integrated headset candidate. A small Kokoro audition can happen during development before batch generation. No extra headset session is needed just to choose a voice. Secondary guest/onboarding polish remains explicitly deferred and is not a prerequisite for this milestone.

## 1. Judgment on the previous plan

The previous plan would improve presentation, but its priorities were wrong for a credible demonstration:

- It placed premium narration, guest sessions and interaction details ahead of proving that the demo actually finishes its flight.
- Its suggestion to pause at a presentation endpoint could avoid exposing the failed landing. That does not satisfy a complete flight demonstration.
- It treated aircraft audio mainly as a mixing problem without auditing the source for voices and other contamination.
- It proposed a short tour without accounting for climb/descent performance, the actual route length or the time needed for a credible landing.
- It mentioned control ownership but did not specify how manual flight changes route progress, autopilot recovery, aircraft configuration or the meaning of narration.
- It spread effort across guest profiles, lesson recap, onboarding and cosmetic details. Those remain useful but are subordinate to fixing the product's central experience.

The revised deliverable is one convincing complete experience: **clean cockpit sound; native-physics takeoff and scenic flight; deliberate user takeover and safe route rejoin; narration that follows what is actually happening; a competent landing, runway exit and stop.** Existing accepted cockpit/scenery/UI work remains the visual foundation.

This is a meaningful step toward a polished release. It is a flight-guidance and presentation milestone, not a small cosmetic patch. Licensing and distribution are outside this request.

## 2. Verified project findings

Source inspection was followed by one native approach diagnostic using the authored final and existing production guidance. No audio audition or final worn acceptance has yet been performed for this milestone. Distinguish the user's observed symptoms, verified source mechanisms, diagnostic evidence and proposed fixes.

| Finding | Evidence | Consequence |
| --- | --- | --- |
| The user hears faint voices at idle. | User report. The active source manifest identifies an excerpt from a real C172E cockpit recording; its idle segment is seconds 600–612. | Treat this as a release-blocking audio defect. Replace the contaminated material. |
| Idle preparation does not remove speech. | `tools/audio/prepare_c172_audio.py`: `loop_segment` selects the excerpt, blends a 0.75-second seam and peak-normalizes. `ProductionC172AudioController` makes the idle layer prominent at low RPM. | Loop editing and normalization cannot establish that source audio is clean. The excerpt/layer mix still needs an isolated listening check to pinpoint all contaminated clips. |
| The active demo already contains return, final and rollout waypoints. | `ProductionWorldRoot.prefab` references GUID `f3ef2c193b64693478841b149d4ec8d2`, the serialized `KBDU_ExtendedReservoirLessonV1.asset`. | Adding a landing waypoint alone is not a fix. |
| There is no dedicated demo flare/touchdown landing controller. | `ProductDemoPilot` has a takeoff state machine, but landing uses the generic `FlightLessonGuidanceController` plus segment flap/power/brake settings. The guidance aircraft state lacks ground contact and runway-relative landing geometry. | Add a scoped landing controller that consumes native state and produces controls through the existing input boundary. |
| Final geometry and allowed descent disagree. | Authored final: 5,700 to 5,300 ft MSL over 1,170 m; target speed 70 to 65 knots; descent cap 400 ft/min. | At zero wind, following that slope at those speeds requires about 690–740 ft/min. This is a calculated profile mismatch, not a replay of the exact reported crash. Rebuild a flyable approach consistent with local procedures and measured aircraft response. |
| Rollout and completion can be triggered while airborne. | `FlightLessonNavigator.AdvanceIfCaptured` advances by horizontal capture/end-plane crossing. Final/rollout have zero altitude tolerance and no weight-on-wheels condition. `ConfigureCommandForSegment` selects 0.10 throttle and braking for rollout. Completion also lacks confirmed stop/contact. | Landing phases and success must be driven by aircraft state, not proximity to a point. Airborne brake commands and premature completion are unacceptable. |
| A native checkpoint run reproduces an unsafe landing sequence. | `demo_completion_20260921/original_approach_summary.txt`: airborne rollout observed; last sampled precontact vertical speed about **−2,173 ft/min**; contact occurred. The diagnostic bypassed runtime crash recovery. | This is evidence of a failed approach, not an accepted landing because the aircraft eventually slowed. Preserve this trace and compare the corrected controller against measured touchdown, rollout and stop criteria. |
| Existing manual override is blended per axis. | `FlightLessonAssistanceMixer` gives more manual authority with input magnitude; neutral input returns guidance. Throttle ownership times out. | This is not a clear whole-aircraft “you fly / demo flies” contract. Preserve training behavior while introducing explicit ownership for the tour. |
| Suppression is not an adequate handoff API. | `ProductDemoPilot.SetGuidanceSuppressed` resets applied controls toward neutral/0.10 throttle and is used by recovery. | Do not reuse it blindly for smooth takeover/rejoin. Separate deliberate tour ownership from crash-recovery suppression. |
| Wind correction and terrain clearance need route-level treatment. | Generic guidance compares desired track with heading and applies cross-track correction; it does not explicitly solve a wind-corrected ground track. | Use the shared wind field, measured velocity and terrain/runway data for rejoin and landing. |
| Runway protection exists but the demo has no explicit approach reservation. | `AmbientTrafficDirector` protects the player's position/velocity and explicit training reservations. | Integrate demo approach/exit intent with existing traffic scheduling; do not disable all traffic. |
| Natural prerecorded narration is feasible. | Current lesson library: 93 clips, 17.11 minutes; Microsoft David Desktop; voice source has no mixer bus. | Replace voice offline and add one shared, prioritized presentation path. No runtime generative AI is necessary. |
| Current voice generation overwrites the entire library. | `scripts/generate_training_voice.ps1` merges `LessonTeachingContent.json` into `VoiceScripts.json`, then writes Microsoft David recordings directly to the production paths. | Preserve that content precedence, stage Kokoro output separately, and prevent the old generator from overwriting promoted clips. |
| Speech import settings need a scoped update. | Inspected `01_taxi_00.wav.meta` uses mono, compressed playback and an explicit 22,050 Hz override. Kokoro's documented output is 24,000 Hz. | Keep a 24 kHz mono master and author an appropriate speech importer preset after a compressed-playback check; preserve every existing GUID. |

These findings establish that the current sequence cannot deliver a credible landing in the traced scenario. The diagnostic is an isolated native approach, not a replay of every reported headset crash or a full-route acceptance. Inspect its CSV for phase/control timing while tuning; do not repeat the same baseline merely to gather more paperwork. No fixed landing or improved Kokoro recording has yet been accepted.

## 3. Priority A — clean aircraft audio

1. Solo idle, cruise, propeller and starter clips. Check the low-RPM blend and idle-to-taxi transition; determine whether contamination occurs in more than the reported idle excerpt.
2. Replace contaminated clips with genuinely speech-free piston-single interior recordings. First look for a clean section in the available source; if none exists, use a better recording. Do not spend the milestone trying to rescue bad source material with heavy filtering. Offline separation is a fallback only if its result has no voice residue or watery artifacts.
3. Match RPM character, loudness and tonal balance to the retained engine layers. Make seamless loops; preserve the existing RPM-driven mix and source count. Avoid a short obvious repeating pulse or audible pitch changes at layer crossovers.
4. Listen to the final Unity-compressed clips for several loops with narration off, at ordinary playback level and at a louder inspection level. Verify idle, taxi, run-up, cruise and return to idle.
5. Update source hashes, clip provenance and the authoring pipeline so regeneration cannot restore the bad excerpt. Preserve Unity GUIDs where clips are replaced.

Done: no audible voices, radio chatter, clicks, seam or obvious pumping in the idle bed and transitions. This is primarily offline asset work; it adds no rendering cost. Quieting the engine or covering it with narration is not acceptance.

## 4. Priority B — make the current flight land before extending the tour

### Establish the actual failure

Use the captured native baseline and existing JSBSim route/checkpoint tooling. Retain the approach profile, runway-relative position, true/indicated speed where available, groundspeed, wind, altitude above the runway surface, vertical speed, pitch/bank, flap/trim state, applied controls, weight-on-wheels and crash/recovery reason where applicable. Confirm that the current build and serialized route are the ones being exercised. Extend missing instrumentation only where it resolves a specific controller decision.

### Implement an explicit landing sequence

**Arrival/pattern → stabilized final → roundout/flare → touchdown confirmation → rollout → runway exit → stopped/completed.**

- Replace inconsistent final geometry with an achievable arrival, descent and speed/configuration schedule. Use the current C172P-derived model and configuration rather than generic universal C172 numbers.
- Reference the authored runway centerline, threshold, elevation and surface/contact geometry. Use ground track for path capture and airspeed for aerodynamic targets. Account for wind instead of treating heading as track.
- Retain native JSBSim as the sole authority. All guidance supplies bounded control commands; it never animates or teleports the aircraft/camera into a landing.
- Roundout and flare use height above the runway, descent trend, speed and remaining runway. Require credible alignment and landing configuration before committing.
- Confirm touchdown with contact state and short hysteresis. Handle bounce without declaring success or continuing to apply airborne brakes. Keep steering and rudder behavior appropriate to ground speed.
- Brake after confirmed contact and maintain the runway centerline. Reserve a known clear exit/holding path, stop at a verified location clear of the runway and then show completion. First prove controlled rollout internally; runway exit/stop is part of the final deliverable, not a full general airport taxi planner.
- Completion requires the correct surface/location, stable ground contact, near-zero groundspeed and no crash/recovery event. Merely passing the final waypoint never counts.
- Retain crash recovery for exceptional failures, but log it as a failed demonstration. An automatic restart, fade or hidden reset is not a successful landing.

### Go-around and traffic

A stabilized approach is central to credible landing behavior. FAA guidance emphasizes controlled path, airspeed and configuration; an unstable approach must not be forced onto the runway. [FAA guidance](https://www.faa.gov/newsroom/safety-briefing/stabilized-approach-and-landing).

Check stability at an authored gate appropriate to this C172 and local approach, and continue checking below it. Wrong alignment, excessive energy/sink, insufficient runway or occupied runway triggers a clear go-around. Maintain safe speed/configuration, climb, rejoin a reachable pattern and try again. Do not loop indefinitely: repeated inability to land reaches an explicit paused assistance/error state and fails demo acceptance.

Use the same runway-direction/wind logic as ambient traffic. Support the current ordinary shared weather envelope, including the appropriate runway 08/26 arrival; do not silently disable wind or land with an excessive tailwind. Outside the validated envelope, explain why automated landing is unavailable. The implementation scope must name and measure that envelope rather than promise all-weather autoland.

Boulder requires local treatment: pattern altitude 6,300 ft MSL, downwind south of Jay Road, glider awareness, and different 08/26 procedures. Runway 08 discourages extended western approaches and calls for a close, steeper approach. Do not impose a generic three-degree long straight-in or label the existing experimental route an official pattern. Overlay the revised path with the city's published geometry during route validation. [City procedures](https://bouldercolorado.gov/services/airport-traffic-pattern).

Done: consecutive unassisted complete flights land, exit and stop credibly in the supported conditions. An occupied runway produces a safe go-around followed by a landing when available.

## 5. Priority C — intentional control handoff and safe rejoin

Provide explicit actions in the existing pause menu: **Take control**, **Let the demo fly**, and **Return to airport**. Preserve thumbstick/A/Menu/Back mappings. Do not add an undiscoverable gesture or repurpose the rudder/trim sticks.

Use clear states: **Demo flying / Your controls / Demo stabilizing / Rejoining tour / Returning to airport / Landing / Paused / Complete**. Display ownership compactly and speak one short acknowledgment. This is a tour session state, not a change to the app's Free flight selection semantics.

### Taking control

- Pause menu selection transfers ownership on resume without repositioning the aircraft.
- The user owns normal flight controls as a coherent set; neutral yoke input must not cause the demo to steer back toward the route.
- Preserve current throttle, trim and configuration until the user changes them. Reconcile physical/virtual lever ownership so grabbing a control does not introduce a sudden jump.
- Freeze automatic route advancement and procedural tour narration. Position-aware point-of-interest eligibility can still be evaluated.
- Keep the existing warning/pause behavior. No hidden assist should fight the user's deliberate input.

### Giving control back

- Require intentional confirmation and released controls. Start from the actual current aircraft state and applied controls, not the former waypoint or a neutral-throttle reset.
- First stabilize airspeed/attitude/configuration as appropriate; then choose a reachable forward route segment or airport return corridor.
- Use a small authored network of safe corridors and rejoin gates, with terrain clearance, map bounds, bank radius, altitude and wind constraints. Choose reachable progress, not simply the Euclidean-nearest point. Never cut across a ridge to reach a waypoint.
- If handed back near the runway, determine whether a stabilized continuation is possible. Otherwise go around/re-enter; do not dive toward the old final point.
- On the ground, stop safely and resume only from a known feasible ground path; do not apply airborne cruise logic.
- If the user has created a state outside the supported recovery envelope, say so and pause with a clear restart/return choice. Do not promise recovery from an unrecoverable dive or disguise a teleport as pilot skill.
- Reset/seed guidance integrators deliberately and rate-limit control takeover. Handoff must never create competing flight authorities.

“Return to airport” skips optional remaining scenery and requests the normal arrival/landing sequence. It is an actual flight, not a teleport or instant completion. Main-menu → Free flight still creates a fresh flight, preserving the accepted reset behavior.

Done: repeated takeover, deviation and return do not cause control fighting, abrupt power/configuration jumps, route-state corruption or incoherent narration.

## 6. Priority D — a scenic route worth demonstrating

Extend only after the original route can land and hand back correctly. Build a tour with 3–5 recognizable highlights, not a long checklist of weak distant buildings.

Proposed backbone: **KBDU departure → Boulder Reservoir → foothill/Flatirons vista from safe eastern terrain → a city/campus viewing leg if the current scenery supports it → wind-appropriate KBDU arrival → landing/exit/stop.**

- The reservoir is already on the current route. Its water/recreation role supplies a simple factual narration topic. [City reservoir information](https://bouldercolorado.gov/locations/boulder-reservoir).
- The Flatirons offer a recognizable skyline and a brief explanation of the terrain/wind relationship. Remain in a corridor with measured clearance rather than adding a low mountain pass. [City Flatiron reference](https://bouldercolorado.gov/trail/1st2nd-flatiron).
- **Folsom Field/CU Boulder** is the preferred building/location candidate, subject to existing map coverage and in-game silhouette visibility. Its history can be sourced from the university, but its actual rendered recognizability is not yet established. [University reference](https://www.colorado.edu/coloradan/2018/04/24/then-and-now-folsom-field).
- Valmont or another already represented feature may replace the campus leg if it yields a clearer view with less route complexity. Do not silently add landmark reconstruction to the milestone. Record the selected POIs and why others were dropped.

Author altitude, turn radii, sightline side and duration against the actual native aircraft performance and terrain. Inspect normal seated forward/side-window views, not overhead cameras. Avoid placing featured objects directly below the aircraft. Respect local noise/glider/pattern geometry when returning.

Provisional duration target: roughly **10–15 minutes** for the extended uninterrupted tour, finalized from the feasible route. The earlier 4–6 minute goal is not a requirement; do not speed up or omit the landing to meet it. Provide Return to airport for shorter visits. Manual exploration can extend the duration.

Done: the selected highlights are recognizable from normal pilot views, the route is comfortable enough for the intended demonstration group, and it completes a normal landing without scripted resets. No claim that every visitor will tolerate VR flight equally.

## 7. Priority E — natural, context-aware narration

Use short authored recordings with a deterministic director. No runtime AI, online speech generation, or rigid soundtrack on a timer.

### Eligibility and interruption rules

Every cue has a type, prerequisites, optional direction variant, expiration, priority and played/interrupted state.

| Cue | Speak only when | On manual takeover/deviation |
| --- | --- | --- |
| Ownership acknowledgment | Ownership has actually changed. | Interrupt obsolete tour narration promptly. |
| Flight explanation | The corresponding actual maneuver/configuration is occurring under demo control. | Cancel claims such as “I am turning onto final.” |
| Landmark fact | Object is loaded, within a useful distance/angular size, and visible through a normal pilot window for long enough to be meaningful. | Defer/skip if no longer relevant. An optional fact may still play during stable manual sightseeing if these conditions hold. |
| Landing/go-around status | Actual landing controller enters the stated phase. | Speak only the relevant ownership/safety state; never pretend the demo is landing while the user flies. |
| Completion | Confirmed landed, clear of the runway and stopped. | Never fire from elapsed time or a passed waypoint. |

- Use geography, aircraft pose, approximate stereo view/window visibility and low-rate bounded occlusion checks. Do not infer visibility just from proximity or force a head turn. Read the tracked camera; never write it.
- Give visibility hysteresis so a momentary glance away does not chop a sentence. A material route change or takeover invalidates incompatible speech quickly with a short audio fade.
- Cancel expired queued clips; do not deliver a backlog of missed landmarks. Track whether facts were completed so returning to a place does not restart the tour repeatedly.
- After rejoin, say a short relevant line such as “I've got the controls. We'll rejoin the tour ahead,” then choose the next eligible cue. Do not resume halfway through an old sentence.
- Author left/right/ahead variants only where useful, selected using actual aircraft-relative bearing. If no variant is accurate, omit the direction or skip the cue.
- Leave substantial quiet intervals. Keep sightseeing out of takeoff, short final, unstable flight and go-around. A restrained flight explanation is more useful than continuous chatter.
- Use one speech owner across tour/training/system messages. Preserve captions, repeat and urgent priorities. A mode change cancels speech from the mode that ended.

Illustrative lines, not final route-timed copy:

- Before departure: “I'll fly the tour. You can take control from the pause menu whenever you're ready.”
- Reservoir, only while visible: “Boulder Reservoir is a recreation area and part of the city's water supply.”
- After takeover: “You have the controls.”
- After accepted handback: “I've got the controls. I'll settle the airplane before we rejoin.”
- Actual go-around: “This approach isn't settled. I'll climb away and try again.”
- Actual completion: “We're clear of the runway and stopped. You can try a lesson or take another flight.”

### Kokoro production decision

Use **Kokoro-82M v1.0 locally**, through the official Python package and English pronunciation pipeline. Pin the tested package/dependency versions, model revision/hash and voice asset hash. Use an isolated development environment and an external asset cache; download only the selected model/voices and required dependencies. Verify Windows phonemizer prerequisites during setup. CPU generation is acceptable; use an already compatible GPU installation if available, without turning this into a GPU/toolchain migration. The model provides 24 kHz audio. [Official implementation](https://github.com/hexgrad/kokoro), [model and usage](https://huggingface.co/hexgrad/Kokoro-82M).

This replaces ElevenLabs/Azure access as a dependency. There are no hosted per-generation charges for local generation; development compute, storage and review time still exist. Do not add Kokoro, Python, model weights or a speech service to the Android build.

### Voice and delivery

- Use **`am_michael`**, the American English male voice explicitly selected by the user after the Heart/Michael audition. Use one consistent voice for lessons and tour; no blending or cloning. Review intelligibility and aviation pronunciation in the resulting clips. [Voice guidance](https://huggingface.co/hexgrad/Kokoro-82M/blob/main/VOICES.md).
- The six excerpts cover a cockpit reference; heading/altitude/airspeed numbers; a short correction; takeover/handback; a Boulder landmark; and landing/go-around status. Review at matched loudness in isolation and against the clean engine bed. Provide the audition files for optional user listening without making another approval or headset session a prerequisite.
- Aim for a calm, concise instructor. Begin at default speed and adjust within a small range only if listening justifies it. Preserve the short, one-action teaching structure already authored; longer and more theatrical delivery is not better instruction.
- Kokoro's documentation warns about weak very short utterances and rushing in long ones. First try punctuation and a complete, concise sentence. If a short cue still fails, generate it with adjacent context and retain only the verified complete target sentence at a clean boundary. Do not pad a time-critical correction with unnecessary words, cut phonemes, or lengthen lessons to fit the model. Split long briefings at natural sentence boundaries, never mid-instruction. [Voice guidance](https://huggingface.co/hexgrad/Kokoro-82M/blob/main/VOICES.md).
- Kokoro is not an instruction-following voice actor with guaranteed emotional prompting. Direct delivery using copy, punctuation, supported pronunciation controls and pacing; do not feed stage directions that might be spoken aloud. If an isolated line remains poor after a bounded revision, flag and repair that line instead of repeatedly regenerating the whole library.

### Aviation pronunciation and authoritative copy

Keep `LessonTeachingContent.json` authoritative for the lesson/stage/reference/coaching entries it owns, and retain other existing entries from `VoiceScripts.json`. Add a tour cue manifest with stable IDs and state/visibility contracts. Do not author a separate competing set of lesson instructions in the audio script.

Store human-readable caption text separately from speech-only normalization and pronunciation overrides, linked by the same cue ID. Examples:

| Meaning | Spoken form |
| --- | --- |
| Heading 090 | “heading zero nine zero” |
| Runway 08 / 26 | “runway zero eight” / “runway two six” |
| Altitude 6,300 ft | “six thousand three hundred feet” |
| Airspeed 65 kt | “sixty-five knots” |
| C172 | “Cessna one seventy-two” when naming the airplane |
| KBDU | Prefer “Boulder Municipal Airport” in visitor-facing copy; spell the identifier only where needed. |

Use context-aware authored replacements, not global number substitutions: a heading, runway, speed and altitude need different readings. Check Boulder, Valmont, Flatirons, Folsom, altimeter, aileron, rudder and the simulator's button names. Use supported phoneme overrides only for words that need them. Inspect each final line for omitted/repeated words, wrong units or unintended directions. ASR can flag suspects but cannot replace listening or certify pronunciation.

### Reproducible generation and safe promotion

1. Resolve source copy and clip IDs using the existing content precedence. Fail on duplicate/invalid IDs, missing text or unreferenced required cues.
2. Generate to a staging directory outside Unity. Record caption/spoken-text hashes, pronunciation overrides, model/package/voice versions, speed, duration and output hash per clip. Cache by these inputs and regenerate only changed clips. Pinned inputs improve reproducibility; retain approved WAV hashes rather than promise cross-hardware bit-identical synthesis.
3. Keep **24 kHz mono WAV masters**. Trim only excess boundary silence, preserve natural pauses and avoid per-word splicing. Match perceived voice level across the library, leaving headroom and avoiding aggressive compression or peak normalization that amplifies tiny cues/noise unpredictably. A working authoring target is around −18 LUFS for sufficiently long speech with peaks below −1 dBTP; short cues use matched perceived level rather than a misleading loudness number. Final mix decisions depend on listening.
4. Review every generated clip once, with extra attention to the short corrections, aviation numbers and joined sentences. Cheap automated asset checks cover missing/empty files, clipping, invalid durations and manifest mismatches; they do not establish naturalness. Listening is the acceptance gate. Keep a reviewed/needs-fix record and repair only failing clips.
5. Promote the reviewed library together: replace the existing **93 lesson clips** in place, preserve `.meta` GUIDs and serialized references, and add the final tour/ownership/status clips. Count final coverage from the manifests rather than hard-code 93 as a permanent limit. Record source/license provenance for the generated binary assets as required by the repository.
6. Change the existing regeneration entry point so Kokoro is the intended production path. A legacy Windows-voice preview, if retained, must write to a separate destination; a build or authoring action must never silently replace Kokoro with David. A missing/stale required recording fails validation with its cue ID; a missing optional tour fact is skipped at runtime without blocking flight. Preserve captions and the last reviewed recordings during development.

### Runtime timing, mix and Quest delivery

- Keep speech prerecorded and local. Use **one serialized narrator source** with a shared arbiter for the tour and training, or route the existing source through that arbiter; do not add competing voices. Warnings have priority over coaching/status, which have priority over optional sightseeing. Repeated corrections get cooldowns and no accumulating queue. A control-ownership acknowledgment is brief and does not suppress an urgent safety warning.
- Use actual new clip lengths/playback state for caption visibility, repeat, interruption and instruction readiness. The inspected controller already uses `clip.length` for some live-cue lifetime, but audit the remaining timers. The flight/evaluation loop must continue independently; longer narration must neither freeze the aircraft nor make an objective complete early. Ordinary stage speech should finish when still relevant; genuinely obsolete or urgent content can interrupt it.
- Add a dedicated voice bus with smooth ducking of engine/wind only, independent voice/aircraft volume and existing captions/repeat behavior. Start with modest attenuation, roughly 3–5 dB, and a gentle release; tune once against idle and cruise. Do not duck warning tones or abruptly pump the engine on every word. No new reverberation or stereo widening is required for a clear nearby instructor.
- Author mono compressed speech imports at 24 kHz, then inspect decoded/compressed results. Use compressed playback/loading appropriate to the existing Unity path; preload only a bounded set of immediate status/correction cues and upcoming content. Avoid loading the full narration library as PCM or introducing first-use stalls during approach.
- Pause/resume, mode exit and invalidated cue cancellation must stop both the clip and its ducking/caption state coherently. Repeat restarts a still-relevant complete cue. No replay backlog after a manual diversion.
- Measure installed size, voice memory and first-play behavior. These are authoring settings and acceptance targets, not existing performance measurements. The engine source must be clean before the final mix is judged.

Done: one clear, consistent instructor across lessons and tour; correct aviation readings; no clipped words, stale statements or overlapping narration; speech remains intelligible over engine sound without hiding warnings. Voice generation and playback are separate: changing a sentence later requires regenerating that clip, not a cloud service or runtime AI.

## 8. Quest 3 costs and protected scope

- No new scenery population, extra render cameras, real-time reflections, bloom or rendering-pipeline changes.
- Preserve accepted Medium FFR, render scale, MSAA, cockpit art, marker design, menu mappings and native JSBSim authority.
- One active narrator source. Kokoro runs only during development; audio is packaged locally and compressed clips are not all decoded at startup. Current 17.11-minute lesson audio would be about **49 MB as 24 kHz mono 16-bit PCM** before compression (calculated, excluding tour clips); this is why the whole library must not be decoded at startup. Measure the final Android audio footprint instead of assuming that estimate is runtime memory use.
- Evaluate a small POI candidate set at a bounded low rate, initially about 5 Hz, with cheap geographic tests before bounded visibility queries. Do not scan every building or perform runtime vision/inference.
- Keep flight guidance at the cadence required by the existing native-control loop; narration scheduling frequency must not throttle control updates.
- Author reusable route graphs and runway data offline. Avoid unbounded runtime searches or allocations.
- Existing C172P physics coefficients remain unchanged. This work changes guidance and route behavior, which still requires native-flight evidence.
- The extended route may expose heavier scenery windows than the old reservoir route. Unchanged meshes do not imply unchanged cost. Measure the final route with active narration/UI on physical Quest; do not promise zero performance cost or use old mismatched High-FFR measurements.
- Preserve existing documented resource failures and target stable 72 rendered frames/s as the product comfort goal. Any measured bottleneck gets one focused investigation before more cosmetic additions.

## 9. Exact implementation order and minimum evidence

1. **Replace contaminated engine audio and establish the Kokoro authoring path.** Solo the existing engine layers, select clean source, and listen to exported/compressed results. Generate the twelve-file, two-voice audition and select one instructor before batch voice work; do not hold up landing diagnosis for a new user decision.
2. **Use the captured failing approach to implement landing/rollout/exit.** Use native checkpoint runs for fast iteration, then full departure-to-stop runs. Record actual failure causes and fix the controller/geometry, not the crash detector thresholds.
3. **Implement explicit handoff and rejoin.** Validate ownership, configuration continuity, off-route recovery and early return.
4. **Author the selected scenic extension and contextual cue director.** Verify pilot sightlines and conditional narration before producing all final recordings.
5. **Generate, review and promote the Kokoro library; finish the mix and minimal supporting UI.** The stable existing lesson text can be generated earlier when convenient. Generate tour recordings once route/state contracts are settled; only changed cues need regeneration. Preserve the accepted design. Defer guest profiles, extra lesson-recap work and decorative haptics until the main demonstration passes.
6. **Build/install one integrated candidate for the final worn session.** Inspect affected visuals, verify native hierarchy/authority and package identity, then measure the representative route on Quest.

Required focused scenarios:

- At least three consecutive complete native flights in the ordinary shipped conditions, plus the other supported runway/wind direction and representative gust variation; zero crashes, concealed recoveries or false success.
- Occupied runway → go-around → later successful landing; unstable/long approach → go-around.
- Takeover/handback in cruise, banked/off-route flight, near final and on the ground; repeated handoffs; changed throttle/trim/flaps; user request to return early.
- Unsupported recovery state → explicit pause/recovery choice, not a claim of successful automatic rescue.
- Manual diversion away from a landmark, head glance away, interrupted cue, rejoin after missed sights and mode change: no stale location/manoeuvre narration or duplicate backlog.
- Pause, tracking loss/headset removal and resume during handoff/approach retain safe ownership and require intentional resume.
- Repeated idle/transition playback has no voices or loop defects. Narration remains intelligible at engine power and warning cues remain audible.
- All required lesson/tour clip IDs resolve to reviewed Kokoro recordings and matching captions; no stale David clips, incorrect numeric readings, truncated corrections or silent first-use failures. Mode changes and interruption release ducking. Confirm packaged playback works without a network connection.

Pure state checks plus native scenario traces are necessary here; a compile, waypoint unit test or attractive still image cannot establish a successful landing. Reuse existing tools, avoid unrelated full-map suites and repeated aerial-photo comparisons. Inspect affected UI with the required scoped stereo Visual QA; do not repeat scenery reviews for an audio/guidance change. Build validation remains required. Do not ask the user to wear the headset while core landing failures are still reproducible offline. The final session follows readiness, and a failed gate triggers a targeted fix rather than another premature demonstration.

Acceptance records must include touchdown location/sink/attitude, rollout containment, exit/stop, control/route state and any go-around/recovery. Set explicit limits from the actual model and runway during implementation; merely avoiding the crash classifier is insufficient. For narration, log cue eligibility and cancellations without claiming frame-level GPU occlusion certainty.

## 10. Remaining polish after the complete demo works

Retain the previous plan's compact first-visit setup, guest progress isolation, consistent activation feedback and limited evidence-backed lesson recap as secondary work. They must not delay the clean-audio/complete-flight candidate or substitute for it.

Before a high-stakes presentation, one prepared full headset session and a small first-time-user trial remain appropriate. The demo must land, reset, explain itself and remain understandable without a developer apologizing for normal behavior. This establishes a credible demonstration milestone, not all-weather autoland, certified training, or market-readiness proof for every use case.

## Research references

- [Boulder airport procedures](https://bouldercolorado.gov/services/airport-traffic-pattern)
- [FAA stabilized approach and landing](https://www.faa.gov/newsroom/safety-briefing/stabilized-approach-and-landing)
- [FAA Airplane Flying Handbook, approaches and landings](https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/airplane_handbook/10_afh_ch9.pdf)
- [Boulder Reservoir](https://bouldercolorado.gov/locations/boulder-reservoir)
- [Flatirons reference](https://bouldercolorado.gov/trail/1st2nd-flatiron)
- [Folsom Field reference](https://www.colorado.edu/coloradan/2018/04/24/then-and-now-folsom-field)
- [Kokoro model, license and generation format](https://huggingface.co/hexgrad/Kokoro-82M)
- [Kokoro official implementation](https://github.com/hexgrad/kokoro)
- [Kokoro voices and utterance-length considerations](https://huggingface.co/hexgrad/Kokoro-82M/blob/main/VOICES.md)
- [Audio accessibility guidance](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/105)
- [Current Meta performance criterion](https://developers.meta.com/horizon/resources/vrc-quest-performance-1/)
