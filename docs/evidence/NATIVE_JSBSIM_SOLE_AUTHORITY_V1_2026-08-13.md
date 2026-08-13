# Native JSBSim sole authority V1 — 2026-08-13

## Decision

QuestFlight now has one flight model: the embedded native JSBSim 1.3.1 C172P
derivative. The UnityPrototype backend and `SimpleAircraftPhysics` source,
serialized component, build selection, test adapter, and runtime fallback were
removed. Editor, development, and release products share the same native
authority. Native preparation, integrity, ABI, initialization, reset,
non-finite-state, or step failure fails closed.

Source branch: `feature/native-jsbsim-sole-authority-v1`

Starting source: `f624490584aca3a6f187fca8b81ac5a5458a306a`

## Proven root cause

The prior high-resolution-ground APK was a development build whose build
manifest explicitly recorded `forceUnityPrototype=true` and
`enableNativeJsbsim=false`. Its power-off trace at approximately 73 kt produced
an impossible mean sink near -6,832 ft/min, final sink near -7,165 ft/min, and
angle of attack increasing from about 42 to 75 degrees. That implementation
applied a fixed-density authored rate model and lift along Unity `transform.up`;
it did not integrate the native C172 aerodynamics, propulsion, mass/CG, wind,
terrain, or landing-gear model.

The native C172P trace at approximately 72 kt recorded about -260 ft/min mean
sink. This is strong regression evidence that the observed vertical plunge
belonged to the retired prototype path. It is not yet a validated best-glide
performance point because weight/CG, atmosphere, propeller state, trim, and an
independent reference envelope are not pinned as an acceptance contract.

## Native model boundary

- JSBSim: 1.3.1, revision
  `3b25f25e49b42d0489c04ac805674fc1450ca579`
- Model: `qfl-c172p-private-fidelity-v2`, based on upstream `c172x`
- Initial load: 2,054 lb, symmetric front occupants and fuel
- Native ABI: version 2
- Android: ARM64 shared libraries, NDK 27.2.12479018, verified 16 KiB ELF LOAD
  alignment
- Runtime model and binaries: SHA-256 validated before initialization
- Distribution: private personal use; public/commercial redistribution is not
  claimed for the aircraft model payload

## Scenario evidence

Artifact root:
`C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\native_jsbsim_sole_authority_v1_scenarios`

Twelve native-only scenarios ran with zero failures and zero errors. Beta/yaw
damping, coordinated turn, and ground steering passed all currently defined
bounds. Control doublets, neutral trim, slow-flight/stall recovery, power-off
glide, flap response, takeoff/climb, approach/landing, mass/CG, and wind remain
`Unproven` where no independently sourced, condition-specific C172P acceptance
envelope is pinned. `Unproven` is not a failure, but it is also not a fidelity
pass.

## Validation

- EditMode after prototype-source removal: PASS 319/319, zero failed, zero
  skipped/ignored.
- PlayMode after prototype-source removal: PASS 10/10, zero failed, zero
  skipped/ignored.
- Android release-style build: PASS, ARM64/IL2CPP/Vulkan/OpenXR; embedded
  `libJSBSim.so`, `libqfl_jsbsim_native.so`, and pinned JSBSim runtime data
  verified in the APK. APK size 1,462,983,127 bytes; SHA-256
  `47e7c7772f10fef8c0e84593d6f6c9ccef0ea70ab90867c133aeefd8d0ee1461`.
- Quest worn handling: ACCEPTED. Alex tested the exact APK above on the Quest 3
  and reported that the current version was working well. No prototype fallback,
  vertical-drop, teleport, freeze, crash, ANR, or OOM was reported during this
  bounded worn smoke.
- Exact-APK long-duration frame/thermal performance: not measured. The worn
  acceptance is a canonical development checkpoint, not a final performance or
  flight-fidelity certification.

This change does not claim FAA approval, navigation suitability, legal training
credit, certified C172 fidelity, or final Quest performance.
