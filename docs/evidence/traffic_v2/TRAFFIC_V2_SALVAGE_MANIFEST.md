# Traffic V2 Salvage Manifest

Date: 2026-08-22 MDT

Accepted product source: `fda11a25eb526824d82a47330f2be80b1410bb64`

Working branch: `feature/traffic-system-v2`

This manifest separates evidence that remains useful from the failed Phase-3
critical path. It grants no Traffic V2 product, source-coverage, runtime, build,
or headset acceptance.

## Keep

- `tools/traffic_v2/schemas/traffic_system_v2_spec.json`
- `tools/traffic_v2/schemas/traffic_v2_benchmark_camera_contract_v1.json`
- `tools/traffic_v2/schemas/traffic_v2_budget_v1.json`
- `docs/evidence/traffic_v2/TRAFFIC_V2_ACCEPTANCE_CONTRACT.md`
- `docs/evidence/traffic_v2/TRAFFIC_V2_BASELINE_VERIFICATION.md`
- `QuestFlight/Assets/Editor/Tests/TrafficV2/TrafficV2BaselineBatchRunner.cs`
- The immutable external Phase-2 baseline evidence and APK.
- The immutable external 19-source freeze at
  `traffic_system_v2_20260821_170722/workstream_a/freeze_manifest.json`,
  SHA-256 `c7cd7b9362d01bacbeb7c23833cae65882bceaaaab767dcb00f9b8c0904e07b6`.

## Salvage selectively

- `tools/traffic_v2/geospatial/source_registry.json` remains the source URL,
  license, bounds, and coordinate-contract inventory.
- `tools/traffic_v2/geospatial/source_gate.py` is retained as acquisition and
  validation reference code. Its monolithic compile/approval path is not the
  Gate-1 product critical path.
- `tools/traffic_v2/geospatial/test_source_gate.py` is a semantic regression
  inventory. Only tests relevant to a current artifact gate should run.
- `tools/traffic_v2/validation/verify_phase3_source_gate.py` remains a
  read-only reference verifier; its prior FAIL result is not current evidence.

## Quarantine

The five prior Phase-3 normative/report files bound the rejected
`178ac781ea094e4f09af9a38493babace8f78e8942301b86927e414f2675ca3d`
freeze and explicitly reported `FAIL_CLOSED` / `BLOCKED_AUTHORITY`. They were
copied byte-for-byte to the external non-product quarantine:

`traffic_system_v2_20260821_170722/gate0_salvage/quarantine/stale_phase3_178ac781`

They were removed from the active repository paths so they cannot be mistaken
for current `c7cd7b93...` evidence.

## Discard as generated cache

- Aborted `traffic_v2_compile_*` staging trees.
- Stale operation locks from terminated compiler processes.
- Empty approval-candidate placeholders.

These generated items carried no accepted output and can be recreated.

## Next authorized gate

Gate 1 may consume the immutable source freeze offline to produce one compact,
deterministic walking-skeleton lane graph, timed-trajectory manifest, and
inspectable overlay for the airport, divided-highway, and residential benchmark
areas. Unity integration, APK construction, public-data acquisition, and
full-map expansion remain outside Gate 1.
