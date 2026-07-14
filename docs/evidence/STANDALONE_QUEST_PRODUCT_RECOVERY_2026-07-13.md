# Standalone Quest Product Recovery — 2026-07-13

## Outcome

- Product decision: **CONTINUE STANDALONE QUEST**.
- Shipping target: self-contained Meta Quest 3, Android ARM64, Vulkan, OpenXR.
- Visual review: **6.5/10 overall**, with no scored category below 5/10.
- Canonical product validation: pass, including 40/40 EditMode, 9/9 PlayMode, standard Visual QA, static Quest budget, and Android build.
- Physical-headset acceptance: **not passed**. One valid unattended sample missed the strict delivered-interval/allocation gate, had no direct GPU/VrApi samples, and had no human witness. A no-recording control was then blocked by the one-time Guardian tracking dialog.

This decision does not claim Quest performance, worn comfort, controller acceptance, photorealism, training credit, or certified flight-model fidelity. The current headset evidence is not a reason to pivot to PCVR and does not establish that standalone Quest is infeasible.

## Superseded PCVR decision

The historical record in `CLEAN_PRODUCT_EXTRACTION_AND_QUEST_GATE_2026-07-12.md` is retained unchanged. Its PCVR recommendation followed a 5/10 visual score missing a self-imposed precondition for headset testing; it was not based on measured standalone hardware failure. Alex's explicit standalone requirement supersedes that product decision. PCVR, Link, Air Link, Windows shipping, remote rendering, and a required-PC runtime are not supported fallbacks. Future choices are continue standalone, narrow standalone, or pause/stop.

## Meta XR Simulator

- Installed standalone Meta XR Simulator v201.0 at `C:\Program Files\MetaXRSimulator\v201.0`.
- Executable product version: `201.0.0.25.633`; the executable itself is not Authenticode-signed.
- Official Meta installer SHA-256: `34c2faddf857fbeb57c1ae08a8906a015bf8e87f4c21b3be2b0c74a400e94810`; installer signature verified as Meta Platforms, Inc.
- Unity/OpenXR: Unity 6000.3.8f1, OpenXR 1.16.1, XR Management 4.5.4, Input System 1.18.0.
- Device/render profile: Quest 3, 72 Hz requested and reported, Single Pass Instanced, two eye views, Touch/Touch Plus profiles, 4x MSAA.
- Activation uses per-process `XR_RUNTIME_JSON`; the Oculus machine OpenXR runtime and Meta persistent configuration were restored. Android shipping configuration hashes were unchanged.
- The obsolete embedded Meta simulator package was not added.

The live simulator smoke session passed 14 machine checks and produced coherent, nonblank, distinct left/right 1440×1440 images, a 2880×1440 stereo pair, and a contact sheet. The mountain probe sampled 288 frames / 1,152 renderer samples with zero identity, transform, material, renderer-set, LOD, or fallback violations. The lead inspected the full-resolution images.

The aggregate simulator gate remains `BLOCKED_MISSING_RECORDED_REPLAYS` with fail-closed exit code 2. The ten required genuine VRS files are absent: `cockpit_neutral`, `head_pan`, `rear_view`, `viewpoint_calibration`, `control_surface_sweep`, `taxi`, `takeoff_roll`, `rotation_climb`, `shallow_turns`, and `final_approach`. The installed v201 workflow exposes recording through the simulator UI; no recordings were synthesized, renamed, or represented as complete.

Primary external evidence:

- `meta_xr_simulator/setup/meta_xr_simulator_setup_report.json`
- `meta_xr_simulator/qa_run/simulator_qa_run_report.json`
- `meta_xr_simulator/visual_gate/simulator_visual_report.json`
- `meta_xr_simulator/visual_gate/simulator_visual_summary.md`
- `meta_xr_simulator/visual_gate/simulator_stereo_contact_sheet.png`

## Orthophoto environment

