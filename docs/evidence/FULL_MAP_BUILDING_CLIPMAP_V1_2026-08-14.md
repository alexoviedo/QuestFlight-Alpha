# Full-Map Building Clipmap V1 evidence

## Decision

The feature replaces the fixed airport-distance context-building subset with an
aircraft-centered, page-streamed representation across the existing 24 km map.
It is a feature-branch candidate pending physical Quest visual and performance
acceptance. It is not a claim of surveyed geometry, literal photorealism, or
release readiness.

## Source and compilation

- source branch/SHA: `main` / `feef31432b41775e094ffa3a372ecfc89cf3aede`;
- City source: 51,258 active records, with 42,780 retained components;
- County fallback: 23,847 records, with 14,627 retained components;
- final database: 57,407 components in 576 one-kilometre pages, 482 nonempty;
- temporal contract: features known to post-date the authoritative 2020 ground
  imagery are excluded;
- airport hero exclusion: 356 source components remain owned by the accepted
  authored airport corridor;
- 28,726 components receive footprint-aware gable or hip roof profiles;
- 155 named/large landmark components retain their official multipart
  footprints and source heights, but are not bespoke landmark art.

The compact database SHA-256 is
`2298f3c861a6fcf46d0ef9f96860d60cd4c5c629e3bfa8d2da178dbfe68c0f46`.
Raw City and County responses remain external. Exact source hashes and
attribution are in the adjacent provenance manifest.

## Runtime representation

`AircraftCenteredBuildingClipmapController` owns a fixed 4x4 pool of 16
serialized mesh renderers. It follows the aircraft simulation frame, predicts
2.5 seconds ahead, and swaps precompiled page/LOD meshes loaded from local
Android AssetBundles. It does not follow or write the XR camera, construct
hierarchy, create runtime meshes, or allocate one GameObject per building.

- LOD0: all source-qualified buildings through 650 m;
- LOD1: simplified significant buildings through 1,500 m;
- LOD2: landmark/large silhouettes beyond 1,500 m inside the active window;
- ordinary distant structures remain represented by the 2020 orthophoto;
- one opaque material, three 1024-square texture maps, zero transparent slots;
- compiled payload: 144 region bundles, 582 files, 65,798,359 bytes;
- measured authoring maximum: 11 active page draws and 45,213 building
  triangles for sampled aircraft positions.

The previous generic building batches are inactive. The accepted airport hero
buildings remain active, and their source footprints are excluded from the new
database to prevent duplicates.

## Materials and geometry

The one 4x4 project-owned atlas contains semantically grouped residential,
commercial, industrial, institutional, utility, roof, and opaque reflective
window responses. Meter-space UVs preserve texel scale. Mirrored local repeat
avoids atlas-edge seams. Windows are opaque procedural facade details with a
bounded static cubemap glint; they add no transparency or reflection camera.

Exterior-wall triangle winding was corrected after full-resolution review
showed back-face-culling gaps. Roof selection is class-aware rather than random:
residential buildings favor shingles and pitched roofs, commercial/institutional
buildings favor membrane roofs, and industrial/utility buildings favor metal.

## Automated and inspected evidence

- source/database validator: PASS, 57,407 components, 100% represented coverage,
  zero airport-hero centroid overlaps, maximum footprint inflation 0;
- final EditMode: PASS 323/323, zero failures or skips;
- final PlayMode: PASS 10/10, zero failures or skips;
- final Editor Visual QA: 123 captures / 105 required, all capture/image checks
  pass;
- full-resolution building views inspected: CU/downtown, residential, North
  Boulder, facade-low, and residential-low;
- inspection result: closed walls, aligned footprints, coherent pitched roofs,
  and no material-grid seams;
- aggregate machine classification remains `FAIL_MACHINE_QA` solely because
  the inherited complete-scene material count is 38 against the older planning
  target of 29. This feature adds one material and removes the prior context
  material path; final material count is unchanged from its immediate baseline;
- immediate baseline/current maximum views: 80/71 draws and
  694,164/623,632 selected triangles, a reduction of 9 draws and 70,532
  triangles in the inspected camera set.

The aggregate `validate_product.ps1` run passed source/license validation,
dependency closure, EditMode, and PlayMode, then stopped at the Visual QA
policy check described above: 38 complete-scene materials against the inherited
planning target of 29. All 123 captures passed image-validity checks. This is an
honest partial product-gate result, not a full validation pass.

Meta XR Simulator setup 201.0 used the Quest 3 / 72 Hz profile and restored all
host state, but its result is `BLOCKED_MISSING_RECORDED_REPLAYS`: all ten
required `.vrs` sessions are absent and no passing live-stereo result was
produced. There were no replay failures because no replay files existed.

The Android ARM64/Vulkan/OpenXR release-style build passed. The external APK is
`QuestFlight/Builds/Android/QuestFlight-production.apk`, 1,505,222,611 bytes,
SHA-256
`bcdda4d8a0297e953afc9ace5854d66d25780bfee4af0c086c2537a0c170ab71`.
It is 17,430,589 bytes smaller than the immediately preceding full-map aerial
clipmap APK despite adding the 65,679,541-byte compressed building-streaming
payload. The APK and generated bundles are not committed.

Editor and simulator results do not establish Quest hardware timing. Physical
Quest visual, LOD-transition, frame-time, and thermal acceptance remain pending.

## Known limitations

- Facades are deterministic procedural approximations, not photographs of each
  real facade; door/window placement is semantic rather than surveyed.
- Official footprints and source heights vary in completeness and age.
- Named landmarks retain correct footprint masses but do not yet have bespoke
  recognizable roof furniture or facade art.
- The 4x4 streamed window deliberately leaves small ordinary distant buildings
  to the orthophoto to control draw and triangle cost.
- No final Quest frame-time, thermal, worn LOD-transition, or comfort acceptance
  is claimed until the exact candidate APK is tested on hardware.

## Evidence paths

- external artifact root:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_buildings_v1_20260814_155752`;
- final contact sheet: `visual_qa_v3/visual_qa_contact_sheet.png`;
- building validation: `full_map_building_validation_final.json`;
- authoring log: `unity_building_authoring_v7.log`;
- aggregate validation: product extraction artifact
  `validate_product_20260814_175359`;
- final EditMode XML: product extraction artifact
  `product_unity_tests_20260814_175115/unity_EditMode_test_results.xml`;
- final PlayMode XML: product extraction artifact
  `product_unity_tests_20260814_175308/unity_PlayMode_test_results.xml`;
- simulator evidence: product artifact
  `standalone_recovery_20260815_000813/meta_xr_simulator`;
- Android build log: product extraction artifact
  `product_build_20260814_181747/unity_product_build.log`.
