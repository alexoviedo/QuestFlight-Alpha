# Environment imagery decision: KBDU aerial surface V1

## Decision

Use the City of Boulder `2020 Imagery` cached map service for the existing KBDU 24 km terrain footprint. The official item is public, identifies Pictometry International as the imagery producer, states a 9-inch orthogonal source resolution and Spring/Summer 2020 flights, and links the imagery to Creative Commons CC0 1.0 Universal.

This is an engineering source-and-rights record, not legal advice. It applies only to the pinned item revision, service contract, fixed KBDU footprint, and derivative workflow described here. A different revision, endpoint, footprint, or product use requires another review.

The imagery is visual context only. It is not current airport, survey, chart, or navigation data. Exact source-frame dates are not published, so the acquisition date is recorded only as `Spring and Summer 2020`. Do not imply City of Boulder or Pictometry endorsement.

## Selected source

| Field | Pinned value |
| --- | --- |
| Publisher | City of Boulder |
| Producer | Pictometry International |
| ArcGIS item | `22b7c93a74394696824258f807f345ba`, `2020 Imagery` |
| Item revision | `1707343148000` milliseconds since Unix epoch |
| Source description | Tiled orthogonal imagery from nadir images; 9 inches; Spring and Summer 2020 |
| License | Creative Commons CC0 1.0 Universal (`CC0-1.0`) |
| Credit record | City of Boulder; source imagery produced by Pictometry International (2020) |
| Service | `AP2020Cached9inWM/MapServer`, public Web Mercator cache |
| Cache contract | 256 px `MIXED` tiles, service LODs through 21, single-image export capped at 4096×4096 |
| Runtime samples used | LOD 15–18 cached tiles; no sample is promoted as 9-inch runtime detail |

Official evidence:

- City open-data policy: https://bouldercolorado.gov/services/open-data
- Official ArcGIS item: https://www.arcgis.com/home/item.html?id=22b7c93a74394696824258f807f345ba
- Item metadata: https://www.arcgis.com/sharing/rest/content/items/22b7c93a74394696824258f807f345ba?f=json
- Map service: https://gis.bouldercolorado.gov/ags_svr1/rest/services/raster/AP2020Cached9inWM/MapServer
- Service metadata: https://gis.bouldercolorado.gov/ags_svr1/rest/services/raster/AP2020Cached9inWM/MapServer?f=pjson
- CC0 legal tool: https://creativecommons.org/publicdomain/zero/1.0/

The City policy describes catalog data as available under CC0 1.0, subject to dataset-specific metadata. More importantly for this binary asset, the selected ArcGIS item itself carries the CC0 URL. The builder fails closed unless the expected item ID, modified time, public access, service URL, CC0 string, description evidence, cache geometry, and spatial reference all match.

Pinned metadata bytes and normalized contracts:

| Record | SHA-256 |
| --- | --- |
| ArcGIS item response | `b7f17522b698a8657e85b9b750bc5ca5e0e388976ac915907a13ccb76b52f4b0` |
| ArcGIS item contract | `aafc729bdb6dd666d003912b9db06025e1579e144733b1c7e4a49ce4ffd58663` |
| Service metadata response | `ddecdb4e09f830ce2f6e57a13b2bc5db64c5c7dae8e1adf99d716aca9580da80` |
| Service contract | `177a053bdbc7ecd3484c21e6d39ecf7ca18ea810ab750f76e7ebfe48bad19c79` |
| Final canonical source snapshot | `6fa6baea9eb2d876577da883d88b029e018a75dad9728f3faad13bed76ead02b` |
| Final source tile set (5,551 tiles / 85,223,758 bytes) | `41c88c222bae40db5ba061332e2919e99b682015c7ce7bc203f0c7d85c8a6906` |

The complete source snapshot additionally hashes every requested tile and the ordered tile-set contract. It stays in the external product-artifact root and is copied into the compact runtime source manifest only by digest. Raw source responses and cache tiles must not be committed.

