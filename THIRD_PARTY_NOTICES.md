# Third-party notices

These notices apply to the assets and data included in T2 QuestFlight. They do not imply endorsement by any creator, agency, aircraft manufacturer, or data provider.

## Cessna172 3D model

`Cessna172` by KOG_THORNS (`@ioai25312`).

- Source: <https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4>
- License: Creative Commons Attribution 4.0 International, <https://creativecommons.org/licenses/by/4.0/>
- Copyright attribution: KOG_THORNS
- Changes: the original audited GLB binary remains unchanged for the primary aircraft. T2 QuestFlight adds a separate Unity scene/prefab hierarchy, material/import settings, cockpit/external visibility profiles, and authored control-surface pivots and animation bindings. A separate static parked-scenery adaptation retains only exterior body geometry; removes cockpit, interior, glass, textures, source materials, branding presentation, and animation; joins and decimates that geometry into two opaque LOD meshes; and offline-combines three non-interactive placements per LOD.

The licensed material is supplied without warranties. Use of the model does not imply that KOG_THORNS, Sketchfab, Cessna, Textron Aviation, or any other party endorses T2 QuestFlight.

## OpenStreetMap

Contains information from OpenStreetMap.

`© OpenStreetMap contributors`

- Copyright and attribution: <https://www.openstreetmap.org/copyright>
- Database license: Open Data Commons Open Database License 1.0, <https://opendatacommons.org/licenses/odbl/1-0/>

The product's bounded derivative databases include `kbdu_reference_context.json` and the OSM-footprint authority recorded in `KBDU_ReferenceContextV2.json`. They are governed by ODbL-1.0. Rendered context meshes, footprint-preserving building surfaces, and macro images are Produced Works derived from those databases and from public-domain/project-authored inputs. Project-authored shaders, materials, and serialization do not alter the ODbL boundary of the source data.

## City of Boulder 2020 aerial imagery

The Photoreal Aerial Surface V1 candidate includes bounded, optimized derivatives of the City of Boulder ArcGIS item `22b7c93a74394696824258f807f345ba`, `2020 Imagery`.

- Publisher: City of Boulder
- Source imagery producer: Pictometry International
- Source period: Spring and Summer 2020
- License: Creative Commons CC0 1.0 Universal, <https://creativecommons.org/publicdomain/zero/1.0/>
- Official item: <https://www.arcgis.com/home/item.html?id=22b7c93a74394696824258f807f345ba>
- Official map service: <https://gis.bouldercolorado.gov/ags_svr1/rest/services/raster/AP2020Cached9inWM/MapServer>

Attribution is not required by CC0, but the source credit `City of Boulder; source imagery produced by Pictometry International (2020)` is retained as provenance. Raw item/service responses, source snapshots, and cache tiles are not distributed. Exact source-contract and derivative hashes are recorded in `QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_AerialSurfaceV1_SourceManifest.json`. The imagery is visual context only, is not current or survey data, and is not suitable for navigation. Use does not imply endorsement by the City of Boulder or Pictometry International.

## Poly Haven CC0 textures

Poly Haven assets are dedicated under CC0 1.0. Attribution is not required, but is retained here as provenance.

- `Withered Grass`, Charlotte Baglioni: <https://polyhaven.com/a/withered_grass>
- `Sparse Grass`, Amal Kumar: <https://polyhaven.com/a/sparse_grass>
- `Dry Ground 01`, Rob Tuytel: <https://polyhaven.com/a/dry_ground_01>
- `Asphalt 02`: <https://polyhaven.com/a/asphalt_02>
- `Brown Brick 02`: <https://polyhaven.com/a/brown_brick_02>
- `Concrete Wall 005`: <https://polyhaven.com/a/concrete_wall_005>
- `Corrugated Iron 02`: <https://polyhaven.com/a/corrugated_iron_02>
- `Worn Corrugated Iron`: <https://polyhaven.com/a/worn_corrugated_iron>
- `Tree Small 02`: <https://polyhaven.com/a/tree_small_02>
- License: <https://polyhaven.com/license>

