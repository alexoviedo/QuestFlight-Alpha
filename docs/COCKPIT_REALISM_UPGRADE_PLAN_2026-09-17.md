# Cockpit realism upgrade — proposed September 17, 2026

**Status: remaining cockpit enhancements authorized and implemented September 17.** The first material/readout pass was accepted on Quest; the integrated six-pack, cabin finish and reflection candidate is now awaiting its own headset acceptance. See [implementation and evidence](COCKPIT_TRAINER_FINISH_2026-09-17.md). Latest user direction supersedes the broad validation sequence below: no simulator session or broad automated suite; only necessary geometry inspection, gauge calibration/depth checks, Android build and exact-package installation before the worn check.

The best next investment is a coherent refresh of the instrument panel, glare shield, yokes and nearby cabin trim. Improve their artwork, material response and depth together, then tune the existing optics. Adding more reflections to the current panel would leave its largest discrepancies intact.

**User direction:** this should look like a practical trainer, not an expensive all-digital cockpit. Use conventional round instruments, basic radios and the existing simple GPS. Aim for maintained, lightly used materials, modest wear and clear controls. No glass-panel conversion, luxury cabin treatment or new digital avionics features.

## Evidence actually inspected

Current project evidence:

- September 17 repaired cockpit: `../T2-QuestFlight-product-artifacts/cockpit_flicker_20260917/standard_qa/screenshots/03_instrument_yoke.png` and `pasv1_23_instrument_glass.png`; repaired isolated center stack under `cockpit_flicker_20260917/final/screenshots/`.
- Existing live stereo simulator cockpit: `../T2-QuestFlight-product-artifacts/building_grounding_20260917/simulator/meta_xr_simulator/qa_run/sessions/ambient_foothill/ambient_foothill_stereo.png`. This predates the September 17 display repair, but establishes that the unusual dial artwork/layout and flat appearance also occur in a running stereo player.
- Current prefab, instrument/display generators, shaders, materials, and `ProductionAircraftLightingController`; July reflection/shadow/completion evidence and recent performance records.

The side-window Editor capture contains the previously documented large gray obstruction. It is unsuitable for judging cabin transparency and is **not** evidence that the headset has that particular defect. It must not become the reference for an art adjustment. Existing captures suffice for this proposal; another headset session is unnecessary now.

Real references inspected visually:

