# KBDU Real-Data Environment Pipeline

This standard-library Python pipeline produces bounded, Quest-oriented KBDU terrain and reference-context assets from public/open data. It does not use Google Maps, Google Earth, Street View, paid data, or raw imagery as committed content.

The shared local tangent origin is the FAA airport reference point:

- latitude: `40.03936527` degrees north
- longitude: `-105.22608958` degrees east (`105.22608958` degrees west)
- elevation: `5288 ft MSL` / `1611.7824 m MSL`
- Unity mapping: local ENU, `+X=east`, `+Y=up`, `+Z=north`

The output is explicitly **KBDU-inspired / not for navigation**.

## Sources

- FAA-derived USDOT/BTS NTAD airport and runway services: U.S. government work, unrestricted public use.
- USGS 3DEP Bare Earth DEM dynamic service: public-domain U.S. government elevation data.
- OpenStreetMap through bounded Overpass queries: ODbL 1.0; runtime attribution must show `© OpenStreetMap contributors`.
- USGS/USDA NAIP Plus: a bounded 4 km availability query is recorded. No imagery pixels are downloaded or committed in this version.

Every request URL, request hash, retrieval time, raw relative path, byte count, SHA256, source agency, and license is recorded in the external `fetch_receipt.json` and committed `kbdu_source_manifest.json`.

## Rebuild

Use Python 3.11 or newer. There are no third-party Python dependencies.

```powershell
$raw = "C:\path\outside\the\repo\kbdu_raw_YYYYMMDD"
python -B tools/kbdu_environment/fetch_kbdu_sources.py --raw-dir $raw
python -B tools/kbdu_environment/process_kbdu_environment.py --raw-dir $raw
python -B tools/kbdu_environment/validate_kbdu_assets.py --raw-dir $raw --report "C:\path\outside\the\repo\kbdu_validation.json"
python -B tools/kbdu_environment/render_kbdu_preview.py --output "C:\path\outside\the\repo\kbdu_data_preview.png"
python -B -m unittest discover -s tools/kbdu_environment/tests -v
```

If a fetch is interrupted before `fetch_receipt.json` is written, resume only that incomplete directory:

```powershell
python -B tools/kbdu_environment/fetch_kbdu_sources.py --resume --raw-dir $raw
```

Never point `--raw-dir` inside the repository. The fetcher refuses to overwrite a completed snapshot.

## Committed Outputs

- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json`
  - 10 m airport patch, 40 m inner ring, 150 m mid ring, and 400 m far ring.
  - exact 4 km, 12 km, and 24 km square coverage.
  - signed int16 decimeter heights, little-endian and base64 encoded relative to the FAA datum.
  - 30,304 stored samples and 54,768 source-grid triangles; runtime seam stitching adds only the transition triangles needed to join independently sampled rings.
  - primary and turf runway terrain is smoothly blended to planes between USGS-sampled FAA endpoints.
- `QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json`
  - FAA runway polygons/endpoints and selected official attributes.
  - clipped/simplified/quantized OSM aeroway, building, road, water, land-cover, and barrier features.
  - deterministic OSM-tag-driven macro material IDs and a project-authored color/roughness palette.
- `tools/kbdu_environment/kbdu_source_manifest.json`
  - source/license/raw hash/derived hash/toolchain provenance.

## NAIP Decision and Fallback

The automated inner-ring query proves NAIP catalog coverage at KBDU. The service advertises a 4000 x 4000 maximum export, while a 24 km square at 1 m resolution is roughly 24,000 x 24,000 pixels before tiling. The project does not yet have a validated seam-safe tiler, color normalizer, mip workflow, and Android ASTC import path. A one-image downsample would either exceed the 750 KB macro-texture budget or erase useful airport/field detail.

The current output therefore uses deterministic macro material regions derived from OSM categories, with a self-generated default dry-prairie material. No raw or downsampled NAIP image is committed. A future imagery pass should tile each ring, normalize seams, generate mips, enforce ASTC import settings, and capture before/after Unity Visual QA before enabling it.

## Unity integration

The clean product does not contain a runtime environment builder or procedural fallback. The frozen JSON data is baked offline into `Assets/Production/Environment/ProductionEnvironmentRoot.prefab` and its deterministic mesh/material dependencies. The authored production scene serializes that prefab directly.

Product invariants are enforced by `ProductionEnvironmentRoot`, product EditMode/PlayMode tests, `ProductIntegrityValidator`, and the 16-view Visual QA runner:

1. one immutable airport/near/mid/far terrain source;
2. no distance LOD, crossfade, camera-facing scale, or runtime mesh rebuilding;
3. one FAA-derived runway plane/collider/marking system with measured gaps;
4. one OSM-derived Boulder Reservoir mesh with positive terrain separation;
5. accessible `© OpenStreetMap contributors` attribution;
6. fixed Quest renderer, triangle, material, texture, and shader budgets.

The historical runtime-builder paths retained inside `kbdu_source_manifest.json` are provenance for the frozen R&D generation snapshot, not product dependencies. The product validator verifies the frozen manifest hash and committed derivatives while separately proving that those legacy builders are absent.

Enabling these assets changes scene visuals. The project rule therefore requires inspected before/after Visual QA and an integrated Quest performance report before release. Editor evidence is not on-device Quest performance evidence.
