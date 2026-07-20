# Airport-core V4 water and vegetation benchmark

This directory owns the reproducible, bounded Workstream C candidates. It does
not modify the product scene or construct runtime hierarchy.

Outputs are deliberately limited to:

- one opaque Boulder Reservoir material contract that reuses the authoritative
  V2 shoreline mesh as a single renderer;
- one terrain-mask minor-water prototype with no water ribbon mesh;
- one project-authored vegetation cluster containing two cottonwood variants,
  one deciduous tree, and one shrub group at three authored LODs;
- deterministic texture, source, cost, geometry, and validation reports.

Run from the repository root:

```powershell
python tools/kbdu_airport_core_v4/water_vegetation/generate_benchmark_assets.py `
  --output <staging-or-Assets-directory>

& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" `
  --background --python tools/kbdu_airport_core_v4/water_vegetation/author_vegetation_blender.py -- `
  --atlas <output>\Vegetation\Textures\AirportCoreVegetation_Atlas.png `
  --blend <output-or-Assets-ArtSource>\VegetationHeroCluster.blend `
  --fbx <output>\Vegetation\Models\AirportCoreVegetationCluster.fbx `
  --report <output>\Vegetation\AirportCoreVegetation_GeometryReport.json `
  --preview-dir <artifact-directory>

python tools/kbdu_airport_core_v4/water_vegetation/validate_benchmark_assets.py `
  --root <output> --strict
```

The generator uses only the Python standard library. All image pixels and all
mesh geometry are project-authored from fixed seeds. No raw photo, high-poly
model, or external texture is required or committed.

The candidates remain unaccepted until the lead integrates them into the
explicitly authored benchmark root and inspects full-resolution standard and
stereo QA. Offline PASS means only that the asset contract and cost ceilings
are coherent.
