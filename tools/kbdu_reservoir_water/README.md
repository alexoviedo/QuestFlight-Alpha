# Boulder Reservoir water assets

This standard-library offline tool owns only Boulder Reservoir water derivatives. It preserves the existing immutable, one-submesh `BoulderReservoir_OSM_AuthoritativeWater_Mesh` and validates that its 254-point boundary is the exact one-pass Chaikin smoothing of the committed 127-point OpenStreetMap source shoreline.

It generates three bounded 512 × 512 RGB textures:

- `BoulderReservoir_BroadWave_NM.png`: tileable, project-authored broad wave normals;
- `BoulderReservoir_Ripple_NM.png`: tileable, project-authored fine ripple normals;
- `BoulderReservoir_DepthShoreRoughness.png`: R = artistic distance-to-shore depth, G = static shore band, B = deterministic roughness variation.

The depth channel is not measured or surveyed bathymetry. It is an artistic distance-to-shore approximation used only for shallow/deep color variation. The shoreline input remains © OpenStreetMap contributors under ODbL 1.0 and is not for navigation.

Regenerate and validate from the repository root:

```powershell
python -B tools/kbdu_reservoir_water/generate_reservoir_assets.py
python -B tools/kbdu_reservoir_water/validate_reservoir_assets.py
python -B -m unittest discover -s tools/kbdu_reservoir_water/tests -v
```

The generator writes no raw downloads or caches. The committed `asset_manifest.json` pins source and output SHA-256 values, geometry statistics, licensing, limitations, and the incremental Quest texture/draw contract.

The selected HDR sky cubemap is deliberately not guessed or copied here. Integration must inject the selected sky through `ReservoirWaterMaterialAuthoring.ConfigureForSelectedSky`; a missing cubemap is an integration blocker, not permission to use a realtime probe or unrelated fallback.
