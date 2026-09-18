# Road geometry and traffic contact repair

Status: **Approved by the user; core repair implemented, full plan incomplete. Android packaging succeeded after cleanup, and the exact verified APK is installed on Quest 3. See [implementation evidence and remaining work](ROAD_REPAIR_IMPLEMENTATION_2026-09-18.md). Ready for focused headset review; not yet worn-accepted.**

This revision supersedes the initial four-step proposal. It includes terrain shape, pavement boundaries, moving paths, bridge geometry/ownership, and parked-car grounding as one repair. It does not authorize a new city-scale traffic simulator.

## Objective

Remove the artificial road corrugations and vehicle burial reported on the highway near KBDU and on the perimeter road after the bend. Preserve real grades, embankments, bridges and drainage. Work from the existing source data and ship a bounded repair suitable for standalone Quest 3.

The reported locations are consistent with Foothills Parkway and Airport Road south of the airport. Both have independently reproduced defects. The user has not identified an exact coordinate, so these findings do not claim to identify every place seen during the flight.

## Implementation findings (September 18)

The first local terrain bake reduces mesh-to-DTM maximum error on the same measured profiles from 0.760 m to 0.063 m (Foothills) and 0.481 m to 0.052 m (Airport Road). These are numerical measurements, not visual acceptance.

Implementation also uncovered the omitted **D-16-DG Foothills Parkway over BNSF railroad** crossing, immediately south of Diagonal. The frozen CDOT inventory identifies the 1988 bridge, and the existing aerial clearly shows it. The previous inventory rejected its OSM members solely because there was no lower *road*. Bare-earth data drops below the highway at this location. A locally refined terrain mesh alone cannot reconstruct the missing deck. This crossing is now included in the authored bridge repair. Its deck profile is an approach/roadway-lidar reconstruction; railway clearance is not surveyed or certified.

Contact authoring has exposed unrelated outer-map surface discontinuities and ambiguous route aliases. They must be reported explicitly, and no failed contact bake may be silently published as validated. New bank data must be gated before an Android build. A failing or visually defective candidate is not ready merely because Unity authoring completed.

## Confirmed findings

### 1. Terrain triangles cut across the road and adjacent lower ground

The active near-terrain mesh is `PhotorealAerialSurfaceV1/Terrain/Native/KBDU_ProductAeqd_BareEarth_Fixed.asset`: 46,426 vertices and 92,000 triangles. `ProductionEnvironmentRoot.prefab` assigns it to `Terrain_Near4km_USGS`.

`tools/aerial_surface/build_product_terrain.py` constructs axis-aligned terrain cells, adds selected cell-center refinement, and samples the existing 4 m lidar DTM. It preserves runway geometry but does not constrain triangles to road edges. More source detail alone would not fix this topology.

Direct sampling of the current serialized triangles along actual traffic lanes shows repeated dips absent from the underlying DTM. For example, a Foothills Parkway triangle at local X/Z approximately (-1590.2, -518) spans 22.69 m east-west and 11.44 m north-south. Its vertices have heights 9.213, 4.699 and 8.564 m relative to the project datum. These vertices accurately sample their own source locations; the problem is interpolation across the road and the lower median. A nearby triangle spans heights 10.098, 4.755 and 9.293 m.

In the inspected 81 m highway profile, mesh-to-DTM disagreement reaches 0.76 m. On a 261 m Airport Road bend profile, it reaches 0.48 m. The source road elevations are substantially smoother than the rendered profiles. The sampled source pixels in these two profiles are direct-ground pixels, not nearest-filled gaps; missing lidar is not the demonstrated explanation here.

### 2. Traffic follows different elevations from the visible mesh

`TrafficV1/KBDU_TrafficRoot.prefab` assigns `KBDU_ProductionTerrainSurfaceV2.asset` to the traffic runtime. Its near layer remains a 25 m grid, with a 5 m airport layer. `ProductionTerrainSurfaceAsset.GridLayer.Sample` bilinearly interpolates that grid. The newer visible mesh uses different samples and triangle interpolation. Existing triangle patches reconcile terrain-tier boundaries, not the near-terrain interior.

