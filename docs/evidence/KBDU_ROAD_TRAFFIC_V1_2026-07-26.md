# KBDU Road Traffic V1

## Scope and source

- Product branch: `feature/quest-road-traffic-v1`
- Source checkpoint: `8fc6202aca2c2b34f1eafa001b0958f1b01fb162`
- Shipping architecture: standalone Quest 3, Android ARM64, Vulkan, OpenXR
- Product scene authority remains explicitly serialized with one XR Origin,
  tracked Main Camera, aircraft simulation root, and flight authority.

Traffic V1 adds a bounded road-life representation without changing aircraft,
physics, inputs, terrain, buildings, vegetation, runway, or water. Thirty parked
vehicles were traced in three visible parking clusters from the pinned
City 2020 near orthophoto. Twelve moving vehicles use both directions of six
selected OpenStreetMap ways covering Valmont Road, 55th Street, Diagonal
Highway, Jay Road, and North 61st Street.

The compiler pins the source orthophoto GUID
`0021365d50509d2abe127a826626ee6e` and SHA-256
`143c4946c06464dbdea1567cd5fe284acc4c5ba7718a38900a2e83a8eccbfb53`.
Road geometry is an OSM derivative under ODbL 1.0 with required notice
`© OpenStreetMap contributors`. All four vehicle meshes are deterministic,
project-authored, and project-owned.

## Implementation

- one serialized `TrafficRoot` and one `KbduTrafficRuntime`;
- no per-vehicle GameObjects, runtime hierarchy construction, NavMesh, physics,
  colliders, rigidbodies, realtime shadows, or transparent passes;
- three near archetypes (sedan, SUV, pickup) plus one far proxy;
- one existing opaque instancing material and at most four draw calls;
- deterministic precompiled lane following using preallocated matrix arrays;
- terrain-sampled positions with 0.035 m authored clearance;
- 24-instance near cap, 48-instance mid cap, 550 m near transition, and
  1,500 m hard cull;
- 3,888 maximum visible vehicle triangles;
- zero added texture bytes, materials, shaders, or transparent slots.

The Editor path freezes route time at zero for repeatable captures. Player
motion uses unscaled runtime time. Visibility is recomputed at camera pre-cull;
this is necessary because the QA runner reuses one Camera at multiple authored
poses in a single frame. Matrices and counters remain preallocated.

## Visual evidence

Machine classification:
`PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`, 97/97 views.

Full-resolution traffic views:

- `32_traffic_reservoir_parking.png`
- `33_traffic_airport_parking.png`
- `34_traffic_valmont_moving.png`
- `35_traffic_diagonal_highway.png`
- `36_traffic_pattern_altitude.png`

External artifact root:
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_v1_visual_qa_r4`

Lead inspection confirms the parked cars are grounded over the photographed
parking evidence and the two road views place moving cars on the compiled road
surface. The pattern-altitude view confirms that ordinary vehicles naturally
become negligible rather than consuming distant detail.

## Validation and budget

| Gate | Result |
|---|---|
| Traffic manifest validator | PASS: 30 parked, 12 routes, 12 moving, 1,644 samples |
| EditMode | PASS 130/130 |
| PlayMode | PASS 9/9 |
| Product Visual QA | PASS 97/97 |
| Live Meta XR stereo/HMD/Touch | PASS |
| Recorded simulator replay aggregate | `BLOCKED_MISSING_RECORDED_REPLAYS` |
| Android build | PASS |
| Quest install | PASS |
| Unattended Quest launch | `BLOCKED_HUMAN_SYSTEM_DIALOG` (Touch controllers required) |

Static scene report:

| Metric | Result | Ceiling |
|---|---:|---:|
| Estimated draw calls | 56 | 75 |
| Visible triangles | 638,446 | 650,000 |
| Visible materials | 29 | 29 |
| Transparent slots | 3 | 3 |
| Texture bytes | 221,506,450 | 220,200,960 |
| MSAA | 4x | 4x |

The texture result is an inherited airport-hero baseline blocker: the
pre-traffic report has the identical 221,506,450-byte estimate. Traffic adds no
textures or materials. This evidence does not relabel the aggregate static gate
as passing.

Android APK (external, uncommitted):

- path: `QuestFlight\Builds\Android\QuestFlight-production.apk`
- size: 135,858,491 bytes
- SHA-256:
  `aafde744d6cc1082b0cd2720f5477fcf622f5762311c7b5e5c0813a2a6a0d810`

## Limitations

This is a bounded visual traffic cue, not a city traffic simulation or a
current occupancy record. It omits intersections, signals, collision avoidance,
turning behavior, most local streets, and most parking lots. Vehicle geometry is
optimized for flight views rather than ground-level hero inspection. The exact
APK installed on the connected Quest 3, but Horizon required awake Touch
controllers before launch. It has not received worn traffic review or physical
frame-time/thermal acceptance. Simulator timing is not Quest hardware timing.
