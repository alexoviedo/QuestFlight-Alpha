# Boulder tree realism from the airplane

Status: **implemented September 15; candidate installed, worn visual/performance acceptance pending.** This is the tree amendment to the [unified visual upgrade plan](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md). The user explicitly approved implementation. This consolidated version supersedes earlier catalog-first requirements, per-tree accuracy goals and speculative resource allowances.

Implementation progress and validation results: [September 15 implementation record](TREE_REALISM_IMPLEMENTATION_2026-09-15.md).

## Decision and success criterion

Deliver one map-wide visual correction: connected, substantial broadleaf canopies with believable exposed support around town and waterways, and recognizable conifer forests on wooded foothills. Preserve the accepted canopy coverage, viewing distance and continuity. Judge the result from ordinary seated flight, including banks and side-window views.

The user's requirement is **plausible appearance from the airplane, not a perfectly accurate tree inventory**. Approximate stem locations, tree dimensions and species mixtures are acceptable when consistent with the grove and habitat. Floating foliage, flat panels, obvious repetition, forests in clearings, and disappearing crown shapes are not acceptable approximations.

**Delivery priority: the largest visible improvement in the shortest practical pass.** The next candidate must combine two changes: supported, substantial grove crowns and visibly different foothill conifers. Diagnose only enough to identify and repair the floating-band mechanisms; do not perfect lowland trees before producing the mountain contrast. Use one eligible conifer source to derive both open and tapered variants where its artwork permits. One internally reviewed composition pass precedes all-map generation and the required final validation. No inventory join, additional ecological data acquisition, custom species library, general renderer optimization or close-up bark polish belongs in this delivery. Once the ordinary-flight appearance meets the criteria below, stop refining and prepare the headset candidate.

## Critical assessment of the previous revision

The previous revision corrected the broad technical direction but still overcomplicated delivery:

- Public-tree joins, diameter validation and detailed structural records were still in the required sequence. They do not establish visual realism and are now optional refinements, not dependencies.
- It emphasized individual-tree completeness without sufficiently defining the appearance of the whole grove. More poles under the existing foliage could leave the same flat canopy, now with more geometry.
- Its visual priorities were insufficiently ranked. Canopy mass, silhouette and continuity at normal flight distance should lead; small bark/branch details should follow only where visible.
- It risked polishing close trees before changing the distant mountain forest. Broadleaf repair and conifer appearance must form one visible delivery, with middle/far LODs included from the start.
- It made cost neutrality a target without a demonstrated allocation. That remains an objective to verify, not free capacity or a guarantee. No invented trunk count, added-draw allowance or foliage-triangle subtraction is authorized.

This work can plausibly address the reported defects because it changes their visible structure and regional silhouettes. Code inspection alone has not proved the cause of every floating grove. A short reproduction step is still necessary to avoid treating grounding, selection loss and missing crown geometry as the same problem.

## Existing project evidence

| Inspected condition | What the implementation must address |
| --- | --- |
| `MapBranchGroveRenderer` selects dense foliage cells independently of the individual trees in `AirportCloseTreeLodRenderer`. The latter's near/middle caps are 24/300 instances. | Selected canopy does not guarantee supporting wood. These caps are not counts of trunks actually visible in the user's flight. |
| `ProductionSemanticSurface.shader` suppresses original tree foliage using grove ownership at the tree center. Whole far-tree impostors are included in that foliage classification. | An impostor's pictured trunk and upper outline can disappear while replacement middle foliage remains. Whole-tree coverage must not be inferred from one covered center point. |
| Grove cells use shared branch artwork and a common layered composition, without species identity. | Recoloring these cells cannot create convincing pine or fir crowns. |
| The compiler interpolates grove base heights from neighboring tree bases, accepting support within 80 m. | Slopes can require correction against the actual rendered terrain; exact errors at the reported groves remain unmeasured. |
| The accepted renderer already uses bounded streaming, three tiers, both-eye culling and pose/bank refresh. | Retain this machinery instead of inventing a replacement renderer or promising savings from culling that already exists. |

Current source uses 1,600 m full / 1,800 m fading grove coverage and a 393,216 selected-grove triangle ceiling. Other trees and transition work have separate costs; the ceiling is not the whole vegetation budget. Oversubscription can invoke fallback before the nominal range. Preserve the useful visible result, not just these constants.

Existing imagery was inspected during the original planning pass: the external `map_branch_lod_20260910` pilot views and `ambient_visibility_20260915/worn/flight.png`. Older Editor views have a known cockpit obstruction; the worn flight image does not resolve every stem. These are references, not proof that all reported defects were reproduced or that a repair is accepted.

