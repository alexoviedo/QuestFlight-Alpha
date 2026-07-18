# Airport-core V4 continuous runway integration contract

- Replace only the material binding on the authoritative V2 runway mesh.
- Preserve mesh vertices, endpoint elevations, grade, collider, controlled-depth
  markings, and all current runway/terrain tolerances.
- The shader maps its two asphalt frequencies in stable world XZ space, so
  separate runway sections do not restart an alternating large macro block.
- The authored feature mask uses the mesh UV once for rubber, repairs, cracks,
  and edge dirt. Confirm that the authoritative mesh has continuous UV0 before
  enabling those features; otherwise bind a neutral feature mask until UV0 is
  corrected without geometry movement.
- Keep this material opaque with ZWrite enabled. Do not add overlay geometry,
  transparent wear decals, tessellation, displacement, or another shadow pass.
- Full-resolution close/overview QA must prove no tiling, no floating markings,
  no z-fighting, and coherent exposure against the orthophoto before acceptance.
- Simulator stereo and the physical Quest budget remain mandatory later gates.
