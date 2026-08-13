# Flight-dynamics model provenance

The sole product flight model is the embedded JSBSim 1.3.1 `c172x`
approximation of a 1982 Cessna 172P, patched as
`qfl-c172p-private-fidelity-v2`. Exact model, runtime-data, wrapper, ABI, and
binary hashes are maintained in:

- `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/FLIGHT_DYNAMICS_MODEL_PROVENANCE.json`
- `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/SOURCE_MANIFEST.json`
- `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/Documentation~/C172P_MODEL_CONFIGURATION.md`

The deterministic baseline load is 2,054 lb: 1,454 lb empty aircraft, two
symmetric 170 lb front occupants, and two symmetric 130 lb fuel tanks. Rear
occupants and baggage default to zero, and the upstream off-center
`PesticideBomb` test payload is absent.

Native ABI version 2 exposes the configured initial conditions, submitted
controls and atmosphere, actual control-surface and nosewheel positions,
component forces and moments, mass, center of gravity, fuel, gear compression,
weight-on-wheels state, in-flight terrain elevation, and trim. The native
libraries and model data are SHA-256 validated before authority initialization.

JSBSim is now the only product authority; there is no UnityPrototype fallback.
The 2026-08-13 native-only scenario run executed 12 scenarios with zero failures
or errors. Three scenarios passed every defined bound and nine remain
`Unproven` because condition-specific independent C172P reference envelopes are
not yet pinned. Physical Quest handling and performance of the exact APK are
not yet accepted.

The JSBSim library is LGPL-2.1-or-later. The aircraft XML separately declares
GPL and includes an upstream non-sale statement, so this derivative is recorded
for private personal use and no public/commercial redistribution claim is made.
No certified fidelity, FAA approval, navigation suitability, or training credit
is claimed.
