# Traffic Vehicle Art V1 — 2026-07-27

## Scope and result

This bounded pass replaces the three project-authored box-car silhouettes and
serialized parked-car batch with five optimized CC0 vehicle derivatives:
sedan, hatchback, crossover/SUV, pickup, and cargo van. It does not change the
road network, traffic controller, aircraft, scenery, or shipping architecture.
The product remains standalone Quest 3, Android ARM64, Vulkan, and OpenXR.

All forty parked records and 924 moving records now use the same renderer-free,
instanced runtime. Eight deterministic paint colors follow the recorded 2025
U.S. market distribution: white, black, gray, silver, blue, red, green, and
brown. Neutral colors account for approximately 80 percent of the compiled
fleet; blue accounts for approximately 8.5 percent.

## Model and license record

The selected source is RGSDev's `Free Low Poly Vehicles Pack`, distributed under
CC0 1.0 Universal:

- <https://opengameart.org/content/free-low-poly-vehicles-pack>
- source archive SHA-256:
  `9091e478bd211a7cf8b562f04e8f061a2188d323e2fa0878f3f1641e5e67c800`

Raw archives and source FBX files remain in the external artifact cache and are
not committed. Blender 5.1.2 normalizes the models to plausible market
dimensions, reduces them to 794–1,008 triangles, removes source materials,
joins each model to one submesh, and encodes body/glass/tire/trim/light semantics
in vertex colors. Exact per-file hashes and derivative dimensions are recorded
in `tools/kbdu_traffic/traffic_vehicle_derivatives_v1.json`.

The one shared 128-square material-detail texture is project-authored,
ASTC-compressed for Android, and recorded by
`tools/kbdu_traffic/traffic_surface_texture_manifest.json`.

## Market basis

The fleet emphasizes crossovers/SUVs and pickups while retaining sedans,
hatchbacks, and cargo vans. This is a visual population model, not a current
vehicle census. The supporting market references are:

- Colorado Department of Revenue, 2025 Annual Report:
  <https://cdor.colorado.gov/sites/revenue/files/documents/DR_4000_2025_Annual_Report.pdf>
- Colorado Automobile Dealers Association, 2025 Q4 Auto Outlook:
  <https://colorado.auto/wp-content/uploads/2026/01/2025-Colorado-Auto-Outlook-Q4.pdf>
- Edmunds 2025 vehicle-color registration analysis:
  <https://www.edmunds.com/most-popular-car-colors/>

## Quest rendering contract

| Metric | Result |
| --- | ---: |
| body styles | 5 |
| paint variants | 8 |
| serialized traffic renderers | 0 |
| per-car GameObjects | 0 |
| maximum instanced draws | 5 |
| maximum selected instances | 128 |
| worst-case selected traffic triangles | 129,024 |
| traffic materials | 1 |
| traffic detail textures | 1 × 128² |
| transparent slots | 0 |
| realtime traffic shadows | 0 |

Removing the serialized parked batch reduced full-scene serialized geometry
from 799,975 to 799,735 triangles and active renderer count from 98 to 97. The
full-scene static report remains at 56 selected draws, 638,446 selected
triangles, 29 visible materials, and 221,506,450 estimated texture bytes.
The inherited texture estimate remains 1,305,490 bytes above its current
220,200,960-byte planning ceiling; this pass did not increase that reported
value and did not weaken the ceiling.

## Validation and evidence

- Traffic Python compiler/validator: PASS; 40 parked, 924 moving, 402 routes.
- Deterministic 120-second following simulation: PASS; minimum bumper gap
  15.986 m, 658 changing following gaps.
- Unity EditMode: PASS, 132/132.
- Unity PlayMode: PASS, 9/9.
- Product Visual QA: `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`.
- Full-resolution traffic views inspected: reservoir parking, airport parking,
  Valmont Road, Diagonal Highway, and pattern altitude.
- Meta XR Simulator 201.0 live stereo: PASS for nonblank, scene-consistent
  Quest 3 HMD/Touch capture; the aggregate gate remains
  `BLOCKED_MISSING_RECORDED_REPLAYS` because the ten `.vrs` files do not exist.
- Android production build: PASS.
- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`,
  136,455,443 bytes,
  SHA-256
  `5df25388deef79b8b4f2b6631272302efc6c03f24a4cf621b0b9e2156524a618`.
- APK install on the connected Quest 3: PASS.

Captures, logs, raw sources, Blender caches, and APK output remain external at
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_vehicle_art_v1_20260727_141704`.

## Limitations

These are optimized flight-distance vehicle models, not hero automobile
interiors. Glass is opaque with a reflective-looking response. Vehicles have no
occupants, animated wheels, active lights, or realtime shadows. Simulator and
Editor evidence do not establish physical Quest performance; worn visual review
and timing/thermal acceptance of the exact APK remain pending.
