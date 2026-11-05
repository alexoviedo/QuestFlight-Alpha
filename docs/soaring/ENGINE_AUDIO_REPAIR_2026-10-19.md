# Sailplane engine audio repair — 2026-10-19

Implemented in source; pending the next integrated APK. No headset installation or listening acceptance in this task.

## Cause and correction

The C172 audio controller was disabled on glider selection, but its ten separate AudioSources kept playing. The controller now stops and zeros all owned sources on disable and starts from a clean mix on re-enable. It also refuses a non-C172 aircraft snapshot, preventing stale engine, propeller, starter, wind, flap or horn playback if another route leaves the controller enabled. Instructor and radio sources remain independently owned.

The native tug already had a 3D source, but logarithmic rolloff did not guarantee silence past its configured maximum. Its serialized source now follows the actual tug propeller, uses full 3D positioning with zero spread and a finite custom attenuation curve, and routes through the existing ExternalWorld mixer group. A serialized reference identifies the tracked Main Camera's AudioListener. No camera transform is written or new runtime hierarchy constructed.

The reference curve uses 8 m as its full-level radius, inverse-distance amplitude falloff, and a taper from 600 m to zero at 1200 m. The runtime also pauses and zeros the source at or beyond 1200 m, on pause/hold/mute, and stops it for an ended/invalid tow session, missing listener or stopped engine. Resuming nearby playback preserves the paused clip. A nearby running tug remains audible after release. RPM still comes exclusively from native JSBSim. This is a bounded reference audio mix, not a measured PA-18 acoustic/noise contour. Doppler remains disabled to avoid transform-reset pitch spikes.

All four serialized engine/propeller loop sources in the current product world were checked for ownership. Other ambient aircraft currently have no engine AudioSources. Glider airflow is project-generated band-limited noise, not the C172 engine recording. Existing generic CC0 piston recordings and all original media remain unchanged.

## Verification

- 15/15 focused checks passed, including actual Play Mode source playback: disable stops all ten sources; narration continues; C172 re-enable resumes; glider selection suppresses stale playback; return resumes C172 sound.
- 20/20 native-session checks passed: native initialization/selection; near, receding, far and returning tug; pause/resume; recovery hold; aircraft-volume mute; release; unchanged pilot pose; ending tow and returning to C172.
- Native-driven source gain after the serialized curve: 60 m 0.0351563; 250 m 0.0081787; 600 m 0.0033333; 1199 m 0.0000026; 1201 m and 5 km exactly zero. These are software gain values, not recorded sound pressure or rendered headphone measurements.
- Android player compilation passed for 61 assemblies; strict Android audio asset-bundle build passed. No full APK/IL2CPP link or install performed.
- Inspected left/right editor scene previews show the authored SGS cockpit and paused-flight UI intact. No visual content changes. No new live simulator, hardware timing or binaural listening acceptance claimed.
- No new AudioSources, audio clips, mixers or runtime GameObjects. Added ten curve keys and one listener reference. Actual Quest audio CPU/memory cost unmeasured; no budget or quality-setting changes.

Earlier failed test attempts were fixture issues: inactive prefab-parent lookup, a coroutine surviving Play Mode reload, assertions made before the next Update/RPM fade, and a visual-QA helper that disables audio listeners. Final tests restore the audio fixture correctly and retain the runtime checks.

Evidence and rollback copies: external `T2-QuestFlight-product-artifacts/sailplane_engine_audio_20261019`. Final results: `tests_verified`, `verification`, `verification_final.log`; source and artifact SHA-256 lists are `SOURCE_HASHES.json` and `VERIFICATION_HASHES.json`.

Unity references: [AudioSource maximum distance](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/audiosource/maxdistance), [Audio Source spatial blend and rolloff](https://docs.unity.cn/2021.3/Documentation/Manual/class-AudioSource.html).

Later headset check: start a tow, release, listen as the tug departs, turn your head to confirm its direction follows the visible aircraft, then switch C172/SGS and confirm no engine loop survives in the sailplane. Check the aircraft-volume control and pause/resume.
