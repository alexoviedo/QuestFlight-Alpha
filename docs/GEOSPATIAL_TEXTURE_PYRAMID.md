# KBDU geospatial texture pyramid V1

## Runtime contract

The aerial surface is a fixed, explicitly authored texture set for the existing KBDU terrain shader. It is not a runtime map downloader, quadtree, addressable-tile system, or camera-driven hierarchy. The serialized material owns seven texture references and seven fixed local-UV vectors. The existing shader selects them from terrain world position and blends them across its existing distance bands.

All seven imports enable `streamingMipmaps`. The explicitly serialized `QuestFlightLab.Environment.GeospatialMipController` owns their mip-residency requests. The shader's fixed ENU vectors still own spatial lookup and its `tex2Dgrad` calls still select a sample level within the resident mip range; the controller owns which mip ranges Unity keeps resident. It does not own or write a camera transform and does not construct a runtime hierarchy.

The manifest contract is exact: `runtime.controller_type` is `QuestFlightLab.Environment.GeospatialMipController`, `runtime.controller_owned_texture_ids` lists all seven configured IDs in authored order, and `runtime.selection_mode` is `explicit GeospatialMipController-managed mip residency for seven fixed ENU shader tiles; no camera transform ownership or hierarchy construction`.

The coordinate frame is a WGS84 azimuthal-equidistant projection centered at the serialized KBDU origin `(40.03936527, -105.22608958, 1611.7824 m MSL)`. Derived image row 0 is north, column 0 is west, Unity `+X` is east, and Unity `+Z` is north. Calibration, XR Origin, tracked camera, aircraft center of gravity, and flight authority are outside this pipeline.

Lidar-derived data textures are the exception: their pixels remain on the
origin-relative UTM zone 13N source grid. The two grids differ by as much as
7.444086 meters across the four-kilometer lidar square. The terrain and semantic
surface shaders therefore transform product-AEQD world XZ into source UTM XZ
before sampling lidar maps, and rotate sampled horizontal normals back into
product axes. The pinned affine used by the shader has a maximum position
residual of 0.002037 meters; offline mesh generation uses the exact pyproj
transform. Treating the two local grids as interchangeable is a validation
failure.

## Fixed texture set

| Role / shader property | Local XZ bounds (m) | Output | Density | Reviewed source cache | Android import | Full mip estimate |
| --- | --- | --- | --- | --- | --- | ---: |
| Near / `_NearAlbedo` | `[-2000,-2000]..[2000,2000]` | 8192² | 0.48828125 m/texel | LOD 18, ≈0.4572 m ground sample | ASTC 8×8, streaming | 22,369,664 B |
| Mid NW / `_RecoveryMidNWAlbedo` | `[-4032,-32]..[32,4032]` | 2048² | 1.984375 m/texel | LOD 16, ≈1.8288 m ground sample | ASTC 6×6, streaming | 2,497,712 B |
| Mid NE / `_RecoveryMidNEAlbedo` | `[-32,-32]..[4032,4032]` | 2048² | 1.984375 m/texel | LOD 16, ≈1.8288 m ground sample | ASTC 6×6, streaming | 2,497,712 B |
| Mid SW / `_RecoveryMidSWAlbedo` | `[-4032,-4032]..[32,32]` | 2048² | 1.984375 m/texel | LOD 16, ≈1.8288 m ground sample | ASTC 6×6, streaming | 2,497,712 B |
| Mid SE / `_RecoveryMidSEAlbedo` | `[-32,-4032]..[4032,32]` | 2048² | 1.984375 m/texel | LOD 16, ≈1.8288 m ground sample | ASTC 6×6, streaming | 2,497,712 B |
| 12 km base / `_MidAlbedo` | `[-6000,-6000]..[6000,6000]` | 8192² | 1.46484375 m/texel | LOD 17, ≈0.9144 m ground sample | ASTC 10×10, streaming | 14,348,096 B |
| 24 km far / `_FarAlbedo` | `[-12000,-12000]..[12000,12000]` | 4096² | 5.859375 m/texel | LOD 15, ≈3.6575 m ground sample | ASTC 10×10, streaming | 3,589,696 B |

