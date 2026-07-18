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

The product's bounded, simplified derivative database is `kbdu_reference_context.json`. It is made available under ODbL-1.0. The rendered meshes and macro image are Produced Works derived from that database and from public-domain/project-authored inputs.

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
- License: <https://polyhaven.com/license>

## Poly Haven CC0 HDR sky

`Kloppenheim 03 (Pure Sky)`, original by Greg Zaal with sky edits by Jarod Guest.

- Source: <https://polyhaven.com/a/kloppenheim_03_puresky>
- License: CC0 1.0, <https://polyhaven.com/license>
- Changes: the official 4096×2048 Radiance HDR source is reduced to a 2048×1024 runtime derivative by exact 2×2 linear-radiance box filtering, without tone mapping or clipping. The raw download remains outside Git.

## United States government data

The product includes bounded derivatives of United States government data that the source agencies identify as public domain or unrestricted for public use.

- Federal Aviation Administration and USDOT Bureau of Transportation Statistics, NTAD Aviation Facilities and Runways.
- U.S. Geological Survey 3D Elevation Program (3DEP).
- U.S. Geological Survey / USDA Farm Service Agency, 2019 National Agriculture Imagery Program (NAIP) natural-color imagery distributed through The National Map.

Courtesy credit: Federal Aviation Administration (FAA); USDOT Bureau of Transportation Statistics (BTS); U.S. Geological Survey 3D Elevation Program (3DEP); USGS, USDA, The National Map: Orthoimagery.

The product includes only bounded, optimized NAIP derivatives. Raw imagery exports and source archives are not distributed. Exact acquisition date, raster IDs, service endpoint, bounds, processing, and hashes are recorded in `tools/kbdu_orthophoto/kbdu_orthophoto_manifest.json`.

No agency logo is included and no agency endorsement, navigation suitability, surveying accuracy, FAA approval, or training credit is claimed.

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
