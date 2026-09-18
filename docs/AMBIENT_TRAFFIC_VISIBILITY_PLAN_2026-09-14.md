# Boulder ambient traffic: revised visibility and realism plan

Status: approved; candidate built, installed and presented in the requested short worn session. User feedback: "This was an improvement." Specific airborne/glider visibility and taxi-motion acceptance remain open. This replaces the previous correction plan within the unified visual upgrade/shared-atmosphere milestone. The full ordinary-flight visibility contract remains open.

## Implementation evidence — September 15

- Removed whole-flight airport ownership. Dwell advances independently; the opposing apron connector, runway and runway-26 opposing taxiway receive separate protection. Runway-08 following traffic can move concurrently. Runway-26 outbound traffic waits when its shared lane would obstruct an imminent arrival.
- Unobstructed runway-08 Cub taxi begins at 20.4 seconds; second C172 taxi begins at 90.5 seconds. All three powered actors complete a cycle within the 20-minute scenario, including two schedule offsets. Eight focused scenarios pass with zero emergency suppressions (`ambient_visibility_20260915/scenarios_10/traffic_scenarios.json`). Reverse-runway testing demonstrates arrival, departure and no deadlock, but does not establish all three completing within 20 minutes.
- Routes maintain the published 6,300 ft MSL downwind height and north-side pattern. Runway 26 is separately authored. Exact western street/noise-boundary alignment still needs the local overlay audit; do not label current paths a fully verified reproduction of airport operations.
- Three existing LODs now use the pilot camera and conservative stereo projection scale with shared hysteresis. Inactive prop/flap LOD pivots stop animating. Distinct broad trainer accent colors use the existing shader pass.
- A tight final-turn revision failed acceleration checks and was corrected; an opposing runway-26 taxi/exit deadlock was reproduced and corrected. The airborne-conflict fixture now explicitly verifies that its target is airborne instead of using a timestamp that had become ground rollout on the revised route.
- Inspected 15 temporal stereo samples and supplemental seated-window views in `ambient_visibility_20260915/simulator/meta_xr_simulator/qa_run/sessions/ambient_airport`. The yellow Cub is recognizable in both eye window views, moving from about 126 m to 99 m away. These window views use separate diagnostic eye cameras; the tracked Main Camera is never moved. The forward C172 is still an indistinct distant speck (about 2.0–2.3 km), so ordinary airborne legibility is NOT accepted. Normal foothill transit and close glider presentation remain unverified for this candidate.
- Standard Visual QA: 130 shots, the same 26 machine-flagged shot IDs as the September 14 baseline; no new failed IDs. Inspected the contact sheet and relevant full-size stereo window images. This is not a clean machine-QA pass or Quest performance evidence.
- Android production build passed; all 1,912 scenery payload files verified. Installed APK SHA256 verified on Quest: `5d2f4e3c99125acd10335e9b5af11a9d330a7daa1d6c5cac5b3cedcb61b5e029` (`ambient_visibility_20260915/android/product_apk_manifest.json`).
- The user requested an immediate short headset presentation instead of more automated checks. Proceed with the demonstrated right-window Cub taxi opportunity, then free flight with shared wind. This early presentation does not close the remaining airborne/glider visibility, local boundary audit or sustained Quest performance gates.
- Worn session: user confirmed headset on, entered Free Flight and subsequently took off. `ambient_visibility_20260915/worn/taxi.png` shows the yellow Cub clearly in both eyes; `airport.mp4` and `departure.mp4` retain the short captures. `flight.png` confirms subsequent airborne cockpit presentation near the airport. User assessment of motion and wind remains pending; no airborne ambient aircraft or glider acceptance is claimed from these captures. The app was left running for free flight.

## What was wrong with the previous plan

The scheduling correction is necessary, but the previous plan did not establish that normal flights would show noticeable, realistic traffic.

