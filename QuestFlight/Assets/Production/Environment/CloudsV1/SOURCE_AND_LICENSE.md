# Boulder Cloud Field V1 source and license

All runtime cloud meshes, shader, material, and placement data are project-authored deterministic derivatives. No external photograph, model, or archive is included.

`KBDU_CloudImpostorAtlasV3.png` was generated for this project with OpenAI image generation on 2026-08-10. Prompt constraints required neutral midday lighting, broad shadowed bases, vapor-eroded edges, no sky/horizon/ground, and no text or watermark. The original RGB source SHA-256 is `a4bf9047b4427f71e902cb6aa1b5c3ff1296e29f4f5d0dd685be5ecc4548dd82`.

The runtime atlas is a deterministic project-owned derivative produced by `tools/clouds/build_cloud_rgba_atlas.py`. The compiler rejects the black source plate, trims its contaminated charcoal contour, rebuilds a feathered alpha matte, protects every 2×2 atlas-cell gutter, and dilates valid cloud color through transparent pixels so mip filtering, Alpha-to-Coverage, and ASTC compression cannot reintroduce the original border. Runtime SHA-256: `712ee9dfc6dc1a56619d1c54111831937dbabec7a4f1daaffbc23f0ca635ab1a`. Unity imports it as straight-alpha RGBA, maximum 512², coverage-preserving mipmapped, streaming, clamp-wrapped Android ASTC 6×6.

At runtime, one quad per authored spatial volume is batched into one renderer and oriented from the shared center-eye position in the vertex shader. Both eyes therefore receive the same world-locked orientation; there is no per-eye billboard selection, CPU camera-facing update, transparent blend pass, cloud camera, or particle system.

Meteorological references used to constrain the authored Boulder presets:

- NOAA Aviation Weather Center METAR data and cloud-layer conventions: https://aviationweather.gov/data/metar/
- NOAA/NCEI Local Climatological Data and hourly surface observations: https://www.ncei.noaa.gov/access/search/data-search/local-climatological-data
- NSF NCAR/UCAR, “Clouds Form Due to Mountains”: https://scied.ucar.edu/learning-zone/clouds/clouds-form-mountains
- NSF NCAR/UCAR, “Lenticular Clouds”: https://scied.ucar.edu/image/lenticular-clouds
- National Weather Service cloud-development training: https://www.weather.gov/source/zhu/ZHU_Training_Page/clouds/cloud_development/clouds.htm

The default product profile is a restrained scattered fair-weather cumulus presentation. It is not live weather and must not be described as a literal reconstruction of a particular observation time.

License: project-owned source and generated derivatives; use is subject to the OpenAI terms applicable to the generated atlas.
