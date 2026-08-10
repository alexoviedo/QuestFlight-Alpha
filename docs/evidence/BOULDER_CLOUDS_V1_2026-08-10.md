# Boulder Cloud Field V1 evidence — 2026-08-10

## Scope and source

- Repository: `alexoviedo/T2-QuestFlight`
- Branch: `feature/boulder-clouds-v1`
- Source checkpoint: `7dba946bfc8fd8a04b0f6e9132443fed037bda6a`
- Cloud implementation commit: `7c63da4d8099c83a636cd70dcc3895babcfcb1ed`
- Product scene: `Assets/Scenes/ProductionVerticalSlice.unity`
- Profile: `KBDU_SUMMER_SCATTERED_V1`
- KBDU elevation datum: 1,612.6 m MSL
- Declared layer: 1,450–2,550 m AGL; 15 bounded spatial volumes

The source repository was bundled before implementation. Existing native-flight
candidate work was committed separately before the cloud branch was created.
No aircraft, cockpit, physics, input, terrain, water, runway, or XR hierarchy
was reconstructed for this milestone.

## Research and asset provenance

The authored profile was constrained using NOAA Aviation Weather Center METAR
terminology, NOAA/NCEI Local Climatological Data, NSF NCAR/UCAR guidance on
mountain and lenticular clouds, and National Weather Service cloud-development
guidance. Exact authoritative URLs are recorded in the adjacent cloud source
manifest.

The four-view cumulus atlas was generated with OpenAI image generation from a
prompt requesting isolated fair-weather cumulus, neutral midday lighting,
broad shadowed bases, vapor-eroded edges, and no sky, ground, text, or
watermark. It was then imported as a maximum-512-square, mipmapped, streaming
Android ASTC 6x6 derivative. SHA-256:
`a4bf9047b4427f71e902cb6aa1b5c3ff1296e29f4f5d0dd685be5ecc4548dd82`.
No external cloud photograph or raw download enters Git.

## Runtime representation

- One explicitly serialized `BoulderCloudFieldRoot`.
- One renderer, one material, one opaque alpha-to-coverage pass.
- 15 quad impostors batched into one 30-triangle mesh.
- One center-eye orientation calculated in the vertex shader and shared by
  both eyes; no CPU/per-eye billboarding.
- Depth write on; transparent blending, particles, cloud cameras, realtime
  cloud shadows, refraction, and raymarching off.
- Entering an authored volume smoothly modulates existing fog color/density,
  directional-sun intensity, and sky exposure. It does not write the camera or
  create runtime hierarchy.

This is a Quest-bounded spatial cloud presentation, not a literal volumetric
weather renderer.

## Static budget delta

| Metric | Pre-cloud | Final | Delta |
|---|---:|---:|---:|
| Environment renderers | 51 | 52 | +1 |
| Environment serialized triangles | 504,064 | 504,094 | +30 |
| Environment materials | 10 | 11 | +1 |
| Maximum QA estimated draws | 78 | 79 | +1 |
| Maximum QA visible triangles | 675,835 | 675,865 | +30 |

The generic aggregate QA ceiling was already exceeded before this milestone,
so both baseline and candidate classify `FAIL_MACHINE_QA`. The cloud work did
not conceal or relabel that inherited failure.

## Validation evidence

- Product integrity: PASS — 1,299 assets; 1,904 scene objects; 120 renderers;
  784,799/800,000 serialized triangles; 36 materials.
- Dependency closure: PASS — 580 total; 386 approved; 194 packages; zero
  rejected; zero unknown.
- EditMode: PASS 303/303, including five cloud contract tests.
- PlayMode: PASS 10/10.
- Dedicated QA: ground scattered, pattern-volume, sunward-structure, and
  interior-transition views captured. Full-resolution images were inspected;
  the one-batch images retain the final cloud silhouettes and soft edges.
- Meta XR Simulator: live Quest 3-profile stereo capture inspected with the
  cloud orientation matching between eyes. Aggregate status is
  `BLOCKED_MISSING_RECORDED_REPLAYS` because all ten genuine VRS files are
  absent.
- Android release-style build: PASS; package
  `com.alexoviedo.t2.questflight`; 174,747,481 bytes; SHA-256
  `37af3ab6049da78cd73915ab11382fa278c6806fb4fa663160b4d54517ea5315`.

Artifact root:
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\boulder_clouds_v1_20260810_135248`

Key external evidence:

- `visual_qa_one_draw_final/visual_qa_contact_sheet.png`
- `visual_qa_one_draw_final/screenshots/cloud_01_ground_scattered.png`
- `visual_qa_one_draw_final/screenshots/cloud_02_pattern_volume.png`
- `visual_qa_one_draw_final/screenshots/cloud_03_sunward_structure.png`
- `visual_qa_one_draw_final/screenshots/cloud_04_inside.png`
- `simulator_gate/meta_xr_simulator/visual_gate/simulator_stereo_contact_sheet.png`
- `build_final/product_apk_manifest.json`

## Limitations and decision

The branch is suitable for a physical Quest visual/performance check, but no
worn cloud review, sustained hardware timing, thermal, stale/tear, comfort, or
allocation acceptance has been run for this exact APK. The authored preset is
static rather than meteorologically live, and cloud interiors use bounded fog
rather than full density rendering. Therefore this evidence does not claim
literal photorealism, release readiness, or Quest performance acceptance.
