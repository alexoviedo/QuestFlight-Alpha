# Quest Environment Reconstruction V2 Rendering Strategy

## Decision

Retain Unity's Built-in Forward Render Pipeline for this milestone. The product does not contain the Universal Render Pipeline package or any configured Scriptable Render Pipeline asset. Migrating to URP would require shader, material, lighting, XR stereo, and performance revalidation across the complete authored product scene, so it is outside this reconstruction milestone.

GPU Resident Drawer is not applicable. Unity supports it in URP only with the Forward+ rendering path and a compute-capable graphics API; the current Built-in pipeline does not provide it. GPU occlusion culling associated with GPU Resident Drawer is likewise not applicable and will not be enabled. The open flight environment has limited occlusion, so any different occlusion system would require an isolated physical-Quest A/B result before adoption.

## Current shipping rendering contract

- Standalone Quest 3 remains the sole shipping target.
- Android remains ARM64, IL2CPP, Vulkan-only, ASTC, OpenXR single-pass instanced, and self-contained.
- The authored product uses 4x MSAA, multithreaded rendering, mipmaps, and texture mip streaming.
- Custom environment shaders must preserve Built-in Forward lighting, GPU-instancing variants, and the full Unity single-pass-instanced stereo macro contract.
- Opaque, depth-writing materials remain the default. Transparent material slots stay tightly bounded.
- The immutable far terrain remains one world-anchored renderer without runtime LOD, crossfade, dithering, billboard behavior, or camera-relative scaling.

## Reconstruction architecture

Environment scale is handled through deterministic offline production, not a custom virtualized-geometry renderer:

1. Generate building, vegetation, prop, and middle-distance HLOD assets offline in approximately 200-300 m cells where appropriate.
2. Keep shared PBR-style atlases and a small material set. Do not create a material per object or cell.
3. Use non-crossfaded `LODGroup` tiers for screen-size selection. Preserve stable stereo silhouettes and avoid XR shimmer.
4. Use ordinary Built-in GPU instancing for repeated meshes that share one shader and material, especially trees and repeated airport props.
5. If repeated-instance counts justify it, use bounded `Graphics.RenderMeshInstanced` batches generated from serialized placement data. Allocate instance arrays once, issue at most one call per visible mesh/material/cell batch, and create no steady-state managed allocations.
6. Use one bounded `CullingGroup` controller for serialized HLOD cell bounds only after the offline cells exist. It may read the single Main Camera for center-camera visibility and far-distance bands, but must never write the OpenXR-owned camera or build product hierarchy at runtime.
7. Do not combine meshes at runtime and do not attach per-instance `Update` methods.

`CullingGroup` supplements rather than replaces `LODGroup`: LOD groups select screen-relative mesh detail, while the culling controller can disable whole cells beyond their authored range. Cell transitions and instance batches require stereo simulator review and physical Quest measurement before acceptance.

## Budget policy

The integrated reconstruction targets are:

- no more than 130 maximum estimated visible draw slots;
- no more than 650,000 maximum estimated visible triangles;
- no more than 30 maximum visible materials;
- no more than 220 MiB estimated runtime texture memory;
- no more than three transparent material slots;
- zero steady-state managed allocations from environment systems.

Static Editor estimates are planning evidence only. The budget report distinguishes maximum-visible materials from all distinct active-scene materials and records the configured render pipeline. Its renderer-slot estimate is not a measured GPU draw-call count. Final timing, thermal, stale-frame, and allocation acceptance requires the physical Quest 3.

New reconstruction content replaces or consolidates prior context assets; nominal headroom is not permission to append an additional world layer. GPU occlusion, HDR changes, or broad quality reductions require an isolated A/B result identifying a measured benefit without stereo or visual regression.

## Rejected alternatives

- URP migration solely to obtain Forward+ or GPU Resident Drawer.
- A custom Nanite-like or runtime virtualized-geometry system.
- GPU occlusion without a measured win.
- Runtime mesh combining.
- Unique materials or per-instance material clones.
- Camera-facing single-plane vegetation billboards that can diverge between stereo eyes.
- Simulator or Editor timing presented as Quest hardware performance.
