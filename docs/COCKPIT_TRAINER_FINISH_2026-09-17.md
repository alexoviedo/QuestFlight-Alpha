# Trainer cockpit finish — September 17, 2026

**Status: accepted by the user in the worn Quest session on September 17. This cockpit pass is complete for now; remaining enhancements are deferred.**

## First candidate scope

User approved the cockpit realism plan and prioritized the fastest useful headset presentation with minimal testing. This first candidate changes existing artwork/materials, not instrument placement or simulation:

- Neutral charcoal painted panel, retaining the black radio-stack blanking plate and original cabin trim. Fine material variation is generated offline into the existing UV layout.
- Subtly lighter, smoother molded yoke finish. Both yokes now use the same extracted material; the pilot yoke had still referenced the imported all-black source.
- Darkened neutral paint in the source dial atlas, preserving colored markings, UVs, scale calibration and live indication geometry.
- Physically proportioned radio/transponder characters and moderately condensed GPS text, with less saturated green ink. Existing authoritative values and 74 glyph slots retained.
- The repaired seven-aperture display ownership remains intact: six live displays and one unpowered lower unit; no layered screen covers or additional transparent passes.

Generation is reproducible through `QuestFlight.Editor.CockpitTrainerFinishAuthoring.Build`. It reads frozen source textures instead of repeatedly modifying the previous output. Source/license provenance is recorded in `C172_TrainerFinishManifest.json`. No outside cockpit photographs are embedded in assets.

The following evidence describes the accepted first pass. The subsequently authorized integrated enhancement is recorded below.

## Validation and cost

Evidence root: `../T2-QuestFlight-product-artifacts/cockpit_realism_20260917/`.

- Authoring and existing display-depth ownership validation succeeded.
- Standard Visual QA completed 130 captures. Inspected forward, panel and instrument-glass views. An initial patchy radio-stack mask was found and corrected before the final captures.
- Authored hierarchy and environment contracts pass. No newly failing QA shot IDs versus the display-repair reference. The existing 26 estimated-triangle budget failures remain; this is not an overall performance pass.
- Maximum estimated draws unchanged at 97; maximum estimated visible triangles unchanged at 1,022,888. Scene material count falls from 41 to 40 through the shared yoke material. These are Editor estimates, not Quest frame timing.
- No new geometry, render passes, cameras, lights or shader texture samples. Three new texture assets total approximately 1.21 MiB of ASTC 6×6 mip payload, calculated from authored dimensions/import settings. This is a conservative added-payload bound, not a measurement of net device residency. Old embedded textures may remain packaged with the source GLB.
- Broad automated suite intentionally skipped per user direction. No physics change was made.
- A rebuilt simulator player completed a live forward stereo capture and three temporal eye pairs. Inspected the stereo capture: updated panel and both yokes visible; live radios present; lower unsupported screen dark. The replay report remains `BLOCKED_MISSING_RECORDINGS` because no recorded replay exists, not a full simulator acceptance. The user requested bypassing further simulator work; the already-running session had completed by then. No additional simulator sessions requested.
- Android ARM64/Vulkan/OpenXR build passed; all 1,912 scenery files verified. Installed and launched normally on Quest `2G0YC5ZG8907TD`; device package SHA256 matches `0b09b8e5895606b031f1d153ecd4c3655e258e458af1830abe8d768609f9c735`.
- Worn visual acceptance: user confirmed “This is fine for right now. We can consider this done for the time being.” Inspected the physical headset capture in `headset/current.png`. No further changes or tests requested.
- Physical Quest frame/thermal acceptance is not established by this short visual session. Incidental device logs are retained; they are not a matched performance comparison.

## Short headset presentation

Use ordinary Free Flight in the C172. Look down at the **painted panel around the gauges**, then at **both yoke grips** and the **center radios/transponder**. The panel should separate from the darker glare shield, the yokes should retain visible form in shade, and the digits should look less stretched. Turn/lean gently to check that the center displays remain stable. Confirm readability at normal seating distance before calling the candidate accepted.


