# Quest Environment Reconstruction V2 evidence — 2026-07-17

Outcome: **PARTIAL — PUSH FEATURE BRANCH ONLY**. Main is not merged.

## Baseline and scope

Alex's worn V1 checkpoint score was 4/10. Reported defects included a slightly close default eye point, mountain aliasing, ground shimmer/transitions, synthetic single-location water, unrealistic runway/buildings, missing context, repeated trees, and badly oriented/grounded parked aircraft. V2 changed only the bounded standalone Quest environment; physics, JSBSim, input, cockpit remodeling, PCVR, and world expansion were excluded.

## Integrated implementation

- One fixed ENU terrain authority and `IProductionTerrainSurface` contract; serialized terrain, parking-stand, and placement assets are referenced by `ProductionVerticalSlice` through the authored environment prefab.
- Public-domain USGS 3DEP 1 m source reduced to a 5 m airport grid and 25 m near grid, plus a 1024² normal map. New runtime terrain estimate: 128,000 triangles. Measured airport/near average errors: 0.0407/0.2117 m; maxima: 3.7056/14.1289 m; sealed boundary max: 0.008 m.
- 28,336 placement entries: 27,679 buildings, 377 vegetation instances, three parked aircraft, and 277 represented water features. Building foundation median/p95/max: 0/0/0 m. Parked-aircraft maximum gear error: 0.014 m; heading error: 0°.
- OSM building representation: 1,831/1,831 at 0–2 km, 7,863/7,863 at 2–4 km, and 17,985/17,985 at 4–6 km.
- Natural-color NAIP fallback masks and four project-owned vegetation species; 377 validated instances and no camera-facing billboards.
- All 800 bounded OSM water candidates classified: 277 represented and 523 excluded with reasons. Boulder Reservoir is retained; other eligible features are batched into shared surface/bank meshes.
- Built-in Forward retained. Offline deterministic HLOD uses 20 building, four vegetation, and one parked-aircraft group, each with three tiers; no URP migration, Nanite clone, runtime combining, crossfade, or GPU-occlusion claim.

## Validation

- `validate_product.ps1`: PASS after one exact dependency-allowlist correction for the three new environment scripts.
- Unity EditMode: 61/61 PASS. Unity PlayMode: 9/9 PASS.
- Production Visual QA: 24/24 machine checks PASS; full-resolution images were inspected by the lead.
- Static planning: 122/130 draw calls, 609,168/650,000 visible triangles, 25/30 visible materials, 164.96/220 MiB textures, two transparent slots, 4x MSAA, 171 active renderers.
- Android release APK: 94,948,436 bytes; SHA-256 `fbc04f7b5199b0172e78221b80c84c6dd77db043a2c8c270754d2f0457aa8e6d`.
- `run_current_environment_reconstruction_qa.ps1`: FAIL CLOSED because its adapter selected stale intermediate surface/placement/vegetation/water/HLOD/budget reports rather than the final Unity artifacts. This is an evidence-wiring blocker, not a visual pass.
- Meta XR Simulator: NOT RUN for V2. Physical Quest timing/human review: NOT RUN for V2. The lead visual prerequisite failed, so Alex was not asked to repeat the rejected baseline checklist.

## Lead visual scores

| Category | V1 human baseline | V2 lead review |
| --- | ---: | ---: |
| Near terrain | 4 | 5 |
| Terrain geometry | 4 | 4.5 |
| Ground integration | 4 | 5 |
| Building quality | 3 | 4 |
| Building coverage | 3 | 8 |
| Vegetation quality | 2 | 3.5 |
| Aerial canopy | 2 | 3.5 |
| Water | 4 | 3 |
| Airport/parked aircraft | 2 | 5.5 |
| Runway integration | 4 | 3.5 |
| Overall environment | 4 | 5.2 |

Coverage, grounding, and static architecture improved materially. Visual acceptance did not: the runway's alternating block pattern, segmented dark secondary-water ribbons, grey building carpet, repeated pale vegetation, faceted mountains, and terrain/imagery transitions are prominent in full-resolution views. No critical-category-below-6 requirement or overall 7/10 gate is met.

## Sources and licenses

USGS 3DEP and USDA/USGS NAIP derivatives are public-domain United States government works. OSM building/water data is ODbL-1.0 with `© OpenStreetMap contributors`. Vegetation geometry is project-owned. Parked-aircraft geometry remains the previously audited KOG_THORNS CC BY 4.0 derivative. Exact hashes and raw-data boundaries are in `docs/ASSET_LICENSES.md` and the ReconstructionV2 source manifests. Raw downloads and build/capture artifacts remain outside Git.

## Decision and next milestone

Preserve and push `feature/environment-reconstruction-v2`; do not merge main and do not claim Quest performance. The exact next standalone milestone is a bounded visual-coherence repair of three defects only: replace the runway block artifact, remove/rebuild the secondary-water ribbon geometry, and replace the grey-building/pale-vegetation presentation with a small shared-atlas close/mid representation while retaining the terrain/placement/HLOD manifests and current budgets. Headset interaction is not required until a new full-resolution lead review reaches 7/10 and simulator stereo passes.
