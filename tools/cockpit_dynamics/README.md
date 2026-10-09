# Cockpit dynamics authoring tools

These tools audit and derive cockpit presentation assets from the licensed C172
source without modifying the source GLB.

- `inspect_cockpit_source.py` imports the GLB in Blender and emits deterministic
  object, material, UV, bounds, and connected-component evidence.
- `cockpit_representation_manifest_v2.json` retains its filename for compatibility
  and contains the current **schema 3** reviewed identity and authority contract:
  `implementedMotion`, instrument/avionics presentation, touch interaction,
  `representedButBlocked`, and `excludedBecauseNotRepresented`. Motion requires
  the reviewed geometry, channels and owner; blocked/excluded identities cannot
  also be implemented. The architecture declares the native flight snapshot
  owner and limited deterministic systems presentation.
- Run `python tools/cockpit_dynamics/validate_cockpit_representation.py` and
  `python -m unittest discover -s tools/cockpit_dynamics/tests -v` with the exact
  source GLB hydrated. The validator verifies source size/hash and current
  classification/authority, including negative cases. Historical validation
  strings inside the manifest are milestone records, not fresh execution evidence.

Run Blender inspection outside the Unity project so extracted source images and
diagnostic JSON remain external artifacts:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" `
  --background `
  --python tools/cockpit_dynamics/inspect_cockpit_source.py -- `
  QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb `
  "$env:TEMP\questflight_cockpit_source_inspection.json"
```

The Blender tool is offline authoring evidence only. It is not included in the
Android product and it must not create runtime hierarchy repair.
