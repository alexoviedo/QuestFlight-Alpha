# Standalone Scenery Baseline V1 — 2026-07-20

## Decision

`feature/aerial-scenery-recovery-v2` at `b976314c147d2319a3d4021f534f6d22483a17d8` is accepted as the standalone Quest scenery **development baseline**. It was merged from starting `origin/main` `c7393beebd97362e78ccb8c5d0579045b5fad852` with no conflicts and is identified by annotated tag `standalone-scenery-baseline-v1`. This decision consolidates validated work; it does not claim release readiness, production photorealism, FAA approval, navigation suitability, legal training credit, certified flight fidelity, or final Quest performance.

## Accepted product state

- default seat profile moved 5 cm aft and the prior worn smoke required no compensating backward step;
- 2,875 fixed, closed, footprint-preserving context-building shells replace the widespread floating-roof and scale-changing path; 24,804 ordinary far features remain orthophoto-only;
- four 2048² hot-zone quadrants plus a 4096² 12 km fallback derive from official public-domain 2019 USGS/USDA NAIP Plus imagery;
- 22 near vegetation placements are batched in two draws, with terrain-owned lidar canopy shading beyond the near tier;
- water presentation is intentionally orthophoto-only; failed reservoir/ribbon/suppression renderers are inactive;
- immutable mountains, the current runway, aircraft/cockpit, input path, and Unity prototype flight authority remain unchanged;
- `ProductionVerticalSlice` remains explicitly serialized with one XR Origin, tracked Main Camera, aircraft simulation root, and flight authority.

## Integration and validation

The accepted source was first validated in an isolated worktree. The integration merge commit is `56f4375ef3209653b16a05b268131c7172db27e0`; its product tree matched the accepted source before documentation and repository-hygiene changes. No Unity merge conflict occurred. The aggregate validation runner was corrected to forward its requested `ProjectPath` to nested Unity scripts so isolated worktree results cannot silently target another checkout.

Results on the mainline candidate:

- ProductIntegrity and dependency closure: PASS;
- EditMode: 98/98 PASS;
- PlayMode: 9/9 PASS, including a repeat run;
- 56-view Visual QA: `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; every full-resolution frame and the contact sheet were inspected;
- visual parity: static scenery and budget match the accepted source; 42/56 images were byte-identical and remaining dynamic cockpit/aircraft frames showed capture-run variation, not a source-tree difference;
- Meta XR Simulator v201: live Quest 3 profile stereo/HMD/both Touch PASS with both eyes inspected; aggregate `BLOCKED_MISSING_RECORDED_REPLAYS` because all ten genuine `.vrs` files are absent;
- static plan: 55 maximum draws, 534,976 selected triangles, 671,940 serialized scene triangles, 24 visible materials, 185,361,306 estimated texture bytes (176.77 MiB), two transparent slots, 11 shaders / 17 variants, 4x MSAA, and 94 active renderers;
- Android release-style build: PASS for `com.alexoviedo.t2.questflight`, 106,789,598 bytes, SHA-256 `5d2ab8fdc18f379bb9fcefe4e1858b911ad51f1821cb7f2763afe566d8919208`.

The external integration artifact root is `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\scenery_baseline_v1_20260720_093859`. It contains the safety bundle, branch audit, accepted-source and candidate logs, manifests, inspected contact sheets, simulator evidence, and Quest telemetry. Captures, APKs, logs, and the artifact directory are not committed.

## Human and physical Quest evidence

The accepted source's prior worn smoke scored approximately 7/10. It confirmed the corrected seat, no observed building shape/size popping, no widespread floating roofs, a visible canopy cue, and preference for orthophoto-only water. It also reported that right-side imagery over the reservoir remained blurry while the left view looked decent. That smoke did not establish long-duration performance.

One unattended run used the exact integration APK above: 90-second warmup, 60-second measurement, PID 7966. It averaged 72.5333 FPS; combined CPU/GPU average was 13.117833 ms, p95 13.7545 ms, and 1/60 samples (1.6667%) exceeded 13.8889 ms. Stale and tear counts were zero; temperature was 31 °C with no thermal warning; crash, ANR, OOM, and Unity-exception counts were zero; 4,322 observed frames reported zero managed collections. The run is `FAIL_MACHINE_GATE` because combined average exceeded 12.8 ms and the hardware inventory reported 509,437 visible triangles versus 500,000. Direct GPU milliseconds were unavailable. It is one unattended run, not the required three-run acceptance, and supplies no new human comfort or visual evidence.

## Deliberately excluded experiments

No failed branch tip or URP experiment was separately merged. Historical experiment commits already in the accepted source ancestry are retained only as history; rejected water, grove, hero-building, suppression, ribbon, runtime-repair, and URP representations are not active product authority. See `docs/SCENERY_BRANCH_LINEAGE.md`.

## Known limitations and next boundary

Context buildings remain visually simple; aerial canopy and grove volume need future improvement; some imagery is soft at oblique angles; runway presentation is imperfect; orthophoto-only water is intentionally flat; genuine simulator replay sessions are absent; and three-run worn Quest performance/thermal/comfort acceptance remains pending. These limitations do not invalidate the coherent development baseline but do block any release-readiness claim.
