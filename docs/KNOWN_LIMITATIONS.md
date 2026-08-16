# Known limitations

## Full-map building clipmap V1 candidate

- The 57,407 components greatly improve coverage and spatial fidelity, but
  official footprint/height records are incomplete and are aligned to the 2020
  imagery chronology rather than current conditions.
- Facades and doors/windows are semantic procedural approximations, not surveyed
  or photographically reconstructed per-building elevations.
- Named landmarks preserve multipart footprint massing and height but lack
  bespoke landmark silhouettes, signage, and roof equipment.
- Very small ordinary buildings outside the current 4x4 page window remain
  orthophoto-only; this is deliberate Quest cost control.
- Editor Visual QA shows fewer peak draws and selected triangles, while the
  inherited complete-scene 38-material count still exceeds the old 29-material
  planning target. Physical Quest frame, memory, thermal, transition-pop, and
  worn-fidelity acceptance remain pending.

## Full-map building clipmap V1.1 transition correction

- Coverage pages preserve every source roof, but ordinary small buildings omit
  far facade walls to keep the moving 4x4 window inside the Quest triangle
  ceiling. The exact page containing the aircraft restores complete walls.
- Crossing a one-kilometre page boundary can still change an individual roof
  from its exact to 1.5 m-error-bounded simplified outline. The building mass no
  longer disappears, but physical-headset review is required to judge the
  remaining fidelity transition.
- The active window is symmetric around the aircraft and its far edge is still
  finite. Orthophoto imagery remains the representation beyond that window.
- Static planning metrics and an Android build do not establish sustained 72 Hz,
  thermal, or memory acceptance on Quest hardware.

## Full-map building clipmap V1.2 stable shells

- The former roof-only coverage representation and aircraft-page wall swap are
  removed. All 57,407 represented buildings now remain closed shells across
  page transitions.
- Ordinary footprints are deliberately simplified to keep the worst active
  window within 140,000 building triangles. The measured maximum is 139,588;
  landmarks retain exact footprints, while ordinary shells are bounded to 30%
  area error, 5% inflation, 3 m centroid shift, and 10 m boundary error.
- This change fixes representation-dependent wall popping, but it does not turn
  procedural facades into surveyed building art. Full physical-headset review
  is still required to judge page-boundary continuity and worn fidelity.
- The inherited scene-wide 38-material inventory still exceeds the older
  29-material planning target. Missing recorded Meta XR Simulator sessions also
  block the aggregate stereo replay gate; neither condition was introduced by
  the stable-shell compiler.

## Full-map aerial clipmap V1 candidate

- The source is Spring/Summer 2020 visual context, not current or navigable
  imagery. Vehicles, construction, vegetation, and surface markings can differ
  from present conditions.
- The moving high tier is 0.333 m/texel, not the service's 0.2286 m native
  pixel size. JPEG source/derivative compression and oblique viewing still limit
  taxi-range detail; no AI hallucination or synthetic upscaling is used.
- A fixed 3x3 high/medium window follows the aircraft rather than the airport.
  Its deterministic 22.78 MiB ASTC active/staging/global estimate is not a
  physical Quest frame-time, memory-residency, thermal, or comfort acceptance.
- The previous fixed pyramid remains a startup fallback and still exists in the
  product dependency closure. Its explicit mip requests are released after the
  full clipmap becomes ready; total installed APK/storage size is therefore
  higher even though the moving runtime windows are bounded.
- Terrain geometry, semantic objects, photographed shadows, and clean-plate
  content were not regenerated. The shared 2020 chronology minimizes—but cannot
  eliminate—source-image/object discrepancies. This milestone makes no literal
  photorealism, FAA, navigation, training-credit, or certified-fidelity claim.

## QuestFlight 0.1.0 Alpha 1

- This is a private, sideloaded Quest 3 alpha checkpoint for invited friends,
  not a public store release or a production-readiness claim.
- The current native JSBSim 1.3.1 C172P derivative is the sole flight authority.
  Its guided reservoir route, aerodynamic response, systems behavior, and
  recovery logic remain private-development behavior, not certified C172
  fidelity. The exact native-only Quest APK still needs handling and timing
  acceptance.
- The accepted worn smoke found the current experience shareable and the final
  unattended takeoff run passed the former recovery-cut point. The exact alpha
  APK has not completed three long-duration Quest timing, thermal, stale-frame,
  tear, or steady-state allocation measurements.
- The scene retains the static planning-budget debt documented below. A
  satisfactory worn smoke does not establish sustained 72 Hz performance.
- Meta hand and Touch interaction is supported for represented cockpit controls;
  unrepresented or visually merged controls remain static. Xbox/gamepad support
  has not received the same recent worn acceptance as hand and Touch input.
- Distribution remains private because the repository and GitHub Release are
  private. Testers require explicit GitHub repository access before downloading.
- This alpha is not FAA approved, is not for navigation, provides no legal
  training credit, and makes no certified-fidelity or final-photorealism claim.

## Standalone Quest cleanup checkpoint

- The 2026-08-07 cleanup checkpoint preserves the user-accepted mixed Meta OVR
  hand/Touch controls, stable scripted takeoff, and photoreal KBDU runway, and
  passes 248/248 EditMode tests, 9/9 PlayMode tests, dependency closure, and the
  Android release-style build.