Two bounded public-domain natural-color derivatives came from the official USGS NAIP Plus ImageServer, cataloged as USDA-FSA APFO imagery acquired 2019-09-19 at 0.6 m source resolution. No Google-derived imagery was used. Raw exports, receipts, rejected attempts, and processing caches remain external.

| Zone | Runtime derivative | Coverage / resolution | SHA-256 |
| --- | --- | --- | --- |
| Near | `KBDU_NAIP_2019_Near4km_4096.jpg` | 4 km × 4 km; 4096²; 0.9765625 m/pixel | `446e5c7fa13fc653de1f3e8404829c0e54b7e919fe99ebfba79d747a0b1dadea` |
| Mid | `KBDU_NAIP_2019_Mid12km_2048.jpg` | 12 km × 12 km; 2048²; 5.859375 m/pixel | `d75316cb03a8ddcdc6d326186f9fc5e936d19d90ffc4e42ffe627888aa8593cd` |

Both use clamp wrapping, trilinear mip filtering, streaming mipmaps, anisotropy, and Android ASTC 6×6. Their estimated combined ASTC mip chain is 12,459,248 bytes. The near/mid blend is color-matched and retains world-space CC0 microdetail. A measured 20 m mismatch between the airport patch and near ring was closed with one authored transition mesh (644 vertices / 640 triangles) without changing source terrain meshes, adding runtime repair, or adding a material.

## Mountains

Option A was selected: one immutable 24 km USGS-derived far mesh on a stable 100 m render grid, with offline normals and sealed boundaries. It contains 89,280 triangles, uses no runtime LOD, crossfade, dithering, billboard, camera-facing, camera-relative scale, or duplicate legacy source. The grid is deterministic bilinear densification of the frozen 400 m USGS sample set and does not claim added elevation detail.

Option B, a same-source 360-degree panorama, had lower render cost but visibly flattened stereo/parallax during head translation. It remains external comparison evidence only. Simulator and physical-Quest mountain probes both found immutable mesh/transform/renderer identity with zero violations; this does not replace worn silhouette review.

## Airport, water, vegetation, and cockpit

- Runway: project-authored albedo/metallic-smoothness textures, bounded wear and joints, existing aligned markings, shared visual/collider mesh, 4.676 mm measured terrain gap, 1.0 mm marking gap, zero collider disagreement.
- Airport: project-owned Blender source and generated modular kit with three hangars, fuel/service structure, fencing/signage, and ramp props; three LODs and shared instancing-compatible material.
- Water: only Boulder Reservoir remains; stable closed shoreline/bank, opaque shared Quest shader, bounded Fresnel/specular/normal response, no SSR/refraction/transparent overdraw.
- Vegetation: sparse project-authored cottonwoods and bounded airport clusters with shared material/LOD; no grass carpet or material explosion.
- Cockpit: 14 explicit material instances derived from the licensed aircraft, bounded AO/roughness/exposure/readability tuning, stable reflections, zero remaining realtime shadow casters/receivers, no SSAO or realtime cockpit shadow map. Aircraft geometry, control animation, seat calibration, CG, and reference-frame architecture were preserved.

## Lead visual review

The lead inspected all 16 final 1280×720 Visual QA images at full resolution, the standard contact sheet, the live simulator left/right eyes, the stereo pair/contact sheet, and the valid physical-Quest stereo screenshot. Scores are subjective iteration guidance, not release permission.

