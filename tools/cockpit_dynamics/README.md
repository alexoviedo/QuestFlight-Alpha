# Cockpit dynamics authoring tools

These tools audit and derive cockpit presentation assets from the licensed C172
source without modifying the source GLB.

- `inspect_cockpit_source.py` imports the GLB in Blender and emits deterministic
  object, material, UV, bounds, and connected-component evidence.
- `cockpit_representation_manifest_v2.json` is the reviewed identity and
  authority allowlist. Product authoring and tests may animate only its
  exact `implementedTargets`. `representedCandidates` require both identity and
  authority gates before they may move into that set.

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
