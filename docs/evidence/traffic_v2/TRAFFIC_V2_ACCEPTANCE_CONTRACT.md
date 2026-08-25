# Traffic System V2 acceptance-method contract

- Review date: 2026-08-21
- Independent validation owner: Workstream D
- Normative specification: `tools/traffic_v2/schemas/traffic_system_v2_spec.json`
- Specification revision: `2026-08-21.1`
- Specification status: `FROZEN`
- Specification SHA-256: `8e1b3f7442652128b306a0706d3eac02b59750158839ca0443351312fcbf09c7`
- Accepted comparison source: `fda11a25eb526824d82a47330f2be80b1410bb64`
- Method decision: **APPROVED, 23 of 23 methods**
- Evidence decision: **NOT PASSED, 0 of 23 requirements evaluated**

This document records Workstream D's approval of the validation methods in the
exact frozen specification byte stream identified above. D rechecked the final
status-only freeze; it did not change a requirement, method, or threshold. This
document does not approve an
implementation, candidate data, an APK, a visual result, Quest performance, or
any of the 23 acceptance requirements. Any byte change to the specification
invalidates these method approvals until D records the new SHA-256 and performs
a new review.

The method-approval prerequisite for implementation is satisfied only for the
specification hash above. Every evidence gate remains fail-closed. At review
time, the clean recovery worktree contained the shared specification but no
Traffic V2 source freeze, candidate, independent validator suite, benchmark
capture set, post-baseline budget, exact APK, or physical Quest result.

## Decision semantics

- **METHOD APPROVED** means the specification identifies a user-visible
  objective, independent source of truth, implementation owner, validation
  method, measurable pass threshold, required artifact, and fail-closed
  response sufficient for D to build and run an independent test.
- **EVIDENCE PASS** may be recorded only after every required artifact exists,
  D reproduces every machine result from pinned inputs, and the lead inspects
  every required full-resolution image. A method approval never implies an
  evidence pass.
- Missing source inventories, authority records, frozen distributions,
  validation tools, benchmark traces, imagery, an exact APK, or physical
  hardware results are prerequisites. Their absence is `NOT RUN` or
  `BLOCKED_PREREQUISITE`, never `PASS`.
- A failure stops at the first distinguishable contract violation. A later
  aggregate result, visual impression, or performance result cannot override an
  earlier failed gate.
- The rejected dirty Traffic V2 candidate and its validator output are forensic
  inputs only. They are not reusable acceptance evidence.

## Binding execution rules

These rules remove execution ambiguity without changing the shared
specification:

1. Every value described as `source-derived`, `documented`, `frozen`,
   `authority`, `target`, `band`, `confidence interval`, `uncertainty`,
   `profile bound`, `reservation`, or `benchmark trace` must exist in a hashed,
   provenance-bearing input before the candidate-dependent validation run. D
   must reject a value selected or adjusted after inspecting candidate results.
2. D acquires and processes the authoritative inputs independently. D may parse
   implementation outputs, but it may not use the implementation compiler,
   routing logic, collision result, visibility count, or LOD decision as its
   expected answer.
3. Every run manifest records the specification hash, source hashes, candidate
   Git commit, dirty-state result, Unity and package versions, validation-tool
   hashes, exact command line, deterministic seed, numeric tolerances, sampling
   cadence, time range, benchmark-trace hash, and output hashes.
4. `Continuous`, `adaptive`, and `sampled` checks must publish their error bound
   and witness spacing before execution. The explicit 0.10 m, 0.50 m, 0.25 m,
   0.05 s, and 0.20 s limits in the specification cannot be weakened by a tool
   default.
5. For TV2-COL-015, `all resident cells and relevant times` means the complete
   `max(1800 seconds, two schedule periods)` collision horizon plus initial
   load, schedule seam, residency entry/exit, LOD transition, and pool-pressure
   events. For percentage-of-frame thresholds, the denominator is the complete
   frozen benchmark trace; the run may not discard blank, failed, warm-up, or
   inconvenient frames.
6. A source or candidate update requires a new complete affected-gate run. A
   partial rerun cannot inherit results across different input hashes.
7. A machine image score is necessary but not sufficient. The lead inspects the
   original full-resolution images, not only contact sheets or thumbnails. D
   separately verifies the machine result. Where stereo is required, both eye
   images and the stereo presentation are inspected.
8. Meta XR Simulator proves simulator stereo and visual behavior only. It cannot
   prove Quest frame timing, thermals, stale frames, tears, comfort, or worn
   quality. Those claims require the exact immutable APK on a physical Quest 3.

## Gate sequence

1. **G0 - Frozen specification and method approval only.** Verify the frozen
   specification SHA-256, this contract, and 23 of 23 D method approvals. G0
   does not acquire or freeze source authority, capture a candidate result, or
   authorize candidate implementation against a different specification hash.
2. **G1 - Accepted baseline capture and aggregate static-budget freeze.** At
   exact accepted source `fda11a2`, capture the truthful aggregate product draw,
   triangle, material, memory, and APK-size baseline and freeze the allowed
   post-baseline ceilings/deltas. Record existing failures as failures; do not
   invent headroom, relabel aggregate cost as traffic-only cost, or move a
   ceiling after later results are known. **G1 must complete before any new
   Traffic V2 source acquisition or candidate work.**
