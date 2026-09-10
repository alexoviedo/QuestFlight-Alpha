# Map-wide branch-grove rollout — September 9–10, 2026

September 10 update: the user reports a huge visual improvement on the headset, with late canopy arrival as the remaining issue. The resource table below describes the retained 520–640 m reference. The longer-range candidate and its unpassed cost gates are recorded in [the distance-LOD report](CANOPY_DISTANCE_LOD_2026-09-10.md).

The user authorized geographic rollout after repeatedly finding the single-grove candidate indistinguishable in the headset. The new authored renderer and all-map payload are implemented. **Final device visibility and performance acceptance remain open.** This report distinguishes completed implementation from that acceptance.

## Scope and provenance

- The 36 × 36 scenery grid contains 1,296 generated pages, 1,295 nonempty, spanning the existing approximately 24 km map. The empty page has no accepted foliage cells; missing payloads are not silently treated as empty.
- 12,407,662 cells form 361,157 connected components; 248,173,976 bytes of page data are packaged. Connectivity is calculated before partitioning, with no bounding-box bridges or page-specific component IDs.
- 807,544 cells use the previously assembled City 2020 classified canopy extents. The source manifest carries the City/NV5/DRCOG attribution and CC BY 4.0 record.
- 11,600,118 cells use the retained orthophoto-derived angular-grove **mask data**, with its existing source/license record and surface exclusions. The rejected photographic card geometry is not reused. Fallback classification remains less certain than measured extents; it is not a new validated computer-vision result.
- Heights, ground support and broad tint are estimated from the existing accepted tree data, including inside measured footprint coverage. They are not claimed to be map-wide measured canopy heights. 1,833 cells without nearby height support were omitted; existing trees remain the fallback.
- Existing licensed branch artwork is reused without enlarging leaf texels. No raw imagery, new external artwork, or source archives are introduced into the shipping assets.

The unresolved independent boundary benchmark is retained. The user's latest geographic authorization changes delivery scope; it does not change benchmark scores or validate uncertain source regions. The authored terrain spans −12,000 to +12,000 m, while normal Free Flight recovery triggers at ±10,500 m (1,500 m inset). Coverage data includes that outer scenery margin, but ordinary pilot validation must use the flyable region.

## Rendering and resource limits

`MapBranchGroveRenderer` is explicitly serialized under the product environment. It streams immutable data and submits instances; it creates no product hierarchy. The OpenXR Main Camera remains tracked and is not repositioned by the renderer. Nine observation providers are authored for development diagnostics and place the aircraft through the existing native-JSBSim reset path.

Each accepted cell uses six small branch cards in two foliage layers, shaped by its local footprint radius and estimated height. Edge cards crop the same artwork instead of stretching it. The old single-grove and angular renderers are disabled. Original trunks remain. An ownership texture suppresses original leaves only where replacement cells are selected and faded in; failed loads, distant trees and unselected cells retain their original representation.

| Resource/control | Implemented value or behavior |
| --- | --- |
| Full new-canopy coverage | Up to 520 m slant range |
| Handoff | Continuous fade to existing trees over 520–640 m |
| Resident page limit | Nine pages, with independently verified hashes |
| Submitted cell limit | 32,768; excess retains original-tree fallback |
| Geometry | 12 triangles per cell; maximum 393,216 grove triangles |
| Grove submission | One procedural instanced draw for the authored viewing camera |
| CPU culling | 32 m blocks followed by conservative both-eye cell tests |
| GPU instance buffer | 1,572,864 bytes |
| Ownership texture | 756 × 756 R8, no mip chain |
| Extra leaf atlas / real-time shadows | None |

This limit is not a Quest frame-time guarantee. The current nine-location Editor sample selected 3,476–18,211 cells per view; dense unsampled views can cost more. Early clipping avoids shading fully replaced original leaves but does not remove their submitted vertices. The distant representation remains the existing trees/HLOD; composed distant-grove impostors are not implemented.

## Verification and defects found

