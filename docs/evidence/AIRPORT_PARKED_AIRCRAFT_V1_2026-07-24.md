# Airport Parked Aircraft V1

## Outcome

The takeoff/landing corridor now contains 17 explicitly authored static aircraft:
seven powered C172-derived aircraft on the south apron and ten sailplanes on the
north glider line. Their orientation follows the parking silhouettes visible in
the pinned 2019 NAIP ground image. Every placement is grounded from the source
mesh's lowest point to the sampled production terrain plus a 0.025 m clearance.

This is standalone Quest development evidence, not current ramp occupancy,
surveyed placement, manufacturer endorsement, or physical performance
acceptance.

## Source and rights

- Powered aircraft: KOG_THORNS `Cessna172`, CC BY 4.0, source SHA-256
  `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`.
- Sailplane: `Glider Flying` by app4soft / EGPJET from OpenGameArt, CC0 1.0.
  The external `FFGL.blend` is 3,966,104 bytes with SHA-256
  `c367729605b0d67d1d989d18be1ca92b0279037006110858f1e7fc00909cd077`.
- Raw sailplane source and its 2,686,228-byte source archive remain external.
- Both committed derivatives remove interiors, animation, interaction, source
  textures, and source materials; retain the recognizable exterior, gear, and
  canopy; use deterministic vertex colors; and are ground-pivoted.

The committed powered derivative is 101,168 bytes with SHA-256
`2c2c8154343d24dd093c878c1f94b33ba7a71445eb886fbf93d119452df7e391`.
The committed sailplane derivative is 36,668 bytes with SHA-256
`cb69e85643cdefc3a90f4376f98f4e6d19319117a94268ad09ada9382a9bb50b`.
Exact generation and change records are in
`AirportParkedAircraftSourceManifestV1.json`.

## Runtime representation

- One logical parked-aircraft batch, one shared opaque material, and one
  non-crossfaded `LODGroup`.
- LOD0: 11,783 triangles for all 17 aircraft.
- LOD1 and LOD2: 204 triangles each as distant aggregate silhouettes.
- Screen-relative thresholds: 0.008, 0.005, and 0.002.
- No per-aircraft GameObjects, scripts, animation, colliders, transparency, or
  per-frame updates.
- The focused authoring path updates only the parked-aircraft meshes, material,
  bounds, and thresholds in the existing serialized context prefab. It does not
  reconstruct the product hierarchy.

## Visual review

Production Visual QA passed 92/92 deterministic captures with classification
`PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`. The lead inspected the three
new full-resolution views and the contact sheet:

- `29_south_powered_aircraft.png`: powered aircraft are upright, consistently
  scaled, and clear of the terrain.
- `30_north_sailplane_line.png`: sailplanes are upright, grounded, and aligned
  with the north-side glider line.
- `31_aircraft_orthophoto_alignment.png`: all 17 authored placements are visible
  in the bounded overhead alignment view.

The maximum captured planning load was 56 draw calls, 638,407 visible
triangles, and 29 visible materials.

## Validation

- Airport parked-aircraft EditMode contracts: PASS as part of 125/125 EditMode
  tests.
- PlayMode: PASS 9/9.
- Product integrity and dependency closure: PASS after adding the already
  accepted `AirportHeroCorridorRoot.cs` to the product-authored allowlist.
- Android ARM64/Vulkan/OpenXR release-style build: PASS with Unity 6000.3.8f1.
  The external APK is 135,844,488 bytes, SHA-256
  `23894176073b694e63fedb1f244fdedb24faee8bd6eb592824d369d3d8584c59`.
- Static geometry/draw/material checks pass at 56/75 draws, 638,407/650,000
  visible triangles, 29/29 visible materials, and three/three transparent
  slots.
- The aggregate static performance classification is
  `FAIL_STATIC_QUEST_BUDGET` solely because the conservative imported-texture
  estimate is 211.25 MiB versus 210 MiB. The aircraft derivatives add no
  textures; the overage already exists in the accepted airport hero-corridor
  source. No threshold was weakened and no physical Quest performance is
  inferred.

## Limitations

The 2019 aerial image is a historical placement reference, not proof of present
ramp occupancy. Some photographed aircraft remain in the orthophoto because a
new clean plate was outside this task. Distant aggregate LODs prioritize stable
silhouette and cost over individual aircraft detail. The exact APK has not had
a worn visual or timed Quest run.
