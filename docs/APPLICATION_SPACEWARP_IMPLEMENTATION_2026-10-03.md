# Application SpaceWarp implementation

> October 6 consolidation: further optimization is deferred. SpaceWarp remains OFF by default and experimental. See [the preserved future plan](SPACEWARP_DEFERRED_OPTIMIZATION_PLAN_2026-10-06.md) and [consolidation record](MAIN_CONSOLIDATION_2026-10-06.md) for current results; the chronological entries below are historical.

This milestone implements an optional standalone Quest 3 SpaceWarp path. It is
not a claim of measured Quest performance or release acceptance. The initial
unattended build was followed by hardware testing; the October 4 regression work
and its current acceptance limits are recorded below. The last accepted APK is
preserved separately; SpaceWarp defaults off and is selectable in Settings →
Graphics. The same APK retains normal rendering when off or unavailable.

## Production contracts

- Unity 6000.3.8f1, Android ARM64, Vulkan, OpenXR, Meta XR 205; native JSBSim
  remains the only flight authority at its existing 120 Hz simulation cadence.
- URP 17.3.0 Forward with RenderGraph replaces Built-in rendering. No render-scale,
  texture-resolution, MSAA (4x), or accepted Medium FFR reduction is bundled here.
- MetaXRSpaceWarp owns the extension. Unity's alternative SpaceWarp feature is
  disabled. The application requests 72 Hz display refresh and SpaceWarp's 36 Hz
  application cadence; a request is not evidence of actual compositor activation.
- App-space pose comes from the existing OVR `TrackingSpace` **after** aircraft
  presentation, excluding `CenterEyeAnchor` and tracked head pose. The tracked camera, XR origin ownership, aircraft CG and seat
  calibration contracts are unchanged. No second flight or camera stack exists.
- Menu, pause, lost focus/tracking, session changes and discontinuities invalidate
  history. Two rendered frames warm history before resuming SpaceWarp. Missing
  provider motion attachments causes normal-rendering fallback with diagnostics.

## Rendering coverage

`Assets/Production/Rendering/ShaderMigrationInventory.json` identifies generated
shader policies. `tools/rendering/port_spacewarp_shaders.py` deterministically
ports accepted color programs and generates corresponding XR motion coverage.
`VendorMotionSources.json` records the SDK sources of necessary derivatives.

The narrowly embedded URP package is pinned to 17.3.0; `QuestFlightPatch.json`
records modifications. It exposes the provider's existing RenderGraph motion/depth
attachments, seeds history on discontinuities, and supplies inverse eye matrices.
The ShaderGraph patch also preserves clip-space Z in the SpaceWarp varying path;
the upstream XYW packing discarded it. GPU readback tests check positive and
negative depth-direction displacement through the actual glTF motion pass.
The product renderer appends procedural vegetation/traffic and transparent
surfaces to those attachments rather than creating competing XR targets.

Static geometry uses camera motion; moving objects retain previous object state.
Traffic history uses persistent vehicle identity rather than compacted buffer
indices. CPU instrument deformation retains previous presented vertices; bird
skinning has motion enabled and seeds newly visible actors. View-facing grove
cards evaluate their previous view position. Cloud motion uses the same density
march as color, opacity-weighted representative depth, and wind displacement.
Tree selection is shared between the color and motion submissions within a frame,
including identical fade values, rather than repeating the visibility/LOD work.
Legacy scene shaders retain color instancing, but their motion passes use individual
renderer transforms: the legacy instancing include does not supply an array of
previous object matrices. Explicit vegetation/grove/traffic batches keep their
separate stable-history path. Moving-aircraft GPU probes check object displacement
independently of camera displacement. Vulkan stereo signature fields remain after
ordinary vertex attributes/varyings in generated passes.

Transparent glass reflections, blurred propeller and blob shadows deliberately
retain underlying surface motion/depth. This is a single-layer approximation,
not exact reconstruction of overlapping transparency. Cloud edges, foliage holes,
thin geometry and disocclusions remain mandatory physical visual checks.

The cockpit shadow pass now records into RenderGraph; offline authoring/QA camera
captures use the pipeline's render-request API. Existing geometry, lightmaps and
material color equations are retained wherever possible. Material conversion is
not considered visually accepted merely because shaders compile.

## Validation and acceptance

For a development APK with SpaceWarp active, create
`capture-spacewarp.request` in the app's persistent-data directory. The one-shot
diagnostic copies the actual provider attachments after product motion submission,
then writes left/right raw motion and device-depth EXRs under `spacewarp-captures`.
It does not replace the submitted buffers. Pull that directory and run
`python tools/rendering/inspect_spacewarp_capture.py <directory>` for channel
ranges and inspection previews. Gray motion means zero; preview RGB encode signed
XYZ at an 8x display scale. Depth previews are percentile stretched, not metric
distance. A capture/readback failure is an error, not empty passing evidence.
This diagnostic is inactive in a non-development APK and allocates no capture
textures until requested. Raw compositor attachment capture requires the device;
offline numeric shader tests do not pretend to exercise the XR provider.

Use `scripts/validate_current_product.ps1` with an external artifact directory.
It records production integrity, dependency closure, motion coverage, EditMode,
PlayMode and standard Visual QA independently; failures do not become passes
because a Unity process exits successfully. Stereo simulator inspection is a
separate required visual gate. Simulator timing is not Quest timing.

`tools/rendering/evaluate_spacewarp.py` is a separate physical 36/72 evaluator;
the existing native-72 gates are unchanged. Its JSON input requires physical
Quest 3 identity, exact APK SHA256, at least 20 minutes, sufficient valid CPU/GPU
samples, observed application motion submission, independently measured compositor
activation and 36/72 cadence, <1% missed/repeated display frames, CPU/GPU p95 ≤25 ms,
no thermal throttling and worn visual acceptance. The 25 ms value is an engineering
headroom target below the 27.78 ms application deadline, not a measured result.
Archive the raw headset/compositor evidence with its synchronized measurement
window; do not invent these fields from application FPS alone.

Required physical scenarios on the exact final APK:

1. SpaceWarp off/on at the same airport and dense mountain route; assess both CPU
   and GPU work, submission overhead, display misses and thermal behavior.
2. Rapid and slow head turns, translation and banking in both eyes; inspect cockpit
   text, needles, yoke, pedals, propeller, window reflections and near terrain.
3. Dense dark foliage and canopy transitions; traffic, water, clouds, animated
   birds/aircraft and moving training targets. No trails, tearing or stereo mismatch.
4. Demo/manual takeover, lesson placement, local recovery and explicit restart;
   first frames must not inherit stale object/camera history or alter flight ownership.
5. Pause/menu/resume, recenter/calibration/cancel, focus loss, tracking loss and
   session recreation; verify off fallback and subsequent stable reactivation.
6. Settings persistence; unsupported/missing-extension fallback; normal startup,
   training and free flight with SpaceWarp off. No dependency on a connected PC.

Do not promote SpaceWarp to the default until these checks pass. This document
describes implementation and required acceptance; the external completion ledger
records actual outcomes, including inherited failures and unverified hardware.

## Scope and known validation debt

The accepted source baseline is `fdcc160c23360676ca9d87aa3626009785f8e28f`.
Work is isolated on `feature/application-spacewarp-complete`; the previous accepted
APK, Git bundle, local-only preservation inventory and all new evidence are under
the external `application_spacewarp_20261003` artifact directory. No headset
installation or physical performance claim is part of this unattended run.

The historical dependency exporter is still pinned to a different extraction
revision (`cd4131f...`). It rejects 146 paths that already exist in the accepted
Git tree, including the current training lessons and narration. Its failure is
retained, not hidden by broadening the old allowlist. The current production
integrity validator and material/motion coverage audit are separate checks.

