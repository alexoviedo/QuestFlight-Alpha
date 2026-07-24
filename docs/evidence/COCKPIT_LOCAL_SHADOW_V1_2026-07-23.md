# Cockpit Local Shadow V1 evidence

## Scope and source

- Repository: `C:\Users\ovied\Dev\T2\T2-QuestFlight`
- Branch: `feature/cockpit-local-shadow-v1`
- Source commit: `b1551ace7a862d2ca09de3efd6eabc03e1537622`
- Shipping target: standalone Quest 3, Android ARM64, Vulkan, OpenXR
- Unity: `6000.3.8f1`

This milestone changes only cockpit-local directional shading. It preserves the
accepted cabin glass, instrument glass, displays, aircraft hierarchy, XR Origin,
tracked Main Camera, seat calibration, flight authority, exterior art, and
physics.

## Implementation

The authored aircraft prefab contains one `ProductionCockpitLocalShadow` root:

- one disabled orthographic camera with no XR eye target;
- one disabled 120-triangle, one-submesh cabin-frame/coaming proxy;
- one 512 x 512 `R16G16_SFloat` render texture with a 16-bit depth attachment;
- one depth shader writing linear depth and depth squared;
- one late-update offscreen caster draw;
- one filtered receiver lookup in the existing opaque cockpit shader.

The runtime map is world-locked to the aircraft and aligned to the authored sun.
Both XR eyes sample the same map and transform. A mip-filtered two-moment
probability edge replaces the rejected sparse-PCF samples, producing a continuous
penumbra without temporal jitter. The proxy is enabled only for the synchronous
offscreen draw and then disabled. The implementation adds no light, cascade,
projector, transparent overlay, Main Camera dependency, runtime hierarchy
construction, or per-eye camera-facing behavior.

## Root cause and correction

The first accepted directional silhouette used sparse depth comparisons. Wider
four-, nine-, and sixteen-tap PCF kernels changed the sample footprint but kept
each comparison hard, so the worn edge remained visibly triangular and
sawtoothed. The final representation stores the first two depth moments,
generates hardware-filtered mips, and performs one continuous Chebyshev
visibility evaluation. This changes edge continuity while preserving the
already accepted shadow direction, bounded strength, stereo lock, and readable
instrument emission.

## Validation

- EditMode: PASS 118/118.
- PlayMode: PASS 9/9.
- Production Visual QA: PASS 85/85,
  `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`.
- Full-resolution default cockpit, instrument, directional-shadow, and
  shadow-map preview images inspected.
- Static Quest planning: PASS,
  `PASS_STATIC_QUEST_BUDGET_HEADSET_MEASUREMENT_REQUIRED`.
- Maximum selected draws: 55.
- Maximum selected triangles: 628,768.
- Visible materials: 29; active distinct materials: 28.
- Estimated texture bytes: 212,069,266.
- Transparent material slots: 3.
- Shaders/keyword combinations: 13/18.
- MSAA: 4x.
- Meta XR Simulator v201.0 live `cockpit_neutral`: PASS with 1440-square
  nonblank stereo eyes, OpenXR HMD, both Touch controllers, consistent render
  roots/materials, and no missing components.
- Aggregate simulator status remains `BLOCKED_MISSING_RECORDINGS` because
  genuine deterministic `.vrs` recordings were not available.
- Android release-style build: PASS.

Primary artifact root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\cockpit_local_shadow_v1_20260723_160258`

Primary captures:

- `visual_qa_vsm_final\visual_qa_contact_sheet.png`
- `visual_qa_vsm_final\cockpit_local_shadow_depth_preview.png`
- `simulator_vsm_final\meta_xr_simulator\qa_run\sessions\cockpit_neutral\cockpit_neutral_stereo.png`

## Exact APK and worn result

- APK:
  `C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight\Builds\Android\QuestFlight-production.apk`
- Bytes: 136,203,589
- SHA-256:
  `4dc479af8b961909ddbac0249b863c040ebf95e04796437aa26bcc2ac9cf6057`
- Device: connected Quest 3

The exact APK was installed and launched on the physical Quest 3. The final worn
review reported:

- blurred/continuous shadow edge: yes;
- remaining jagged or sawtoothed boundary: no;
- direction and strength: mostly realistic, possibly slightly underwhelming;
- flicker or stereo mismatch: no;
- instruments and readouts readable: yes.

This is visual acceptance of the bounded shadow presentation. It is not
long-duration Quest performance, thermal, allocation, comfort-duration, release,
FAA, navigation, training-credit, or certified-fidelity evidence.

## Remaining limitation

The final shadow strength is intentionally conservative to preserve comfort and
instrument readability. A future polish milestone may evaluate a modest strength
increase only with another exact-APK worn check. No such change is included here.
