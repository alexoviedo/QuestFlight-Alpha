# Golden Eagle Thermal V1 — 2026-08-11/12

## Scope and source

- Branch: `feature/golden-eagle-thermal-v1`
- Starting checkpoint: `95befbd7289d3b6d1edec7b708f10ebe05085541`
- Target: standalone Quest 3, Android ARM64, Vulkan, OpenXR
- Source: `Animated Eagle` by Clement Abai / Asim3d, Sketchfab asset
  `de73b663acbc426e8e6c107fd25e08cd`, CC BY 4.0
- Raw download: 3,683,969 bytes; SHA-256
  `e7fc0fa0dd0f50d089bf75d0a76d686c80281c41fe4e601f2b483d75fa74d818`

The raw archive remains outside Git. Blender 5.1.2 and
`tools/bird/author_golden_eagle.py` produce the committed Blender source,
FBX, recolored albedo, and five animation clips. Exact hashes and the change
notice are pinned in
`Assets/Production/Environment/Wildlife/SOURCE_MANIFEST.json`.

## Corrected representation and motion

The rejected candidate translated a generic hawk through pilot-relative space,
held its sole animation at one normalized time during most of the encounter,
and used `CullUpdateTransforms`. Those facts explain the worn result: a rigid,
unconvincing model moving along an unnatural path.

The replacement is one 3,858-triangle skinned renderer, one opaque material,
and a 1024-square albedo/normal/packed texture set. Unity imports 870 animation
curve bindings. The original 208-frame wingbeat is retained as `FlapLoop`;
`SoarIdle`, `BankLeft`, `BankRight`, and `GlideTransition` provide a bounded
flight vocabulary. `AlwaysAnimate` prevents off-screen pose freezing.

The first lesson encounter snapshots one fixed-world cubic route, advances it
by arc length at 15 m/s, aligns the bird to its path derivative, and joins the
55 m thermal orbit continuously. It is not camera-parented and does not rebuild
or discover hierarchy at runtime. The shared thermal remains a development
native-JSBSim cue; the Unity prototype remains release authority.

The first worn review accepted the replacement model and animation but found
the close staging visually obvious. The correction arms during
`climb_to_reservoir` inside a bounded 1,250 m thermal-approach envelope and
starts the 2.1 m bird 120 m ahead. Its initial angular wingspan is approximately
one degree, allowing it to resolve from distance before the close encounter.

## Validation

- Focused Golden Eagle EditMode: **PASS 5/5**.
- Full EditMode after replacement: **PASS 311/311**.
- Skinned deformation: **PASS**; the test bakes the authored prefab at two
  `FlapLoop` phases and requires more than 0.35 m maximum vertex travel.
- Visual QA: close, raised-wing, extended-wing, route-context, and seated-pilot
  views captured at 1280x720 and inspected. Raised and extended poses have
  distinct image hashes and visibly different wing silhouettes. The bird is
  discoverable ahead-left in the reservoir-route pilot view.
- Aggregate Visual QA remains `FAIL_MACHINE_QA` on inherited whole-scene
  planning ceilings; this is not attributed to the one-renderer bird delta.
- Android build and exact-APK worn review: pending at the time this record was
  authored and must be appended from actual results.

Artifact root:
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\golden_eagle_animation_replacement_20260812_003920`

## Limitations

The bird is a rights-recorded optimized animated eagle recolored as a Golden
Eagle cue; it is not a surveyed species specimen or ecological population
model. There is one bird, one scripted encounter, and one fair-weather thermal.
No bird-strike, live-weather, certified-training, FAA, navigation, or physical
Quest performance claim is made without corresponding evidence.
