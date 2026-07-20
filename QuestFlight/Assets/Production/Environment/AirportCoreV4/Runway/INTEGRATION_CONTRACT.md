# Airport-core V4 continuous runway integration contract

- Replace only the material binding on the authoritative V2 runway mesh.
- Preserve mesh vertices, endpoint elevations, grade, collider, controlled-depth
  markings, and all current runway/terrain tolerances.
- The shader derives one longitudinal/lateral coordinate frame from the authored
  runway 08/26 endpoints. It does not sample mesh UV or lightmap charts.
- Rubber, sparse joints, irregular repairs, and edge dirt are analytic functions
  of that continuous runway-local coordinate. No macro feature texture or chart
  reset is permitted.
- Keep this material opaque with ZWrite enabled. Do not add overlay geometry,
  transparent wear decals, tessellation, displacement, or another shadow pass.
- Full-resolution close/overview QA must prove no tiling, no floating markings,
  no z-fighting, and coherent exposure against the orthophoto before acceptance.
- Simulator stereo and the physical Quest budget remain mandatory later gates.