The full EditMode run contains existing failures; the external comparison ledger
identifies 26 matching the baseline run. The two PlayMode failures concern touch
override admission during demo ownership and an expectation that all interior
renderers except one pedal are enabled (it rejects `Static_Display`). Their
fixtures and relevant control/visibility behavior were not rewritten for this
rendering work. They remain failed checks, not assumed passes. A vendor resource
leak check also reports one unnamed texture after editor play transitions; its
remaining cause is not established. Asset-bundle cleanup errors exposed during
that check were corrected by waiting for pending requests before unloading and
using the proper asset unload API.

Standard Visual QA rendered all 130 views. The existing planning-budget failures
remain failures; static renders do not establish Quest performance. The final
completion record must additionally identify the saved APK, inspected stereo
captures and any blocked checks before this candidate is handed off.
The largest inspected baseline image difference is the exterior aircraft underside:
the URP glTF material shows brighter cloud reflections than the former built-in
pipeline (`pasv1_19_aircraft_underside_reflection.png`). Geometry and textures are
present, but exact reflection parity is not established. This remains a visual
review item; the migration is not being described as pixel-identical or accepted
on the headset.

## October 4 launch and motion regression correction

The first Free Flight freeze is reproduced through the production native menu
flow: `TimeManager.asset` serialized `m_TimeScale: 0`; the selector saved that
value and restored it when launching the first flight. Returning to the menu
stored 1, so a second launch ran. Head tracking was unaffected. The same native
test failed with expected 1 / actual 0 before the correction. The selector now
normalizes a nonpositive incoming time scale to 1; the serialized default and
project build configuration also explicitly start unpaused. The two native menu
flow tests pass with an initially paused fixture.

Generated custom motion vertices previously projected through the color shader
and inverted that result with the XR motion provider's matrices. Those projections
are not interchangeable. A GPU stereo test with separate eye histories reproduced
missing coverage in the custom cockpit, instrument, aircraft and building passes;
the URP Lit control passed. Motion vertices now carry the actual deformed world
position directly into the provider's current/previous eye projections. Instrument
depth-layer offsets are retained. Color programs are not changed by this fix.
Stereo variants remain compiled; ordinary object batching is disabled on motion
renderer lists separately, so it cannot substitute an incorrect previous-object
matrix. Explicit procedural batches keep their existing history path.

All 25 focused motion tests and both native menu-flow tests pass in
`spacewarp_launch_20261004/motion_final` and `native_final`. The stereo GPU fixture
exercises real shader passes, but is not a Meta XR Simulator or physical compositor
capture. A temporary legacy color-pass draw did not work in that synthetic motion
fixture and was excluded from its motion assertions; this does not establish a
production color regression. Physical confirmation of the reported cockpit glitch
remains necessary. Do not equate these tests with headset acceptance.

## October 4 sustained SpaceWarp regression investigation — incomplete

The user rejected the launch-fix candidate: cockpit glitches occur with SpaceWarp
on even while stationary on the runway; SpaceWarp off does not exhibit the defect.
The prior section records checks performed, not a successful physical correction.

Preserved source archives establish that the original-to-stability candidate change
affected only the activation controller and its tests. The later launch candidate
also changed generated motion programs. The runway worsening was already reported
after the controller change, so the later shader change cannot by itself explain
that progression. The original retained log contains 71 activation transitions in
roughly 6.9 seconds; repeated enabling also resets motion history. Stable activation
may have exposed a sustained-history or workload problem, but the exact compositor
pixel failure is still unproven. Do not restore the activation loop as a workaround.

Added `ProductSpaceWarpRendererListTests` exercises real URP renderer lists and
engine-owned previous-object transforms in the player loop. The final
`spacewarp_history_20261004/renderer_history2` run passes for URP Lit, cockpit
interior, instrument and aircraft shaders, both stationary and during 12 rendered
co-moving camera/object frames per shader. This is desktop evidence, not Android
Vulkan, XR stereo or physical compositor acceptance. An earlier fixture failure
used implicit texture bindings; explicit white atlas inputs corrected that fixture.

The development-only attachment capture now requests bounded sequences of matched
stereo color, raw motion and raw depth, with provider matrices and history metadata.
No shipping rendering correction has been established in this investigation.
Diagnostic Android packaging was stopped to avoid exhausting disk after reaching
native compilation. No diagnostic APK was produced or installed. Three prior
comparison APKs and their source snapshots remain preserved.

An unworn run of the installed launch candidate confirmed sustained App SpaceWarp
submission. Android screenshots did not expose the requested compositor motion
overlay and must not be treated as raw motion evidence. Temporary proximity and
overlay settings were restored, the task-created acceptance request removed, and
the application stopped. No new worn acceptance occurred.

### Resumed after disk cleanup: actual Quest evidence

The storage block above is historical. Development Android builds now complete
and the bounded attachment capture has run on the Quest 3, without a worn visual
acceptance claim. All 1,912 authored scenery files were verified after packaging.
The desktop renderer-list check also passes under Vulkan, with the continuous
runway shader added. Neither desktop result reproduces the hardware defect.

`spacewarp_history_20261004/diagnostic_build3/captures/20261004_192318_650`
contains a valid matching stereo color/depth/motion packet during sustained
SpaceWarp (history generation 8, resetMotion 0). Scenery motion is severely
incorrect: signed NDC values reach the half-float limit of 65,504, while sampled
cockpit motion is approximately 0.0004. At one near runway pixel, the submitted
motion is approximately (-242, -24.5, 1.20). The first history-reset packet has
zero motion; the problem persists in the following five packets. This is direct
evidence of invalid submitted motion, not merely a performance hypothesis.

`diagnostic_build5/capture` adds a private stock URP Lit geometry replay using
the same eye-history matrices. The stock replay has small motion (left-eye
maximum 0.028), while product motion again reaches 65,504. Its coverage differs
from the product materials, so it does not alone establish the guilty transform
or prove every surface is correct. Further input isolation is underway. The
intermediate color target is vertically flipped relative to the provider target;
raw preview orientation must not be mistaken for a shipping projection defect.

Diagnostic build 2 failed to copy external XR targets through the Texture-based
Blitter overload; build 4 failed while querying a transient target during graph
recording. Their empty/failed captures are explicitly excluded from evidence.
The capture now binds the external target identifier with the correct color/depth
subelement and avoids resolving transient handles before graph execution.
An experimental desktop static-batching fixture also failed with stock URP Lit;
it was removed as inconclusive, not treated as proof of a product shader bug.

### Demonstrated object-history defect and fixed-world correction

The subsequent diagnostic build 8 probes the actual generated material programs
in a private replay with culling disabled only for that diagnostic target. At the
same runway pixel, the vertex-history difference is zero, both optional vertex
history flags are zero, but applying `QFPreviousWorld` introduces 5.69 metres of
false object movement. Another runway sample has 15.84 metres of false movement.
The earlier rigid override probe has a correct identity history at those pixels.
Thus clean override materials and one-renderer desktop fixtures were insufficient
to validate the original mixed-material product submission.

Reconstruction of 15,586 bad runway pixels is consistent with an approximately
0.0114-scale aircraft-relative previous transform (99th-percentile fit residual
5 mm), rather than the fixed runway's world transform. The generated fixed-world
passes incorrectly consumed ordinary per-renderer previous-model history. The
exact internal Unity/driver buffer-cache mechanism is not established; it is not
being claimed as a proven vendor bug.

