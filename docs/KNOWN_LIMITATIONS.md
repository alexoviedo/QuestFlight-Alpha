# Known limitations

- The extracted source began at 4/10 and reached 5/10 after its first targeted visual pass. That historical visual-score precondition prevented a worn Quest gate, but it was not evidence of standalone hardware failure and no longer determines product scope.
- The bounded NAIP macro environment, fixed DEM horizon, modular airport kit, runway material, reservoir shader, sparse vegetation, and cockpit material pass materially improve the scene, but the runway, airport structures, reservoir surface, and far mountains remain intentionally Quest-bounded and visibly stylized rather than photorealistic.
- Meta XR Simulator live stereo evidence is available, but the ten required VRS head/controller recordings still require a one-time supported simulator-UI recording pass. The replay gate fails closed until those files exist.
- Worn stereo stability, comfort, controller feel, and subjective seat placement still require a human headset witness. Unattended hardware metrics, when collected, do not substitute for that review.
- One unattended Quest 3 run completed the 90-second warmup and 60-second sample, but the strict machine gate did not pass: delivered cadence averaged 13.8886 ms while p95 was 14.4589 ms, 54.33% of samples exceeded the exact 72 Hz interval threshold, release allocation instrumentation was unavailable, and no direct GPU/VrApi/stale/tear series was exposed. An unrecorded control was then blocked by Horizon's `Finding position in room` / `GUARDIAN_TRACKING_LOST_CONTINUE` dialog. Alex must choose `Continue without tracking` or establish a stationary boundary once before the control can be repeated; no CPU/GPU bottleneck or standalone infeasibility is inferred from the incomplete evidence.
- The Unity prototype backend is authoritative and is not a certified or final C172 flight model.
- Native JSBSim now passes its software promotion gate (11/11 total, 10/10
  critical) and is present as a bounded development-only package. Release
  builds still resolve to the Unity prototype; physical Quest timing and
  handling have not been measured. Its frozen C172 definition also retains a
  10 Hz `JSBout172B.csv` diagnostic output that must be disabled or explicitly
  redirected, followed by a complete gate rerun, before native release
  authority. Native JSBSim is not a release default and no certified-fidelity
  or training claim is made.
- The KOG_THORNS aircraft is redistributed under CC BY 4.0 with attribution; CC BY does not grant Cessna/Textron trademark rights, so public commercial release still requires a branding/no-endorsement review.
- Several cockpit controls lack separable pivots and remain static.
- Compatibility beyond the tested Quest 3/Unity configuration is not claimed.
- The validated APK is non-debuggable and passes v2 integrity plus 16 KiB ELF alignment, but it uses the Android Debug signing certificate. Store distribution requires a production signing configuration.
- Meta XR Simulator can establish API-level stereo, tracking, input, and composition behavior only; it cannot establish Quest GPU timing, thermals, comfort, or worn-device acceptance.
- PCVR, Quest Link, Air Link, remote rendering, Windows shipping builds, and required-PC runtime dependencies are out of scope.
- The application is not FAA approved, not for navigation, and provides no legal training credit.