- Moving the first C172 to the departure segment still starts it about 1.28 km from the runway-start pilot. Direction improves; apparent size may not. This is a candidate to inspect, not a proven solution.
- Enabled renderers and frustum hits do not establish visible aircraft pixels past cockpit/terrain/cloud obstruction. A thin aircraft can remain an indistinct speck.
- A close diagnostic start beside a glider proves only that encounter. It cannot pass normal-flight visibility.
- A shared taxiway lock held throughout an approach or after clearing into a holding space can recreate long queues.
- Three LODs already exist. Aggressive culling is not the principal opportunity for five aircraft and risks worsening disappearance.
- Appropriate aircraft types do not alone reproduce Boulder operations. Current powered routes share a mirrored template, while gliders have no visible tow/landing/retrieval cycle.

Recommendation: correct scheduling, verify locally credible routes, preserve silhouettes and prove ordinary-flight encounters. Add only exterior details that contribute at their actual viewing size. Do not expand the fleet or rebuild the renderer.

## Project evidence

The whole-flight airport owner blocks other ground actors before their dwell timers advance. Existing `traffic_02/clear_soaring_20min.csv` records first-C172 shutdown at 541 s, Cub taxi-out at 625 s, and the second C172 parked throughout 20 minutes. These are scenario timings, not telemetry from the user's flight. Approach, TaxiOut and TaxiIn also share the current ground reservation.

The simulator witness places the C172 1,069 m away outside both forward eye viewports; gliders are about 3.2 and 5.9 km away. The user saw the ground Piper but no moving traffic. We did not capture the exact airborne rendering state of that flight.

`AmbientAircraftActor.Present` uses span/distance and hysteresis for three LODs. For the configured 11 m powered span, near-tier transitions are approximately 379–458 m, and middle/far transitions 1.83–2.2 km. For 15 m gliders they are 517–625 m and 2.5–3 km. These are code-derived thresholds, not proven recognition ranges; selection uses player-aircraft position rather than per-eye projected geometry.

There is no explicit actor distance cutoff. Unsuppressed renderers are enabled, with normal camera culling still applying. The director's both-eye `IsPilotVisible` test principally governs retirement/reentry. Current inspected captures show recognizable C172 and slender sailplane shapes with simple paint/window blocks. The opaque vertex-color shader has diffuse lighting but no explicit glass response, light emission or propeller blur. Prop/flap pivots currently update even in inactive LODs.

## The local realism contract

