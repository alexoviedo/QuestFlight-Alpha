# Standalone Quest product requirements

## Shipping target

Meta Quest 3 is the sole shipping target. The application must remain a self-contained Android ARM64, Vulkan, and OpenXR product. PCVR, Quest Link, Air Link, remote rendering, Windows shipping builds, and a required PC runtime are not supported architectures or fallback strategies.

The product scene and its XR, camera, aircraft, environment, calibration, and flight-authority hierarchy remain explicitly authored and serialized. OpenXR owns tracked camera motion. Legacy R&D systems may not enter the product dependency closure. The Unity prototype flight backend remains authoritative unless native JSBSim passes every unchanged promotion gate.

## Superseded recommendation

The historical recovery evidence in `docs/evidence/CLEAN_PRODUCT_EXTRACTION_AND_QUEST_GATE_2026-07-12.md` is retained without alteration. Its PCVR recommendation followed from a 6/10 visual-score precondition that the first art pass did not meet. It did not follow from measured Quest frame timing, thermal behavior, or another demonstrated standalone hardware failure. That recommendation is superseded by the standalone-only product requirement.

## Development evidence

Meta XR Simulator and a Windows/OpenXR development executable or Unity Play Mode are permitted only for development and repeatable visual regression. Simulator evidence may cover API-level stereo rendering, tracking, controller input, composition, scene stability, and deterministic replay. Simulator or Editor timing must never be labeled as Quest hardware performance.

Product visual changes require inspected stereo simulator captures plus the standard Visual QA. Physical Quest hardware remains required for final frame-time, thermal, comfort, controller, Guardian, and worn-display acceptance. Absence of headset evidence is reported as unmeasured, never inferred.

## Decisions

The allowed product decisions are:

1. continue standalone Quest;
2. narrow the standalone Quest feature or visual scope; or
3. pause or stop the project when appropriate evidence justifies that outcome.

PCVR is not an allowed product decision. Standalone failure must not be declared without measured physical-headset evidence.
