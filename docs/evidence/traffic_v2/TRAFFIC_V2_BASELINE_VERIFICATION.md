# Traffic V2 Phase-2 baseline verification

- Audit date: 2026-08-21 MDT / 2026-08-22 UTC
- Validation owner: Workstream D
- Accepted product source: `fda11a25eb526824d82a47330f2be80b1410bb64`
- External evidence root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_system_v2_20260821_170722\baseline`

## Verdict

**PASS for the Phase-2/G1 baseline-capture and budget-freeze requirement.** D recommends closing G1 against the exact hashes in this report. The recommendation preserves, rather than waives, the accepted product's six pre-existing 77-draw-ceiling failures.

**No product, Traffic V2, stereo, or Quest 3 acceptance is granted.** Standard Visual QA remains `FAIL_MACHINE_QA`; physical-headset and stereo acceptance remain `NOT RUN`; the frozen specification remains 0/23 evidence passes.

| Phase-2 item | Result | Verified boundary |
| --- | --- | --- |
| Exact accepted source/spec/tool | **PASS** | HEAD is `fda11a25...`; spec is frozen SHA-256 `8e1b3f74...`; runner is SHA-256 `b74bc945...` (76,356 bytes). |
| Frozen traffic captures | **PASS** | All 15 specification view IDs occur once, in exact order; all 15 PNGs decode at 1280x720; no capture/reflection/projection/budget errors; contact sheet is 1600x540. |
| Runner artifact integrity | **PASS** | All 19 indexed files exist and independently match recorded byte lengths and SHA-256 values; the index covers 15 PNGs plus JSON, review, contact sheet, and fleet CSV. |
| Scene/camera/source invariants | **PASS** | Exactly one serialized tagged Main Camera and one `KbduTrafficRuntime` were resolved. Runner source only reads Main Camera state and writes a hidden `HideAndDontSave` diagnostic camera. Scene SHA-256 before/after/current is `9d42816933bb7dccc9986abf4be73cbc8239ccf52bf597b6b1a410bd6928c3c9`; no scene save occurred. |
| Fleet-by-camera evidence | **PASS** | CSV has exactly 15 ordered rows; every parked/moving total and all six per-archetype columns match the JSON's independent >=2x2-pixel geometric counts. |
| Procedural render accounting | **PASS** | Every per-batch triangle multiplication, nonempty-batch draw count, material addition, and static-plus-traffic sum recomputes exactly. |
| Editor CPU/allocation/memory capture | **PASS (planning evidence only)** | Required warmups/sample counts, distributions, host identity, allocation scope, Stopwatch frequency, memory-object inventory, and 17 raw-tick hashes are present and internally consistent. This is not Quest evidence. |
| Static-budget output capture | **PASS (capture only)** | Static reports are present and hash-bound below. The default-view static audit passes its own limits, but it omits procedural traffic and is not the aggregate baseline. |
| Standard Visual QA capture completeness | **PASS** | 123/123 report rows, 123 distinct nonzero PNGs, and 123/123 analyzer checks are nonblank; no giant UI overlay is reported. |
| Standard Visual QA acceptance | **FAIL** | Six accepted-product views exceed the existing 77 estimated-draw ceiling. There are no other shot warnings/errors; the image analyzer's sole error is the corresponding overall Unity-report failure. |
| Exact baseline APK capture | **PASS (size/buildability only)** | Immutable external APK independently matches 223,115,247 bytes and SHA-256 `3d39d066...`; final build result is succeeded with 0 errors. Packaging/signing limitations are below. |
| Stereo simulator / worn visual acceptance | **NOT RUN** | Phase 2 contains monoscopic diagnostic renders only. |
| Quest 3 frame, thermal, allocation, and 72 Hz acceptance | **NOT RUN** | No APK install/launch or physical-headset run was performed. |

## Frozen authority and artifacts

The authoritative specification is `tools/traffic_v2/schemas/traffic_system_v2_spec.json`, revision `2026-08-21.1`, 33,411 bytes, SHA-256 `8e1b3f7442652128b306a0706d3eac02b59750158839ca0443351312fcbf09c7`. It contains exactly 23 unique requirements and these exact 15 views:

`cockpit_runway_takeoff`, `left_airport_glider_road`, `airport_boulevard`, `diagonal_highway_approach`, `highway_flyover`, `reservoir`, `downtown`, `residential`, `industrial`, `map_periphery`, `agl_300m_oblique`, `agl_800m_aerial`, `head_pan_90deg`, `rearward_glance`, `aircraft_turn_180deg`.

The frozen budget is `tools/traffic_v2/schemas/traffic_v2_budget_v1.json`, 9,825 bytes, SHA-256 `5efb9c82ff38850a0514b732df4730697e1e9c018048205f9841c833669acc92`. Every artifact byte length and hash cited by its `baseline_evidence` object independently matches disk.

| External artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `traffic_v2_baseline.json` | 31,747,408 | `185908c0becf5491fa2dedefdc26ed799068c57faacd7882a1cd46024a5b1016` |
| `traffic_v2_baseline_hashes.json` | 3,901 | `a80a3c101e1937e240fdcf503f091e189593fe9072d55a08c90c42c81739c7a6` |
| `traffic_v2_baseline_review.md` | 5,081 | `6f7d04459217fa15a4e9b8752309c6cb4eb9ffd09a7e2dc4820b63657278c49b` |
| `traffic_v2_baseline_fleet_by_camera.csv` | 1,614 | `cd0435a9cec211507e60ede96f5beedd450212080e59cc92626e6610450e0f59` |
| `traffic_v2_baseline_contact_sheet.png` | 1,470,446 | `ec43813010e6b1e471be989941367929a4432d805499618832f30a9aa2f30600` |
| `unity_traffic_v2_baseline.log` | 44,311 | `401737f4173cc5ddcf4992365729445bf3ab35dac1ce44142739858a8a58ce17` |
| `static_budget/performance_budget_report.json` | 3,638 | `4616461e26609404f7ae1dde45123da5be1fbf3dde63441f87552c791ce40cde` |
| `static_budget/material_shader_report.json` | 19,145 | `ee1f69e6a5ea45b32d9652292d64f9ef59d9dc75dffcb3555e798201b97478ba` |
| `static_budget/texture_memory_report.json` | 56,565 | `d1c430731fe717c3b874ccc718737b9993682c665b91c9f813ca1c67665b6896` |
| `static_budget/lod_culling_report.json` | 22,162 | `502013a16dc152c4c7cb82215e93697a744e25478f0c0068cd683c7b7a1c95f1` |
| `standard_visual_qa/visual_qa_report.json` | 184,456 | `259b69631ed7c8ebdeab08f0b082e2ab1fc8f8d8b895a8cde941880c4a61f870` |
| `standard_visual_qa/visual_qa_analysis.json` | 56,004 | `76950c03ce130784e1cd41012bd58361f779fa522280bec88b551407da0d72fd` |
| `build/QuestFlight-production-fda11a2.apk` | 223,115,247 | `3d39d066bf2a8f6d487e6457b6cea6e6725e3d7de59bb3107bfcce07f70bbeec` |
| `build/questflight_build_evidence.json` | 991 | `b67fbb6b3ea3410150bc4667726515c98340752ee8aebe141d0d7c4910f7d46d` |

The runner index intentionally excludes itself and does not index its Unity log. The budget file binds the primary static, Visual QA, and APK evidence; this D report separately binds the omitted index/log and supporting reports. Preserve the entire external root read-only.

## Recomputed baseline and frozen ceilings

The accepted V1 manifest independently hashes to `3b8ac16873bdc4c2a436f6f9751c8466e97d4b07eb171ddaa664593347f19705` at 8,577,943 bytes and contains 24,347 parked vehicles, 788 moving vehicles, and 361 routes. LOD thresholds are near 800/1000 m and far 4200/4500 m; capacities are 64 near and 1,800 far.

| Cost | Accepted observation | Frozen candidate ceiling |
| --- | ---: | ---: |
| Traffic draws / triangles / distinct opaque materials | 9 / 123,936 / 1 observed; 9 / 129,312 / 1 authored | 9 / 129,312 / 1 |
| Aggregate frozen-view draws / triangles / visible materials | 112 / 798,068 / 38 observed | 114 / 804,524 / 38 |
| Active closure materials / shaders / keyword combinations / textures | 39 / 23 / 28 / 78 | 39 / 23 / 28 / 78 |
| Estimated texture bytes / transparent slots | 296,332,790 / 18 | 296,332,790 / 18 |
| Serialized payload bytes | 8,577,943 | 8,577,943 maximum; 4,194,304 design target |
| Enumerated traffic asset Editor runtime / disk bytes | 9,347,428 / 9,373,115 | 9,347,428 / 9,373,115 |
| APK bytes | 223,115,247 | 224,163,823 (+1,048,576) |

All 15 selected-traffic snapshots account correctly. The maximum frozen-render far batch is 907 (`industrial`) and no captured batch exceeds 1,023. The timed preparation loop separately observed 906; these are different sampling scopes, not a contradiction. Accepted V1 can nevertheless place up to 1,800 instances in one proxy batch and does not chunk or fail closed at Unity's 1,023-instance limit; that remains a recorded baseline defect.

Static planning outputs report 38 materials, 22 shaders, 27 material-keyword combinations, 77 textures, 296,322,278 estimated texture bytes, 18 transparent slots, 122 active renderers, 39 LOD groups, zero crossfade groups, and zero custom cullers. The default-camera report passes at 63 draws and 598,228 triangles against 77 and 692,871, but explicitly says no standard Visual QA report was supplied and omits procedural traffic. Therefore candidate comparison must use the aggregate accounting above, while the legacy 77-draw static threshold remains unchanged.

## Editor-only timing and memory

On the recorded Windows Editor host (`Stopwatch.Frequency=10,000,000`, i7-13620H, 16 logical cores, 16,087 MiB RAM, RTX 4060 Laptop GPU), the 0.1 s simulation delegate measured 100 warmups plus 1,000 samples: mean/p50/p95/max `219.5879/215.8/249.5/309.2 us`, with 0 current-thread allocated bytes. Visibility preparation measured 100 warmups plus 225 samples across all frozen poses: `6046.8231/6707.7/7609.4/10680.9 us`, with 0 current-thread allocated bytes. All 15 poses have 15 samples; `residential` is the reconciled worst p95 at 10,680.9 us.

All 17 timing hashes are distinct, lowercase 64-hex SHA-256 values and use the declared signed-int64 little-endian Stopwatch-tick encoding. The simulation hash is `f4dfd6a1fcd6d0b01a28977e721e2f889e4b4fb777c875baab193c6ad69112b3`; aggregate visibility is `db5df19217c56dbefcaf37a6c35e3d380597b3b0e6cc002ff52f5abb76b5f7fb`. **Limitation:** raw tick arrays are not emitted, so D can verify the hash fields and summary consistency but cannot independently recompute those hashes or percentiles from a sidecar. Per-view allocation is deliberately `-1`/not separately measured; only aggregate current-thread allocation is 0. These values are same-host Editor regression guards, never Quest/runtime claims.

## Standard Visual QA failure retained

The Visual QA report contains 117 passing and six failing shots. All six failures have exactly one warning, and every warning is the unchanged 77-draw ceiling:

| View | Estimated draws |
| --- | ---: |
| `04b_cockpit_engine_cowl` | 78 |
| `12_external_control_sweep` | 85 |
| `16_approach` | 87 |
| `cloud_01_ground_scattered` | 79 |
| `pasv1_18_cockpit_side_window` | 82 |
| `pasv1_27_cockpit_shadow_heading_090` | 78 |

The report has zero top-level errors; the independent image analyzer has exactly one error, `Unity visual QA report marked overall pass=false`. Thus capture completeness passes while product machine-QA acceptance fails. Phase 2 does not waive or reclassify this accepted-baseline defect.

## APK and platform boundary

The immutable external APK is `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_system_v2_20260821_170722\baseline\build\QuestFlight-production-fda11a2.apk`. Its hash and length match both the live build output and the frozen budget. Build evidence records a non-development build, ARM64, Vulkan-only, IL2CPP, OpenXR single-pass instanced, Linear color, 4x MSAA, package `com.alexoviedo.t2.questflight`, native JSBSim required, and UnityPrototype forbidden. Independent `aapt` inspection confirms package version `0.1.0-alpha.1`/code 1 and native code `arm64-v8a`.

This is not a distribution-signed release: `customSigning=false`, and independent APK signature verification identifies one RSA-2048 `C=US, O=Android, CN=Android Debug` signer using APK Signature Scheme v2 only. Build evidence also records texture compression as `Generic`; the static project's ASTC-target flag does not prove this APK used an ASTC build subtarget. The successful build reports 15 warnings and 0 errors. No installation, launch, device residency, frame timing, thermals, stereo presentation, or worn inspection occurred, so the APK establishes only reproducible size, package shape, and buildability.

## Source-state and evidence limitations

- The tracked product content matches accepted HEAD `fda11a25...`; `git diff` and the index are content-clean. Phase-2 spec, budget, contract, runner, and this report are untracked overlays at audit time and are bound by the explicit SHA-256 values above. A stat-dirty cockpit render-texture path has the same Git blob as HEAD, so it is not a content change.
- The build manifests do not embed the Git commit or the immutable external-copy path; their original APK path points into ignored `QuestFlight/Builds`. This report pairs the observed accepted HEAD with the byte-identical external copy. Future evidence should put commit, dirty-state result, external relative path, and all producer commands directly in one run manifest.
- The runner's monoscopic diagnostic camera does not establish per-eye agreement, occlusion-correct visible counts, or headset quality. Its >=2x2 counts are geometric frustum candidates at manifest initial moving positions.
- Runtime memory is `Profiler.GetRuntimeMemorySizeLong` over 11 unique loaded Editor objects/assets, not Android resident/peak decoded memory. Disk bytes are imported source-asset bytes, not APK contribution.
- No PlayMode CPU profile, runtime frame trace, Meta XR Simulator stereo replay, APK install/launch, or physical Quest 3 run was attempted in Phase 2.
- Traffic V1 remains airport-centered, one-camera LOD, per-route looping, and vulnerable to an unchunked >1,023 proxy batch. Those observations are baseline defects, not Traffic V2 evidence.

## Frozen requirement state

The authoritative 23 categories are: `TV2-COV-001` full-map eligible-road coverage; `TV2-LANE-002` directed lanes; `TV2-LANE-003` divided carriageways; `TV2-GEO-004` road containment; `TV2-GEO-005` exclusions; `TV2-GEO-006` bridge/grade separation; `TV2-TURN-007` legal turns; `TV2-INT-008` right-of-way; `TV2-DEM-009` density; `TV2-FLT-010` fleet; `TV2-DYN-011` speed; `TV2-DYN-012` headway; `TV2-COL-013/014/015` same-route, cross-route, and overlap/phasing safety; `TV2-ID-016` identity; `TV2-LOOP-017` loop continuity; `TV2-LOD-018/019` head-motion and per-eye LOD; `TV2-CAP-020` parked/moving capacity; `TV2-VIS-021` pilot-camera visibility; `TV2-PERF-022` Quest 3 performance; and `TV2-REG-023` regression.

Every one remains **NOT RUN at G1**. G1 may close only as the accepted-baseline capture/budget gate, with these exact artifacts and ceilings preserved; it must not be cited as a pass for any of the 23 requirements or for product, stereo, or headset acceptance.
