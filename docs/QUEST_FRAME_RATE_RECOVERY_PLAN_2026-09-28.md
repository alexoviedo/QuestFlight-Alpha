# Quest frame-rate recovery while preserving the accepted image

**Status: approved by the user; implementation in progress.** The implementation
and measured outcomes are tracked in
[the recovery evidence](evidence/FRAME_RATE_RECOVERY_2026-09-28.md). Offline
geometry/CPU results are not Quest performance acceptance.

**Revised after the user's challenge: make distant groves cheaper to represent,
as well as cheaper to select.** Keep the accepted nearby foliage, dark mountain
tree silhouettes, canopy coverage and head-turn continuity. Promote a bounded
distant-cluster LOD experiment to the main work, immediately after a short
bottleneck check. Do not spend a full milestone polishing per-cell selection
before examining whether those cells need to be submitted at all.

The earlier sequencing was too conservative about the representation and too
broad about secondary optimizations. Faster hash lookups cannot remove expensive
foliage fragments or tiny triangles. Conversely, an impostor is not automatically
faster: texture memory, masked overdraw and view blending can consume the saving.
The proposal below requires a measured net win before map-wide generation.

This is a performance proposal, not a new canopy art direction. Bake the current
accepted three-dimensional branch foliage; reuse existing connected footprints,
terrain heights and tree families. No new canopy detection campaign, orthophoto
sheets, solid green shells, engine migration or new flight features.

The previous check established selection continuity and unchanged maximum grove
triangle counts. It did **not** establish unchanged Quest frame time. That was an
insufficient performance gate: the user now reports an unacceptable regression.

Keep two problems separate: the new regression and the older cost of the accepted
long-range forest. Dense mountain triangle totals barely changed in the recent
fix, so excess geometry alone is not an established explanation for the new
slowdown. A small selection correction may be the fastest recovery; cluster LOD
is the more substantial opportunity for additional headroom. The first diagnostic
pass determines their order instead of assuming either cause.

**What the evidence establishes**

Read the actual renderers, shaders, authored settings, production build manifest,
September 6–10 performance reports, and September 28 head-turn reproduction data.
Retrieved existing logs from the connected Quest without launching, installing,
changing settings, or requesting a worn test.

The user clarified that slowdown is **worst in the mountains but not great
anywhere**. Prioritize dense mountain views, then compare airport and sky-heavy
views to distinguish foliage cost from shared frame overhead. This does not
establish whether the slowdown persists with a stationary head.