- The integrated scene does **not** pass its existing static planning budget:
  77/75 selected draws, 675,833/650,000 visible triangles, 36/29 visible
  materials, 267.60/210.00 MiB estimated texture memory, and 17/3 transparent
  material slots. The limits were not weakened. Some texture-subasset estimates
  are conservative, but measured Quest performance may not be inferred.
- The 109-view capture is nonblank and free of magenta fallback and was visually
  inspected, but the aggregate Visual QA result remains failed because it
  correctly includes the static-budget failure.
- No long-duration timing, thermal, allocation, stale-frame, or tear acceptance
  is claimed for the exact cleanup APK. See
  `docs/evidence/STANDALONE_QUEST_PROJECT_CLEANUP_V1_2026-08-07.md`.

## Cockpit Completion V3

- The represented yokes, throttle, mixture, ALT/BAT master, primer,
  ignition/start key, avionics master, flap selector, and fuel selector now
  move from one cockpit authority. Nine controls have bounded either-hand
  OpenXR Touch bindings.
- Airspeed, vertical speed, directional gyro, altimeter, flap position, and
  brake pressure have live presentation in one opaque batch. GPS, COM1, COM2,
  and transponder have live presentation in one opaque glyph batch.
- Rudder/toe-brake pedals, trim, parking brake, attitude, turn/slip, magnetic
  compass, clock, tachometer, fuel/oil/electrical/vacuum/thermal faces,
  NAV/DME/audio, lighting switches, circuit breakers, alternate static,
  environmental controls, vents, doors, latches, seats, and belts remain
  static. Their imported geometry is merged, incomplete, or not positively
  identifiable; some also lack truthful system authority. False overlays are
  not used to simulate completion.
- SystemsV2 values are deterministic development approximations, not certified
  C172 systems fidelity. DME, NAV deviation, audio routing, CHT/EGT, faults,
  and certified magnetic/engine/electrical/fuel behavior remain unavailable.
- The inherited scene still exceeds its 29-material and three-transparent-slot
  planning targets at 31 and seven. Cockpit Completion V3 adds no draw,
  material, or transparent-slot regression, but does not fix that separately
  scoped scene-budget debt.
- Live Meta XR Simulator stereo/HMD/both-Touch passes. The ten genuine recorded
  `.vrs` sessions remain absent, so aggregate replay evidence is blocked.
  Physical Quest worn interaction, timing, thermal, and comfort acceptance
  remain required.

- **Airport Near-Field Ground Detail V1 is a bounded development candidate, not
  final grass or infrastructure fidelity.** It adds 1,000 deterministic
  volumetric grass tufts, a pilot-left utility corridor, restrained runway-edge
  breakup, runway-edge lights, one windsock, and bounded signs in two draws and
  one already-active material. The product environment contract passes at 51
  draws, 500,650 selected triangles, and 10 materials; 143/143 EditMode, 9/9
  PlayMode, Android build, and live Meta XR stereo pass. The aggregate Visual QA
  still reports its inherited 30/29 material-planning mismatch even though this
  layer adds no unique material. Ten recorded `.vrs` sessions remain absent,
  and no physical Quest timing or worn acceptance is claimed for the exact APK.
  Grass is intentionally bounded to the airport operating corridor and does not
  represent a botanical survey or full-world vegetation layer.
- **Airport Parked Aircraft V1 is machine-validated and awaits worn review.**
  Seventeen static aircraft now occupy the takeoff/landing corridor: seven
  powered aircraft on the south apron and ten sailplanes on the north glider
  line. They are terrain-grounded and orientation-aligned to the pinned 2019
  NAIP image, but that image is historical evidence rather than current ramp
  occupancy. Some photographed aircraft remain beneath or near the authored
  models because a new clean plate was outside scope. Static draw, triangle,
  material, transparency, EditMode, PlayMode, dependency, Visual QA, and Android
  build checks pass. The aggregate performance report remains 1.25 MiB over its
  conservative 210 MiB texture ceiling due to the accepted hero-corridor source;
  the new texture-free aircraft derivatives add zero texture residency. No
  physical timing or worn acceptance is claimed for this exact APK.
- **Cockpit Local Shadow V1 passed its exact-APK worn visual gate.** One authored
  120-triangle cabin-frame proxy produces one 512-square filtered two-moment map;
  both eyes sample the same world-locked result. The final worn check found a
  continuous blurred edge, no remaining sawtooth boundary, mostly realistic
  direction/strength, no flicker or stereo mismatch, and preserved instrument
  readability. The effect was described as slightly underwhelming, so strength
  remains a future polish choice rather than being increased without another
  comfort/readability gate. No long-duration frame-time, thermal, or allocation
  acceptance is claimed for this APK.
