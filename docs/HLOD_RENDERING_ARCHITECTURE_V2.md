# KBDU Reconstruction V2 HLOD and rendering evidence

The standalone Quest context architecture keeps deterministic 250 m logical cells
as the coverage and provenance unit, then aggregates them into coarser offline
render groups. The current policy uses 1.5 km groups in the 0–2 km zone, 4 km groups
in the 2–4 km zone, and 6 km groups in the 4–6 km zone. Every render group has
three serialized geometry levels: LOD0, LOD1, and LOD2/HLOD. Crossfade, dithering,
camera-relative transforms, runtime hierarchy repair, runtime mesh combining,
per-instance scripts, and per-object material creation are excluded.

Geometry fidelity is distance appropriate. Near LOD0 retains source footprints;
extended LOD0 uses one box per building; far LOD0 and lower levels use deterministic
100–400 m occupied-cell cluster boxes. This preserves macro building coverage
without making all 27,679 footprints the worst-case far-field geometry.

The offline audit in `tools/kbdu_hlod_v2` treats the recipe, placement manifest,
serialized mesh assets, prefab, materials, shaders, and Android texture import
metadata as one evidence set. It checks mesh topology and tier coverage, reports
cell and placement counts, verifies opaque instancing and single-pass-instanced
shader contracts, and produces conservative context-subset triangle, renderer,
material, texture, and transparency estimates.

The audit fails closed when any HLOD tier or integrated whole-product budget is
missing. A context-subset count cannot establish the product budget. Likewise,
source-token compatibility cannot prove runtime batching or stereo correctness.
Only a physical Quest gate may establish frame time, thermals, comfort, or actual
standalone performance.

The complete V2 source contains 1,224 logical building cells and 27,679 buildings.
The derived runtime recipe maps those cells exactly once into 20 building render
groups. Four vegetation groups preserve 92 logical cells, 377 placements, and four
species; one parked-aircraft group brings the context to 25 groups and 75 serialized
LOD renderers. Against the measured 99-renderer non-context scene baseline, the
offline plan estimates 174 serialized renderers before the reserved two-renderer
water addition and 176 including that reserve. The corresponding conservative
planning totals are 126 visible slots, 638,107 visible triangles, and 684,241
aggregate serialized triangles. These are authoring gates, not measured Quest
performance.

Final Unity evidence reports 171 active renderers, 122 maximum estimated visible draw calls, 609,168 maximum visible triangles, 25 maximum visible materials, 683,589 aggregate scene-mesh triangles, 164.96 MiB estimated texture residency, 39 LOD groups, and zero crossfade groups. This passes the V2 static ceilings. It does not establish actual draw submission, allocations, frame timing, thermals, or stereo stability on Quest.