## Visual priorities

| Order | What needs to read correctly | Sufficient technique |
| --- | --- | --- |
| 1 | Canopy mass and upper outline at middle/far flight distance | Connected overlapping branch clusters, broad height variation and irregular crown tops; retain natural openings and visible conifer profiles. |
| 2 | Depth and grounding in oblique views | A believable crown base, a limited but convincing population of tapered stems and a few primary forks reaching into foliage. |
| 3 | Regional identity | Spreading broadleaf groves near town/water, irregular open pines on dry slopes, fuller tapered conifers in cooler wooded patches. |
| 4 | Stable appearance in motion | Consistent coverage, family and crown height across LOD/streaming, shared stereo decisions and no abrupt loss during bank/head motion. |
| 5 | Material coherence | Branch-scale holes, restrained color variation, darker undersides/junctions and lighting consistent with the ground imagery. |

Use three base crown structures: the accepted broadleaf branch style, open ponderosa-like pine, and tapered Douglas-fir-like conifer. Broadleaf scale, crown-base and branching variants can suggest cottonwoods, willows, street shade trees and smaller ornamentals without separate species meshes. Preserve shrubs.

Natural variation should be spatially coherent: neighboring trees form a stand, with a few height/shape variants, not random colors and extreme scales on every cell. A grove must remain cohesive while its upper edge and exposed side have depth. Do not carve a visible gap around every structural anchor.

Trunks should appear where they would resolve through crown gaps or beneath exposed edges. They need not remain individually visible at long range or beneath a canopy viewed from above. Avoid a uniformly spaced ring of trunks around the grove, which would read as a fence. Use irregular depths and spacing within the footprint, keeping stems rooted and branch junctions inside foliage.

No individual leaf/needle geometry, high-resolution bark, roots with microscopic accuracy, exact cultivar models, new vegetation wind, or new real-time tree-shadow/reflection system is required. Avoid dark vertical curtains and painted ground shadows used to disguise missing structure. Existing lighting and modest baked/vertex depth cues are the initial approach.

## Habitat guidance, without a new survey

The existing research is enough to select a plausible palette:

| Map context | Visual assignment |
| --- | --- |
| Dry sunny wooded foothills | Ponderosa-like open pines, irregular spacing, visible rocky/grass openings. |
| Cooler north-facing woods and protected ravines | A higher proportion of fuller tapered fir-like crowns, mixed with pines. |
| Supported creekside and wet-margin vegetation | Larger spreading broadleaf crowns and some lower, branching/multi-stem willow-like forms. |
| Streets, parks and developed neighborhoods | Varied broadleaf shade/ornamental forms, with occasional planted conifers. |
| Grassland, rock, runway, roads, roofs, open water and existing clearings | Preserve exclusions and open space. Do not turn every green area or mountain into forest. |

Boulder's Mountain Parks plan supports the dry-slope ponderosa / cooler-slope Douglas-fir distinction. Apply it as a habitat tendency, not a surveyed species boundary. [City habitat plan](https://bouldercolorado.gov/media/2497/download?inline=) Boulder Creek research documents cottonwood and willow communities. [USGS study](https://www.usgs.gov/publications/constraints-establishment-plains-cottonwood-urban-riparian-preserve)

The City's public inventory documents a diverse planted urban population and remains background reference. Point joins, exact diameter matching and reconciliation of current records with older imagery are **deferred out of this delivery**, including bespoke landmark-tree matching. [Inventory schema](https://gis.bouldercolorado.gov/ags_svr2/rest/services/parks/TreesOpenData/MapServer/layers)

Use current canopy masks, terrain/aspect, known wet corridors and the existing reference imagery to assign broad zones offline. Blend mixtures at stand boundaries; do not switch species per terrain pixel or use a hard altitude boundary. Existing masks remain the occupancy authority unless a visible error is demonstrated. Exact habitat polygon acquisition, new raw LiDAR, ML species recognition and further aerial benchmarking are not prerequisites. Keep approximate/source-derived distinctions and asset license records, without turning confidence scoring into a separate project.

## Implementation after approval

### 1. One bounded diagnosis and a complete visual example

Preserve the current source/build identity and accepted foliage appearance. Reuse existing evidence and tools. Select one exposed lowland grove and one wooded slope in ordinary pilot views inside the flyable boundary. Record the relevant grove/tree IDs and pose only as needed to reproduce the issue.

