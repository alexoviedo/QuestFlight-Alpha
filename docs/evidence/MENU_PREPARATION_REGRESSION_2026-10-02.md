# Menu preparation regression — 2026-10-02

The physical Quest user reported that the first Free Flight selection remained at
“Preparing flight” until navigating to other options and selecting Free Flight again.
The earlier ESP32 control check was accepted by the user; this repair changes no
ESP32 firmware or control mapping.

## Reproduced cause

CockpitSystemsModel.Step rejected a zero presentation timestep and invalidated its
snapshot. While navigation or app focus paused Time.timeScale, the cockpit adapter
alternated between resetting a valid snapshot and invalidating it on the next
Step(0). The startup transition requires consecutive valid cockpit snapshots, so
this combination could wait indefinitely. An EditMode test failed against the
baseline model and passes with the repair. A test using the real native JSBSim
backend also exercises Free Flight selection followed immediately by menu pause.

## Repair

- A zero presentation timestep retains a valid authoritative cockpit snapshot and
  freezes instrument response. Negative/nonfinite timesteps and invalid native
  telemetry remain rejected. Native JSBSim remains the sole flight authority.
- A successful selection explicitly activates its authored transition panel,
  including after calibration hid it.
- Cockpit preparation has a ten-second unscaled deadline. Missing/disabled or
  persistently invalid cockpit presentation returns to an actionable paused menu
  with an error instead of spinning indefinitely.
- Returning to the main menu cancels any queued activity so it cannot start during
  a later unrelated flight selection.
- The world prefab's derived environment budget counts were refreshed to match
  its existing authored content (652137 triangles); no geometry was authored by
  this repair.

## Validation

Artifacts are external to the repository at
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\menu_hang_2026-10-02`.

The focused PlayMode suite passed 21 tests with zero failures/skips, including
real native cold launch, an immediate navigation pause, repeated Free Flight /
Demo / Training changes, all six lesson briefings, transition reactivation,
preparation failure recovery, and cancellation of queued activity.

The final focused EditMode run passed 45 tests with zero failures/skips, including
21 submenu Back routes, 13 tool page round trips (including alternate map/control
return destinations), guest progress isolation, quick-start/calibration return,
and cockpit presentation checks.

The full EditMode run completed with 526 passes and 28 failures. One stale guide
test expected Settings to return directly to App; it now expects the authored
Tools parent and passes in the focused rerun. The remaining failures include
older scenery ceilings/topology signatures, cockpit lens/asset contracts, and
frozen source hashes. Their exact names are retained in `editmode/failed_cases.txt`.
This repair does not claim a passing full project suite.

The release Android build succeeded (ARM64/IL2CPP, Vulkan, OpenXR, required native
JSBSim). All 1912 packaged scenery files were verified. APK SHA-256:
`f41159b963e214af5b47454585196f05dde5d43652ff96f72a1b549e2d517a86`.
Its source inventory and archive are in `build/`; generated APKs remain uncommitted.

Standard Visual QA ran and was inspected. Its overall result is FAIL_MACHINE_QA
because the current scene has 42 materials against a planning target of 38.
The interface capture set contains readable selector, pause, briefing, and debrief
images, but the first eye after changing a page sometimes omits that page while
the second eye displays it. An attempted camera warm-up did not solve the capture
artifact and was reverted. These editor images do not establish stereo acceptance;
the live simulator check was recorded separately.

The isolated Windows simulator development build succeeded with zero build errors.
The live native Free Flight session completed its transition and produced inspected,
consistent left/right cockpit images (`meta_xr_simulator/qa_native_transition/`).
A second live run entered Training, completed the authored transition, and captured
all ten tools pages (`meta_xr_simulator/qa_run/`): Map, Kneeboard, Comfort, Controls,
Bindings, InputPreview, InstrumentHelp, Captions, ControlDiagram, and Checklist.
Both eye images for every page were inspected: text/buttons were present and readable,
with no observed page disappearance or clipping. These captures preview presentation;
they do not constitute physical button-dispatch acceptance for every setting.

The simulator machine gate still fails its existing mountain mesh/transform identity
check. The orchestration report is BLOCKED_MISSING_RECORDINGS, since live smoke
captures do not replace recorded replay coverage. The scripts restored the original
OpenXR runtime/configuration and removed their isolated development project. No
shipping configuration or tracked camera transform was modified for these captures.

The first in-place USB installation attempt failed when the headset went offline.
Windows still detects the Reality Labs ADB interface and Quest 3, but both local
ADB versions report device `2G0YC5ZG8907TD` offline. Restarting ADB and reconnecting
offline transports did not restore communication. A targeted Windows interface
restart was denied by Windows; no driver configuration was changed. The user was
asked to wake/reseat the headset USB connection.

On October 3 the headset returned online. The corrected APK above was verified
against its recorded SHA256 and installed successfully using an in-place
`adb install --no-streaming -r`, preserving application data. Android reports
`lastUpdateTime=2026-10-03 10:47:25`. QuestFlight was force-stopped and freshly
launched, with logcat captured under `headset_acceptance_2026-10-03/` in the
external artifact directory. The first-launch Free Flight/HOTAS test has been
requested with the user wearing the headset and both Touch controllers awake;
the user subsequently reported, "Looks fine on the headset for right now."
This records a positive current headset observation, not an exhaustive acceptance
of every menu, setting, lesson, or sustained flight scenario.
Batch tests do not establish physical headset input/focus behavior, frame-rate,
or thermal acceptance.