## Integrated deferred enhancements — September 17

Implemented after the instruction to complete the deferred cockpit scope:

- Conventional six-pack: airspeed / attitude / altimeter above turn coordinator / heading / vertical speed. Authored mesh islands and live binding pivots move together. Existing case rims are reused at destination apertures; flight controls and tracking transforms are unchanged.
- Project-authored 0–180 KIAS and ±2,000 FPM faces with matching native-backed needle mappings. C172P POH figure 2-2 supplies white 33–85, green 44–127, yellow 127–158 and red 158 KIAS markings. This is a simplified trainer presentation, not a factory instrument replica. Native availability and invalid-value checks remain.
- Six unsupported or abandoned apertures receive matte blanking plates after removal of source face geometry. Retained auxiliary instruments and compass preserve existing authority.
- Dark rubber adjustment controls, retained bezel depth, neutralized brown cabin trim, distinct seat fabric/belt/hardware roughness. Existing panel/glare-shield/yoke finishes and live radio typography remain.
- Offline cabin cubemap recaptured from the updated interior, excluding disabled obsolete screen geometry. Existing window/lens reflections and glints reduced for readability. Eight planar gauge lenses remain in the same batch; obsolete decorative lenses are removed. The existing spherical attitude display remains.

Authoring: `QuestFlight.Editor.CockpitInstrumentV2Authoring.BuildCompleteTrainer`, followed by `PhotorealAerialReflectionAuthoring.RefreshTrainerCabinReflection`. These are Editor asset-generation operations, not runtime bootstrap. Provenance is in the trainer finish and existing instrument manifests. New dial artwork is project-authored; reference photographs are not embedded.

### Minimum critical validation

Evidence: `../T2-QuestFlight-product-artifacts/cockpit_trainer_complete_20260917/`.

- Targeted Editor captures caught and corrected disconnected geometry during relocation. Final pilot-seat panel captures cover level flight and ±25° bank. This is art inspection, not simulator, physics or Quest timing evidence. The documented gray side-window Editor obstruction is not treated as a new headset defect.
- Authoring checks every new IAS/VSI needle against the artwork using actual generated mesh, pivot and runtime rotation formula at rest, climb/descent and scale limits: passed. Existing display-depth ownership, generated layout and hierarchy/environment checks: passed.
- No simulator session, broad automated suite or scenery comparison was run for this task, following explicit user direction. Existing Editor scene-budget thresholds still report excess estimated draws/triangles; this is not a Quest performance pass.
- No additional cockpit renderer, shader pass, runtime camera/light, reflection capture or texture sample. Existing dial atlas expands from 1024×512 to 1024×1024, adding approximately 0.30 MiB of ASTC 6×6 mip payload. Actual residency/frame cost remain unmeasured. Removed source surfaces and decorative lenses are not hidden under extra transparent covers.
- Android ARM64/Vulkan/OpenXR build passed; all 1,912 scenery files verified. Installed and launched on Quest `2G0YC5ZG8907TD`; device APK SHA256 matches `db7f7cda5f8ab5c8eaa27cdaf63b543f7d55f0774989dfc0a4189ee931f7c2e7`. Integrated worn visual/frame/thermal acceptance remains pending.

### Headset check

Use C172 Free Flight. Look at the six principal gauges directly ahead: blue/brown attitude indicator **top center**, airspeed **top left**, vertical speed **bottom right**. Look at dark adjustment knobs, the surrounding panel and cabin trim. During taxi/takeoff/climb confirm airspeed and climb indication respond; gently move your head to check gauge and center-display stability. The earlier accepted texture-only pass does not accept these subsequent changes.

### Headset follow-up: left blanking-cover perimeter gaps

