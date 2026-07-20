# Airport-core V4 lighting integration contract

- Serialize this fixed profile under `AirportCoreBenchmarkRoot`; do not construct it at runtime.
- Current V4.1 fallback has no accepted lightmap. Its bounded remediation target is one non-directional 1024 atlas; the original Stage-1 ceiling of one or two compressed 2048 atlases remains a broader future limit, not current evidence.
- Disable broad realtime shadows on those static renderers and reconcile every removed photograph shadow.
- Use the listed light probes for dynamic aircraft/props and at most one baked 128-face reflection probe.
- Opaque terminal glazing samples the selected sky/reflection probe; no realtime reflection updates.
- Preserve the existing cockpit shadow contract; an aircraft-only realtime sun shadow is an A/B candidate, not a default.
- Any future successful Unity bake requires exported lighting-data hashes, memory, overlap, pixel-level finiteness validation, and full-resolution A/B evidence.
- Simulator stereo and physical Quest timing remain separate mandatory gates.
