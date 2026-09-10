# GPU shader candidate: implementation and validation

> Execution authority: [the revised unified remaster plan](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md) supersedes any next-step ordering or intermediate headset requests below. The revised sequence is now explicitly approved. This document remains historical evidence.

**Physical follow-up:** the user completed an A/B/A headset comparison and reported **no new visual defects** in the candidate. Candidate mean delivered interval was 16.657 ms versus baseline 17.078/17.363 ms; all runs still failed performance acceptance. The timing result is preliminary, with one candidate run and differing head views. The baseline was restored after testing. [Full worn comparison and limitations](GPU_WORN_COMPARISON_2026-09-07.md). The preparation history below predates that test.

The user approved the [GPU reduction plan](GPU_COST_REDUCTION_PLAN_2026-09-07.md). The first candidate removes unused color sampling and repeated cloud coordinate transformations. **No Quest speedup or complete visual/performance acceptance is claimed.** The reference installed APK remains `e04a5ef0fa9f62ed54c976d27cc512c312686bb272d93a2fb26df956aefb68c5` until the physical A/B sequence is ready.

## Implemented

- `ProductionSemanticSurface.shader`: sample orthophoto color only in the building branch; canopy already supplies its own top color. Avoid sampling the near image outside its blend region while retaining a single recovery sampling path. Preserve the accepted far-canopy UV repair.
- `KBDU_TerrainPyramid.shader`: evaluate either the aircraft-centered clipmap color or the fallback near/mid/far color. Previously the fallback color was evaluated before clipmap mode unconditionally replaced it. Keep clipmap global fallback, gradients, page blending, water, clean plates and canopy integration.
- `BoulderCloudShell.shader`: use the existing local ray origin and direction for each march sample, avoiding repeated world-to-local matrix arithmetic and diameter division. Keep the same sample distances, step count, density atlas, early termination and lighting. Floating-point reassociation is subject to visual comparison.
- `GpuShaderAuditRunner.cs`: Editor-only Android/Vulkan compiler export, including vertex/fragment and fog/no-fog variants, plus an entry point for the existing settled and standard visual QA. It does not save product assets. Unity generated its paired `.meta` file.

No resolution, MSAA, foveation, alpha cutoff, texture, geometry, population, visibility distance, flight physics or product hierarchy changes were made in this slice. The additional canopy-selection prototype remains off.

## Compiler evidence and rejected work

Exported actual Unity 6000.3.8f1 Android/Vulkan variants using `ShaderData.Pass.CompileVariant`, with `DIRECTIONAL`, `INSTANCING_ON`, `STEREO_INSTANCING_ON`, and a separate `FOG_LINEAR` variant. Both stages compile successfully. Preserved source copies and raw SPIR-V outside Git, then inspected disassembly and an additional NDK `spirv-opt -O` normalization. This is compiler evidence, not Adreno machine-code timing or a device frame profile.

The baseline fragment code retains fallback sampling before clipmap replacement and orthophoto sampling before canopy selection. It also retains the repeated cloud coordinate transformation inside the loop. In contrast, the baseline compiler already places foliage-detail sampling in the non-card branch and water ripple arithmetic inside the water-reflection branch. Those two proposed rewrites were therefore omitted.

An initial orthophoto early-return experiment duplicated the recovery sampling code. It was replaced with conditional near sampling and one recovery path before the final build.

| Clear fragment variant | Baseline normalized SPIR-V bytes | Candidate bytes | Interpretation |
|---|---:|---:|---|
| Semantic surface | 59,068 | 58,880 | Unused orthophoto work is excluded from canopy execution; recovery code is not duplicated. |
| Terrain | 40,504 | 40,748 | Slightly larger static control flow; fallback and clipmap sampling are now mutually exclusive. Static size is not execution cost. |
| Cloud | 11,696 | 11,456 | Smaller march-loop coordinate calculation; density sampling remains unchanged. |

Static texture-instruction counts remain 28/20/2 respectively; branch exclusion changes which instructions execute, not necessarily how many appear in the program. No conversion of these counts to GPU milliseconds is valid. Generic SPIR-V optimization is not a substitute for the device driver or hardware measurement.