- **The cockpit reflection/directional-visibility candidate is machine-validated and awaits exact-APK worn review.** Cabin glass now uses the actual imported cockpit interior baked into a finite-volume static reflection, the broad instrument cover is restored, and twenty opaque cockpit renderers use offline four-direction visibility streams instead of a realtime shadow map. Product validation, 117/117 EditMode, 9/9 PlayMode, 81-view Visual QA, static budgeting, live simulator stereo, and Android build pass. The exact final APK identity is recorded in `docs/evidence/COCKPIT_REFLECTION_DIRECTIONAL_VISIBILITY_V1_2026-07-22.md`; no prior worn score or physical timing is attributed to it.
- **Worn Visual Runtime V1 canopy/facade work remains the accepted source beneath this candidate.** Its near representation contains 143 detailed groves / 696 grounded trees with perforated leaf-and-twig cards, branches, and trunks; medium/far coverage uses bounded aggregate groves plus the existing terrain canopy handoff. The canopy is 58,018 triangles in four fixed draws and one shared material.
- The groves are deterministic visual scenery derived from aligned public-domain imagery/canopy/DTM evidence, not a surveyed botanical inventory. Near foliage uses a project-owned generated sixteen-cell atlas, while medium aggregates remain visibly simplified at close range. Species, season, branches, and individual crown form are approximate. Raised geometry covers 59.364% of eligible represented canopy pixels inside 0–1,500 m; the deterministic terrain handoff preserves 100% source assignment without adding far tree geometry.
- Context buildings remain footprint-preserving procedural shells rather than reference-authored architecture. Their facade modules, entrances, opaque window reflections, category palettes, and metre-scale UVs are deterministic and semantically coherent, but individual window/door placement and material identity are inferred from incomplete OSM tags and are not surveyed. Hero-building quality and literal close-range photorealism remain future work.
- Cabin glass and instrument glass use bounded static reflection approximations; they do not capture the moving pilot or update the cabin at runtime. Directional cockpit visibility is baked at four cardinal directions and interpolated, not a full moving penumbra or surveyed C172 optical model. The exact worn build must still prove visibility, plausibility, and comfort. Instruments remain mostly static or blank; functional data binding was outside this visual milestone.
- The final static planning report passes at 55 selected draws, 628,444 selected triangles, 793,823 serialized mesh triangles, 27 maximum visible/active materials, 211,911,484 estimated texture bytes, three transparent slots, 12 shaders/17 keyword combinations, and 4x MSAA. These are Editor planning values, not physical Quest timing. Meta XR live stereo passes, but all ten deterministic `.vrs` replays remain absent.
- **Quest Visual Fidelity V1 is machine-validated but has not been worn on its exact final APK.** It height-calibrates the 42 fixed Tier-2 grove cues, replaces world-cell-random facade selection with stable OSM-semantic material families, adds one-pass procedural openings/glint to the authoritative context shells, strengthens static cockpit-window response, and increases baked cockpit occlusion. Unity EditMode 114/114, PlayMode 9/9, 81-view Visual QA, the static budget gate, live simulator stereo, and Android build pass. The final APK is 133,529,570 bytes, SHA-256 `584821b6022a5e583b971d80df04579c23f450989b125acc0ef2eb0011cc7e6f`; the prior worn 8/10 score belongs to a different APK and is not attributed to it.
- Canopy volume remains 42 representative repeated clusters rather than continuous surveyed groves. Context buildings remain simplified footprint shells with procedural shader openings, not reference-authored architecture; close views still fall short of literal photorealism/AAA asset quality. Cockpit reflection and interior depth are static/baked approximations, and realtime cockpit shadows remain disabled pending exact-APK flicker, comfort, and timing evidence.
- The final static planning report passes at 54 draws, 624,086 selected triangles, 774,139 serialized triangles, 26 visible materials, 211,849,528 estimated texture bytes, three transparent slots, 13 shaders/18 variants, and 4x MSAA. These are Editor planning values, not Quest hardware performance. Meta XR live stereo passes, but all ten deterministic `.vrs` replays remain absent.
- **Rounded Canopy Volume V1 is a machine-valid branch candidate, not final photorealism or hardware acceptance.** It retains the tagged Photoreal Aerial Surface V1 terrain canopy map and adds 42 fixed-world, non-camera-facing project-authored Tier-2 grove clusters in one instanced draw. The final scoped budget passes at 54 draws, 624,086 selected triangles, 774,139 serialized triangles, 26 visible / 27 active materials, 211,849,528 estimated texture bytes, three transparent slots, 13 shaders / 18 variants, and 4x MSAA. The exact 133,992,887-byte APK has not been worn or physically timed, so the prior 8/10 smoke is not evidence for this build.
- The 42 clusters are representative repeated volume cues, not a continuous dense forest, botanical inventory, surveyed canopy, or final species/age/season distribution. Thirty lie within 1.5 km. One source cluster, one shared atlas, and sparse placement limit variety and coverage; the retained terrain map supplies the broader canopy response. All ten deterministic simulator replays remain missing, so live stereo does not prove motion/LOD-transition comfort or Quest performance.
- **Photoreal Aerial Surface V1 is an accepted development baseline, not final photorealism or release acceptance.** It fixes the proven 7.444086 m AEQD/UTM frame divergence and the generic-OBJ east/west mirror, uses DTM-only bare-earth terrain, and correctly owns buildings and ordinary canopy. The 81-view machine review scored 6.8/10, while the subsequent exact-APK worn Quest smoke scored 8/10 and confirmed aligned/grounded buildings, consistent ground sharpness, and no noticed stereo instability or discomfort. Near trees remain prop-like, map-only canopy lacks convincing rounded volume, and some lighting/reflection presentation remains incomplete.
- The candidate represents 9,692 non-hero OSM footprints in 16 fixed batches / 128,092 triangles. It fixes footprint placement, mirror, missing-zone, grounding, and scale-pop architecture, but OSM coverage and tags are incomplete; only 28 bounded heights use non-authoritative lidar support; roof forms and facades are not surveyed or architecturally faithful; and one shared material cannot supply final neighborhood-level variety.
- In the tagged Photoreal Aerial Surface V1 baseline, ordinary canopy intentionally owns zero raised meshes, zero triangles, and zero additional geometry draws. All 63,308 eligible non-hero pixels use aligned height/density/normal/occlusion response in the terrain draw, while 144 pixels remain hero-owned. The rejected 29-component / 1,960-triangle candidate stays inactive because inspected silhouettes looked like blocks and cliffs. Rounded Canopy Volume V1 adds its separate bounded grove overlay without reviving that false raised-ground path.
- Candidate terrain is a 46,426-vertex / 92,000-triangle DTM reconstruction. Outside disclosed high-variation source cells, mesh-to-raster MAE/p95/max are 0.101071/0.357723/1.387371 m; runway-plane residual is below 0.000001 m and the integrated airport boundary is 0 m over 4,404 probes. Those measurements are not surveyed elevation, runway, foundation, or navigation accuracy.
- The accepted baseline's scoped Editor budget passes at 53 selected draws, 601,406 selected triangles, 751,459 serialized scene triangles, 26 visible materials, 211,849,528 estimated texture bytes, three transparent slots, and 4x MSAA. Texture memory and transparent slots are near their development ceilings. Meta XR live stereo and the short worn visual smoke pass, but ten recorded replays are absent and no physical Quest frame-time, thermal, allocation, or duration acceptance exists for the exact 134,254,889-byte APK.
- The global ground/sky cubemap, bounded static airport probe, window material, and instrument-glass response are an authored foundation, not accepted final presentation. Underside/strut reflection direction, cockpit material realism, and lighting remain imperfect; the optional cockpit-shadow profile remains off by default pending physical Quest flicker and timing evidence. Instruments remain mostly static or blank because functional instruments were outside this milestone.
- **`standalone-scenery-baseline-v1` is the accepted development baseline, not final photorealism or release acceptance.** All 56 production QA frames and 20 dedicated aerial frames were inspected at full resolution. The recovery removes the widespread floating-roof condition, scale-changing context-building LOD path, synthetic Tier-2 canopy blobs, and active water-effect geometry, but machine lead review remains **5.3/10**. Context shells still look plain, near vegetation remains prop-like, no convincing grove volume is demonstrated, and taxi/low-oblique ground imagery remains soft. The prior worn smoke scored approximately 7/10 and is retained as the human development-baseline evidence.
- The recovery covers 27,679 OSM records with 2,875 fixed closed shells and 24,804 orthophoto-only features. The fixed shells preserve footprint scale better and use no runtime LODGroup, but their simplified roofs/facades, shared material, dark footprint halos, and abrupt geometry-to-orthophoto cutoff remain visible. The hero Type 2 hangar LODs also retain a measured 0.865 m / 10.19% maximum silhouette-height difference and require a future motion/stereo transition check.
- Vegetation is limited to 22 Tier 0/1 placements in two batches (12,860 triangles, one material). Tier 2 owns no geometry; Tier 2/3 use orthophoto and lidar canopy maps within local X/Z `[-2048,+2048]` m. This is stable and inexpensive but is not visually convincing canopy. Outside that map coverage vegetation is orthophoto-only.
- Water presentation is deliberately `OrthophotoOnly`. Reservoir and minor-water renderers and material bindings are inactive; retained licensed meshes, masks, normals, and shaders are not evidence of an active water effect. This removes rejected gray/synthetic artifacts but leaves water visibly flat until a later bounded polish milestone.
- The final static Editor planning report passes at 55 estimated draws, 534,976 visible triangles, 24 visible materials, 176.77 MiB estimated texture residency, two transparent slots, and 4x MSAA. An unattended 90+60-second run of the integrated candidate averaged 72.5333 FPS with 13.7545 ms combined p95, 1.6667% samples over budget, zero stale frames, zero tears, no thermal warning, and zero observed managed collections. It nevertheless **failed the current machine gate** because combined average was 13.117833 ms versus 12.8 ms and the hardware inventory reported 509,437 visible triangles versus 500,000. One run is not the required three-run acceptance, direct GPU milliseconds were unavailable, and no new human comfort or visual acceptance is inferred.
- Meta XR Simulator v201.0 live Quest-3-profile stereo, HMD, and both Touch devices pass with nonblank, scene-consistent 1440² eyes. The aggregate remains `BLOCKED_MISSING_RECORDED_REPLAYS` because all ten required `.vrs` recordings are absent. No head-pan, transition, canopy, or building cutoff stereo acceptance is claimed.
- The nominal seat position moved 5 cm aft to reduce the reported need to lean away from the firewall. The serialized/profile contract and PlayMode regression pass, and the post-change worn smoke confirmed that Alex no longer needed to move backward.
- **Aerial Scenery Representation V1 fails its visual gate and is not a merge candidate.** Lead scores are near terrain 5.0/10, aerial terrain 5.8/10, near vegetation 3.0/10, grove grouping 3.5/10, aerial canopy 4.5/10, building spatial fidelity 8.0/10, building LOD continuity 7.0/10, and overall aerial scenery 5.5/10. The lidar/serialization architecture is coherent, but near trees remain sparse, Tier-2 aggregate crowns read as repeated dark blobs, low-oblique terrain is soft, and an actual building threshold crossing was not captured.
- The 92,000-triangle lidar mesh meets its declared error gate only outside explicit outliers: global MAE/p95/max are 0.16947/0.65324/10.4941 m; outside outliers they are 0.10747/0.37577/1.46116 m. Airport-patch boundary residual is below 0.000001 m after as much as 1.711126 m of source-boundary adjustment. This is deterministic approximation evidence, not surveyed operational, runway, or navigation accuracy.
- The active scenery is budget-compliant planning evidence at 57 maximum draws, 527,199 selected triangles, 683,452 serialized triangles, 24 visible materials, 182,930,546 estimated texture bytes, two transparent slots, and 4x MSAA. It is not Quest timing, thermal, stale/tear, or allocation evidence. Disabled legacy building groups no longer render, but their 57 old mesh dependencies remain inside the shared context-prefab package closure.
- Meta XR Simulator live Quest-3-profile stereo, HMD, and both Touch devices pass with consistent nonblank eyes. The aggregate remains `BLOCKED_MISSING_RECORDED_REPLAYS` because all ten required `.vrs` recordings are absent; no canopy transition, LOD threshold, head-pan, or rear-view stereo acceptance is claimed.
- **Airport-Core V4.3.1 is a branch-only partial gate, not product acceptance.** The runway band source is proven as a millimetre-scale pavement/terrain depth conflict and the bounded depth-bias correction scores 7.0/10 in cockpit, close, and aerial views. Three OSM-backed minor-water components now have deterministic mask/final/orthophoto evidence and score 7.0/10; the unchanged reservoir remains 7.0/10. Unity 6000.3.8f1 exposes its internal Frame Debugger API but returns zero local events in batch execution, and an interactive Editor attempt produced no report; exact per-event capture is therefore unavailable. Meta XR Simulator live stereo/HMD/Touch connection passes, but the ten required `.vrs` recordings remain absent and require one-time interactive recording. The branch is not merged.
- V4.3.1 changes no geometry or budget: 165 active renderers, 62 selected draws, 503,339 selected triangles, 689,275 serialized triangles, 24 visible materials, 168.08 MiB estimated textures, two transparent slots, 12 shaders/18 variants, and 4x MSAA. These remain planning values rather than Quest timing evidence.
- **Airport-Core V4.3 is a preserved failed-visual checkpoint, not acceptance.** The gray reservoir rectangle is removed and reservoir cockpit/aerial views reach 7.0/10, but broad runway bands remain visible after the one permitted corrective rerun and score about 5.5/10. The legacy minor-water surface/bank renderers are gone and a deterministic near/mid terrain mask is integrated, but the existing dedicated camera did not produce decision-quality proof of that subtle effect; it is scored 5.5/10.
- V4.3 is technically coherent and cheaper than V4.2: one-command product validation, EditMode 75/75, PlayMode 9/9, the 56-view machine capture, static budgets, and Android build pass. Static planning reports 62 draws, 503,339 selected-LOD triangles, 24 visible materials, 168.08 MiB textures, two transparent slots, and 4x MSAA. These are Editor planning figures, not headset performance evidence.
- **Airport-Core V4.2 closes the static packing/build blockers but still fails visual Stage 1.** The branch has one finite 512² non-directional lightmap, 171 serialized renderers, 698,328 serialized triangles, a passing 56-view machine capture, passing static planning budgets, and a successful Android build. Lead inspection still scores the benchmark about **4.0/10** against 7.5/10. The alternating runway slabs are dominant; the reservoir is surrounded by a large opaque gray suppression polygon; minor water is a flat ribbon; terminal/hangar materials remain flat or stretched; context buildings remain generic boxes; the clean plate retains soft photographed ghosts; and the vegetation cluster remains sparse and procedural. Stage 2 was not started.
- V4.2 has only 1,672 serialized triangles of margin and uses exactly 25/25 visible materials. Its 168.26 MiB texture estimate includes conservative estimates for 26 texture subassets without a `TextureImporter`. Those are tight Editor planning margins, not measured Quest headroom. Meta XR stereo, worn visual quality, comfort, 72 Hz frame timing, stale/tear counts, thermal behavior, and allocations were not run because the subjective pre-headset benchmark failed.
- **Airport-Core Photoreal Benchmark V4 failed Stage 1 and was not scaled.** The final 56-view rerun scored 4.0/10 against a required 7.5/10. The terminal and Type 2 hangar geometry are materially more specific than V2, but their baked response is washed out; the whole scene remains strongly yellow/green; the clean plate is soft; runway macro blocks remain; reservoir normals form dense stripes with a hard olive shore; and vegetation remains visibly procedural. The corrected static audit also fails at 27/25 visible materials and 255.46/180 MiB estimated texture memory. ProductIntegrity fails at 207/180 aggregate renderers and 797,050/700,000 aggregate serialized triangles, so Android build preflight stops before producing an APK. Stage 2, simulator, and physical Quest gates were not run.
- The V4 GPU light-bake attempt wrote two directional 2048 atlases and 20 light probes, but logged six non-finite indirect/environment/directionality values. No baked reflection probe was produced. The benchmark vegetation atlas was authored for a different low-confidence sun direction than the final serialized lighting profile, and the minor-water mask remains a normalized, non-georeferenced prototype. None of these systems is visually accepted.
- **Photoreal Environment V1 is rejected by human headset review and is not complete.** Machine Visual QA had scored the integrated checkpoint 6.0/10, but Alex's worn Quest 3 review scored it **4/10**. The default eye position remained slightly too close; mountain ridges aliased; near terrain stayed geometric; ground shimmered and exposed resolution transitions; runway, buildings, repeated trees, parked aircraft, and terrain-object integration remained visibly unrealistic. Machine image-integrity checks did not predict this worn visual result.
- **Environment Reconstruction V2 is also not accepted.** It materially improves coordinate authority, source coverage, grounding, HLOD accounting, and static budgets, but full-resolution lead review is only about **5.2/10**, below the required 7/10. Secondary water is visibly segmented/dark, runway markings form unrealistic alternating blocks, buildings read as grey extrusions, vegetation reads as repeated pale low-detail forms, and terrain remains faceted with visible resolution transitions. No V2 simulator or headset run was requested because the required pre-headset visual bar was not met.
- The 1 m 3DEP source was reduced to 5 m airport and 25 m near grids. Average absolute errors are 0.0407 m and 0.2117 m, but isolated maxima are 3.7056 m and 14.1289 m. The regular-grid reduction is therefore not an error-bounded simplification despite a sealed boundary maximum of 0.008 m.
- The current reconstruction QA adapter fails closed because it still maps several stale intermediate reports instead of the final Unity placement, HLOD, water, vegetation, and static-budget artifacts. This tooling mismatch is recorded separately from the passing final product validation and does not convert the visual result into a pass.
- Low-oblique NAIP imagery remains soft. The near/mid/far shared-ENU pyramid removes the former gross photo/generated-color ring and blends over bounded overlaps, but it does not create source detail that is absent at distance. Fixed-camera machine checks cannot prove that the transition is imperceptible in stereo.
- The retained immutable USGS far DEM is stable under a 25-heading identity/transform sweep, but some ridge silhouettes remain faceted and synthetic. The external Option A/Option B images compare source shape only; a genuine integrated Meta XR Simulator stereo A/B run was not completed.
- The retained Boulder Reservoir source package includes an opaque sky-reflective dual-normal material and artistic shore/depth mask, but those bindings are inactive on the recovery branch. The mask is not surveyed bathymetry.
- The airport kit, fixed OSM footprint shells, two vegetation batches, and three parked aircraft improve scale cues but remain simplified. OSM coverage, heights, and roof tags are incomplete; vegetation placement is not a botanical inventory; parked-aircraft placement is not current ramp occupancy. Unity instancing flags alone are not evidence that every custom shader compiles and uses GPU-instancing variants.
- The KOG_THORNS parked-aircraft adaptation is redistributed under CC BY 4.0 with attribution and a change notice. CC BY does not grant Cessna/Textron trademark rights or endorsement, so public commercial release still requires a branding review.
- Meta XR Simulator v201 made a fresh live stereo/HMD/Touch connection. Its frontend changed the 64-bit machine OpenXR runtime during startup; the final guarded run detected that mutation, used Meta's paired elevated deactivation, restored both runtime views and persistent state exactly, and removed `PreviousActiveRuntime`. The ten required genuine `.vrs` sessions are still missing because Windows computer control failed at the host sandbox-policy handoff. The gate is `BLOCKED_MISSING_RECORDED_REPLAYS`; no recordings were synthesized or renamed. Future simulator runs may require the paired UAC cleanup.
- Meta XR Simulator proves API-level stereo, tracking, input, and composition behavior only. It never establishes standalone Quest GPU timing, frame intervals, thermals, stale/tear behavior, controller feel, comfort, or worn visual quality.
- The checkpoint was subsequently launched successfully on Quest 3 from immutable APK SHA-256 `b51b135797e5f9abaa9af13ac85edb8bff9e51ef2c1d2b154b605191619a6d0e`; the product acquired PID `13135`, reported production readiness, and had no active Guardian/controller dialog. Alex completed a worn visual inspection but rejected the environment at **4/10**. No three-run timing gate was started after that visual rejection, so no FPS, frame-time, stale/tear, thermal, or allocation acceptance is claimed for this APK.
- Human evidence found no significant discomfort or nausea. The default viewpoint still required a very small physical step backward. Mountain boundaries aliased; ground shimmer and resolution transitions remained; near terrain and object integration looked geometric; water was stable but synthetic and hard-edged; runway and buildings lacked realism; nearby context was incomplete; repeated low-poly trees were conspicuous; and parked aircraft had incorrect positions, approximately 90-degree orientation errors, and ground penetration. Viewpoint calibration was not tested because the expected interaction was not clearly presented.
- The earlier 72.55 FPS headset baseline belongs to a prior APK and cannot be applied to this rebuilt environment. No standalone failure is claimed without measurements from the exact current APK.
- The historical PCVR recommendation followed an earlier visual-score precondition, not a measured standalone hardware failure. That evidence is preserved in its original milestone record, but the recommendation is superseded and is not an allowed product outcome.
- That historical environment checkpoint used the retired Unity prototype flight backend and is not current handling evidence. Native JSBSim is now the sole product authority, but it is not a certified or final C172 flight model.
- Several cockpit controls lack separable pivots and remain static. Aircraft and cockpit remodeling were outside this environment milestone.
- Compatibility beyond the tested Quest 3, Unity, Android ARM64, Vulkan, and OpenXR configuration is not claimed. The APK remains external build output and is not committed; store distribution still requires production signing and release-channel validation.
- PCVR, Quest Link, Air Link, remote rendering, Windows shipping builds, and required-PC runtime dependencies are out of scope and are not fallback product strategies. The current decision is **NARROW STANDALONE QUEST SCOPE**, not STOP and not PCVR.
- The application is not FAA approved, is not for navigation, and provides no legal training credit.

