# Main consolidation, October 3, 2026

The user requested consolidation of the accumulated wins on main after reporting
that the corrected build currently looked fine on the physical headset.

The lesson and stabilization branches both started at the same commit as main,
`4ceb92fdbb2aeddebcd4ae99566fd8ecc1289187`; the accumulated work remained local
and uncommitted. The consolidated source includes authored demo/lesson and menu
improvements, narration/audio, bird encounters, cockpit/facility presentation,
local recovery, foliage work, and the paused-initialization menu regression fix.
Per-milestone evidence and asset provenance accompany the sources.

Before consolidation, all 598 entries in the October 2 Android build source
inventory were compared with their SHA256 hashes. Only the menu evidence note
differed, to record installation and user feedback. All changed Unity assets had
paired metadata. The disabled development-agent address was restored to its
existing committed value instead of promoting a machine-specific IP change.
The mixer whitespace rule follows Unity's other serialized asset rules.

Validation carried forward from the installed build: successful Android
ARM64/Vulkan/OpenXR build, native JSBSim required, 1,912 scenery payload files
verified; 45 focused EditMode and 21 focused PlayMode tests passed. Inspected
live simulator stereo captures cover the cockpit transition and ten tools pages.
The APK SHA256 is
`f41159b963e214af5b47454585196f05dde5d43652ff96f72a1b549e2d517a86`.
It was installed in place on Quest 3 on October 3, preserving application data.

This is a development consolidation, not release acceptance. The full EditMode
run had 28 failures before one stale expectation was corrected and retested;
the remaining suite was not rerun. Standard QA exceeds the existing material
planning target; the simulator mountain-identity check and missing replay
recordings remain unresolved. The user's current positive headset observation
does not establish exhaustive menu acceptance or sustained frame/thermal results.
See `MENU_PREPARATION_REGRESSION_2026-10-02.md` for the exact validation limits.

Storage cleanup preserves the current installed APK, historical source archives,
rights/source records, reports, stereo captures, and unique raw inputs. Only
generated simulator player directories and reproducible Unity/Gradle caches are
selected for removal. Unity will reimport/rebuild its cache on the next opening;
Gradle dependencies will be downloaded again when needed. The external cleanup
ledger records exact paths and the measured reclaimed space.
