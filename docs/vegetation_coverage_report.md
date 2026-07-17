# Vegetation coverage report

Status: source, exclusion, placement, and HLOD accounting pass; visual acceptance fails.

Natural-color public-domain NAIP is used as the documented fallback because the bounded source did not provide NIR. The deterministic likelihood, exclusion, and suitability masks are combined with OSM context and exclude runway/taxi/apron, buildings, roads, open water, parking, and safety areas. No raw decoded imagery is committed.

- 377 accepted placements: 30 within 0–600 m and 347 within 600–1500 m.
- Species: 152 plains cottonwood, 65 narrowleaf willow, 47 Rocky Mountain juniper, and 113 rabbitbrush shrubs.
- Four project-owned procedural species each provide closed-volume LOD0/LOD1/LOD2 meshes; no camera-facing billboard is used.
- Aggregate geometry if every instance used one tier: 241,320 / 60,320 / 9,048 triangles.
- Four shared opaque material slots; 24 m minimum spacing; orthophoto-only beyond 1500 m.
- Placement validation reports zero unvalidated exclusion records in the final serialized manifest.

Full-resolution review does not accept the art. The forms are too pale, simple, small, and repetitive; the top-down canopy lacks convincing mass and airport scale cues. Vegetation quality and aerial canopy each score 3.5/10, so no stereo or physical-headset promotion is claimed.