Milestone evidence: `docs/evidence/PHOTOREAL_AERIAL_SURFACE_V1_2026-07-20.md`, `docs/evidence/STANDALONE_SCENERY_BASELINE_V1_2026-07-20.md`, `docs/evidence/AERIAL_SCENERY_RECOVERY_V2_2026-07-19.md`, `docs/evidence/AERIAL_SCENERY_REPRESENTATION_V1_2026-07-18.md`, `docs/evidence/PHOTOREAL_STANDALONE_ENVIRONMENT_V1_2026-07-16.md`, `docs/evidence/AIRPORT_CORE_PHOTOREAL_BENCHMARK_V4_2026-07-17.md`, `docs/evidence/AIRPORT_CORE_PHOTOREAL_BENCHMARK_V4_1_2026-07-18.md`, and `docs/evidence/AIRPORT_CORE_PHOTOREAL_BENCHMARK_V4_2_2026-07-18.md`.
# Airport hero corridor

The airport corridor is a reference-faithful procedural approximation rather than surveyed architecture. It has accepted footprint/roof coherence in the worn Quest review, but facade microdetail and exact historical building variants remain bounded by the shared Quest material and geometry budget.

## Bounded road traffic

- Traffic V1 is a bounded life cue, not a current occupancy database. Its
  27,948 parked records are detector-supported observations in spring/summer
  2020 City imagery, not a claim about present-day vehicles. All 27,679 OSM
  buildings in the product area received a native-source search envelope;
  8,754 buildings had at least one accepted source-visible vehicle and 18,925
  did not. No vehicle is invented merely to satisfy building coverage.
