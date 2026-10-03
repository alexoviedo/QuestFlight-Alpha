# Narration completion and bird direction review

## Reported defects and findings

The demo explicitly stopped its AudioSource and cleared its caption when the eagle left the viewing cone or a landmark lost visibility. Turning to follow the subject could therefore cut off the very sentence identifying it. Phase announcements also replaced active clips immediately. Lessons could replace active speech at time-critical stage transitions, and live-caption expiry used an independent timer.

Ordinary demo and lesson narration now uses a single bounded pending utterance. Active sentences finish; routine replacements have a short gap. Captions track actual playback and retain a reading hold. Lesson evaluation can advance time-critical objectives without replacing the sentence already being spoken. Stale queued stage instructions are discarded, and successful lesson completion waits for speech. Safety corrections and explicit control handoffs retain priority with a 180 ms fade before replacement. Explicit exits, failed exercises and tracking pauses remain intentional context boundaries.

The imported golden eagle was **not reversed**. Its head is ahead of its pelvis on local +Z in both flight clips, matching the motion controller's air-relative forward direction. The same check passed for all three hawks, both ospreys and all 18 geese, including every LOD and 16 phases of each flap/glide clip. No bird model or flight axis was rotated speculatively.

All 24 actors also retained positive forward world motion through full turns with still air, opposing/cross winds up to 6 m/s and an upward component. The lowest forward component was about 5.66 m/s; the eagle's was 8.61 m/s. This covers the current authored horizontal wind envelope, not arbitrary future weather changes. A faster airplane overtaking a slower, forward-flying bird can make the bird move backward relative to the cockpit; that is a possible explanation of the reported impression, not a reproduction of the user's exact view.

Habitat and animation policy remains intact: hawks/eagle use open-country and foothill lift areas; ospreys use reservoir/pond areas; geese travel between water and adjacent grassland. Raptors alternate powered flight and soaring according to sampled lift and altitude needs; geese flap in individually phased formations. Existing terrain lookahead, avoidance, angular LOD, stereo culling and population limits are unchanged.

## Evidence

External root: `T2-QuestFlight-product-artifacts/bird_implementation_20260922`.

- `orientation_before.txt`: original imported bone positions, including eagle head/pelvis/tail.
- `orientation_verified.txt`: all 24 actors, animation phases, all LODs and forward-motion results.
- `narration_tests.xml`: focused actual AudioSource playback test passed, covering queued completion, urgent fade/volume restoration and pause preservation. Initial test-fixture failures were resolved by isolating Play Mode setup and providing an AudioListener; they were not product acceptance passes.
- `presentation_fix/visual_qa`: six standard QA captures, including all four species and pilot left/right views. Species closeups inspected; correct beak/tail layout confirmed. The supplemental static pilot poses exhibit cockpit near-plane artifacts, so they are not accepted as live headset presentation evidence.

Android build passed and installation succeeded on Quest `2G0YC5ZG8907TD`; the app was launched to its menu. APK SHA256: `e47f85dfbeeb7b306f539003df19cf1085bac4b5776b3a4aade8f663819d6c68`. Manifest: `presentation_fix/quest_build/product_apk_manifest.json`. The refreshed Windows stereo player built, but the live run remained paused at startup (`TimeScale=0`, `awaiting_flight_state`) and timed out. No new live stereo acceptance is claimed; the older session report/images retained in that directory are not evidence for this build. The run report also records missing VRS recordings. Worn audio/visual acceptance remains pending; no new hardware performance claim.
