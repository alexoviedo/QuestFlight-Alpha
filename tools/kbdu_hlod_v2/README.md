# KBDU HLOD V2 offline audit

This tool produces deterministic static evidence from the Reconstruction V2
manifests and serialized Unity assets. It does not launch Unity, edit the scene,
or measure Quest performance.

First derive the small runtime grouping recipe from the complete 250 m logical
cell recipe and V2 reference context:

```powershell
python tools/kbdu_hlod_v2/build_runtime_hlod_recipe.py
```

The source recipe remains unchanged and complete. The derived recipe records
every logical cell exactly once while aggregating render-time groups by zone. It
fails without writing a recipe if renderer, triangle, material, texture,
transparency, or single-mesh limits would be exceeded.

Run it with an artifact directory outside Git:

```powershell
python tools/kbdu_hlod_v2/generate_hlod_report.py `
  --output-dir C:\path\to\artifacts\hlod_v2
```

The command writes `hlod_render_budget_report.json` and
`hlod_render_budget_report.md`. A passing process exit code requires all of:

- 200–300 m placement and logical recipe cells;
- valid LOD0, LOD1, and LOD2 mesh assets for every recipe batch;
- a serialized three-level LODGroup prefab with crossfade disabled;
- opaque, instancing-enabled materials and stereo/instancing shader contracts;
- Android ASTC mipmapped context texture evidence;
- accepted building and vegetation source manifests;
- configured whole-product static budget evidence within every limit.

Missing evidence is a failure. Context-only estimates are never promoted into a
whole-product result. The report also states that static evidence cannot prove
frame timing, thermal behavior, runtime batching, comfort, or stereo quality.

Run the tests with:

```powershell
python -m unittest discover -s tools/kbdu_hlod_v2/tests -v
```