| Category | Before | After | Review note |
| --- | ---: | ---: | --- |
| Viewpoint | 6.0 | 6.5 | Panel and outside view are usable; worn seat confirmation remains. |
| Cockpit | 5.0 | 6.0 | Instruments are clearer and stable; geometry remains dark/simple. |
| Runway | 4.0 | 5.0 | Alignment/markings improved; surface remains visibly stylized. |
| Airport | not scored | 5.0 | Coherent modular context; intentionally low-poly/gray. |
| Ground | 5.0 | 7.0 | Unique NAIP macro imagery is the largest improvement; repeated strips and boundary bands are absent. |
| Mountains | 4.0 | 5.5 | Stable real-data silhouette; distant shading remains simplified. |
| Water | 4.0 | 5.0 | Stable shoreline and response; reservoir still reads flat/stylized. |
| Vegetation | not scored | 5.0 | Sparse and purposeful, not dense. |
| Aircraft exterior | 7.0 | 7.5 | Strongest visual asset; preserved. |
| Animation | 6.0 | 6.5 | Control sweep remains visible and coherent. |
| **Overall** | **5.0** | **6.5** | Target reached with a bounded standalone-Quest presentation. |

## Static Quest planning budget

The corrected audit counts non-null material/submesh slots and skinned geometry. These are planning estimates, not GPU timing.

| Metric | Result | Target |
| --- | ---: | ---: |
| Maximum estimated visible draw calls | 57 | ≤ 180 |
| Maximum estimated visible triangles | 388,870 | ≤ 700,000 |
| Scene mesh triangles | 454,889 | informational |
| Visible/scene materials | 19 | ≤ 40 |
| Active renderers | 94 | informational |
| Estimated texture bytes | 157,555,880 | planning estimate |
| Transparent material slots | 1 | controlled |
| Shaders / keyword combinations | 7 / 14 | controlled |
| LOD groups / crossfades | 14 / 0 | no crossfade |
| MSAA | 4x | required default |

Android configuration is ARM64-only, Vulkan-only, IL2CPP, ASTC, OpenXR Single Pass Instanced, mip streaming, and multithreaded rendering. Three qualitative overdraw captures passed the static audit.

## Native JSBSim promotion gate

The frozen R&D repository remained clean at `cd4131fa7732a6db2d7387921dfef33e2c206970`. The go-around schedule was corrected without lowering the 75 ft recovery threshold.

- 11/11 total and 10/10 critical scenarios passed.
- Go-around second-half altitude recovery: 78.968717 ft.
- No non-finite state; zero steady-state managed allocations.
- Windows average timing: 0.026804 ms; worst scenario p95: 0.1037 ms.
- Android ARM64 package build passed; required ELF LOAD alignment is 0x4000.
- Source/license manifest: 18/18 entries, SHA-256 `4b5ea046f70fb1b51ba762f1d7fc3dd4365354686287b59d7600f71434340d21`.

The bounded package is `Packages/com.alexoviedo.questflight.jsbsim`. Release builds always resolve to `UnityPrototype`; native JSBSim is available only in the Editor/development builds and falls back to the prototype on failure. It cannot become the release default until physical Quest timing and handling are accepted.

Post-gate package review found that the frozen upstream C172 definition retains an active 10 Hz `JSBout172B.csv` diagnostic output; the promotion run produced only an ignored, header-only file. This does not change the recorded software-gate result or the authoritative release backend, but native release authority additionally requires disabling or explicitly redirecting that output and rerunning all 11/11, allocation, timing, Android, alignment, and manifest checks on the changed payload.

## APK and packaging audit

The final delivery APK is 74,417,736 bytes with SHA-256 `b7fccd8dfccb7a3e2b7f6b6d8ad7fbbd024d96401eacace6c78b7ccb438f7264`. It is non-debuggable, target SDK 36 / minimum SDK 29, and contains only `arm64-v8a`. All 11 ELF64 AArch64 shared libraries pass 16 KiB PT_LOAD alignment and offset/address congruence. Both JSBSim libraries match the promoted package, all 14 runtime-data files match exactly, and all 13 bounded runtime-manifest references are present. No Windows/x86 native entry exists.

The physical headset sample below installed the immediately preceding validated build: 74,417,728 bytes, SHA-256 `4c04fe7e5474a5c937cc2695b4a0c878de6624130c42df27594a9fe59e8c1fce`. Hardware measurements are attributed only to that exact APK; the final rebuild is not represented as a second headset run.

