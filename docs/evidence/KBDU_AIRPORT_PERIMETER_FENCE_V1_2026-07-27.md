# KBDU Airport Perimeter Fence V1 Evidence

## Source and scope

- Product source: `feature/quest-road-traffic-v1` at
  `3ce4eb241f35182bad70d2d0255cbb131296a7f4`.
- Implementation branch: `feature/airport-perimeter-fence-v1`.
- Unity: `6000.3.8f1`.
- Product target remains standalone Quest 3, Android ARM64, Vulkan, OpenXR.
- No aircraft, cockpit, traffic, terrain, water, physics, input, or runtime
  hierarchy architecture was changed.

The City sources establish mixed perimeter/security-fence conditions rather
than one uniform enclosure. The authored visual perimeter follows the bounded
OpenStreetMap aerodrome boundary while using northeast/northwest wildlife,
south black chain-link, and west-hangar weathered treatments. It must not be
used as surveyed security, navigation, or current access-control data.

## Provenance

Raw OSM and reference photographs remain external under:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_fence_reference_20260727`

| Item | Status | SHA-256 |
| --- | --- | --- |
| Raw OSM snapshot | external only; ODbL 1.0 | `b429bea7576869c0a35443c07c0179874f9e0fb8e827b8d577e5c1db94c7899a` |
| Wikimedia airport photograph | external reference only; CC BY 4.0 | `250445c55b8a2023e5f4f9b17a8691597ca74926191f0965b1b139e67858db30` |
| Airport-Data terminal photograph | external reference only | `a3409e3de51751b481512f9e06c09c4c2e26f46e6fb5d77c0a176c8bdf0c6c2f` |
| Compact fence authority JSON | committed ODbL-derived data | `6db6987c22250117e55af6621dbb22abfc53a9b135d564649b315b79a1338c35` |

No external photograph, raw OSM extract, downloaded model, or new texture is
committed. `build_airport_fence_manifest.py` deterministically compiles the
reviewed boundary vertices, section contracts, and attribution. The four Unity
mesh assets and shader treatment are project-authored derivatives.

## Implementation

- One serialized `AirportPerimeterFenceRoot` under `AirportContext`.
- Four fixed meshes, one renderer per documented section.
- Total length: 4,273.191 m.
- Selected and serialized geometry: 104 triangles.
- Existing shared airport material: one; new materials/textures: zero.
- Analytic chain-link, posts, and rails use continuous perimeter-distance UVs,
  shader derivatives, alpha clip, and 4x-MSAA alpha-to-coverage.
- No LOD switching, realtime shadows, colliders, rigidbodies, transparent
  passes, runtime hierarchy construction, or per-frame script.

An initial detailed-geometry prototype was rejected because it exceeded the
unchanged 800,000-triangle product closure. A 224-triangle solid-strip fallback
passed tests but was rejected during full-resolution review because it read as
a single diagonal line. The shipped candidate preserves the 104-triangle strip
budget while providing recognizable open mesh through the analytic shader.
Acceptance criteria and product ceilings were not weakened.

## Validation

- Fence manifest validation: PASS; 4 sections, 4,273.191 m, at most 4 draws,
  zero new materials and textures.
- Product integrity: PASS; 1,115 assets, 367 scene objects, 101 renderers,
  799,839/800,000 serialized triangles.
- EditMode: PASS 136/136.
- PlayMode: PASS 9/9.
- Production Visual QA: all 100 captured views pass their image-integrity
  checks. Dedicated full-resolution views 37–39 were inspected and show an
  open fence at ground/oblique range with a suitable high-altitude fallback.
- Aggregate Visual QA classification remains `FAIL_MACHINE_QA` because its
  older generic thresholds are 600,000 visible triangles and 25 materials.
  The actual accepted product report is 638,538/650,000 triangles and 29/29
  materials; this fence adds no material and no texture.
- Static Quest planning report: draw calls 60/75, visible triangles
  638,538/650,000, materials 29/29, transparent slots 3/3, and 4x MSAA pass.
  Estimated texture residency remains the inherited
  221,506,450/220,200,960-byte failure; the fence adds zero texture bytes.
- Meta XR Simulator v201: live Quest 3 profile stereo/HMD/both Touch connection
  PASS; both eyes are nonblank and scene-consistent. Aggregate status remains
  `BLOCKED_MISSING_RECORDED_REPLAYS` for the ten absent genuine `.vrs` files.
  Simulator evidence is not Quest hardware performance evidence.
- Android release-style build: PASS; APK
  `QuestFlight/Builds/Android/QuestFlight-production.apk`,
  137,116,114 bytes, SHA-256
  `5abf73962f72c6a6675d2a0e3ac83e6ba96976fda940753a6adfb4c5e45f377f`.
  The APK is ignored build output and is not committed.

## Evidence paths

- Full Visual QA/contact sheet:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_fence_reference_20260727\visual_qa_analytic`
- Dedicated fence screenshots:
  `visual_qa_analytic\screenshots\37_airport_south_security_fence.png`,
  `38_airport_west_hangar_fence.png`, and
  `39_airport_fence_overview.png`
- Static budget:
  `airport_fence_reference_20260727\performance_budget`
- Simulator stereo:
  `airport_fence_reference_20260727\simulator_retry`
- Android build log:
  `airport_fence_reference_20260727\build_final\unity_product_build.log`

## Limitations

The perimeter follows an OSM aerodrome boundary, not surveyed fence centerlines.
Gate positions are representative and not operational access information.
Section material character is based on City documents and inspected imagery,
not a complete current-condition inventory. The shader is optimized for
flight-simulator viewing distance and is not hero-quality close-range fence
hardware. No physical Quest timing or thermal gate was run for this APK.
