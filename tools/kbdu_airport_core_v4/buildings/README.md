# Airport-core V4 building authoring

This directory owns the deterministic Workstream A inputs for the Stage-1 terminal and Type 2 T-hangar benchmark. Reference photographs remain outside the repository.

Generate with Blender 5.x:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" `
  --background `
  --python tools\kbdu_airport_core_v4\buildings\generate_airport_core_buildings.py `
  -- `
  --source-dir QuestFlight\Assets\ArtSource\Blender\KBDU `
  --runtime-dir QuestFlight\Assets\Production\Environment\AirportCoreV4\Buildings
```

Then refresh provenance and validate:

```powershell
python tools\kbdu_airport_core_v4\buildings\write_source_manifest.py
python tools\kbdu_airport_core_v4\buildings\write_unity_meta.py
python tools\kbdu_airport_core_v4\buildings\validate_airport_core_buildings.py
```

The GLBs are deliberately isolated per LOD. Do not instantiate three LOD files simultaneously; construct one serialized Unity `LODGroup` with the lead-approved transition distances. Material textures are owned by Workstream B and bind through `AirportCoreTerminalAtlas` and `AirportCoreHangarKitAtlas`.

An offline PASS establishes model/LOD integrity only. It is not visual acceptance, stereo evidence, or Quest performance evidence.
