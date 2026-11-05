# Ground and cloud visual repair — 2026-10-17

30-minute scope started 22:05:58 UTC; deadline 22:35:58 UTC. Source changes are newer than installed alpha.7. No APK built or installed and no headset test requested.

## Implemented

- Runway paint has a distinct depth bias from pavement. Terrain fragments strictly underneath the authored runway are discarded, with a 5 cm inset. Existing terrain geometry, colliders, scenery extent, camera clipping and render quality remain unchanged. This addresses a concrete depth conflict; the reported moving colored pattern is not yet confirmed resolved on Quest.
- Preserved the photographic clouded sky. The abandoned clear-sky replacement was removed and exact original sky files restored from task backups.
- All eight 3D formations use the existing compact CC0 VDB density family, mirrored for variation. Horizontal proxy dimensions now vary by at most 11%, with unchanged centers and cloud-layer heights. No intersecting proxy bounds.
- Removed the separate ellipsoid fog envelope and global cloud fade. Inside/outside visibility now comes from the actual ray-marched density.
- Fixed distant clouds drawing over near clouds: reorder the existing proxy triangles back-to-front, in the same single renderer. No runtime hierarchy construction. Existing vertices remain unchanged at runtime.
- Cloud rays stop at opaque geometry. Moved the existing depth-copy pass before transparency; no extra copy pass. Original order made the depth texture unavailable to cloud rendering.
- Updated authoring paths so regeneration preserves the fixes. Original texture payloads and source/license records remain available; revised proxy geometry remains a project-authored derivative.

## Evidence and limits

17/17 focused EditMode checks passed. Android Vulkan stereo shader compilation and strict scoped Android asset-bundle build passed. Forty final offset-eye editor captures cover runway turns, airborne views, cloud approach, edge and interior. Inspected final cloud entry pairs show foreground attenuation of distant formations and retained cloudy sky; ground views show no opened terrain holes. These are editor captures, not simulator or headset evidence.

The final standard 130-view run is recorded in GROUND_CLOUD_REPAIR_STATUS.json; previous runs retained the same 23 baseline failures. Fresh Meta XR Simulator audit is BLOCKED_MISSING_RECORDINGS. No hardware performance or full visual acceptance claimed.

Cloud draw/triangle/texture counts and the 48-step ceiling remain unchanged. Sorting adds one readable mesh copy, eight ordering integers and 288 index integers, plus native mesh overhead. Actual runtime memory/CPU/GPU impact is unmeasured. Budget limits were not increased. Geometry sorting uses one eye-center position and no steady-state managed allocations after initialization. No physics changes.

Source file hashes, task backups, exact asset bundle and capture evidence are external under `T2-QuestFlight-product-artifacts/ground_cloud_repair_20261017`; `SOURCE_FILES.json` identifies modified source/assets. Earlier intermediate directories are diagnostic attempts, not accepted final evidence. Final candidate evidence is `accepted_candidate`, `accepted_tests`, `accepted_standard`.

## Reference and later headset check

[NOAA cloud descriptions](https://www.weather.gov/fgz/cloudscontrails) informed the compact cumulus family and billowing form. The uniform size range is an authored reference-weather choice, not a claim that natural clouds are identical or a reconstruction of a specific Boulder observation.

After a future integrated APK: turn the head slowly and quickly over runway markings and nearby ground; approach, enter and exit two clouds; verify distant clouds attenuate naturally and cockpit/terrain occlusion remains correct. Check timing and memory on Quest separately.
