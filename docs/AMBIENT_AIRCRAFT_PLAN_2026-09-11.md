# Boulder ambient aircraft and shared soaring wind — implementation plan

Status: **approved; implementation in progress on `feature/boulder-ambient-aircraft-wind`**. September 11, 2026. This is the next visible-feature milestone within the [unified visual upgrade plan](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md), following `b542191` / `quest3-scenery-dev-20260910`. See [implementation evidence and remaining gates](AMBIENT_AIRCRAFT_IMPLEMENTATION_2026-09-11.md). Unity licensing is restored. Five serialized aircraft, native shared wind, weather selection and presentation cues are integrated; acceptance/build status is recorded in the implementation checkpoint.

## Recommended result

Create a small, persistent population of locally appropriate aircraft: **two C172s, one PA-18 Super Cub, and two modern sailplanes, targeting the locally based Discus CS appearance**. Five ambient aircraft is a population ceiling, not a requirement to have five airborne or in sight. Normally show one useful airport movement and one or two airborne aircraft relevant to the pilot's location. Allow at most four airborne and two taxiing, subject to the five-aircraft total. Keep the existing static parked fleet.

Powered aircraft receive complete parked/start/taxi/hold/takeoff/departure/arrival/landing/parking behavior. Sailplanes initially appear already airborne, use available thermal or ridge lift, glide between lift areas, and leave through distant route boundaries. They never self-taxi, hover, climb without lift, or perform powered go-arounds.

**User refinement during planning:** include real simulated wind as a foundation for a future flyable sailplane. V1 therefore includes a shared, deterministic wind/lift field consumed by native player physics and ambient motion, with calm and moderate foothill-soaring presets. This supersedes a purely presentation-only mountain-lift design. Realistic mountain-wave/rotor weather and the flyable sailplane itself remain explicitly sequenced follow-ons. The user approved implementation after downloading the Super Cub.

**Scope decision:** a complete aerotow launch, rope release, glider landing, and ground-retrieval cycle is a separately identified extension, not a prerequisite for this first feature. The Cub can return from an off-map tow or make an ordinary powered flight in V1; it will not pretend to tow an unconnected glider. Section 7 specifies the additional behavior required before visible glider runway operations can be enabled. This keeps the first delivery small while making its limitations explicit.

Success means recognizably different aircraft moving naturally through views available from the actual pilot seat, reacting correctly to the player's aircraft, with measured and bounded additional cost. It does not mean filling the sky, adding airline traffic, or simulating an entire airport's daily schedule.

## 1. Evidence from the current project and Boulder

### Project inspection

Inspected on September 11:

| Current evidence | Consequence for this feature |
| --- | --- |
| `ProjectVersion.txt`: Unity 6000.3.8f1; product is standalone Android ARM64 / Vulkan / OpenXR. | Use the existing renderer and serialized product hierarchy. No renderer migration or additional flight backend. |
| `ProductionEnvironmentRoot.prefab` contains runway endpoints near ENU `(-629.757, -30.671)` and `(620.628, -30.054)`, approximately 1.25 km long and 22.86 m wide. `FlightDynamicsInitialConditions.KbduRunwayTrueHeadingDegrees` is 89.972 degrees. | Derive paths from serialized coordinates. Runway number 08 is not an 80-degree true-world heading. Preserve the existing sloping visual/physical runway. |
| `AirportParkingStandManifest` has stand positions, apron planes, boundaries and spacing. `airport_aircraft_placements_v1.json` describes 17 south-apron powered and 18 north-line sailplane placements. | Reuse the placement evidence and obstacle footprints. The manifest is not an operational taxi graph; author and validate that missing graph. |
| `AirportParkedAircraftSourceManifestV1.json` records C172 LODs of 400/350 triangles and sailplane LODs of 279/239 triangles, joined with no animation. | These are distant scenery assets, not suitable near moving-aircraft models without preparation. Reuse the higher-detail licensed sources. |
| `FlightDynamicsCoordinator.CurrentState` exposes authoritative position, velocity, attitude, ground speed, MSL/AGL and weight-on-wheels. | Read this snapshot for separation. Do not use headset gaze or the legacy Unity-relative `AircraftState.altitudeFt` as aircraft position/MSL authority. |
| `AtmosphereFromUpdraft` currently sets horizontal wind to zero and clips vertical flow to updraft only. The shared thermal profile is northeast of the airport, at ENU `(1700, 2600)`. | Replace the coordinator's updraft-only composition with the proposed shared, signed 3D field. Preserve the existing thermal once within it. The legacy profile is not a mountain or southeast glider-box thermal. |
| The managed/native bridge already exposes north/east/down wind; `qfl_jsbsim_set_atmosphere` writes JSBSim's three wind properties. The existing `WindMatrix` records a response as `Unproven`, with no pinned trimmed reference. | No new flight engine is needed. Wind scenario evidence must be strengthened before claiming physically credible behavior. Do not treat the old wind test as a passed validation. |
| `AirportNearFieldDetailAuthoring.AddWindsock` bakes crossed sock faces into the static near-field mesh. | Extract just the sock into an authored movable visual so its direction/extension agree with simulated local wind. Retain the mast and surrounding scenery. |
| No ambient-aircraft scheduler, taxi conflict graph, or runway reservation system was found in `Assets/Scripts`. | This is new, bounded behavior, not a toggle in existing road traffic. |
| Accepted High FFR, canopy range and appearance are checkpointed. The recovery diagnostic reported 17.645 ms mean delivered cadence / 56.62 FPS, with unmatched views and no isolated GPU duration. | There is no established spare GPU budget. Proposed limits below are engineering targets, not measured Quest performance. Preserve the accepted scenery settings. |