## Alternatives reviewed

| Candidate | Rights result | Fidelity/currency result | Decision |
| --- | --- | --- | --- |
| City of Boulder 2024 aerial item `0be14d94439c4035aa2121cd657d6268` | Inspected item and image service did not publish an imagery-specific license or adequate credit/rights terms | Newer | Reject pending written rights clearance |
| Boulder County open-data catalog | Catalog policy links CC BY 4.0, but each candidate still needs source-specific lineage and rights | No suitable orthophoto item was found in the inspected official content group | Reject for this milestone |
| USGS High Resolution Orthoimagery | U.S. public domain | Archive spans 2000–2016, 1 m or finer | Legally usable, but older than selected source |
| USGS/USDA NAIP at KBDU | U.S. public domain | Existing baseline resolves to 2019-09-19, 0.6 m | Retain only as the legal alignment reference/fallback |
| Google or generic commercial basemap imagery | Redistribution/modification rights unsuitable or unverified | Not evaluated | Prohibited |

Alternative evidence:

- City 2024 item: https://www.arcgis.com/home/item.html?id=0be14d94439c4035aa2121cd657d6268
- City 2024 image service: https://gis.bouldercolorado.gov/ags_svr1/rest/services/raster/2024_Aerial_City_of_Boulder/ImageServer
- Boulder County open-data policy: https://bouldercounty.gov/government/open-data/
- Boulder County open-data group: https://www.arcgis.com/home/group.html?id=b2d3c1b63a3c452fa7141f70f017f0b6
- USGS HRO archive and public-domain statement: https://www.usgs.gov/centers/eros/science/usgs-eros-archive-aerial-photography-high-resolution-orthoimagery-hro
- USGS imagery-selection FAQ: https://www.usgs.gov/faqs/what-aerial-imagery-data-set-should-i-use-earthexplorer
- USGS NAIP Plus service: https://imagery.nationalmap.gov/arcgis/rest/services/USGSNAIPPlus/ImageServer
- USDA current NAIP service: https://apps.geo.fpac.usda.gov/geo-imagery/rest/services/naip/conus_naip/ImageServer

The machine-readable candidate audit is `tools/aerial_surface/source_imagery_candidates.json`.

## Permitted preprocessing

The derivative pipeline performs only these image operations:

1. Fetch the reviewed ArcGIS cached tile bytes over HTTPS from an allowlisted City host.
2. Mosaic the exact hash-pinned tiles by `(LOD, row, column)`.
3. Reproject Web Mercator to a WGS84 azimuthal-equidistant local frame centered at the serialized KBDU origin.
4. Resample once with deterministic single-thread Lanczos into fixed north-up, west-left local bounds.
5. Encode RGB JPEG at quality 92, 4:4:4, non-progressive, with no optimizer-dependent rewrite.
6. Let Unity generate standard mipmaps and Android ASTC imports from the committed compact derivatives.

There is no AI generation, AI upscaling, synthetic sharpening, super-resolution, frame interpolation, color replacement, watermark removal, or map-label compositing. The City cache itself is lossy; a high JPEG output quality preserves that input but cannot create source detail.

## Water and scope boundary

Reservoir and minor water pixels remain exactly the selected orthophoto. This work adds no water material, tint, transparency, reflection, animation, geometry, or draw call.

The decision replaces imagery inside the existing near, recovery-mid, 12 km mid, and 24 km far texture bounds. It does not expand the world, change terrain geometry, runway geometry, airport markings, sky, mountains, camera ownership, aircraft transforms, XR hierarchy, or flight authority.

## Re-review triggers

Stop and repeat the rights and source audit if any of these changes:

- ArcGIS item ID, item `modified` value, license text, public-access state, service URL, service contract, or tile bytes;
- source publisher, producer, acquisition period, or credit requirement;
- local footprint, output purpose, redistribution model, or shipping platform;
- use of a new aerial provider, basemap, generated pixel process, or source whose rights are not explicit.
