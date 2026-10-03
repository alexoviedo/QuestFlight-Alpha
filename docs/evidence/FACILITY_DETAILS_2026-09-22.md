# WRRF tanks and Valmont stack review

## Bounded correction

City of Boulder Water Resource Recovery Facility, 4049 N 75th Street. The City 2020 orthophoto was compared with the actual serialized scenery and its City/County footprint records. Seventeen nearly circular City footprints had been reduced to angular building shells twice: the compiler selected `lod2_q`, then simplified that ring again. Residential pitched-roof and facade rules also affected industrial tanks. County aggregate 44816 overlapped the four northwest clarifiers and service platform, creating a false enclosing building.

Compiler v18 preserves the detailed source ring for those 17 tanks and the connecting service platform (34222), uses smooth cylinder wall normals and the existing concrete material without windows/doors, removes the duplicate County aggregate, and replaces the two large digesters' gabled roofs with low, smooth domes at the recorded roof rise. Other tank covers stay flat and retain world-projected orthophoto detail. No new runtime objects, materials, textures, draw calls, distance settings or shaders. The existing 235,000 selected-building-triangle ceiling remains enforced, including dome triangles.

The allowlist validates exact source identifiers and fails if the data changes. Source database geometry remains unmodified; reviewed corrections are applied deterministically during offline compilation. The aggregate is excluded from renderer counts as well as mesh generation. Existing asset GUIDs remain intact.

## Stack decision

The prominent Valmont stack belongs to the coal facility retired in 2017. The remaining natural-gas combustion turbines are separate. The user selected present-day accuracy: no smoke or steam was added to the retired stack. An invented plume would contradict that decision.

## Sources

- [City WRRF program](https://bouldercolorado.gov/services/water-resource-recovery-program).
- [City 2020 orthophoto service](https://gis.bouldercolorado.gov/ags_svr1/rest/services/raster/AP2020Cached9inWM/MapServer). Existing project imagery source; reference download remains outside the repository.
- [Boulder County Valmont record](https://bouldercounty.gov/environment/water/valmont-station/).
- [Xcel Valmont project fact sheet](https://www.xcelenergy.com/staticfiles/xe-responsive/Environment/Coal%20Ash%20Management/Xcel%20Energy%20Valmont%20Project%20Info%20Sheet.pdf).

## Validation

Geometry/bundle compilation passed. All 574 unaffected page meshes are byte-for-byte unchanged; only pages 015/012 and 015/013 differ. Total source geometry increases by 1,990 triangles (613,550 to 615,540 baseline triangles); the conservative worst-case selected total is unchanged at 235,000. Selected pitched roofs elsewhere remain unchanged. Source count is unchanged; represented count drops by one intentionally excluded overlapping aggregate.

Three targeted editor captures passed machine checks and were inspected: close oblique, wider aerial, and deterministic stereo. Circular silhouettes and the two dome profiles are visibly improved. These are editor presentation images, not live Meta XR Simulator or Quest acceptance. The current live simulator harness previously stalled awaiting flight state; that gap is not represented as a pass here. The standard 130-capture Visual QA sweep was also run and its contact sheet inspected. It reports FAIL_MACHINE_QA: 41 scene materials versus a planning target of 38, draw/triangle planning thresholds in several views, and inactive runway contact-shadow checks in two static aircraft views. No materials or draw calls were added by this correction; these broader checks are recorded rather than relaxed. They are not physical Quest timing measurements. Android build and packaged scenery inventory validation passed (1,912 authored scenery files). APK SHA256: `39626a9c77956fcec6748dd8f9a7e069672bef09fd93a590d15f3fed2f4ac82d`. Physical headset visual and frame-time acceptance remain pending.

External evidence: `T2-QuestFlight-product-artifacts/facility_details_20260922`. Includes orthophoto, source footprint overlay, before/after scenery captures, mesh hashes and compiler/build logs. Editor imagery is not live simulator or physical Quest performance evidence.

Installation succeeded on Quest `2G0YC5ZG8907TD`; launched to the app menu. No worn acceptance or physical GPU measurement is claimed.
