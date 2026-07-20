# Airport-core V4 lighting and materials

This bounded, fail-closed pipeline owns only the V4 terminal/hangar material
atlases, the exact-replacement clean plate, the fixed lighting authoring
contract, and the continuous runway material candidate. It never edits the
product scene or prefabs and it never reports a Unity bake, stereo result, or
Quest result that did not run.

Raw Poly Haven maps must be placed outside Git. The fetch command queries the
official file API, downloads only the configured 1K/2K diffuse, OpenGL normal,
and ARM maps, verifies official MD5 values, and writes a receipt beside the raw
files:

```powershell
python tools/kbdu_airport_core_v4/lighting_materials/fetch_polyhaven_sources.py `
  --raw-root <artifact-root>\workstream_b\raw_polyhaven
```

The build requires Workstream A's exact replacement-footprint contract. It
will not fall back to masking every airport building:

```powershell
python tools/kbdu_airport_core_v4/lighting_materials/build_workstream_b.py `
  --raw-root <artifact-root>\workstream_b\raw_polyhaven `
  --replacement-manifest <workstream-a-replacement-manifest.json> `
  --artifact-dir <artifact-root>\workstream_b\build

python tools/kbdu_airport_core_v4/lighting_materials/validate_workstream_b.py `
  --replacement-manifest <workstream-a-replacement-manifest.json> `
  --output <artifact-root>\workstream_b\validation.json

python -m unittest discover `
  -s tools/kbdu_airport_core_v4/lighting_materials/tests -p "test_*.py"
```

The clean plate is a full-size near-zone derivative so it can replace the
existing near texture without changing UVs. Pixels outside the union of the
explicit roof/aircraft and reconciled-shadow masks remain byte-identical before
JPEG encoding; the committed lossless mask proves the edit boundary. The
replacement manifest remains authoritative for coverage.

The generated `AirportCoreLightingProfile.json`, lightmap/probe/reflection
plans, and Unity integration contract are inputs to a lead-owned authored
scene integration and bake. `AUTHORING_PLAN_READY_BAKE_NOT_RUN` is intentional.