- [G-BOIL C172N instrument/radio closeup, Stuart Roberts](https://commons.wikimedia.org/wiki/File:Cessna_172N_GBOIL_Interior_instrument_panel_%26_radio.jpg): recessed dial faces, dark lens rims, localized reflections, worn control edges and different radio display technologies. Reference photograph, CC BY-SA 4.0.
- [Conventional C172 instrument panel, Giovanni Handal](https://commons.wikimedia.org/wiki/File:Standard_instrument_set_up_in_a_cessna_172.jpg): principal instrument arrangement, colored airspeed arcs, gauge recesses, panel grain and placards. Reference photograph, CC BY-SA 3.0; exact subvariant is not established by its caption.
- [Operator's N734PU, a modified 1977 C172N](https://www.airplanesandattitude.com/n734pu): distinct matte panel, polished yoke grips, black rubber knobs, metal control collars and mixed display colors. This retrofit is evidence of material behavior and legitimate variation, not a panel to copy wholesale.
- [Textron Skyhawk brochure, pages 5–6](https://cessna.txtav.com/-/media/cessna/files/brochures/piston/skyhawk_brochure.pdf): modern panel/cabin photographs. Useful for molded trim, upholstery and broad highlights; its G1000 equipment is not the target for this analog cockpit.
- [Garmin GPS 400 guide, printed page 1-11](https://static.garmin.com/pumac/GPS400_PilotsGuide.pdf): a structured color navigation display, unlike our three lines of stretched green text under a GPS 400 bezel.

Reference copies/rendered pages are outside product content in `../T2-QuestFlight-product-artifacts/cockpit_realism_research_20260917/`. Photographs/manual artwork are observation references, not approved game textures. Author new artwork or derive it from the existing licensed source with recorded provenance.

## What differs, and what matters most

| Priority | Current observation | Proposed improvement | Expected cost mechanism, not a measured saving |
|---|---|---|---|
| 1 | The panel, coaming and yokes contain broad nearly uniform dark areas. Gauge faces stand out like bright decals against them. | Distinguish painted panel, textured glare shield, smooth yoke grips, rubber knobs, metal collars and light cabin trim. Use restrained grain, edge wear, seams and local contact shading. | Replace existing maps and tune existing material inputs; avoid additional passes. |
| 1 | The pilot cluster includes a 400-knot scale, MACH lettering and an unusual arrangement, with the attitude indicator low in the panel. Some warning-looking labels remain visible in the running capture. | Create a coherent conventional C172 instrument presentation, using the existing native data channels. Correct units, scale, tick spacing, typography and unsupported decorative instruments. Treat moving instrument positions/calibration as a bounded panel task. | Reauthor the existing combined mesh/atlas rather than create one renderer per instrument. Functional verification is required. |
| 2 | Center displays share one green 5×7 font, with heavily stretched characters; the larger GPS lacks a credible visual hierarchy. | Give basic radios appropriately proportioned digits and separators; clean up the existing GPS data layout. Match display color to positively identified equipment instead of tinting every unit identically. Keep the equipment simple. | Reuse the opaque glyph batch and atlas. No UI canvas, display camera or live map render texture. |
| 2 | Gauge rims/adjusters have weak depth and several knobs appear chalk-white. | Clear bezel lips, shallow recess shading, restrained metallic highlights and properly finished adjustment knobs. | Bake fine details; change geometry only where the pilot sees a silhouette or parallax difference. |
| 3 | Gauge reflections exist but do not consistently make the faces read as glass; the stereo windshield has broad reflection streaks. | Rebalance reflection content, tint and angle response. Keep a subtle window-shaped sheen and preserve outside visibility. | Tune the existing lens/window passes and recapture the existing cabin cubemap offline. |
| 3 | Large cabin surfaces lack the material distinction visible in real trim and upholstery. | Improve visible door trim, seals, seat edge and headliner after the forward panel reads well. | Reuse existing texture channels and shared materials; rear-cabin detail is lower priority. |

These are visual judgments from the inspected images. Photos have different exposure, camera position and equipment; they are not calibrated brightness measurements. The presence of a warning flag in one capture does not establish a logic fault. Check its underlying state before changing it.

## Existing rendering capabilities to retain

This is not a missing-effects problem:

- The stable interior shader already samples base color, normal, metallic/roughness, baked occlusion, emission and a local shadow map. Its normal strength, ambient floor, warm bounce, enclosure and roughness response can be tuned without introducing another material system.
- The aircraft already renders a **512×512 cockpit-local shadow map**, using a small authored proxy, every `LateUpdate`, and generates its mip chain. Keep that existing mechanism; do not add a second shadow system. Previous sparse shadow sampling produced unacceptable edges.
- Gauge lenses already have a dedicated additive reflection pass with Fresnel and sun glint. Window glass already combines cabin and environment reflections in aircraft coordinates.
- The opaque instrument shader currently returns sampled face artwork without illumination modulation. That helps explain the decal-like separation from the shaded panel. A small bounded ambient/directional response is a candidate; printed faces and actual luminous digits must remain separate.
- Reflection-looking properties serialized on `ProductionCockpitInstrumentV2.mat` are not consumed by its current opaque shader. Tuning those stale values would accomplish nothing. Tune the active lens material/shader instead.
- The avionics use a single opaque mesh, 74 dynamic glyph slots and a small project-authored atlas. Preserve this batching and the newly repaired single ownership of every screen surface.

## Implementation sequence after approval

### 1. Lock the design and protect working behavior

Use a conventional analog C172 presentation compatible with the project's native C172P-derived authority, retaining its existing GPS/radio capability. Do not introduce G1000 or a full new avionics simulation.

Make one annotated panel layout from the available references. Inventory every visible dial, its actual data source, aperture, pointer/card, glass and control. Verify variant-specific markings against a suitable C172P reference and the current simulation configuration before authoring them; the N-model photographs establish visual conventions, not P-model operating limits.

Retain the latest repaired APK/source checkpoint. The previous repair's Android package was installed and hash-verified; its stereo validation remained incomplete at the disk-space pause. Do not describe that repair as fully accepted or rebuild its old defect into this candidate.

### 2. Produce the first substantial visual result

Rework the complete forward-facing material set together: charcoal painted panel, matte textured glare shield, subtly smoother yoke grips, rubber knobs, metal control collars, neutral trim. Keep a maintained-aircraft appearance, with localized handling wear instead of uniform dirt or scratches.

Bake narrow bezel/contact cavities and material microdetail into existing atlas channels. Paint no fixed directional sun highlight into the aircraft: it would rotate incorrectly during heading/bank changes. Preserve clear readable labeling, with registered artwork rather than enlarged generic glyphs.

Reauthor the six principal instruments as a coherent group: airspeed/attitude/altimeter above turn coordinator/heading/vertical speed. Reuse the existing native-backed functions and combined mesh. Reposition full instrument assemblies, including faces, pointers, lenses and adjustment controls, as authored geometry; moving a painted face alone is unacceptable. Remove obsolete source triangles rather than cover them with competing surfaces. Retain unrelated flight-control pivots, interactions and cabin dimensions.

Replace inappropriate decorative dials with suitable blanking plates unless a real corresponding instrument and authority are established. Retain supported useful indications in credible auxiliary positions. Airspeed artwork and needle calibration must change together; the new face cannot simply be pasted over the old 400-knot mapping. Model-unavailable or failed instruments remain visibly unavailable, never animated from invented values.

This panel reauthoring is the main implementation risk. If the source topology prevents a clean conversion, author a replacement **panel module** using the existing licensed cabin and controls, rather than trying successive overlays or replacing the entire aircraft. That work remains inside this proposed scope and must preserve the renderer budget.

### 3. Refine displays and light response

Replace stretched radio characters with readable, equipment-appropriate typography. Clean up spacing and labels in the existing navigator using only existing authoritative fields; a new GPS interface or graphics feature is not necessary for this trainer milestone. It remains a simplified simulator display: no fake live map, fabricated CDI, or implied full Garmin feature set. Leave the unsupported lowest display dark with a credible physical face.

Tune diffuse cabin fill and contact depth together. The panel should show subtle material variation in shade while the footwell remains darker. Do not raise global exposure or dim every instrument to compensate for one dark material. If a bounded lighting multiplier is added to printed dial faces, keep luminous content independently readable.

Finish by regenerating the cabin reflection texture offline from the changed interior and dialing the active lens/window effects. Inspect each eye, looking for reflections sliding incorrectly with head motion, repeated bright blobs, reflected stale screen text and reflections masking outside traffic. Keep static reflected instrument content indistinct rather than presenting frozen readable telemetry. No extra transparent screen covers or smudge layers.

### 4. Validate one integrated candidate, then request one final worn session

Use normal pilot views: forward flight, glance down at instruments, center stack, yokes/controls, left/right windows and modest lean. Include startup/unpowered and powered flight, headings toward/away from the sun, banking both ways, and airport/far-map coordinates. This exercises the depth precision and banked lighting issues already encountered.

Inspect full-resolution stereo simulator captures and short motion sequences, then standard Visual QA. Detached closeups are diagnostic only; the improvement must be obvious at the normal seat distance. If only a magnified comparison reveals the change, the art pass is not finished. No repeated user headset sessions to choose tiny parameter differences.

Use focused instrument checks for calibration, units, power/failure presentation, source-surface removal, glass ordering, interaction alignment and truthful native data binding. Preserve one XR Origin, tracked camera and native flight authority. Build Android ARM64/Vulkan/OpenXR, verify/install the exact APK, then conduct one prepared worn check of readability, reflections and frame timing. Inability to complete stereo/device validation remains an explicit open gate.

## Quest 3 cost limits

Target **no repeatable frame-time regression**, not an assumed allowance for more effects. Existing performance acceptance is still open; historical scene timing is not a measurement of the cost of this proposal.

For the first candidate, require zero additional cockpit draw calls, transparent passes, real-time lights, render textures or cameras. Aim for unchanged visible triangle count by reallocating existing geometry. Reuse texture dimensions/channels first; permit at most **4 MiB net additional resident texture memory** as a proposed implementation cap, verified from Android import/build data. It is not a measured amount of free memory. Any extra fragment arithmetic must be checked on device even if draw counts stay constant.

Keep the accepted High fixed foveation, resolution and MSAA during comparisons. Budget using actual cockpit-facing pixel coverage: a small panel mesh can still cover a large part of both eye buffers. Do not add aggressive cockpit LOD that pops during a head turn or cull valid side-window views.

Meta recommends restrained draw counts, texture bandwidth and fill rate, shared materials, texture compression and avoiding overlapping transparency/full-screen effects. Apply those principles rather than treating generic historic triangle limits as our measured Quest budget. [Meta Android rendering guidance](https://developers.meta.com/horizon/documentation/unity/unity-mobile-performance-intro/).

Use baked/custom reflection content where suitable; realtime cubemap refresh has materially higher processing overhead. Our moving aircraft needs aircraft-local coordinates for cabin content and separate world-relative illumination, not a fixed world lightmap attached to the plane. [Unity reflection probe types](https://docs.unity3d.com/6000.0/Documentation/Manual/RefProbeTypes.html), [Android mobile lighting guidance](https://developer.android.com/games/optimize/lighting-for-mobile-games-with-unity).

Exclude screen-space reflections/AO, realtime reflection probes, ray tracing, full-screen bloom, depth of field, motion blur, chromatic aberration, glass refraction and a renderer migration from this milestone. They do not address the observed panel/artwork mismatch efficiently. Shadow caching is only a later measured optimization opportunity, not an assumed source of budget for this work.

## Approval deliverable

One coherent cockpit candidate with a recognizable conventional C172 panel, credible material differences and recessed instruments, properly proportioned live readouts, subtle stable glass, and preserved control behavior. Present inspected normal pilot views and honest cost/validation results. Approval of this document authorizes that implementation sequence; this research turn does not implement it.
