# Meta XR Simulator workflow

Meta XR Simulator is a Windows development and visual-regression tool for the standalone Quest 3 product. It is not a shipping target, PC runtime dependency, or substitute for worn-headset timing. The shipping configuration remains self-contained Android ARM64, Vulkan, and OpenXR.

## Verified local installation

The July 13, 2026 setup audit installed the current signed Windows release:

- Meta XR Simulator v201.0 at `C:\Program Files\MetaXRSimulator\v201.0`
- executable product version `201.0.0.25.633`
- installer SHA256 `34C2FADDF857FBEB57C1AE08A8906A015BF8E87F4C21B3BE2B0C74A400E94810`
- Authenticode signer `Meta Platforms, Inc.`
- Unity 6000.3.8f1 with OpenXR 1.16.1, XR Management 4.5.4, and Input System 1.18.0
- Quest 3 is the installed simulator's default device profile
- the obsolete embedded `com.meta.xr.simulator` package is absent
- the Oculus desktop runtime remains the machine's active OpenXR runtime

Meta's current [Getting Started guide](https://developers.meta.com/horizon/documentation/unity/xrsim-getting-started/) describes the standalone application, says no additional simulator package is required for an OpenXR Unity project, and marks the old Asset Store simulator path deprecated. The installed release came from Meta's [official Windows package page](https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-windows/), which listed v201.0 on the audit date.

## Safety architecture

`scripts/setup_meta_xr_simulator.ps1` audits the signed installer provenance, current 64-bit and 32-bit OpenXR runtimes, Quest 3 profile, and Unity package compatibility. QA processes request Meta's runtime with the Khronos loader's per-process `XR_RUNTIME_JSON` override. Meta XR Simulator v201's frontend can nevertheless activate its runtime in the 64-bit machine registry during startup. The runner snapshots both registry views, permits paired cleanup only when Meta's `PreviousActiveRuntime` exactly matches the pre-run value and the 32-bit view is unchanged, invokes Meta's installed `deactivate_simulator.ps1`, and then re-reads the registry. That paired deactivation requires UAC. Any declined UAC, conflicting runtime, incomplete restoration, persistent-configuration change, or shipping-configuration change fails closed as `FAIL_SIMULATOR_STATE_RESTORATION`, ahead of missing-replay classification. The [OpenXR loader specification](https://registry.khronos.org/OpenXR/specs/1.1/loader.html) defines the process override behavior; the additional snapshot/restore guard covers Meta frontend behavior observed in v201.

The Windows development player is built only from a fresh copy under the external artifact root. In that copy, the build entry point enables Standalone OpenXR, stereo rendering, Oculus Touch, and Meta Quest Touch Plus. It refuses to run against the product project. No Windows build profile or Standalone XR loader is added to the product repository, and the Android configuration is never altered.

Unity may replace static renderers' `sharedMesh` references with combined meshes and clear consumed `GameObject.isStatic` flags in a built player. The authored Editor gate therefore remains strict about the runway visual/collider sharing one mesh, exact far-terrain vertex/index topology, static terrain authoring flags, and the live renderer/triangle/material budget; it also proves that the serialized bake counts and far-terrain topology hash match the authored assets. Built-player QA uses only that verified serialized evidence for its budget and far topology, and suppresses only the consumed static flag and static-batch reference-identity false positives. Missing meshes, runway/terrain/marking/collider tolerances, serialized topology identity, temporal mesh/vertex-count identity, transforms, materials, LOD/culling behavior, and all budget ceilings remain enforced.

The runtime witness:

- reads the OpenXR-owned tracked Main Camera's left/right stereo matrices;
- renders each eye with a separate hidden diagnostic camera;
- never writes the Main Camera, XR Origin, aircraft, seat, or environment transform;
- records camera, XR Origin, aircraft, frame, timestamp, runtime, refresh rate, environment/render profile, warnings, and errors;
- uses the authored read-only mountain stability probe and environment contract;
- checks blank eyes, plausible stereo separation, cross-eye consistency, CG pivot invariants, mountain identity, runway attachment, missing components, protected-panel UI overlap, and debug-overlay presence.

The eye images are simulator-driven scene renders using the exact stereo view/projection matrices supplied by OpenXR. They are not Quest compositor captures and are labeled accordingly.

## Commands

Use the milestone's external artifact directory:

```powershell
$artifacts = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\standalone_recovery_20260713_143655"
.\scripts\setup_meta_xr_simulator.ps1 -ArtifactRoot $artifacts -AuditOnly
.\scripts\run_meta_xr_simulator_qa.ps1 -ArtifactRoot $artifacts -AuditOnly
.\scripts\run_simulator_visual_gate.ps1 -ArtifactRoot $artifacts -RebuildPlayer
```

