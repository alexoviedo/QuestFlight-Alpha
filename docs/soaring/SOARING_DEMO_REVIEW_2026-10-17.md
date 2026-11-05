# SGS scenic demo candidate review

The approved demo is implemented using the existing native JSBSim SGS model, authored product hierarchy, shared menus, instructor voice and bounded bird population. No flight-model fidelity approval is implied. Build/install identity is maintained in `SOARING_DEMO_STATUS.json`; exact evidence is outside the repository in `T2-QuestFlight-product-artifacts/soaring_demo_20261017`.

Final candidate: **0.1.0-alpha.7 / code 7**, installed on Quest `2G0YC5ZG8907TD` with exact APK hash and unchanged training-save hash. Full Unity Android build, signature, ZIP/ELF 16 KiB alignment, 19 aircraft payload files and all 1,912 scenery files pass. APK is 1,817,349,524 bytes; SHA256 `e5fa341db5dec3d5d657ec7823125ffb019cbc55d0e874d76893e246985c52b7`. Exact source/model/file identities: external `android_alpha7_final/CANDIDATE_IDENTITY.json`. Build inputs matched recorded source hashes after the build; this final documentation update is subsequent. APK growth versus alpha.6 is 1,333,132 bytes for the entire change, not an isolated audio-memory measurement.

Completed runnable work in approximately 80 minutes. App launch returned OK and a process was observed, but the Quest was asleep. No presence spoofing or performance inference. No branch merge/push, user-file cleanup or rollback removal.

## User path and behavior

Home â†’ Fly SGS sailplane â†’ Scenic soaring demo. Full flight is about 20 minutes, beginning with a physical ground launch and tow release. Short tour is about 12 minutes and explicitly begins airborne. Both circle in rising air, offer a golden-eagle encounter, narrate visible Boulder landmarks and make an unpowered landing to a stop. Menu provides pause, explicit restart confirmation, return and end; manual takeover is enabled only during a stable sightseeing glide. Automatic handback requires adequate speed, altitude and return energy. An occupied landing surface causes a paused hold with restart/end paths.

The six landmark anchors cover the reservoir, Mount Sanitas, the Flatirons, downtown Boulder, CU Boulder and the airport. Narration starts only when a landmark is in view with sampled terrain clearance; speech availability and the user's gaze determine which facts are heard. The native test confirms visibility opportunities, not that every utterance will play for every head direction. Twenty-three generated clips add 224.125 seconds of voice content. Human listening remains pending.

## Verification

| Gate | Result and exact scope |
| --- | --- |
| Native full/short | PASS. Final viewport-gated run: 1185.217 / 692.942 simulated seconds, 150.008 / 150.009 m total-energy gain, 5/5 and 3/3 scenic legs, eagle cue, unpowered landing and stop. Full tow releases and tug parks. |
| Native adverse cases | PASS. Early return, safe manual handback, occupied-runway hold; weak lift loses 222.149 m of energy height and correctly returns early and lands. No artificial lift or airborne relocation. |
| C172 demo regression | PASS 7/7 native cases, including wind, Runway 26, occupancy, handoff and early return. |
| Focused software | Latest result for all 142 distinct tests PASS across three runs. Final affected rerun 59/59. Earlier guide fixture failure and real first-use Skip dead end were corrected. This is not an exhaustive full-project test claim. |
| New editor views | Inspected full/short menu, eagle left/right pairs, representative landmark views and landing. Text fits; stereo separation is consistent; eagle is visible but small at actual size. Discoverability in a worn Quest remains pending. |
| Standard visual QA | FAIL retained baseline: 107/130 pass, same 23 failing views and maxima as prior baseline. Contact sheet and selector inspected. No new failing views; no thresholds raised. |
| Required live simulator stereo | BLOCKED_MISSING_RECORDINGS. Fresh audit found no required replay recordings. Editor camera pairs are not simulator or headset evidence. |
| Human / Quest timing | NOT ACCEPTED for this demo. No worn flight, sustained frame/thermal measurement or human narration/comfort assessment in this implementation run. |

Native scenario timing is simulated flight duration. Guidance is evaluated at 72 Hz against native 120 Hz stepping; it is not Quest frame timing. The short manual interval in the handback scenario is a bounded automation probe, not broad pilot recovery validation.

## Resources and preserved limits

The SGS remains 7,969 triangles, three renderers and two materials under its 8,000-triangle ceiling. Bird population remains 24 with caps of 16 visible / 30,000 visible triangles; the scenarios observed at most eight / 5,412. New-view static maxima are 72 estimated draws, 428,124 visible triangles and 22 materials. Whole-scene standard maxima remain 80 / 967,513 / 37 and retain the existing resource failure. Rendering quality, scenery and simulation time were not reduced.

New voice source files total 10,759,012 bytes, mono 24 kHz, with no clipped PCM samples in software analysis. APK growth is recorded after packaging; Android resident audio memory and CPU/GPU costs are unmeasured. The final full native run measured roughly 0.0089 ms mean Windows guidance time and zero managed allocation in the measured guidance calls; these are development-machine measurements only.

## Remaining acceptance work

The reported SGS near-level stall/vertical-fall and control-response issue remains unresolved and unaccepted. Existing ridge/training failures and actual Quest timing failures remain open. This tour uses the existing paved development runway and a defensible repeatable thermal reference; it is not a verified Mile High Gliding route, grass-strip staging procedure, aircraft endorsement or observed weather record. Source/reference links and voice license records are in the implementation document and authored voice manifest.

Headset checklist when available:

1. Open the short soaring tour; check narration/captions, visible POI callouts, eagle discoverability and turning comfort.
2. Pause/resume, take controls when offered, hand back, then let the aircraft land and stop; check that menu choices remain usable.
3. Run the full tour through tow and release; record frame/thermal evidence separately from subjective flight and visual acceptance.

