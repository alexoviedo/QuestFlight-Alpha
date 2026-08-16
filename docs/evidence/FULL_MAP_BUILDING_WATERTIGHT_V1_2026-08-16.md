# Full-map building watertight shells V1

## Scope and source

- Source branch: `feature/full-map-building-roofs-v1` at
  `987a685f06d808e3e182f367bae40b2af895518f`.
- Implementation branch: `feature/full-map-building-watertight-v1`.
- Scope is limited to sealing ordinary full-map procedural building walls to
  their retained projected roofs and to the authoritative terrain surface.
  Footprints, page selection, moving imagery, facades, airport hero models,
  runtime hierarchy, and flight systems are unchanged.

## Confirmed causes

The previous shell compiler used one centroid height for every wall-bottom
vertex. Terrain elevation varies across a footprint, so downhill wall edges
could terminate visibly above the terrain. It also authored flat wall tops but
independently raised roof perimeter vertices for gable, hip, and irregular
roofs. Those two independently computed boundaries could not be watertight.

The existing ear-clipping path had two additional deterministic failure modes:
boundary points could be treated as triangle interiors, and a partially
triangulated polygon could be accepted. A small number of source rings also
contained consecutive duplicate points. Together these cases could omit a roof
perimeter vertex even though the corresponding wall remained present.

## Implemented sealing contract

- Every wall-top endpoint now reuses the exact roof-perimeter position rather
  than recomputing a separate flat top.
- Every wall-bottom endpoint samples the authoritative
  `KBDU_ProductionTerrainSurfaceV2` at that footprint point and extends 1.60 m
  below the sampled surface. This bounded foundation skirt exceeds the
  documented 1.50 m simplified-terrain maximum error while retaining the same
  wall topology.
- Rare source footprint points just outside the bounded 12 km terrain domain
  sample the nearest authoritative boundary point. This affects only the
  footprint overhang at the map boundary.
- Consecutive duplicate ring points are removed. Signed area uses local-origin
  double precision, and roof triangulation must emit exactly `n - 2` triangles
  while referencing every perimeter vertex. Partial non-collinear results fail
  closed.
- Generated meshes are measured before acceptance. Every wall-top position
  must match a roof position within 1 mm, every wall bottom must meet the
  terrain-skirt contract, and every vertical wall corner must be shared.

The correction changes existing vertex positions only. It adds no renderer,
draw, material, transparent slot, runtime texture, hierarchy object, or
selected triangle cost.

## Compiler evidence

- Compiler: `terrain-sealed-shell-compiler-v7-20260816`.
- Represented buildings: 57,407 across 576 pages and the existing 16-renderer
  page pool.
- Terrain-sealed wall vertices: 485,182 / 485,182.
- Roof-sealed wall vertices: 485,182 / 485,182.
- Maximum measured roof-to-wall seam: 0.0 m.
- Maximum measured vertical-corner seam: 0.0 m.
- Foundation embed: 1.60 m.
- Maximum active-window building triangles remain 139,594.
- Footprint compiler constraints remain bounded: maximum inflation 4.999911%,
  maximum centroid shift 2.999870 m, and maximum boundary error 10.0 m.

## Validation and visual review

- Full EditMode: PASS 327/327, including the new authoritative-terrain,
  exact-roof-perimeter, and open-corner guards.
- PlayMode: PASS 10/10.
- Product integrity during the release build: PASS; 3,200 assets, 2,017 scene
  objects, 121 renderers, 659,873/800,000 serialized triangles, and 37
  materials.
- Product dependency closure: PASS; 595 dependencies, 401 approved, 194
  package-owned, zero local, zero rejected, and zero unknown.
- Visual QA generated 123 nonblank captures. Full-resolution residential-low,
  facade, takeoff-left, residential, and airport-building views were inspected
  against the projected-roof baseline. Previously visible sloped-roof perimeter
  holes and floating downhill wall bottoms are absent in these captures; roof
  projection, footprint placement, and facades remain intact.
- Aggregate Visual QA remains `FAIL_MACHINE_QA` only for the inherited 38/29
  material planning target. Maximum estimated draws remain 77 and maximum
  estimated visible triangles remain 655,103; this correction adds no cost.
- Meta XR Simulator produced a connected live stereo session, but its machine
  gate is `BLOCKED_MISSING_RECORDED_REPLAYS`: ten required VRS recordings are
  unavailable, and the Windows player cannot load Android-only aerial/building
  AssetBundles. Shipping Android settings were restored. No stereo pass is
  claimed.
- Android ARM64/Vulkan/OpenXR release-style build: PASS; 1,483,937,488 bytes;
  SHA-256
  `e72f8a28882f99141b1a77712793becd91e2a45fcd031f5ed6cc90df079feab9`.
- Exact APK install to connected Quest 3 `2G0YC5ZG8907TD`: PASS. Worn visual,
  frame-time, and thermal acceptance remain pending.

## Evidence paths

External artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_building_watertight_v1_20260816_001`

It contains the safety bundle, compiler logs, EditMode and PlayMode XML,
full-resolution Visual QA captures, simulator reports, dependency closure, and
Android build manifest. Generated meshes, bundles, captures, and the APK remain
outside Git.

## Known limitations

- The 1.60 m below-terrain foundation is a hidden robustness skirt, not a
  surveyed foundation or terrain excavation model.
- Buildings remain procedural approximations of source footprints and tags.
  Roof shape, facade detail, and height are not surveyed architectural truth.
- The vertical orthophoto roof projection retains source-image baked lighting
  and remains limited at close grazing angles.
- This work does not claim literal photorealism, FAA approval, navigation
  accuracy, training credit, certified flight fidelity, or measured Quest
  performance.
