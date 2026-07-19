# KBDU bounded ground-imagery recovery

This pipeline reallocates the existing KBDU macro-imagery budget toward the views a pilot actually sees. It retains the existing 4 km airport-core atlas, replaces the single 12 km/4096 mid atlas with four overlapping 2048 quadrant assets across the central 8 km square, and adds one 12 km/4096 fallback. The source is the current official USGS/USDA NAIP catalog at KBDU: 2019 natural-color, 0.6 m source imagery. The service query found no newer official NAIP acquisition at this location.

The new 1.984 m/texel hot tiles are real downsampled imagery. No AI upscaling, invented detail, Google imagery, commercial basemap, or unclear-license pixel is used. Raw USGS exports and service responses remain under the external artifact root.

The four hot tiles use ASTC 6x6; the 4096 fallback uses ASTC 10x10 to preserve the baseline 2.93 m/texel sampling from 4-6 km. Their exact full-chain total and net replacement delta are generated into the manifest. Automatic mip streaming is disabled: the current world-space custom terrain shader has no proven automatic streaming UV contract, so this milestone budgets full residency rather than claiming unmeasured streaming savings.

Run with the pinned scenery Python environment:

```powershell
$py = "<artifact-root>\toolchain\envs\scenery-pinned\python.exe"
$external = "<artifact-root>\ground_imagery"
& $py -B tools\scenery\imagery\fetch_and_build_ground_imagery.py `
  --raw-dir "$external\raw" `
  --work-dir "$external\work"
& $py -B tools\scenery\imagery\validate_ground_imagery.py `
  --report "$external\validation.json" `
  --contact-sheet "$external\ground_imagery_contact_sheet.jpg" `
  --comparison-sheet "$external\ground_imagery_before_after.jpg"
& $py -B -m unittest discover -s tools\scenery\imagery\tests -v
```

Primary references:

- USGS, The National Map supporting themes and public-domain NAIP: https://www.usgs.gov/national-map-supporting-themes
- USGS NAIP archive and source characteristics: https://www.usgs.gov/centers/eros/science/usgs-eros-archive-aerial-photography-national-agriculture-imagery-program-naip
- USGS TNM Access API: https://apps.nationalmap.gov/tnmaccess/
- Unity mipmap streaming requirements and custom-renderer limitations: https://docs.unity3d.com/Manual/TextureStreaming.html
- Unity Android ASTC guidance: https://docs.unity3d.com/Manual/class-TextureImporterOverride.html
- Meta Quest 72 Hz / 13.9 ms minimum frame target: https://developers.meta.com/horizon/documentation/unreal/po-perf-opt-mobile/

The exact material properties, vectors, blend bands, replacement path, and branch order are serialized in `KBDU_GroundImageryRecoveryV2_Contract.json`. Reservoir and minor water are intentionally orthophoto-only: no renderer, water material, transparency, reflection, animation, tint, or additional draw is part of this contract.
