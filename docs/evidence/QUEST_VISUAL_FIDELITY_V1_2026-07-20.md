# Quest Visual Fidelity V1

## Scope and source

- Source branch: `main` at `b2bbfbd11804011cc440e4e00c0f532b84458668`.
- Candidate branch: `feature/quest-visual-fidelity-v1`.
- Shipping architecture remains self-contained standalone Quest 3: Android ARM64, Vulkan, OpenXR, and Built-in Forward.
- This bounded pass addresses three witnessed deficits only: short canopy-volume cues, incoherent context-building facades at close range, and weak cockpit window/ambient response.

No terrain, water, runway, mountain, sky, flight-physics, input, or world-coverage system was replaced. No new external asset was downloaded.

## Implemented result

### Canopy calibration

The deterministic Tier-2 grove distribution now records an apparent canopy height derived from the existing public-domain USGS 3DEP source and applies a bounded shadow-informed calibration: source reference `11.785 m`, multiplier `1.28`, minimum `15 m`, and maximum `26 m`. Horizontal placement and footprint remain unchanged. The final distribution contains 64 placements: 3 Tier 0, 19 Tier 1, and 42 Tier 2. Tier 2 remains one instanced draw; the authored vegetation worst case remains 35,540 triangles and one material.

This makes the fixed-world rounded grove cues taller without reviving raised terrain columns or camera-facing billboards. It is visual calibration, not surveyed tree height or a botanical inventory.

### Semantic facades

The authoritative `PhotorealAerialSurfaceV1` building pipeline now assigns one coherent facade family per OSM footprint from existing OSM usage tags and the existing project atlas. The accepted families are brick, light metal, concrete, corrugated metal, and weathered metal. Stable variation remains inside plausible families rather than selecting unrelated materials per world cell.

Sixteen existing building batches retain their GUIDs, renderer count, material count, and 128,092 triangles. Compact vertex-color metadata carries atlas family, stable seed, building base elevation, and semantic usage to the existing opaque shader. The shader adds distance-bounded, antialiased procedural windows, doors, static sky/ground reflection tint, and sun glint in the same pass. It adds no transparent pass, texture, material, renderer, realtime probe, or draw call.

The resulting close facade is materially more coherent and legible, but the context structures remain simplified OSM footprint shells. Their procedural openings repeat and are not surveyed architecture; this is not final AAA building art or literal photorealism.

### Cockpit presentation

The existing opaque/static cockpit window reflection contract now has a stronger low-alpha visibility response and bounded sun glint. Serialized windshield/side-window opacity, Fresnel, reflection, glint, and roughness values were retuned through the existing deterministic authoring path. Baked cockpit occlusion strength increased from `0.32` to `0.64` while the realtime cockpit shadow profile remains disabled.

This adds no realtime reflection probe or cockpit shadow map. Window reflection and baked occlusion require a final worn check on the exact APK; Editor captures cannot establish worn visibility, flicker, or comfort.

## Validation

- Aerial-surface Python tests: **22/22 PASS**.
- Vegetation Python tests: **9/9 PASS**.
- Vegetation asset validator: **PASS**.
- Unity EditMode: **114/114 PASS**.
- Unity PlayMode: **9/9 PASS**.
- Production Visual QA: **81/81 PASS**, classification `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`; every affected full-resolution frame was inspected.
- Static Quest planning gate: **PASS** at 54 maximum selected draws, 624,086 maximum visible triangles, 774,139 serialized scene triangles, 26 visible materials, 211,849,528 estimated texture bytes (202.04 MiB), three transparent slots, 13 shaders / 18 keyword combinations, and 4x MSAA.
- Meta XR Simulator: live Quest-3-profile stereo, HMD, and both Touch controllers **PASS** with nonblank, scene-consistent eyes; aggregate remains `BLOCKED_MISSING_RECORDED_REPLAYS` because all ten genuine `.vrs` sessions are absent.
- Android release-style build: **PASS**; APK is 133,529,570 bytes, SHA-256 `643f377140eb641c6ef921047bd7c044d61e672e91e9a67c4bd6232950d961e6`.

Meta XR Simulator timing is not Quest performance. The static report is planning evidence, not physical frame, thermal, stale/tear, allocation, or comfort acceptance.

## Human evidence boundary

The immediately preceding worn build scored **8/10** and confirmed aligned buildings, improved terrain topology, no noticed stereo instability, and no discomfort. That APK was 133,992,887 bytes with SHA-256 `0d31c39f113c3ed5edf286259f35f25cf18852b0129445621b79bf32999851d9`.

The final APK documented above is different and has not been worn. The prior 8/10 score motivated this pass but is not attributed to the final binary. A later short exact-APK worn smoke should check canopy height, close facade scale/semantics, window reflection visibility, and cockpit interior response before any release-quality claim.

## Remaining limitations

- Tier-2 canopy remains 42 representative project-authored clusters rather than continuous surveyed grove volume; variety and coverage remain limited.
- Context buildings remain simplified footprint shells. Windows and doors are procedural shader detail rather than authored geometry, and some close views remain below final AAA-quality expectations.
- Cockpit reflections are static/bounded and interior depth uses baked ambient occlusion; realtime cockpit shadows remain intentionally disabled until physical stability and timing are proven.
- No final long-duration physical Quest performance acceptance exists for this exact APK.
- Genuine deterministic Meta XR Simulator replay files remain missing.
- No FAA approval, navigation suitability, training credit, final flight-model fidelity, or literal photorealism is claimed.

## Artifacts

Closure artifacts are external to Git under:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\quest_visual_fidelity_v1_20260720_234643`

Important evidence includes `visual_qa_authoritative_final`, `performance_budget_final_rerun`, `simulator_final`, `editmode_final_rerun`, `playmode_final`, and `build_final`.