APK Signature Scheme v2 integrity passes, but the certificate is Android Debug. This APK is suitable for local headset gating, not store distribution; production signing remains a release task.

## Physical Quest 3 gate

Authorized device `2G0YC5ZG8907TD` installed and launched the APK. A valid unattended capture completed the 90-second warm-up and 60.683-second measurement with 4,322 frames:

- mean delivered interval: 13.8886 ms;
- p95/p99 delivered interval: 14.4589 / 14.6360 ms;
- strict over-13.8889 ms ratio: 54.33%;
- runtime planning estimate: 38 visible draw slots, 385,747 visible triangles, 20 materials;
- physical mountain probe: 4,321 frames / 17,284 renderer samples, zero violations;
- no app crash, ANR, OOM, Unity exception, or thermal warning after correcting two same-log parser false positives; thermal service status was 0;
- no direct Quest GPU duration, app-PID VrApi rows, stale/tear series, or valid release allocation recorder was available;
- human status: `NOT_RUN_NO_USER_AVAILABLE`.

The strict machine gate remains failed: p95 and over-budget ratio missed their thresholds, zero steady-state allocation was unproven because the release profiler recorder was unavailable, and no direct GPU/VrApi evidence identified a bottleneck. Screen recording was active, so an unrecorded control was attempted. That control was blocked before product readiness by Horizon's stereo `Finding position in room` / `GUARDIAN_TRACKING_LOST_CONTINUE` dialog. The dialog offers `Continue without tracking` or travel mode and requires Alex to make the one-time in-headset choice; it was not automated.

Primary external evidence:

- `headset_gate_pass2/temporal_gate/quest_temporal_visual_gate_summary.json`
- `headset_gate_pass2/temporal_gate/adb_screenshot_final.png`
- `headset_gate_pass2/temporal_gate/pulled_device_evidence/`
- `headset_gate_pass2_reparsed/quest_temporal_visual_gate_summary.json`
- `headset_gate_control_no_recording/temporal_gate/adb_screenshot_final.png`
- `headset_gate_control_reparsed/quest_temporal_visual_gate_summary.json`

## Validation summary

- Product dependency closure: 99 dependencies = 94 approved product assets + 5 Unity packages; 0 legacy, 0 unknown.
- Standalone gate policy tests: 6/6.
- Extraction tool tests: 9/9.
- KBDU source/pipeline tests: 8/8; orthophoto pipeline tests: 7/7.
- Unity EditMode: 40/40 across all 9 discovered fixtures.
- Unity PlayMode: 9/9 across all 3 discovered fixtures.
- Standard Visual QA: 16/16 machine checks passed; full-resolution lead review completed.
- Static Quest budget: passed planning thresholds.
- Android build and independent APK/ELF audit: passed, subject to the debug-signing caveat.
- Meta XR Simulator live smoke: machine pass; ten-session replay aggregate blocked as described above.

The canonical final gate is archived under external artifact `final_validation_pass3`; the independent final APK report is under `workstream_c/final_apk_audit_pass3`.

## Decision and next milestone

**CONTINUE STANDALONE QUEST.** Architecture, visual quality, static budgets, release build, native packaging, and one physical stereo launch remain credible. The single valid performance sample fails acceptance but does not identify a standalone-implausible CPU/GPU bottleneck; reducing visual scope without direct evidence would be premature.

The exact next standalone milestone is: Alex selects **Continue without tracking** (or establishes a valid stationary boundary) once in the headset, wakes both Touch controllers, confirms the cockpit, and reruns the unrecorded physical gate with direct app-PID GPU/stale/tear and allocation instrumentation. The same worn session must review seat/viewpoint, controller operation, comfort, and native-JSBSim development handling. If repeatable direct hardware evidence misses 72 Hz, narrow scenery/vegetation/water/cockpit effects based on the measured bottleneck; do not pivot to PCVR.
