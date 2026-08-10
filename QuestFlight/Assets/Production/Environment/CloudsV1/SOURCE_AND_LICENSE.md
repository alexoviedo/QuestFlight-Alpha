# Boulder Cloud Field V1 source and license

All runtime cloud meshes, shader, material, and placement data are project-authored deterministic derivatives. No external photograph, model, or archive is included.

`KBDU_CloudImpostorAtlasV3.png` was generated for this project with OpenAI image generation on 2026-08-10, then constrained by the Unity importer to a 512² ASTC 6×6 mipmapped Android runtime derivative. It contains four isolated fair-weather cumulus views on a black luminance-key background. Prompt constraints required neutral midday lighting, broad shadowed bases, vapor-eroded edges, no sky/horizon/ground, and no text or watermark. SHA-256: `a4bf9047b4427f71e902cb6aa1b5c3ff1296e29f4f5d0dd685be5ecc4548dd82`.

At runtime, one quad per authored spatial volume is batched into one renderer and oriented from the shared center-eye position in the vertex shader. Both eyes therefore receive the same world-locked orientation; there is no per-eye billboard selection, CPU camera-facing update, transparent blend pass, cloud camera, or particle system.

Meteorological references used to constrain the authored Boulder presets:

- NOAA Aviation Weather Center METAR data and cloud-layer conventions: https://aviationweather.gov/data/metar/
- NOAA/NCEI Local Climatological Data and hourly surface observations: https://www.ncei.noaa.gov/access/search/data-search/local-climatological-data
- NSF NCAR/UCAR, “Clouds Form Due to Mountains”: https://scied.ucar.edu/learning-zone/clouds/clouds-form-mountains
- NSF NCAR/UCAR, “Lenticular Clouds”: https://scied.ucar.edu/image/lenticular-clouds
- National Weather Service cloud-development training: https://www.weather.gov/source/zhu/ZHU_Training_Page/clouds/cloud_development/clouds.htm

The default product profile is a restrained scattered fair-weather cumulus presentation. It is not live weather and must not be described as a literal reconstruction of a particular observation time.

License: project-owned source and generated derivatives; use is subject to the OpenAI terms applicable to the generated atlas.
