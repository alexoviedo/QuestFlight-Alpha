# Cockpit menu clearance and pedal repair

## Defects and repairs

The shared navigation layout placed content 1.03 m ahead of the nominal eye and grew downward. This put long menus through the cockpit; overlay materials then drew content through the instrument panel. The panel face is approximately 0.547 m ahead, but the glare shield projects farther back, so panel-face distance alone was not a sufficient clearance rule.

Navigation now scales geometry, depth and hit targets together to 0.42 of their original size, retaining nominal angular text size. The menu plane is approximately 0.433 m ahead. Its bottom edge stays 0.025 m above the nominal eye so long content clears the glare shield. It is anchored to the authored seat reference, independent of head tracking and seat calibration. Briefings, debrief, options, settings, guide, calibration and lesson selection share this rule. Instruction cards use the same cabin-space approach at 0.46 scale; the existing larger-text preference remains functional. Modal surfaces and text use depth testing. Instrument reference labels and highlights are unchanged.

The missing heel was a geometry defect. The previous fourth-pedal derivation copied isolated parts but omitted a shared welded floor/heel component spanning the pilot pair. The source also contained consumer-pedal-style central hardware; its component ownership moved parts of that base with a toe brake. Rudder and toe-brake pivots coincided at the top of the footplates.

The repaired assembly uses four complete copies of the intact source footplate, backing and tread, preserving UVs, normals and mirrored tangent/visibility conventions. It replaces the incomplete floor hardware with fixed bearing blocks, two transverse rudder bars, four support arms and separate toe hinges. Pilot and copilot pedals remain linked to the existing authoritative rudder/brake channels, with rotation about fixed axes. Flight physics and input mappings are unchanged.

Mechanical reference: Cessna 172 service manual, Figure 10-2, printed page 10-5 (PDF page 264), shows separate rudder bars, bearing blocks, pedal pivot shafts and brake links. [Manufacturer manual, hosted mirror](https://www.aeroelectric.com/Reference_Docs/Cessna/cessna-maintenance-manuals/Cessna_172_1977-1986_MM_D2065-3-13.pdf). The visible linkage is an economical representation; it does not claim exact maintenance dimensions or control travel.

## Cost and provenance

- Previous assembly: 18,903 vertices / 16,913 triangles.
- Repaired assembly: 15,440 vertices / 13,024 triangles, a reduction of 3,889 triangles (23%).
- Still one skinned renderer, one existing material, nine bones, eight existing presentation bindings. No additional runtime textures, physics or simulation systems.
- Source: KOG_THORNS C172, CC BY 4.0, original source SHA-256 `a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872`.
- Generated mesh SHA-256 `aa13dd4a58df306ca67c7cc8355006657a458b2c82a98880fd43ba7c030ecdb9`.
- Existing mesh and prefab GUIDs preserved. Derivation manifest updated to v2.

## Validation

Artifact root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/cockpit_clearance_pedals_20260922` (work began September 22).

- `review/screenshots`: 34 targeted images, including both eyes, forward lean, all six lesson briefings, settings, calibration, guide, debrief, longest instruction, seated footwell, and neutral/rudder/brake/combined pedal poses. Inspected representative images from each affected class. Isolated motion captures bake the actual skinned pose before rendering; edit-mode controller rays are suppressed only in the diagnostic fixture because they have no tracking update.
- `pedal_test.xml`: one focused existing mechanical contract test passed. Checks equal complete footplates, fixed-bearing ownership, separate hinges, rigid triangle ownership, one draw/material, all eight bindings, linked rudder motion and independent left/right toe brakes.
- `standard`: all 130 standard captures completed. Hierarchy/environment contracts pass. Existing global material-budget failure remains: 41 materials versus target 38. Peak estimated draws/triangles/materials: 99 / 1,062,694 / 41. This is not an overall Visual QA pass and not physical Quest performance evidence.
- `meta_xr_simulator/qa_run/sessions/cockpit_neutral`: live OpenXR stereo session machine gate passed; inspected stereo menu and both downward footwell eye views. No cockpit/menu intersection or missing pedal heel in these views. Replay orchestration still reports missing recordings because this was a live session, not a prerecorded replay.
- Android ARM64 / Vulkan / OpenXR build passed; all 1,912 scenery payload files verified. APK SHA-256 `07bfa576005225f63722c95502b7073f842d7d8c26a8e42d610127761cc44a5d`. Installed on Quest 3 `2G0YC5ZG8907TD` with `adb install -r`; installed APK hash matches the validated build. Launched successfully. Worn visual acceptance remains pending.

## Headset check

Open Flight lessons, then Settings and Seat position; check readability and clearance while leaning normally. In free flight, look down at both pedal pairs while applying rudder left/right and toe braking. Confirm all four footplates remain complete and the floor bearings stay still. Comfort/readability at the new menu depth still needs user acceptance on hardware.
