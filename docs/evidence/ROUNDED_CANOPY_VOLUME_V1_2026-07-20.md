# Rounded Canopy Volume V1 evidence

## Outcome

The bounded rounded-canopy candidate is **machine-valid for branch delivery**.
It adds a fixed-world Tier-2 grove-volume cue to the tagged Photoreal Aerial
Surface V1 baseline without changing terrain height, semantic building
ownership, the orthophoto-only water policy, or the standalone Quest rendering
architecture. This record does not claim literal photorealism or physical Quest
performance. The exact APK has not been worn or timed on Quest hardware.

## Source and scope

- Product repository: `C:\Users\ovied\Dev\T2\T2-QuestFlight`
- Delivery branch: `feature/rounded-canopy-volume-v1`
- Source/main commit: `82fb39c5c84f4a96b321b53242ecd46348d6d244`
- Source tag: `photoreal-aerial-surface-v1`
- Unity: `6000.3.8f1`
- Product scene: `QuestFlight/Assets/Scenes/ProductionVerticalSlice.unity`

The source baseline already separated bare-earth DTM terrain, OSM-footprint
buildings, authored hero objects, and terrain-owned canopy maps. This milestone
changes only the bounded vegetation representation. Buildings remain on their
semantic footprints; lidar/CHM is not applied as generic raised ground.

## Representation

| Tier | Runtime representation | Instances | Triangles | Draws |
| --- | --- | ---: | ---: | ---: |
| Tier 0 | Existing near hero vegetation | 3 | included below | included below |
| Tier 1 | Existing near vegetation batch | 19 | 12,860 combined T0/T1 | 2 combined T0/T1 |
| Tier 2 | Fixed-world `AerialGroveCluster_T2` instances | 42 | 22,680 | 1 |
| Tier 3 | Existing terrain canopy height/density/normal/occlusion response | map-owned | 0 | 0 additional |
| Total vegetation | Shared project-authored family | 64 | 35,540 | 3 |

Thirty Tier-2 clusters are within 1.5 km. Each source cluster contains 540
triangles. All 42 matrices are submitted through one `DrawMeshInstanced` batch
and one existing shared material. The clusters are fixed in world orientation:
they do not face a camera, select a different frame per eye, crossfade, dither,
or own one GameObject/`Update` per tree. The authored matrix explicitly
right-multiplies a `Quaternion.Euler(-90, 0, 0)` conversion so the Blender Z-up
source mesh is Unity Y-up while ENU placement and yaw remain stable.

The existing terrain canopy map remains beneath the aggregate volume. It keeps
the broad aligned canopy color/normal/occlusion cue while the sparse Tier-2
clusters provide a pilot-visible rounded silhouette. No canopy source is used
to displace bare-earth terrain.

Closure validation also found that the deterministic vegetation builder's
canopy-include template lagged the accepted generated shader. Regeneration
would have removed the required product-AEQD-to-lidar-UTM sampling transform.
The template now retains both position and normal transforms, and the validator
fails if the generated include differs from its source template or omits those
semantic-frame operations.

## Data and rights

The Blender source, exported grove mesh, atlas, material presentation, shaders,
authoring code, and configuration are project-authored. Lidar/CHM contributes
only deterministic semantic placement, height, and density evidence. The
canonical upstream record is the public-domain USGS 3DEP manifest:

`QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json`

Its SHA-256 is
`1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5`.
The final vegetation `SOURCE_MANIFEST.json` is 9,197 bytes with SHA-256
`7358f7d5870d0ed9cc7b12d00baa39e35f5a7b87498dd0e88cc352a3ba505f32`.
It records no external vegetation asset. Raw EPT/LAZ, source downloads,
processing caches, screenshots, simulator output, builds, and APKs remain
outside Git.

The source LAS lacks vegetation classes 3/4/5 and building class 6 in the
bounded work unit. Its elevated-return classification is therefore a visual
heuristic, not botanical identification, current tree inventory, surveyed
height, or navigation data.

## Rejected experiments

Three external-only alternatives were inspected and rejected before product
integration:

- ellipsoid lobes read as rocks or avocados;
- denser lobes increased faceted-rock repetition;
- a raster-derived shell recreated raised plates and ledges.

