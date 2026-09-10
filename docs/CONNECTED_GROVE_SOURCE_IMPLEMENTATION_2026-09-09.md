# Connected grove source implementation — September 9, 2026

The approved connected-grove replacement has reached source compilation. **The source acceptance gate remains unresolved; no new foliage geometry or shipping change is ready.** This document records the actual implementation and the reason the next stage cannot yet pass its gate.

## Implemented

- `tools/full_map_vegetation/fetch_city_canopy.py` downloads bounded City of Boulder derivatives, checks their publisher-specific CC BY 4.0 license, records tile metadata and hashes, rejects changed cached sources, and reserves 8 GiB of disk headroom for each download. Raw downloads are rejected inside the repository.
- Five official one-foot canopy-height rasters and eleven connected-extent archives were acquired. Total retained source files are approximately **1.13 GB**. Extent-only adjoining tiles avoid several additional gigabytes of height downloads during boundary evaluation.
- `build_connected_grove_evidence.py` converts relative canopy heights from US survey feet once, uses the project's AEQD X-east/Z-north coordinates, preserves nodata and records source-edge contact. Reprojection uses a two-dimensional source CRS: relative heights must not undergo an absolute vertical-datum transformation. [GDAL documents automatic vertical transformations for some single-band compound-CRS inputs](https://gdal.org/en/stable/programs/gdalwarp.html#vertical-transformation).
- `assemble_connected_grove_sources.py` joins polygons in their native coordinate system before projection and render partitioning. It uses exact supported adjacency without dilation or old tree-center circles. **49,310 input components become 49,039 joined components**, removing **271 source-tile splits**. **31,495** components remain after the declared 4 m² minimum-area filter. These are source components, not runtime tree/mesh counts. Real interior holes and separate crowns remain distinct.
- Content-derived IDs identify the immutable compiled source snapshot. All future render sections must inherit their component's ID. Expanding source coverage can alter component geometry and IDs; a lineage mapping is still required for map-wide incremental compilation. Do not claim global identity across arbitrary source revisions is already implemented.
- `connected_grove_reference_crops.json` and `prepare_connected_grove_references.py` fix 30 aerial crops, five per required category, with 12 development and 18 held-out crops. Actual source JPEG hashes are retained externally. All thirty have extent coverage after adjoining-tile acquisition; this does not mean every region has acquired height data.
- `build_connected_grove_review.py` produces the source comparison and selected-grove evidence. The complete pilot component is **2,024.98 m²**, approximately **78.5 × 74.9 m** in bounding extent, with zero unavailable height samples in its displayed footprint. The color-height image is a data diagnostic, not proposed opaque canopy geometry.

Source output and review: [comparison.html](../../T2-QuestFlight-product-artifacts/connected_groves_20260909/comparison.html), [assembly](../../T2-QuestFlight-product-artifacts/connected_groves_20260909/assembly/assembly.json), [selected pilot](../../T2-QuestFlight-product-artifacts/connected_groves_20260909/selected_pilot.json).

## Evidence limits and the incomplete gate

Inspected Valmont source overlays show whole irregular crowns and visible paths between them, and the height source resolves variation within a single connected grove. The initial review also exposed a source-tile cutoff; unavailable areas are now explicitly marked and adjoining extent sources are joined. Two simple held-out negative controls—a paved road and an empty field—contain zero detected canopy pixels. These findings are useful checks, **not** a formal accuracy pass.

The first twelve provisional reference annotations were made from raw aerial images. Inspecting their traces showed that coarse ellipses and polygons did not reliably distinguish crown boundaries from shadows, especially in the conifer and narrow-row crops. Their diagnostic scores are therefore **not valid detector precision, recall or boundary measurements**. The rejected draft remains external for traceability and is not committed as ground truth. `score_connected_grove_references.py` explicitly prevents unqualified or incomplete annotation sets from passing the pixel/boundary gate.

The 18 held-out crops still need independently qualified boundary annotations. Split/merge and fixed false-bridge checks also remain incomplete. Do not fit the detector to the held-out images, reuse evaluated polygons as labels, or silently loosen the approved targets. The current City-extent assembly is an initial source proposal; imagery/exclusion fusion and confidence handling have not yet been implemented or validated.

The source index supplies vegetation-class percentages and return-density metadata. Raw point-cloud histograms, individual-return ground support and complete map coverage were not independently verified in this derivative-only acquisition. The City's already-normalized CHM avoids inventing above-ground heights from absolute elevations, but does not recover a measured lower crown surface or internal foliage density.

## Validation

Six regression tests pass:

```powershell
& 'C:/Users/ovied/AppData/Local/Programs/Python/Python313/python.exe' -m unittest discover -s tools/full_map_vegetation -p test_connected_grove_evidence.py -v
```

They cover survey-foot conversion/nodata, local coordinate directions, seam joining with a retained courtyard, preservation of a real 5 cm gap, rasterized holes and cache-integrity failure before network access or manifest replacement. Source compilation and comparison generation completed. The rejected annotation scores are excluded from validation.

An additional check compares **346** canonical-grid height samples with independent nearest-pixel lookups in the original one-foot raster. All agree within 1 cm; maximum absolute difference is **0.00000161 m**. This verifies the height unit conversion and sample alignment for that tile, not the correctness of vegetation classification or imagery boundaries. [Height sample audit](../../T2-QuestFlight-product-artifacts/connected_groves_20260909/height_sample_audit.json).

No product source, scene binding, generated Unity foliage asset or APK was changed in this source milestone. Unity Visual QA, stereo simulator captures, Android build and physical Quest timing are consequently not claimed for a new grove representation. Existing dirty product files and previous headset evidence predate this milestone.

## Remaining authorized sequence

1. Finish a reliable independent reference set and topology audit. Correct source detection from development evidence only, then apply the untouched acceptance thresholds to the held-out set. Leave uncertain/no-data regions on existing trees.
2. After the source gate passes, compose the selected whole grove from physically scaled branch clusters, including near and middle tiers and actual crown ownership replacement. Disable the failed angular overlay in the experimental reference and candidate.
3. Inspect the prescribed paired views and movement at useful flight distances; measure geometry, coverage/overlap and resident memory. Expand to five representative groves only after the first passes.
4. Then perform bounded map compilation, product integration, standard/stereo QA and Android validation. Prepare one final worn comparison after those checks; no intermediate headset request is needed.

## Source and license

The [City's specific LiDAR publication](https://www.arcgis.com/home/item.html?id=8fb05137a9f049afa0b60d44086b19af) describes May 2020 acquisition, NV5 classification, one-foot medium/high vegetation CHM and an extent threshold of ten feet. This source uses **CC BY 4.0**, independently of the existing aerial photographs' CC0 provenance.

Required attribution: **Canopy height and extent derived from City of Boulder / NV5 / DRCOG 2020 LiDAR, CC BY 4.0. Modified for QuestFlight; no endorsement implied.**

Original source files, full geospatial derivatives, rejected labels and review captures remain outside the repository. No new binary asset is committed or implied to have product acceptance.