3. **G2 - Source authority freeze and geospatial lane graph.** Independently
   acquire, license, hash, and freeze road, lane, surface, exclusion, bridge,
   control, count, fleet, speed, headway, vehicle-bound, benchmark-camera, and
   accepted-product authorities and all source-derived bands. Then run
   TV2-COV-001 through TV2-TURN-007. Demand, runtime, imagery, or performance
   cannot excuse an authority, direction, containment, exclusion, grade, or
   turn failure.
4. **G3 - Slice schedules, continuous safety, and Unity machine runtime.** On
   the bounded representative slice, run TV2-INT-008 through TV2-COL-015 and the
   authored portion of TV2-LOOP-017 over the complete required horizon. In the
   clean worktree, validate runtime-decoded TV2-COL-015 and the slice forms of
   TV2-ID-016 through TV2-VIS-021. Unity EditMode/PlayMode, product integrity,
   dependency closure, and frozen-budget preflight are mandatory. Runtime work
   may not hide or repair an authored failure.
5. **G4 - First immutable slice APK and focused physical-headset gate.** Build
   the first Android ARM64, Vulkan-only, OpenXR slice APK, record its SHA-256,
   and run the focused physical Quest 3 timing, transition, thermal, and worn
   visual gate against that exact APK. Preserve the complete machine evidence
   and Alex's explicit acceptance record. **This is a hard stop: no full-map
   expansion may begin before Alex accepts the focused slice on-headset.** G4 is
   a slice-promotion gate and does not substitute for the final full-map
   TV2-PERF-022 evidence in G6.
6. **G5 - Full-map expansion and complete preflight.** Only after G4 acceptance,
   expand to the full frozen map and rerun every affected full-map source,
   schedule, collision, Unity/runtime, identity, LOD, capacity, and pilot-view
   machine gate. Run standard production Visual QA and genuine frozen Meta XR
   Simulator stereo replays, with lead/D inspection of original full-resolution
   evidence. Run complete static and performance preflight against the G1
   ceilings. Live simulator smoke, static cost, and Windows timing are not Quest
   performance evidence.
7. **G6 - Final immutable APK and full physical Quest gate.** Build the final
   full-map Android ARM64, Vulkan-only, OpenXR production APK and record its
   distinct SHA-256. Run TV2-PERF-022 three times on a physical Quest 3 with the
   specified 90-second warm-up and 60-second traffic-stress windows, plus the
   required final worn visual inspection. G4 slice evidence cannot transfer to
   a different G6 APK hash, and simulator timing is inadmissible.
8. **G7 - Final regression.** Run TV2-REG-023 and the complete product gate only
   after G6 passes. D may recommend acceptance only when every requirement has
   its own reproduced evidence pass and all required lead image/headset
   inspections are recorded.

## Evidence storage and commit policy

- Raw downloads, source archives, caches, Unity `Library`, `Temp`, `Obj`, logs,
  builds, APKs, simulator captures, physical-headset captures, and generated
  contact sheets remain outside Git under an immutable run directory such as
  `<external-artifact-root>/traffic_v2/<run-id>/`.
- Each external run contains a manifest with relative path, byte length,
  SHA-256, producer command, source/spec/candidate hashes, capture metadata, and
  inspection status. D must be able to reproduce each machine artifact from
  the recorded command and pinned inputs.
- Small text reports, JSON/CSV summaries, provenance records, and hash indexes
  may be committed in `docs/evidence/traffic_v2/` only when authorized. A PNG or
  capture name in the specification denotes a required external artifact plus
  its committed hash/inspection record; it does not authorize committing the
  binary.
- Every committed binary asset still requires a source and license record. No
  unclear-license input may participate in a passing product dependency
  closure.

## No-counts-as-proof rules

- A route, edge, lane-kilometer, vehicle, sample, test, image, or draw count is
  an inventory only. It cannot prove that the individual members are correct.
- Global vehicle totals cannot prove coverage, calibrated demand, or visibility
  from a pilot camera. Passing TV2-VIS-021 requires per-frame projected,
  unoccluded viewport evidence and lead/D image approval.
- A closed endpoint count cannot prove a legal, tangent-continuous schedule
  seam. A zero same-route collision count cannot prove cross-route safety.
- A nonblank image or contact sheet cannot prove direction, containment,
  identity, temporal stability, or stereo agreement.
- Unity test totals, an Android build, installation, launch, static render
  budgets, and simulator timing cannot prove physical Quest performance.
- No aggregate pass may be inferred when an offender list, missing-source list,
  rejected dependency, unknown dependency, missing frame, missing eye, or
  missing hardware interval is nonempty.

## Per-requirement method decisions

### TV2-COV-001 - full-map eligible-road coverage

- **Method decision:** **APPROVED**. It establishes an independent denominator,
  evaluates the full frozen square by class and benchmark viewport, and requires
  evidence for every exclusion rather than allowing synthetic roads or a
  City-only numerator.
- **User-visible objective:** Eligible roads throughout the flyable map carry
  plausible traffic, not an airport- or City-only patch.
- **Source of truth:** Independently acquired source inventories in the frozen
  24 km square; source precedence plus explicit eligibility/exclusion rules;
  hashed benchmark-camera road inventory.
- **Implementation owner:** A. **Validation owner:** D.
- **Validation method:** Independently reproject and classify source centerlines,
  intersect lane geometry with the frozen bounds, and compare represented
  lane-kilometers by road class and benchmark viewport.
