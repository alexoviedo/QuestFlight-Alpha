# Aerial Scenery V1 building contract

`generate_building_spatial_contract.py` replaces the old centroid-grid outer
rectangles with an offline, source-pinned building representation contract.

- Hero-core and near records use the original OSM-derived quantized footprint.
- Mid records that retain useful projected size use deterministic vertex removal. The simplified polygon must
  remain simple, keep the exact source X/Z bounds, and remain within +/-10% of
  source area. Failure to simplify safely falls back to more source vertices.
- Every active context representation is a closed foundation-to-roof shell.
  Conservative residential footprints use deterministic gable or hip roofs;
  other footprints use a closed flat roof. No active mesh is roof-only.
- Every batch has one fixed renderer, a unit transform, and no `LODGroup`.
  Small structures beyond 2 km use the orthophoto instead of a roof-card HLOD,
  eliminating the former scale/shape pop while reducing runtime geometry.
- Components contain only footprint parts that intersect or touch. A runtime
  batch may combine many components into one mesh, but it must never add a hull,
  cell rectangle, or geometry across the space between them.
- Ordinary far buildings carry no geometry or duplicated record payload. Their
  feature-index set resolves footprint, pivot, bounds, foundation, and fallback
  height from the pinned context/recipe. Only identities listed explicitly in
  `far_landmark_allowlist` may retain a far footprint.
- Every represented form uses the same source-derived polygon centroid/bounds,
  terrain-sampled `base_y_meters`, selected height, and one authored unit scale.
- Fixed shells use one opaque, instancing-compatible shared material. It reuses
  the already-resident AirportCore V4 HangarKit albedo, tangent-normal, and MRAO
  atlas triplet; no context texture is copied or added to the product closure.
  Facades select deterministic brick, concrete, light metal, or weathered-metal
  cells. Roofs select only the authored dark-roof or weathered-corrugated cells.
  Tile indices are bottom-left row-major, and sampling uses the same conservative
  0.012 absolute-atlas inset as the AirportCore V4 Blender authoring to prevent
  cross-cell mip bleed at oblique flight views. The old repeated-window
  `KBDU_ContextAtlas.png` is forbidden from the standalone prefab dependency
  closure.
- Facade repetition is based on edge length and storey height instead of
  stretching a complete atlas cell across every wall. The material has one
  opaque forward pass, no lightmap dependency, no reflection-probe dependency,
  and deterministic normal/MRAO lighting. It derives explicit mip gradients
  from unwrapped local UVs, then progressively flattens albedo, tangent-normal,
  AO, metallic, and specular microdetail once the atlas footprint becomes
  sub-pixel. Near views retain the PBR atlas; aerial views converge to bounded
  per-cell linear-space brick, tan, gray-metal, concrete, and roof averages so
  corrugation cannot become black moire.

## Optional lidar height input

The generator works without lidar. If the configured manifest is absent, it
keeps the serialized terrain foundation and the pinned context render height.

A lidar override is accepted only when all of these are true:

1. the manifest schema is `questflight.aerial-scenery-v1.lidar.v1`;
2. the manifest says `eligible_for_authoritative_override: true`;
3. the manifest says `meets_10_percent_vertical_error_gate: true`;
4. `vertical_error_fraction_bound` is present and <= 0.10;
5. a per-building record is keyed by the contract `stable_id`;
6. record confidence is >= 0.80 and its error fraction is <= 0.10.

The current 4 m/pixel HAG/confidence derivative has no surveyed vertical-error
study and deliberately reports that the gate is not met. It is pinned as
optional scale/QA provenance, yields zero overrides, and never moves a building
foundation. This is a deterministic fallback, not a claim of lidar height
accuracy.

Generate the committed manifest and an optional external evidence report:

```powershell
python -B tools/scenery/buildings/generate_building_spatial_contract.py `
  --report-output C:\path\to\artifacts\building_spatial_contract_report.md
```

Then author the deterministic Unity mesh assets and the standalone prefab:

```powershell
& $Unity -batchmode -nographics -quit `
  -projectPath QuestFlight `
  -executeMethod QuestFlight.Editor.AerialSceneryBuildingAssetAuthoring.RunBatch `
  -logFile C:\path\to\artifacts\building_authoring.log
```

The authoring entry point writes only
`Assets/Production/Environment/AerialSceneryV1/Buildings/Generated` and
`KBDU_AerialBuildingsV1.prefab`. It does not open or edit the product scene,
`ProductionEnvironmentRoot.prefab`, or the legacy combined context prefab.
`KBDU_BuildingBatchReplacementV1.json` is the integration handoff: it maps
each legacy building group and its exact three renderer IDs to disable to the
new footprint-preserving group and its one fixed closed-shell renderer. Source footprints
covered by authored hero replacements remain omitted from the generic prefab.
Authoring also removes generated mesh assets no longer named by a rendered
replacement row, so orthophoto-only far batches cannot leave stale assets in
the prefab dependency closure.

Validate already-authored assets without rewriting them with
`QuestFlight.Editor.AerialSceneryBuildingAssetAuthoring.ValidateBatch`.

The output is derived text under the source OpenStreetMap attribution/license
already pinned by `KBDU_ReferenceContextV2.json`; no raw downloads are emitted.
