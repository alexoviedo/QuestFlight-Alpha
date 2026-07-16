# Mountain and Horizon V2 Decision

## Decision

Retain **Option A: the fixed USGS-derived far DEM mesh** as the only far-mountain geometry. Integrate the continuous far orthophoto from Workstream B and the selected HDR sky from Workstream A onto the existing world-anchored architecture. Do not add a horizon panorama to the product scene at this stage.

This is an architecture and asset-readiness decision, not visual acceptance. Workstream A did not edit `ProductionVerticalSlice` or its environment prefab, and no integrated V2 stereo capture exists yet. Seam removal, sun alignment, mountain score, and runtime cost remain lead-owned integration gates.

## Existing Option A contract

The serialized far-mountain system already has the properties needed for stable stereo:

- USGS 3DEP public-domain source, frozen snapshot `20260710T214309Z`;
- a 24 km × 24 km local-ENU grid with a 12 km × 12 km inner opening;
- 241 × 241 samples at 100 m mesh spacing, derived by deterministic bilinear resampling of the frozen 400 m source;
- 63,841 baked vertices and 89,280 triangles after sealed inner and outer skirts;
- two conservative offline height-smoothing passes, with topology and triangle order preserved;
- offline normals, a fixed transform, one renderer, no `LODGroup`, no crossfade, no dithering, no billboard, no runtime rebuild, and no camera-relative scale.

The current weakness is material continuity, not the ownership model: the far shader still uses synthetic elevation/slope colors while the nearer terrain uses imagery. Workstream B’s globally aligned far macro imagery and overlap blend must replace that visual discontinuity without replacing this mesh.

## Option A versus Option B

Option A preserves head-translation parallax, stereo depth, immutable mesh identity, and a single terrain ownership chain. Reusing its existing renderer adds no horizon renderer or panorama material; the only planned new A texture is the sky cubemap, estimated at 3,757,728 bytes on Android as a 1024-face mipped ASTC HDR 6×6 cubemap.

Option B, a static real-DEM panorama, can produce a smooth silhouette at very low geometry cost, but it removes far-terrain translation parallax and introduces a depth-model boundary against mid-zone geometry. It also adds a renderer, material, and panorama texture. The pre-existing same-source comparison remains external evidence:

- `workstream_a/mountain_ab_reference/mountain_option_a_100m_dem_hillshade.png`, SHA-256 `d5a3989048f665a41e53fd92b94b8a2f20896b92fc34dbca75b9d7abd3ed9387`;
- `workstream_a/mountain_ab_reference/mountain_option_b_dem_horizon_panorama.png`, SHA-256 `deda18e867a1ca760be1e664ea9099332d69e5cb0c478db127743321270eb155`.

Those images document source shape only; they are not substitutes for an integrated stereo A/B run. Option B remains a contingency only if V2 Option A captures show persistent faceting or an unfixable mid/far silhouette discontinuity after the continuous material and haze are applied.

## Selected sky and lighting integration contract

Poly Haven’s CC0 **Kloppenheim 03 (Pure Sky)** was selected after inspecting all three approved official tone-mapped panoramas and current official API metadata. It is explicitly classified as midday, partly cloudy, natural light, and medium contrast. The committed derivative is linear HDR, not a tone-mapped JPEG.

Lead integration should:

1. assign `Kloppenheim03_PureSky_Skybox.mat` to the scene skybox;
2. set ambient lighting to the skybox and update/bake the ambient probe once, never every frame;
3. reuse the same HDR cubemap as the source for static environment reflections, with no realtime reflection probe;
4. retain exactly one directional sun and align it to the photographed sun after skybox rotation is finalized—the derivative’s brightest source location is UV `(0.600342, 0.174316)`, approximately 58.62 degrees above its equirectangular horizon;
5. preserve global linear haze rather than transparent atmospheric geometry;
6. keep broad realtime world shadows disabled unless measured Quest headroom later authorizes a bounded exception;
7. use the photographed clouds as the complete cloud renderer: no volumetric clouds, transparent cloud dome, or billboard field.

The initial sky material uses exposure `1.0` and rotation `0`. These are neutral authoring values, not accepted product tuning. Final exposure, sky rotation, sun transform, ambient intensity, reflection intensity, and fog color must be recorded after full-resolution stereo review.

## Required integration evidence

Run neutral cockpit, slow four-axis head pan, rearward view, taxi/takeoff, pattern-altitude translation, and approach captures after Workstreams A and B are integrated. Reject Option A only if those captures demonstrate a persistent defect after material/fog tuning. The V2 gate must verify:

- no mid/far material or normal seam;
- no cubemap seam in either eye;
- no mountain transform, mesh, or renderer identity change;
- no horizon sliding during head translation;
- photographed sun, terrain light, water reflection, and specular direction agree;
- no new transparent overdraw region;
- physical Quest timing remains within the milestone gate.

Until these captures exist, no sky or mountain visual score is claimed.
