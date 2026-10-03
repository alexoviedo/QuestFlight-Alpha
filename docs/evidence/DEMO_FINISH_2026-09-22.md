# Integrated demo finish — 22 September 2026

## Scope and current status

Implements the approved finishing milestone: aircraft audio, consistent menu feedback, a first-use/guest flow, shared presentation preferences, and a focused airport art correction. Also implements the explicitly requested original-model engine cowling visible from the occupied cockpit.

Authoring, targeted tests, the bounded airport lighting refresh, a live XR cockpit stereo check and the integrated Android build have completed. Final listening and a complete worn demo session remain pending. This is not an assertion of AAA quality, flight-training certification, or Quest frame-rate acceptance.

## Visible cowling

The new cockpit-visible engine cowling is clipped from the original imported C172 exterior body at the firewall plane. It preserves the original painted material, UVs and texture detail instead of enlarging or recoloring the dark low-polygon liner.

The authoring log records **2,520 triangles, 1,740 vertices, one shared-material draw and no new textures**. The original dark interior liner stays in place to preserve the footwell enclosure. The exterior addition does not cast another dynamic shadow, add collision geometry, move the flight authority, or write the tracked camera. It is explicitly included in the cockpit visibility profile.

`CockpitEngineCowlAuthoring` validates the source, clipped bounds, material identity and a 3,000-triangle ceiling. The source-derived mesh remains subject to the existing local aircraft asset provenance; this milestone does not change those rights or publish the asset.

## Audio and interface feedback

The aircraft audio work is detailed in [the audio evidence record](DEMO_FINISH_AUDIO_2026-09-22.md). It restores previously silent authored control/landing cues, applies the aircraft-volume setting consistently, preserves warning audibility, smooths transitions, and fixes objectively discontinuous engine/noise loops. The same C172 source recording is used at higher preview quality with the accepted idle interval and matched levels. Michael's 106 recordings were measured and retained.

One world-root interface AudioSource presents short project-authored focus, selection and back sounds. Because it is outside the disappearing menu panels, selection feedback survives panel changes. It ignores listener pause so menus remain usable while flight is paused. Brief controller vibration follows the controller input, using unscaled time. Both forms of interface feedback can be disabled together in Settings.

Main-menu thumbstick selection and pointer-compatible action entry now reach the same selection-feedback call. App navigation owns lesson menu actions, so lesson menus inherit the same feedback without an additional overlapping training sound. The Back action avoids also emitting Select. Automatic focus reset does not play a navigation tick. A confirmation tone is authored for future explicit confirmation use but is not required by the current navigation flow.

The four short mono WAVs have source recipes and hashes in `Assets/Production/UI/Interface/Audio/SOURCE.json`. Each WAV and the runtime feedback script has an exact approved classification in `ProductionDependencyClosureExporter`; no broad dependency exception was added.

## Guest flow and preferences

- A skippable four-step first-use guide covers thumbstick/A selection, universal Menu/Back controls, seat comfort/calibration, and demo control ownership. It can be reopened from Settings.
- Voice volume, aircraft sound, narration captions, instruction size and interface feedback are available through the common settings page. Larger instructions reuse the existing shared panel at 115% scale rather than constructing a second interface.
- Seat calibration returns to the guide step or menu that opened it. Showroom renderer suppression excludes the existing navigation overlay, keeping startup settings and help visible.
- Guest mode creates a separate in-memory lesson-progress object. Guest completion does not write the owner's progress file. Ending guest mode restores the owner's object; starting another guest creates a fresh guest record. Changing guest mode is disallowed during an active flight/lesson attempt.
- Focus/headset interruption messaging explains that flight has paused and requests an explicit Resume. Existing flight ownership and native JSBSim authority are preserved.

Audio/display preferences are app-wide; guest isolation applies to lesson progress, not a second persistent settings profile.

## Airport terminal correction

Inspection of the source model and City 2020 orthophoto identified the terminal's detailed apron facade facing the car park. Bounded authoring rotates the existing terminal 180 degrees at its existing placement so its entrance/balcony/sign face the apron.

