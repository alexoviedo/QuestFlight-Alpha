# Aerial Scenery Recovery V2 evidence — 2026-07-19

## Disposition

**PARTIAL — preserve and push the feature branch; do not merge to `main`.**

The recovery materially corrects the catastrophic floating-roof and scale-changing context-building representation, removes synthetic Tier-2 canopy geometry, reallocates public-domain imagery for the pilot's normal flight ranges, and defers rejected water effects. It does not achieve photorealism or the requested aerial-canopy quality. Lead review of every full-resolution image is 5.3/10.

## Source and scope

- repository: `C:\Users\ovied\Dev\T2\T2-QuestFlight`
- source branch/SHA: `feature/aerial-scenery-representation-v1` / `6ff04fc9a1dc636030e28ed759b31539ee637396`
- delivery branch: `feature/aerial-scenery-recovery-v2`
- shipping target: standalone Meta Quest 3 only; Android ARM64, Vulkan, OpenXR, IL2CPP, ASTC, 4x MSAA
- external artifact root: `C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\aerial_scenery_recovery_v2_20260719_131303`
- pre-change safety bundle: `pre_aerial_scenery_recovery_v2.bundle`

No runway, reservoir polish, sky, cockpit art, aircraft rendering, physics, JSBSim, input, PCVR, or world-extent feature work was started. The only cockpit change is a bounded 5 cm nominal seat shift responding to the worn firewall-distance report.

## Building representation

The V1 representation assigned mid-distance records to roof-only and scale-changing geometry. Recovery V2 covers all 27,679 bounded OSM source features exactly once:

- 26 hero and 1,152 near records with original footprints, plus 1,697 mid records with bounded footprint simplification: 2,875 fixed closed footprint-preserving shells total;
- 24,804 ordinary records: orthophoto only;
- 16 renderers, 46,005 triangles, one opaque shared material;
- zero runtime building LODGroups and no per-building `Update`;
- wall/foundation plus flat, gable, and hip roof support;
- no lidar height overrides because qualified vertical confidence remains absent.

The shells reuse the existing rights-cleared AirportCore hangar-kit atlas triplet and add no unique texture bytes. The project-authored shell shader uses explicit gradients and fades unstable far microdetail. Python building tests pass 9/9; scoped Unity building tests pass 4/4.

Visible result: widespread floating roofs are gone and static footprint scale is stable. Remaining buildings are too plain, lack facade/roof detail, and transition abruptly to the orthophoto. Building geometry/material scores are 4.0/3.5.

## Vegetation and canopy

- Tier 0: 3 placements;
- Tier 1: 19 placements;
- two draws, 12,860 selected triangles, one alpha-clipped material;
- Tier 2: zero geometry and zero draws;
- Tier 2/3: orthophoto plus lidar canopy normal/occlusion shading within local X/Z `[-2048,+2048]` m;
- no ordinary billboard rotation, crossfade/dither, per-tree GameObject, or per-tree update.

Python vegetation tests pass 7/7; scoped recovery Unity tests pass 5/5. Of eligible canopy pixels, 99.68% remain nonzero: 276,906 total, including 273,076 fully map-owned and 3,830 transition pixels.

Visible result: the synthetic dark/green canopy mounds are removed, but the few near trees remain prop-like and no convincing grove volume is visible. Near vegetation/grove/aerial-canopy scores are 2.5/1.5/4.0.

## Ground imagery

The source is the official public-domain USGS/USDA `USGSNAIPPlus` ImageServer, 2019 acquisition, 0.6 m source imagery. No newer official KBDU response was available during the bounded acquisition. Raw downloads and decoded intermediates remain external.

Committed optimized derivatives:

- four 2048² hot quadrants at 1.984375 m/texel, ASTC 6×6;
- one 4096² 12 km fallback at 2.9296875 m/texel, ASTC 10×10;
- mipmaps on; automatic streaming off because the custom world-space UV path has no proven streaming contract;
- near-to-quadrant blend: 1,800–1,980 m;
- quadrant seam crossblend: 32 m;
- quadrant-to-fallback blend: 3,800–4,000 m;
- fallback-to-far blend: 5,000–6,000 m.

Ground-contract Python tests pass 7/7. Pattern-altitude imagery improves to 6.0/10, while taxi/low-oblique imagery remains soft at 3.5/10. No AI upscaling or Google-derived source is used.

## Water defer

`ProductionWaterPresentationMode` is `OrthophotoOnly`. Reservoir and shore renderers are inactive with zero material slots; the benchmark and manifest reservoir bindings are null; the minor-water terrain effect is inactive. Licensed source meshes, masks, normals, shaders, and manifests remain preserved but unbound for a later polish milestone.

This removes the rejected gray suppression/synthetic water presentation and costs no active water draw. The result is stable but intentionally flat and scores 4.5/10.

## Seat and release-safety corrections

- nominal eye Z changed from `-0.18` m to `-0.23` m; panel distance changes from approximately 0.497 m to 0.547 m;
- OpenXR still exclusively owns the tracked Main Camera; calibration still changes only the seat-relative offset;
- the airport-core benchmark validator now fails closed without throwing when optional probe-position data is absent in a release player.

Both changes are covered by EditMode/PlayMode validation. The seat adjustment requires post-change worn confirmation.

