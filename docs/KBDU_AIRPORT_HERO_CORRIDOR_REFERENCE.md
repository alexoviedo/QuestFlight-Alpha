# KBDU airport hero corridor

The production airport corridor replaces the generic context-building presentation for 81 airport-owned and immediately adjacent takeoff/landing-visible footprints. The authored set preserves pinned local-ENU footprints and uses semantic hangar, commercial, residential, and light-industrial families. Rectangular residential footprints receive footprint-aligned gables; irregular and commercial footprints use low exact-footprint caps so roofs do not overhang unrelated geometry.

Reference inputs are limited to factual and rights-cleared data:

- City of Boulder airport hangar addresses and Type 1/2/3 hangar definitions;
- the repository's pinned OpenStreetMap-derived footprints (ODbL-1.0);
- project-authored Blender geometry, materials, and deterministic export tooling.

City photographs were used only as external visual references and are not committed. The models are reference-faithful procedural approximations, not surveyed structures.

Runtime authority:

- `AirportHeroCorridorRoot` is explicitly serialized under `ProductionEnvironmentRoot`;
- three fixed LOD meshes share the existing airport building atlas;
- matching generic footprint geometry is excluded from the aerial-surface dependency;
- no runtime construction or repair path exists.

