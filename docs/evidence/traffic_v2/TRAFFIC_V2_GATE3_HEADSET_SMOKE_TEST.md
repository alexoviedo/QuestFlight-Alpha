# Traffic V2 Gate 3 — Quest 3 Development Smoke Test

- Date: 2026-08-24
- Candidate: recovery worktree `feature/traffic-system-v2`, base `fda11a25eb526824d82a47330f2be80b1410bb64`
- Status: `PASS_INSTALL_AND_LAUNCH_AWAITING_HUMAN_HEADSET_INSPECTION`
- Scope: developmental smoke test of the Gate 2 Traffic V2 candidate. This is not full-map traffic, visual acceptance, performance acceptance, or release acceptance.

## Focused Traffic Visual QA

The diagnostic runner captured six 1280x720 images in the authored production scene: two times each for airport, divided-highway, and residential trajectories. The images were inspected at full resolution. The selected vehicle was visible on the intended corridor, aligned acceptably to the sampled production surface, oriented with travel, and displaced between the two fixed times.

| Area | Displacement in 2 s | Mean absolute RGB difference | Result |
| --- | ---: | ---: | --- |
| Airport | 25.584 m | 0.014881 | PASS focused inspection |
| Divided highway | 32.401 m | 0.002231 | PASS focused inspection |
| Residential | 14.644 m | 0.002364 | PASS focused inspection |

- JSON: `gate3_focused_visual_qa/traffic_v2_gate3_visual_qa.json`, 4,884 bytes, SHA-256 `7279c123235e2f9a5733806d82113d9a84b0e7167f47d7f2430cada2216c8cf9`
- Traffic manifest SHA-256: `dd687bc7de331cefc439f755de643a79bad6e54ecb2772a71efefbdddde3a469`
- Artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_system_v2_20260821_170722\gate3_focused_visual_qa`
- Observed limitation: the airport pair also showed a vegetation initialization/pop-in difference unrelated to the moving car. Very-close diagnostic views expose the existing low-resolution terrain/orthophoto presentation.

## Standard Product Visual QA

The standard runner completed all 123/123 captures. Authored hierarchy/environment, exactly one enabled XR Origin, legacy suppression, and the mountain sweep passed. Classification remained `FAIL_MACHINE_QA` solely because six previously recorded views exceed the frozen 77-draw ceiling:

- `04b_cockpit_engine_cowl`: 78
- `12_external_control_sweep`: 85
- `16_approach`: 87
- `cloud_01_ground_scattered`: 79
- `pasv1_18_cockpit_side_window`: 82
- `pasv1_27_cockpit_shadow_heading_090`: 78

No Traffic V2 placement failure was reported. Maximum observed estimates were 87 draws, 690,015 visible triangles, and 38 materials. The known draw-budget failures are retained and are not waived for product acceptance; they were treated only as non-blocking for this developmental headset launch.

- Report JSON: 183,972 bytes, SHA-256 `e6cad78b1d2d6fea27cae5c11e6e75a35ff64918794c849d9f35e46875c43d58`
- Contact sheet: 12,170,038 bytes, SHA-256 `b606ae1a8814f728ce2aebd242f40b1601c14dc30e66dc2cbe094ee07138bf11`
- Artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_system_v2_20260821_170722\gate3_standard_visual_qa`

## Android Build

The first clean Android attempt failed in Gradle because Unity OpenXR and Meta OVRPlugin both staged `libopenxr_loader.so`. The product build now checks the generated Gradle dependency shape, retains exactly one Meta OVRPlugin dependency, and removes the redundant Unity AAR only when present. A failed-build idempotence defect was also fixed: the package-owned JSBSim StreamingAssets injection is removed before integrity validation and again in the build `finally` path, but an unowned directory still fails closed.

The corrected build completed successfully with zero errors:

- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`
- APK bytes: 223,146,891
- APK SHA-256: `46ab2d9f4207747b744238688da8b5b5bd390bd5869ec516641fca39e1f1f45e`
- Package/version: `com.alexoviedo.t2.questflight`, `0.1.0-alpha.1` (`versionCode=1`)
- Target: Android ARM64, IL2CPP, Vulkan, OpenXR single-pass instanced, Linear, 4x MSAA
- Build: non-development; Android debug certificate; APK Signature Scheme v2 verified
- Integrity: PASS; native JSBSim required; UnityPrototype forbidden
- Build report: `Succeeded`, zero errors, 15 warnings
- Build evidence SHA-256: `423bc1f6c059a9a3c6b8ba3522d6f7909daad500824d9ecb3ab1cc22ee16f462`
- Successful Unity log SHA-256: `d276fbb51eee42705cb3a7235a0a3a027ba7974eeb175c486dccf868179ee32e`

The debug certificate is acceptable only for this local development smoke test; it is not release signing.

## Physical Quest 3

- Device: connected and authorized Quest 3; battery 100%; approximately 245 GB free before install.
- Install: `adb install -r -d` returned `Success`.
- Launch: `UnityPlayerGameActivity` started and remained alive after 20 seconds; observed RSS approximately 286 MB.
- Android fatal crash evidence: none for the application process during the observed startup interval.
- OpenXR: initialized far enough to create the live application process/surface. Meta logged warnings for optional spatial-scene function pointers whose extensions were not enabled; Traffic V2 does not use those functions.
- Physical visual inspection: awaiting the user wearing the headset. An ADB compositor screenshot returned zero bytes and was discarded, so it is not visual evidence.

## Acceptance Boundary

This gate proves the candidate builds, installs, and launches on the connected Quest 3 and that selected moving cars render and move in Editor diagnostic views. It does not prove headset-visible Traffic V2 placement, stereo quality, sustained frame rate, thermal stability, full-map coverage, density, or the later Traffic V2 acceptance requirements. The current Traffic V2 content remains the exact 12-trip, three-area, 900-second non-looping Gate 1 candidate while the accepted Traffic V1 parked-car field remains present.