The shader compiler now explicitly classifies 22 immutable scenery shaders in
`STATIC_WORLD_MOTION`: terrain, pavement, bridges, buildings, fixed water surfaces,
fences, and vegetation. Their previous world position remains the actual world
position, with previous view-facing deformation still evaluated where applicable.
They do not consume skinned/CPU previous vertices or an unrelated model matrix.
Camera motion and depth remain present; this is not zeroing scenery vectors.
Streamed page replacement is also not interpreted as movement of the old page.
Moving aircraft, cockpit parts, birds, traffic, UI and cloud motion retain their
existing distinct history paths. No tracked pose or flight state is written.

`static_world_motion_tests` passes 28 focused tests, including new runway/building
checks with deliberately unrelated aircraft-transform and previous-vertex inputs.
The moving-material renderer-list fixture continues testing sustained co-motion;
moving a runway object is no longer used as a valid fixed-world material fixture.
Temporary shader probes and private geometry replays were removed after their
evidence/source snapshots were saved. The bounded actual-provider attachment
capture remains development-only. A source comparison across 130 shader files
finds no changes before their motion passes (`color_program_preservation.json`).

The corrected development APK is `fixed_world_candidate`, SHA-256
`4d8adb31d912f6cf9b4a3f5ccaa340122fe19fca2c4d4e660e19b0800d2afc03`.
Its device attachment verification and final visual/build checks are in progress.
No worn acceptance of this correction is claimed. Do not restore the original
repeated-activation/reset loop as a workaround.

### Follow-through: fixed scenery verified; ambient aircraft history corrected

The fixed-world candidate's Quest packet confirms the large corrupt vectors are
gone. The previously bad runway sample is now approximately (-0.051605, -0.030960,
0.000456), agreeing with independent depth/world/view-matrix reconstruction
(-0.051604, -0.030958, 0.000456). Both eye color and motion previews were inspected.
Only a few distant-object pixels still have false movement. A later experiment
moving legacy shader history declarations into a per-draw buffer did not remove
those pixels and was reverted in full; its source/evidence remains in the external
`per_draw_candidate` artifact. It is not part of the correction.

The existing `traffic_off` diagnostic leaves the remaining defects present.
Reconstructed affected points near (-470,3,-164) and (-505,2.25,-158) match the
authored C172_2 and SuperCub_1 ambient aircraft. Their previous positions wrongly
cluster near the player aircraft around (-626,1.65,-30). These moving-capable
actors cannot use the immutable-scenery policy. `AmbientAircraftActor` now uses
`ProductRigidRendererHistory` for its small existing serialized renderer array:
previous matrices are committed on the product camera's FrameCompleted event,
preserve tint property blocks, include propellers/flaps, and reset on actor
initialization, reappearing LODs, and SpaceWarp history invalidation. The motion
shader consumes that explicit per-renderer matrix and ignores unrelated native
previous-vertex flags for these rigid meshes. No new scene hierarchy, physics,
tracked-camera writes, or color shading is introduced.

`presented_history_tests` passes 29 focused tests. The renderer-list fixture now
waits for its render pass to record a new frame before reading pixels; plain
EditMode coroutine yields had allowed stale images in the expanded fixture.
Earlier static-batch fixture failures were missing coverage, not valid proof of
a static-batching defect. Current coverage includes moving/camera co-motion over
12 actual rendered frames for each material, two parked renderers beside a moving
cockpit, and explicit-history lifecycle/property preservation checks. The actual
Quest captures remain necessary because the original runway defect was not
reproduced by an isolated desktop renderer.

The combined development APK (`explicit_history_candidate`, SHA-256
`85c35e0a271e1e5417d43c56f42929c4ceecdf6695facecfd683aee2850eb35e`)
builds and installs successfully. Its Quest packet removes the parked-aircraft
outliers: zero left-eye and one right-eye pixel exceed 0.2 NDC, compared with
3/52 in the fixed-world-only packet. The remaining point reconstructs near
(-81.42,4.74,-122.93), the windsock fabric using the same aircraft shader.
`BoulderWindSock` now uses the same explicit history helper. This final windsock
addition is subsequent to the recorded development packet, not falsely claimed
as hardware-verified by that packet.

The same development APK also ran the ordinary demo for four minutes. The
`inflight_candidate/capture` packet is frame 2774, history 8, resetMotion 0, at
camera world position approximately (-1211,669,4284), with a banked aircraft.
Neither eye has a motion component above 0.2; maximum absolute components are
0.01952 and 0.02701. Matched stereo cockpit color and motion images were inspected.
This establishes removal of the earlier extreme vectors in this flight sample;
it is not worn compositor, mountain-route, thermal or performance acceptance.

`final_motion_tests` passes 32 focused tests, adding stationary/positive/negative
ambient motion with deliberately unrelated native model/previous-vertex inputs.
The test also checks per-renderer property-block history rather than substituting
global shader state. `rotation_history_tests` also passes all 32 checks after
extending the rendered-frame test to coupled pitch/yaw/bank rotation around an
offset pivot, as well as translation. This includes the final windsock source.

Standard Visual QA produced 130/130 captures. Authored hierarchy, environment and
single-XR checks pass. Overall status remains **FAIL_MACHINE_QA** because 42
materials exceed the 38-material planning target. The prior ASW baseline report
has exactly the same failure, material count, 100 estimated draws and 978,018
maximum estimated visible triangles. Pixel comparison finds 84/130 images
identical; the largest remaining mean channel difference is 1.06/255 (compass
views). Inspected scenery and cockpit diagnostic views show no new color-shading
change. Some early standard-QA views named cockpit omit the aircraft, as in the
baseline; they are not used as proof of cockpit visibility. Actual Quest packets
do show both cockpit eyes.

The final **release** APK built, installed and launched successfully:
`spacewarp_history_20261004/release_candidate/QuestFlight-SpaceWarp-history-fix.apk`.
SHA-256: `d6a86339359833f95db9c04d71285f69988c987db8159795a9643d9e395fb361`.
The installed Android base.apk hash matches. All 1,912 scenery inputs were verified;
no runtime input changed after the archived build snapshot. The application was
stopped after the launch check, diagnostic request files removed and the temporary
proximity override restored. Existing user settings were preserved. The APK was
moved to the external artifact directory (the manifest records both paths).

Disk headroom subsequently became available externally. No proposed superseded
APK/player cleanup was executed. The current isolated simulator player built;
both eyes of cockpit_neutral, head_pan and rotation_climb were inspected. The
runtime registry and persistent simulator configuration were restored/preserved;
the temporary Q: mapping was removed. All three live sessions retain the baseline
mountainIdentityImmutable failure (streaming changes four terrain renderer states).
Recorded head-motion replay is unavailable because the VRS files are missing;
these live captures do not establish moving-head or synthesized-frame correctness.

**Headset acceptance: FAIL.** The user subsequently reports significant tearing
and distortion in the upper half of the view, especially during lateral head
turns; the cockpit is less affected. The raw object-motion corrections above are
not proof that the complete compositor result is correct. Follow-up evidence is
scoped to `spacewarp_sky_20261004`, starting with sky/background and cloud motion
coverage under different color/provider projection conventions. Do not label
this release accepted or the inherited validation failures resolved.

### Upper-view tearing: provider/color projection mismatch

The user confirms upper-view tearing disappears with SpaceWarp OFF. Examination
of the existing banked-flight provider packet shows fixed terrain motion agrees
with reconstruction at the actual provider pixel coordinates (median error about
0.0000038 NDC), while sky motion agrees with the vertically mirrored coordinates.
At the actual pixels, sky error reaches 0.01556 NDC left / 0.02341 right. Those
values are finite, so the former range-only inspection failed to detect them.

`XRMotionVector.shader` reconstructed sky world positions with `UNITY_MATRIX_I_VP`,
which belongs to the color camera and can have the opposite Y flip from the
provider attachment. It now uses the provider inverse VP already published by
`XRDepthMotionPass`, retaining homogeneous coordinates. The cloud generator had
the related inconsistency: color-camera raster placement but provider-projected
fragment depth and motion. Its motion pass now rasterizes through the provider
VP. Accepted color programs, clouds, resolution, FFR, physics and tracked poses
are unchanged. No head-motion cancellation or feature suppression is used.

