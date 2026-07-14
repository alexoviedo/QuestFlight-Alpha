# KBDU Orthophoto Provenance

## Product use

The standalone Quest environment uses two bounded natural-color macro atlases centered on the authored KBDU local-tangent origin (40.03936527 N, 105.22608958 W). They are visual scenery only and are not current imagery, surveyed airport data, or navigation data.

| Zone | WGS84 bounding box (west, south, east, north) | Runtime derivative | Nominal output resolution | SHA-256 |
| --- | --- | --- | ---: | --- |
| Near | `-105.24952418183945, 40.02135516248909, -105.20265497816054, 40.05737058440097` | `KBDU_NAIP_2019_Near4km_4096.jpg` | 0.9765625 m/pixel | `446e5c7fa13fc653de1f3e8404829c0e54b7e919fe99ebfba79d747a0b1dadea` |
| Mid | `-105.29643037059333, 39.98532061843375, -105.15574878940667, 40.09336678358586` | `KBDU_NAIP_2019_Mid12km_2048.jpg` | 5.859375 m/pixel | `d75316cb03a8ddcdc6d326186f9fc5e936d19d90ffc4e42ffe627888aa8593cd` |

Unity imports both as clamp-wrapped, trilinear, anisotropic, mipmapped streaming textures with Android ASTC 6×6 overrides. Their combined estimated ASTC mip chain is 12,459,248 bytes. The near atlas blends into the color-matched mid atlas between 1,750 and 2,000 m local extent; existing CC0 dry-prairie, sparse-grass, and soil microdetail fades with distance.

## Official source

- Service: `USGSNAIPPlus`, `https://imagery.nationalmap.gov/arcgis/rest/services/USGSNAIPPlus/ImageServer`
- Catalog agency/vendor: USDA-FSA-APFO, distributed through USGS The National Map
- Acquisition date: 2019-09-19
- Catalog source resolution: 0.6 m
- Raster object IDs: `30800, 30804, 30805, 32832, 32834, 32836, 32837, 32838, 32839`
- Raster function: `NaturalColor`
- Terms: United States government work / public domain
- Courtesy credit: `USGS, USDA, The National Map: Orthoimagery`

## Deterministic processing

The committed Python pipeline computes exact local-ENU square corners, requests WGS84 exports, verifies service-returned extents, assembles top-origin tiles, applies a pinned color normalization, matches the mid-zone overlap to the near atlas, and writes the two bounded JPEG derivatives. Pillow is pinned to 11.3.0.

An initial rejected export used ArcGIS's default aspect adjustment and produced a repeated latitude strip. The accepted fetch sets `adjustAspectRatio=false`; all accepted returned extents match requested extents within 4.55e-13 degrees. The regression test rejects suspicious neighbor overlap below a mean-absolute-error threshold of 6; accepted neighbor checks measure 24.27 or greater at the bounded comparison scale.

## Raw-data boundary

Raw ImageServer exports, service receipts, lossless intermediates, rejected outputs, source archives, and previews remain in the external milestone artifact tree under `kbdu_orthophoto/`. They are not product assets and must not be committed. The repository contains only the two optimized runtime atlases, paired Unity metadata, deterministic tools/tests, and the provenance manifest.

The authoritative machine-readable record is `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`. `tools/kbdu_orthophoto/validate_orthophoto_assets.py` verifies dimensions, hashes, license/source policy, ASTC imports, mip streaming, clamp mode, and the bounded memory estimate.

No Google Maps, Google Earth, Street View, paid, or unclear-license imagery was used.
