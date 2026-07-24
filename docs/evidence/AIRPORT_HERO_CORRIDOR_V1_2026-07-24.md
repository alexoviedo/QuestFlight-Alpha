# Airport hero corridor V1 evidence — 2026-07-24

Source branch: `feature/airport-hero-corridor-v1`

The bounded airport/takeoff corridor contains 81 project-authored, footprint-preserving building replacements generated from `AirportHeroCorridor.blend`. Blender generation produced 9,248 LOD0, 6,800 LOD1, and 4,328 LOD2 triangles across the combined runtime meshes. Replaced generic footprints are excluded from the aerial building surface so only one representation remains.

Validation:

- EditMode: 122/122 passed.
- Production Visual QA: 89/89 captures, `PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED`.
- Maximum captured estimate: 56 draws, 636,224 visible triangles, 29 visible materials.
- Runway-to-terrain, marking-to-runway, and collider disagreement remained within their existing contracts.
- Android APK SHA-256: `95c2cf4b91ef5c2c17c1095ccc9f111d9840305d2bd2b3a40a7aacd63faf63a8`.

The final worn Quest 3 review confirmed that roofs enclose their buildings, gable orientations are plausible, no inverted/detached/impossible roofs remain, and no generic-box, scale-pop, or stutter regression was observed. Human visual score: 8/10.

The corrected machine evidence is stored outside Git under:

`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_hero_corridor_v1_20260724_123707\visual_qa_accepted_corrected_cameras`

This is a development-quality airport corridor, not surveyed architectural fidelity or a final performance acceptance.