- **Pass threshold:** 100% of eligible motorway, trunk, primary, and secondary
  lane-kilometers; at least 95% of eligible tertiary and residential
  lane-kilometers; 100% of benchmark-visible roads classified; 100% of the
  +/-12 km bounds evaluated; every exclusion evidence-backed.
- **Evidence artifacts:** `traffic_source_coverage.json`,
  `traffic_missing_roads.geojson`, `traffic_source_coverage.png`, and
  `traffic_source_provenance.md`.
- **Failure response:** Stop source compilation; resolve the authority gap or
  evidence-backed exclusion. Never synthesize a road to satisfy coverage.
- **Current evidence state:** **NOT RUN**. The independent frozen road inventory,
  eligibility ledger, and benchmark-road inventory are prerequisites.

### TV2-LANE-002 - correct directed lanes

- **Method decision:** **APPROVED**. Exhaustive source-edge comparison plus
  trajectory-heading witnesses tests both graph direction and the motion that
  the pilot will see; unresolved pilot-visible conflicts fail closed.
- **User-visible objective:** Every moving vehicle travels in the lawful lane
  direction.
- **Source of truth:** Authoritative one-way/lane-direction attributes;
  orthophoto review for unresolved markings; directed lane multigraph.
- **Implementation owner:** A. **Validation owner:** D.
- **Validation method:** Independently reconstruct direction from source
  attributes and compare every directed edge and sampled trajectory heading to
  its permitted lane direction. Witness spacing and tangent error are frozen in
  the run manifest before candidate evaluation.
- **Pass threshold:** Zero wrong-way directed edges; zero wrong-way trajectory
  samples; zero unresolved pilot-visible direction conflicts.
- **Evidence artifacts:** `lane_direction_map.png` and
  `lane_graph_validation.json`.
- **Failure response:** Reject the edge and all dependent trajectories until
  source authority corrects the direction.
- **Current evidence state:** **NOT RUN**. Directed-lane authority and D's
  independent comparison tool are prerequisites.

### TV2-LANE-003 - correct divided-highway carriageways

- **Method decision:** **APPROVED**. It validates the complete footprint against
  independently reconstructed carriageways and medians, including illegal
  merges rather than relying on centerline direction alone.
- **User-visible objective:** Opposing highway traffic remains on distinct,
  correctly oriented carriageways.
- **Source of truth:** CDOT highway geometry; source carriageway/median geometry;
  orthophoto ambiguity review.
- **Implementation owner:** A. **Validation owner:** D.
- **Validation method:** Audit every divided-road lane against the reconstructed
  carriageway polygon and median barrier, sweeping the full vehicle footprint
  over every highway trajectory.
- **Pass threshold:** Zero wrong-carriageway edges; zero artificial median
  crossings; zero carriageway merges without a source-backed legal junction.
- **Evidence artifacts:** `lane_direction_map.png` and
  `trajectory_containment_report.json`.
- **Failure response:** Remove the invalid movement and recompile carriageway
  geometry before routing resumes.
- **Current evidence state:** **NOT RUN**. Pinned CDOT/carriageway authority and
  independent footprint sweeps are prerequisites.

### TV2-GEO-004 - road-surface containment

- **Method decision:** **APPROVED**. Adaptive continuous swept volume, a strict
  final-set requirement, explicit 0.10 m/0.50 m witnesses, and visible-join
  checks prevent a sparse center-point test from passing off-road vehicles.
- **User-visible objective:** Complete vehicle footprints remain on paved
  traffic surfaces through lanes and turns.
- **Source of truth:** Road/intersection surface polygons; vehicle-specific
  oriented and articulated bounds; continuous authored trajectories.
- **Implementation owner:** A+B. **Validation owner:** D.
- **Validation method:** Independently perform adaptive continuous swept-volume
  subdivision bounded by frozen displacement/heading error and intersect the
  result with legal drivable surface.
- **Pass threshold:** At least 99.5% continuous containment at the lane-graph
  gate; 100% for the final trajectory set and every pilot-visible movement;
  zero witness more than 0.10 m outside at no more than 0.5 m spacing; zero
  unclosed visible joins.
- **Evidence artifacts:** `lane_graph_validation.json` and
  `trajectory_containment_report.json`.
- **Failure response:** Reject and regenerate the lane or trajectory; culling
  may not hide the violation.
- **Current evidence state:** **NOT RUN**. Surface authority, vehicle/articulated
  bounds, and a pre-frozen adaptive-error contract are prerequisites.

### TV2-GEO-005 - building/water/median exclusion

- **Method decision:** **APPROVED**. Independently rebuilt, conservatively
  buffered masks and continuous rigid/articulated sweeps test the full volume
  against every excluded surface class.
- **User-visible objective:** Vehicles never clip buildings, water, or medians.
- **Source of truth:** Accepted product exclusion masks; source-backed road
  surfaces; vehicle swept volumes.
- **Implementation owner:** A+B. **Validation owner:** D.
- **Validation method:** Independently rebuild exclusion polygons, freeze each
  source-derived uncertainty buffer before candidate evaluation, and
  continuously intersect every rigid and articulated swept volume.
- **Pass threshold:** Zero building, water, or median intrusions.
- **Evidence artifacts:** `trajectory_containment_report.json` and
  `trajectory_conflict_heatmap.png`.
- **Failure response:** Reject dependent lanes and trajectories until authority
  or geometry is corrected.
