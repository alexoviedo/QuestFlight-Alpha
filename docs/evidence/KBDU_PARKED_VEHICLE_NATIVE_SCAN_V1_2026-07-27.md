# KBDU Parked-Vehicle Native Scan V1

## Outcome

The former sparse/synthetic parking placement has been replaced by a
source-backed full-map authority. The product now contains 27,948 parked
vehicle poses recovered from native 2020 City of Boulder nine-inch imagery.
The runtime remains standalone-Quest-bounded through spatial cells, fixed
visible caps, shared meshes/materials, and allocation-free selection.

This is visual context, not current parking occupancy, navigation data, or
surveyed ground truth.

## Source and offline method

- Source: City of Boulder `AP2020Cached9inWM`, ArcGIS item
  `22b7c93a74394696824258f807f345ba`.
- Acquisition: spring/summer 2020.
- Native scan level: 19, `0.228595274 m/px`.
- License: CC0 1.0 Universal.
- Raw source tiles: external artifact/cache only; not committed.
- Detector: external GeoDeep 0.9.13 / ONNX car model, AGPL-3.0 authoring tool
  only; neither package nor model is committed or shipped.
- Detector model SHA-256:
  `4875d27f34985a080c4def32bc9408b9820e4178e55acdc5fcc185e0e8f29a56`.
- Position/orientation: native source pixel center plus locally fitted long
  axis, converted offline into the KBDU local tangent frame.
- Semantic filter: rejects OSM roads/highways, aeroways, water, building
  footprints, and candidates without nearby building context.

The scanner searched a 100 m source envelope around all 27,679 OSM buildings
in the 12 km product area, plus the developed Boulder Reservoir parking
envelope. It found accepted source evidence at 8,754 buildings. The remaining
18,925 buildings receive no invented car. Final accepted count is 27,948 after
removing 16 otherwise accepted detections beyond the runtime ground-image
boundary.

## Runtime representation

- 378 fixed 500 m spatial cells.
- 64 maximum near/detail instances.
- 1,800 maximum far/proxy instances.
- 800/1,000 m near enter/exit hysteresis.
- 4,200/4,500 m far enter/exit hysteresis.
- Three far batches sharing two proxy meshes.
- Nine opaque instanced draws maximum.
- 129,312 selected vehicle triangles maximum in current authored output.
- One existing shared material and 128-square detail texture.
- No per-car GameObjects, colliders, rigidbodies, realtime shadows, transparent
  vehicle glass, runtime detector, source imagery, or steady-state allocation.

## Validation and inspected evidence

- Python manifest/source validation: PASS, including exact authority hash and
  ID parity, product extent, reservoir coverage, all-cell continuity,
  collision-safe moving traffic, and fleet/color variation.
- Unity EditMode: PASS 133/133.
- Unity PlayMode: PASS 9/9.
- Machine Visual QA: PASS 97/97,
  `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`.
- Android ARM64/Vulkan/OpenXR build: PASS.
- APK: `QuestFlight/Builds/Android/QuestFlight-production.apk`
  (external, not committed), 137,099,870 bytes.
- APK SHA-256:
  `529f2a715585f4b11d212b34b2f5c8690751681fbca809ff59c371cf9d18778b`.
- Worn Quest 3 review: PASS at 9/10. Vehicle placement, density, and alignment
  were accepted; the stronger retained-selection preference and expanded
  far-proxy pool materially reduced the previously reported popping without
  observed stutter, discomfort, or misplaced cars.

External evidence root:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\traffic_fleet_v1_1_20260727_152609\parked_vehicle_scan_exact_v2`

Key inspected evidence:

- `full/parked_vehicle_native_review_contact_sheet.jpg`
- `full/parked_vehicle_reservoir_exact_overlay.jpg`
- `full/parked_vehicle_map_overview.png`
- `full/parked_vehicle_native_scan_report.json`
- `product_validation/visual_qa/screenshots/32_traffic_reservoir_parking.png`
- `product_validation/visual_qa/screenshots/33_traffic_airport_parking.png`
- `product_validation/visual_qa/visual_qa_contact_sheet.png`

The native review contact sheet shows detector centers and long axes aligned
with photographed vehicles across dense parking rows. The reservoir and airport
product views show the streamed 3D fleet in the expected developed contexts.

## Budget boundary and limitations

The traffic change adds no runtime texture residency. Full-scene static draw,
triangle, material, transparent-slot, and shipping-architecture gates pass.
The full product still reports the inherited texture estimate of
221,506,450 bytes against its 220,200,960-byte planning target; prior traffic
reports have the identical estimate. This 1.25 MiB overage is not attributed to
the parked-vehicle scan and is not hidden or relaxed here.

The 2020 detector authority can miss small, shadowed, occluded, or
low-contrast vehicles. Its recovered long axis may be ambiguous where source
contrast is weak, and runtime model/paint selection is an approximation. Only
the nearest visible subset is rendered. No exact worn Quest frame/thermal
acceptance is claimed for this APK until measured on the physical headset.