The exact-zero cloud-density skip remains unimplemented: it adds a per-step branch for narrow vertical regions and has no measured benefit yet. Conservative atlas empty-space acceleration remains separately gated as planned.

[Compiler comparison](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/final_compiler_comparison.json) · [baseline exports](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/baseline_compiled/) · [candidate exports](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/final_compiled/).

## Offline validation

**50/50 focused tests pass**, covering far-canopy rendering/LOD, cloud contracts, semantic surfaces, water contracts and render-budget audit behavior. All 12 requested candidate shader variants compile. Existing recovery-function compiler warnings were already present in the baseline; no new shader compilation failure occurred.

Standard QA renders all 130 views and remains **128/130**, with the existing 79/82 draw estimates against 77 and the existing 39-material warning against 38. No limit was raised. The six settled benchmark captures pass and are **pixel-identical to the reference in both eyes at all three heights**. Both 300 m eyes were inspected, along with cloud and rearward horizon views.

The four dedicated cloud views (ground, volume, sunward and inside) were inspected and differ in 11/10/63/99 pixels, with maximum channel differences of 1/1/1/2 respectively. This supports very close offline appearance for those views; it does not establish worn temporal equivalence. Broader captures have larger differences: rearward horizon has 4,071 pixels over two code values, and the flight-mode menu has 1,375. The horizon pair was inspected without an obvious broad visual loss.

A same-code repeat confirms variability in standard QA: 108/130 images are identical; the menu changes in 1,431 pixels over two codes and the horizon changes in 1,239. The horizon differences occupy the same right-hand tree region (x=885..1279), not the sky. Standard shots use live tree selection/transition time unless `QFL_SETTLED_SCENERY_PREVIEW` is enabled; the settled benchmark freezes that state and is identical. This establishes a repeatability limitation, not attribution of every changed pixel or exact full-scene equivalence. No runtime tree behavior was changed to hide it. [Same-code comparison](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/repeatability_comparison.json).

The settled Editor views use fallback aerial imagery. They do not validate Android clipmap residency or the enabled clipmap branch visually. The runtime controller enables that branch after complete initial windows are resident; physical validation of that path remains required.

[Test results](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/shader_regression_tests.xml) · [standard QA](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/final_qa/standard_qa/visual_qa_report.json) · [image differences](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/image_comparison.json).

## Remaining acceptance

Android ARM64/Vulkan/OpenXR build **passed**, including all **616** payload checks. Candidate APK SHA-256 **`b69fa41aa1dffeafb57ad90d35942640a1d8d7a8831e69669544002e7cc84fd0`**, **1,514,040,395 bytes**. The preserved evidence copy has the same verified checksum. [Build manifest](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/build/product_apk_manifest.json) · [candidate APK, local and uncommitted](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/build/QuestFlight-gpu-candidate.apk).

Physical Quest baseline/candidate timing, representative feature-cost ranking, worn temporal image inspection and thermal acceptance remain pending. The connected Quest was asleep during preparation; no new unworn timing window is being presented as representative evidence. The baseline recorder already has frame-timing statistics enabled but reports zero GPU-duration samples; VrApi direct GPU duration is also unavailable in the reference evidence. The accepted APK remains installed for the first baseline measurement; the candidate is built but not deployed. No device power/foveation configuration was changed.

The fresh Meta XR Simulator audit reports **BLOCKED_MISSING_RECORDINGS** for all ten required sessions. No simulator replay pass or new simulator stereo capture is claimed. The installed simulator is present; the missing recordings are the prerequisite. Shipping Android configuration and simulator persistent configuration remain unchanged/restored by the audit.

[Simulator audit](../../T2-QuestFlight-current-evidence/gpu_reduction_20260907/meta_xr_simulator/qa_run/simulator_qa_run_report.json).

**Approved next step:** retain this shader candidate and proceed with the bounded Valmont visual milestone under existing resource ceilings. The completed A/B/A and positive user feedback are recorded in the linked physical report; earlier pending-test statements above describe preparation-time state. Defer another optimization-only headset cycle. Validate the combined visual milestone before broad rollout; no shader saving is booked as additional art budget. The existing frame/GC, draw/material and simulator replay gates remain open.