- **Current evidence state:** **NOT RUN**. Accepted mask hashes and documented
  source-uncertainty buffers are prerequisites.

### TV2-GEO-006 - bridge and grade-separation correctness

- **Method decision:** **APPROVED**. The method requires explicit topology and
  elevation authority for every structure, checks a numeric deck tolerance, and
  distinguishes geometric crossings from same-grade conflicts.
- **User-visible objective:** Vehicles follow bridge decks without terrain or
  different-grade traffic collisions.
- **Source of truth:** Bridge/layer attributes; deck/roadway elevation evidence;
  accepted bare-earth terrain.
- **Implementation owner:** A. **Validation owner:** D.
- **Validation method:** Audit every crossing for layer assignment, compare road
  elevation with deck authority rather than bare earth, and suppress conflict
  zones only between verified separated layers.
- **Pass threshold:** Explicit layer/elevation authority for 100% of bridge and
  grade-separated edges; zero bare-earth deck snapping; absolute deck error at
  most 0.25 m; zero reservations between separated lanes; zero terrain or
  ground-clearance penetrations.
- **Evidence artifacts:** `lane_graph_validation.json` and
  `trajectory_containment_report.json`.
- **Failure response:** Exclude unresolved structures and stop the source gate
  when one is pilot-visible or required for major-road continuity.
- **Current evidence state:** **NOT RUN**. Pinned bridge/layer/deck authority is a
  prerequisite; omission is not a pass.

### TV2-TURN-007 - legal turn movements

- **Method decision:** **APPROVED**. It exhaustively derives allowed inbound and
  outbound pairs and separately requires physical containment for every vehicle
  class assigned to a turn.
- **User-visible objective:** Vehicles make only legal, physically contained
  turns.
- **Source of truth:** Turn restrictions/lane-use attributes; intersection
  topology/drivable polygons; vehicle-size turn envelopes.
- **Implementation owner:** A+B. **Validation owner:** D.
- **Validation method:** Enumerate every authored turn arc, independently derive
  permitted pairs, and continuously sweep every supported vehicle class through
  its turn polygon.
- **Pass threshold:** Zero illegal turns; zero artificial U-turn closures or
  straight-line joins; 100% containment for every assigned vehicle class.
- **Evidence artifacts:** `intersection_turn_map.png` and
  `lane_graph_validation.json`.
- **Failure response:** Remove the movement and every path using it until legal
  topology and containment both pass.
- **Current evidence state:** **NOT RUN**. Frozen turn/lane-use authority and
  vehicle-class envelopes are prerequisites.

### TV2-INT-008 - intersection right-of-way

- **Method decision:** **APPROVED**. Full-schedule independent reservation,
  control, dwell, phase, queue, and temporal-clearance checks directly test
  right-of-way rather than inferring it from collision totals.
- **User-visible objective:** Signals, stops, yields, queues, and releases behave
  coherently without conflicts.
- **Source of truth:** Signal/control records; precompiled phase plans;
  intersection conflict-zone reservations.
- **Implementation owner:** B. **Validation owner:** D.
- **Validation method:** Replay the complete schedule through D's reservation
  checker and verify controlled approaches, dwell, phase compatibility, queue
  order, and smooth entry/exit speeds.
- **Pass threshold:** Zero overlapping conflict reservations; zero movement on a
  conflicting red; 100% of stop movements meet authored dwell; 100% of yield
  movements meet required temporal clearance.
- **Evidence artifacts:** `trajectory_collision_report.json` and
  `traffic_slice_intersection_report.json`.
- **Failure response:** Reject at the first conflicting reservation and
  regenerate the affected intersection plan.
- **Current evidence state:** **NOT RUN**. Control records, phase plans,
  clearances, and independent replay tooling are prerequisites.

### TV2-DEM-009 - realistic density

- **Method decision:** **APPROVED**. Directional measured/interpolated bands,
  GEH, aggregate-flow limits, road-class behavior, and per-viewport occupancy
  jointly prevent airport-only or performance-capped demand from passing.
- **User-visible objective:** Major roads look active while local streets remain
  plausibly intermittent across the whole map.
- **Source of truth:** Measured directional counts; frozen time-of-day scenario;
  road class, lanes, capacity, speed, land use, and freight relevance.
- **Implementation owner:** B. **Validation owner:** D.
- **Validation method:** Independently recompute target directional flow,
  compare rolling scheduled counts, and audit pilot-visible occupancy by frozen
  benchmark camera/path.
- **Pass threshold:** Measured segments within source uncertainty;
  interpolations within frozen confidence intervals; GEH below 5 for at least
  85% and below 10 for 100% of calibrated directional screenlines; aggregate
  directional flow within +/-10%; all major-road views inside their
  source-derived density band; intermittent residential occupancy; no
  airport-only concentration or performance-selected global cap.
- **Evidence artifacts:** `traffic_demand_v2.json`,
  `traffic_count_source_comparison.csv`, and
  `full_map_viewport_inventory.csv`.
- **Failure response:** Correct demand evidence or scheduling; never lower
  semantic coverage to meet render cost.
- **Current evidence state:** **NOT RUN**. Directional counts, uncertainty,
  interpolation confidence, scenario, and viewport bands must be frozen first.

### TV2-FLT-010 - regional fleet composition

