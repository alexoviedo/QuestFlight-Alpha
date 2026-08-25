# Traffic V2 Gate 2 Unity Integration

Date: 2026-08-24

Accepted source revision: `fda11a25eb526824d82a47330f2be80b1410bb64`

Status: **PASS_GATE2_IMPLEMENTATION_ONLY**

This checkpoint integrates the exact Gate-1 walking-skeleton trajectory manifest into the authored production TrafficRoot. It is not a full-map traffic milestone, visual acceptance, an Android build, an APK, a headset result, or a Quest performance claim.

## Usable product change

- `KBDU_TrafficRoot.prefab` now serializes exactly one `KbduTrafficRuntime` and one `KbduTrafficV2Runtime` on its existing renderer-free root.
- Traffic V1 moving vehicles are explicitly disabled. Its accepted 24,347 parked-vehicle records remain enabled and continue to use the existing instanced renderer.
- Traffic V2 owns moving presentation for the exact 12 Gate-1 non-wrapping trips in the airport, divided-highway, and residential demonstration areas.
- The runtime uses the accepted six near meshes, three proxy bindings, one opaque instanced material, and the accepted production terrain surface. It creates no runtime hierarchy and writes no camera transform.
- The schedule uses one deterministic global clock, clamps at 900 seconds, and does not wrap.
- Vehicle position and heading are interpolated from frozen keyframes; height and slope alignment come from the serialized `ProductionTerrainSurfaceAsset` rather than physics raycasts.
- Instanced submissions are explicitly chunked at Unity's 1,023-instance limit. This walking skeleton contains at most 12 authored moving vehicles and exactly three concurrent trips.

## Frozen inputs

| Input | Bytes | SHA-256 |
|---|---:|---|
| `KBDU_TrafficV2Gate1TrajectoryManifest.json` | 131,675 | `dd687bc7de331cefc439f755de643a79bad6e54ecb2772a71efefbdddde3a469` |
| `KBDU_TrafficV2Gate1SourceAndLicense.json` | 1,126 | `b842a2559b01d8b4c8279f9dc44b92229803d06262aa9f69e2298bf40b5ef10c` |
| Gate-1 source freeze | 1,161,313 | `c7cd7b9362d01bacbeb7c23833cae65882bceaaaab767dcb00f9b8c0904e07b6` |

The product contains the compiled 131 KB derivative and its ODbL source/attribution record. It does not contain the 88 MB raw OSM download or other raw acquisition payloads.

## Focused validation

Unity: `6000.3.8f1`

Test filter: `QuestFlightLab.Environment.Editor.Tests.KbduTrafficV2Gate2EditModeTests`

Result: **5 passed, 0 failed, 0 skipped**, 1.1747844 seconds.

The five tests verify:

1. V1 parked traffic is retained while V1 moving traffic is disabled and the V2 contract passes.
2. The product environment resolves exactly one V1 parked runtime and one V2 moving runtime.
3. Manifest and provenance bytes match the compiled-in SHA-256 contracts.
4. Schedule time zero evaluates one trip in each Gate-1 area and the non-looping schedule does not wrap at 900 seconds.
5. The runtime source contains no hierarchy bootstrap or camera writes and contains the 1,023-instance chunk loop.

Focused result XML (local, not for commit): `build/traffic_v2_gate2_tests.xml`, 9,335 bytes, SHA-256 `245361fc85ff07355833571ea54566970e8edf4e9fbf20bcf6a00d35df0935b7`.

## Execution accounting

- The first Unity authoring run rebuilt the absent `Library` cache and then completed successfully, serializing the prefab.
- The first test command incorrectly included `-quit`; Unity compiled and exited before producing results. No test result was claimed from that invocation.
- The corrected focused test command omitted `-quit`, produced the bound XML above, and passed 5/5.
- No full test suite, Visual QA, Android build, APK install, or headset run was performed in this gate.

## Remaining gates

Gate 3 must visually inspect the moving vehicles in the product scene, including road height/orientation and duplicate suppression, before any product visual acceptance. An Android ARM64/Vulkan/OpenXR build and physical Quest 3 install/run remain separate explicitly approved work. Full-map traffic, demand calibration, intersection behavior, continuous collision clearance, density targets, thermal behavior, and Quest performance remain unimplemented or unproven.