On Foothills Parkway route `canonical:osm_bounded_roads:way/362700164:0:0:0`, a lane sample at (-1590.188, -487.886) has rendered Y=10.036 m and queried Y=6.157 m: **3.879 m of disagreement**. This route has no upper-road profile in the current grade-separated road authority. Reproducing the sedan's front/rear/center height rule still leaves approximately the same discrepancy, before its small contact offset.

On Airport Road route `canonical:osm_bounded_roads:way/17022009:0:0:0`, the bend has approximately **0.39–0.41 m** of disagreement in the inspected lane samples. This is sufficient to bury wheels and lower bodywork. These are static surface/contact measurements, not recorded penetration for every scheduled vehicle.

### 3. Existing vehicle support has limited protection against cross-slope errors

`KbduTrafficV2Runtime` samples center, front and rear on the vehicle centerline. It has no lateral wheel-track samples. That is a secondary weakness around sharp cross-slopes, especially for trucks; it does not explain away the much larger elevation-source mismatch.

### 4. A confirmed driving path leaves the generated bridge deck

At the Foothills Parkway crossing over Diagonal Highway (structure D-16-DF), the two OSM carriageway routes `43098445` and `43098448` are each marked FORWARD with two lanes. The runtime offsets every lane to the right of its source polyline by `1.75 + laneIndex * 3.5` metres. It does not distinguish a two-way road centerline from a one-way carriageway centerline.

Meanwhile, `GradeSeparatedRoadV1Authoring.EffectiveRoadWidth` reduces each of these profiles' 14 m width to 7 m because `source_lanes` is 2. Reading the current native bridge mesh and intersecting its triangles at the runtime's lane positions gives:

| Route | Lane index | X, Z at bridge midpoint | Bridge mesh at this XY |
|---|---:|---:|---|
| 43098445 | 0 | -1606.324, -122.362 | Top at 12.124 m; underside at 11.524 m |
| 43098445 | 1 | -1609.824, -122.293 | **No bridge triangle** |
| 43098448 | 0 | -1590.260, -117.912 | Top at 12.124 m; underside at 11.524 m |
| 43098448 | 1 | -1586.760, -117.893 | **No bridge triangle** |

This is a geometric failure for paths the scheduler can select, not a speculative risk. It does not establish that those lanes were occupied during the user's last flight. Correct Y alone cannot make a car stay on a deck that is missing under its XZ path. The profile sampler's permitted horizontal distance is also separate from the actual deck footprint, so accepting a height sample is not proof of visible support.

### 5. Path continuity and lane semantics remain incomplete

- The current manifest has 32,770 corridors. Its own limitations disclose that 19,786 use development defaults for missing lane totals. Those are not verified lane geometries.
- `SelectLane` divides bidirectional lane totals equally using integer division. That does not represent asymmetric lane counts or a center turn lane.
- Each segment computes its own perpendicular lane offset and heading. At one Airport Road bend vertex, the 10.28-degree direction change creates a **0.314 m instantaneous lane-position jump** with the current 1.75 m offset. Smoothing height cannot remove that XY/heading discontinuity.
- `BuildContinuousStreams` only joins exact two-road endpoints with matching class, direction, speed, headway and lanes, and a heading dot product of at least 0.999. It explicitly excludes routes associated with bridges. Junctions and ambiguous overlaps remain independently scheduled.
- Existing source and evidence disclose that legal turns, intersection control and vehicle collision behavior are unfinished. We must not describe the current corridor animation as a complete connected road simulation.