- **Method decision:** **APPROVED**. Global, contextual, neighborhood, and
  viewport comparisons plus byte-consistent weights close the candidate's prior
  source/builder divergence and prevent global ratios from hiding local errors.
- **User-visible objective:** Ordinary views are passenger-dominant, with rare
  semis confined to credible freight movements.
- **Source of truth:** Regional registration/class evidence; corridor freight
  evidence; contextual road-class fleet distributions.
- **Implementation owner:** B. **Validation owner:** D.
- **Validation method:** Audit class ratios globally, by context, spatial
  neighborhood, and benchmark viewport against frozen source-derived bands.
- **Pass threshold:** Passenger cars/SUVs/pickups are the largest combined class
  in every ordinary view; zero semis on residential paths; freight-corridor
  semis inside their evidence band; each frozen light archetype within +/-3
  percentage points and aggregate heavy/commercial within +/-2; source,
  demand, compiler, and runtime weights byte-identical; every camera ratio
  inside its frozen band.
- **Evidence artifacts:** `fleet_distribution_v2.json` and
  `fleet_distribution_by_camera.csv`.
- **Failure response:** Regenerate contextual assignments; do not hand-place
  exceptions.
- **Current evidence state:** **NOT RUN**. Regional/freight sources, archetype
  mapping, and camera bands are prerequisites.

### TV2-DYN-011 - variable speed

- **Method decision:** **APPROVED**. Complete distributions, KS and CV limits,
  event derivatives, persistent profiles, and class-specific acceleration/turn
  constraints test natural variation without allowing runtime randomness.
- **User-visible objective:** Vehicles vary speed naturally and brake, turn, and
  accelerate smoothly.
- **Source of truth:** Speed limits/observed references; persistent driver
  profiles; vehicle-class acceleration and turn constraints.
- **Implementation owner:** B. **Validation owner:** D.
- **Validation method:** Compare complete trajectory speed distributions and
  event derivatives to frozen class/road targets, including within-route
  variance and heavy-vehicle constraints.
- **Pass threshold:** Nondegenerate source-derived distributions for every
  nontrivial road class; nonqueued empirical CDF KS D <=0.10 and within-stratum
  CV >=0.05; zero speed/acceleration discontinuities above the pre-frozen
  interpolation tolerance; all events within driver/vehicle bounds; heavy
  acceleration/turn distributions slower than passenger equivalents.
- **Evidence artifacts:** `trajectory_speed_headway.csv` and
  `traffic_slice_speed_headway.png`.
- **Failure response:** Regenerate the profile or trajectory; never add runtime
  randomness.
- **Current evidence state:** **NOT RUN**. Observed targets, profile bounds, and
  numeric interpolation tolerances must be frozen before evaluation.

### TV2-DYN-012 - variable headway

- **Method decision:** **APPROVED**. It checks safe minima, source-target CDFs,
  persistent diversity, lattice artifacts, and queue stop/release behavior over
  the whole schedule rather than accepting random-looking initial placement.
- **User-visible objective:** Traffic has irregular safe gaps and signal-created
  platoons instead of a uniform lattice.
- **Source of truth:** Persistent driver headway profiles; demand arrival
  process; signal/queue schedule.
- **Implementation owner:** B. **Validation owner:** D.
- **Validation method:** Measure time/distance headways by class over the entire
  schedule, compare with frozen target distributions, and apply a pre-frozen
  periodic-spacing test.
- **Pass threshold:** Zero headways below the continuous collision-safe minimum;
  passenger desired headway in [0.9, 2.2] s and heavy in [1.4, 2.4] s unless an
  independently approved reference narrows them; KS D <=0.10; at least three
  persistent targets per populated stratum; every major class nondegenerate and
  in-band; no dominant fixed interval/repeated lattice; safe queue gaps through
  stop and release.
- **Evidence artifacts:** `trajectory_speed_headway.csv` and
  `speed_headway_reference.md`.
- **Failure response:** Regenerate demand arrivals/profiles while preserving
  calibrated flow.
- **Current evidence state:** **NOT RUN**. Target CDFs, collision-safe minimum,
  queue schedule, and periodicity statistic must be pinned first.

### TV2-COL-013 - no same-route collisions

- **Method decision:** **APPROVED**. An independent continuous broadphase and
  conservative OBB/articulated narrowphase cover stops and seams for at least
  two periods, with stress witnesses and an exact 2.00 m closest-approach limit.
- **User-visible objective:** Following vehicles never contact or pass through
  each other.
- **Source of truth:** Oriented vehicle dimensions; continuous global-time
  trajectories.
- **Implementation owner:** B. **Validation owner:** D.
- **Validation method:** Test every co-lane pair over
  `max(1800 seconds, two schedule periods)` at authored interpolation precision
  plus 0.05 s and 0.20 s stress witnesses.
- **Pass threshold:** Zero same-route collisions; zero interpenetrations;
  minimum continuous bumper clearance at least 2.00 m.
- **Evidence artifact:** `trajectory_collision_report.json`.
- **Failure response:** Reject and reschedule the colliding trajectories.
- **Current evidence state:** **NOT RUN**. Frozen articulated dimensions,
  trajectories, and D's independent continuous solver are prerequisites.

### TV2-COL-014 - no cross-route collisions

- **Method decision:** **APPROVED**. Global spatial-temporal indexing and
  independent narrowphase over the same long horizon test every route pair,
  reservation, merge, driveway, crossing, and verified grade separation.