New rendered tests reproduce the old sky failure for positive/negative yaw and
pitch (roughly 0.10 NDC error when the color projection flips). Cloud fixtures
fail coverage with the old program and pass after the correction, including
stationary/yaw/pitch cases. All 38 focused checks pass in
`spacewarp_sky_20261004/motion_tests`. The packet-inspection tool now reconstructs
background motion numerically instead of accepting finite values alone.

The corrected **development test APK** built and installed successfully:
`spacewarp_sky_20261004/quest_candidate/QuestFlight-SpaceWarp-sky-fix.apk`, SHA-256
`221006560da26b86ae6000493239b26760150eb93619da2aedc9d05622ef9490`.
The installed base.apk hash matches; 1,912 scenery files and 2,047 post-build
runtime inputs were checked, with no changed runtime input. No extra draws,
textures, geometry, resolution reduction or flight/camera changes were added.

The actual Quest packet `device_verification/capture`, timestamp
20261004_220727_145, frame 2638, history 8, resetMotion 0, follows four minutes of
the ordinary demo. Both color and motion eyes were inspected. Maximum sky-motion
reconstruction error is now 0.00001552 left / 0.00006106 right; both pass the
0.001-NDC numeric check. Positive/negative yaw and pitch are covered by the rendered
tests; this unworn banked-flight packet is not a worn head-turn/compositor PASS.
The application was stopped after capture and task request files/proximity
override removed. SpaceWarp was restored to the user's recorded OFF preference.

Fresh standard Visual QA has 130 captures and the same inherited material-count
failure (42 versus 38). Comparison against the preceding candidate finds 79
identical images, with a largest mean channel difference of 0.701/255. Clouds,
sky, cockpit and scenery captures were inspected. The preceding six inspected
simulator stereo images cover unchanged color programs; no new isolated Windows
player was built for this motion-only correction (about 3 GiB disk remains after
Android packaging). Missing recorded simulator head-motion coverage and the
inherited terrain-state machine failure remain documented above. A physical
worn check of the corrected candidate is **NOT RUN**. This is not release or
performance acceptance.

#### Diagnostic preferences incident

An agent-authored ADB `exec-out` stdin write failed after truncating the Android
PlayerPrefs XML while attempting to enable SpaceWarp for diagnostics. A complete
preference backup had not been taken; this was an operational error, not a product
shader change. The user was informed immediately. No full preference recovery
source was found. The known SpaceWarp OFF value and already-completed quick start
were restored using staged, validated, atomic replacement and readback. Other
unrecorded audio/UI/control preferences remain unrecovered and may use defaults;
the user was asked for any customized values. Do not describe all settings as
preserved. Seat calibration, lesson progress and flight history are separate files
and were not overwritten; recovery copies were saved. The corrected diagnostic
helper now validates and saves its input and verifies a staged file before atomic
replacement. It is external tooling, not shipped runtime code.


### October 4 — severe all-view stutter: diagnostic packaging regression

User rejected the installed sky-fix development APK for severe stuttering with
SpaceWarp both on and off. The upper-sky shader correction remains in source.

- The installed APK enabled `Development | AllowDebugging | ConnectWithProfiler`.
  Device startup independently confirms managed debugger startup, a failed host
  connection, and profiler shutdown after filling its 128 MiB buffer.
- Physical Quest CPU sampling (unworn, same stationary main-menu view) identifies
  procedural traffic/tree preparation as the dominant CPU path. There were no
  pending diagnostic capture requests; device thermal status was zero.
- Last ten recorded menu samples: development median 13 FPS (12–16), GPU 9.265 ms;
  prior release reference median 50.5 FPS (48–52), GPU 9.355 ms. This isolates a
  large CPU-side diagnostic-build regression, not proof of sustained 72 Hz or a
  controlled single-flag attribution. The reference predates the sky correction.
- `ProductionBuild.cs` no longer implicitly enables profiling or script debugging
  for diagnostic builds. `build_production_quest.ps1` checks the actual APK's
  boot.config and rejects automatic profiling/debugging before producing its
  success manifest. Guard checked against both archived APKs; PowerShell parse
  and scoped diff whitespace checks pass.
- Corrected release build/install: **BLOCKED on disk cleanup**, about 2.85 GiB
  free. Exact generated Gradle output directories totaling 9.14 GiB inventoried;
  preserved diagnostic APK hash verified against the generated duplicate. Delete
  command rejected by automatic review; user commands sent. No deletion done.
- Device currently has the **prior release reference**, SHA-256
  `d6a86339359833f95db9c04d71285f69988c987db8159795a9643d9e395fb361`,
  installed solely for the timing comparison, app stopped and proximity override
  disabled. It is not the completed sky/stutter fix candidate.
- Next: build release **without -Development**, save/hash, install after Unity
  exits, compare timings, then request worn menu/free-flight check. No new visual
  or flight-authority changes; retain previous visual evidence and its limitations.

Evidence: external `spacewarp_stutter_20261004/` contains device logs, raw and
symbolized CPU profiles, matching native symbols, timing comparison, cleanup
inventory, read-only current preference backup, and exact pending-work handoff.


### October 4 — release rebuild and installation completed after user cleanup

- Verified 29.19 GiB free before build; user performed cleanup. No assistant deletion.
- Release build PASS, `development=False`; automatic profiling/debugging absent
  from the actual APK. All 1,912 scenery payload files verified. All 3,028
  inventoried changed/untracked source files unchanged after build.
- Saved and installed `spacewarp_stutter_20261004/quest_candidate/QuestFlight-SpaceWarp-release-fix.apk`.
  SHA-256 `cf0fa45a587b56318a339579482fe9ca5a68f169aac3088e00c53d316947655b`;
  installed base.apk hash matches. Runtime IL2CPP library is byte-identical to the
  prior release reference; latest sky/cloud projection fixes retained in assets.
- Corrected Quest menu's last ten samples: 48–50 FPS (median49), GPU median9.32ms;
  diagnostic predecessor:12–16 FPS (median13), GPU9.265ms. Similar prior release
  performance is restored without reducing visual fidelity settings. This was
  stationary/unworn and does not establish sustained72Hz or free-flight acceptance.
- Proximity automation disabled after capture; application left at menu. User
  asked to check menu and Free Flight, SpaceWarp OFF then ON, including head-turn
  tearing and cockpit stability. **Worn acceptance pending.**
- Build configuration only changed for this performance correction. Prior visual
  evidence and inherited gate failures remain as documented; no new simulator
  capture or broad regression suite was run for the packaging-only correction.


### October 4 — user feedback on the corrected release

User reports the severe frame-rate issue appears fixed, but SpaceWarp ON still
causes unacceptable distortion; OFF is fine. Therefore the packaging regression
has user confirmation, while SpaceWarp visual acceptance remains **FAIL**.

Follow-up reviewed application-space ownership/SDK code, XR depth/motion imports,
projection/history, final blit/foveation, and custom sky/cloud/glass passes. No
new exact cause is yet established. Existing raw-capture numeric consistency
must not be equated with correct compositor output. A Meta investigation of
FFR/depth artifacts on Unity6000.4.8 is a hypothesis to isolate, not evidence of
the same engine defect here. Requested a30-second stationary head-turn check
with SpaceWarpON and temporary FFR OFF using the documented device override;
original override saved externally, not changed while awaiting readiness. No
new runtime/visual code changes or rebuild performed for this investigation.
Evidence and references: external `spacewarp_distortion_followup_20261004/`.

### October 4 — tracked-head pose incorrectly submitted as application motion

