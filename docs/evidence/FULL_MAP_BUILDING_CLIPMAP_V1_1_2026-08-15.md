# Full-map building clipmap V1.1 transition correction

## Scope and source

- Repository source: `feature/full-map-building-clipmap-v1` at `5403fbba72b6616d2a41595416da7f002836446d`.
- Correction branch: `feature/full-map-building-clipmap-v1-1`.
- Scope is limited to building representation, page ownership, AssetBundle
  residency, deterministic transition tests, documentation, and validation.
- Building geometry, textures, material semantics, hero airport authority,
  terrain, imagery, vegetation, flight, cockpit, and input are otherwise unchanged.

## Proven V1 root causes

1. The LOD1 compiler retained only 618 of 57,407 components; 56,789 components
   (98.92%) disappeared at the first threshold. LOD2 retained 184.
2. Slot ownership was local-index based. A one-cell shift reassigned and loaded
   all 16 pages instead of retaining 12; a diagonal shift loaded 16 instead of 7.
3. Pages changed sequentially, exposing mixed old/new windows during travel.
4. Prediction selected the visible window instead of serving only prefetch.
5. Every page request reopened a bundle and successful loads called
   `AssetBundle.Unload(false)`, the Unity-documented duplicate-object hazard.

## Implemented representation

- Each of 576 pages has an immutable `coverage` mesh and `detail` mesh.
- Coverage keeps all 57,407 roofs. Buildings at least 1,000 square metres and
  landmarks also retain far walls.
- The page containing the aircraft uses exact source footprints and full walls;
  its coverage renderer is disabled so there is no overlap or z-fighting.
- No alpha crossfade, dither, billboard, runtime mesh construction, or new
  material was introduced.
- The exact exhaustive generated-mesh active-window maximum is 109,166 building triangles.
  Replacing the V1 45,213 maximum gives an estimated complete-scene maximum of
  687,585 triangles, below the 700,000 correction ceiling.

## Implemented streaming contract

- The 4x4 visible window is selected from current aircraft position and centered
  symmetrically where map bounds permit.
- Prediction supplies a bounded prefetch residency set only.
- PageKey ownership retains unchanged slots. One-axis shifts retain 12 and load
  4; diagonal shifts retain 9 and load 7.
- All entering assets load before the old fringe is detached and the new window
  is committed.
- A generated index records CRC and content hash for all 144 LZ4 Android region
  bundles. A region is opened once while resident and released with
  `UnloadAsync(true)` only after its pages leave active and prefetch residency.

## Validation record

- Offline authoring and serialized product contract: PASS; 57,407 components,
  576 pages.
- Generated bundles: 144; 81,434,599 bytes; CRC/hash index present.
- Deterministic budget: 109,166 maximum active building triangles.
- EditMode: PASS, 325/325.
- PlayMode: PASS, 10/10.
- Standard Visual QA produced 123/105 required captures. All building-specific
  views passed inspection. The aggregate machine gate reports the inherited
  scene-wide 77/75 draw-ceiling excess; this correction did not add an active
  building draw.
- Measured Visual QA maximum: 77 draws, 654,442 triangles, 38 materials.
- Android release-style build: PASS. APK size 1,516,141,564 bytes; SHA-256
  `d7015482219e96fc23fc41d77f10b38b24efde0b5d88163fcae1c9d591b43e9e`.
- Installed Quest launch: PASS. Device log validated the airport benchmark and
  recorded an atomic page shift with 12 retained pages and four entering pages.
- Meta XR Simulator aggregate wrapper: `BLOCKED_TOOLING_TIMEOUT`; it produced
  no final report and was terminated without changing the product. Standard
  full-resolution Visual QA and the physical-Quest launch evidence remain valid.
- Physical visual judgment and hardware timing are not inferred from launch or
  log evidence.

## Limitations

- Ordinary small coverage buildings are roof-only until their page contains the
  aircraft. This is deliberate triangle-budget control.
- Exact-to-simplified roof outline changes can remain at a page boundary even
  though building membership and mass persist.
- This evidence does not claim literal photorealism, FAA approval, navigation
  accuracy, training credit, or Quest performance acceptance.