- Detection is not perfect. Small, occluded, shadowed, or low-contrast vehicles
  may be absent, and source-pixel long-axis fitting can be ambiguous. The
  semantic filter rejects candidates on roads/highways, aeroways, water, and
  building footprints, but the committed poses are still an offline visual
  approximation rather than surveyed parking inventory.
- Seven hundred eighty-eight deterministic moving records follow 361
  directional routes compiled from the bounded OSM road network. Traffic does
  not model every street, intersection, signal, collision, or driver decision.
- The five CC0 vehicle derivatives are optimized flight-distance assets, not hero
  automobile interiors. They provide distinct sedan, hatchback, crossover/SUV,
  pickup, and cargo-van geometry; eight paint colors; opaque reflective-looking
  glass; and wheel/light material response. They do not provide transparent
  cabins, occupants, active lamps, animated wheels, or realtime shadows.
- The runtime visits only 500 m spatial cells intersecting a 4.5 km hysteretic
  visibility envelope and selects at most 64 detailed vehicles plus 1,800
  far-proxy vehicles. Source-supported vehicles outside that visible set are
  intentionally culled to preserve standalone Quest cost.
- Simulator evidence does not establish Quest hardware performance. A final
  worn review and timing/thermal/stale/tear/allocation gate are required for the
  exact rebuilt APK before any hardware-performance claim.