`Tree Small 02` is processed offline into a three-level airport-tree
representation: source-leaf hero geometry, a reduced volumetric-clump mesh,
and an eight-azimuth world-upright impostor. The raw blend, source textures,
and intermediate renders remain external. The product commits only optimized
runtime meshes, a leaf atlas, and an impostor atlas. All tiers share the
existing opaque canopy material. Exact source and derivative hashes are
recorded in `AirportCloseTreeLods_SourceManifest.json`.

## Poly Haven CC0 HDR sky

`Kloppenheim 03 (Pure Sky)`, original by Greg Zaal with sky edits by Jarod Guest.

- Source: <https://polyhaven.com/a/kloppenheim_03_puresky>
- License: CC0 1.0, <https://polyhaven.com/license>
- Changes: the official 4096×2048 Radiance HDR source is reduced to a 2048×1024 runtime derivative by exact 2×2 linear-radiance box filtering, without tone mapping or clipping. The raw download remains outside Git.

## United States government data

The product includes bounded derivatives of United States government data that the source agencies identify as public domain or unrestricted for public use.

- Federal Aviation Administration and USDOT Bureau of Transportation Statistics, NTAD Aviation Facilities and Runways.
- U.S. Geological Survey 3D Elevation Program (3DEP), including bounded DTM, DSM, normalized-height, terrain, surface-class, and canopy derivatives.
- U.S. Geological Survey / USDA Farm Service Agency, 2019 National Agriculture Imagery Program (NAIP) natural-color imagery distributed through The National Map.

Courtesy credit: Federal Aviation Administration (FAA); USDOT Bureau of Transportation Statistics (BTS); U.S. Geological Survey 3D Elevation Program (3DEP); USGS, USDA, The National Map: Orthoimagery.

The product includes only bounded, optimized government-data derivatives. Raw NAIP imagery exports, raw 3DEP EPT/LAZ nodes, source archives, and processing caches are not distributed. Exact NAIP acquisition records and hashes are in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`; exact 3DEP lineage and compact-output hashes are in `QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json` and the manifests under `QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1`.

The tagged Photoreal Aerial Surface V1 baseline uses a 3DEP-derived terrain-map canopy presentation outside the existing hero radius. Rounded Canopy Volume V1 retains that map and adds one bounded, project-authored fixed-world grove batch whose placement and height/density parameters derive from the same public-domain 3DEP evidence. The older raised-terrain candidate remains rejected and excluded from the runtime dependency closure. Neither representation is a botanical inventory or surveyed canopy record.

No agency logo is included and no agency endorsement, navigation suitability, surveying accuracy, FAA approval, or training credit is claimed.

## RGSDev CC0 traffic vehicles

The product's bounded road-traffic presentation uses optimized derivatives of
RGSDev's `Free Low Poly Vehicles Pack`.

- Source: <https://opengameart.org/content/free-low-poly-vehicles-pack>
- Author: RGSDev
- License: Creative Commons CC0 1.0 Universal, <https://creativecommons.org/publicdomain/zero/1.0/>
- Changes: five selected body styles are normalized to realistic dimensions,
  decimated, joined to one submesh each, stripped of source materials, and
  encoded with semantic vertex colors for one shared opaque instanced material.

Raw archives and source FBX files remain external. Exact hashes and derivative
inventory are recorded in
`tools/kbdu_traffic/traffic_vehicle_derivatives_v1.json`. Attribution is not
required by CC0 but is retained as provenance. Use does not imply endorsement
by RGSDev or OpenGameArt.

## JSBSim (frozen and out of scope)

The frozen optional development-gated native flight-dynamics package includes JSBSim
1.3.1 at revision `3b25f25e49b42d0489c04ac805674fc1450ca579`.

- Source: <https://github.com/JSBSim-Team/jsbsim>
- License: GNU Lesser General Public License 2.1 or later
- Complete license text: `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/RuntimeData/JSBSim-1.3.1/COPYING.txt`
- Exact source, binary hashes, architecture, and build flags: `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/SOURCE_MANIFEST.json`

No JSBSim work or promotion is part of the photoreal standalone environment milestone. JSBSim remains a replaceable shared library. Its inclusion does not establish
FAA approval, certified fidelity, training credit, or measured Quest hardware
performance. Release builds remain on the Unity prototype backend until native
timing and handling are measured on a physical Quest 3.
