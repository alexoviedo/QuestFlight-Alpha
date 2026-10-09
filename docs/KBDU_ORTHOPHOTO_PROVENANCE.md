# KBDU orthophoto provenance

## Historical NAIP 2019 baseline

The earlier standalone scenery baseline used one aligned three-level natural-color imagery pyramid centered on the authored KBDU local-tangent origin (40.03936527 N, 105.22608958 W). The imagery is visual scenery only. It is not current imagery, surveyed airport data, or navigation data.

| Zone | WGS84 bounding box (west, south, east, north) | Current runtime derivative | Dimensions | Nominal resolution | SHA-256 |
| --- | --- | --- | ---: | ---: | --- |
| Near | `-105.24952418183945, 40.02135516248909, -105.20265497816054, 40.05737058440097` | `KBDU_NAIP_2019_Near4km_4096.jpg` | 4096×4096 | 0.9765625 m/pixel | `446e5c7fa13fc653de1f3e8404829c0e54b7e919fe99ebfba79d747a0b1dadea` |
| Mid | `-105.29643037059333, 39.98532061843375, -105.15574878940667, 40.09336678358586` | `KBDU_NAIP_2019_Mid12km_4096.jpg` | 4096×4096 | 2.9296875 m/pixel | `5e8d6edbcc5dc937c78b5fb8e4a5f8f7a06bda726b4509684e237f2b17fbdf9a` |
| Far | `-105.36688214425344, 39.93123316841624, -105.08529701574655, 40.14732481978453` | `KBDU_NAIP_2019_Far24km_2048.jpg` | 2048×2048 | 11.71875 m/pixel | `799be878d5c6be73b5596cbea70ee4e122859a528231608e40796f1020617b33` |

Unity imports all three as clamp-wrapped, trilinear, mipmapped streaming textures with Android ASTC 6×6 overrides and anisotropy 8. The near and mid levels use mip bias `-0.15`; the far level uses `-0.10`. At that revision, their combined estimated Android ASTC mip-chain size is 22,420,784 bytes. The shared terrain material maps all three from the same local ENU coordinates, blends near to mid over radial distances 1,650–2,000 m, and blends mid to far over 5,000–6,000 m. Existing CC0 ground microdetail fades before the far range.

The earlier `KBDU_NAIP_2019_Mid12km_2048.jpg` remains a rights-cleared historical derivative in the repository, but it is superseded and is not bound by the current terrain-pyramid material.

## Official source

- Service: `USGSNAIPPlus`, `https://imagery.nationalmap.gov/arcgis/rest/services/USGSNAIPPlus/ImageServer`
- Catalog agency/vendor: USDA-FSA-APFO, distributed through USGS The National Map
- Catalog year: 2019
- Acquisition dates represented in the bounded source catalog: 2019-08-03 and 2019-09-19
- Catalog source resolution: 0.6 m
- Raster object IDs: `30800–30807, 30809, 30811, 32802, 32803, 32806, 32807, 32811, 32832–32839, 32841, 32843`
- Raster function: `NaturalColor`
- Terms: United States government work / public domain
- Courtesy credit: `USGS, USDA, The National Map: Orthoimagery`

## Deterministic processing

The committed Python pipeline computes exact local-ENU square corners, requests bounded WGS84 exports, verifies service-returned extents, assembles top-origin tiles, applies one pinned color normalization, matches the mid overlap to the near level, matches the far overlap to the mid level, and writes the three optimized JPEG derivatives. The material then maps those extents through one local ENU coordinate contract. Pillow is pinned to 11.3.0.

An initial rejected export used ArcGIS's default aspect adjustment and produced a repeated latitude strip. The accepted fetch sets `adjustAspectRatio=false`; every accepted returned extent matches its requested extent within 4.69e-13 degrees. The regression test rejects suspicious neighboring tiles below a mean-absolute-error threshold of 6; accepted neighbor comparisons measure 24.27 or greater at the bounded comparison scale.

The authoritative machine-readable record is `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`. It records the service metadata, complete raster catalog, raw external request and tile hashes, exact output dimensions and hashes, normalization statistics, tool hashes, Android estimates, and prohibited-source confirmation. `tools/kbdu_orthophoto/validate_orthophoto_assets.py` verifies the current three-level dimensions, hashes, license/source policy, ASTC imports, mip streaming, clamp mode, and bounded memory estimate.

## Raw-data boundary

Raw ImageServer exports, service receipts, lossless intermediates, rejected outputs, source archives, and previews remain in the external milestone artifact tree under `kbdu_orthophoto/`. They are not product assets and must not be committed. That historical material used only the three optimized Near4096/Mid4096/Far2048 derivatives, their paired Unity metadata, the deterministic tools/tests, and the provenance manifest.

No Google Maps, Google Earth, Street View, paid, or unclear-license imagery was used.

## Current product bindings

The current `PhotorealTerrainV1/KBDU_TerrainPyramid.mat` binds the City of Boulder 2020 derivatives `KBDU_City2020_Near4km_8192.jpg`, `KBDU_City2020_MidBase12km_8192.jpg`, and `KBDU_City2020_Far24km_4096.jpg`. Their paired metadata, source hashes and CC0 record live under `QuestFlight/Assets/Production/Environment/AerialSurfaceV1`. The NAIP table above is retained for historical provenance and does not describe current texture resolution, imports or memory. Current configuration and validation boundaries are recorded in [CURRENT_PRODUCT_CONTRACTS.md](CURRENT_PRODUCT_CONTRACTS.md).
