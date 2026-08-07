# Airport-core V4 continuous runway integration contract

- Replace only the material binding on the authoritative V2 runway mesh.
- Preserve mesh vertices, endpoint elevations, grade, collider, controlled-depth
  markings, and all current runway/terrain tolerances.
- The shader derives one longitudinal/lateral coordinate frame from the authored
  runway 08/26 endpoints. It does not sample mesh UV or lightmap charts.
- The generated 1024² albedo/normal pair supplies neutral-gray photographic
  aggregate at two rotated scales. Android uses ASTC 6x6, mipmaps, trilinear
  filtering, and anisotropy 8; the normal is tangent-space and contains no
  baked illumination.
- The existing packed texture slot is a unique runway-length wear map sampled
  in normalized runway-local coordinates: R tire polish, G roughness, and B
  repairs/edge weathering. It replaces repeating analytic patch/joint waves and
  adds no material, sampler, renderer, or draw call.
- The source appearance follows City of Boulder imagery and the FAA-derived
  excellent-condition asphalt record: restrained wear, sparse sealed cracks,
  no exaggerated damage, no warm-brown base, and no repeating slab pattern.
- Keep this material opaque with ZWrite enabled. Do not add overlay geometry,
  transparent wear decals, tessellation, displacement, or another shadow pass.
- Full-resolution close/overview QA must prove no tiling, no floating markings,
  no z-fighting, and coherent exposure against the orthophoto before acceptance.
- Simulator stereo and the physical Quest budget remain mandatory later gates.