None of those experiments is in the product dependency closure. The accepted
candidate uses project-authored irregular grove cards with rounded volume cues;
it does not revive the earlier 29-component, 1,960-triangle raised-terrain
candidate rejected by Photoreal Aerial Surface V1.

## Visual QA

The final production Visual QA classification is
`PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`. The lead inspected the
full-resolution canopy views at 250 m, 500 m, 1 km, pattern, and top-down
distances, plus the airport/building, cockpit, terrain-seam, reservoir, runway,
aircraft, and mountain regression views. The contact sheets and source frames
are external at:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\rounded_canopy_volume_v1_20260720_175101\product_validation_semantic_final\visual_qa`

Key files include `photoreal_aerial_surface_contact_sheet.png`,
`visual_qa_contact_sheet.png`, and `screenshots\pasv1_13_canopy_250m.png`
through `screenshots\pasv1_17_canopy_topdown.png`. These captures are evidence,
not committed assets. No new numeric human visual score is assigned and the
prior 8/10 worn score is not revalidated for this APK.

The final representation avoids the rejected raised-ground topology and gives
selected grove regions actual rounded volume. It remains deliberately sparse:
42 repeated instances are representative cues, not continuous dense forest or
final botanical variety.

## Static budget

| Metric | Tagged source | Rounded canopy | Delta | Result |
| --- | ---: | ---: | ---: | --- |
| selected draws | 53 | 54 | +1 | PASS |
| selected triangles | 601,406 | 624,086 | +22,680 | PASS scoped delta gate |
| serialized triangles | 751,459 | 774,139 | +22,680 | PASS |
| visible materials | 26 | 26 | 0 | PASS |
| active materials | 27 | 27 | 0 | PASS |
| estimated texture bytes | 211,849,528 | 211,849,528 | 0 | PASS |
| transparent slots | 3 | 3 | 0 | PASS |
| shaders / keyword combinations | 13 / 18 | 13 / 18 | 0 / 0 | PASS |
| MSAA | 4x | 4x | 0 | PASS |

There are no new runtime textures, materials, transparent slots, or shader
variants. Static planning is not mobile GPU, thermal, stale-frame, tear, or
comfort evidence.

## Validation and build

- vegetation source/contract validator: PASS;
- focused Python tests: PASS 9/9;
- EditMode: PASS 114/114;
- PlayMode: PASS 9/9;
- one-command product validation and dependency closure: PASS;
- production Visual QA: `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`;
- static Quest planning budget: PASS at the values above;
- Android release-style build: PASS;
- APK size: 133,992,887 bytes;
- APK SHA-256:
  `0d31c39f113c3ed5edf286259f35f25cf18852b0129445621b79bf32999851d9`.

The APK and all build output remain external and are not committed.

## Meta XR Simulator

The live Quest-3-profile cockpit session passed connection, nonblank stereo,
HMD, and both Touch-controller checks; both full-resolution eyes and the stereo
contact sheet were inspected. Evidence is external at:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\rounded_canopy_volume_v1_20260720_175101\simulator_semantic_final\meta_xr_simulator\visual_gate`

The aggregate classification is `BLOCKED_MISSING_RECORDED_REPLAYS` because all
ten genuine `.vrs` sessions are absent. Live simulator evidence verifies API
stereo/tracking/composition only. It does not establish standalone Quest frame
timing, thermal behavior, comfort, or transition quality during a worn flight.

## Limitations and decision boundary

- Forty-two representative clusters do not create continuous dense canopy.
- One repeated source cluster and one shared atlas limit species, age, silhouette,
  season, and local density variation.
- Lidar/CHM placement is heuristic and cannot establish botanical or surveyed
  truth.
- Deterministic head-pan, aerial-transition, and motion replay evidence remains
  blocked by the missing `.vrs` recordings.
- The exact APK has not been worn or physically timed on Quest 3.
- This is not FAA approval, navigation data, training credit, certified flight
  fidelity, literal photorealism, or release acceptance.

The bounded next step is a physical Quest visual/performance acceptance of this
immutable APK before treating the rounded grove overlay as hardware-proven.
