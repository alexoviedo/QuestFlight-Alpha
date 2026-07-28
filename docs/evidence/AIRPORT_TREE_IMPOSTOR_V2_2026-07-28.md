# Airport Tree Hybrid LOD V2 evidence

Date: 2026-07-28

Branch: `feature/airport-tree-impostor-v2`

Accepted source checkpoint:
`6aba45a255cc1c00d0fa4e8d10c46be9bfa59563`

Rejected comparison:
`feature/airport-close-tree-lod-v1` at
`a2812ee`

## Decision

The rejected V1 close-tree system selected only eight near and eight middle
trees, changed LOD directly from distance every pre-cull, and represented
foliage with opaque geometric diamonds. The worn result scored 3/10 because
crowns were sparse, transitions popped badly, and the airport trees did not
look materially better.

V2 preserves the accepted fence checkpoint and replaces that tree path with
one bounded hybrid representation:

- LOD0 retains source-proportional trunk, branch, and leaf-card geometry;
- LOD1 uses reduced three-dimensional foliage clumps;
- LOD2 uses one world-upright, center-eye-selected eight-azimuth impostor;
- projected-size thresholds, asymmetric hysteresis, minimum residency, and
  bounded alpha-to-coverage transitions replace instantaneous distance swaps;
- all 696 placements remain available to the persistent renderer;
- one existing canopy material services all three tiers in three maximum
  draws.

This is a Quest-bounded visual archetype improvement, not a botanical survey
or a claim of final photorealism.

## Source and provenance

- Source: Poly Haven `Tree Small 02`
- Source URL: <https://polyhaven.com/a/tree_small_02>
- License: CC0 1.0, <https://polyhaven.com/license>
- External source blend SHA-256:
  `af3fef6da3eb97057d9d4d3c4281c5944696b8d518715deddd8500482d30b1bd`
- Blender: 5.1.2
- Compiler: `tools/airport_close_trees/build_airport_close_tree_lods.py`
- Raw blend, source textures, and intermediate frames: external artifact only
- Committed runtime derivative hashes:
  `AirportCloseTreeLods_SourceManifest.json`

## Runtime representation

| Tier | Representation | Geometry |
| --- | --- | ---: |
| LOD0 | source leaves plus 42 supplemental volumetric clumps | 2,337 triangles |
| LOD1 | 34 volumetric crossed-foliage clumps | 194 triangles |
| LOD2 | eight-view center-eye impostor | 2 triangles |
| Maximum tree draws | one per tier | 3 |
| Maximum transition triangles | persistent transition envelope | 22,066 |
| Runtime material growth | existing shared canopy material | 0 |
| Runtime texture growth | two ASTC atlases | about 1.49 MiB |

The shader uses the same frame for both eyes and rotates the far quad only
around world up. The renderer performs no per-tree `Update`, preallocates its
state and draw buffers, culls behind/outside the view, and applies projected
screen-size selection with angular and temporal hysteresis.

## Validation

- Deterministic Unity authoring: PASS.
- ProductIntegrity: PASS; 1,129 assets, 368 scene objects, 104 renderers,
  799,781 serialized triangles under the photoreal-aerial-surface ceiling,
  and 28 serialized materials.
- Production dependency closure: PASS; 337 dependencies, 332 approved
  project assets, five Unity package dependencies, zero rejected, and zero
  unknown.
- EditMode: PASS, 139/139.
- PlayMode: PASS, 9/9.
- Production Visual QA: 101/101 images captured and nonblank.
- Full-resolution lead inspection: source leaf/trunk structure is present at
  close range and the far representation remains bounded. Worn validation is
  still required to accept transition quality.
- Visual-QA aggregate budget: 63 draws, 645,200 visible triangles, 30 visible
  materials.
- Static gate: draw and triangle ceilings pass. The aggregate report remains
  red for pre-existing accepted-checkpoint debt: 30 versus 29 visible
  materials, 212.73 versus 210 MiB estimated textures, and 7 versus 3
  transparent slots. The tree change adds no material or transparent slot and
  adds about 1.49 MiB of ASTC texture residency.
- Meta XR Simulator live gate: PASS for the rebuilt player. Meta XR Simulator
  201.0 reported the Quest 3 profile, 72 Hz request, Single Pass Instanced
  stereo, tracked HMD, and both Touch Plus controllers. Both eyes were
  nonblank and the full-resolution neutral stereo pair was inspected.
- Aggregate simulator replay gate:
  `BLOCKED_MISSING_RECORDED_REPLAYS`; the ten required genuine `.vrs` sessions
  do not exist. Runtime activation and persistent replay configuration were
  restored, and Android shipping configuration was unchanged. Simulator
  timing is not Quest performance evidence.
- Python aerial-surface unit suite: 41/42 pass. The remaining check compares
  regenerated geospatial floating-point coefficients for exact JSON equality;
  this machine differs from the committed values only around 10^-15 while all
  explicit transform residual tolerances pass. No runtime geospatial asset was
  regenerated and the validator was not weakened.
- Physical Quest timing, thermals, and worn transition quality: NOT RUN.
- Android ARM64/Vulkan/OpenXR release-style build: PASS using Unity
  6000.3.8f1.
- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`,
  140,559,717 bytes, SHA-256
  `1d121f456b8689b660ba00c7ef346af6e37de280372c0561595a46b401fc5937`.
  The APK is excluded from Git.

## Visual review

The inspected close view shows more organic source-leaf silhouettes, visible
trunk/branch structure, and fewer opaque geometric foliage shapes than V1.
The pattern-altitude and takeoff views preserve canopy coverage. Automated
still captures cannot prove XR transition quality, so the rejected V1 branch
remains preserved and V2 must not merge to `main` without a successful worn
comparison and the outstanding baseline budget debt being resolved.

## Limitations

The placement manifest remains a deterministic semantic approximation rather
than a surveyed inventory. Only the highest projected-size trees use source
leaf geometry. The middle tier deliberately trades botanical detail for
bounded volume, and the far tier deliberately drops stereo depth where its
projected size is low. This milestone does not establish Quest frame timing,
thermal acceptance, release readiness, or production photorealism.