- **User-visible objective:** Different-route vehicles never collide at
  intersections, merges, driveways, or crossings.
- **Source of truth:** All-route continuous trajectories; intersection/merge
  reservations; grade-separation layers.
- **Implementation owner:** B. **Validation owner:** D.
- **Validation method:** Index all swept volumes over
  `max(1800 seconds, two schedule periods)`, narrowphase every overlapping
  interval independently at authored precision plus 0.05 s and 0.20 s stress
  witnesses, and audit reservation ownership.
- **Pass threshold:** Zero cross-route collisions; zero reservation conflicts;
  zero false conflicts between correctly separated grades.
- **Evidence artifacts:** `trajectory_collision_report.json` and
  `trajectory_conflict_heatmap.png`.
- **Failure response:** Reject the first conflicting reservation set and
  regenerate affected schedules.
- **Current evidence state:** **NOT RUN**. All-route trajectories, reservations,
  layers, and an independent global solver are prerequisites.

### TV2-COL-015 - no vehicle overlap or phasing

- **Method decision:** **APPROVED**. Separate authored-precision and
  runtime-decoded checks identify compression/interpolation defects, while the
  binding execution rule covers the complete collision horizon and every
  residency/streaming boundary rather than an initial-pose sample.
- **User-visible objective:** Moving and parked vehicles remain physically
  distinct during runtime interpolation and streaming.
- **Source of truth:** Moving trajectory bounds; accepted/reclassified parked
  oriented bounds; runtime interpolation contract.
- **Implementation owner:** B+C. **Validation owner:** D.
- **Validation method:** Independently collide moving/moving and moving/parked
  OBBs at authored and decoded precision over all resident cells, the full
  collision horizon, and every load/seam/residency/LOD/pool-pressure event.
- **Pass threshold:** Zero moving/moving overlap; zero moving/parked overlap;
  zero overlap introduced by compression or runtime interpolation.
- **Evidence artifacts:** `trajectory_collision_report.json` and
  `parked_vehicle_overlap_report.json`.
- **Failure response:** Reject the responsible trajectory, parked placement, or
  compression precision at the first overlap.
- **Current evidence state:** **NOT RUN**. Accepted parked bounds, candidate
  trajectories, decoded transforms, and runtime-event traces are prerequisites.

### TV2-ID-016 - stable persistent identity

- **Method decision:** **APPROVED**. Per-ID immutable-field and transform traces
  under cell transitions, capacity pressure, and LOD changes directly test the
  visible lifetime in which identity must persist.
- **User-visible objective:** A visible vehicle retains its model, paint, path,
  and identity through residency and LOD changes.
- **Source of truth:** Persistent IDs/immutable appearance records; runtime
  residency trace.
- **Implementation owner:** B+C. **Validation owner:** D.
- **Validation method:** Trace every benchmark-visible ID across time, cell
  transitions, pool pressure, and LOD transitions and compare immutable fields
  and transforms.
- **Pass threshold:** 100% unique persistent IDs; zero model/paint mutation;
  zero resolvable identity replacement during cell/capacity transitions.
- **Evidence artifacts:** `full_map_lod_residency_report.json` and
  `traffic_slice_head_pan_sequence.png`.
- **Failure response:** Fail the runtime gate and correct residency/selection
  ownership rather than increasing churn tolerance.
- **Current evidence state:** **NOT RUN**. Immutable identity records and frozen
  benchmark residency traces are prerequisites.

### TV2-LOOP-017 - no visible schedule-loop discontinuity

- **Method decision:** **APPROVED**. It compares every vehicle's continuous
  boundary state, defines numeric position/yaw/speed tolerances, and requires
  temporal viewport replay and verified portals for any inactive interval.
- **User-visible objective:** The 15-minute repeat has no disappearance, wrap,
  reversal, or teleportation.
- **Source of truth:** Global boundary state; valid map-boundary, destination,
  driveway, parking, occlusion, and unresolved-size portals.
- **Implementation owner:** B+C. **Validation owner:** D.
- **Validation method:** Compare continuous state around t=0/t=period for every
  vehicle and replay every frozen benchmark across the boundary with visibility
  and projected-size instrumentation.
- **Pass threshold:** Zero visible transform discontinuities; seam position
  jump <=0.05 m; tangent/yaw delta <=5 degrees; speed delta <=0.10 m/s; zero
  visible activation/deactivation, wrapping, or reversal; every discontinuous
  inactive interval begins/ends at a verified unresolved or occluded portal.
- **Evidence artifact:** `trajectory_loop_continuity.json` plus the externally
  indexed full-resolution boundary sequences required by the method.
- **Failure response:** Regenerate the boundary or portal interval; transparent
  fade is forbidden as a mask.
- **Current evidence state:** **NOT RUN**. Candidate boundary states, portal
  authority, instrumentation, and image sequences are prerequisites.

### TV2-LOD-018 - no head-motion LOD churn

- **Method decision:** **APPROVED**. Deterministic fixed-aircraft pans under
  pressure, per-ID existence/tier traces, hysteresis assertions, and lead image
  inspection jointly test both machine churn and visible popping.
- **User-visible objective:** Brief head turns do not make vehicles pop,
  disappear, or oscillate in detail.
- **Source of truth:** Aircraft-centered residency contract; frozen head-pan and
  rear-glance traces; LOD hysteresis state.