The existing masonry and cladding atlas regions now use physical-scale mapping, with 1.4 m and 4 m tile widths respectively. Roof, glass, signs, trim and other atlas regions retain their original mapping. This is an opt-in terminal material change: no new meshes, textures, props or draw calls.

Terminal authoring completed, followed by the existing airport lightmap refresh. `lighting.log` reports success for the single **512 × 512** `Lightmap-0_comp_light.exr`. This refresh does not introduce runtime lighting or an additional lightmap atlas.

## Validation and remaining acceptance

External evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/demo_finish_20260922/`.

| Evidence | Result / limit |
|---|---|
| Cowling authoring | Source-derived 2,520-triangle geometry and shared material recorded in `author.log`. |
| Guest presentation | Thirteen editor contracts passed; twelve focused eye images recaptured in `guest_final` after fixing new-cowling suppression in the showroom. Settings, guide/calibration return and larger-instruction captures inspected. This is authored presentation evidence, not a live XR run. |
| Guest regression tests | Both passed: actual controller completion cannot alter owner memory/file in guest mode; owner saving resumes; guide/calibration returns preserve the overlay. See `focused_tests.xml`. |
| Aircraft audio | Eleven provenance hashes verified; source alignment, loop seams and 106 voice recordings measured. Human listening remains pending. |
| Audio regression test | Four audio tests passed, including the new actual-source gain/warning independence regression check. Six tests total ran; no broad suite was requested. |
| Terminal authoring / bake | Authoring and single-atlas lighting refresh succeeded. `PASS_TERMINAL_MACHINE_QA`; apron view inspected, corrected sign/facade and masonry scale are visible. |
| Standard Visual QA | 130/130 captures generated and contact sheet inspected. Overall `FAIL_MACHINE_QA` remains: 41 materials against a planning target of 38; maximum estimated 99 draws / 1,066,583 triangles. Prior facility baseline was 99 / 1,064,063 / 41: the peak triangle delta is exactly the new 2,520-triangle cowling. No threshold was relaxed and this is not a Quest performance pass. |
| Cowling static QA | `REVIEW_COWLING_QA`: aggregate scene estimates exceed the fixture's configured triangle thresholds; footwell also exceeds its estimated draw threshold. These estimates are not Quest timing measurements and are not a pass. |
| Editor overlays | Translucent planes occur in supplemental static cockpit captures. They do not appear across the instruments in the inspected live XR seated stereo pair. This does not establish every leaning/headset viewpoint. |
| Live XR | Development build succeeded with zero build errors. `cockpit_neutral` ran the actual lesson Practice state and passed all 15 machine checks; both eye images and the stereo pair were inspected. Eye separation 0.0601 m; tracked camera owned by OpenXR; one authored hierarchy. Required replay recordings are still absent, so this is one live smoke session, not full replay-suite acceptance. |
| Android | Non-development production build passed; all 1,912 packaged scenery files verified. Self-contained Android ARM64/Vulkan/OpenXR configuration retained, native JSBSim required. APK SHA-256 `8486f02974227503cbd00c73d4658016d24bda56c9e6822ea74692c85916060d`; manifest `quest_build/product_apk_manifest.json`. |
| Headset | Installed successfully on Quest `2G0YC5ZG8907TD`; device-side `sha256sum` of the installed base APK exactly matches the build manifest. App launched and Android reports its activity resumed/focused. `quest_startup.log` retained. Worn visual/audio acceptance remains pending; no Quest performance gain or sustained thermal result is claimed. |

The final headset check should cover the nose from both occupied seating positions, footwell enclosure, readable first-use/settings panels, menu cue audibility, guest progress behavior, idle/cruise/switch/landing sound balance, and a full narrated demo through landing. Existing takeover and handback behavior should be observed during that same session. Broader renderer rewrites, additional traffic populations and speculative effects are outside this bounded milestone.
