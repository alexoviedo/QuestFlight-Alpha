# Connected grove pilot — September 9, 2026

Historical sparse-pilot build and device evidence. The newer [fuller foliage composition](FOLIAGE_VISIBLE_IMPROVEMENT_2026-09-09.md) supersedes its appearance; the installed APK described here does not contain that newer revision. Worn testing is currently on hold.

One complete, inspected 2,025 m² LiDAR component is implemented for a bounded headset comparison. This is not map-wide completion, statistical source acceptance or Quest performance acceptance. The renewed approval to continue through headset readiness revised the order of delivery; the unresolved independent 30-crop benchmark still gates geographic expansion.

**Delivery status: READY FOR A CONTROLLED WORN PILOT COMPARISON.** The corrected Android APK is installed and its device SHA-256 verified. Both baseline and candidate observation dry runs pass with native JSBSim initialized and zero native/simulation/presentation position error. The app is stopped, the queued request is removed and temporary power automation is restored. Worn appearance and GPU/thermal acceptance remain pending; the standard and simulator QA failures below are not waived or labeled passed.

## What changed

- Offline composition follows the connected source polygon and measured upper canopy heights rather than separate old-tree circles. Lower foliage placement remains an explicit estimate.
- 304 core and 126 near-interior clusters reuse the production branch atlas at local leaf scale; maximum card width is 4.5 m. No ground-photo sheet, giant scaled tree or opaque grove shell is used.
- Six owned original crowns are suppressed only while valid replacement assets exist. Original wood is retained. The existing 230-tree neighborhood replacement is unchanged.
- Core geometry remains present with distance. Only additional interior foliage fades, using projected extent and the same decision for both eyes. Invalid/disabled replacement restores original crown ownership.
- Authored prefab meshes use 2,016 core triangles and 756 near triangles, at most two additional draws, 311,712 bytes of measured Editor mesh residency and no new texture atlas. These are geometry/resource observations, not measured GPU savings.
- Both comparison variants disable the rejected angular overlay. The baseline switch disables only the new connected pilot and restores its original crowns.
- Two authored observation providers allow explicit, expiring requests to place the aircraft through the existing native-JSBSim reset API and pause flight for a visual comparison. OpenXR retains camera ownership. Restart without a request restores ordinary startup.

## Evidence

External artifacts: `../T2-QuestFlight-product-artifacts/connected_grove_pilot_20260909` relative to the repository parent.

- [Paired review](../../T2-QuestFlight-product-artifacts/connected_grove_pilot_20260909/comparison.html): 32 offset-eye Editor captures across eight near, side, overhead and distant poses. These are not tracked-XR captures.
- Inspected near aerial, low-side, both-eye and approximately 400 m views. The composition creates a wider connected foliage volume without broad photographic panels; foliage remains airy and its middle-distance improvement is modest. User appearance acceptance remains open.
- Six Unity checks pass: ownership/fallback, continuous near fade, encoded tint, bounded observation request, isolation restoration and paused presentation reset. A double color-space conversion found during inspection was corrected before the final paired captures.
- Standard QA: 130/130 captures, hierarchy/environment contract true, one XR Origin true, mountain sweep true. Contact sheet inspected. **FAIL_MACHINE_QA** remains: external control sweep 80 estimated draws and approach 84, against 77. Maximum visible triangles 694,662 and materials 39. Earlier September 7 QA already failed these same views at 79 and 82; that historical comparison is not an exact matched baseline. No threshold was raised.
- The separate Windows development build succeeded (zero build errors), and the live XR run produced inspected left/right/stereo images with distinct 60 mm eye poses, camera ownership and authored hierarchy checks passing. **Simulator acceptance failed:** Android aerial/building bundles are incompatible with the Windows player, runtime identification did not pass, and mountain identity did not pass. The observed captures show the mode menu, not the grove. The run-level report also remains `BLOCKED_MISSING_RECORDINGS`; no recorded-motion coverage or successful grove simulator test is claimed. The external development project was removed by its normal build cleanup, and Android project configuration and the machine OpenXR registry were preserved.
- Android build succeeded with zero errors and 19 warnings: ARM64 / Vulkan / IL2CPP / single-pass-instanced OpenXR, native JSBSim required. All 616 authored scenery payload files verified. Final APK SHA-256: `9f517f832037725add12c10c4f5da2a63e9711f1b33d4dadedc6c7dea8464897`. The previous APK was copied and hash-verified at `rollback/QuestFlight-before-connected-grove.apk` (`0e595cc93a3533ffebdebc6c2fefd41e077e27b1da20074c0fbafc10d865ac67`).
- The first APK installed successfully, and its installed SHA-256 matched. Native JSBSim and authored scene readiness passed. An unworn observation initially timed out because the headset returned to sleep. Temporary existing power automation allowed a complete dry run, with power automation restored afterward. Inspection of its device capture caught stale presentation history: the native reset had moved the simulation, but pausing before the next fixed update retained the runway visual pose. Resetting rig history alone passed the Editor regression check but failed the next physical alignment check by 319 m: Rigidbody interpolation also retained pre-reset state. The observation now temporarily disables Rigidbody interpolation, resets through native JSBSim, synchronizes transforms and invokes the rig's existing `ResetPresentationHistory()`. It checks both native-to-simulation and simulation-to-presentation error before reporting READY. The temporary interpolation override is restored on exit. This is confined to an explicitly requested paused diagnostic; ordinary flight dynamics and camera-local pose are unchanged. The final corrected APK passed both on-device observation rechecks described below.

## Prepared headset comparison

Final device evidence is in `device_ready/`: both observation JSONs, complete logcats, raw stereo device screenshots and installation verification. The unworn screenshots were inspected to verify airborne scenery and cockpit rendering after the pose correction; headset orientation differed and the baseline showed the system boundary overlay. They are not matched visual-quality acceptance or worn evidence. The successful native and presentation positions are identical across variants: approximately (-374.018, 72.840, -1029.157) in the authored world frame. Both position errors are 0 m.

Use `scripts/run_connected_grove_observation.ps1` with `-Variant baseline` then `-Variant candidate`, `-Station 0`. Look ahead/right toward the wooded strip beside the bike-park paths. Station 1 provides a closer view. Compare continuous crown coverage, depth, leaf scale, panels, stereo consistency and head-turn stability. The host removes its queued request after execution, including failures.

These paused observations test appearance only. Use the existing normal-flight temporal runner with `production` and `connected_grove_off` profiles for hardware cost; pause-state frame times cannot establish flight or thermal acceptance. Do not expand to further groves before this pilot earns visual and hardware evidence.

Storage note: automatic approval review rejected deletion of the generated Android launcher cache as “blocked by policy,” without a more specific reason. The build completed without that deletion. Only about 0.3 GiB remained after the final build, so further large builds/acquisition need space first; personal files were not removed.

## Provenance

City of Boulder / NV5 / DRCOG 2020 LiDAR, CC BY 4.0. Modified for QuestFlight; no endorsement implied. [Publisher record](https://www.arcgis.com/home/item.html?id=8fb05137a9f049afa0b60d44086b19af). Generated meshes retain a serialized source manifest with source hashes and component identity. Existing production foliage texture provenance remains unchanged. Raw source downloads, screenshots, APKs and build caches stay outside committed product assets.