- Traffic adds one shared 128-square ASTC detail texture and one shared opaque
  material while removing the serialized parked batch. Aggregate texture-budget
  status remains governed by the full product performance report. The current
  branch retains the inherited 221,506,450-byte estimate against the
  220,200,960-byte planning target; this parked-vehicle change added no texture
  residency and does not resolve that existing 1.25 MiB overage.

## Airport perimeter fence

- The fence follows the current OSM aerodrome boundary as a bounded visual
  placement authority. It is not surveyed fence-centerline, gate, ownership, or
  security data.
- City evidence documents mixed and historically incomplete fencing. Closing
  the visual perimeter is a simulation choice requested for scene coherence;
  it must not be described as a literal current airport-security layout.
- The project-authored green privacy-slat treatment is optimized for cockpit,
  taxi, takeoff, and approach distances. It intentionally disappears into the
  orthophoto at high altitude and does not model individual fittings, signs,
  locks, or exact gate hardware.
- Worn Quest visual acceptance reached 9/10 for visibility, continuity, color,
  and stability. No physical Quest timing/thermal acceptance was run for the
  exact fence APK; visual acceptance does not establish performance.

## Boulder Cloud Field V1

- The default profile is one authored `KBDU_SUMMER_SCATTERED_V1` fair-weather
  presentation, not live weather or a reconstruction of a particular day.