The need to distinguish source-line placement, lane count and connectivity is also documented by OpenStreetMap: [placement](https://wiki.openstreetmap.org/wiki/Key:placement), [lanes](https://wiki.openstreetmap.org/wiki/Key:lanes), and [shared center lanes](https://wiki.openstreetmap.org/wiki/Key:lanes:both_ways). These definitions guide interpretation; they do not prove the source tags are complete at KBDU.

### 6. Parked cars are affected and were omitted from the first plan

The enabled V1 parked runtime renders **24,347** authored parked vehicles. V1 moving traffic is disabled; V2 supplies the moving vehicles. Parked records contain fixed X/Y/Z and yaw, and the runtime uses yaw-only matrices. Their compiler samples the older `kbdu_terrain_rings.json`, not the current rendered near mesh.

A read-only scan of the 1,580 parked records within X/Z +/-1,800 m found 129 roots more than 0.25 m below the visible near terrain and 261 more than 0.25 m above it: **390 flagged placements**. Median absolute root-to-surface disagreement was 0.127 m, the 95th percentile 0.647 m, and the maximum 2.152 m. These are root-height discrepancies, not complete wheel/contact or visibility acceptance results; they justify a grounding pass without claiming all 390 are equally noticeable.

The existing parked placement pipeline already uses oriented detections, row consensus, full-footprint road/building/water exclusions and overlap rejection. Preserve this work. Its static heights/orientations and exclusions need rechecking against corrected surfaces and lanes; another detector or a new parking simulation is unnecessary.

### 7. Existing bridges are partial visual reconstructions

There are 29 profiles for 18 accepted structures, rendered in two batches totaling 6,496 triangles in the current assets (3,522 near; 2,974 outer). The authoring code builds deck tops, sides, undersides, closures and approach embankments. It does not author piers, abutments or barriers. Deck intervals are held at a constant crown height with linear approaches; widths can be clamped from lane counts and neighboring profiles can be merged by proximity.

The earlier inventory left 102 OSM bridge ways unresolved. That number is historical inventory evidence, not proof that 102 additional vehicle bridges need construction: some cross water/rail or lack sufficient matching evidence. The old requirement for an identifiable lower *road* must not make a real road-over-creek bridge disappear from consideration.

A clearance inventory value constrains the space between a road surface and an obstruction; it does not by itself determine an exact upper-deck elevation, crossfall, support layout or longitudinal curve. [FHWA bridge inventory definitions](https://www.fhwa.dot.gov/bridge/snbi/snbi_march_2022_publication.pdf) support that distinction. We should retain verified source facts while reviewing visibly approximate geometry.

### Previous fixes do not cover this defect

The September 3 terrain repair addressed near/mid/far seams. The September 4 grade-separated-road work and subsequent overlap repairs addressed 29 bridge profiles and their approaches. Those repairs should be preserved. Neither made every ordinary road's triangulation road-aware nor synchronized near-terrain traffic contact with the newer lidar mesh.

## Evidence inspected

- Current prefab references, native mesh vertex/index buffers, serialized elevation grids, traffic routes, bridge authority, and relevant authoring/runtime source.
- Existing aerial texture: `AerialSurfaceV1/Ground/KBDU_City2020_Near4km_8192.jpg`; road locations checked against its documented bounds.
- Existing road QA image `08_ordinary_graded_road_contact.png`, used as historical context, not as proof of the current build.
- Fresh offline plots and measurements from current assets: [road elevation evidence](../../T2-QuestFlight-product-artifacts/road_surface_investigation_20260917/road_elevation_evidence.png).
- Reproducible read-only diagnostic scripts and JSON results are in `../T2-QuestFlight-product-artifacts/road_surface_investigation_20260917/`. These are external investigation artifacts, not product code.

The plots sample current geometry; they are not Unity screenshots or headset performance evidence. No simulator build, APK installation or headset session was needed for this diagnosis.

## Critical assessment of the original proposal

The original plan would likely improve the two measured road profiles and reduce burial. It would **not** justify promising cars that stay on roads, realistic finished bridges, or corrected parked cars. Its main errors were:

1. Treating elevation agreement as nearly equivalent to a correct road. A vehicle can match the terrain perfectly while driving through a median or off a bridge.
2. Leaving path correction vague despite fixed lane offsets, default lane totals and discontinuous segment joins.
3. Preserving prior bridge work too uncritically. Preserve its verified facts and solved seams, not its demonstrably insufficient deck width or incomplete silhouette.
4. Making lateral contact optional before considering full vehicle footprints. Long trucks need space through curves and under structures, not just a supported center point.
5. Ignoring the separate parked-car pipeline and the stationary cars already photographed in the ground imagery.
6. Making two good-looking samples stand in for coverage. Automated coverage reporting is cheap; repeated headset discovery of another broken route is expensive.
7. Pursuing exactly unchanged triangle counts as if that guaranteed speed. Local extra geometry can be the simpler repair; any increase still needs an explicit resource check, not an invented allowance.

## Scope and delivery rule

One approved implementation should produce one integrated candidate with terrain, lanes, bridge contact and parked grounding repaired together. No headset requests between these steps.

| Coverage | Included work | Limit of the claim |
|---|---|---|
| Entire existing traffic coverage | Offline inventory of paths, contact errors, bridge assignments and parked placements; generation of shared contact data; clear coverage/failure report | An automated scan is not visual acceptance of every map location |
| Airport Road, Airport Boulevard, adjacent Foothills Parkway and connected approaches/crossings | Complete corrected pavement, lane placement, continuous paths, vehicle clearance, bridge appearance and adjacent parking review | First visually finished area, chosen because it contains reported defects and normal low-flight views |
| All 29 existing bridge profiles | Check road/deck footprint, approach continuity, upper/lower ownership and vehicle clearance; repair confirmed failures | Existing profile count is not complete bridge coverage |
| Other confirmed defects found by the scan | Apply the same bounded terrain/path/contact correction where source evidence resolves it | Unresolved source geometry stays explicitly listed; do not declare it fixed |
| New/unmodeled bridges | Resolve crossings on the repaired flight routes first, including road over water/rail; retain an inventory for the rest | No blanket promise to recreate every structure before the first candidate |

Do not hide invalid lanes by reducing visibility, fading vehicles at the defect, deleting a whole traffic class or silently emptying a road. Resolve the geometry/source assignment. Any genuinely unresolvable route remains an explicit exception; it prevents a claim of complete map-wide correctness.

## Updated implementation sequence after approval

### 1. Establish one shared road description before editing meshes

Reuse the existing aerial imagery, lidar, centerlines, source tags, bridge inventory and accepted asset provenance. Keep the scene's imagery chronology consistent. Obtain additional official/local reference imagery only for specific structures or ambiguous pavement boundaries that existing evidence cannot resolve.

Create a compact offline road description containing:

- Stable physical road/carriageway ID and source-route aliases; eliminate duplicate ownership when multiple datasets describe the same lane, without merging parallel carriageways or grade-separated roads.
- Pavement boundaries, shoulder/median exclusions, direction-specific lane centers and widths, supported vehicle classes, and confidence/source references.
- Surface ownership: terrain road, bridge approach, deck, underpass or unresolved structure; an explicit elevation level at crossings.
- Longitudinal profile, cross-sections, connection IDs and allowed continuations/turns supported by the data.

Start by resolving the reported airport corridors and D-16-DF. Run the inventory over the full existing manifest so defaults, duplicates, missing crossings and unsupported paths are visible in the report. Do not assume a source polyline is always the center of both directions. For a verified centered two-lane one-way carriageway, lane centers should straddle its center; for a two-way road they belong on opposite sides. Interpret placement/directional lane information and aerial geometry before choosing offsets. Center turn lanes, shoulders and bike/parking lanes are not ordinary through lanes.

**Deliverable:** road/path overlay and exception ledger keyed to actual asset/route IDs. The already diagnosed failures need no fresh general scenery comparison.

### 2. Repair terrain and pavement topology locally

Constrain affected terrain triangles to pavement edges and enough longitudinal stations to represent the source road profile. Prevent lower ditch/median vertices from pulling down the pavement. Sample the correct product-coordinate DTM and retain real grades, crossfall, drainage and embankments.

Use constrained local profile fitting only where the 4 m source cannot resolve a narrow road edge. Preserve reliable pavement samples, junction elevations and verified bridge endpoints. A smoother road must still fit its landscape; do not impose an arbitrary flat plane or generic maximum slope on mountain roads.

Replace the affected part of the existing terrain mesh rather than layering an opaque road sheet over a spike. Weld patch boundaries, preserve texture alignment and existing terrain-tier seams, and inspect affected nearby tree/building/parking bases. Keep runway geometry untouched. If any correction changes an aircraft-accessible/collision-relevant surface, synchronize that surface and run the required ground-contact scenario rather than leaving visual and collision geometry inconsistent.

**Deliverable:** smoother roads in the same landscape, with no new cracks, overlaps or stretched pavement edges.

### 3. Finish bridge geometry that matters from the airplane

Make deck width and each permitted lane's full vehicle envelope agree. Resolve the confirmed D-16-DF lane/deck mismatch using actual carriageway boundaries; neither moving every lane blindly inward nor widening every bridge to cover arbitrary trajectories is acceptable.

Use one final bridge geometry source for its visible top, approach contacts and traffic support. Preserve verified clearances; check underside-to-lower-road clearance across the whole traversed width, with the tallest supported vehicle. Do not infer an upper-deck surface from bare-earth elevation alone, and do not confuse clearance with top-surface height.

Review flat deck plateaus and linear approach kinks. Retain a flat deck where supported by evidence; otherwise fit a continuous, plausible profile tied to both approaches. Keep separate decks/medians separate where the aerial view shows a gap. Do not let proximity clustering fill that gap with a solid slab.

Add only the structural features that materially define the silhouette in low/oblique pilot views: deck edge/thickness, abutment contact, major piers where supported by reference evidence, and simple parapets/barriers where visible. Keep lower roads open and terrain out of their clearance envelope. Use opaque low-poly geometry and simple shaded concrete/edge treatment; do not project roadway photography down vertical walls and call it finished. Fine bolts, rail posts, underside beams invisible from the pilot seat and dynamic bridge shadows are deferred.

Check all current profiles for functional geometry. Prioritize visual detailing at the airport crossings, especially Foothills/Diagonal and 47th/Foothills. Use real sources before inventing support positions. Revisit relevant rejected inventory entries that cross water or rail rather than a lower road.

**Deliverable:** supported-looking, correctly sized bridges with believable approaches and distinct upper/lower traffic paths.

### 4. Bake continuous driving paths inside the pavement

Generate lane paths from the shared road description, with consistent offset joins and smooth tangents. Fit curves within the drivable pavement; do not apply an unconstrained spline that cuts across the inside of a bend. Parameterize movement by path distance so extra curve samples do not change speed.

Validate the **swept vehicle footprint**, including wheel track, body width, front/rear overhang and trailer extent, against pavement/deck boundaries, barriers, parked cars and other fixed obstacles. Include intervals between samples, not only path vertices. The current semi is a rigid combined asset: do not claim articulated off-tracking. Keep it on curves its actual mesh can negotiate; prefer eligible passenger vehicles on genuinely narrow/tight routes. That is vehicle-class suitability, not suppressing defective traffic indiscriminately. Trailer articulation is a separate enhancement unless a required highway movement cannot be made credible with the current asset.

Connect same-level, compatible road segments, including bridge entry/exit; preserve stable vehicle identity, paint, lane and timing through those connections. Handle reversed source-line direction, lane drops and ramps explicitly. Use a bounded lane transition only where pavement allows it. Do not join crossing XY lines across different levels.

On the corrected connected routes, include simple plausible behavior: curvature-limited speed, smooth slowing/acceleration, longitudinal spacing and deterministic conflict reservations at visible merges/crossings. Choose evidence-supported straight continuations and a limited set of legal turns; a driving game with every turn and lane change is unnecessary. Bake sparse timing/stop events against the existing deterministic clock instead of running autonomous physics/pathfinding for every vehicle. Check every supported archetype and the full authored schedule, including repeat boundaries and joins to unchanged traffic streams.

Vehicles must not materialize or dissolve midway along the repaired visible route or on its bridges. Extend paths through valid connections and place unavoidable entry/exit transitions outside the normally inspected segment at plausible destinations/network boundaries. Camera visibility must not change vehicle identity or trajectory. Do not promise universal no-pop behavior at every unfinished network endpoint.

**Deliverable:** paths that stay on pavement and move continuously through the repaired area, with no scheduled vehicle overlap in the validated movements. Complete city-wide intersection/signal simulation remains outside this repair and is not claimed.

### 5. Bake exact surface support and vehicle poses

Generate contact data after final terrain, bridge and lane geometry are established. Use exact visible-triangle intersections assigned to the correct road level. Do not use a highest-hit raycast that puts an underpass car on the deck, and do not accept a broad bridge-distance tolerance as proof that a deck exists beneath the vehicle.

Include lane path triangle crossings or adaptively subdivide until contact interpolation differs from the intended rendered surface by no more than 2 cm. This is an internal consistency tolerance, not a claim of surveyed real-world accuracy.

Use wheel/axle track support and the actual vehicle meshes to derive height, pitch and roll. Check wheel clearance and underbody clearance separately; a root or an AABB alone cannot prove that a long truck clears a crest. The rigid vehicles need sufficiently smooth supported roads; avoid a blanket lift or pose smoothing that makes them lag through the surface. Keep the near/far mesh pivots consistent.

Store compact, tiled path/contact data with shared mesh/source/version hashes. Reuse data for source-route aliases and invalidate affected tiles when terrain or deck geometry changes. Quantify decoded memory, load time and query cost before choosing final sampling/compression. Do not store per-frame transforms for the whole map. Keep general terrain/aircraft physics authority unchanged except where an actual modified collidable surface requires synchronization.

**Deliverable:** the visible surface and moving-vehicle support cannot silently drift apart on subsequent scenery rebuilds.

### 6. Re-ground parked cars and respect their footprints

Re-bake parked poses against final visible parking surfaces, including pitch/roll where a lot slopes. Use wheel-track support and existing source-observed positions/headings; preserve successful detections and row layout. Parked cars remain static instanced scenery with no extra simulation loop.

Recheck their oriented full footprints against corrected lanes, sidewalks/curbs, buildings, water and other parked vehicles. Moving paths must avoid real parked-car space. Correct a false detection or misplaced car using existing imagery evidence; do not slide a whole row arbitrarily just to satisfy a collision check. Prioritize near-airport lots and all lots touched by terrain changes; run the inexpensive grounding audit across the complete inventory.

Distinguish 3D parked cars from cars photographed into the ground imagery. The latter cannot collide, but can create ghost/duplicate cars when the 3D placement is offset or a moving vehicle passes over a photographed vehicle. Preserve alignment for credible static rows. Locally remove conspicuous conflicting photo remnants only where approved source rights and existing texture-authoring workflows permit it; do not start map-wide image cleanup, redetection or add more cars as part of this fix.

**Deliverable:** grounded parked vehicles that do not intrude into corrected traffic paths. Parking maneuvers, doors, occupants and additional parked density are deferred because they do not solve this defect.

### 7. Integrate, inspect once, build once, then use the headset

Complete steps 1–6 before asking the user to wear the headset. Preserve a known installed baseline and record changed source/asset hashes. Keep the work in coherent units so a failed bridge detail can be reverted without losing the terrain/contact repair.

Run a compact offline check over affected geometry and the full generated contact/path inventory. Inspect focused stereo visual QA of the complete candidate from pilot-relevant views: the reported highway, Airport Road bend, upper/lower bridge traffic and nearby parked rows. Include motion, not just stills. Use engineering views only to diagnose a specific failure; they are not the user's acceptance view.

Build the Android ARM64/Vulkan/OpenXR candidate, verify scene references and packaged data hashes, install the exact APK and perform one short guided headset flight. Existing diagnostics should record timing during that flight. Repeated simulator surveys, broad unrelated test suites and separate headset sessions for each subsystem are unnecessary. A short visual flight does not establish sustained thermal or 72 Hz acceptance.

## Completion checks

| Requirement | Required evidence |
|---|---|
| Smooth, credible roads | Source-grounded longitudinal/cross-section profiles; removal of the measured artificial corrugations; inspected pilot-view motion |
| Continuous geometry | Shared patch/deck/approach boundary differences <= 1 cm, no cracks/T-junctions or duplicate coplanar surfaces; no artificial normal/grade seam |
| Cars stay on the road | Full swept wheel/body footprint stays inside its permitted pavement/deck envelope with a small numerical margin; no intersection with medians, barriers or fixed objects |
| Correct bridge layer | Upper and lower route ownership remains distinct; no jump to another level; measured underside/vehicle clearance and correctly supported deck paths |
| Correct vehicle contact | Contact bake <= 2 cm from final geometry; all applicable archetypes checked for wheel gaps and underbody penetration; no visible burial/floating in pilot views |
| Continuous driving | No position/lane/identity jump at repaired joins; plausible speed changes; no conflict in the authored movements across the validated schedule and its boundary |
| Correct parked cars | Re-baked support/orientation, footprint exclusions and inspected near-airport rows; quantified remaining exceptions |
| Coverage honesty | Counts of checked/corrected/unresolved routes, bridges and parked records, with stable IDs; zero unresolved critical failures on the repaired airport flight route |
| Quest readiness | Content/build validation, resource deltas, exact installed APK identity and focused headset appearance/timing; no unsupported global performance claim |

The 1–2 cm checks measure consistency between generated assets, not survey accuracy. Swept-envelope checks must allow intentional road markings/shadows, but never excuse actual mesh penetration. Rigid-wheel approximation tolerances should be based on the existing vehicle model dimensions and inspected appearance, rather than invented as an accuracy claim.

## Quest 3 cost controls

| Resource | Plan |
|---|---|
| Terrain geometry | Local edge-aware refinement, keeping the current 92,000 near triangles as the comparison baseline. Reallocate where practical; report necessary additions rather than globally increasing density or degrading other visible scenery to meet an arbitrary exact count. |
| Bridge geometry | Current baseline: 6,496 triangles in two batches. Combine any justified supports/barriers into static batches; report the delta. Geometry invisible from the pilot's operating views gets no detailing budget. |
| Draws/materials | Target unchanged batches and shared opaque materials. If concrete side/support appearance requires another material/submesh, record its draw cost explicitly and justify the visible benefit before treating the candidate as acceptable. No per-bridge runtime scripts. |
| Moving traffic | Retain instancing, tile selection, stereo-safe culling, near/far proxies, distance hysteresis and existing visibility distance. Current V2 code ceiling is 18 draws and 2,048 instances per group; these are ceilings, not measured typical usage. |
| Parked traffic | Keep static instancing and existing LOD/caps. Store corrected poses; no frame-by-frame grounding, physics bodies or parking AI. |
| CPU | Offline surface/path/conflict preparation; compact indexed lookups and the existing global clock. No per-car GameObject/Update, runtime pathfinding or per-frame physics raycasts. Check steady-state allocations and timing delta. |
| Memory/loading | Tiled shared data, deduplicated physical routes, bounded adaptive samples and sparse timing events. Report serialized and decoded sizes; no unbounded map-wide 1 m grid or frame-keyframe expansion. |
| Performance acceptance | Compare actual current component/scene costs, not obsolete July traffic budget totals. Preserve accepted canopy range, High FFR, cockpit clarity and clouds. Record hardware timing; no claim of free added detail or 72 Hz/thermal acceptance without corresponding evidence. |

## Work deliberately deferred

New vehicle models, density increases, full articulated-truck simulation, city-wide reactive traffic/signals, parking animations, close-up bridge engineering detail, terrain replacement, a new imagery/ML pipeline, and unrelated cockpit/foliage/cloud optimization. These would slow the visible repair without resolving the demonstrated core errors.

## Recommendation and honest outcome

Approve this as a **road-surface, path and vehicle-grounding repair with focused bridge finishing**. The highest-value first implementation is the shared pavement/lane/deck definition at the airport: it fixes the proven off-deck lane and supplies the boundaries needed by terrain repair and contact generation. Finish the integrated area and apply reusable contact/grounding corrections across the existing inventory before the next headset session.

This is substantially more likely to solve the reported experience than the original elevation-only emphasis. It can deliver believable bridges and vehicles that stay on and above the correct surfaces in the validated coverage. It cannot honestly guarantee that every map road, every unmodeled crossing and every traffic interaction is perfect. Completion must state the repaired coverage and any unresolved exceptions, not repeat the project's earlier mistake of calling an unseen or partial change finished.
