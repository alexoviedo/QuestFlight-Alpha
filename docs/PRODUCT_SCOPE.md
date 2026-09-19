# Product scope

## Included

- standalone Quest 3 as the sole shipping target;
- self-contained Android ARM64, Vulkan, and OpenXR runtime architecture;
- one C172-style aircraft/cockpit presentation path;
- one KBDU-inspired authored scene and environment;
- one embedded native JSBSim 1.3.1 C172P flight authority;
- deterministic demo and standard gamepad input;
- seat-relative viewpoint calibration;
- six short C172 simulator-practice lessons, local progress, offline spoken coaching, and explicit optional assistance (approved September 18, 2026);
- production build, Visual QA, and Quest temporal gates.
- Meta XR Simulator solely as a development, stereo-composition, tracking, input, and visual-regression tool.

## Excluded from v1

- advanced flight-training curriculum, certification, or training-credit claims;
- USB2BLE, HOTAS, and hardware-integration experiments;
- Gaussian splats;
- a UnityPrototype flight-dynamics authority, second flight authority, native
  sidecar process, required-PC flight service, or runtime backend fallback;
- legacy R&D scenes, runtime repair, procedural fallback world construction, debug HUDs, and verbose evidence overlays;
- airport/world expansion unrelated to the vertical slice.
- PCVR, Quest Link, Air Link, Windows shipping builds, remote rendering, and any required-PC product dependency;
- simulator/editor timing represented as Quest hardware performance.

If the standalone target must change, the allowed product decisions are to continue standalone Quest, narrow its feature or visual scope, or pause/stop. PCVR is not a supported fallback. Actual Quest frame-time, thermal, comfort, and controller acceptance requires physical-headset evidence before release.

The application is not FAA approved and is not for navigation or legal pilot-training credit.