Inspect root height, selected wood, ownership, crown base/top and alpha/mip behavior in those same views. Use temporary diagnostics to isolate the missing contribution, then judge normal rendering. Avoid another map-wide research pass or requiring the user to locate exact trees.

Repair demonstrated terrain and ownership errors in the current code. Ground structural anchors using the authored `ProductionTerrainSurfaceAsset`, including transition patches. Coordinate foliage and wood selection so a replacement cannot discard the visible support or upper crown it has not supplied. Do not restore every whole original tree on top of the new groves; retain only needed wood/crown parts and suppress valid replacements.

Use current anchors first. Where support is visibly missing, add deterministic approximate anchors within the existing footprint using crown size and irregular spacing. Neither one anchor per foliage cell nor a fully measured stem database is necessary. A structural record needs enough information for grounding, crown envelope, family and stable ownership; avoid a wholesale schema/renderer rewrite.

### 2. Compose the finished appearance at flight distance

Refine broadleaf branch placement to preserve connected canopy mass while breaking the flat upper/lower bands. Keep branch-scale cutouts and consistent texel scale. Place exposed wood and major forks into the crown volume, not merely under a foliage sheet. Fix silhouettes and density distribution before adding finer textures.

Add the two conifer structures using compact needle-bearing branch artwork and appropriate vertical branch placement. Audit eligible local sources, then obtain or author only the missing conifer source; keep raw high-poly assets external and bake runtime derivatives with rights records. Do not import a full forest system or make user downloads a planned dependency.

Build all LODs from the same composition. Retain simple 3D wood where exposed and resolvable; use the existing complete-tree impostor machinery for sufficiently distant support and shape. The grove and individual-tree paths must agree on crown height/family through their overlap. If a simplified tier makes trees float or changes a pine into a broadleaf shape, it is incomplete even if it saves triangles. No new grove shell/impostor renderer is in scope.

Inspect normal forward/side-window flight and banks, at middle/far range first and then closer oblique range. Keep diagnostic stills for debugging, but the internal milestone requires an obvious improvement in ordinary moving stereo views. The two examples are engineering checks within one delivery, not separate user approval rounds.

### 3. Apply the same result across the map

Assign the broad habitat zones and variants, extend existing compact page records only where required, and generate the correction across all 1,296 pages. Retain existing component continuity, foliage occupancy/exclusions and the current distant fallback. Stream compatible wood/crown/family records together; missing or late data must retain a complete fallback.

Verify ordinary startup and Free Flight enable the change globally. Keep the accepted 1,600/1,800 m range and visible mass through cap saturation as well as nominal LOD changes. Full-map availability does not require rendering everything behind the pilot or outside the current view.

Use existing wider map observation sites to check seams, empty pages and habitat variation. Keep aircraft positions within the actual recovery boundary while retaining the outer scenery margin in the data. Do not turn these checks into a new exact-species or canopy-boundary survey.

### 4. Control cost throughout; validate the completed candidate once

Rendering cost is part of asset composition, not a late optimization project. Reuse materials, small branch artwork, instancing, shared LOD decisions, cached selection and bounded residency. Prefer simple opaque wood to extra leaf layers when it supplies the missing visual cue. Remove fully replaced legacy parts when compatible with batching; fading alone does not remove submitted geometry.

The allocation order is: preserve visible crown silhouette/coverage, supply exposed support, then spend any remaining capacity on close detail. Do not thin the whole grove or shorten range to fund trunk detail. Hidden interior wood may be omitted using conservative visibility/occupancy estimates; porous foliage cannot be assumed to be an opaque occluder. No new per-tree raycasts, GameObjects, colliders, `Update` loops or hard bank/window visibility masks.