| Finding | Evidence and interpretation |
| --- | --- |
| Severe slowdown is present in available app telemetry. | Two surviving VrApi samples for app PID 15402, September 28 13:26:59–13:27:00 local time, report **28/72 and 42/72 FPS**, 47/41 stale frames, Fov=2, GPU utilization 89%/84%, and PLS=0. They are only two seconds, with no synchronized pose or mode record; they are not an average flight benchmark or proof of a particular bottleneck. Other process samples were excluded. |
| The latest fix added CPU work in a frame-critical callback. | `MapBranchGroveRenderer.Select` now performs a hash lookup for every eligible cell, clears/repopulates a retained-cell hash set, and walks all candidates when emitting. It runs from `Camera.onPreCull`, triggered by time, translation, or approximately 2.56 degrees of orientation change. The old emission loop could stop when its tier was full. |
| The latest fix widened both foliage drawing regions. | Grove projection guard changed from .90 to .85; individual trees gained a .90 guard. Actual stored 81-view data show mean grove triangles rising **283,966 → 291,187 (+2.54%)**. Most uncapped stations rise roughly 3–6%; dense station 6 stays at 393,216 and station 7 is almost unchanged. This does not explain a large slowdown by itself. |
| Constant triangle counts do not mean constant GPU cost. | Retaining a different mixture of near/far cells and removing the budget-edge alpha fade changes screen coverage and occlusion. Groves are double-sided, alpha-tested, alpha-to-coverage foliage. More opaque coverage can either add work or help depth rejection; the net effect requires an actual GPU profile. |
| A large CPU-to-GPU update already happens on every grove refresh. | The code clears and uploads a **1,764 × 1,764 R8 ownership texture: 3,111,696 bytes**, plus 48 bytes per submitted cell. At 55,000 cells this is about **5.75 MB per refresh**; 15 refreshes/s would be about 86 MB/s of payload, not a measured bandwidth limit. Upload calls can also wait on the driver. |
| Head-turn CPU cost existed before this regression. | The September 10 report measured gather/fill/buffer/ownership means of 2.781/1.455/.242/.432 ms per selection at 14.99 selections/s. Those are old, differently posed device observations, not current timings. They identify an already expensive path worth profiling. |
| Streaming is already implemented. | Imagery uses predicted high/medium 3×3 windows and a global fallback; buildings use bounded streamed regions; groves use 49 resident 672 m pages. Grove drawing remains bounded to 1.8 km and at most three draws. Residency and submitted visibility are already distinct. |
| Cloud sampling LOD is already on; useful whole-renderer culling is limited. | `_CloudSamplingLod=1` uses maximum 48/24/12 sample budgets. `BoulderCloudField.AdvanceWind` assigns the combined cloud renderer **48,000 × 20,000 × 48,000 m bounds** to accommodate shader wrapping. It therefore cannot reject individual offscreen formations at the renderer level. GPU clipping still rejects offscreen triangles; this does not mean every cloud pixel is shaded. |
| Several standard optimizations are already in place. | Shipping build is non-development IL2CPP ARM64, Vulkan, single-pass instanced stereo, 4× MSAA, Built-in rendering. Authored FFR is **Medium**, with Android subsampled layout enabled. The later user-requested Medium setting supersedes the older High-FFR report. Needle artwork already has mipmaps and alpha-coverage preservation. |
| There is additional release-path CPU/I/O work to examine. | The current session wrote 912 input trace records. `OvrConcurrentInputTrace.WriteLine` performs file metadata checks and `File.AppendAllText` per record. Individual trees rebuild active/draw lists in the camera callback. These are verified work paths, not yet proven dominant costs. |

The current logs do not contain grove-phase counters or direct GPU stage timing
for the reported slow flight. They cannot establish the exact contribution of
CPU selection, fragment work, upload stalls, streaming, system overlays, or
thermals. PLS=0 in two samples does not certify sustained thermal behavior.
`CPU&GPU` is not GPU-only time; the VrApi `ASW` line with `PTWFromStereo` does not
establish that application-generated motion-vector SpaceWarp is enabled.

Local evidence: sibling `T2-QuestFlight-product-artifacts/performance_plan_20260928/`
contains the retrieved logs and samples. The numerical before/after comparison
uses `tree_head_turn_20260928/baseline.json` and `fixed.json`. See also
[the head-turn evidence](evidence/TREE_HEAD_TURN_POPPING_2026-09-28.md) and
[the earlier device measurements](FFR_CLOUD_RECOVERY_2026-09-10.md).

**Do we need so many individual trees? What the current renderer really draws**

The expensive grove system is already batched into at most three procedural
draws. It does not create one GameObject or draw call per tree. It draws small
branch patches on a **2.667 m grid**, using 12, 6 or 4 triangles per patch.
Even the far tier still submits every selected patch separately. Dense station
6 averages **55,255 selected patches and 393,216 triangles** in the recorded
nine head-turn views; station 7 averages 59,087 patches and 392,937 triangles.

The separate individual-tree renderer has caps of 24 close trees, 300 middle
trees and 3,792 far instances; the far tree impostor itself is already only two
triangles. It also supplies visible stems and upper crowns while groves replace
middle foliage. Start by consolidating grove patches. Removing this whole tree
path would risk the floating canopies and missing crowns already fixed.