User reported see-through gaps around the two leftmost round blanking covers. Narrow correction: increase only those two existing opaque backing discs from 1.07 to 1.28 times their authored aperture radius, extending beneath the rims. Same vertex/triangle counts, materials and renderers. The generator preserves the correction; an idempotent asset patch avoids regenerating the cockpit. Simulator and visual-suite runs omitted at the user's request. Android build passed; installed and launched on Quest. Local/device APK SHA256 verified: `2f07e918dad2a7f4732ca4006f573ea25b276514672e20e0294eb245a972af72`. Worn confirmation pending.


### Follow-up: fit covers to the actual bezels

The 1.28-radius patch sealed the gaps but was oversized. Inspection of the frozen GLB ButtonPlate vertex islands 10562 and 11730 found that their planes differ from the dial-derived cover planes; the former gyro cover was also centered using a gyro pivot projected onto another dial. Corrected both covers to the measured bezel centers/normals. Large/small model-space radii are now 0.003900 / 0.001945, within the measured outer rims 0.003948 / 0.001968, with shallow recesses and overlap beneath the inner rim. The generator and idempotent two-disc patch share these measured frames. No added vertices, triangles, materials or render passes. No simulator or broad tests. Android build passed and installed APK hash verified: `38ecd803d811f2bd8a4e684679a3a47fea5bde0f3572ed081faba9823382eed9`. Launched on Quest for the user; worn sizing confirmation pending.


### Follow-up: attitude display mounting alignment

The relocated attitude assembly retained an off-center reference inside the top-center bezel. Raw GLB plane comparison suggested a 7.8-degree difference, but the actual imported-mesh alignment measured zero additional rotation and a 0.002323182 model-unit positional correction. Corrected the entire 1,138-vertex assembly (fixed miniature aircraft plus 998-vertex gyro sphere), sphere pivot and bank-axis reference together against the measured destination bezel. No pitch/roll input offset, physics change, new geometry or rendering pass. One focused Editor panel capture was inspected to confirm centering; no simulator or broad suite. Android build passed, installed and launched on Quest; local/device SHA256 matches `5309d9e4830d8171eb18550c6da2787d87721abbeb1cf06808914bb0324ec0d0`. Worn confirmation pending.


### Follow-up: separate gyro-ball depth from fixed-reference alignment

Root causes: the previous common translation pushed the sphere pivot deeper while centering the protruding fixed symbol; computing a normalized cross product from tiny imported triangle edges produced a zero normal and silently skipped the intended mounting rotation. The corrected authoring normalizes edges before crossing, regenerates only the 998 gyro vertices from the frozen source, independently centers its axis, restores pre-regression bezel depth 0.003262830 model units, and applies the measured 7.783579-degree mounting correction. The pivot matches the transformed ball. The fixed symbol retains its in-plane center and is placed in front of the sphere's maximum envelope with fit-error clearance, avoiding occlusion for pitch/bank rotations. No flight-data offset, physics change, new geometry, material or pass. Focused Editor captures inspected; no simulator/broad tests. Android build passed, installed and launched on Quest; local/device APK SHA256 matches `e7501ae36c5164675458867d493cb7429ac515d6b4b0dc0b5337853ee7531758`. Worn confirmation pending.


### Follow-up: floor fuel selector detents

Cause: the enum OFF/LEFT/BOTH/RIGHT (0/1/2/3) was linearly mapped from -90 to +90 degrees, yielding -90/-30/+30/+90 rather than the plate's quarter-turn detents. Four focused Editor images establish the authored pointer at LEFT=0, BOTH=-90, RIGHT=180, OFF=90, with a correct spindle pivot and local-Y axis. Changed only the fuel-selector binding endpoints in the serialized prefab and its generator to +90/-180, producing OFF=90, LEFT=0, BOTH=-90, RIGHT=-180. Native fuel selection, touch commands, control geometry and other bindings unchanged. No simulator or broad suite. Android build passed, installed and launched on Quest; local/device SHA256 matches `dbf90628a16d24febbce3f7659c4f1209a0bdd4467eb7fb4eaabac9069ca1ea5`. Worn confirmation pending.