The worn isolation check confirmed `Fov=0` and `Type=App`; the user reported
**unchanged distortion**. The original empty foveation override was restored and
verified; subsequent telemetry returned to `Fov=2`. No user preferences were
edited. This excludes foveation as the source of the reported distortion.

The previous camera-parent assessment missed the actual serialized hierarchy:
`OVRCameraRig / TrackingSpace / CenterEyeAnchor / Main Camera`. The enabled
product camera is a child of the tracked eye anchor; the SDK camera component
on the anchor itself is disabled. `OVRCameraRig.UpdateAnchors` writes head pose
to that anchor, including before rendering. Submitting its world pose through
`MetaSetAppSpacePosition/Rotation` therefore includes head movement in application
motion, although OpenXR already tracks that movement. The SDK's camera-parent
shortcut assumes a camera directly on the eye anchor and does not fit this rig.

`ProductSpaceWarpController.ResolveApplicationSpace` now selects the existing
`OVRCameraRig.trackingSpace`, validates camera ownership, and fails closed when
the authored tracking space is unavailable. Aircraft presentation, seat
calibration and recentering remain included; tracked head pose is excluded.
No hierarchy, camera, calibration, flight, shader, asset or foveation changes.

Saved source archives from the initial SpaceWarp release, defects build, launch
build and history release all contain the incorrect selection. This is a
persistent integration defect; it does not independently prove why every older
build differed in severity. Previous unworn buffer checks did not exercise this
submission-pose error.

- Focused EditMode/GPU/history suite: **PASS, 42/42**. Three new actual-prefab
  cases reproduce the old parent's contamination for positive/negative yaw,
  pitch and translation. Correct application space stays unchanged while the
  camera tracks the head. Aircraft movement/bank, calibration and recentering
  still reach application space. Missing ownership fails closed.
- Release Android build: **PASS**, automatic profiling/debugging absent; all
  1,912 scenery files verified. All 3,028 inventoried changed/untracked source
  files match their pre-build identities.
- Saved APK: external
  `spacewarp_distortion_followup_20261004/quest_candidate/QuestFlight-SpaceWarp-tracking-space-fix.apk`.
  SHA-256 `0227c32ead0473ef8c47bd91e4780dad21d98dd1c4586d42b07d5d59da87a50e`.
  Generated APK moved from the build output to this scoped archive, avoiding a
  duplicate 1.69 GiB copy; no earlier APK or source asset deleted.
- **Physical visual acceptance pending.** First check: stationary runway,
  SpaceWarp ON, lateral head turns and small head translations. Then check the
  same mountain route. Do not call this visually resolved until worn feedback.
  No new simulator capture: this correction changes the physical compositor's
  app-space input, which the simulator does not validate. Prior standard Visual
  QA limitations remain recorded above; this is not a full release acceptance.

Evidence: `confirmed_cause.txt`, `foveation_override.json`, `ffr_off.log`,
`focused_tests/`, and `quest_candidate/` in the follow-up artifact directory.

Installed base.apk SHA-256 matches the saved tracking-space candidate above;
application launched successfully. Worn feedback requested, still pending.


### October 4 — distant wall window filtering

User reports the tracking-space candidate addressed distortion, but distant walls
still flicker with SpaceWarp ON; an OFF comparison was not provided.

Confirmed a separate aliasing defect in FullMapBuildingClipmap.shader: derivatives
of wrapped bay/storey coordinates underestimate the projected window footprint.
The detail atlas also sampled wrapped aperture coordinates with implicit gradients.
Replaced these with integrated unwrapped coverage and explicit metric texture
gradients, retaining average distant window coverage and resolved nearby detail.
The shader generator preserves this correction. No texture, geometry, FFR, XR,
flight or hierarchy changes; no additional detail texture fetch.

Controlled 24-phase GPU reproduction: left/right mean-luminance range reduced
from 0.012071/0.012011 to 0.000085844/0.000086015 (about 140x). Window-suppressed
control remained effectively constant. This proves the shader defect and its
reduction, not that every possible headset flicker has been eliminated.

43/43 focused facade/SpaceWarp checks passed; final near/far probes passed 2/2.
Both eye probes inspected: individual nearby apertures retained, distant detail
averaged. These are offline URP two-view renders, not simulator compositor proof.
Fresh standard Visual QA produced 130 captures; facade, CU close facade and 1 km
building views inspected. Existing material-count excess (42 versus 38) remains.
A fresh isolated simulator player was not built: Android native rebuilding used
most available space, leaving about 4 GB. Stereo simulator gate remains incomplete.

Unity compiled and packaged the release APK, but final output copying failed
with insufficient disk space. Archived the already-signed Gradle package directly;
verified its v2 signature, complete ZIP CRCs, ARM64 IL2CPP payload, absence of
unattended profiling, all 1,912 scenery files, and all 3,028 source identities.
Do not label the failed Unity output-copy step a passed build wrapper.

APK: external building_flicker_20261004/quest_candidate/QuestFlight-building-wall-filter-fix.apk
SHA-256: 815ec1a34357b82270cb9932ba0fc5387c51657aa7fac7098b27b922296fb794.
Evidence: building_flicker_20261004/{before,filtered,verified,filtered_tests,
verified_tests,standard_visual_qa,quest_candidate}. Headset acceptance pending.

Installed base.apk SHA-256 matches 815ec1a34357b82270cb9932ba0fc5387c51657aa7fac7098b27b922296fb794; activity launch succeeded. Worn wall-flicker check requested. Standard visual analyzer exits 2 because the report's overall pass is false; do not treat it as a passed full visual gate.

### October 4 — remaining brown-facade shimmer and ground strut distortion: proposed work

**Approved; implementation in progress, not headset-accepted.** User reports most facade flicker improved, with
residual shimmer most obvious on large brown/possibly brick buildings. Wing struts
distort only with SpaceWarp ON, mainly on the ground; stationary occurrence is
uncertain. The last candidate is a partial improvement, not full visual acceptance.

Evidence reviewed:

- Compared source inventories of the tracking-space APK and wall-filter APK:
  only FullMapBuildingClipmap.shader, its generator, its focused test, and this
  document differ. Aircraft geometry/materials and XR motion code did not change
  between those APKs. This does not exclude an indirect workload effect.
- Facade arrays already use mipmaps, trilinear filtering, anisotropy 4. Red/tan
  brick are material IDs 3/4. Missing mipmaps are not an established cause.
- FullMapBuildingClipmap computes `n` from the facade normal map, then changes
  window albedo/roughness without replacing that normal for window lighting.
  Its direct window highlight therefore follows masonry relief; the cubemap
  reflection uses the geometric normal. This is a confirmed shading inconsistency,
  but its contribution to the reported remaining flicker needs isolation.
- The previous facade probe used material 7/style 0 and disabled normal mapping,
  reflection, and corner lighting. It proved periodic aperture aliasing, not
  stability of complete red/tan brick materials or every facade style.
- Imported exterior parts .003/.005 are the rigid wing-strut meshes (53 vertices,
  76 triangles each) using the glTF Body_MAT ShaderGraph material. Their renderers
  request object motion. No strut deformation/ground animation writer was found.
- ShaderGraph XRMotionVector render states omit the color pass's switched cull
  state, while Body_MAT is double-sided. Source-mesh center-ray analysis from the
  authored default eye found no visible back-facing triangle centers on either
  strut. Thus the culling inconsistency is not proof of the reported symptom.
- Earlier device motion/depth attachments were 420 x 440 per eye. Existing capture
  code resamples color to that same resolution, losing full-resolution edge
  evidence. Renderer-list co-motion tests omit the imported glTF shader; its
  separate direct-draw test supplies matrices manually and does not close this gap.
