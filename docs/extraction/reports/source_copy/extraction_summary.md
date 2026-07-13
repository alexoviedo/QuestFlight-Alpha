# Deterministic extraction summary

- Source commit: `cd4131fa7732a6db2d7387921dfef33e2c206970`
- Mode: `copy`
- Files inventoried: **338**
- Total copied/inventoried bytes: **43775309**
- Missing allowlist entries: **0**
- Product-owned replacements intentionally not copied: **2**
- Rejected scene dependencies: **1**
- Unknown scene dependencies: **2**
- Conditional local-only dependencies: **0**
- Clean-product dependency readiness: **BLOCKED**

## Inventory classifications

- `approved_cc0`: 7
- `approved_local_aircraft_shell`: 4
- `approved_production`: 278
- `approved_provenance`: 2
- `approved_public_data_derivative`: 5
- `approved_requires_product_cleanup`: 28
- `approved_third_party_cc_by_4_0`: 4
- `approved_tooling`: 10

## Product-owned replacements not copied

- `scripts/build_production_quest.ps1` -> `scripts/build_production_quest.ps1` — product_replacement_not_copied
- `QuestFlightLab/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/SOURCE.txt` -> `docs/aircraft_rights_report.md` — superseded_provenance_not_copied

## Dependency blockers requiring clean-product replacement

- `QuestFlightLab/Assets/Scripts/Input/Usb2BleInputMapper.cs` — excluded_legacy_dependency
- `QuestFlightLab/Assets/Scripts/Flight/FlightTelemetry.cs` — unknown_unclassified_dependency
- `QuestFlightLab/Assets/Scripts/Runtime/ShortPlaytestDemoPilot.cs` — unknown_unclassified_dependency

Unresolved GUIDs are package/built-in candidates and must be resolved by
`ProductionDependencyClosureExporter` after Unity package restoration.