- All 1,296 files pass page/header/hash/record validation and corrupt payloads are rejected. All 62,528 checked adjacent cell pairs crossing page seams preserve component identity. This is data integrity, not field accuracy.
- Focused authoring, camera binding, old-overlay deactivation, handoff, page-edge loading, ownership restoration and existing connected-grove checks pass.
- The initial renderer enumerated a shared desired-page list across an asynchronous request. Camera selection also rewrote that list, which can invalidate the suspended enumerator. Loading now uses a snapshot and rejects completed requests that no longer belong to the current window. The asynchronous regression passes: 945 Editor updates, interrupted remote-window changes, nine pages loaded and no failures.
- 36 paired-eye Editor captures cover nine ordinary pilot locations across central and outer map regions. These use the authored renderer's selection/draw path, with the cockpit present. Offset Editor stereo is not tracked OpenXR stereo.
- Standard QA generated 130/130 captures without capture exceptions. Hierarchy, environment contract and the single-XR checks pass. **Machine QA fails in five views:** external control sweep and approach exceed the 77-draw estimate; ground-scattered-cloud, side-window and shadow-heading-180 views exceed the 692,871-triangle estimate. Maximum estimates are 84 draws and 721,542 triangles. Thresholds were not relaxed; two additional triangle overruns appear relative to the preceding fuller-grove run.
- The inspected side-window and map pilot renders show a large pale cockpit obstruction. The same obstruction exists in the retained preceding standard-QA image. It limits the usefulness of these images and is not proof of normal headset appearance.
- The earlier tracked Windows simulator pipeline has incompatible Android scenery bundles and unresolved runtime identification. Previous menu-only captures do not validate these groves. No new simulator pass is claimed.

Evidence root: `../T2-QuestFlight-product-artifacts/map_branch_groves_20260910/`. Page statistics are in `map_integrity.json`, pilot counts in `pilot_visibility_evidence.json`, and full QA results in `standard_qa/visual_qa_report.json`.

## Android/device state

The initial rollout Android build passed, including all **1,912 packaged scenery files**. Its APK SHA-256 is `c8bba3e5522e0a64501ef12b52322a8e833af16bee82a245d6a5d08df003d95c`, and its installed device copy was verified. It predates the asynchronous streaming correction and is not the final accepted build.

Unattended observation startup timed out twice. The activity dump identified `com.oculus.guardian/...GuardianDialogActivity` taking focus with Quest Flight paused; the inspected screenshot showed the system view. The existing on-device observation JSON dated 20:09 predates this build and was rejected as evidence. The user has been asked only to clear the system dialog and leave tracking cameras unobstructed, not to run another worn comparison. Temporary power automation was restored after each scripted attempt.

The device script now retains failure focus/screenshot evidence, requires an active correctly bound renderer and a submitted draw, and retains both a short-lived magenta diagnostic and normal stereo captures. A submitted draw alone does not prove visible foliage pixels. The diagnostic is disabled in the authored material and automatically expires; ordinary launches have no queued observation.

**Corrected build PASS, September 10 at 17:00 UTC.** The user restored 49.24 GiB of free space. The Android build completed and all 1,912 packaged scenery files verified. Corrected APK SHA-256: `a12e1760ec5fdc1b03620d5312bf903a828f994a79e570147f35b2e2a1396d16`; size 1,995,882,680 bytes. Installation and on-device SHA verification passed. Evidence is in `android_build_streaming_fixed/`. This includes the passing asynchronous streaming fix.

The first five actual Quest observations now pass loading/alignment checks: nine resident pages each, one grove draw, zero loading failures and zero native/presentation position error. Selected cells are 5,972 / 8,218 / 3,081 / 9,923 / 8,109. Inspected the first diagnostic and normal captures and the next two diagnostic captures: real highlighted foliage is visible, and normal rendering clears the highlight. Station 0 shows it in both eyes. Some other views expose only a small ground sliver in one eye because the resting headset points upward; no stereo-quality pass is inferred from pixel counts.

The first outer observation (station 5) failed for an exact, separate reason: its authored aircraft position x=11,020 m triggered `ProductMapBoundaryRecovery`, which reset the native aircraft to x≈9,000 m while paused presentation remained at the old pose. The alignment gate correctly rejected the 2,028.531 m mismatch. This was an invalid test placement, not evidence of failed outer canopy loading. The four outer observation sites are now placed inside the existing flyable region, and the authoring test verifies every station against the actual recovery policy. The Editor pilot review now reads those authored spawn transforms directly, including ground height, instead of maintaining a separate pose list. The corrected-observation package built successfully (SHA-256 `832285c9880bd666aa161425acc70bc110e2e5b3e3220184072c83d151bc4378`), including all 1,912 scenery payloads. Installation attempts failed because ADB was offline; no installed identity is claimed for it. No flight recovery policy was changed.

Current evidence: `device_streaming_fixed/`, `device_streaming_fixed_outer/` and `device_pixel_visibility.json`. These are unworn rendering checks; normal seated-pilot appearance, tracked simulator and performance gates remain unresolved.

## Workspace stewardship

Audited cleanup enabled the initial build; the first corrected rebuild filled the disk. Automatic review rejected Gradle-directory, duplicate-APK and redundant-archive deletions; none ran. Compression then failed. Attempted log compression truncated that failed build log; its console excerpt records disk exhaustion. The user subsequently restored space, resolving the build blockage. The successful retry retains its complete build log. Personal files, source assets, baseline and rollback APKs remain. No commit was made.