- **Implementation owner:** C. **Validation owner:** D.
- **Validation method:** Replay deterministic 90-degree pans/rear glances under
  capacity pressure with aircraft state fixed and compare per-ID existence/LOD
  traces plus full-resolution sequences.
- **Pass threshold:** Zero aircraft-corridor evictions caused only by head
  direction; zero resolvable disappearance; zero one-frame oscillation; zero
  head-only membership/tier changes for continuously visible vehicles in a
  stable projected-size band; no A-B-A tier oscillation within 2 s; lead sees no
  obvious transition in the full-resolution sequence.
- **Evidence artifacts:** `traffic_slice_head_pan_sequence.png` and
  `full_map_lod_residency_report.json`.
- **Failure response:** Correct residency reservations or hysteresis before
  adding capacity.
- **Current evidence state:** **NOT RUN**. Frozen replay traces, numeric LOD
  bands, capacity-pressure setup, telemetry, and inspected imagery are missing.

### TV2-LOD-019 - no per-eye LOD disagreement

- **Method decision:** **APPROVED**. Recording both eye projections and one
  shared tier, requiring the maximum projected size, and inspecting stereo
  absence directly tests the specified binocular invariant.
- **User-visible objective:** Both eyes see the same representation of every
  vehicle.
- **Source of truth:** Maximum projected size across both OpenXR eyes; stereo
  simulator instrumentation.
- **Implementation owner:** C. **Validation owner:** D.
- **Validation method:** Record left/right projected sizes and the shared tier
  for every stereo-visible ID through pans and cell transitions.
- **Pass threshold:** Zero per-eye tier disagreement; 100% of decisions use the
  maximum projected size across both eyes; inspected stereo contains no
  eye-specific vehicle absence.
- **Evidence artifacts:** `full_map_lod_residency_report.json` and an external,
  SHA-indexed simulator stereo capture set.
- **Failure response:** Fail stereo and correct the shared-eye selection
  calculation.
- **Current evidence state:** **NOT RUN**. Shared-eye telemetry, genuine frozen
  replays, both-eye captures, and lead/D inspection are prerequisites.

### TV2-CAP-020 - separate parked/moving capacity

- **Method decision:** **APPROVED**. Above-capacity controlled experiments,
  explicit minimum reservations, overflow borrowing rules, and lower-LOD
  retention distinguish genuine capacity separation from a score bias.
- **User-visible objective:** Dense parking cannot erase moving traffic, and
  moving traffic cannot erase source-backed parked context.
- **Source of truth:** Frozen separate category reservations; controlled
  overflow pool; capacity-pressure benchmark traces.
- **Implementation owner:** C. **Validation owner:** D.
- **Validation method:** Replay every benchmark with both candidate sets above
  capacity, assert category reservations before borrowing, and compare visible
  inventories.
- **Pass threshold:** Each category meets its minimum whenever enough resolvable
  candidates exist; unused capacity may be borrowed without consuming the
  other's reservation; every resolvable displaced detailed vehicle retains an
  appropriate lower LOD.
- **Evidence artifacts:** `traffic_slice_budget.json` and
  `full_map_lod_residency_report.json`.
- **Failure response:** Correct pool partition/overflow accounting; a category
  score multiplier is not a substitute.
- **Current evidence state:** **NOT RUN**. Numeric reservations, overflow
  contract, pressure traces, and inventory telemetry must be frozen first.

### TV2-VIS-021 - visible traffic from pilot cameras

- **Method decision:** **APPROVED**. The method counts only projected,
  unoccluded vehicles on frozen actual-flight view traces, constrains both lower
  and upper density, evaluates fleet repetition, and requires independent lead
  and D image approval; global totals cannot pass it.
- **User-visible objective:** Traffic is plausibly visible from actual flight
  views wherever mapped roads can be resolved.
- **Source of truth:** Frozen production-scene benchmark cameras/paths;
  source-derived density/fleet bands; stereo and fixed-camera full-resolution
  captures.
- **Implementation owner:** B+C. **Validation owner:** D.
- **Validation method:** Count projected, unoccluded vehicles in every frame of
  each frozen viewport trace, classify road/fleet context, and pair telemetry
  with lead inspection of exact full-resolution images. The trace hash fixes
  camera ownership, pose, duration, cadence, resolution, schedule phase, and
  frame denominator before candidate evaluation.
- **Pass threshold:** Every benchmark-visible eligible major road meets its
  frozen density band; where a major road projects at least 2 px, at least one
  moving vehicle projects at least 2x2 px in 95% of frames; designated urban
  and pattern views have at least five resolvable moving vehicles in 90% of
  frames without exceeding the frozen upper band; every unexpected empty road
  is resolved or source-justified; pilot views are passenger-dominant with no
  conspicuous local model/paint repetition; lead and D approve slice/full-map
  images.
- **Evidence artifacts:** `traffic_v2_baseline_contact_sheet.png`,
  `traffic_slice_contact_sheet.png`, and `full_map_viewport_inventory.csv`, with
  original full-resolution frames externally indexed.
- **Failure response:** Correct demand, schedule, or residency at the first
  failed viewport; global totals are inadmissible.
- **Current evidence state:** **NOT RUN**. Actual production pilot traces,
  viewport road inventory/bands, instrumentation, and inspected image sets are
  prerequisites. Existing external Visual QA traffic cameras alone do not pass.

### TV2-PERF-022 - Quest 3 performance

