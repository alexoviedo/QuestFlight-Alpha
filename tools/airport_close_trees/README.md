# Airport tree hybrid LOD compiler

This offline toolchain adapts Poly Haven's CC0 **Tree Small 02** for the
standalone Quest airport tree tier. Raw source files and intermediate renders
remain external.

The runtime representation has three complementary levels:

- LOD0 preserves the source tree's volumetric leaf geometry and supplements it
  with source-positioned crown clumps around a three-dimensional trunk and
  branch skeleton.
- LOD1 uses a reduced volumetric clump mesh with the same world-space scale,
  pivot, and shared material.
- LOD2 is a world-upright quad sampling an eight-view, center-eye-selected
  impostor atlas. It never rotates independently for either stereo eye.

`AirportCloseTreeLodRenderer` selects levels by projected screen size, retains
per-instance state, uses asymmetric hysteresis and minimum residency, and
cross-fades with alpha-to-coverage under the product's 4x MSAA contract. The
compiler and runtime preserve the existing terrain-canopy and far-map
representations instead of adding one GameObject or Update loop per tree.

Run `render_tree_impostor_frames.py` in Blender first, then
`build_tree_texture_atlas.py`, and finally
`build_airport_close_tree_lods.py`. The committed runtime outputs are the
optimized meshes, two atlases, source manifest, and their paired Unity metadata.