The source manifest contains **12,407,662 branch cells, 361,157 connected
components and 1,296 pages**. Each record already stores its global component ID
and broadleaf/open-pine/tapered-fir family. Runtime currently validates the
component ID without retaining it in `Cell`; the offline compiler can use it
directly. These labels are reusable segmentation, not proof of botanical accuracy.

A new read-only count used existing page bytes around the authored mountain
station at x=-10,180, z=-300. Grouping by 32 m world block, connected-component ID
and family gives:

| Horizontal distance band | Source cells in the full surrounding ring | Groups containing at least 24 cells | Cells in those groups | Mean cells per qualifying group |
| --- | ---: | ---: | ---: | ---: |
| 720–1,800 m | 234,535 | 2,742 | 123,034 (52.5%) | 44.9 |
| 1,250–1,800 m | 158,692 | 1,880 | 88,701 (55.9%) | 47.2 |

All those station records use conifer families. An equivalent airport-region
count finds 49,262 of 94,554 far-ring cells (52.1%) in qualifying groups, all
broadleaf. The opportunity is not confined to the mountains.

These are **offline horizontal-ring counts**, before stereo frustum, slant range,
terrain occlusion, slope splitting and admission limits. They are not actual
visible instance counts or measured GPU savings. A disconnected subset inside
one block may need further splitting. In the far tier, a representative 47-cell
group costs about 188 branch triangles today. A 12–24-triangle proxy would remove
roughly 87–94% of that group's branch geometry, if it can preserve the image.
That arithmetic does not predict the whole scene's FPS.
Count details and source identity are retained in the external evidence file
`performance_plan_20260928/canopy_cluster_feasibility.json`.

Existing pilot-position capture `tree_head_turn_20260928/pilot_stereo/screenshots/
headturn_6_0_left.png` was inspected for this revision. It shows distinct nearby
clumps and exposed skyline trees, with dense foliage on the intervening slopes.
Those different visual roles require different LOD decisions. It is an offline
image, not new headset or performance evidence.

**Research and the approach selected**