- Clouds are stereo-stable cutout impostors plus bounded interior atmosphere
  modulation. They are not literal volumetric density fields: fly-through
  visibility is an ellipsoid-driven fog/sun/sky transition rather than sampled
  three-dimensional vapor.
- The final cloud delta is one renderer/draw, 30 selected and serialized
  triangles, one material, and one maximum-512-square ASTC atlas. The aggregate
  Visual QA planning gate already failed before clouds at 78 draws and 675,835
  visible triangles; the cloud candidate records 79 draws and 675,865 triangles.
- A live Meta XR Simulator stereo pair was inspected, but the aggregate gate is
  `BLOCKED_MISSING_RECORDED_REPLAYS`. No physical Quest timing, thermal,
  stale/tear, comfort, or worn visual acceptance is claimed for the cloud APK.
- The 2026-08-11 correction replaces the RGB/luminance-key atlas with a pinned
  straight-alpha derivative, preserves alpha coverage through Android mips,
  clamps atlas sampling, expands serialized bounds to the true card radius, and
  calibrates clouds to the authored sun/sky. Machine captures no longer show the
  former charcoal card border, but exact-APK worn visual acceptance remains
  pending.
- The earlier cloud APK used the now-retired Unity prototype backend; that
  historical result is not current flight evidence. Native JSBSim is now the
  sole authority, but this does not establish stall, slip, spin, or recovery
  fidelity; nine current native scenarios remain unproven pending independent
  condition-specific C172P envelopes.

