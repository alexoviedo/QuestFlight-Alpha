# Full-map building clipmap V1.2 stable shells

## Scope and source

- Source branch: `feature/full-map-building-clipmap-v1-1` at
  `0d85c58354d515859110245c9f207cfbe77f1e1c`.
- Implementation branch: `feature/full-map-building-clipmap-v1-2`.
- Scope is limited to eliminating representation-dependent building-wall
  popping. Terrain, imagery, vegetation, hero buildings, cockpit, aircraft,
  input, native JSBSim authority, and rendering architecture are unchanged.

## Proven root cause

V1.1 generated roof-only coverage geometry for 56,042 of the 57,407 ordinary
building components. The page containing the aircraft disabled its coverage
renderer and substituted an exact full-wall detail renderer. Crossing a page
boundary therefore changed whether complete walls existed. Device logs had
already shown atomic page changes with 12 retained and four entering pages, so
the defect was not a streaming-delay race; it was the representation swap.

## Implemented correction

- Every building is compiled into one opaque closed shell containing its roof
  and all exterior walls.
- Each of the 576 source pages has one stable runtime mesh. The serialized pool
  contains 16 renderers and no aircraft-page detail slot.
- Landmarks retain exact source footprints. Ordinary buildings use a
  deterministic, topology-safe offline simplifier. The same simplified ring
  authors walls and roof with consistent winding.
- Compiler gates reject non-simple footprints and enforce maximum ordinary-shell
  errors of 30% area, 5% inflation, 3 m centroid shift, and 10 m boundary error.
- Runtime window selection remains aircraft-centered; prediction remains
  prefetch-only; entering pages still load before atomic commit.
- No transparent transition, dither, billboard, crossed-wall card, material,
  renderer type, runtime mesh construction, or product hierarchy was added.

## Compilation and budget evidence

The committed compilation report records:

- 57,407 represented components;
- 155 exact landmarks;
- 52,730 four-vertex shells;
- 3,949 six-vertex shells;
- 728 higher-vertex shells;
- maximum area error 29.9918%;
- maximum inflation 4.99618%;
- maximum centroid shift 2.99987 m;
- maximum boundary error 10.0 m;
- conservative compiler active-window bound 139,594 building triangles;
- exact generated-mesh active-window maximum 139,588 building triangles;
- one opaque building material and at most 16 building-page draws.

The building triangle maximum is below the milestone's 140,000 correction
ceiling. This is a bounded correction budget, not physical Quest performance
evidence.

## Validation

- Stable-shell authoring and serialized product contract: PASS.
- EditMode: PASS 325/325.
- PlayMode: PASS 10/10.
- Product dependency closure: ready, zero rejected and zero unknown.
- Full-resolution building views at 500 m, 1 km, 3 km, facade distance,
  residential areas, CU/downtown, and North Boulder: per-view PASS and manually
  inspected for closed walls and gross shell distortion.
- Aggregate Visual QA: `FAIL_MACHINE_QA` because the inherited scene contains
  38 materials against an older 29-material planning target. One unrelated view
  is two draws above its local target and another is 5,103 triangles above its
  local target. The stable-shell change adds no material or transparent slot.
- Meta XR Simulator: `BLOCKED_MISSING_RECORDED_REPLAYS`; all ten required VRS
  recordings are absent. The runner preserved shipping Android configuration.
- Android release-style build: PASS. APK SHA-256:
  `20719a6a4d2712447a15e9f5620de27e6ab80df98487ce576c3d9b8dadb2824d`.
- Physical Quest wall-transition and sustained 72 Hz/thermal acceptance: not run
  and not inferred.

## Evidence paths

External artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\full_map_building_clipmap_v1_2_20260815_142252`

It contains the safety bundle, compiler logs, test XML, full-resolution Visual
QA, simulator reports, product-validation output, and Android build evidence.

## Limitations

This correction removes the proven wall representation pop. It does not claim
surveyed facade fidelity, literal photorealism, FAA approval, navigation
accuracy, training credit, certified flight fidelity, or physical Quest
performance acceptance.