Multi-view impostors are an established distant-foliage technique. Epic documents
using upper-hemisphere impostors for Fortnite trees when Nanite is disabled, and
explicitly identifies the tradeoff between fewer vertices and more pixel-shader
work. Its mobile path uses a single sampled view. This supports investigating
the technique; it does not establish a Quest 3 speedup or justify copying its
144-view asset settings. [Epic's impostor documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/impostor-baker-plugin-in-unreal-engine).

Meta's open-world guidance combines lower-complexity distant assets with bounded
streaming, and specifically cautions about many small triangles with MSAA.
That matches this project's aerial workload better than merely combining already
batched objects. [Meta open-world guidance](https://developers.meta.com/horizon/documentation/unity/po-assetstreaming/).

**Selected candidate: small, component-aware, multi-view grove impostors for the
far tier, baked offline from the accepted branch geometry.** Keep current
individual crowns/trunks and nearby branch cards. An impostor is a render of a
three-dimensional clump viewed from several directions, preserving needles,
holes and crown shape. It is not a enlarged leaf image or a projected ground
photograph. Add middle-range use only where this same candidate passes visual
and performance gates; it is not assumed safe at 720 m.

Texture cost is a first-class limit. Naively baking 25 views at 64×64 pixels for
all 1,880 qualifying far-ring groups would cost approximately **109 MiB for color
and alpha alone**, assuming ASTC 6×6 and a full mip chain, before padding, normal
or depth data and staging buffers. At 128×128 it is about 435 MiB. This is an
illustrative sizing calculation, not a proposed resident allocation. A naive
unique-atlas-per-grove rollout is rejected.

Use cropped views, resolution chosen from projected pixel size, coarse resident
mips and a bounded cache of finer view data. Reuse genuinely identical bake
outputs where possible, without assuming unrelated groves share their shape.
Report actual package size, resident bytes and staging peak on the pilot patch
before committing to all-map baking. Do not silently raise the existing texture
budget; the September 15 estimate already had little spare room, and a current
measurement is required. If economical storage or acceptable stereo shape cannot
be achieved, use the fallback below instead of building an elaborate texture
streaming system around a failed proxy.

**Implementation sequence after approval**

1. **Spend one bounded diagnostic pass establishing which work must shrink.**
   Preserve the installed APK/source. Reuse the mountain, airport and sky-heavy
   starts with stationary and turning views; record a page crossing. Measure CPU
   selection/upload and GPU foliage/cloud work separately, together with frame
   intervals, actual pose, focus, FFR, resolution, clocks and temperature. A short
   frozen-selection variant separates selection from rendering; a diagnostic
   grove isolation must account for the underlying tree fallback and ownership.
   Inspect draw order, overlapping alpha cards and whether most expensive foliage
   is near, middle or far. Use existing counters and device tools rather than
   making a new benchmark application. Compare immediate pre-fix selection only
   if needed to attribute a CPU regression; no lengthy old-release campaign.
   If CPU is limiting, the next step leads. If distant foliage dominates GPU,
   proceed directly to the cluster experiment. If clouds or another pass dominate,
   take that measured larger win first and retain the cluster proposal for foliage.
   [Meta profiling workflow](https://developers.meta.com/horizon/documentation/unity/po-perf-opt-mobile/).

2. **Make the directly related CPU correction and stop there until remeasured.**
   Replace per-cell hash membership with persistent indexed state, avoid unused
   candidate tails and cache immutable bounds. Reuse unchanged selections and
   ownership data; do not clear and upload 3.1 MB merely because a timer expired.
   Changes in residency, ownership, active fades, tier or safe stereo coverage
   still trigger the necessary updates. Keep the successful visibility guard and
   continuity policy. Derive unchanged/dirty state explicitly; do not freeze a
   live flight's selection. Buffer/log release diagnostics if profiling confirms
   their synchronous writes matter. Keep Jobs/Burst, GPU compute and buffer
   architecture changes as escalation, not prerequisites for the visual LOD work.

3. **Build one integrated far-grove candidate at the existing mountain station.**
   The patch must fill a useful forward/side pilot view at the target distance,
   large enough to affect aggregate cost. Include a sloping dense conifer stand,
   an exposed edge/skyline and sparse gaps, with a small broadleaf control patch.
   An isolated attractive asset or a grove beneath the aircraft is insufficient.

   - **Partition once offline.** Start from 32 m blocks intersected with existing
     connected components and families; re-split disconnected in-block pieces.
     Preserve roads, clearings and component gaps. Split steep or excessive-depth
     groups toward 16 m where needed; retain source geometry for sparse groups.
     Resolve page-edge ownership in world coordinates, including card extents,
     so geometry crossing a cell/page boundary is neither cut off nor duplicated.
     Initial qualifying size is at least 24 cells. This is a screening threshold,
     not an assumption that all counted groups can become impostors.
   - **Bake the actual branch renderer's appearance.** Preserve its seeded card
     placement, terrain-conforming height, needle/leaf artwork, tint and opacity.
     Keep original tree crowns/trunks in their existing separate path for this
     first candidate. Do not bake them again and double their density. Match the
     shipping lighting convention; avoid double lighting, fog baked into the
     texture, road/terrain pixels and ground shadows floating in the canopy.
   - **Cover aerial angles.** Pilot bake starts with eight azimuths, three
     elevations and a zenith view, including grazing views. These are a bounded
     starting sample set, not a promised final atlas. Unsupported below-slope
     views retain geometry. Use tightly cropped, mip-safe color/coverage views
     and a small depth-shaped proxy where needed for terrain intersection and
     stereo shape. Aim for 12–24 proxy triangles per qualifying group; no
     iterative ray marching or mandatory per-pixel depth writes in the first
     candidate. If such complexity becomes necessary, re-evaluate its net cost.
   - **Keep view selection stable.** Use center-eye position relative to the
     cluster, not head-facing direction, to choose capture views. Both eyes share
     view indices and transition state but retain their own stereo projection.
     Head rotation alone must not rotate the forest's appearance or change LOD.
     Use the cheapest sampled view in stable regions, with a bounded two-view
     handoff where necessary. Any silhouette morph, double image, angular pop or
     depth error that remains visible disqualifies that cluster/view range.
   - **Start far enough away.** Initial use is 1,250–1,800 m, with each proxy also
     limited by projected size/error in both eyes; start screening at no more
     than 48 pixels across. Distance alone is insufficient. Preserve resolvable
     skyline trees and return to child geometry where slope, parallax or viewing
     angle makes the approximation fail. After the far tier proves useful,
     advance the same representation inward only where the error bound permits.
   - **Replace work completely.** Once a proxy is ready, omit its branch children
     from detailed selection, GPU records and submission. Preserve their exact
     ownership footprint toward original middle foliage. Retain source children
     until replacement texture and geometry are resident. Use bounded,
     complementary coverage transitions rather than two fully opaque overlapping
     representations. Do not add a canopy layer on top of the existing grove.
   - **Do not spend the saving immediately.** Hold admitted coverage constant for
     the diagnostic comparison. For rollout, lower workload targets based on
     measured cost instead of filling the old 393,216-triangle cap again. The cap
     is a ceiling, not a quota. Do not extend range or add extra density in this
     performance milestone.

4. **Accept or reject that candidate before large generation.** Inspect the
   integrated pilot view in both eyes during translation, banking, head turns,
   skyline crossing and LOD handoff, using existing captures and focused stereo
   tooling. No new art audition or user-worn test for each adjustment. Compare
   representative release-mode device timings with identical content/settings;
   record CPU, GPU, total frame time, memory, uploads and transitions. A proposed
   go/no-go target is at least **50% fewer branch instances/triangles in the
   replaced cohort**, plus a material measured bottleneck improvement: target
   **20% lower foliage GPU time and an observable whole-frame gain**, beyond run
   variation. A CPU-bound result instead must reduce that measured critical path.
   These are experiment targets, not predicted gains or substitutes for 72 Hz.
   Reject a proxy that merely lowers counters, increases fragment cost, visibly
   changes the forest, or requires excessive unique texture storage.

   If the proxy fails due to texture storage, disocclusion or stereo shape, the
   single fallback is a **simplified three-dimensional cluster mesh reusing the
   current needle/leaf atlas**, retaining exposed silhouette cards and removing
   interior cards only within a verified distant-view error bound. This exchanges
   some geometry savings for simpler shading and little new texture residency.
   It must pass the same gate; no solid blob/sheet substitute. Do not implement
   multiple competing systems or continue an unsuccessful impostor research loop.

5. **Roll the winning representation across eligible map coverage.** Reuse the
   1,296-page manifest, source records, hash validation and fallback behavior.
   Publish a cluster/page coverage manifest including explicit geometry fallbacks
   and reasons; map-wide support does not require every tree to use a proxy.
   Cache coarse proxies around the aircraft, prefetch along the flight path and
   retain safe coverage for rapid turns. Stream detail by projected need, with
   stable ownership and bounded uploads. Unready data keeps the previous valid
   representation, never a hole. Do not create a per-cluster GameObject, material
   or draw: batch by representation/atlas page, with measured draw/state budgets.
   Reduce detailed-cell residency only when all required source/fallback views
   remain available; unload/reload on every head turn is forbidden.

6. **Take the next large measured win, then package one combined candidate.**
   If still needed, prioritize tightly bounded alpha-card outlines and simpler
   foliage shading when fragments dominate; conservative terrain-horizon/block
   culling when hidden geometry dominates; cloud formation bounds/empty-space
   skipping when cloud rendering dominates; or staggered decode/uploads when
   streaming hitches dominate. Preserve occupied cloud samples and both-eye
   visibility. A new depth prepass, per-pixel depth impostors or hierarchical-Z
   culling requires evidence that its additional mobile GPU passes pay off.

   Run affected continuity/ownership/page-boundary checks, inspected stereo QA
   and Android build validation. Repair only validation inputs needed to see the
   relevant mountain views. Check airport, mountains, reservoir, traffic and
   training text. Stop adding techniques once the target is met. Prepare one
   release candidate and request one final worn review when ready; if valid
   physical timing cannot be obtained autonomously, mark performance pending.

The approved fast path is **short attribution → necessary CPU correction → far-grove
representation proof → map-wide generation only after it earns its cost → final
combined acceptance**. A measured larger bottleneck takes precedence. It is not
a commitment to try every row of the options inventory below.

**Broader options considered**

| Technique | Decision for this simulator |
| --- | --- |
| GTA-style world streaming / quadtree or clipmap residency | Already present in principle. Improve scheduling, prediction and lifetime; do not replace the system or unload the rear view on every head turn. A flight simulator sees more terrain than a street-level game. |
| Data-oriented CPU loops, Jobs/Burst | Good escalation if selection/decoding remains CPU-bound after simple caching. Restrict to pure-data jobs; no ECS rewrite or Unity-object calls from workers. Avoid replacing a stall with an immediate job-completion wait. [Unity Burst overview](https://docs.unity.com/en-us/engine/6000.6/manual/scripting/compilation-and-code-reload/script-compilation/burst/introduction-to-burst). |
| GPU compute culling, compact index lists, indirect draws | Feasible candidate under Vulkan if CPU submission is limiting. Adds dispatch, synchronization and memory costs to an already busy GPU. Prototype only the foliage path; keep stereo visibility and deterministic ownership. It is not automatically better than the current three procedural draws. |
| Terrain-horizon, cockpit and building occlusion | Potentially useful, especially in mountains and behind the panel. Require conservative block tests, correct both-eye coverage and an uncertain-means-visible rule. No blanket bank-side, rear-quadrant or beneath-aircraft removal. |
| Unity baked occlusion | Static terrain/buildings may benefit locally. It cannot make the moving cockpit a baked occluder; full-map flying volumes can be expensive. [Unity dynamic-occlusion limits](https://docs.unity.com/en-us/engine/6000.6/manual/cameras/occlusion-culling/dynamic-gameobjects). |
| Hierarchical depth-buffer occlusion | Conditional experiment if many expensive blocks are actually hidden. Depth passes, pyramid construction and synchronization can erase the saving on mobile. Avoid synchronous occlusion-query/readback stalls. |
| GPU Resident Drawer / SRP Batcher | Not a checkbox fix in the current Built-in pipeline, and existing custom procedural foliage would need integration. Unity's GPU occlusion path is tied to its GPU Resident Drawer/URP workflow. [Unity 6.3 GPU culling](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-culling.html). |
| Tighter bounds and spatial batching | Prefer measured cluster sizes over giant combined meshes or thousands of tiny draws. Precompute conservative tree bounds instead of transforming them repeatedly where possible. Preserve the correct cloud wind envelope. |
| Opaque draw order / depth-only pass | Verify existing opaque depth rejection first. A new depth prepass duplicates geometry and may cause mobile memory traffic; retain it only if captured net GPU time improves. Do not add a full-scene prepass by default. [Meta pass costs](https://developers.meta.com/horizon/documentation/unity/po-advanced-gpu-pipelines/). |
| Alpha-test removal or conversion to blended foliage | Not a blanket fix: can break depth, sorting and canopy density. Maintain existing alpha-to-coverage and leaf silhouettes. Benchmark a targeted alternative only with convincing visual equivalence. |
| HLOD, cluster impostors, baked distant canopy views | **Promoted to the main bounded candidate.** Can reduce per-cell CPU work, tiny triangles and overlapping interior branch layers. Gains depend on the measured bottleneck. Include texture residency, atlas batching, silhouette/parallax error and transition cost in the decision; preserve nearby source geometry. |
| Simplified 3D grove meshes using the current atlases | The one fallback if multi-view impostors cannot fit memory or maintain stereo shape. Less aggressive geometry reduction, but cheaper material/storage and stable three-dimensional parallax. Retain exposed crowns and gaps; no opaque blanket canopy. |
| Texture/mesh compression, mip residency and material atlases | Audit actual Android imports and resident memory, not only the build's generic compression label. Needle textures already have mip coverage preservation. Repack only if sampling cost, state changes or memory pressure justify it. Keep source resolution, leaf coverage and cockpit readability. |
| Cloud coarse occupancy acceleration | Promising if transparent ray marching is a leading cost. Conservative empty-space bounds can skip empty samples while retaining occupied-region detail. Added lookup cost must be measured. |
| Half-resolution clouds, temporal cloud reconstruction or distant baked clouds | Potentially substantial fill savings but not inherently lossless; risk halos, cockpit-edge bleed, stereo errors, banking trails and changed cloud interiors. Reserve for a controlled visual-equivalence experiment, not the first regression fix. |
| Static lighting, shader specialization and cached reflections | Keep existing baked/static results. Audit redundant passes, unused dynamic lights/reflection updates and material variants; do not rebake the whole scene or remove accepted gauge/cowling appearance on speculation. |
| UI, instruments, animation, traffic, birds and audio scheduling | Cache static UI, update changed text only, avoid rebuilding hidden canvases and cull unnecessary animation work. Keep visible animation smooth with interpolation and maintain logical traffic/avoidance state when offscreen. Profile audio decoding/spatialization and instrument updates before changing them. No reduction in flight-authority or safety-relevant evaluation rate. |
| Release diagnostics, allocations and file I/O | Buffer or opt-in the confirmed per-event synchronous input trace writes, retain bounded useful crash/feedback evidence, eliminate hot-path allocations and scene searches if measured. Current build is already non-development IL2CPP with incremental GC and multithreaded rendering. |
| Medium → High/HighTop or dynamic FFR | Potential fill savings, but the user reduced High because training text suffered. Keep Medium in the first package. Existing subsampled layout is already enabled. A later experiment must preserve text and gauge clarity; no repeat FFR ladder. [Meta foveation guidance](https://developers.meta.com/horizon/essentials/fixed-foveated-rendering/). |
| Sharp compositor UI plus stronger exterior FFR | Worth a later bounded prototype if fill remains limiting. Compositor text layers can preserve sharpness, but proper cockpit occlusion, interaction and layer cost must be handled. Different FFR levels for cockpit and exterior are not provided by merely adding a second camera to the same eye target; a separate layer/render-target design is needed. [Meta compositor layers](https://developers.meta.com/horizon/documentation/unity/unity-ovroverlay/). |
| Dynamic resolution / Meta Quest Super Resolution | Useful quality/performance controls, not guaranteed fidelity-preserving optimizations. Upscaling cannot restore all fine foliage or distant aircraft detail. Hold resolution fixed for the first package; later evaluate a tightly bounded floor only with explicit visual acceptance. [Meta Super Resolution](https://developers.meta.com/horizon/blog/vr-image-quality-meta-quest-super-resolution/). |
| Application SpaceWarp | Keep on the table for a separately scoped prototype if native 72 Hz cannot be achieved without unacceptable image loss. Current Built-in project would need pipeline/custom-shader motion-vector work; Meta's documented Unity route uses supported URP. Cockpit needles, translucent clouds, propeller, birds, thin foliage and disocclusion all need inspection. 36 real frames plus generated frames is not native 72, and a 28 FPS workload would still need recovery. [Meta AppSW guide](https://developers.meta.com/horizon/documentation/unity/unity-asw/), [artifact sample](https://developers.meta.com/horizon/documentation/unity/unity-sample-app-spacewarp/). |
| URP migration / engine update | An enabling project for some advanced options, not a demonstrated speedup. Past URP experiments failed; use a separate milestone only if a specific measured benefit justifies migrating the custom shaders and visual validation. |
| CPU/GPU performance levels and thermal management | Verify actual clocks and sustainable supported levels. Use controlled clocks only for diagnosis and restore overrides; do not rely on overclocking, disabling thermal protection or a short cold run. |
| Eye-tracked foveation / desktop DLSS / PC rendering | Quest 3 has no eye-tracking hardware; desktop NVIDIA DLSS and required-PC rendering are not solutions for this standalone ARM64 target. [Meta eye-tracking support](https://developers.meta.com/horizon/documentation/unreal/unreal-eye-tracked-foveated-rendering/). |
| Lower refresh, visibly fewer trees, shorter distance, reduced MSAA or texture quality | These can reduce cost but fail the requested quality-preserving milestone. Fewer submitted branch records at unchanged apparent density are explicitly allowed and are the cluster proposal's objective. Retain 72 Hz, existing 4× MSAA, Medium FFR, distance and visible density. [Meta MSAA guidance](https://developers.meta.com/horizon/documentation/unity/gpu-improved-algorithms/). |

**Acceptance and lessons from prior work**

- Visual reference is the improved September 28 build, not the older popping
  version. Require stable canopies through ordinary fast head turns, banks and
  return views; preserve street/mountain tree identities, trunks, distant
  coverage, cloud shapes, cockpit text and visible traffic. Exact-output changes
  should compare exactly or within explained floating-point differences;
  perceptual substitutes need separate human acceptance.
- Measure **CPU and GPU separately** where supported, plus delivered frame
  intervals and stale frames. A 72 Hz frame interval is 13.89 ms. Aim for roughly
  12 ms or less of app CPU/GPU critical-path work to leave margin; these are
  proposed engineering targets, not results. Verify sustained native 72 FPS and
  no recurring head-turn/page-crossing spikes during a representative warmed
  route. Include a 15–20 minute thermal run before claiming release readiness.
- Do not add CPU and GPU milliseconds as if they were serial, equate GPU
  utilization with GPU duration, count generated frames as native frames, or
  subtract unmatched resting-headset runs. Preserve APK hashes, poses, counters,
  actual FFR/resolution/clocks and timing windows. Profile captures and release
  timing runs serve different purposes. [Meta measurement caveats](https://developers.meta.com/horizon/documentation/unity/po-per-frame-gpu/).
- Validate changes incrementally using affected checks, then one combined
  headset-ready build. No repeated art auditions or optimization-only worn tests
  after each tweak. If tracked representative hardware measurements cannot run,
  mark that evidence pending rather than accepting unworn/system-dialog data.
- The inherited material-count failure and the Windows simulator's Android-only
  mountain imagery do not prove this regression's cause. Track them separately;
  repair only the validation input needed for meaningful stereo review. Do not
  spend the optimization milestone chasing unrelated audit counts.
- Preserve authored hierarchy, OpenXR camera ownership, native JSBSim authority,
  Android ARM64/Vulkan self-containment and existing source assets. No physics
  rewrite, new visual features or broad asset conversion is included.

No reliable whole-frame percentage or FPS gain can be promised from the present
two log samples or offline grouping counts. The strongest newly identified
structural opportunity is to replace many distant branch patches with a much
smaller representation. The fastest regression correction may still be CPU
selection; the first profile distinguishes those cases. Both belong in the plan.

Success means the same perceived forest density, dark conifer character, coverage
and stable motion at lower measured frame cost. Impostors are approximations:
perceptual equivalence must be demonstrated, not called mathematically lossless.
If this package cannot reach the frame target, report the remaining measured gap
and choose the next expensive subsystem from the inventory. Do not count a
geometry reduction, a prettier screenshot or a successful build as performance
acceptance. This document supersedes the earlier sequence; implementation still
has received explicit approval. Further unrelated features remain out of scope.