Record total vegetation geometry/submissions, overlapping tiers, resident texture/buffer memory and selection/upload costs at the affected and existing broad-map views. Derive explicit implementation caps from this evidence; the previous speculative extra-trunk/draw/memory allowances remain withdrawn. Keep High FFR and resolution fixed. No claimed percentage gain or GPU neutrality follows from the 393,216 grove triangle cap. Meta likewise emphasizes silhouette preservation, complete frame budgets and foliage overdraw. [Meta art/performance guidance](https://developers.meta.com/horizon/design/art-and-performance/)

Target no material deterioration of current frame cadence or slow frames. Judge any added wood or atlas against the integrated cost rather than assuming it is affordable. If a detail exceeds the allocation, simplify that detail or the redundant work it replaces while preserving the accepted image. If no satisfactory fit is demonstrated, report the specific tradeoff; do not quietly accept a performance regression or begin unrelated optimization.

## Established process and lessons that remain binding

Follow [AGENTS.md](../AGENTS.md), [validation](VALIDATION.md), [simulator workflow](META_XR_SIMULATOR_WORKFLOW.md) and current checked-in gate definitions. Historical prose contains older counts/limits; do not select a looser historical threshold. This feature does not change aircraft physics, tracked camera ownership or the authored single-XR product hierarchy. Runtime streams data and submits instances; it does not construct product hierarchies.

| Lesson / required step | Application here |
| --- | --- |
| Sparse solid foliage and abrupt tree LOD swaps failed. [Tree V2 evidence](evidence/AIRPORT_TREE_IMPOSTOR_V2_2026-07-28.md) | Retain branch cutouts, sufficient crown mass, projected-size LOD and hysteresis. |
| Shells looked like rocks; photographic/angular sheets exposed panels and lifted roads. [Earlier failures](evidence/ROUNDED_CANOPY_VOLUME_V1_2026-07-20.md), [unified history](VISUAL_FIDELITY_UPGRADE_PLAN_2026-09-06.md) | No solid roofs, giant stretched cards, curtains or new ground-photo projection; preserve accepted masks. |
| Small isolated changes were missed; map-wide coverage and longer range earned visible improvement. [Rollout](MAP_BRANCH_GROVE_ROLLOUT_2026-09-10.md), [distance](CANOPY_DISTANCE_LOD_2026-09-10.md) | Deliver the complete map in ordinary flight, with middle/far quality included. No diagnostic-only island or repeated user hunt. |
| Build/import overlap exhausted disk; content/state mismatches invalidated tests. | Preserve source and existing user edits, check disk before large jobs, reuse external stores and run large builds sequentially. |
| Standard product checks are required. | Focused checks during development; run `scripts/validate_product.ps1` when the integrated candidate is complete. Reuse its integrity, dependency, EditMode/PlayMode, Visual QA, static-budget and canonical Android build outputs; inspect child failures, avoid duplicate runs. |
| Menu-only, obstructed or offset-eye images did not prove the feature in tracked stereo. | Use the existing simulator gate/runner with correct platform bundles; inspect full-resolution eye images and normal-flight transitions. Do not move the tracked Main Camera or substitute a contact sheet for relevant image inspection. |
| A successful build did not prove the intended data reached the headset. | Verify all packaged payloads, source/manifest identity, installed APK hash and normal-launch settings. Clear temporary diagnostics before presenting. |
| Unmatched resting-headset measurements did not establish gains. [Performance evidence](FFR_CLOUD_RECOVERY_2026-09-10.md) | One final prepared worn session after offline completion. Use normal flight, existing physical gates and comparable settings/thermal state; keep captures outside timing windows. Simulator speed is not Quest performance. |

No intermediate worn requests. Present clear landmarks and visible changes at the final session; no side-by-side assignment. Reuse existing departure/creek/foothill paths and normal banks. No need to test inaccessible exterior views for artistic acceptance, although general product integrity tests still apply.

The current candidate has inherited machine-QA/performance failures. Preserve actual failure status and thresholds. Missing genuine simulator replays stay missing; do not fabricate evidence or label a partial run a full pass. Short worn feedback does not establish sustained thermal acceptance. No unrelated campaign to attain native 72 FPS is a prerequisite for completing the visual correction, and no release-performance claim is made.

## Definition of done

- From an ordinary seated flight, broadleaf groves read as connected trees with depth and plausible support where visible, not foliage sheets or poles under a flat band.
- Wooded foothills read recognizably differently from lowland/urban vegetation at useful middle/far distance. Plausible regional mixtures are sufficient; exact species, trunk counts and precise original-tree matching are not acceptance gates.
- Normal Free Flight uses the correction across eligible pages. Coverage, natural gaps and the accepted visible range remain intact.
- Head movement, banks, LOD overlap, streaming and allocation pressure introduce no new floating support, popping or eye disagreement.
- Required inspected visual/build evidence and cost results are recorded honestly against the retained checkpoint; current failures are not erased.

Stop local iteration when these visual criteria are met. Do not keep refining botanical accuracy, invisible branches or minor bark differences. If the first normal-flight examples remain subtle or wrong, fix the visible composition before all-map expansion or another headset presentation. If unsupported appearance persists despite corrected grounding/ownership, change the branch/crown composition rather than adding more trunks by default.

Maintain one reviewable feature with reproducible sources, licensed derivatives and paired Unity metadata; keep raw data, builds and captures outside Git. The complete sequence through a prepared map-wide candidate is authorized. Visual/build and physical performance results must be recorded separately as they become available.
