# Full-map building roofs V1

## Scope and source

- Source branch: `feature/full-map-building-clipmap-v1-2` at
  `5c9f0080d5fbd7cf570e65d56ba2d88e9d3c087c`.
- Implementation branch: `feature/full-map-building-roofs-v1`.
- Scope is limited to the visible roof finish of ordinary full-map building
  shells. Their footprints, heights, pitched/flat geometry, walls, page
  partitioning, runtime selection, and colliders are unchanged.
- Authored airport hero buildings retain their authored materials. The airport
  clean plate removes their photographed roofs, so projecting that imagery
  onto those models would not reproduce the source building.

## Implemented representation

The full-map building shader now treats the existing roof vertex flag as a
strict semantic branch. Roof fragments sample the same georeferenced 2020
orthophoto used by the terrain, using world-space `XZ` coordinates and the same
fixed near/mid/far mappings. At runtime, the aircraft-centered controller binds
the same nine near and nine medium texture-array pages and mapping vectors to
both terrain and roof material in one update. Until a moving bundle is ready,
both materials use matching serialized fallback imagery.

The shared `AerialClipmapSampling.cginc` owns the page selection and derivative
sampling contract so terrain and roofs cannot silently diverge. Roof geometry
continues to provide silhouette, pitch, normals, lighting, and roughness.
Facade fragments continue through the existing semantic atlas, window, and
reflection path.

The correction adds no renderer, draw, material, transparent slot, runtime
texture, shader keyword, hierarchy object, or runtime material construction.
The accepted inventory remains 57,407 represented components, 576 compiled
pages, and a 16-renderer building-page pool.

## Validation and visual review

- Deterministic authoring/serialization validation: PASS for all 57,407
  components, 576 pages, the stable 16-renderer pool, and the matching terrain
  and building imagery bindings.
- Product integrity: PASS; 3,179 assets, 2,017 scene objects, 121 renderers,
  659,873/800,000 serialized triangles, and 37 materials.
- Product dependency closure: PASS; 595 dependencies, 401 approved, 194
  package-owned, zero local, zero rejected, and zero unknown.
- EditMode: PASS 326/326.
- PlayMode: PASS 10/10.
- Android ARM64/Vulkan/OpenXR release-style build: PASS; 1,481,910,892 bytes;
  SHA-256
  `76471e5cd2920a65e53bdb6cc7645aa72398f6a98c094961014c53cf2dc171e6`.
- The exact APK was installed successfully on connected Quest 3
  `2G0YC5ZG8907TD`; worn visual acceptance and hardware timing were not run and
  are not inferred.
- Visual QA produced 123 nonblank 1920x1080 captures. The lead inspected the
  dedicated top-down, 500 m, 1 km, 3 km, facade, CU/downtown, residential,
  North Boulder, takeoff-left, and airport views against the V1.2 baseline.
  Photographed roof colors and features follow the retained roof geometry and
  source footprints; the former generated roof-color checkerboard is absent.
  No new roof offset, wall hole, duplicate footprint, or geometry pop was
  observed in the machine captures.
- Aggregate machine Visual QA remains `FAIL_MACHINE_QA` only because the
  inherited scene contains 38 materials against the older planning target of
  29. This feature adds zero materials.
- Meta XR Simulator result is `FAIL_SIMULATOR_ORCHESTRATION`: its Windows
  development-player build asserted while writing `sharedassets0.assets`, and
  the ten recorded VRS inputs remain unavailable. Android shipping settings and
  the active runtime were restored. No stereo pass is claimed.

## Evidence paths

External artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_building_roofs_v1_20260816_001`

It contains the safety bundle, authoring output, test XML, full-resolution
captures and contact sheets, simulator logs, and Android build manifest.

## Known limitations

- A vertical orthophoto projection preserves source roof color and aerial
  detail; it does not create surveyed roof construction, true facade detail,
  or hidden roof surfaces absent from the image.
- The source image includes baked illumination, shadows, and occasional
  photographed objects. Geometry lighting can therefore differ under future
  time-of-day changes.
- Very close, grazing-angle inspection remains limited by orthophoto ground
  sample distance and texture residency. Physical Quest image and performance
  acceptance require the installed-APK worn gate.
- This is not a claim of literal photorealism, FAA approval, navigation
  accuracy, training credit, certified flight fidelity, or measured Quest
  performance.