### Current V4 representation

- The active candidate replaces cutout impostors with eight bounded,
  VDB-derived density volumes in one draw. It provides real side/underside
  shading and sampled fly-through extinction, but it is a sparse Quest field,
  not a physically simulated cloud/weather system.
- Density, placement, illumination, and coverage are authored for the observed
  KBDU scattered-cloud condition. They do not update from live weather and do
  not model cloud evolution, precipitation, icing, turbulence, or cloud-cast
  terrain shadows.
- The aggregate planning gate still inherits unrelated full-scene draw and
  visible-triangle excesses. Meta XR Simulator live connection was blocked by
  a Windows Security prompt, so final stereo stability must be established by
  the exact installed Quest APK.
- The exact installed APK passed Alex's worn visual review. No physical Quest
  72 Hz, thermal, stale/tear, or long-duration comfort acceptance is claimed
  because those metrics were not captured.

## Golden Eagle thermal V1

- The visible bird is a rights-recorded, low-poly animated eagle derivative
  recolored for a Golden Eagle/soaring cue; it is not a species-accurate
  ecological model.
- There is one fixed-world lesson encounter, one deterministic fair-weather
  orbit, and one thermal. Bird strikes,
  flocking, random weather, and a broad thermal field are out of scope.
- Release builds use native JSBSim and submit the shared updraft through its
  atmosphere ABI. The current native evidence remains incomplete at three
  passed and nine unproven scenarios; the thermal cue is not proof of modeled
  convective-weather or soaring fidelity.
- The replacement preserves a real skinned 208-frame wingbeat and adds explicit
  soar/bank/transition states. It remains one deterministic training cue, not a
  behavioral or population simulation.
- Exact-APK worn visual acceptance and physical Quest timing/thermal evidence
  remain pending.

## C172 cockpit soundscape V1

- The current soundscape is a first authored cockpit mix, not a calibrated
  psychoacoustic or certified C172 recording. Engine layers derive from a real
  CC0 1964 C172E interior recording, while auxiliary cues are project-authored.
- The pilot presentation intentionally approximates an aviation-headset mix:
  engine/propeller and wind remain audible but controlled. It does not model a
  specific ANR headset, airframe transfer function, or measured sound pressure.
- No fabricated ATC dialogue is included. KBDU radio/CTAF, intercom speech,
  traffic, wildlife, and weather event audio remain future authored content.
- The bounded implementation owns eight continuous and two pooled one-shot
  sources. Physical Quest loudness, comfort, 72 Hz timing, and thermal impact
  require worn hardware acceptance of the exact APK and are not inferred from
  EditMode or build results.

## Full-map projected building roofs V1

- Ordinary building roofs now reproduce the georeferenced orthophoto vertically
  on retained roof geometry. This improves aerial semantic consistency but is
  not surveyed construction detail.
- Source-image baked light, shadows, and photographed roof objects remain in
  the projected color and may not match a future time-of-day system.
- Close grazing views remain limited by source ground sample distance and the
  current moving-imagery residency. Airport hero buildings intentionally keep
  their authored materials because the airport clean plate removes their source
  roof imagery.
- The exact APK is installed for worn review. Stereo simulator orchestration
  failed before connection, and no physical Quest frame, thermal, or comfort
  acceptance is claimed.

## Full-map watertight building shells V1

- Ordinary procedural shells now share exact wall-top/roof-perimeter vertices
  and extend 1.60 m below the sampled authoritative terrain. This removes the
  visible shell gaps; the hidden skirt is not a surveyed foundation model.
- Terrain height is sampled from the authored production surface. Rare source
  footprint overhangs beyond the bounded terrain domain use the nearest domain
  boundary sample.
- The correction does not improve incomplete OSM tags, uncertain building
  heights, procedural facade detail, or close-range architectural fidelity.
- Machine captures and deterministic mesh validation pass the sealing contract.
  Worn visual acceptance and physical Quest timing/thermal evidence remain
  pending for the installed APK.
