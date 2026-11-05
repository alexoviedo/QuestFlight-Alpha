# SGS canopy yaw string — 2026-10-24

Implemented in source and local authored meshes. Pending integration into the next APK; installed alpha.8 has not changed.

## Finding and reference

The previous yaw string was one rigid rod, instantly rotated around the aircraft forward axis by twice native sideslip, capped at 35 degrees. It had no airspeed dependence, bending, response delay or still-air settling. Its near-aft orientation made that rotation a poor representation of motion over the glazing. Its tape was a vertical box rather than aligned to the canopy.

The [FAA Glider Flying Handbook, chapter 4, page 4-18](https://www.faa.gov/regulations_policies/handbooks_manuals/aviation/glider_handbook/gfh_chapter_4.pdf) describes the yarn as a local airflow indicator: aft in coordinated flight, with its free end outside a slipping turn and inside a skidding turn. The [Soaring Tigers photo collection](https://soaringtigers.org/photos.html) links an [in-flight yaw-string photograph](https://vanderbei.princeton.edu/IMG_5522.JPG) showing a thin, flexible red strand with nonuniform curvature. That photo uses a mast attachment; it is not evidence for an exact canopy-tape installation on a particular Mile High aircraft. Photographs are reference-only external files, not shipped assets.

We retain the requested canopy attachment as a defensible reference configuration. An 80 mm strand, 2.4 mm base diameter, response constants and tiny flutter amplitude are visual estimates, not measured operator rigging or validated fibre dynamics. The original string was approximately 79 mm long with the same base diameter.

## Implementation

- Replace the old rod with four constant-length links and five triangular rings. The tapered strand bends during airflow changes; the free end responds more slowly than its base.
- Reconstruct air-relative velocity from native JSBSim true airspeed, angle of attack and sideslip; add the angular-rate contribution at the attachment relative to CG. Project onto the canopy tangent. Positive native beta produces a leftward free end, not a rudder-command animation. Native wind/gust effects already present in air data remain included.
- Blend speed-squared airflow loading with a small gravity component. In still air the strand lies downhill on the glazing; ground wind can still deflect it. Use bounded, sub-degree, zero-mean flutter at flight speeds. This is an inexpensive presentation approximation, not canopy boundary-layer CFD or a stall/separation detector.
- Use native simulation timestamps, exponential response and fixed arrays. Repeated timestamps freeze the string; time reversal and aircraft re-enable reset it. Pause/hold freezes it after initial valid placement. Invalid samples cannot contaminate vertices. No native integration or simulation time is changed.
- Intersect the existing canopy mesh to place the attachment; align the existing tape to its tangent and keep the yarn just outside the glazing. Both authored prefab paths and future aircraft authoring use this setup. No tracked-camera, seat, control, flight authority or hierarchy edits.

## Resource and validation evidence

| Item | Result |
| --- | --- |
| SGS mesh triangles | 7,969 before and after; existing 8,000 ceiling unchanged |
| String triangles / vertices | 24 unchanged; 72 → 15 vertices |
| SGS renderers / materials / textures | 3 / 2 / 0 unchanged |
| New runtime objects | One small plain C# state holder and two fixed arrays per presenter; no GameObjects, renderers, cloth, colliders or materials |
| Warm helper sampling + vertex deformation | 10,000 updates in 24.366 ms, 0 managed bytes allocated; about 0.00244 ms/update on this Editor host |
| Software checks | 33/33: 14 yaw-string checks plus 19 existing SGS visual/control regressions |
| Android build validation | 61 player assemblies compiled; strict Android mesh/material asset bundle passed |
| Focused visual inspection | 20 final editor eye-offset images; full view and enlarged view of still air, coordinated flow, both sideslips and reversal; reviewed contact sheet and full-resolution examples |
| Standard visual QA | 107/130 pass; same 23 pre-existing failures; all 130 pass/resource/warning rows identical to previous ground/cloud baseline; maximum estimates 80 draws / 967,513 triangles / 37 materials |
| Required simulator stereo | BLOCKED_MISSING_RECORDED_REPLAYS; no live stereo acceptance claimed |
| Quest CPU/GPU/frame/thermal cost | Unmeasured; Editor microbenchmark is not Quest performance evidence |
| Integrated APK / installation | Not performed for this change; alpha.8 remains installed |

The standard gate remains failed on its existing limits; those limits were not raised. The first focused test invocation encountered a namespace qualification error in the new editor verification helper; it was fixed before the passing run. Initial previews included the pause panel; final editor-only captures hide that panel to inspect the resting strand without changing player focus policy.

Evidence and exact pre-edit rollback copies: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\yaw_string_20261024`. Final folders: `tests_final`, `verification_final`, `standard_visual`, `meta_xr_simulator`; hashes and scoped final source/mesh snapshot are alongside them. Prefab diffs contain only yaw-string binding changes. Restricted existing aircraft meshes remain local and uncommitted.

Later headset check: observe the yarn on the ground in calm/windy conditions, in coordinated glide, with small left/right sideslips and during a reversal; confirm readability in both eyes and freezing on pause. Verify frame timing on the integrated APK before claiming a hardware cost.