## Visual QA

The lead inspected all 56 production Visual QA images and all 20 dedicated aerial-recovery images at full resolution.

| Category | Score |
|---|---:|
| Building grounding/completeness | 7.0 |
| Building footprint/spatial fidelity | 6.5 |
| Building geometry | 4.0 |
| Building materials/lighting | 3.5 |
| Building LOD continuity | 5.5 |
| Near vegetation | 2.5 |
| Grove grouping | 1.5 |
| Aerial canopy | 4.0 |
| Near ground imagery | 3.5 |
| Pattern-altitude imagery | 6.0 |
| Scenery transitions/coherence | 5.5 |
| Water | 4.5 |
| Mountains/horizon | 6.5 |
| **Overall** | **5.3** |

Evidence:

- production contact sheet: `visual_qa_final/visual_qa_contact_sheet.png`
- dedicated aerial contact sheet: `aerial_qa_final_thresholds/aerial_scenery_before_after_contact_sheet.png`
- review: `final_visual_review.md`

Machine Visual QA is `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; that checks image/integrity contracts and does not override the lead visual result.

## Static planning budget

| Metric | Result | Ceiling |
|---|---:|---:|
| Estimated selected draws | 55 | 120 |
| Maximum visible triangles | 534,976 | 600,000 |
| Maximum visible materials | 24 | 25 |
| Estimated texture residency | 176.77 MiB | 180 MiB |
| Transparent material slots | 2 | 3 |
| Shaders / keyword combinations | 11 / 17 | controlled |
| MSAA | 4x | 4x |

This is Editor/static planning evidence only. It does not establish Quest GPU/CPU timing, allocations, thermals, stale frames, tears, or comfort.

## Simulator

Meta XR Simulator v201.0 built and ran the external Windows/OpenXR QA player without changing shipping Android settings. The Meta Quest 3 / 72 Hz request connected through Meta OpenXR; HMD and both Touch Plus devices were tracked. The live cockpit-neutral machine session passed, and both 1440² eye images were inspected as nonblank and scene-consistent.

Aggregate result: `BLOCKED_MISSING_RECORDED_REPLAYS`. All ten genuine `.vrs` recordings are absent: cockpit neutral, head pan, rear view, viewpoint calibration, control sweep, taxi, takeoff roll, rotation/climb, shallow turns, and final approach. No replay, transition-motion, or simulator performance acceptance is claimed.

- summary: `simulator_gate/meta_xr_simulator/visual_gate/simulator_visual_summary.md`
- stereo contact sheet: `simulator_gate/meta_xr_simulator/visual_gate/simulator_stereo_contact_sheet.png`

## Product validation and APK

`scripts/validate_product.ps1` passes:

- standalone policy tests: 9/9;
- product integrity and dependency closure: PASS;
- EditMode: 98/98;
- PlayMode: 9/9;
- 56-view machine Visual QA: PASS;
- static Quest planning budget: PASS;
- Android release-style build: PASS.

APK:

- package: `com.alexoviedo.t2.questflight`
- scene: `Assets/Scenes/ProductionVerticalSlice.unity`
- external path: `QuestFlight/Builds/Android/QuestFlight-production.apk`
- bytes: 106,789,590
- SHA-256: `e97292bc1e060caf7e924998faa0c4c3939a36835cb1beb67e8a2b5a74ff9933`

The validation summary's frozen-source `PASS_1_REJECTED_2_UNKNOWN` result is the expected R&D extraction fixture. The product dependency closure is separately PASS; no rejected or unknown R&D dependency entered the product.

## Physical Quest evidence

The exact APK above was installed and cold-launched on one authorized Quest 3. The application acquired PID `26718`, and the authored production environment readiness marker reported 42 environment renderers, 360,505 triangles, nine materials, 4.7 mm runway gap, and 1.0 mm marking gap.

Alex's short worn smoke result:

- default seat: PASS; no backward movement required;
- floating roofs / building size or shape popping: not observed;
- grove/canopy cue: visible and accepted in the worn view;
- ground imagery: still blurry looking right while flying over the reservoir, but looked decent to the left;
- orthophoto-only water: preferred over the previous effects;
- overall worn visual score: **7/10**.

Artifact screenshot: `quest_quick_smoke/questflight_recovery_smoke.png`, SHA-256 `5e041e58c4703056781485905430200495330a17f6dacee52102a8bc60266f52`.

No 90+60-second timing run was started, and no FPS, frame-time, thermal, stale/tear, allocation, or sustained-comfort acceptance is claimed. The worn score does not override the 5.3/10 full-resolution lead audit; the two reviews observe different presentation conditions and both are retained.

## Remaining blockers and decision

The recovery is safe and coherent enough to preserve, but it is not close to AAA or literal photoreal quality. The highest-value next milestone is a bounded **near-airport scenery quality benchmark**: prove one detailed context-building family and one dense aerial-stable grove/upper-canopy representation across taxi, 100 m, and 300 m AGL views while holding the current budget. Reuse the fixed footprint-shell pipeline, public-domain imagery, canopy maps, shared vegetation family, immutable mountains, and current validation harness. Do not repeat broad world rollout, water polish, renderer migration, or another sparse individual-tree scatter.
