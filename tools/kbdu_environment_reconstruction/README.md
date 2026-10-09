# KBDU environment reconstruction content pipeline

This tools-only pipeline converts the checkpoint's committed bounded OSM
context and context recipe into deterministic V2 content contracts. It does not
place raw downloads in the repository, modify Unity assets, or author the
product scene.

Run from the repository root:

```powershell
python tools/kbdu_environment_reconstruction/fetch_osm_buildings_6km.py --artifact-dir <external-artifact-directory>
./tools/kbdu_environment_reconstruction/decode_vegetation_source.ps1 -ArtifactDir <external-artifact-directory>
python tools/kbdu_environment_reconstruction/generate_vegetation_v2.py --ppm <external-artifact-directory>/KBDU_NAIP_2019_Mid12km_1024.ppm --decode-receipt <external-artifact-directory>/vegetation_source_decode_receipt.json
python tools/kbdu_environment_reconstruction/generate_content_contracts.py
python -m unittest discover -s tools/kbdu_environment_reconstruction/tests -p "test_*.py"
```

Generated files remain beside this README:

- `KBDU_ContextBatchRecipeV2.json`
- `KBDU_ReferenceContextV2.json`
- `osm_buildings_6km_source.json`
- `building_coverage_report.json`
- `production_placement_manifest.json`
- `airport_parking_stand_manifest.json`
- `vegetation_distribution_manifest.json`
- `vegetation_source_v2.json`
- `vegetation_coverage_report.json`
- `vegetation_species_library_v2.json`
- `vegetation_likelihood_mask_v2.png`
- `vegetation_exclusion_mask_v2.png`
- `vegetation_suitability_mask_v2.png`
- `vegetation_meshes/*.obj` and one shared `.mtl`
- `content_pipeline_report.json`

The V2 recipe replaces capped building and legacy vegetation batches with the
bounded 6 km/250 m-cell building derivative and validated vegetation source. Its
legacy field shapes remain Unity-compatible. Parked-aircraft records additionally
carry the current reviewed 35-aircraft family, resting roll, active City 2020 silhouette alignment and source identity. The final composition invokes the same pure placement producer as `apply_aircraft_placement_contract.py` before emitting its report. The older three-stand `airport_parking_stand_manifest.json` is historical plane/contact calibration only; it must not replace the 17 powered aircraft and 18 sailplanes.

The normalized OSM building derivative is bounded to a 6 km radial zone and is
covered by ODbL-1.0 with OpenStreetMap attribution. The raw Overpass response,
query, and fetch receipt remain external under the supplied artifact directory.

The V2 vegetation pass replaces those 246 legacy candidates. Because the
committed NAIP derivative is natural-color rather than four-band NIR, the source
uses a recorded natural-color canopy-likelihood fallback plus deterministic OSM
exclusions. Raw decoded pixels remain external; only optimized masks, source
contracts, and project-authored procedural LOD meshes are kept here. The four
regional species variants use closed crown volumes for stable top and oblique
views and share one opaque material family.

Current placement dependency: `tools/kbdu_airport_aircraft/airport_aircraft_placements_v1.json` (schema 2 despite its retained filename), verified active orthophoto and terrain rings. `content_pipeline_report.json` hashes all final composing inputs and outputs; a subsequent standalone patch refreshes that final identity record. Full source regeneration still requires the pinned external normalized `osm_buildings_6km_source.json`; unavailable external input is an explicit skip/blocker, never a PASS. Run `test_current_aircraft_composition.py` independently to test current contracts without that historical external input.
