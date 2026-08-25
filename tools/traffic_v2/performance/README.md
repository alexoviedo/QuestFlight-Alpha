# Traffic V2 Gate-1 Walking Skeleton

This directory contains the deliberately small offline Gate-1 proof. It invokes
`geospatial/compile_walking_skeleton_lanes.py` against the immutable external
source freeze and emits:

- a directed lane-chain graph for three benchmark-camera areas;
- twelve persistent, non-wrapping, timed real-road trajectories;
- an inspectable three-panel overlay;
- a limitations-first report and exact hash manifest.

The proof is not product data. It does not claim full-map coverage, calibrated
demand, legal intersection turns, continuous collision freedom, Unity runtime
integration, Quest performance, or headset acceptance.

Example using the approved 2026-08-22 freeze:

```powershell
& '<external-workstream-a>/toolchain/py313-env/Scripts/python.exe' -I -B `
  tools/traffic_v2/performance/compile_walking_skeleton.py `
  --repo-root . `
  --external-run '<external-workstream-a>' `
  --output-dir '<external-artifacts>/gate1_walking_skeleton' `
  --expected-freeze-sha256 c7cd7b9362d01bacbeb7c23833cae65882bceaaaab767dcb00f9b8c0904e07b6
```

Run only the focused tests for this gate. The real-freeze determinism test is
enabled by setting `TRAFFIC_V2_GATE1_EXTERNAL_RUN` and
`TRAFFIC_V2_GATE1_FREEZE_SHA256`.