Outputs are external and must not be committed:

```text
<artifact root>/meta_xr_simulator/
  setup/meta_xr_simulator_setup_report.json
  player/simulator_dev_build.json
  qa_run/simulator_qa_run_report.json
  qa_run/sessions/<session>/*_left.png
  qa_run/sessions/<session>/*_right.png
  qa_run/sessions/<session>/*_stereo.png
  qa_run/sessions/<session>/*_session_report.json
  qa_run/replays/*.vrs
  qa_run/logs/*
  visual_gate/simulator_visual_report.json
  visual_gate/simulator_visual_summary.md
  visual_gate/simulator_stereo_contact_sheet.png
```

The visual gate returns a failing exit code if any required recording is missing, either eye is blank, eye poses or scene output are inconsistent, the mountain identity changes, the aircraft violates its CG/reference-frame invariant, the calibration panel enters its protected instrument region, the runway/environment contract fails, missing components/errors appear, a debug/training overlay is active, or the exact OpenXR/persistent state is not restored. State-restoration failure takes precedence over a missing-recording blocker.

## One-time VRS recording set

Meta documents VRS record/replay and screenshot synchronization in [Session capture](https://developers.meta.com/horizon/documentation/unity/xrsim-session-capture/). Automated replay is supported by a standalone application, not Unity Play Mode. Do not fabricate, hand-edit, or rename unrelated VRS files.

Record each session once:

```powershell
.\scripts\run_meta_xr_simulator_qa.ps1 -ArtifactRoot $artifacts -RecordSession cockpit_neutral
```

Repeat for every identifier below. The script opens Meta XR Simulator and the development player, transactionally selects `exec_state: record`, writes `<artifact root>\meta_xr_simulator\recordings\<id>.vrs`, and restores the user's prior persistent and OpenXR configuration on exit. Stop recording in the simulator Record panel, then close the development player. A simulator UI acknowledgement and Meta's paired UAC deactivation may be unavoidable.

| Session ID | Recorded action |
|---|---|
| `cockpit_neutral` | Remain seated, neutral, looking forward for at least 8 seconds. |
| `head_pan` | Slowly pan left, right, up, and down; return forward after at least 12 seconds. |
| `rear_view` | Turn through both sides to a stable 180-degree rear view, then return forward. |
| `viewpoint_calibration` | Open the Touch-only seat menu; adjust, save, reopen, reset, and close it. |
| `control_surface_sweep` | Sweep pitch, roll, yaw, throttle, and brakes through the product input boundary. |
| `taxi` | Neutral head motion; the authored deterministic demo pilot supplies the taxi control phase. |
| `takeoff_roll` | Neutral/slight runway scan; the authored deterministic demo pilot supplies the takeoff roll. |
| `rotation_climb` | Look from panel to horizon during the authored deterministic rotation/climb. |
| `shallow_turns` | Fly one shallow left and one shallow right turn through recorded Touch/gamepad input. |
| `final_approach` | Record the full deterministic flight setup needed to be established on final; keep the recording beyond the 120-second target capture. |

The VRS replay remains authoritative for headset and controller motion. The in-app demo pilot is used only where listed and injects controls through the product input boundary; it never writes aircraft or XR transforms.

## Replay and review

After all ten VRS files exist, run:

```powershell
.\scripts\run_simulator_visual_gate.ps1 -ArtifactRoot $artifacts
```

The script replays each VRS file using Meta's documented persistent-data schema, captures both eyes at the cataloged deterministic frame, copies the replay files into the evidence directory, aggregates logs and metadata, and creates a stereo contact sheet. The runtime mountain probe samples every active frame before the target capture, so the head-pan replay checks temporal mesh/transform identity rather than only the final image.

The lead must inspect every full-resolution left-eye, right-eye, and side-by-side image. The machine panel-overlap region is intentionally conservative and does not replace image review. Meta's [Simulator overview](https://developers.meta.com/horizon/documentation/unity/unity-simulate-xrsim/) describes the supported simulated headset/controller interaction.

## Evidence boundary

Meta XR Simulator evidence may establish OpenXR API behavior, stereoscopic rendering, tracking, controller/gamepad input, deterministic VRS replay, composition, and visual regression. It must never be cited as Quest 3 GPU timing, thermal behavior, stale/tear rate, worn comfort, or headset frame-performance evidence. Those gates require the physical Quest 3 and `scripts/run_standalone_quest_gate.ps1`.