Relevant code: [environment root](../QuestFlight/Assets/Scripts/Environment/ProductionEnvironmentRoot.cs), [native state and coordinate contract](../QuestFlight/Assets/Scripts/Flight/Backends/FlightDynamicsTypes.cs), [terrain query interface](../QuestFlight/Assets/Scripts/Environment/IProductionTerrainSurface.cs), [parking manifest](../QuestFlight/Assets/Scripts/Environment/AirportParkingStandManifest.cs), [existing asset generator](../tools/kbdu_airport_aircraft/build_airport_aircraft_sources.py), and [checkpoint evidence](SCENERY_DEVELOPMENT_MILESTONE_2026-09-10.md).

I inspected the checkpoint's `29_south_powered_aircraft.png`, `30_north_sailplane_line.png`, and `v4_after_10_runway_apron_overview.png`, the existing sailplane source preview, and the city's airport diagram and traffic-pattern map. The current static aircraft provide recognizable silhouettes, but the distant screenshots do not establish close moving-model quality. The overhead airport image also shows photographic aircraft/shadow remnants beneath some placements: a moving plane must not leave a second plane painted onto the apron.

Existing captures are under `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/main_milestone_20260910/visual_qa/standard_qa/screenshots/`. Research previews and the city PDF are retained outside Git in `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/ambient_aircraft_plan_20260911/`.

### Local operating facts

Boulder Municipal is a general-aviation airport with no scheduled airline service. The city explicitly prohibits simultaneous operations on paved 8/26 and adjacent glider 8G/26G because their centerlines are only 197 ft apart. Ambient operations must lock **both strips as one resource**, including a player's use of either strip. Powered ambient arrivals yield to gliders. [City airport information](https://bouldercolorado.gov/government/departments/airport).

The city's diagram places taxiway A south of the paved runway, with connectors A1–A6, and the glider strip to the north. Its published pattern is left-hand for 08 and right-hand for 26, at 6,300 ft MSL. Runway 08 is preferred in calm conditions; routine 08 straight-ins are discouraged/prohibited by the published local procedure, and the pattern must respect the mapped residential/road boundaries. Glider arrivals use the separate modified pattern. Author routes against this diagram and the actual scenery, rather than a generic rectangular circuit. The map is guidance for scene authoring, not permission to assume every historical taxiway label or tenant is current. [City airport diagram and noise-abatement map](https://bouldercolorado.gov/sites/default/files/2021-01/noise-abatement-procedures-traffic-patterns.pdf).

Local type evidence supports this deliberately small mix:

