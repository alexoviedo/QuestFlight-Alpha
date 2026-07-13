# Clean product extraction contract

The product extraction is pinned to R&D commit
`cd4131fa7732a6db2d7387921dfef33e2c206970` and is driven by
`tools/extraction_manifest.json`. The extractor reads every source byte from
that Git object rather than from the mutable R&D working tree.

## Deterministic workflow

```powershell
python tools/extract_from_rd.py `
  --source-repo C:\Users\ovied\Dev\T2\T2-QuestFlightLab `
  --source-ref cd4131fa7732a6db2d7387921dfef33e2c206970 `
  --destination-project .\QuestFlight `
  --manifest .\tools\extraction_manifest.json `
  --dry-run
```

Replace `--dry-run` with `--copy` only against an empty or already-identical
destination. Existing differing files are never overwritten. Every selected
Unity asset must have its paired `.meta`; folder metas are preserved as well.
Each run emits stable JSON/CSV hashes and a Markdown summary under
`docs/extraction/reports` unless `--report-dir` points to an external artifact
location.

`product_replacements` records R&D files that are deliberately not copied. The
clean product's separated build wrapper is the first such case: it occupies the
same destination path but has product-only project assumptions. This exception
does not weaken duplicate-destination refusal for any selected extraction file.

The source scene intentionally reports pre-extraction blockers. Its serialized
R&D prefab still reaches `Usb2BleInputMapper`, `FlightTelemetry`, and
`ShortPlaytestDemoPilot`. Those references must be removed or replaced in the
clean authored product before the Unity closure is allowed to pass. Reporting
these leaks is the purpose of the source-closure comparison; copying the
excluded scripts is not an acceptable fix.

The manifest separately names four clean product-authored runtime files. They
are classification entries only and are never read from or copied out of R&D:
the control adapter, deterministic demo pilot, runtime attribution component,
and runtime policy. Any additional scene dependency remains unknown until it is
reviewed and explicitly classified.

## Aircraft rights gate

Only the exact aircraft GLB and paired meta are enabled under
`rights_gated_entries`. Workstream C independently verified the original
Sketchfab source identity and its CC BY 4.0 license. The stale R&D `SOURCE.txt`
and meta are explicitly classified as superseded provenance and are not copied;
the product rights report, asset-license record, and third-party notice replace
them. The embedded GLB metadata and extraction hash remain preserved. Any other
`ImportedAssets` dependency is rejected.

## Final Unity closure

After packages are restored and the clean scene/prefabs are integrated, run:

```powershell
Unity.exe -batchmode -quit -projectPath .\QuestFlight `
  -executeMethod T2.QuestFlight.Editor.ProductionDependencyClosureExporter.ExportBatch
```

The exporter uses `AssetDatabase.GetDependencies` and rewrites the sorted
closure and comparison reports. A final closure is ready only with zero legacy
and zero unknown asset dependencies. Package dependencies are classified
separately; missing serialized references are handled by the product integrity
validator.
