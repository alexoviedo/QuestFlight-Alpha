# KBDU airport-core reference and Workstream A model basis

This document defines the Stage-1 reference basis for the executive terminal at 3335 Airport Road and one representative Type 2 T-hangar row at 3354 Airport Road. It is an art-direction and traceability record, not an architectural survey. Project-authored models must still pass integrated Unity Visual QA, stereo review, static budgets, and physical Quest validation before they are accepted.

No copyrighted reference photograph, page screenshot, or contact sheet is committed. The inspected full-resolution sources and derived board remain outside the repository at:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_photoreal_v4_20260717_122642\workstream_a\reference_board`

The six-image contact sheet is:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_photoreal_v4_20260717_122642\workstream_a\reference_contact_sheet.png`

Per-file SHA-256 values and rights handling are in `reference_board\reference_receipts.json`.

## Sources and repository handling

| Source | Evidence used | Rights and handling |
|---|---|---|
| [City of Boulder — Airport Hangar Addresses](https://bouldercolorado.gov/airport-hangar-addresses) | Official clockwise address arrangement; terminal at 3335; Type 2 rows at 3351, 3353, 3354, and 3355; runway north of the airport buildings | City page is all-rights-reserved. The map is external reference only. Facts are attributed here. |
| [City of Boulder — Hangar and Tie-Down Leasing](https://bouldercolorado.gov/services/airport-hangar-and-tie-down-leasing) | Current official Type 2 record: built 1972–80; 3351/3353/3354/3355; 40 ft 3 in door; electricity; asphalt floor; bi-fold doors; doors face east/west | Facts only; no page media committed. |
| [City of Boulder — Airport Webcams](https://bouldercolorado.gov/airport-webcams) | Current webcam page checked for orientation/context | No stable, licensed still was retained. |
| [City of Boulder — Airport Master Plan](https://bouldercolorado.gov/projects/airport-master-plan) and its [terminal-area plans](https://bouldercolorado.gov/sites/default/files/2021-02/7-airport-master-plan-airport-plans.pdf) | Official general-aviation role, terminal area, apron, storage, support-facility, and landside context | Facts and plan context only. No plan page is embedded in an asset. |
| [Airport-Data AP7472](https://airport-data.com/airport/photo/007472.html) and [AP7473](https://airport-data.com/airport/photo/007473.html) | 10 May 2008 apron facade: lower masonry, light vertical panels, extensive glazing, balcony/rails, dark awning, address/sign bands, flagpoles, roof equipment, rocks/shrubs | Copyright Bluedharma / Airport-Data. Downloaded only to the external board; no pixels or logos are reproduced in committed textures. |
| [Wikimedia Commons — Boulder Municipal Airport.JPG](https://commons.wikimedia.org/wiki/File:Boulder_Municipal_Airport.JPG) | 29 December 2016 airport/apron scale, restrained industrial colors, small-aircraft activity, dry landscape, and foothill context | Jeffrey Beall, CC BY 4.0. A 1280 px copy remains external; no pixel is embedded. |
| [City of Boulder OpenGov airport budget](https://stories.opengov.com/cityofboulderco/published/SoI4Kud_R) | Airport-wide organization, low building heights, grouped hangars, hardscape/grass balance, and operations context | No reusable media license was identified. Two images remain external reference only. |

Reference dates span 2008–2026. Facade signs, operators, equipment, paint, vegetation, and minor structures may have changed. The models therefore reproduce stable architectural cues without claiming present-day survey accuracy.

## Coordinate and footprint basis

The geometry contract uses KBDU local tangent ENU: Unity `+X=east`, `+Z=north`; Blender `+X=east`, `+Y=north`, `+Z=up`. Exact Stage-1 source and padded clean-plate polygons are recorded in `tools/kbdu_airport_core_v4/buildings/airport_core_replacement_footprints.json`.

| Object | Stable source | ENU center (m) | Source footprint | Stage-1 handling |
|---|---|---:|---:|---|
| Executive terminal | OSM way 77434544 / official 3335 identity | `(-385.25, -224.125)` | approximately 21.5 × 43.5 m | Replace the whole photographed terminal only inside its bounded padded clean-plate polygon. |
| Type 2 T-hangar row | OSM way 78054463 / official address order | `(-392.25, -344.875)` | approximately 15.5 × 68.5 m | Replace only 3354; retain adjacent 3353/3355 V2 context for A/B comparison. |

OSM footprints are reference-grade. The replacement meshes use centered, zero-grounded pivots and preserve the source footprint envelope; final serialized placement and terrain grounding remain integration responsibilities.

## Executive terminal brief

The apron-facing facade is modeled on the east side of the long north/south footprint. Required stable cues are:

- two visually distinct levels rather than a single industrial box;
- tan/brick lower masonry and light vertical metal upper/parapet panels;
- a broad upper glazing band with individual mullions and framed doors;
- a projecting balcony deck with posts, three horizontal rail bands, and supports;
- a dark standing-seam entry awning;
- raised, project-authored `EXECUTIVE TERMINAL` and `3335` sign geometry in a neutral type treatment, with no third-party logo;
- two flagpoles without copyrighted flag textures;
- bounded parapet/roof seams, three roof units with louvers, service entries, curb segments, rocks, and foundation shrubs;
- visible edge bevels and a real foundation contact, with no hidden furnished interior.

The source footprint is 21.5 × 43.5 m. Architectural roof height is about 8.1 m; rooftop equipment and flagpoles extend above it. These heights are visual estimates rather than surveyed dimensions.

## Type 2 T-hangar-row brief

The Stage-1 representative is 3354 Airport Road. Official facts are: Type 2, built 1972–80, asphalt floor, electricity, manual bi-fold doors, and east/west-facing doors. The project model applies these facts as:

- a 15.5 × 68.5 m row divided into five repeated structural bays;
- repeated gable/ridge modules rather than a flat gray roof slab;
- east/west alternating bi-fold aviation-door faces with top/side frames, fold rails, center seams, and fine corrugation;
- light/weathered corrugated cladding using atlas variation rather than unique materials;
- standing roof seams, eave gutters, downspouts, roof vents, address plaques, service doors, and end-unit variation;
- a bounded asphalt-contact foundation and zero-grounded pivot.

The five-bay subdivision and detail spacing are visual inferences; the City facts do not provide construction drawings. The row must not be described as an exact reconstruction.

## Fuel/service and authored props

`FuelServiceCore.blend` supplies a small service building, canopy, cabinets, bollards, door/sign panels, vent stack, and roof seams. It is deliberately **not assigned a product placement**: no survey-grade public footprint for a standalone fuel structure was established. The lead must author and visually validate any placement rather than inpainting or placing it speculatively.

`AirportCoreProps.blend` supplies independently exported LODs for a tie-down anchor, cone, light pole, service cart, fence segment, and signboard. These support deliberate authored stands and service context; they are not a random-scattering library.

## PBR atlas contract

Workstream A models reference two stable, Workstream B-owned material names:

- `AirportCoreTerminalAtlas`
- `AirportCoreHangarKitAtlas`

UVs occupy the documented 4 × 4 tile contract in `airport_core_building_spec.json`, covering brick, light metal, glazing, dark roof/awning, trim, concrete, sign face, roof equipment, hangar cladding, bi-fold door, weathering, foundation, safety yellow, landscape rock, shrub, and service props. GLBs embed no images or textures. This prevents copyrighted pixels and avoids unique materials per building.

## Model and LOD evidence

Blender 5.1.2 generated four project-owned source files plus isolated GLBs for every LOD. Isolated files prevent the V3 failure in which LOD0/1/2 rendered together.

| Model | LOD0 | LOD1 | LOD2 | LOD1 ratio | LOD2 ratio |
|---|---:|---:|---:|---:|---:|
| Executive terminal | 20,172 tris | 8,592 | 2,364 | 42.59% | 11.72% |
| Type 2 T-hangar row | 25,492 tris | 10,244 | 3,444 | 40.19% | 13.51% |

Offline validation proves one renderer/material contract per isolated GLB, UVs, normals, exported tangents, zero nonmanifold edges, zero reported inverted faces, centered pivots, zero-grounded bounds, source/output hashes, paired Unity `.meta` files, and no reference pixels in the runtime tree.

These machine checks do **not** establish reference-quality appearance. Atlas binding, clean-plate integration, lighting, Stage-1 full-resolution review, stereo stability, and physical Quest performance remain required.

## V4 Stage-1 result

The source/reference and model basis remains useful, but the integrated benchmark is **not accepted**. A single targeted correction fixed a top-origin/bottom-origin atlas mismatch and materially improved terminal/hangar material assignment. The final rerun still scored the terminal 5.0/10 and the Type 2 row 4.5/10, below the required 8/10, because exposure, baked lighting, ground color, material response, and surrounding integration remain visibly synthetic. No wider airport rollout, simulator gate, headset test, or mainline merge followed.
