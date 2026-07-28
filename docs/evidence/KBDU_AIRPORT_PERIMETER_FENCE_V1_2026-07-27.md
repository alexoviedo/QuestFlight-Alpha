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
- Selected and serialized geometry: 252 triangles.
- One dedicated project-authored material; new textures: zero.
- Terrain-conformed panel spans, bounded physical top rails/posts, and
  continuous perimeter-distance UVs render green vertical privacy slats with a
  galvanized diamond pattern.
- No LOD switching, realtime shadows, colliders, rigidbodies, transparent
  passes, runtime hierarchy construction, or per-frame script.

An initial detailed-geometry prototype was rejected because it exceeded the
unchanged 800,000-triangle product closure. The first 104-triangle analytic
candidate and subsequent sparse-rail candidates were rejected in repeated worn
reviews because only posts were visible. Pilot-left sightline sampling proved
terrain was not occluding the fence; the failure was inadequate screen
coverage. The accepted 252-triangle candidate uses terrain-conformed panels and
physical rails/posts while retaining four draws. Acceptance criteria and
product ceilings were not weakened.

## Validation

- Fence manifest validation: PASS; 4 sections, 4,273.191 m, at most 4 draws,
  one project-authored material, zero new textures, and 252 triangles.
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
- Final green-slat Android release-style build: PASS; APK
  `QuestFlight/Builds/Android/QuestFlight-production.apk`,
  137,698,121 bytes, SHA-256
  `00b487ab7d46bc147d3708336f8c88f213e67557edb4ea493be561096e9cec0f`.
  The APK is ignored build output and is not committed.
- Worn Quest 3 visual acceptance: 9/10. Alex confirmed green infill,
  galvanized mesh/top rail, continuous pilot-left coverage, and no flicker or
  clipping.

## Evidence paths

- Full Visual QA/contact sheet:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_fence_reference_20260727\visual_qa_analytic`
- Dedicated fence screenshots:
  `visual_qa_green_slat_fence\screenshots\37_airport_south_security_fence.png`
  and
  `visual_qa_green_slat_fence\screenshots\40_pilot_left_airport_fence.png`
- Static budget:
  `airport_fence_reference_20260727\performance_budget`
- Simulator stereo:
  `airport_fence_reference_20260727\simulator_retry`
- Android build log:
  `airport_fence_reference_20260727\build_green_slat_fence\unity_product_build.log`

## Limitations

The perimeter follows an OSM aerodrome boundary, not surveyed fence centerlines.
Gate positions are representative and not operational access information.
Section material character is based on City documents and inspected imagery,
not a complete current-condition inventory. The shader is optimized for
flight-simulator viewing distance and is not hero-quality close-range fence
hardware. No physical Quest timing or thermal gate was run for this APK.