Every output density is equal to or coarser than its chosen cache ground sample. The pipeline fails if a configuration would upscale a reviewed cache LOD. The publisher's 9-inch source claim is retained as provenance; it is not used to claim that the selected cached runtime derivatives contain 9-inch detail.

The four recovery-mid tiles include a 32 m border on each side of their nominal 4 km cells. Their adjacent cells therefore share a 64 m world-coordinate overlap. The pipeline resamples both sides of each overlap onto the same comparison grid and rejects mean channel discontinuities above 12/255.

## Material vectors and selection

The source manifest emits each vector as `(minimum X, minimum Z, inverse width, inverse height)`:

| Shader property | Fixed vector |
| --- | --- |
| `_NearWorldMinInvSize` | `(-2000, -2000, 0.00025, 0.00025)` |
| `_RecoveryMidNWWorldMinInvSize` | `(-4032, -32, 0.00024606299212598425, 0.00024606299212598425)` |
| `_RecoveryMidNEWorldMinInvSize` | `(-32, -32, 0.00024606299212598425, 0.00024606299212598425)` |
| `_RecoveryMidSWWorldMinInvSize` | `(-4032, -4032, 0.00024606299212598425, 0.00024606299212598425)` |
| `_RecoveryMidSEWorldMinInvSize` | `(-32, -4032, 0.00024606299212598425, 0.00024606299212598425)` |
| `_MidWorldMinInvSize` | `(-6000, -6000, 0.00008333333333333333, 0.00008333333333333333)` |
| `_FarWorldMinInvSize` | `(-12000, -12000, 0.000041666666666666665, 0.000041666666666666665)` |

This preserves the existing authored radii and coverage. Increasing the far texture from 2048² to 4096² improves density inside the same 24 km bounds; it does not expand the world.

## Deterministic source snapshot

`tools/aerial_surface/build_texture_pyramid.py` uses only the reviewed City of Boulder item and service endpoints from `tools/aerial_surface/config.json`. Network access is HTTPS-only and host-allowlisted. Google-hosted or Google-named imagery is rejected by configuration validation.

The City service caps one exported image at 4096×4096, so the 8K derivatives are reconstructed from its native 256 px cache, not from an oversized export. For the fixed footprint, the final plan is:

| Cache LOD | Inclusive rows | Inclusive columns | Tile count |
| ---: | --- | --- | ---: |
| 15 | 12386–12414 | 6792–6819 | 812 |
| 16 | 24791–24810 | 13602–13621 | 400 |
| 17 | 49575–49629 | 27197–27250 | 2,970 |
| 18 | 99186–99222 | 54430–54466 | 1,369 |
| **Total** |  |  | **5,551** |

The approved first-acquisition snapshot canonical SHA-256 is `6fa6baea9eb2d876577da883d88b029e018a75dad9728f3faad13bed76ead02b`. Its 5,551 tile bytes total 85,223,758 bytes and its ordered tile-set SHA-256 is `41c88c222bae40db5ba061332e2919e99b682015c7ce7bc203f0c7d85c8a6906`.

Each tile record includes its exact URL, byte length, SHA-256, LOD, row, column, and external-cache-relative path. The source snapshot also pins the raw metadata response hashes, normalized metadata contract hashes, union tile ranges, per-level digests, and complete ordered tile-set digest. Offline mode requires the exact planned coordinate set and exact cached bytes; added, missing, or changed bytes fail the rebuild.

Raw responses, raw tiles, source snapshots, contact sheets, and scratch mosaics stay under the external product-artifact root. They are not Unity assets and must not be committed. Only the seven compact JPEG derivatives, their paired `.meta` files, and the compact source/runtime manifest are handoff candidates.

## Build and offline verification

Use the pinned scenery environment recorded in the output manifest. From this repository root, the approved V1 acquisition used these external paths:

