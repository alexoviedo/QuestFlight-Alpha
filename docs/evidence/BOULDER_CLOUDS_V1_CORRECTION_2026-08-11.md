# Boulder Cloud Field V1 correction evidence — 2026-08-11

## Scope

- Repository: `alexoviedo/T2-QuestFlight`
- Branch: `feature/boulder-clouds-v1`
- Starting source: `5f832328dece45b0831586719fc23178acba861f`
- Product scene: `Assets/Scenes/ProductionVerticalSlice.unity`
- Artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\boulder_cloud_correction_20260811_132351`

This bounded correction addresses three reported defects: cloud-card borders,
cloud/sky photometric mismatch, and the apparent vertical post-stall descent in
the previously installed cloud APK. It does not add another weather system or
change the aircraft model, controls, product hierarchy, or cloud placement.

## Proven causes

1. The original RGB atlas had no alpha channel and retained a black plate. The
   shader inferred coverage from luminance, while the texture repeated and did
   not preserve mip coverage. Filtered black texels therefore produced visible
   card borders.
2. The cloud pass was effectively bright unlit white while the skybox used its
   calibrated 0.74 exposure. The two systems had no common sun/ambient response,
   creating an over-bright cloud/dark-sky mismatch.
3. The reported flight regression was not caused by cloud forces. The prior
   174,747,481-byte cloud APK was a release-style build with native JSBSim
   disabled, so it ran the Unity prototype backend. That simplified backend can
   collapse lift after its stall threshold while gravity remains fully active,
   allowing an implausibly vertical descent. The previously accepted native
   handling APK and the cloud APK were therefore not backend-identical.
4. Cloud mesh CPU bounds covered only card centers, not the vertex-shader-expanded
   quads. This could cull a card while its visible pixels were still in view.

## Correction

- `tools/clouds/build_cloud_rgba_atlas.py` deterministically converts the pinned
  source into a straight-alpha RGBA runtime derivative. It removes the
  contaminated contour, feathers coverage, dilates safe edge colors, and keeps
  atlas-cell gutters transparent. The committed runtime SHA-256 is
  `712ee9dfc6dc1a56619d1c54111831937dbabec7a4f1daaffbc23f0ca635ab1a`.
- The Android importer uses Clamp, mipmaps with alpha-coverage preservation,
  alpha transparency, and ASTC 6x6. The shader samples authored alpha, clips only
  at 0.02, uses alpha-to-coverage with 4x MSAA, and applies restrained shared
  ambient/sun response instead of an unlit white output.
- The authored mesh and submesh bounds now enclose the full expanded radius of
  every card. The authoring path updates the existing serialized objects rather
  than recreating product hierarchy.
- Product build entry points now reject contradictory prototype/native flags and
  reject native JSBSim when development mode is off. This prevents another APK
  from silently differing from the handling candidate in build identity.
- The flight-evidence Python validator now consumes the current v2 evidence
  schema emitted by Unity.

## Validation

- Atlas compiler: deterministic source/hash guard and final output hash pass.
- EditMode: PASS 305/305.
- PlayMode: PASS 10/10.
- Python flight-evidence tests: PASS 3/3.
- Structural flight evidence: valid v2 contract; 0 errors, 0 failures, 5 passes,
  15 unproven items, and 4 unsupported items. This is regression evidence, not
  handling acceptance.
- Dedicated 1920x1080 cloud images were inspected at full resolution. The former
  charcoal card borders are absent and cloud luminance is materially closer to
  the sky. Evidence is under `visual_qa_verified/screenshots/cloud_0*.png`.
- Full Visual QA retains the inherited `FAIL_MACHINE_QA` because the scene has
  38 materials against the generic planning target of 29. The cloud delta is
  unchanged at one draw and 30 triangles; this correction does not conceal that
  pre-existing aggregate result.
- Meta XR live stereo captured matching cloud silhouettes in both eyes. The
  aggregate remains `BLOCKED_MISSING_RECORDED_REPLAYS` because the ten required
  `.vrs` recordings are absent.
- Android ARM64/Vulkan/OpenXR development/native build: PASS. APK size is
  301,748,868 bytes; SHA-256
  `5d6fdef693b99f11877447bf07c395a9ffe84d397ab9f8389d826441e0090c2c`;
  package `com.alexoviedo.t2.questflight`; manifest flags are
  `development=true`, `forceUnityPrototype=false`, and
  `enableNativeJsbsim=true`.
- The exact APK is installed on the connected Quest 3. Because the headset was
  not worn during unattended relaunch, Horizon paused the application before
  the backend initialization log could be captured. Runtime backend identity,
  visual acceptance, stall/slip recovery, comfort, and physical performance
  remain pending the exact-APK worn test.

## Decision and limits

The source is a test-ready correction candidate. It is not a release, literal
volumetric-cloud, certified-flight, training-credit, or Quest-performance
acceptance claim. Cloud interiors remain a bounded fog transition rather than a
sampled vapor volume. The Unity prototype remains the formal shipping authority
until native JSBSim passes its promotion gate; this development APK intentionally
uses native JSBSim to restore the already-reviewed handling path for physical
comparison.