- Specialty Flight Training lists C172S, TR182 and 7GCAA aircraft. [Operator fleet](https://www.specialtyflight.com/rentalfleet.html).
- Journeys lists Cessna, American Champion and Pipistrel aircraft. Its dated 2024 rental sheet additionally establishes historical C150, C172, C182, DA20 and Citabria presence; that sheet is not a current availability guarantee. [Operator](https://www.journeysaviation.com/), [dated fleet sheet](https://www.journeysaviation.com/uploads/1/3/2/8/132898297/aircraft_rental_price_list_6.1.2024.pdf).
- Soaring Society of Boulder lists DG-505, ASK-21, two Discus CS sailplanes, Piper Pawnee and Piper Super Cub. This supports a small towplane and modern gliders, rather than jets or a random worldwide aircraft pack. [Club fleet](https://www.soarboulder.org/aircraft).

The population proportions are a proposed visual composition, not a claim about measured real-world traffic frequency. Use restrained generic liveries appropriate to these types, not copied local registrations or operator branding.

## 2. Exact model acquisition and preparation decision

| Aircraft / planned count | Selected source and preparation | Readiness |
| --- | --- | --- |
| C172 family / 2 | Existing KOG_THORNS [Cessna172](https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4), already recorded as CC BY 4.0. Extract an exterior-only derivative from the pinned original GLB, with two restrained paint variants. Preserve propeller, wheels, flap/control pivots and a simple occupant silhouette. | Available locally; no user download needed. Do not duplicate the flyable rig or ship its cockpit/backend with ambient actors. |
| PA-18 Super Cub / 1 | [Piper Pa 18 by helijah](https://sketchfab.com/3d-models/piper-pa-18-413e3f81b0db4e0ebba0f27490732770). The public model API currently reports downloadable, 18,022 faces and CC BY 4.0. Prepare a wheeled exterior with a taildragger attitude and separate prop/control parts. | Source page/API checked; archive not downloaded or audited. This is the one recommended Sketchfab download if account access is needed after approval. Verify included textures and license against the downloaded archive. |
| Discus CS appearance / 2 | Start from the higher-detail CC0 [Glider Flying by app4soft / EGPJET](https://opengameart.org/content/glider-flying), already used by the parked fleet. Prepare a 15 m, single-seat exterior against the club's [Discus CS reference](https://www.soarboulder.org/aircraft?catid=23&id=27%3An23sg-schempp-hirth-discus-cs&view=article). Correct planform, fuselage/canopy, tail, gear and tips where necessary. | The CC0 source is a generic modern sailplane, **not a verified Discus CS model**. Exact-type silhouette/dimension review is an asset-preparation gate. Do not rename the unchanged source to claim accuracy. |

The existing C172 derivative's recorded span is about 12.64 m, so normalize the new moving derivative against its actual chosen variant's dimensions rather than inheriting a parked-scene scaling assumption. Likewise, do not stretch a 15 m single-seat sailplane into a two-seat ASK-21. The club describes the ASK-21 as a 17 m two-seater, with a different outline. [ASK-21 reference](https://www.soarboulder.org/aircraft?catid=24&id=32%3An421ef-schleicher-ask-21&view=article).

If the CC0 glider needs extensive remodeling, complete one accurate, simple exterior using measured references, or acquire an exact-type source before visual acceptance. This asset gate is the main unresolved art effort; there is no verified ready-to-drop-in free Discus CS archive in the inspected project. Do not spend an open-ended search on it or silently substitute a low-quality placeholder in the final test.

ASK-21 is the first later variety addition, followed by a locally appropriate low-wing trainer or Pawnee. A [BlendSwap ASK-21-MI](https://blendswap.com/blend/18327) exists, but it is a motorized variant under CC BY-SA, with archived uploader attribution. It is a backup candidate requiring provenance, license-version and variant review, not an approved V1 asset. ShareAlike derivatives carry distribution obligations; keep that review explicit. [Creative Commons ShareAlike terms](https://creativecommons.org/licenses/by-sa/3.0/). A very high-poly marketplace Discus or a stylized/caricature glider is not a shortcut for this use case.

For every accepted derivative, record original URL, author, exact license, download date, source/output hashes, modifications and attribution; preserve Unity GUIDs. Keep raw downloads and unclear-rights assets external. Only reviewed optimized derivatives enter the product. Model preview availability is not archive/license verification.

## 3. Airport routes and believable animation

Author an `AirportAmbientTrafficProfile` offline in the existing KBDU ENU coordinate frame. It contains a small directed taxi graph, stand occupancy, hold-short boundaries, runway/approach protection volumes, airborne route segments, escape routes and model performance envelopes. Scene references and the five actor slots are serialized beneath an environment-owned `AmbientAircraftRoot`. Author both runway directions for the shared weather foundation; select the active direction from the airport's sampled wind and local calm-wind preference. Do not change direction while an aircraft is committed or the shared runway is occupied. Pause new admissions for a weather/runway transition, finish or divert existing operations, then admit traffic in the new direction.

### Ground route preparation

- Use verified empty stands with a clear photographic ground surface. Keep the existing 17/18 static-placement groups intact initially. Do not animate a mesh out of a combined parked batch or place a dynamic aircraft over an occupied/baked aircraft footprint.
- Trace actual taxi centerlines, turn radii, stop points and runway exits against the orthophoto, current mesh and city diagram. Check full wingspan/prop/tail envelopes, not just a point on a path. Hold-short positions must keep the whole aircraft clear.
- Bake wheel-contact height and surface normal from `IProductionTerrainSurface`; account for the runway slope, taxiway transitions and separate wheel contacts. Validate hangars, parked aircraft, fences, roads, vegetation and terrain clearance along every swept route. Do not rebuild the existing scenery to make an invalid taxi route fit.
- Permit one-way reservations on narrow segments. Never drive aircraft nose-to-nose into a segment without an available holding place or escape. A blocked path waits or takes a prevalidated alternative; it does not use grass as a generic shortcut.

### Powered-aircraft behavior

| Phase | Required visible behavior | Conflict response |
| --- | --- | --- |
| Parked / start | Occupy one legal stand; stationary prop before start, then natural spin-up. Brief preparation dwell. No invisible pushback machinery. | Remain parked if the player obstructs the stand's departure envelope. |
| Taxi / run-up | Smooth acceleration and braking; wheels turn with travelled distance; nose/tailwheel steers; slower tight turns; brief run-up at a verified bay/holding location. | Reserve taxi segments, stop before crossing traffic/player, wait without clipping wings. |
| Hold short / line up | Stop completely outside the protected strip; enter only after a full operation slot is secured. Short alignment before rolling. | Recheck player intent immediately before entry; cancel entry while still clear. |
| Takeoff | Progressive acceleration, plausible rotation and pitch/climb, appropriate flap state. Cub raises its tail before its takeoff posture; C172 remains tricycle gear. | A predicted conflict stops entry. If already rolling, use a type-specific stopping-distance/commit rule: reject only while stopping fits, otherwise use a prevalidated climb escape. Never reverse, freeze, or leap sideways. |
| Climb / circuit / departure | Continuous curved paths, local pattern direction, coherent bank and pitch, modest variation in timing/routes. | Extend spacing, choose a clear route, or depart; no close orbit around the player. |
| Arrival / final | Join the appropriate local pattern, slow gradually, set landing configuration, stable descent. | Re-evaluate occupancy before final commitment; hold outside the pattern, extend downwind when permitted, or go around early. |
| Flare / touchdown / rollout | Transition smoothly from descent to wheel contact; settle on the main gear, lower the nose, brake progressively and take an achievable exit. | Keep the runway group reserved until the full aircraft has cleared the exit boundary, not merely until touchdown. |
| Taxi in / shutdown | Taxi to a reserved empty stand, stop, prop spin-down, and a meaningful parked dwell. | Choose another valid stand or wait at a permitted location. Do not disappear on the runway/apron. |

Suggested starting animation ranges, **to tune and validate per model**, are 4–8 kt normal taxi, 2–4 kt tight turns, 15–25 degrees normal bank, and type-specific approach/climb speeds. These are game-animation starting points, not operational aircraft guidance. Set phase speeds, acceleration, pitch, climb and stopping envelopes using the selected variant's reference data before finalizing curves. Account for Boulder's elevation when converting indicated/reference airspeeds into credible ground motion; do not transplant sea-level takeoff timing.

Use distance-parameterized splines with baked arc-length tables and continuous tangents. In calm air derive bank approximately from `atan(v² × curvature / g)`, with bounded roll rate; in wind, derive attitude from the air-relative path while advancing the ground path with the sampled wind. Model heading/crab and ground track separately. Derive pitch from air-relative flight path plus a type/phase attitude offset. This is kinematic animation of an environment object, not a second flight-dynamics authority. It needs neither a Rigidbody flight model nor JSBSim instances for traffic. Reject/replan tracks requiring impossible airspeed, crosswind correction or climb; do not stretch path timing to conceal infeasible conditions.

## 4. Separation from the player and other traffic

One `AmbientTrafficDirector` owns operation reservations and precedence. It reads the native player state without changing controls, forces, pose, camera, calibration or aircraft authority. Traffic never requires the player to answer ATC or wait for a scripted clearance. Its inferred intent must accommodate free flight, demo/training routes, wrong-way approaches and an aircraft stopped on the runway.

### Runway group

Treat 08/26 and 08G/26G as one exclusive group for ambient takeoff, landing and crossing operations. Include both directions, low-final corridors and the initial departure conflict region. Author the glider strip's actual extent separately; a 197 ft centerline offset alone is not an adequate geometry survey.

Use a two-stage reservation: a future scheduling slot and a final immediate occupancy/trajectory check before entering. Predict the player's likely runway use from position, velocity, alignment, AGL, weight-on-wheels and proximity to hold-short/approach corridors. Expand the predicted time interval for uncertainty, including the sampled wind and gust envelope. Heading alone is insufficient: a pilot may stop, turn, perform a touch-and-go, approach the opposite end, crab into a crosswind, or go around.

V1 scheduling deliberately gives the player priority. Do not launch an ambient aircraft while the player is lined up, accelerating, on short final, rolling out, or likely to reach the conflict region before that operation fully clears. If the player stops on either strip, stop scheduling runway traffic and keep airborne traffic out through safe alternatives. Reservations expire only with verified clearance or a controlled reset; timers must not release a runway still occupied. Use hysteresis so intent changes do not cause repeated stop/start or go-around oscillation.

### Predictive separation

- Evaluate ownship-to-traffic and traffic-to-traffic closest approach, including wingspan/body extents and ground turning sweeps. Use the actor's actual future path and a bounded uncertainty corridor for the player, not center-point distance alone.
- Begin planning with a 30-second airborne lookahead, refreshed at 10 Hz near the airport/player; extend the reservation horizon to cover each full runway operation. A starting airborne avoidance trigger is approximately 250 m horizontal and 75 m vertical when both would be violated. Those are proposed game buffers, not regulatory separation minima, and must be increased if scenario evidence requires it.
- Ground stopping distance includes speed, reaction/update latency, braking limits and a margin around full aircraft outlines. Use continuous swept-volume checks each presentation frame for nearby actors, so a fast player cannot pass through between lower-rate decision updates.
- Prefer early speed/route changes, delayed departures and powered go-arounds over sudden avoidance. A glider can leave a thermal, glide away or make a gradual turn while altitude permits; it cannot invent power to escape.
- Continue conflict logic for relevant offscreen actors. A camera looking away does not release a runway or stop a taxiing aircraft's logical movement.
- Reserve/replan from the player's aircraft root, including actual banking and flight trajectory. The pilot looking sideways does not rotate the traffic system around their head.

**Important limit:** arbitrary last-instant player maneuvers or teleport/reset into an occupied location cannot always be reconciled with physically believable ambient motion. Normal encounters must prevent visible overlap using prediction. If an imminent swept-volume intersection remains unavoidable, suppress the offending ambient visual before intersection, record the failure, and retire/reseed it only when hidden. This exceptional removal is preferable to flying through the player or modifying player physics; it is not a normal culling or traffic-density mechanism. Any such event in normal scripted QA blocks acceptance. Do not promise both absolute collision prevention and perfectly continuous realism for every deliberate ramming/reset case.

On pause/menu/session reset, stop admissions, release stale reservations and reconcile all actor states before resuming. When native player state is unavailable/invalid, admit no new traffic and suppress uncertain moving encounters; do not guess a player position. Never add colliders that could push or destabilize the player as a substitute for separation.

## 5. Sailplanes near the foothills

Use two world-anchored soaring areas near the mapped eastern foothills, plus safe transit/exit corridors. Determine their exact coordinates and height envelopes from the existing terrain during authoring; no unsurveyed waypoint coordinates are invented in this plan. Keep them within available scenery coverage and away from terrain, towers, restricted low-altitude corridors and the airport arrival/departure path.

In the calm preset, use **thermal soaring**. In the foothill wind preset, allow **ridge soaring** only on the windward slopes where the shared field supplies enough lift to exceed the glider's sink rate. Use runs along the ridge with turns away from terrain; circling belongs in thermals, not automatically in a narrow ridge-lift band. Separate opposing ridge traffic into different segments or schedule a pass with adequate terrain-side clearance. Gliders drift with the air mass, lose height between lift areas and leave when the energy/clearance envelope no longer permits the route. Do not represent calm-air circling as ridge or wave lift, or reuse the northeast wildlife thermal as though it were in the mountains.

The club's Discus CS reference supplies useful limits: 15 m span, best glide around 54 kt and minimum sink around 0.59 m/s. Use a modest speed/sink envelope around appropriate reference conditions, rather than constant altitude and speed on every segment. Bank radius, climb and drift must agree visually. [Local Discus CS reference](https://www.soarboulder.org/aircraft?catid=23&id=27%3An23sg-schempp-hirth-discus-cs&view=article).

If both gliders share a thermal, they use the same circling direction, separated altitudes/phases, and predictable joins/exits. They do not fly opposite circles through one another. For fast V1 delivery, default to separate thermals and allow a shared thermal only after separation tests pass. Keep meaningful terrain clearance throughout a full turn, including the outer wing and a margin for sampled height uncertainty.

Place encounter routes so aircraft can naturally pass through front/side-window views at useful distances; do not tether them to the camera. Select admission time based on a safe opportunity, then let the flight proceed in world coordinates. If the player's route never approaches the foothills, it is acceptable not to see the mountain sailplanes. The standard demo's initial visible movement should be an airport aircraft instead.

### Shared wind foundation included in V1

Create one serialized `BoulderWindFieldProfile` and allocation-free sampler: world position plus simulation time returns a signed ENU wind vector, with diagnostic base-wind, thermal and terrain-flow contributions. Consumers sample the same function at their own positions; they do not all copy the wind at the player's aircraft. Its public sampling contract is independent of a C172, so a future native sailplane can reuse it.

| Layer | V1 design |
| --- | --- |
| Base wind and height variation | Authored horizontal vector/height knots, smooth interpolation, near-ground attenuation and explicit MSL/AGL conversion. Store vectors internally; convert meteorological wind-from direction once at the boundary. |
| Thermal contribution | Preserve the accepted thermal source and add selected foothill thermals as shared physical fields. Smoothly advect/tilt thermal columns with the preset where appropriate. Compose once; do not add the old updraft a second time. |
| Ridge flow | Offline-precompute a coarse, multiscale terrain response for each supported weather preset, including exposure/upwind sheltering. Near an exposed slope, signed `horizontalWind dot terrainGradient` is an initial kinematic deflection estimate, with bounded influence/height falloff and smooth spatial blending. Include lee-side sink; never clamp all downward flow away. Inspect the resulting cross-sections and tune against reference behavior. This is an approximate terrain-flow model, not resolved airflow around every cliff. |
| Gusts | Small, bounded, repeatable, time-correlated vector variations shared across nearby locations, rather than frame-random shaking or camera motion. Keep severe turbulence out of the first preset. Do not also enable a second independent native gust source over the same field. |

The FAA distinguishes windward slope lift, lee downdrafts and mountain waves requiring suitable wind/stability profiles; the phenomena cannot be reproduced by adding upward velocity everywhere near a mountain. This motivates separate ridge and wave milestones. [FAA soaring-weather chapter](https://www.faa.gov/regulations_policies/handbooks_manuals/aviation/glider_handbook/gfh_chapter_9.pdf).

The first presets are **Calm / existing thermal** and **Foothill soaring**. The latter is an authored moderate easterly/upslope scenario, suitable for investigating lift on exposed east-facing foothill slopes; its exact speeds and vertical profile are set from native handling and terrain-flow evidence. It is not a claim about today's weather or Boulder's prevailing wind. A westerly wind would put many of those same faces in the lee, so do not simply reverse the airport windsock and retain the same lift zones. Keep preset selection explicit; preserve calm for existing lessons and establish a separate wind-enabled demonstration. Do not silently make all current training flights windy.

Native integration is limited to sampling immediately before each existing fixed JSBSim step and passing the resulting vector through `SetAtmosphere`. ENU `(east, up, north)` maps to native `(north, east, down)` as `(z, x, -y)`, with the existing meters/feet conversion applied once. Replace the current `Math.Max(0, updraft)` behavior for the new composed field so downdrafts reach native physics. Keep native JSBSim as the sole flight authority: no `AddForce`, direct pose changes, extra pilot physics integrator, or camera shake. Validate finite values and bounded rates before crossing the native boundary; invalid authored data must stop the wind-enabled scenario rather than silently claiming calm is the selected weather. The existing bridge already exposes these wind properties. [JSBSim wind interface](https://jsbsim-team.github.io/jsbsim/classJSBSim_1_1FGWinds.html).

Ambient gliders use a lightweight sink-versus-speed/bank envelope and `vertical wind - sink` to advance their height; waypoint animation cannot force a climb the common field cannot support. This envelope is only for believable traffic. It must not become the future player's flight model. Read-only diagnostic probes at the same position/time must agree across the native adapter, traffic and wildlife. Adapt the existing eagle to the same field without changing its accepted encounter unnecessarily.

Make the wind visible and consistent: the authored windsock rotates downwind and changes extension with local surface speed; taxi/takeoff/landing animations use the same runway wind; airborne heading and track diverge appropriately. Audit existing cloud motion and align the current fixed-count cloud presentation with the sampled wind aloft where needed, including its inside-cloud/culling data. Preserve cloud appearance, volume count and sampling budgets; do not use this task to add a new cloud renderer or assume all altitudes have the same wind. Stationary orographic cloud behavior belongs with a later wave preset, not arbitrary scenery drift.

The field adds **no terrain-sized GPU simulation or online weather dependency**. Precompute static spatial responses offline; use compact data and constant-bounded sampling at runtime. Proposed additional targets: at most 4 MiB wind data, 0.1 ms mean main-thread sampling/composition across player and traffic, and no new atmospheric rendering passes. These are ceilings to measure, not established savings. With aircraft, the combined new-data target is 20 MiB and mean CPU target is 0.3 ms; keep the combined delivered-frame acceptance limits in section 6.

### Native validation and future sailplane boundary

This changes real player handling and therefore requires physics scenario evidence, even though the ambient aircraft remain animated. Extend the existing native-only scenario harness, whose current wind result is explicitly unproven:

- Pin equivalent trimmed calm/headwind/tailwind/crosswind reference runs; record wind properties, true/calibrated airspeed, ground track, ground speed and control response. Test east/west/north/south and upward/downward signs, units and altitude reference.
- Show native C172 power-off flight traversing positive lift, neutral air and lee sink with the expected relative energy/vertical-speed response. This validates wind coupling; it does not establish a sailplane polar or handling fidelity.
- Test flat terrain, wind parallel to a ridge, zero wind, exposed/sheltered slopes, height falloff, interpolation boundaries and opposite wind directions. Inspect lift/sink cross-sections over selected real terrain. Smoothness must prevent discontinuous accelerations at cells/preset boundaries.
- Run wind-enabled ground roll, takeoff, landing/go-around, taxi steering and the existing native scenario suite; verify the demo pilot can follow the selected wind-enabled route. Preserve calm lesson behavior. A wind test that only checks for a finite number or visible drift is insufficient.
- Verify sampler agreement, deterministic time/pause/reset behavior, no doubled thermal/gust contribution, and graceful refusal of infeasible ambient routes. Weather switches occur at a controlled scenario boundary or via a fully tested transition, not an instantaneous in-flight vector jump.

The later **mountain-wave milestone** adds a defensible vertical stability/wind profile, terrain-anchored downstream lift/sink bands, and the associated rotor/turbulence model with native response evidence. It must be validated against mountain cross-sections, not represented as random noise over the ridge formula. The later **flyable-sailplane milestone** needs a reviewed native JSBSim airframe with measured sink polar, energy handling, ballast/CG, spoilers, gear and launch/recovery behavior, plus an authored cockpit/rig and controls. Changing selected aircraft still leaves exactly one native flight authority. Neither milestone is claimed complete by an attractive ambient glider or a successful C172 wind test.

## 6. Quest 3 implementation budget

All numbers in this section are **proposed limits to verify**, not performance already achieved. Geometry/draw counts alone cannot prove a millisecond budget.

| Item | Initial target / ceiling |
| --- | --- |
| Population | Five persistent ambient aircraft; no extra actors spawned to replace an offscreen one. |
| Mesh detail per aircraft | LOD0 at most 4,000 triangles including visible movable parts; LOD1 about 800–1,200; far 3D silhouette about 150–300. |
| Total visible new geometry | At most 22,000 triangles, including bounded prop/light/contact-shadow geometry. Typical airborne views should be substantially lower. |
| Materials and draws | One shared opaque exterior material/atlas family where practical; small bounded prop/light exceptions. Target at most 20 additional submissions in the actual XR mode including auxiliary passes; report measured per-eye/multiview accounting. |
| Added resident memory | Traffic target at most 16 MiB for optimized meshes, textures/mips and behavior data, plus at most 4 MiB wind data. Strip unused interiors/source textures and disable unnecessary mesh read/write copies. |
| Behavior cost | Traffic target at most 0.2 ms mean / 0.3 ms p95 main-thread work, plus wind target 0.1 ms mean, with no steady-state GC allocations. Render transforms interpolate every frame; central decisions are staggered. |
| Added rendering cost | Target at most 0.3 ms GPU when an actual GPU duration counter is available. Otherwise report delivered cadence separately and leave isolated GPU cost unproven. |
| End-to-end comparison | Same route/view/settings: target mean delivered-frame increase no more than 0.3 ms and p95 no more than 0.5 ms, subject to run variability. A noisy/inconclusive test is not a pass. |

Implementation choices:

- Use ordinary serialized MeshRenderers and rigid moving parts; enable compatible instancing for shared meshes/materials. Five actors do not justify a DOTS/ECS, custom indirect renderer or crowd-simulation rewrite. Unity notes that instancing also has collection/upload overhead; verify whether it helps this small population. [Unity 6.3 instancing](https://docs.unity3d.com/6000.3/Documentation/Manual/GPUInstancing.html).
- Retain aircraft silhouette at distance with low-cost 3D geometry. Choose LOD from projected size with hysteresis, using the more demanding eye so both eyes switch together. Do not reuse the trees' distance limit or a vertical-height metric that prematurely culls a thin, wide glider. Start evaluating silhouette visibility across roughly 1–5 km; derive final range from projection, haze and actual Quest visibility. [Unity LOD controls](https://docs.unity3d.com/6000.3/Documentation/Manual/class-LODGroup.html).
- Use normal stereo frustum/depth handling; no aggressive aircraft-heading/cardioid cutoff for aircraft the pilot may turn to observe. Do not build cockpit-window occlusion masks to save a few tiny aircraft draws.
- Culling affects drawing and fine animation, not identity or essential movement/separation. Far actors can use 1 Hz planning with analytical path progress; runway/near actors retain 10 Hz decisions and per-frame near-conflict checks. Reentering view reconstructs the continuous current pose, not the last visible pose.
- Do not recycle an aircraft in either eye's view. Prefer persistent parked state or a distant route exit. No hard population-budget eviction of a conspicuous aircraft, transparent billboard switch, or enlarged distant marker pretending to be geometry.
- Use mipmapped ASTC textures, restrained albedo/specular detail, existing sun/ambient illumination and existing baked environment reflection where appropriate. No per-aircraft realtime reflection capture, realtime shadow maps, light volumes, trails or contrails. A bounded contact shadow for the closest one or two ground aircraft can ground their appearance using the existing runway-shadow approach after compatibility review.
- Prop blur is a small bounded disc, with opaque blades at stopped/slow RPM and no depth conflict with the fuselage. Use near-only simple emissive aircraft lights; they are not realtime scene lights. Keep AA/MSAA, High FFR and accepted foliage distances unchanged.
- At most two audible nearby powered actors, with distance attenuation and cockpit muffling, using only appropriately licensed exterior recordings. Existing interior C172 recordings are not automatically appropriate for a passing Cub. Audio is polish after movement/separation; missing suitable sound must not hold up the visible motion proof.

If cost exceeds the limits, first reduce small-part detail, unnecessary passes, audio voices and distant decision frequency; then lower new traffic admission/density while preserving visible actors until a natural exit. Do not shorten accepted canopy range, blur the cockpit or retest the FFR ladder to finance this feature.

## 7. Later extension: visible tow and glider airport cycles

These behaviors are designed here so V1 does not paint itself into a corner. They are **not included in the first implementation approval unless explicitly added to scope**:

1. Ground handler/tug brings a glider from a real staging area to the launch point. A glider does not propel itself. Show a credible wing support/runner phase if that phase is visible; ground hardware and crew have their own bounded asset/cost review.
2. Reserve the combined runway group for the entire tow launch. Treat towplane, rope and glider as a linked formation with a combined separation envelope. Simulate the rope with a short constrained visual curve, not full rope physics; never let it cut through either aircraft or the player.
3. Glider lifts off and holds the appropriate tow position; towplane rotation/climb follow. Turn and climb limits apply to the whole pair. Release at a planned altitude/location, animate a clear separation, and return the Cub via the locally appropriate route. Do not omit the rope while close enough to see it.
4. Returning gliders join the local modified pattern with enough altitude and a reserved landing/retrieval opportunity. Show spoilers, flare, main-wheel contact, deceleration and a wing settling naturally. Reserve the group through actual clearance/retrieval. They cannot power-go-around or magically taxi off the strip.
5. Before committing to final, an obstructed glider remains in a feasible airborne route only while its altitude/sink envelope permits, or uses a genuinely authored reachable alternate. Never prolong an unpowered hold indefinitely. A late unavoidable player incursion uses the same exceptional suppression rule as V1; it must not create a simultaneous landing on the parallel strip.

This extension adds real value to Boulder's identity, but its ground handling and non-go-around cases require more authoring than the first visible aircraft pass. Do not mark it complete merely because a Cub and glider are flying near each other.

## 8. Implementation order and acceptance

One proposed V1 milestone, to develop mostly without headset participation after approval:

1. **Establish the shared wind field and native evidence.** Preserve the calm reference, add the moderate foothill preset, compose terrain/thermal/signed wind through the existing native interface, and validate actual native response. Make the windsock consistent. Complete this bounded foundation before finalizing wind-dependent routes; do not turn it into the future mountain-wave or sailplane-airframe project.
2. **Prepare the three exteriors and routes.** Audit the optional PA-18 archive; recover the pinned higher-detail sailplane source; finish the Discus appearance gate. Build reusable LODs, pivots, materials and source records. Author a small taxi graph, both powered runway patterns, foothill thermal/ridge/glide routes and their clearance/escape envelopes. Produce normal pilot-seat captures at taxi, circuit and mountain distances. This proves recognizable assets and useful placement before behavior complexity grows.
3. **Implement complete C172 traffic and separation.** Add the serialized director/actors and native-state read-only adapter. Complete one taxi/takeoff/arrival/parking loop, conflict predictions, runway-group reservations and reset behavior. Reuse that behavior with the Cub's type-specific animation profile. Add the second C172's paint and staggered schedule; do not clone an entire flight rig.
4. **Add the two sailplanes and visibility policy.** Implement wind-consistent thermal/ridge/glide/exit behavior, nearby conflict response, continuous offscreen progress and matching stereo LOD. Use distinct modest paint details on the same accurate mesh. Initial conditions can place them already airborne outside the initial field of view; no visible spawning beside the pilot.
5. **Run autonomous behavior and visual validation.** Exercise the cases below in both supported presets, inspect temporal clips and both eyes from valid pilot views, run standard Visual QA, native physics scenarios and required regression/build checks, and verify the final APK/source identity. Preserve old failures separately; no simulator or timing claim should conceal them.
6. **Perform one prepared final headset session.** Only after visible movement and wind response are proven offline, install the exact candidate and guide a short airport/circuit/foothill route with clear directions about where the aircraft will be. Collect one same-route traffic-off/on diagnostic while keeping the weather preset, scenery and FFR fixed; measure wind CPU separately. It is a performance toggle, not a demand for another user side-by-side visual comparison. Use actual GPU timing only if available, record cadence/tails/thermal state, and stop if the measurements are inconclusive rather than claiming headroom. Broader sustained shipping acceptance remains a separate gate.
7. **Checkpoint the accepted feature.** Update evidence and the unified plan, retain reversible authored traffic/weather settings, commit reviewed assets/code and normal source/license records. Promote only the validated scope; incomplete tow/ground-handling/wave work stays outside the product closure.

The existing simulator checkpoint has unresolved replay/stereo foliage evidence and only airport-area aerial coverage, approximately +/-4 km expanded to bundle boundaries. Resolve the relevant stereo-capture issue and prepare the specific foothill capture coverage needed for these routes. Do not infer mountain visual acceptance from the current airport-only simulator build, and do not restart a full-map scenery recapture campaign. Preserve the known mountain-activation probe issue separately from actual mesh/transform or traffic failures.

### Required scenario evidence

| Scenario | Passing result |
| --- | --- |
| Player sits on either runway or a hold-short line | No conflicting ambient entry/takeoff/landing; traffic waits or takes a permitted airborne alternative. |
| Player short final from either end, including unexpected runway use | Scheduler protects the actual trajectory; ambient direction follows the selected local wind with calm-wind 08 preference. |
| Player touch-and-go, rejected takeoff, early/late go-around | Reservations persist/replan correctly; no landing into the player's rollout or departure path. |
| Player crosses a taxi route, reverses, stops or accelerates abruptly | Full aircraft outlines remain separate; stopping is natural and no taxi deadlock is created. |
| Two ambient ground actors seek the same segment/stand | One waits at a legal point; no nose-to-nose deadlock or overlapping parking. |
| Player approaches a circuit aircraft or glider head-on/from behind | Early plausible separation with continuous animation; no normal-case emergency removal. |
| Player flies into a thermal or banks past a sailplane | Glider can leave smoothly within speed/turn/altitude limits; both-eye silhouettes remain stable. |
| Wind direction changes, ridge has insufficient lift, or native aircraft enters lee sink | Traffic changes runway only after safe clearance; gliders descend/exit naturally; native response follows the same signed field. |
| User turns away and back / pauses / resets / changes flight mode | No positional jump, duplicate aircraft, stale runway reservation or modification of the native flight authority/XR rig. |
| Deliberate instantaneous overlap or invalid native state | Documented fail-closed suppression, no collision impulse or pilot-pose intervention; logged distinctly from successful normal avoidance. |
| Normal flights across all authored routes and schedule seeds | Zero intersections with terrain/buildings/static traffic/player, zero shared-runway reservation overlap, no startup-only or sub-aircraft visibility trap. |
| Closest valid pilot view and worst population clustering | Cost limits checked with materials/parts/shadows included; traffic remains identifiable without obvious low-poly breakup or popping. |

Use deterministic accelerated route/separation tests for broad combinations and real-time pilot-view clips for animation quality. Include randomized player perturbations around holds, final-approach and close-pass cases. Inspect aircraft visibility in the front and side windows; debug overhead diagrams may verify routing, but cannot substitute for the pilot's actual view. Require an unambiguous moving-aircraft encounter within the first minute of a prepared demonstration when safe; if the player blocks the airport, safety takes precedence over the demonstration timer.

Implementation is approved and underway. Powered airport movements are serialized one complete cycle at a time, with fair rotation among the three powered slots; sailplanes remain independent. This deliberately quiet schedule avoids opposing taxi contention while retaining the full roster. See the implementation checkpoint for measured results and remaining gates.
