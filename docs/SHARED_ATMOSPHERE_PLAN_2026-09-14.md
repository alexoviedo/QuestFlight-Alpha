# Shared atmosphere in every flight mode

Status: **Approved September 14; implementation and validation in progress.** The user explicitly authorized this milestone. Final Quest acceptance remains pending.

This is an amendment to the unified visual upgrade and ambient-aircraft plan, not a separate weather-engine project. Upon approval, it supersedes the September 11 requirement to preserve a simplified Calm atmosphere in normal product flights. The user's correction is that all flight modes must use realistic air movement. Conditions may differ; the atmospheric model must not depend on Demo versus Free Flight. Lessons are currently unavailable and must inherit this contract when implemented.

## Outcome and scope

Every normal launch uses the same shared atmosphere automatically. There is no special soaring switch needed for wind, gusts, thermals, terrain lift or sink. Start with one authored moderate daytime Boulder upslope condition, available identically in Demo and Free Flight. Remove the current Calm/Foothill Soaring toggle from the product menu; do not display a nonselectable weather item (user clarification, September 14). Preserve still-air and alternate-wind configurations as explicit developer test fixtures, not product physics shortcuts. Future weather controls may change physical conditions through this same model.

This milestone implements a bounded approximation of background wind/shear, spatially coherent gusts and light turbulence, thermals with surrounding sink, and direction-dependent ridge lift/lee sink. It does not claim to simulate every form of atmospheric motion. Mountain waves, rotor circulation, storms, live weather and a flyable sailplane remain later work. Ridge lift and mountain waves require different conditions and models; adding arbitrary mountain updrafts would not satisfy the goal. The distinction follows the [FAA soaring-weather chapter](https://www.faa.gov/regulations_policies/handbooks_manuals/aviation/glider_handbook/gfh_chapter_9.pdf).

## Findings from the actual project

Inspected September 14 at feature commit `de25c244e0a4836df3aa722b552d409e6f6ca6d3`; installed Quest APK hash matched `64b3c7528559effee9683bb6c47f4bd35881cfb911093416f8e6808eb4bc0fd5`. The user's local `DevAgentSettings.asset` edit is unrelated and must be preserved.

| Current behavior | Required correction |
| --- | --- |
| `BoulderWindFieldProfile.Sample` returns only the accepted airport thermal for Calm, before evaluating horizontal wind, gusts, ridge flow or foothill thermals. | Use one composition path regardless of flight mode; weather supplies parameters. |
| `FlightDynamicsCoordinator` allows a missing wind profile in Calm and initially supplies still air during native initialization. | Require the serialized shared atmosphere in product scenes; validate wind during initialization/reset as well as stepping. |
| `BakeWind` derives one east/west terrain response with shelter sampled to the east. Runtime multiplies that response by wind magnitude. Reversing wind does not reverse or relocate the lift field. | Bake directional terrain data and evaluate it against the actual wind vector. |
| Gusts are two smooth scalar oscillations along the mean wind. The airport thermal remains stationary while the foothill thermal tilts. | Add bounded three-component fluctuations and consistent thermal sampling; distinguish gusts from validated turbulence fidelity. |
| `AmbientTrafficDirector` chooses some soaring behaviors by preset name, with a fixed sink approximation. | Select behavior from measured lift, clearance and available energy. |
| Clouds explicitly reset to their rest position in Calm. Eight volumes otherwise drift as one group with no long-session replenishment. | Make visual wind cues use the shared field in every flight; validate sustained coverage and reset behavior. |
| Six traffic scenarios passed, but their pass condition checks suppression, acceleration and powered takeoff/shutdown. Ridge phase coverage is recorded without a sustained-lift requirement. | Add a separate ridge-soaring outcome test and actual pilot-view evidence. |

The eleven existing Windows native wind checks establish signed response, short power-off lift/sink response and limited taxi behavior. They do not establish full wind-enabled takeoff/landing or accurate Boulder airflow. Preserve this evidence and its limits.

## Implementation sequence after approval

### 1. Establish one product atmosphere and migrate authored data

- Keep the existing serialized wind asset and GUID. Separate physical weather parameters from flight-mode selection, and migrate the scene/prefab settings explicitly in Editor authoring.
- Make Demo and Free Flight reference the same asset, weather state and simulation clock. Remove preset-name early returns and missing-profile still-air fallbacks. Invalid atmosphere data must fail closed.
- Keep native JSBSim as the only player flight authority. Supply the composed ENU vector once per native step through the existing NED/meters-to-feet bridge. Audit initialization, reset and menu-to-flight transition so neither stale wind nor an artificial calm first step reaches the running flight.
- Use the existing moderate easterly values as initial tuning inputs, not proven realistic final numbers: 2.5 m/s surface and 5.5 m/s aloft. Select and record the final height profile after inspecting the actual foothill lift band and native handling. This is authored weather, not today's observed weather.
- Remove weather selection and nonselectable weather text from the menu. Flight selection no longer selects atmospheric fidelity. Developer fixtures retain deterministic still-air comparisons.

### 2. Correct terrain flow before increasing its strength

- Reuse the current 161 x 161, 100 m terrain grid. Bake smoothed east and north terrain gradients plus exposure for eight wind-direction sectors. Blend adjacent sectors using actual wind direction; no terrain raycasts or terrain analysis during flight.
- Estimate signed deflection from the horizontal wind projected onto the terrain gradient, with direction-correct upwind shelter, bounded amplitude, height decay and smooth edge blending. Use ridge relief/exposure to keep lift tied to actual slopes rather than a generic height above every terrain cell.
- Inspect vertical sections through the actual proposed foothill route, including crest and lee. Verify east, west, north and along-ridge wind cases. In a synthetic ridge fixture, reversing the wind must exchange windward and lee sides; parallel and zero wind must remove the ridge contribution.
- Inspect the full authored coverage boundary. Keep background wind continuous beyond the terrain-flow grid and fade unsupported local effects without pretending unmodeled terrain has validated lift.
- Require a plausible lift band wide enough for the planned glider path and outward turns. If no such band exists at the inspected ridge, choose a better exposed segment or different authored conditions; do not add lift merely to keep the actor aloft.

### 3. Compose thermal, gust and turbulence contributions consistently

- Use one allocation-free sampler with optional component diagnostics: background, gust/turbulence, thermal and terrain flow. All consumers sample it at their own world position and the same simulation time.
- Retain the accepted airport heat-source location and foothill source. Apply a consistent wind-tilted thermal model, smooth core/edge/height envelopes and bounded surrounding sink. Remove duplicate legacy updraft addition. Thermal strength is a condition parameter, not a flight-mode switch.
- Replace the current gust's restriction to the mean-wind direction with a small fixed set of seeded, spatially and temporally correlated three-component fluctuations. Scale them with authored wind, height and exposure. No random force each frame and no camera shake. Treat this as a light-turbulence approximation, not a validated full turbulence spectrum or rotor model.
- Use the existing native wind-vector input. Do not additionally enable a private native random turbulence source that would double-count disturbances or give the player different weather from traffic. JSBSim supports separate wind/gust/turbulence contributions; audit the packaged configuration accordingly. [JSBSim wind interface](https://jsbsim-team.github.io/jsbsim/classJSBSim_1_1FGWinds.html).
- Keep the initial scenario's thermal parameters constant over a flight. A dynamic solar/day-night weather simulation is not needed to correct this behavior.

### 4. Make sailplanes and environmental cues respond to that field

- Remove preset-name tests from glider decisions. Compare available updraft against the actor's speed/bank-dependent sink, with hysteresis to prevent rapid switching between thermal, ridge and glide behavior.
- Keep the existing animated-aircraft approach. Document its sink approximation; preserve continuous wind drift, bounded bank/turn rate, terrain clearance and altitude loss when lift is insufficient. No powered climb, altitude clamps or hidden repositioning to maintain the demonstration.
- Author a foothill route within the inspected lift band, including turns away from terrain and a reachable descending escape. Use the existing two sailplanes and powered traffic population.
- Windsock and eagle sample the same atmosphere. Clouds drift consistently at cloud altitude in both flight modes; remove the Calm reset branch. Check a 60-minute accelerated advection run for map coverage, culling/interior alignment and reset continuity. If replenishment is needed, reuse the existing authored volumes with off-view placement and a continuous visibility transition; do not add cloud raymarch samples, volumes or passes to solve coverage.
- Combine this with the traffic-visibility correction already proposed: start one existing powered aircraft on a safe airport circuit and validate the initial pilot view. Keep runway priority and separation; a visible encounter is not permission to stage a close pass through the player's departure path.

### 5. Prove behavior offline, then prepare one Quest session

| Gate | Required evidence |
| --- | --- |
| Same physics in every mode | Identical sampled atmosphere for identical position/time/conditions in Demo and Free Flight; no product still-air fallback. Pause freezes weather time; reset is deterministic and does not jump the aircraft/camera. |
| Terrain field correctness | Inspected actual-site cross-sections; synthetic signed-direction, shelter, altitude-decay and boundary tests. Changing direction cannot retain the old windward map. |
| Native flight response | Extend the existing native suite with sampled-field thermal/ridge/lee traversals, takeoff, approach/landing, crosswind, pause/reset and startup. Verify wind units/sign, finite states, ground contact and unchanged single-authority ownership. Run the actual Demo route with wind; fix guidance if needed rather than disabling its atmosphere. |
| Sustained ridge soaring | A ridge-only fixture on the actual terrain: at least five continuous minutes and three outward ridge-end turns, with logged terrain lift, sink rate, altitude and clearance. No thermal contribution may be credited as ridge lift; no suppression, hidden altitude correction or terrain intrusion. A lift-loss case must show descent and a natural exit. |
| Traffic separation and visibility | Retain all runway/ground/air conflict cases and add wind-dependent arrival/departure cases. Inspect both-eye simulator clips from normal pilot windows showing a recognizable powered encounter and a foothill glider. Rendering counters alone do not pass visibility. |
| Build and visual regression | Required regression checks, inspected standard Visual QA and stereo simulator captures, Android ARM64/Vulkan/OpenXR build, packaged payload verification and installed APK/source identity. Report inherited failures separately. |

Use the current accepted scenery/High FFR configuration. Development diagnostics record atmosphere identity/components and actor phase/distance/rendering state at bounded frequency; disable verbose reporting for timing captures. No headset request until these offline gates pass and the expected encounter can be described precisely.

Then use one prepared worn session for startup, wind response, pilot-visible aircraft and actual Quest timing/thermal evidence. A short session establishes a development result, not sustained shipping acceptance. If a required result is inconclusive, report it rather than asking for repeated unexplained viewing attempts. Do not promote the feature to main as accepted before these checks establish the claimed scope.

## Quest 3 constraints

- Offline terrain baking; bounded grid/thermal/noise work during sampling; no runtime CFD, allocations, hierarchy construction or additional flight authority.
- Retain the prior atmosphere target of at most 4 MiB resident data and 0.1 ms mean aggregate main-thread wind work; add a proposed 0.2 ms p95 target measured on Quest. These are acceptance targets, not existing measurements. Profile the native-step sampler plus traffic, cloud and cue consumers together.
- No planned additional GPU passes or cloud volumes. Retain the five actors, existing exterior LOD/material limits and accepted foliage/FFR settings. More aircraft being visible may still increase GPU cost; measure it in the final encounter.
- Retain the ambient feature's proposed combined CPU target (traffic 0.2 ms mean / 0.3 ms p95 plus wind) and rendering target of at most 0.3 ms additional GPU time where a valid counter exists. Do not equate simulator timing or delivered-frame cadence with isolated GPU work.
- If the budget is exceeded, reduce unnecessary samples and distant behavior work first. Do not hide the feature, silently disable wind by flight mode or sacrifice accepted scenery to make the test pass.

## Approval boundary

The September 14 approval authorizes the five-step milestone above, including the existing-population visibility correction, authored data migration, bounded gust/turbulence approximation and required validation. It does not authorize a full weather rewrite, wave/rotor modeling, new aircraft population, flight-physics fallback or immediate main promotion.


## September 14 implementation checkpoint

Shared atmosphere, directional terrain response, coherent gusts, thermal sink, native wind initialization, lift-driven ambient soaring and bounded cloud advection are implemented. Weather selection and nonselectable weather menu text are removed following the user's clarification. Latest authored references were regenerated successfully.

Evidence is retained in `T2-QuestFlight-product-artifacts/shared_atmosphere_20260914`: six native flight scenarios and six traffic scenarios passed; the ridge-only run remained in ridge flight for 600 seconds with 12 reversals, no thermal contribution and no emergency suppression. The loss-of-lift case departed under glide. The full EditMode run had 470 passes and one expected changed-source hash failure; the corrected targeted run passed 20/20. Later menu edits and an added allocation check were not covered by that targeted run. The user requested no further automated test campaigns before headset evaluation.

Standard Visual QA retained the same 26 failed shot IDs as the prior baseline, with no new failed IDs; it is not a clean machine-QA pass. Left/right simulator cockpit captures were inspected. Traffic initialization and active renderers are evidenced, but this does not establish that a seated pilot notices the aircraft. Android build/install and worn visibility/performance acceptance remain pending at this checkpoint.

The short worn session should start at the airport: look left of the runway, above the horizon, for the circuit C172. Then fly northwest toward the foothill ridge approximately 6 km west and 1.6 km north of the airport reference, initially around 6,600–7,000 ft MSL subject to terrain clearance. The ridge glider operates around that terrain-relative band. Watch the vertical-speed response with reasonably steady airspeed and power; gusts and aircraft pitch also affect the indication. This is a development handling/visibility check, not sustained thermal or frame-budget acceptance.

Android build and all 1,912 scenery payload checks subsequently passed. Installed package hash was verified on Quest `2G0YC5ZG8907TD`: `3f0f2fa5955df3bf35dec217a29b769539517cce6d6546aa538507740161b038`. App launched to prepare the worn session. Worn acceptance remains pending.


### Worn feedback: ambient visibility not accepted

The user saw the Piper on the ground after taking off and turning around, but did not notice taxiing or airborne aircraft. This does not establish acceptance of moving traffic, ridge lift or the sailplane encounter. The available headset captures from the guided attempt show the menu; they do not prove airborne presentation. Do not claim a headset rendering failure or successful flight encounter without evidence.

Investigation found a concrete scheduling limitation in `AmbientTrafficDirector.AdvancePowered`: `airportMovementOwner` blocks every other ground actor for the owner's complete flight/landing/taxi-in sequence. Existing `traffic_02/clear_soaring_20min.csv` records the first C172 reaching shutdown at 541 seconds, the Cub only beginning taxi-out at 625 seconds, and the second C172 remaining parked for the full 20 minutes. The occupied-runway scenario starts Cub taxi at 188 seconds, showing that duration depends on player/runway state. These are recorded scenario timings, not telemetry from the user's flight.

The existing simulator witness places the first C172 1,069 m away and outside both forward eye viewports; the airport glider is 3,230 m away and the ridge glider 5,877 m away behind the initial aircraft heading. Enabled renderers and passing motion scenarios are insufficient evidence of a useful pilot-visible encounter.

The revised, approval-pending correction is consolidated in [Boulder ambient traffic visibility and realism](AMBIENT_TRAFFIC_VISIBILITY_PLAN_2026-09-14.md). It includes independent safe taxiing, local route checks, conservative stereo culling/LOD and useful exterior details. Ordinary-flight visibility must be demonstrated separately from close diagnostic starts; a new departure position is a candidate, not an assumed fix. Keep population and accepted scenery settings. No implementation is authorized by this documentation revision.