- Read current device logcat into external `facade_strut_diagnosis_20261004/`;
  it contains no usable new product event or matching per-strut attachment capture.
  No exact current-frame compositor cause has been established.

Implementation sequence after approval:

1. Preserve the installed candidate and keep the successful aperture filtering.
   Reproduce full-material red/tan brick and a nonbrick control with actual light,
   reflection, facade styles and oblique/pixel-scale movement. Identify the actual
   reported building renderer before extending changes to another building shader.
   Isolate albedo, normal/specular, reflection and aperture contributions one at a
   time in transient diagnostic materials; inspect per-pixel temporal error as
   well as mean brightness. Check overlapping surfaces if flicker survives an
   unlit constant-color control. Do not label this z-fighting without depth proof.
2. Correct confirmed facade terms only in FullMapBuildingClipmap.shader and
   port_spacewarp_shaders.py. Separate masonry and window lighting normals;
   combine their contributions by the filtered aperture coverage. If unresolved
   normal/specular variation is confirmed, apply footprint/variance-aware filtering
   to that contribution, preserving close brick relief and distant average color.
   Use filtered reflection LOD only if reflection isolation demonstrates need.
   Do not blur all buildings, remove windows, raise global MSAA, or change roofs.
3. Close strut evidence gaps using the actual imported meshes/material and real
   renderer-list submission, with shared aircraft/camera movement and independent
   head movement. Capture both eyes at rest, taxi, takeoff and airborne, including
   a ground-versus-distant-background control. Diagnostic capture must retain native
   color resolution plus provider-resolution depth/motion, viewport and per-eye
   matrices, app-space pose, and current/previous strut transforms from one frame.
   Keep diagnostics request-only; no automatic profiler/debugger attachment.
4. Select the smallest strut correction from that evidence. If depth coverage or
   object history is wrong, fix that specific pass/state and test both sides and
   reset/startup. Preserve the accepted app-space ownership correction. If interior
   motion and coverage are correct but only boundary texels inherit background
   motion, evaluate a depth-aware strut-only motion-coverage guard, bounded initially
   to at most one provider texel, with no visible mesh thickening and no global
   motion blur. Reject it if it creates a halo, hides nearby geometry, or increases
   distortion. Do not silently disable SpaceWarp on the ground or zero all aircraft
   motion. If no bounded mitigation succeeds, report the remaining compositor
   limitation and seek a separate decision before a larger rendering redesign.
5. Validate full facade materials at close/mid/far scales in both eyes, including
   windows, roof boundaries, reflection angles and material transitions. For struts,
   verify finite correct motion/depth through several rendered frames, head movement,
   ground acceleration, liftoff, landing and SpaceWarp OFF/ON. Screen-stationary
   interior points should match analytically expected motion (initial tolerance
   0.25 provider pixel); independent head motion must remain represented. Test
   silhouettes separately, not only center pixels or frame-average brightness.
6. Inspect focused stereo simulator captures and standard Visual QA; retain the
   known material-count gate failure separately. Build one release Quest candidate
   without auto-profiling, verify source/APK identities and scenery payload, then
   perform one combined worn check of the actual brown buildings and both struts.
   Compare on-device CPU/GPU/frame pacing on the same route; no new global render
   target, draw pass, resolution or FFR change is authorized merely to mask these
   symptoms. Physical acceptance remains required.

