# Boulder Reservoir opaque-water contract

## Outcome

The reservoir upgrade preserves the existing product geometry contract and replaces only its material/shader presentation path. The water surface remains the single serialized `BoulderReservoir_OSM_AuthoritativeWater_Mesh`, with one submesh, one renderer, one shared material, one opaque forward pass, and depth writing enabled. The separate natural shore-bank geometry remains unchanged; it is terrain interface geometry, not a second water surface.

No scene, flight, aircraft, terrain, sky, lighting, or runtime hierarchy is authored by this workstream.

## Geometry evidence

`tools/kbdu_reservoir_water/generate_reservoir_assets.py` decodes and validates the committed Unity mesh directly:

- authoritative mesh GUID: `9adf30d07a69af340bd274a2db6be902`;
- OpenStreetMap way: `35597714`, © OpenStreetMap contributors, ODbL 1.0;
- source boundary: 127 vertices;
- product boundary: 254 vertices, exactly one Chaikin pass over the source boundary;
- one closed loop and zero self-intersections;
- 252 surface triangles in one submesh;
- immutable planar height: `-28.6565533 m` in the environment-root local frame;
- center XZ: `(249.5625, 4307.6875) m`;
- half extents XZ: `(1062.5625, 857.1875) m`.

The generator does not modify the mesh. The existing product tests remain authoritative for water-to-terrain separation and scene references.

## Offline textures

Three deterministic 512 × 512 project-ready derivatives are committed under `Assets/Production/Environment/Textures/Reservoir`:

- broad, tileable wave normal;
- fine, tileable ripple normal;
- packed depth/shore/roughness map.

The packed map uses the authoritative shoreline: R is an artistic distance-to-shore depth ramp, G is a static shore light band, and B is low-frequency roughness variation. It is not surveyed bathymetry and must not be described as measured depth. All textures are linear, mipmapped, trilinear, and Android ASTC 6×6; normals repeat and the basin mask clamps.

Source, license, generator, geometry, output SHA-256 values, and exact channel semantics are pinned in `tools/kbdu_reservoir_water/asset_manifest.json`.

## Shader architecture

`QuestFlightLab/Production Stable Water` retains its name and GUID so serialized product references remain stable. Its single opaque pass provides:

- two independently scrolling normal maps in world XZ space;
- Schlick Fresnel with a water dielectric F0;
- an explicitly injected selected-sky cubemap, including matched yaw rotation;
- roughness-selected cubemap mip sampling;
- one main directional-sun glint;
- shallow/deep artistic color driven by the static mask;
- a bounded static shore light band;
- stereo-instancing macros, fog, GPU instancing, and ZWrite.

It has no transparency, screen/depth sampling, secondary camera, realtime reflection probe, mesh animation, or additional pass. Wave motion changes only sampled normals through Unity shader time; it causes no managed allocation or per-frame CPU geometry work.

## Integration dependency

The workstream does not guess the sky. After the sky workstream selects and imports the production HDR cubemap, the lead must inject that exact asset and its skybox yaw:

```powershell
<Unity.exe> -batchmode -quit -projectPath .\QuestFlight `
  -executeMethod QuestFlight.Editor.ReservoirWaterMaterialAuthoring.RunBatch `
  -reservoirSkyCubemapPath Assets/Production/Environment/Sky/<SelectedSky>.cubemap `
  -reservoirSkyRotationDegrees <matched-skybox-yaw> `
  -reservoirReflectionIntensity 0.9
```

Alternatively, select the integrated Cubemap in Unity and run **QuestFlight → Product → Reservoir → Bind Selected Sky Cubemap**. `ConfigureMaterialState` rejects a null cubemap; realtime probes and unrelated fallback textures are not acceptable substitutes.

No prefab/scene edit is required because the existing surface renderer already references `Production_BoulderReservoir_Opaque.mat`. The lead should inspect the resulting material diff and must not run an older environment-authoring pass that overwrites the tuned water colors without re-running this binder.

## Incremental planning cost

- draw slots: `0` added (`1` existing water slot retained);
- water materials: `0` added (`1` existing shared material retained);
- geometry: `0` added (`252` existing triangles retained);
- transparent slots: `0`;
- shader passes: `1`;
- fragment samples: 2 × 2D normals + 1 × packed mask + 1 × selected-sky cubemap;
- estimated Android ASTC 6×6 bytes including full mip chains: `475,296` bytes (`~0.45 MiB`), excluding the sky cubemap shared with the environment.

This is a static estimate, not Quest GPU timing. Integrated stereo captures, head-pan shimmer review, and physical Quest timing remain required.

## Required integrated review

The lead should verify in full-resolution stereo captures and then on Quest 3:

1. the same selected sky/cloud field appears in water reflection with matching yaw;
2. cockpit, pattern-altitude, and overview views retain believable depth and shore color;
3. head pans do not produce high-frequency shimmer or zebra striping;
4. the water mesh remains separated from terrain at every shoreline segment;
5. exactly one water renderer/material/draw slot remains;
6. p95 physical-Quest timing remains within the milestone gate.
