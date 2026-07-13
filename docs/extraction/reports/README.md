# Extraction reports

`source_copy/` is the immutable-Git extraction record. It intentionally shows
the three serialized R&D references that had to be replaced in the clean
product; that blocked source result is evidence that legacy dependencies were
not silently copied.

The JSON/CSV files directly in this directory are generated later by Unity's
`ProductionDependencyClosureExporter`, after packages are restored and the
clean scene/prefab references are authored. Only that final AssetDatabase
closure may report product readiness. Neither report substitutes for the
integrity validator's missing-reference checks or the worn headset gate.
