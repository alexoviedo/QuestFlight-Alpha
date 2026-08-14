# Volumetric Cloud Field V4 — 2026-08-14

## Outcome

This candidate replaces the rejected camera-facing cloud-card presentation
with eight bounded VDB-derived cloud volumes. The volumes have real side and
underside depth, shaded bases, and sampled fly-through extinction while sharing
one mesh, one renderer, one material, and one draw. The release-style APK built,
installed, and launched on the connected Quest 3. Worn visual acceptance is
pending and no merge-to-main decision is recorded yet.

This is a sparse standalone-Quest representation, not literal atmospheric
simulation or a claim of production photorealism.

## Source and preservation

- Canonical source: `bac1e3ab5cff2c4871ed7743cb6b30027eff19c3`
- Candidate branch: `feature/volumetric-cloud-field-v4`
- The rejected V3 worktree and its complete dirty state remain preserved at
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\cloud_surface_v3_20260814_003030\rejected_v3_exact_20260814`.
- V4 was implemented in the clean worktree
  `C:\Users\ovied\Dev\T2\T2-QuestFlight-CloudV4` rather than incrementally
  patching the rejected V3 state.

## Supported root cause and design decision

The prior representation was structurally a camera-facing cutout field. Its
single-depth imagery could not supply a convincing side, underside, or
fly-through volume, and its generated cloud appearance did not match the fixed
sky background. Increasing card count or replacing one atlas could not remove
that representation error.

V4 therefore uses bounded object-space ray integration over offline density.
It does not introduce a full-screen volumetric pass, cloud camera, particles,
transparent card stack, temporal reprojection, realtime cloud shadows, or
runtime-created hierarchy. This preserves a bounded and deterministic Quest
cost while changing the source of the rejected illusion.

Two intermediate V4 defects were found and corrected before the final build:

1. A giant proxy volume produced broad fog instead of discrete clouds. The
   field was changed to eight separate bounded proxies in one combined mesh.
2. Excessive erosion and inappropriate density normalization made early
   candidates visually weak. The offline compiler was corrected and the
   resulting density atlas was rehashed and revalidated.

## Weather and source record

The authored presentation is calibrated to the observed KBDU scattered-cloud
condition:

`METAR KBDU 141055Z AUTO 00000KT 10SM SCT018 17/16 A3013 RMK AO2`

This observation is fixed evidence, not a live-weather feed. The low scattered
layer is represented by the bounded field; the fixed HDR sky retains the high
background cloud layer.

External raw sources remain outside Git:

| Source | License | SHA-256 |
|---|---|---|
| JangaFX EmberGen `cloud_01_variant_0000.vdb` | CC0 | `c91381781adc353f26a25d6cb6c9e254f47b89db04d2e99d289ae0e7d545945c` |
| JangaFX EmberGen `cloud_06_variant_0000.vdb` | CC0 | `3c416cd2ca163e5bbc87911975806312d65b1e39a9ef83f2cd74f152efeadbee` |

Committed optimized derivatives:

| Asset | SHA-256 |
|---|---|
| `KBDU_CloudDensitySlices_RGBA.png` | `a0391ded672092ff24d4dda70e28bb99f02079c9929df62d562680617ccaff1f` |
| `KBDU_CloudCoverage_RGBA.png` | `1a42fb731d80b46e1fd2c92adfa434b60f934ae51ed784c55c7ea8ac8aaf08b4` |

The deterministic compiler is `tools/clouds/build_sparse_volumetric_field.py`.
Complete provenance and import policy are recorded in the adjacent cloud
`SOURCE_MANIFEST.json` and `SOURCE_AND_LICENSE.md`.

## Runtime contract

- eight separately bounded formations;
- one serialized combined mesh;
- 64 vertices and 96 triangles;
- one renderer, one material, one draw;
- one premultiplied-alpha transparent slot with depth testing and no depth write;
- maximum 48 raymarch samples only inside proxy bounds;
- front-to-back Beer–Lambert extinction with early exit;
- manual trilinear interpolation through the packed density slices;
- the same world-space density for both eyes;
- no camera-facing orientation or per-eye frame selection;
- smooth fog/sun/sky attenuation while inside a formation;
- no additional camera, realtime probe, cloud shadow map, or per-frame mesh
  displacement.

## Validation evidence

Focused EditMode tests passed 7/7. They verify the single-batch topology,
formation and altitude contracts, shader architecture, texture import settings,
serialized mesh bounds, and fly-through containment.

Four full-resolution cloud views were generated and inspected at:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\volumetric_cloud_field_v4_20260814\visual_qa_final_shading`

The ground, pattern, sunward, and inside-cloud views show rounded volume,
shaded bases, absence of the former white card outline, absence of proxy-box
edges, and smooth inside-cloud extinction. Long-range silhouettes remain
simple and must be judged in the worn display.

Aggregate Visual QA remains `FAIL_MACHINE_QA` due inherited full-scene planning
ceilings rather than a dedicated cloud assertion: one approach view reaches 80
draws against a 75-draw generic threshold, and the cloud-ground view reaches
694,164 visible triangles against 650,000. The dedicated pattern, sunward, and
inside-cloud views pass their machine gates. Successful build-time integrity
reported 121 renderers, 784,865 of 800,000 serialized triangles, and 37
materials.

Meta XR Simulator compiled the development player. Live stereo connection was
blocked by a Windows Security network prompt, so the result is
`BLOCKED_WINDOWS_SECURITY_NETWORK_PROMPT`. It is neither a stereo pass nor a
cloud-rendering failure.

## Android build and Quest state

- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`
- Bytes: 178,334,854
- SHA-256: `193b7adc00ce69ed36218ef7c70b45c07998375a44c4094af087ea5ebb51db40`
- Shipping contract: Android ARM64, Vulkan, OpenXR, native JSBSim required,
  UnityPrototype forbidden
- A clean-package build exposed duplicate OpenXR loaders. The product build now
  deterministically disables Unity's `openxr_loader.aar` and retains Meta's
  `OVRPlugin.aar`, matching Meta SDK's intended loader policy and removing
  cache/order dependence.
- The exact APK installed successfully, launched as
  `com.alexoviedo.t2.questflight`, and produced no fatal, Unity-exception,
  shader, or OpenXR error in the launch-log scan.

## Pending physical gate

The installed immutable APK still requires the requested worn review for:

1. several separated cloud formations rather than one repeated object;
2. convincing side and underside depth without white outlines or box edges;
3. smooth fly-through visibility transition without a hard wall;
4. stable stereo presentation without shimmer, popping, or discomfort;
5. overall realism score and largest remaining defect.

No 72 Hz timing, thermal, stale/tear, or comfort acceptance is claimed until it
is measured on this exact APK. Main remains unchanged pending that result.
