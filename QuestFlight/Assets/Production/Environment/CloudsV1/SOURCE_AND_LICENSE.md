# Boulder sparse volumetric cloud field V4

The runtime shader, weather map, proxy mesh, placement, and build tools are project-authored. Raw OpenVDB files are external and are not committed.

Two JangaFX EmberGen Free VDB Asset clouds provide offline density only:

- `cloud_01_variant_0000.vdb`, SHA-256 `c91381781adc353f26a25d6cb6c9e254f47b89db04d2e99d289ae0e7d545945c`.
- `cloud_06_variant_0000.vdb`, SHA-256 `3c416cd2ca163e5bbc87911975806312d65b1e39a9ef83f2cd74f152efeadbee`.

The source pack identifies the free VDB assets as CC0. Only compact, deterministic density slices and an authored coverage map enter the Unity product. The reproducible compiler is `tools/clouds/build_sparse_volumetric_field.py`; its exact input/output hashes are recorded in `Generated/KBDU_CloudVolumeBuild.json` and `SOURCE_MANIFEST.json`.

The weather snapshot is anchored to the official NOAA Aviation Weather Center observation `METAR KBDU 141055Z AUTO 00000KT 10SM SCT018 17/16 A3013 RMK AO2` on 2026-08-14. The bounded volume represents the scattered 1,800-foot-AGL low layer. The fixed Kloppenheim HDRI remains responsible for the separately observed high-cloud presentation. This is one authored visual weather snapshot, not live or certified weather.

Runtime representation:

- eight explicitly authored local cloud bounds batched into one 96-triangle mesh;
- one shared material, renderer, and draw;
- a bounded front-to-back density march only inside those cloud bounds;
- two directly sampled VDB-derived density archetypes with offline coherent-vapor reconstruction;
- world-space, per-eye view rays with no billboarding;
- no cloud camera, particles, runtime hierarchy construction, realtime shadow map, or temporal reprojection;
- the existing fog/sun/sky response during cloud containment.

The result must not be described as literal meteorological reconstruction, certified weather, or proven Quest performance until physical-headset evidence exists.
