# Product repository instructions

- The product scene is explicitly authored and serialized.
- Runtime repair or bootstrap code must never construct the product XR, aircraft, environment, or backend hierarchy.
- Exactly one enabled XR Origin, tracked Main Camera, aircraft simulation root, and flight authority may exist.
- The Main Camera is owned by OpenXR and must never be manually written.
- Aircraft rotation occurs around the aircraft center of gravity, never around the user, XR Origin, camera, or seat.
- User calibration affects only the seat-relative calibration transform.
- Legacy R&D systems may not enter the product dependency closure.
- Product changes require inspected Visual QA and build validation.
- Physics changes require scenario evidence.
- Do not claim FAA approval, training credit, certified fidelity, production photorealism, broad compatibility, or Quest performance without evidence.
- Do not commit Unity `Library`, `Temp`, `Obj`, `Logs`, `Builds`, APKs, captures, package caches, raw downloads, or unclear-license assets.
- Every committed binary asset requires a source and license record.
- The Unity prototype backend remains authoritative until the unchanged native JSBSim promotion gate passes.
- Preserve paired Unity `.meta` files and GUIDs for extracted assets.
- Keep restricted or unclear-rights aircraft assets local and uncommitted.
