# Tree structure and habitat implementation

Status: map-wide implementation built, inspected in the stereo simulator, installed on Quest 3 and device APK hash verified. Not headset or performance acceptance. Implements the approved [tree amendment](TREE_STRUCTURE_AND_HABITATS_PLAN.md).

## Delivered source changes

- Preserve all 12,407,662 existing canopy cells, component IDs, radii and coverage footprints across 1,296 pages. Retain the existing range, streaming, both-eye culling, bank refresh and triangle caps.
- Ground those cells and 868,508 existing tree records against Unity's actual authored terrain, including transition patches. Previous canopy-base error: mean 0.285 m, 95th percentile 1.060 m, maximum 56.710 m. A base correction alone does not prove every reported floating canopy is fixed.
- Shape existing canopy layers around retained tree anchors into rounded or tapered height profiles. Lower the existing middle branch layer; add no blanket canopy sheets or extra grove layers.
- Preserve lower stems and upper crowns in existing tree impostors when groves replace the middle foliage. Correct the unit far billboard's scale relative to the nonunit near-tree mesh.
- Use broadleaf, open-pine and tapered-fir visual families. Approximate western foothill elevation/aspect mixtures leave eastern town and low valley trees predominantly broadleaf. This is an appearance heuristic, not a surveyed species map.
- Reuse existing tree mesh topology, with original small leaf cards repositioned into conifer branch whorls. New CC0 needle artwork and matching eight-view conifer impostors supply the silhouette and texture change. No extra family draw passes, physics, colliders or runtime hierarchy construction.

Source/license records accompany the new artwork and mesh derivative. Generator scripts are in `tools/full_map_vegetation`; native authoring is in `TreeRealismAuthoring.cs`. Existing `.meta` GUIDs are retained.

## Validation evidence so far

External artifact root: `C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/tree_realism_20260915`.

- `data_audit.json`: PASS. Compare every original page from Git/LFS against its preserved manifest hash; confirm unchanged footprint fields and exact exported terrain bases. Existing tree positions, dimensions, orientation, kind and colors remain unchanged in the database; only grounding and family seed bits change.
- `review/screenshots`: integrated seat-position scenery diagnostics, with aircraft hidden because the offline cockpit setup obstructs these views. These are not proof of cockpit visibility. Initial review caught a D3D sampler-limit error; sharing the existing foliage sampler corrected it before subsequent captures.
- `validation/visual_qa`: all 130 standard images captured; contact sheet inspected. Same 26 failing views as the accepted traffic build, with no newly failing views. Maximum positive estimated draw delta: zero; maximum positive estimated triangle delta: 2,568. These are selection-dependent Editor estimates, not GPU timings.
- `standard_qa_comparison.json` preserves the per-view comparison. Existing budget failures are retained, not waived.
- Full product validation stopped at dependency classification: 14 existing authored startup/traffic/grove files are unclassified; no rejected dependencies. The remaining checks are run individually, and the overall gate is not represented as passing.
- `validate_trees_final.log`: PASS. Existing map page corruption, coverage, ownership reset, LOD and bank/reversal tests pass; native leaf-card metadata, both tree/grove texture bindings and compiled shaders pass. A missing needle-art material reference was caught and repaired using forced synchronous import and non-null binding checks.
- Broad EditMode: 471/472 pass initially. The remaining traffic flap test omitted the new optional camera argument in a reflection call and asserted animation on inactive LOD copies. The corrected test supplies that argument and requires lowered trailing edges on active flaps, with at least one active flap per aircraft. Its focused rerun passes (`validation/traffic_signature_final.xml`), giving passing evidence for all 472 distinct EditMode tests across the two runs. PlayMode: 21/22 pass; the remaining assertion concerns a cockpit static-display renderer in the exterior visibility profile. No tree assertion failed.
- Initial simulator build stopped on a missing retained aerial-source environment setting. The corrected run supplies that path and expands its bounded western imagery coverage to 8 km. An early script preflight now prevents copying/importing a development project when the required source directory is absent.

## Remaining acceptance

The final 1920x1080 standard pass produced all 130 captures; its contact sheet was inspected. It retains 26 failing views, with no newly failing view IDs. Its resolution differs from the earlier matched 1280x720 comparison, so this is not a matched pixel/performance experiment.

The isolated Windows simulator build succeeded with zero errors. Live OpenXR stereo and supplemental window captures were inspected at the foothill start; distinct conifer silhouettes render in the pilot's exterior view. Both eyes are nonblank and scene/material consistency checks pass. The existing mountain-identity machine check and missing recorded-replay coverage remain open; this is not full simulator-gate acceptance or Quest GPU evidence. Evidence: `simulator_final/meta_xr_simulator/qa_run`.

Static budget audit: texture residency estimate **286.16 / 293.26 MiB PASS**, including **2.97 MiB** for the two new ASTC 6x6 mipmapped atlases. Existing draw/triangle/material targets still fail at **97 / 77**, **1,026,792 / 692,871**, and **41 / 38**. No limits were relaxed.

Canonical Android build: PASS; all **1,912** authored scenery payloads verified. Package audit confirms **1,296** updated branch pages and only `arm64-v8a` native libraries. APK size **1,766,648,692 bytes**; SHA-256 `c279d4f0a44f121a3f9e1023a92a01f37237cc48774c5af906c3b42784227e8f`. Build/source evidence: `android/product_apk_manifest.json`, `android/source_identity.json`, and `android/package_audit.json`.

Device install: PASS on Quest `2G0YC5ZG8907TD`; the installed base APK SHA-256 matches the built candidate. Normal launch issued without observation overrides. Evidence: `android/device_install.json`.

Next acceptance is one worn presentation of lowland support and foothill silhouettes, followed by actual Quest frame/thermal evidence. Additional visible coverage can increase fragment work despite unchanged geometry caps. Do not call this release-ready or performance-approved while these gates remain open.