References: Meta's official [SpaceWarp sample](https://developers.meta.com/vr/documentation/unity/unity-sample-app-spacewarp/)
and [railings/disocclusion explanation](https://github.com/oculus-samples/Unity-AppSpaceWarp#railings)
document thin-geometry boundary artifacts even with correct motion data. This
supports an alternative to test, not a diagnosis of the current strut capture.

Implementation evidence (October 4, current work):

- External evidence: `facade_strut_fix_20261004` under the established product artifact root.
- Actual glTF shader passes engine renderer-list stationary and co-moving history.
  Both authored strut meshes also pass 12 co-moving frames per eye position
  (48 samples), with a 0.0011 NDC motion tolerance. This is an isolated desktop
  render test, not a compositor or headset acceptance result.
- Full-material red/tan brick probes did not support globally reducing normal-map
  detail: normal-disabled controls changed temporal variation little.
- Confirmed atlas-coordinate mismatch: the Python generator packs detail cells
  top-down; the shader addressed them bottom-up. Styles 2/3 reached empty cells.
  Corrected shader cell orientation and explicit sampling gradients, retaining
  the existing atlas and GUID. The regeneration script applies the same correction.
- Separated flat-glass and masonry lighting and blended their final radiance by
  filtered aperture coverage. Empty aperture cells no longer receive a phantom
  window specular term. Close detail and roofs are retained.
- Seven facade sampling cases pass. At 32-pixel facade width, measured eye-0
  mean-luminance range changed from 0.0003031 to 0.0001185 for tan brick and
  0.0003004 to 0.0002887 for red brick. These limited synthetic cases do not prove
  the reported buildings are fully corrected.
- Request-only capture now retains native color size alongside provider-resolution
  motion/depth, with separate dimensions in metadata. Diagnostic inspection accepts
  those separate dimensions. No production resolution or FFR change.
- Strut root cause remains unconfirmed. No strut geometry, motion expansion,
  culling policy, or app-space ownership change has been made. Native Quest capture,
  appropriate correction, final visual gates and release build remain outstanding.

Current handoff:

- Final facade source preserves upright detail within the corrected top-down row;
  eight focused facade checks pass (`facade_final_tests`). The earlier diagnostic
  APK predates this final within-cell orientation correction. It is suitable for
  strut attachment diagnosis, not final facade acceptance.
- Diagnostic Android build and 1,912-file scenery payload verification PASS;
  automatic profiling/debugger attachment disabled. Saved APK:
  `facade_strut_fix_20261004/quest_diagnostic/QuestFlight-facade-strut-diagnostic.apk`;
  SHA-256 `6f73c7e2b47b0a448eccc8de9b6d69429a4752eabf78770bba614961202e0c58`.
- Installation NOT COMPLETE: first transfer interrupted; retry reported device
  offline. ADB reconnect did not restore an online device. Do not claim this APK
  is installed or headset accepted.
- Standard Visual QA captured 130/130 images, but reports FAIL_MACHINE_QA:
  material/draw/triangle gates and runway-contact-shadow checks failed. Inspection
  also found the default-cockpit shot lacks the cockpit. These results cannot serve
  as visual acceptance; whether those broader harness failures are inherited has
  not been established. Inspected CU facade capture retains windows and relief.
- No fresh stereo Meta XR Simulator run completed. Final combined release build,
  stereo validation and physical strut/head-motion acceptance remain pending.
- Next action: reconnect/wake Quest, install the saved diagnostic APK, capture the
  actual strut with SpaceWarp ON on the runway, then select the correction from
  its color/depth/motion evidence. Rebuild the final source after that correction.

Follow-up capture, October 4 evening:

- The diagnostic APK was installed and its on-device SHA-256 matches the retained
  artifact above. User selected Free Flight with SpaceWarp ON and held a strut view.
  Provider logs confirm active SpaceWarp. Full-resolution stereo color and provider
  depth/motion are retained under `headset_capture/20261005_013138_754` (UTC filename).
- User reported: "it seemed like that probably fixed the strut distortion".
  This is a provisional observation, not proof of a strut correction: no strut
  geometry or rendering changes were shipped in that diagnostic APK.
- Reconstructing previous/current world positions from motion/depth and captured
  matrices yields consistent rigid displacement for cockpit and strut interiors.
  Conservative interior samples: left 382 pixels, right 276; maximum difference
  from cockpit median displacement 0.109/0.119 mm. Both color captures show a
  straight, covered strut. Script and JSON evidence are retained beside captures.
  This excludes a large strut-specific interior history error in this captured
  frame; it does not validate synthesized frames, silhouette disocclusion, every
  head turn, or all taxi/takeoff conditions. Do not add an unproven motion-coverage
  expansion based on this run. Original intermittent cause remains unestablished.
- Corrected a demonstrated editor-only QA defect: `RenderRequiredShots` cached the
  eye before `SelectDemo` reset the native aircraft to its route start. Refreshing
  the eye and reference pose after selection restores the cockpit in its capture.
  Runtime camera and flight logic are unchanged. The rerun captured 130/130 images,
  but still fails draw/triangle/material and ground-shadow gates. No gate weakened.
- The user removed the verified duplicate generated debug APK; the installed
  diagnostic artifact and prior accepted APK remain preserved. Release candidate
  build is in progress under `quest_candidate`; final headset acceptance pending.

Release build follow-up: native compilation completed, but Gradle
`compressReleaseAssets` failed with "There is not enough space on the disk".
No release candidate was installed. About 5.97 GB of obsolete debug assets and
compressed-assets staging was inventoried in `debug_staging_cleanup_inventory.json`;
the user confirmed cleanup after automatic cleanup approval rejection. The three inventoried debug staging directories are absent; about 23 GB was free before retry.
Retain the successfully installed diagnostic APK above. Resume with the existing
release build tooling (retry underway in `quest_candidate_retry`); do not report the failed package as a
completed or accepted build.
Release retry outcome: `quest_candidate_retry` failed at Gradle packaging/lint
with disk-write errors after consuming the available space. No new APK was
produced or installed. The installed diagnostic remains unchanged. This is a
build-storage blocker, not a passing release gate. Fresh isolated Meta XR
Simulator stereo validation is also blocked by disk headroom; standard Visual
QA and focused checks remain as recorded above.

Five superseded experimental APKs (about 9.15 GB) were inventoried with hashes in
`obsolete_apk_cleanup_inventory.json`. Automatic approval review rejected deletion
with "blocked by policy"; exact commands were provided to the user. Current
diagnostic, recent accepted fixes, pre-ASW baseline, source assets and logs remain
preserved. Resume packaging after user cleanup; verify any generated Meta input
binding leftovers before retrying, and do not weaken preflight checks.
Final packaging follow-up (October 4): user completed the five superseded APK
removals; their absence is verified in the cleanup inventory (9,153,774,738 bytes).
`quest_candidate_final` release build PASS; all 1,912 scenery files verified,
automatic profiling/debugging disabled. Retained APK:
`quest_candidate_final/QuestFlight-facade-final.apk`, 1,815,018,153 bytes,
SHA-256 `c69dc0dafceff53ccbaf5a121c231402b5136a5dc6bed9523b479fdadf268cfb`.
Exact source identity and patch archive are alongside the APK. Installation is
in progress. Focused facade checks remain 8/8 PASS; standard Visual QA remains
FAIL on the recorded resource/shadow gates. Fresh isolated simulator stereo run
remains NOT RUN (about 7 GB disk headroom after packaging); do not equate isolated
shader tests or device attachment captures with that required simulator gate.
No speculative strut geometry/history correction was added. Final facade visual
acceptance on physical Quest remains pending.

Installation completed successfully; on-device base.apk SHA-256 matches the final
release hash above. App launched via its UnityPlayerGameActivity. Evidence:
quest_candidate_final/installation.json and launch_logcat.txt. Headset acceptance
of this exact final APK remains pending user feedback.

### October 4 — final release headset feedback: takeoff-roll strut defect remains

On final APK `c69dc0dafceff53ccbaf5a121c231402b5136a5dc6bed9523b479fdadf268cfb`,
user reports building flicker seems fixed, but struts distort/flicker while accelerating
down the runway and remain stable stopped and airborne. Facade change is visually
accepted by this report; strut issue is **not resolved**. Prior stopped capture and
isolated rigid-object tests do not cover this failing condition. Narrow follow-up is
retained in external `strut_takeoff_20261004/investigation.txt`. Motion-buffer edge
contamination by nearby fast-moving runway scenery is the leading hypothesis, not a
proven cause. Await a takeoff-roll attachment capture before choosing a correction.
Takeoff-roll follow-up evidence and candidate:

- User reproduced visible strut distortion; six actual provider captures from the
  diagnostic APK are saved under `strut_takeoff_20261004/20261005_021539_890` through
  `20261005_021553_848`. App-rendered stereo color shows straight struts; all six
  captures retain history generation 7 and resetMotion=0. In conservative central
  strut samples, reconstructed world displacement differs from cockpit displacement
  by at most 0.281 mm. No large strut-specific transform/history error is evident.
- Adjacent runway/strut motion discontinuities reach 24–81 color pixels across the
  sampled edge neighborhoods (capture readback perturbs timing; these are diagnostic
  frame values, not normal-cadence or Quest performance measurements). Provider
  motion/depth is 420x440 versus 1680x1760 color. This supports the documented
  thin-geometry motion-discontinuity mechanism in Meta's official Railings sample:
  https://github.com/oculus-samples/Unity-AppSpaceWarp#railings . Synthesized frames
  are not captured by this instrumentation; final causal/visual acceptance still
  requires the guarded candidate on hardware.
- Candidate: preserve original XR motion/depth rasterization and add a guard draw
  only for the two existing authored wing-strut renderers. Production aircraft
  lighting opts those exact renderer names into rendering-layer bit 30 and sets a
  one-provider-pixel guard. The embedded XRDepthMotionPass filters that layer for
  its second renderer list. Shader Graph's XR motion vertex raster position expands
  by at most one target pixel along the projected normal; original motion varyings
  and clip Z/W remain. Color geometry/materials, tracked poses and flight physics
  are unchanged. Ordinary object and transparent motion passes restore guard=0.
  Cost: two extra small motion-only draws, not an extra color render or full-screen
  buffer. No measured Quest timing gain/loss is claimed.
- Rejected first attempt: replacing exact coverage with expanded vertices lost 22
  pixels at mesh seams. The additive version preserves original coverage instead;
  tests were not weakened. An intermediate fixture erroneously drew one renderer
  list twice; corrected to independent lists matching the production submission.
- Final focused checks PASS 2/2 (`guard_final_tests`): both actual struts/both eyes,
  original accelerating co-motion sequence plus guarded movement; zero original
  pixels lost; added pixels 625/609/479/503 within the bounded raster neighborhood.
  Existing stationary/comoving shaders and mixed parked/moving history also pass.
  Both-eye strut coverage images inspected. These isolated renderer tests are not
  a Meta XR Simulator or compositor acceptance claim.
- Building-fixed release c69dc0... was restored and its device SHA-256 verified
  after diagnostics. Guard candidate build underway; do not call strut resolved
  until the same accelerating-runway condition passes on the candidate.
Guard candidate Android release build PASS: retained
`strut_takeoff_20261004/quest_candidate/QuestFlight-strut-motion-guard.apk`, SHA-256
`4182796eeeb3997f7481e1c00cb2a89bc0d2f6a80b751f20af2d65b271438d88`.
All 1,912 scenery files verified; no unattended profiling/debugging. Source identity,
patch archive and package manifest are retained with the APK. About 5 GB free after
packaging; isolated Meta Simulator cold build remains disk-blocked. Installation
underway. This is a headset-test candidate, not full visual/performance acceptance.

Strut-guard candidate installation PASS: on-device base.apk SHA-256 matches
4182796eeeb3997f7481e1c00cb2a89bc0d2f6a80b751f20af2d65b271438d88.
App launched; user asked to repeat acceleration with SpaceWarp ON and report
residual distortion or new halos. Hardware acceptance pending. Standard Visual
QA rerun in progress under strut_takeoff_20261004/visual_qa.

Standard Visual QA completed: 130/130 captures, overall FAIL_MACHINE_QA. Comparing
with the pre-guard final facade report shows identical errors, maximum estimated
draws (100), triangles (994630), and materials (41); retained comparison is
`visual_qa/gate_comparison.json`. Inspected side-window capture retains an existing
QA-view geometry intersection also present before this change; do not use it as
proof of a clear in-headset strut view. Physical stereo color and both-eye isolated
coverage evidence are the useful strut evidence. No thresholds were weakened and
no full QA/Quest performance acceptance is claimed. Current candidate is installed
and user takeoff-roll feedback is pending.
### October 4 — user-approved altitude standby policy

The user reports the strut guard did not materially improve takeoff-roll distortion.
Do not mark that mitigation accepted. The five files changed for that experiment
were restored to their retained pre-guard versions, preserving the earlier building,
tracking-space and renderer-history fixes. The rejected implementation is retained
externally under `spacewarp_altitude_gate_20261004/before` and prior build evidence.

Implemented the user's requested workaround in ProductSpaceWarpController:
- Read native FlightDynamicsCoordinator.CurrentState.altitudeMslMeters, not Unity Y
  or legacy AircraftState.altitudeFt (which the coordinator fills from AGL).
- Enable only in a supported flight mode, with initialized finite native state,
  airborne, saved preference on, and MSL altitude >= 5,500 ft.
- Once eligible, retain eligibility down to 5,450 ft; below that disable. Ground
  contact always inhibits it, including hypothetical ground above 5,500 ft MSL.
- Invalid state, selection/off preference, mode/history/lifecycle reset re-arm the
  gate. Existing tracking/focus/pause/display-frequency/provider/history protections
  remain. Reactivation uses the existing history reset. The saved preference is
  never cleared by altitude standby. No physics or tracked-camera writes added.
- Graphics menu explains the 5,500/5,450 ft policy and existing status reports
  standby/on-ground/below-threshold rather than implying SpaceWarp is active.

Focused tests PASS 12/12 (altitude sequences, dead band, later re-arming, preference,
mode, initialization, ground, non-finite native state, high-altitude lesson start,
and existing physical display frequency gate). Evidence: external
`spacewarp_altitude_gate_20261004/tests`. Build pending disk headroom; about 4.8 GB
free after tests. Exact obsolete-APK cleanup commands were supplied after automatic
approval review blocked deletion. The workaround avoids the reported takeoff
condition; it is not a general cure for SpaceWarp thin-object artifacts over high
terrain. Headset cadence transitions/performance remain unverified.

Altitude-policy standard Visual QA finished: 130/130 captures; overall FAIL_MACHINE_QA
on the existing resource/shadow gates. Baseline comparison retained in visual_qa/gate_comparison.json.
Flight-mode menu capture inspected; existing QA-view artifact remains, so this is not
a full visual acceptance. Guard-removal hashes match the accepted facade candidate
source for all five files (guard_removal_verification.json). Android build/install
and physical cadence-transition check remain BLOCKED pending disk cleanup; current
headset package is still the previous guard candidate.


Altitude-policy deployment follow-up: the user reported unchanged distortion up to
roughly 5,300 ft. ADB verified the installed package still had SHA-256
4182796eeeb3997f7481e1c00cb2a89bc0d2f6a80b751f20af2d65b271438d88
(the rejected strut guard); the altitude policy had not yet been built/installed.
Do not treat that observation as an altitude-gate failure.

With disk space freed, Android compilation and Gradle packaging completed. Unity's
final APK copy failed for insufficient temporary disk space. The completed Gradle
release APK was retained by moving it directly to the scoped external artifact
folder, without rebuilding or modifying its bytes. APK signature verification and
all 1,912 scenery payload checks passed; boot.config has no unattended profiling or
debugger. The existing ProductionBuild.ValidateCompiledProductShaders gate was run
separately for Android and exited 0. The original failed-copy build log remains;
this is a recovered Gradle package, not a claimed successful wrapper exit.

Saved candidate: spacewarp_altitude_gate_20261004/quest_candidate/
QuestFlight-altitude-gated-SpaceWarp.apk.
SHA-256: 575ea4afe1f5c82edccfc797d85cf330aba9c47242c3322d34dbed39e16f0f0b.
Source identity, changes archive, payload inventory, shader log and package manifest
are retained beside it. Physical altitude-transition acceptance remains pending.

Installation verified on Quest 2G0YC5ZG8907TD: base.apk SHA-256 matches
575ea4afe1f5c82edccfc797d85cf330aba9c47242c3322d34dbed39e16f0f0b.
App launched successfully via its production activity. Installation evidence:
quest_candidate/installation.json. User altitude-transition/strut acceptance pending.


### October 5 — worn OFF/ON performance comparison

User accepted the altitude workaround visually for now, then completed two worn
8-minute demo runs on APK575ea4afe1f5c82edccfc797d85cf330aba9c47242c3322d34dbed39e16f0f0b.
External evidence: spacewarp_worn_comparison_20261005/RESULTS.md and raw logs.
Compared elapsed flight seconds90–450,360 VrApi samples per mode, filtered to each
app PID. OFF compositor type PTWFromStereo; ON type App independently verified.
ON activated at5500.2ft MSL. End-of-run pause excluded from analysis.

OFF/ON mean application FPS39.60/32.51; mean App GPU20.47/24.66ms; p95 of one-second
App GPU samples24.90/30.99ms. Mean reported stale counts36.59/9.66 per sample, about
73.6% lower with ON. This is reduced stale reporting, not73.6% compute headroom.
ON exceeded its27.78ms application interval in80/360 GPU samples. No dependable
spare rendering budget established; sustained36/72 performance acceptance FAIL/not
achieved in this comparison. Android thermal status0 and VrApi PLS0 throughout
sampled checks; no20-minute thermal gate performed. Ordered runs, variable clocks,
unrecorded gaze and no continuous geographic trace limit exact causal attribution.

Important metric interpretation: VrApi App is application GPU time. GD is boundary
GPU time; older parser direct-GPU labeling is incorrect and was not used here.
CPU&GPU is render-thread-start to GPU-completion wall time, not pure CPU work.
Independent reproduction script and verified summary are retained externally.
No product implementation changes made for this measurement. Prioritize profiling
GPU color/motion-depth passes and render-thread work before increasing visual load;
no specific object/shader culprit is proven by these timings.

### October 5 — comparative visual feedback and investigation

The user subsequently reported that SpaceWarp ON looked and felt worse: more stutter,
artifacts, aliasing and distortion. Record this comparison as visual FAIL. The earlier
acceptance of the altitude workaround remains distinct from overall SpaceWarp quality.
Reduced stale-frame reporting is not visual acceptance or dependable compute headroom.

The code/log review, ranked bottleneck candidates, research and proposed optimizations
are recorded in
[SpaceWarp performance investigation](SPACEWARP_PERFORMANCE_INVESTIGATION_2026-10-05.md).
The existing data proves missed application deadlines; it does not isolate every
artifact or provide exact CPU/per-pass GPU attribution. Implementation and additional
testing await explicit approval. Only documentation was changed for this investigation.

Critical-review revision: independently reran the comparison and six failing takeoff
captures, preserving originals; 14 relevant current files match the tested APK source
manifest. Reconfirmed sampled strut-interior error <=0.2804 mm and large edge motion
discontinuities. The serialized cloud field is one draw/96 triangles, so cloud-sector
culling was demoted. Any necessary wearer-dependent baseline capture now precedes
render fixes, with a proposed 90-second observation/three-minute wearing cap instead
of constructing another four-block benchmark. No wearer test requested and no product
fixes implemented. Reanalysis evidence: external spacewarp_plan_review_20261005/.
