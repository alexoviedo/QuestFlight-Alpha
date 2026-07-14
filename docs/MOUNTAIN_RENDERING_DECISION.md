# Mountain Rendering Decision

## Decision

Use Option A: one immutable 24 km far-terrain ring with a 100 m render grid, baked normals, and sealed inner/outer skirts. Keep the existing near/mid geometry for parallax. The far object has no `LODGroup`, crossfade, dithering, billboard behavior, camera-relative scaling, or runtime regeneration.

The authored mesh contains 89,280 triangles. Its source package is `tools/kbdu_orthophoto/kbdu_far_dem_100m.json`; the serialized mesh keeps the prior asset GUID `f7e9defd4f2f8134ea4ed04d496c0495`.

## Source fidelity and live-service blocker

The official source remains the public-domain USGS 3DEP Bare Earth service. A live F32 export request returned a valid response with the exact requested extent, but its generated TIFF output URL returned HTTP 502 on all six bounded retries. The failed service metadata and export response are preserved externally under `kbdu_dem/raw/`; no TIFF or false completed receipt was created.

To avoid waiting indefinitely, the 241×241/100 m render grid was deterministically bilinear-resampled from the previously frozen 61×61/400 m USGS grid (snapshot `20260710T214309Z`, fetch receipt SHA-256 `656700adea334874c14ee4f5fdfcdd360a6d8cfb2c76b67cf3e5e95b9dd66ee0`). The densification improves mesh tessellation and baked-normal continuity but does not add source elevation detail. All 3,721 frozen control samples are preserved at their corresponding four-grid-step positions.

## A/B review

Option A evidence is `kbdu_dem/ab_evidence/mountain_option_a_100m_dem_hillshade.png` (SHA-256 `d5a3989048f665a41e53fd92b94b8a2f20896b92fc34dbca75b9d7abd3ed9387`). Option B evidence is the same-source static 360-degree horizon at `kbdu_dem/ab_evidence/mountain_option_b_dem_horizon_panorama.png` (SHA-256 `deda18e867a1ca760be1e664ea9099332d69e5cb0c478db127743321270eb155`). Both full-resolution images were inspected.

The panorama produces a clean western silhouette at minimal geometry cost, but it removes far-terrain stereo/parallax and visibly flattens the horizon during head translation. The fixed mesh retains real geometry, world anchoring, rear-view continuity, and stereo depth while remaining below the static triangle budget. Option B is therefore retained as external comparison evidence only and is not in the product dependency closure.

Meta XR Simulator and Editor captures establish visual/stereo behavior only. They do not establish Quest GPU timing, thermal behavior, or worn-device acceptance.
