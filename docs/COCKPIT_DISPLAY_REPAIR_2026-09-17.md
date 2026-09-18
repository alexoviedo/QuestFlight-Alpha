# Center-stack display repair — 2026-09-17

The reported defect is concentrated on the center displays, especially the lowest unit.

## Findings

The disabled original `Static_Display` renderer has not been re-enabled in the product prefab or its visibility lists. No relevant unsupported-shader error was found in the available device log buffer; that buffer is not a complete launch trace.

The replacement `ProductionStaticDisplayHardware` mesh was incompletely neutralized. The previous repair removed twelve large display-face triangles, but retained extruded legacy digits and other screen geometry. Inspection of the frozen source finds legacy surfaces roughly -1.4 to +0.6 mm from the audited display planes, competing with the replacement readouts. The lowest, unsupported unit also retained its original pink screen texture and lettering.

Isolated renders at aircraft position (10000, 500, 10000), attitude (8, 35, 12) show red legacy digits and the pink bottom screen in the hardware mesh alone. Hiding that mesh eliminates these colors; the green readout batch alone remains clean. This localizes the defect to retained imported geometry, rather than the live glyph atlas. Small camera offsets are included in the evidence. These are development renders, not proof of headset acceptance.

## Repair

- Identify the seven actual source-screen regions from the frozen mesh and remove all triangles wholly inside their thin aperture volumes, including the extruded legacy digits. Preserve surrounding knobs, bezels and controls.
- Keep the six live GPS/radio/transponder readouts and their existing 74 glyph slots.
- Give the unsupported lowest unit one opaque black quad in the existing display batch. It does not show invented telemetry or add a new avionics function.
- Retain the existing screen offset, shader, texture atlas, runtime authority and draw-call structure.
- Extend the depth-ownership contract to reject residual source geometry anywhere inside these screen volumes. Validate complete nonoverlapping coverage of all seven presentation surfaces.

The frozen-source audit removes 3,918 triangles, compared with 12 previously. With the additional two-triangle dark face, the net scene reduction is 3,904 triangles. No new draw call, texture, runtime script or per-frame work is introduced. The existing source/license provenance and asset GUIDs are preserved.

## Evidence and validation

Evidence root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/cockpit_flicker_20260917`.

- `hardware/`: before, isolated original hardware and geometry dump.
- `final/`: repaired closeups and successful full depth-ownership contract.
- `standard_qa/`: all 130 standard captures; inspected instrument-panel capture shows clear live screens and a dark lowest display. Machine report retains the same existing error entry as the accepted building-grounding baseline. Maximum estimated draws remain 97, materials 41, and visible triangles decrease from 1,026,792 to 1,022,888.
- Two focused avionics authoring tests passed before addition of the explicit depth-ownership regression test. Final test run, image analysis, stereo simulator inspection and Android installation are pending.

Physical-headset visual acceptance and performance measurement have not been performed for this repair.
