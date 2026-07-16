# KBDU terrain pyramid and context integration

This workstream intentionally does not edit `ProductionVerticalSlice`, `ProductionEnvironmentRoot.prefab`, lighting, sky, water, aircraft, flight, physics, input, or performance-policy scripts. The lead integrates these reusable serialized assets after merging the workstream.

## Terrain

1. Assign the single shared material `Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat` to the airport patch and the near, mid, and immutable far USGS terrain renderers.
2. Keep every terrain transform fixed in the pinned KBDU ENU frame. Do not apply camera-relative translation, scaling, or UV offsets.
3. Do not retain the 2048² mid texture or generated-color far material in the live dependency closure. The new material samples the aligned 4096² near, 4096² mid, and 2048² far derivatives.
4. Keep `_DiagnosticMode=0` for product captures. Use modes 1–3 only for the lead-owned heatmap review of level selection, blend weights, density, and boundaries.
5. Inspect the 1650–2000 m and 5000–6000 m square-ENU overlap bands in both eyes. The source-only audit does not replace stereo scene review.

## Batched context

1. Add exactly one serialized instance of `Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab` at local position/rotation zero and scale one under the authored environment root.
2. Preserve its 31 LODGroups, two non-crossfaded levels, 23 spatial OSM building batches, eight vegetation batches, and two shared materials. Do not unpack it into one renderer per building or tree.
3. Preserve shadow casting off until the lead measures a bounded static-lighting alternative. The assets use opaque geometry and add no transparent material slot.
4. Review culling and pop from cockpit, takeoff, pattern, and approach. If timing is tight, disable or narrow the mid context before reducing the terrain pyramid.
5. Do not duplicate the existing 27 MB cleared aircraft model as parked scenery. A separately reviewed static aircraft LOD is still required.

## Planning delta before scene integration

- 31 active context draws at worst (one active LOD renderer per batch).
- 49,930 LOD0 triangles or 26,920 LOD1 triangles.
- 2 shared materials, 0 transparent slots.
- 22,420,784 bytes for all terrain ASTC mip chains; replacing the prior near+mid pair increases referenced terrain texture memory by about 9.96 MB.
- The 1024² context atlas adds about 0.63 MB ASTC mip memory.

These are static planning values. The lead must rerun the render-budget audit and physical Quest timing after serialized integration.