Keep two C172s, one PA-18 Super Cub and two modern single-seat sailplanes. Cessna training and the Cub/Discus combination are locally supported. Do not call the generic sailplane derivative an exact Discus without its silhouette/source audit. This is an authored representative sample, not measured or live traffic. [Journeys](https://www.journeysaviation.com/), [Boulder soaring fleet](https://www.soarboulder.org/aircraft).

Validate authored routes against the city's pattern and existing georeferenced scenery: left traffic for 08, right traffic for 26, published 6,300 ft MSL pattern altitude and mapped departure/base/noise boundaries. Distinguish MSL from local terrain height. Validate runway 26 independently, not by assuming mirrored 08 geometry is correct. [City pattern diagram](https://bouldercolorado.gov/sites/default/files/2021-01/noise-abatement-procedures-traffic-patterns.pdf).

Keep paved/glider strips one protected runway resource: the city prohibits simultaneous takeoffs/landings and requires powered aircraft to yield to gliders on final. Unrelated taxi activity may continue. [City airport information](https://bouldercolorado.gov/government/departments/airport).

Use persistent roles and bounded schedule variation: training circuit/full-stop, local departure/return, and lift-driven thermal/ridge soaring. Schedules and destinations are independent of pilot gaze. The Cub is an ordinary powered departure/return until towing is implemented; never imply an invisible tow.

Full Boulder soaring operations remain explicitly incomplete: coupled tow launch, rope/release, tow return, modified glider arrival, spoilers/flare/landing and retrieval without powered go-arounds or self-taxi. That is a valuable later extension, preferably using the same population, but not a prerequisite for fixing invisible traffic.

## 1. Prove legibility before choosing a new start position

Reuse existing evidence. Make one compact temporal stereo capture set from the actual pilot seat at real projection/scale, inspecting relevant aspects/backgrounds around 300 m–2 km plus a far-glider case. These are inspection points, not guaranteed recognition ranges. Inspect normal playback without zoom; retain High FFR on Quest.

An 11 m span at 1 km subtends about 0.63 degrees. Wing thickness and fuselage cover far fewer pixels. More triangles cannot compensate for inadequate image coverage. Record phase, position, velocity, LOD, separation, per-eye projected span, view direction and actual occlusion/visible coverage. Offline diagnostic masks may establish coverage; no new shipping object-ID pass. Distinguish detecting moving aircraft from recognizing an exact subtype.

Evaluate departure/early-circuit placement, but accept it only if actual cockpit footage materially improves. If it remains end-on and indistinct, select a safe oblique encounter phase on locally valid routes and demonstrate it in ordinary departure/circuit flight. Do not lower the pattern, enlarge aircraft, violate separation or reposition them toward the camera. Not every arbitrary user route or fixed gaze can guarantee an encounter.

## 2. Fix scheduling and route behavior together

Remove whole-flight airport ownership. Advance startup/dwell independently and request only the next conflicting space. Use a small fixed set of authored conflict regions: shared taxi bottlenecks, hold footprints, runway and exits. Reuse paths/scenery footprints; no general pathfinding or ATC engine.

Set clear-path Cub taxi eligibility to 20–30 s and the other parked C172 later, initially 90 s. Keep smooth stops, clear destination space, fair queues and committed-arrival priority. Reserve the necessary exit before final commitment, not every taxiway for the whole approach. Never admit opposing traffic that traps a rollout. Release after the whole aircraft clears, or on reset/escape/retirement. Protect a stopped hold footprint without owning its already cleared corridor.

Preserve separation envelopes, exclusive runway operations, player priority and early go-around decisions. Do not use suppression as routine avoidance. No hovering, teleportation or abrupt turns. Offscreen aircraft continue moving.

Audit route geometry against the cited local diagram before using it for encounters. Correct discrepancies in this milestone. Maintain wind-consistent ground track/airspeed, speed-appropriate bank/turn radius, phase-appropriate pitch, approach/flare/contact and braking. Gliders retain the shared lift/sink model. Verify changed motion with scenario evidence; native JSBSim remains the sole player authority.

## 3. Explicit LOD and culling strategy

| Tier | Retain | Avoid |
|---|---|---|
| Near | Correct outline, gear/struts, windows, broad paint, prop state, visible flaps | Flyable interior/backend duplication |
| Middle | Solid wings/body/tail, broad contrast, readable bank and heading | Tiny details and imperceptible part animation |
| Far | Cheap stable 3D silhouette with wings and tail | Small moving parts and detailed shading |

Keep three tiers. Evaluate conservative projected bounds from the more demanding eye and switch both eyes together with hysteresis. Bounds include moving parts; a thin frontal view or temporary roll must not collapse the silhouette. Change thresholds only where footage supports it. No scale inflation, billboard aircraft or routine double-rendered crossfades.

Use Unity's existing camera frustum culling as the primary draw culler. An enabled offscreen object need not be submitted. Preserve depth testing; depth rejection does not eliminate all submission/vertex work. [Unity culling behavior](https://docs.unity3d.com/6000.3/Documentation/Manual/OcclusionCulling.html).

Do not add custom cockpit-window, aircraft-heading or bank-sector masks for five actors. They risk losing aircraft during head turns. If profiling later justifies explicit gating, conservatively union both eye views with margin/hysteresis; never pause simulation. Stop animating inactive LOD pivots. Retain the far silhouette within the current view distance; no hard cutoff until contribution is demonstrably subpixel in both eyes. No occlusion-baking, DOTS or indirect-rendering rewrite.

## 4. Details worth rendering

| Priority | Detail | Approach |
|---|---|---|
| Required | Stable type silhouette, correct dimensions and bank | Local mesh/LOD correction for demonstrated defects |
| Required | Distinct broad paint, readable windows/canopy | Vertex colors or tiny shared mask; avoid washed-out whites/flat black undersides |
| Required | Stopped/running propeller without aliasing | Near-only bounded blur if needed, with explicit pass/draw cost |
| Required | Wheel contact, taildragger attitude/tail rise, approach flaps | Existing rigid pivots and corrected presentation |
| Conditional | Subtle glass/paint sunlight response | Bounded single-pass response only if visibly useful; no realtime reflection |
| Conditional | Installed beacon/nav/strobe/landing lights | Small directional emissive elements, correct phases/daylight behavior; no realtime lights/giant halos |
| Deferred | Rivets, distant registrations, animated occupants, interior instruments, elaborate shadows | Low value at intended viewing distances |

Lights/glints are intermittent cues, not permanent markers or the main daytime visibility solution. Do not invent lights on unequipped gliders. Correct wheel contact before adding shadows. Audio/CTAF may later help awareness but cannot pass visual acceptance; new audio/radio scope is deferred.

## Acceptance and Quest delivery

Normal-route visibility and diagnostic starts are separate gates. Use the ordinary launch, departure/circuit and ridge transit without teleportation or camera-dependent ambient placement. A QA-only native start near the glider, initially 400–700 m with safe separation, supplements appearance/wind handling only.

- Default unobstructed launch: Cub taxi within 30 s and at least 10 s of identifiable movement through a usable side window in the first minute. Blocked cases may wait and record why.
- Ordinary first five minutes: a 10 s identifiable airborne opportunity through a forward or side window on the selected normal departure/circuit. Check immediate departure and a short runway wait; no universal guaranteed close pass.
- Sustained activity: all powered actors complete a cycle when unobstructed, with concurrent airborne/ground movement and no starvation in the existing 20-minute trace. Check two starting-time variations so the feature is not startup-only.
- Normal foothill transit: show an actual opportunity to see the ridge glider. Separately show 15 s of moving sailplane in both eyes from the nearby QA start with lift/separation evidence; this cannot replace normal-route coverage.
- Correctness: both runway directions, player taxi/departure/final, occupied exit/hold, airborne conflict, go-around and reset. Zero intersections, normal-operation emergency suppression, runway overlap or stuck reservations. Local route overlays support behavior.
- Rendering: no one-eye disappearance, lost wing/tail silhouettes at intended sizes, head-turn/LOD popping, incorrect light phases or prop flicker. Retain normal-speed clips with actor-state evidence.

These are targets, not current passes. Use focused existing tools, not an unrelated full test campaign or new aerial-image comparison. Preserve known baseline Visual QA failures and report new regressions separately.

Keep five actors, shared materials, three LODs and accepted scenery/High FFR. Fixed-size scheduling stays at the current 10 Hz with smooth frame-rate presentation. More visible aircraft can cost more despite unchanged population. Account for optional prop/light/glass draws and measure the actual Quest candidate; existing CPU/GPU limits remain targets. Do not promise gains from culling a handful of meshes.

Before asking for a headset, inspect ordinary-flight clips, retain behavior evidence, build Android ARM64/Vulkan/OpenXR and verify payload/hash. Prepare one short airport/foothill session. Separate visibility, handling and sustained performance acceptance. No accepted-feature main promotion before the demonstrated scope is established.

## Approval boundary

Approval received for the bounded scheduler/local-route/visibility/LOD/exterior corrections, diagnostics, focused checks, Android build/install and one prepared worn session. Fleet expansion, complete tow/landing/retrieval, new audio/radio and renderer/weather rewrites remain separate follow-ons.
