# QuestFlight 0.1.0 Alpha 1 checkpoint

This document records a private, sideloaded standalone Quest 3 development
checkpoint. It is not a public release, production-readiness determination,
FAA approval, navigation product, training-credit claim, certified C172 model,
or final performance/photorealism acceptance.

## Source and distribution

- Release tag: `v0.1.0-alpha.1`
- Product scene: `Assets/Scenes/ProductionVerticalSlice.unity`
- Unity: `6000.3.8f1`
- Android package: `com.alexoviedo.t2.questflight`
- Backend: Unity prototype, explicitly forced for the release build
- Native JSBSim: development-only and not promoted
- Release asset: `QuestFlight-0.1.0-alpha.1-Quest3.apk`
- APK bytes: `174,634,521`
- APK SHA-256: `9d08bf84908c2b4d4a66d7e89d0f94ed095d8ee00fefb23a99cb61a43edeb2c9`
- Distribution: private GitHub prerelease; testers require repository access

The exact release asset was installed successfully on the connected Quest 3.
Android reported version name `0.1.0-alpha.1` and version code `1`, and the
launcher activity started successfully.

## Meta Alpha distribution candidate

A second APK was built from the same Alpha product source with a permanent,
developer-owned Android release certificate for Meta release-channel delivery.
It does not change runtime product content.

- Candidate: `QuestFlight-0.1.0-alpha.1-Meta-Quest3.apk`
- Bytes: `174,634,793`
- SHA-256: `f3e5c2178f64f02b98424f5c4a61b13dcc728cab61c9b4cb82eb2425a35393f5`
- Android signing: v2 PASS; one 4096-bit RSA signer
- Certificate SHA-256: `2a505f05bdfa94a3934689140efd1fe907d5aed611fad5962b3b8b9b46709a21`
- Custom signing recorded by Unity build evidence: `true`
- Meta Dashboard upload/channel status: pending authenticated dashboard access

The signing keystore and credentials remain outside Git under Alex's restricted
user profile. Both files must be backed up together; losing either prevents
future updates under the same Android application identity. The prior
debug-signed APK cannot be updated in place by this release-signed APK and must
be uninstalled before switching Alex's headset to Meta-channel delivery.

## Accepted checkpoint

Alex's final worn review accepted the current experience as suitable to share
privately. The checkpoint includes the authored KBDU-inspired scenery, cockpit
presentation, Meta hand/Touch interaction, improved runway, propeller
presentation, guided reservoir route, and recovery loop accumulated before
this release checkpoint.

The immutable Alpha APK was rebuilt after that worn acceptance to apply only
release metadata and validation/test-policy corrections. It received an
unattended install/launch check, but the exact bytes were not subjected to a
new worn review or a three-run long-duration hardware gate.

## Validation evidence

| Gate | Result |
|---|---|
| Product integrity | PASS |
| Serialized dependency closure | PASS — 572 dependencies; 378 approved project assets; 194 Unity/Meta package assets; 0 rejected; 0 unknown |
| EditMode | PASS — 297/297 |
| PlayMode | PASS — 10/10 |
| Required machine QA captures | 97/97 rendered and structurally valid |
| Android release-style build | PASS |
| Exact APK install and launch | PASS |
| Static Quest planning budget | FAIL — retained as disclosed Alpha debt |
| Three-run Quest timing/thermal/allocation gate | NOT RUN |

The dependency policy retains the broad legacy `/Training/` exclusion while
allowing exactly the three product-authored route/recovery dependencies used by
the serialized Alpha scene.

Two stale regression assertions were corrected during the checkpoint: the
prototype backend is required to expose conventional sideslip beta in degrees,
and the stopped propeller is intentionally visible from the cockpit until the
RPM-driven blur presentation takes over. Both complete test suites passed
after those expectation corrections.

## Static budget debt

The limits were not relaxed. The current editor planning report is:

| Metric | Result | Target |
|---|---:|---:|
| Maximum estimated draws | 78 | 75 |
| Maximum visible triangles | 675,835 | 650,000 |
| Maximum visible materials | 37 | 29 |
| Estimated texture residency | 267.60 MiB | 210.00 MiB |
| Transparent material slots | 17 | 3 |
| MSAA | 4x | 4x |

The report also confirms Android ARM64, Vulkan, ASTC, IL2CPP, OpenXR
single-pass instancing, multithreaded rendering, and mip streaming. These are
static/editor facts, not proof of sustained Quest frame rate, thermals, stale
frames, tears, or allocation behavior.

## Flight-model evidence boundary

The Unity prototype remains authoritative because the unchanged native JSBSim
promotion gate has not passed. The engineering scenario harness still contains
failed, unsupported, and unproven fidelity cases. The guided flight is suitable
for this private visual/interaction Alpha demonstration, not for claiming
realistic or certified aircraft handling.

## Evidence locations

External artifacts are preserved outside Git under:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\alpha_checkpoint_20260807_194606`

That directory contains the pre-release Git bundle and patches, full test XML,
dependency and integrity logs, 97 full-resolution QA views and contact sheets,
the static-budget report, build log/manifest, immutable APK, and checksum.

The release-signed Meta candidate and signature evidence are under:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\meta_alpha_distribution_20260807_205631`

## Remaining limitations

- The static Quest planning budget is exceeded on every tracked aggregate
  target except 4x MSAA and the shipping-architecture checks.
- The exact APK lacks long-duration physical Quest timing, thermal, stale-frame,
  tear, and allocation evidence.
- Flight dynamics, systems, and recovery behavior remain prototype-level.
- Visual fidelity remains uneven outside the authored hero areas.
- Some cockpit controls are visually merged or unrepresented and remain static.
- Private GitHub access and sideloading remain required until the signed Meta
  candidate is uploaded and its Alpha-channel invitation is enabled.