- **Method decision:** **APPROVED**. It separates post-baseline static ceilings
  from immutable-APK physical timing and defines run count, warm-up, duration,
  CPU/GPU, tail, stale/tear, allocation, architecture, transition, thermal, and
  failure criteria without allowing simulator substitution.
- **User-visible objective:** Traffic remains stable without breaking the
  standalone Quest 3 72 Hz target.
- **Source of truth:** Accepted-baseline static/runtime metrics; exact-APK Quest
  timing/thermal evidence; allocation/runtime instrumentation.
- **Implementation owner:** C. **Validation owner:** D.
- **Validation method:** Freeze traffic ceilings after baseline capture, then
  audit static cost, steady-state allocation, resident-only sampling, exact-APK
  timing, stale/tear counts, thermals, and failures on physical Quest 3.
- **Pass threshold:** Three exact-APK runs at 72 Hz, each after 90 s warm-up and
  60 s traffic stress; average CPU and GPU <=12.8 ms, p95 <=13.889 ms,
  over-budget <=5%, stale <=2, tears=0; zero traffic steady-state managed
  allocation; zero per-vehicle GameObjects/Update/rigid bodies/invisible-agent
  simulation; static/APK deltas within the frozen post-baseline budget; no
  transition stutter, thermal warning, crash, ANR, or OOM.
- **Evidence artifacts:** `traffic_slice_budget.json`,
  `full_map_lod_residency_report.json`, and an external immutable Quest capture
  with exact APK and log hashes.
- **Failure response:** Reduce mesh, decode, compression, or scheduling cost
  without reducing semantic coverage or visibly emptying roads.
- **Current evidence state:** **NOT RUN**. The post-baseline budget is not frozen,
  no exact Traffic V2 APK exists, and no qualifying physical run has occurred.
  A connected Quest or successful install/launch is not performance evidence.

### TV2-REG-023 - no unrelated regression

- **Method decision:** **APPROVED**. Exact baseline path/dependency comparison,
  singleton product invariants, focused/final validators, masked nontraffic
  image comparison, inspected stereo, and the Android build cover architecture,
  behavior, visuals, dependency closure, and shipping output.
- **User-visible objective:** Traffic V2 preserves the accepted aircraft,
  cockpit, XR, scenery, native JSBSim, controls, audio, and product hierarchy.
- **Source of truth:** `fda11a2` dependency closure/serialized scene;
  scope-freeze path audit; standard product validation, stereo simulator, Unity
  tests, and Android build.
- **Implementation owner:** LEAD. **Validation owner:** D.
- **Validation method:** Compare paths/dependency closure with `fda11a2`, assert
  singleton XR/camera/aircraft/native flight authority, inspect standard visual
  regressions, and run the complete product gate after full-map acceptance.
- **Pass threshold:** Zero out-of-scope changes; exactly one enabled XR Origin,
  tracked OpenXR Main Camera, aircraft root, and native JSBSim authority; zero
  UnityPrototype or rejected-candidate dependencies; every focused/final
  validator passes; all 123 standard views nonblank and inspected, with
  nontraffic-region SSIM >=0.995 or a lead/D-approved evidence-backed
  explanation; standard Visual QA and stereo show no unrelated regression;
  Android ARM64/Vulkan/OpenXR production build succeeds.
- **Evidence artifacts:** `traffic_v2_regression_report.md`,
  `dependency_closure_report.json`, and externally indexed standard product gate
  outputs.
- **Failure response:** Revert or isolate the first unrelated dependency/change
  before traffic work continues.
- **Current evidence state:** **NOT RUN**. No V2 candidate has completed the
  focused/final product gate, deterministic visual comparison, inspected stereo
  set, dependency closure, or production build.

## Review conclusion and current blockers

All 23 validation methods are **APPROVED** for the exact specification SHA-256
recorded above. No shared-spec amendment is required by this method review. This
satisfies the specification's method-approval prerequisite for implementation;
it does not satisfy any acceptance or merge gate.

Evidence remains fail-closed at 0 of 23 because the following inputs or results
have not been produced and independently verified in this clean worktree, in
the required order:

1. the accepted `fda11a2` aggregate baseline capture and truthful frozen
   post-baseline static ceilings/deltas;
2. independently acquired, licensed, hashed full-map road/lane/surface,
   exclusion, bridge/elevation, turn, and traffic-control authorities;
3. frozen count, demand, fleet, speed, headway, vehicle-bound, uncertainty,
   capacity-reservation, and benchmark-camera/path contracts;
4. Workstream D's independent geospatial, continuous-sweep, reservation,
   collision, identity, LOD, stereo, capacity, and viewport validators;
5. a bounded slice tied to a clean Git commit, the first immutable slice APK,
   focused physical Quest evidence, and Alex's explicit slice acceptance;
6. full-map machine, standard Visual QA, genuine Meta XR Simulator replay, and
   performance-preflight evidence against the G1 ceilings;
7. the final immutable full-map APK plus three qualifying physical Quest 3 runs
   and final worn inspection; and
8. the final dependency closure, product tests, visual regression, and Android
   build report.

Until a requirement's own artifacts satisfy its threshold and D reproduces the
result, its evidence status remains `NOT RUN` or `BLOCKED_PREREQUISITE`. No route
count, vehicle count, test total, image total, static budget, simulator result,
build, installation, or device connection changes that status.