```powershell
$python = '..\T2-QuestFlight-product-artifacts\aerial_scenery_v1_20260718_190855\toolchain\envs\scenery-pinned\python.exe'
$artifactRoot = '..\T2-QuestFlight-product-artifacts\photoreal_aerial_surface_v1_20260720_114230\workstream_a'
$raw = Join-Path $artifactRoot 'raw'
$out = Join-Path $artifactRoot 'runtime_handoff_final'

& $python -B tools\aerial_surface\build_texture_pyramid.py `
  --raw-cache $raw `
  --output-dir $out `
  --contact-sheet (Join-Path $artifactRoot 'aerial_surface_final_contact_sheet.jpg')
```

After first acquisition, rebuild into a clean second directory with the pinned external snapshot and no network:

```powershell
& $python -B tools\aerial_surface\build_texture_pyramid.py `
  --raw-cache $raw `
  --snapshot "$raw\KBDU_AerialSurfaceV1_SourceSnapshot.json" `
  --offline `
  --output-dir (Join-Path $artifactRoot 'runtime_handoff_rebuild')
```

Compare the SHA-256 of every JPEG, `.meta`, and JSON output. Do not refresh the source after approval; a deliberate source update starts with a new external snapshot and renewed imagery decision.

Run the deterministic unit suite with the same environment:

```powershell
& $python -B -m unittest discover -s tools\aerial_surface\tests -v
```

## Validation performed by the compiler

The compiler fails closed on:

- source item, revision, license evidence, access, description, service URL, cache geometry, spatial reference, or coverage mismatch;
- raw-cache placement inside the repository;
- missing or changed source tile bytes during a pinned rebuild;
- source LOD coarser than an output texel;
- a disabled streaming importer, missing controller requirement, changed controller type, or incomplete controller-owned texture ID list;
- replacement target path or baseline SHA mismatch;
- blank/corrupt output channel statistics;
- mid-quadrant world-coordinate seam error above 12/255;
- near-texture phase shift above 8 m against the unchanged legal 2019 ENU baseline;
- a projected static Android texture budget over the reviewed milestone ceiling.

The phase check is an alignment regression check, not a claim that the older and newer captures have identical objects or colors.

## Android texture ledger

All estimates include full mip chains and assume the paired streaming-enabled Unity importer settings in the handoff. They are a conservative worst-case residency ledger; enabling controller-owned streaming does not change the imported full-chain byte count:

| Ledger entry | Bytes | MiB |
| --- | ---: | ---: |
| New seven-texture runtime set | 50,298,304 | 47.968 |
| Seven one-for-one textures removed | 26,039,792 | 24.834 |
| Net static texture increase | 24,258,512 | 23.135 |
| Frozen baseline projected scene textures | 186,549,554 | 177.908 |
| Projected integrated scene textures | 210,808,066 | 201.042 |
| Milestone hard ceiling | 220,200,960 | 210.000 |
| Projected headroom | 9,392,894 | 8.958 |

The manifest exposes the integration keys directly as `runtime.near_meters_per_texel`, `runtime.pattern_meters_per_texel`, `runtime.far_meters_per_texel`, `runtime.runtime_set_android_bytes`, `runtime.baseline_replaced_android_bytes`, `runtime.projected_scene_android_texture_bytes`, and `runtime.hard_ceiling_bytes`.

These are deterministic full-chain import estimates, not measured controller residency, Quest memory bandwidth, frame time, thermals, or comfort. Integration is incomplete until the serialized product scene references the replacement assets and `GeospatialMipController`, controller ownership validates for all seven IDs, standard build validation passes, stereo simulator captures are inspected, and physical Quest frame/thermal acceptance is performed when hardware is available.

## Scope constraints

- Water is orthophoto-only. No water material, tint, animation, reflection, geometry, or additional draw is introduced.
- There is no world-extent expansion, terrain-geometry change, runway change, sky change, mountain change, or runtime hierarchy construction.
- The Main Camera remains OpenXR-owned and is never written by this system.
- Standalone Quest 3 / Android ARM64 / Vulkan / OpenXR remains the sole shipping target.
