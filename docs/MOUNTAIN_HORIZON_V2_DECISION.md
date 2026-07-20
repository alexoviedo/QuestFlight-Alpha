# Mountain and Horizon V2 Decision

## Decision

Retain **Option A: the fixed USGS-derived far DEM mesh** as the only far-mountain geometry. Integrate the continuous far orthophoto from Workstream B and the selected HDR sky from Workstream A onto the existing world-anchored architecture. Do not add a horizon panorama to the product scene at this stage.

This is now the integrated product architecture. The lead serialized Option A, the far orthophoto, the HDR sky, photographed-sun alignment, ambient/reflection settings, and haze into `ProductionVerticalSlice` and inspected all 24 full-resolution Visual QA views plus the final Meta XR Simulator neutral stereo pair. It is not a photoreal visual acceptance: the independent mountain score is `6.3/10`, below the `7.0` target.

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

Those images document source shape only; they are not substitutes for an integrated stereo A/B run. The required genuine recorded Meta XR Simulator A/B replay was not completed because the Windows computer-control handoff failed and no `.vrs` recording may be synthesized. Option B therefore remains an unverified contingency. This missing A/B evidence is an explicit milestone blocker and one reason the result is `PARTIAL`, not a basis for claiming Option A visually superior.

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

The inspected product tuning uses sky exposure `0.74`, rotation `0`, sky ambient intensity `0.86`, reflection intensity `0.76`, one directional sun at intensity `1.0`, and water environment reflection strength `0.78`. The serialized scene and final Visual QA provide the authoritative transforms and fog color.

## Required integration evidence

The neutral Meta XR Simulator live stereo capture passed its API/stereo checks, but the required neutral, slow four-axis head pan, rearward, taxi/takeoff, translation, and approach recordings remain missing. A future V2 gate must verify:

- no mid/far material or normal seam;
- no cubemap seam in either eye;
- no mountain transform, mesh, or renderer identity change;
- no horizon sliding during head translation;
- photographed sun, terrain light, water reflection, and specular direction agree;
- no new transparent overdraw region;
- physical Quest timing remains within the milestone gate.

The 24-view Editor capture set scored sky/clouds `7.8/10` and mountains `6.3/10`, but Alex's later worn Quest 3 review found visible mountain-ridge aliasing and rejected the overall environment at `4/10`. The fixed DEM remains the serialized architecture, not a visually accepted solution. No recorded-replay stereo stability or physical Quest timing claim is made; the three-run timing gate was intentionally not started after the human visual rejection.
