# Shared wildlife candidate — September 22, 2026

Status: implementation complete; Android/stereo checks in progress. Worn Quest appearance, new Michael clips and incremental performance are **not yet accepted**.

## Delivered scope

- Explicitly serialized 24-actor population in `ProductionWorldRoot`: existing golden eagle, three red-tailed hawks, two ospreys, three groups of six Canada geese. Every active flight mode uses this population; training receives no wildlife narration.
- Replaced the eagle's old segment/proximity/one-shot controller with the shared director. The legacy component is disabled and retained only to preserve the environment's serialized reference contract and accepted visual assets.
- New mode selection rearms/reset wildlife under the existing transition. Demo has distinct 08/26 initial corridors. Birds already exist in world space; no in-view spawning, camera parenting, aircraft-speed pacing, or changes to the aircraft route/native dynamics.
- After release the eagle flies a fixed world heading, then departs continuously toward open-country habitat. It uses wind-displaced lift centers in ambient soaring. Early manual takeover cancels staged choreography; a new demo rearms it. An already begun encounter is not replayed on handback.
- Shared wind advection, bounded air-relative speed/turn/bank, terrain lookahead, and predicted player/ambient-aircraft avoidance. Raptors alternate flapping/gliding; geese follow a moving leader with individually phased flaps and steering, rather than a rigid formation transform.
- Three short Michael cues select left/right/ahead from the moving subject. Eligibility requires range, pilot view, forward cockpit bearing, terrain/physics sightline and a predicted four-second remaining window. No queued stale fact. Follow-up correction: losing the view or completing the encounter no longer cuts off an active sentence; explicit handoff uses a short fade. See [narration and bird review](NARRATION_AND_BIRD_REVIEW_2026-09-22.md).
- Includes the pending louder narration/default engine ducking adjustment. No cockpit, scenery, aircraft physics or flight-route revision.

## Asset admission

User downloads: `red-tailed-hawk-in-flight.zip`, `osprey-in-flight.zip`, `canada-goose-in-flight.zip`. All three GLBs contain an actual 22-bone wing-flap take with 66 transform channels. These are not turntable animations.

`tools/bird/author_habitat_birds.py` compiles one shared material/atlas and three skinned LODs per species, torso-weights and merges the source's rigid feet, retains UVs and original wing animation, and derives gliding poses from maximum source span. Raw downloads, Blender work files and renders remain outside the project. Source URLs, creator, CC BY 4.0 attribution, input hashes and transformations are in `Assets/Production/Environment/Wildlife/HabitatBirds/SOURCE_MANIFEST.json`.

| Species | Authored span | LOD triangles |
| --- | --- | --- |
| Red-tailed hawk | 1.25 m | 3,892 / 900 / 259 |
| Osprey | 1.65 m | 4,072 / 900 / 260 |
| Canada goose | 1.65 m | 3,354 / 899 / 259 |
| Golden eagle | Existing 2.1 m | Existing 3,858; retained hero detail |

Inspected Blender glide/flap renders and Unity material/skin renders show recognizable natural silhouettes and plumage. The existing golden eagle remains a recolored bald-eagle derivative, not a specimen-accurate new model.

## Focused evidence

External evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/bird_implementation_20260922`.

`HabitatBirdValidation` replays the retained native 1 Hz aircraft traces, interpolated at 30 Hz, through the production wildlife step for the first 250 seconds of both routes. It does not introduce aircraft physics or claim tracked-headset evidence. Forward-view criteria: bird 65–320 m away, bearing within 35 degrees, elevation -7 to +30 degrees. The moving cue additionally checks future view and sightline.

| Trace | Closest eagle distance | Useful forward view | Cue eligible | Minimum sampled bird terrain clearance |
| --- | --- | --- | --- | --- |
| Runway 08 | 54.7 m | 12.5 s | 7.8 s | 125.1 m |
| Runway 26 | 81.4 m | 10.5 s | 8.6 s | 125.5 m |

Both rearmed correctly on reset. Imported animation deformation was checked through Unity's Animator and baked skinned vertices. Initial corridor trials failed below the useful climbing windshield view and were corrected before the candidate build. This is why the encounter altitude differs from the old fixed 578 m orbit and the earliest trial.

Standard focused Visual QA: five inspected images (three species admission images plus left/right ordinary pilot eagle views); `PASS_BIRD_VISUAL_MACHINE_QA`. Authored environment contract, hierarchy, one XR origin and no legacy components passed. External admission closeups establish asset quality, not pilot-view visibility or Quest performance. Stereo runtime validation is recorded separately below when complete.

## Resource policy and limits

No new realtime lights, shadows, physics bodies or transparent feather stacks. One material per bird, shared 1024 atlases with Android ASTC 6x6; three geometry levels selected by angular size and hysteresis. Conservative union of both stereo frusta includes a head-turn margin. Logical motion continues while culled; sleeping ambient Animators are warmed to their current phase before rendering. The hero retains AlwaysAnimate.

Admission caps: 16 visible birds, four full-detail birds and 30,000 bird triangles. Wind/terrain/predictive avoidance decisions run at approximately 5–6 Hz per actor; movement interpolates each frame. The retained route checks saw at most six visible birds and 4,117 bird triangles. These counts are **not GPU/CPU timings**. The approved +0.3 ms CPU / +0.4 ms GPU goals require a physical Quest measurement. Source derivative files total about 6.1 MB; this is not a runtime-memory measurement.

Android build passed and was installed successfully on Quest `2G0YC5ZG8907TD` on September 22; the application was launched. APK SHA256: `e2edec05f54a429b1b4a38999e31d935f6ce6c87922f3eacf0ab0600ba7485b0`. Build manifest is in `quest_build_final/product_apk_manifest.json` under the external evidence root.

Inspected live Meta XR Simulator left/right demo captures show the eagle ahead-left and the matching Michael caption; runtime logged `tour_eagle_left` during `climb_gate`. Stereo, hierarchy and missing-reference checks passed with no logged runtime errors. The overall simulator gate did not pass: its mountain-immutability probe included the showroom-to-flight transition. Development-only QA timing was corrected after the Android build; supplemental habitat capture remains pending. This does not establish Quest performance.

Pending: combined Quest check for eagle visibility, believable goose animation, new Michael cue intelligibility and frame/thermal impact. No claim of measured hardware performance until that check.
