# Known limitations

- The extracted source began at 4/10 and reached 5/10 after its first targeted visual pass. That historical visual-score precondition prevented a worn Quest gate, but it was not evidence of standalone hardware failure and no longer determines product scope.
- The bounded NAIP macro environment, fixed DEM horizon, modular airport kit, runway material, reservoir shader, sparse vegetation, and cockpit material pass materially improve the scene, but the runway, airport structures, reservoir surface, and far mountains remain intentionally Quest-bounded and visibly stylized rather than photorealistic.
- Meta XR Simulator live stereo evidence is available, but the ten required VRS head/controller recordings still require a one-time supported simulator-UI recording pass. The replay gate fails closed until those files exist.
- Worn stereo stability, comfort, controller feel, and subjective seat placement still require a human headset witness. Unattended hardware metrics, when collected, do not substitute for that review.
- The unrecorded Quest 3 control now launches without a Guardian block and completed the exact 90-second warmup plus 60.64-second measurement. The strict machine gate still did not pass: app-PID VrApi reported 60 exact-window samples, 72.55 average FPS, 12.391 ms average and 13.901 ms p95 combined CPU/GPU time, 4/60 samples above the 13.8889 ms budget, two stale frames, zero tears, and 32 C maximum temperature. Direct GPU milliseconds and release allocation proof remained unavailable, and the unattended run cannot establish comfort, controller feel, or worn visual quality. This narrow result does not establish standalone infeasibility or justify a PCVR fallback.
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
