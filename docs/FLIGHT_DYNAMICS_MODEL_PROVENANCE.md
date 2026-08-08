# Flight-dynamics model provenance

The bounded native diagnostic model is the JSBSim 1.3.1 `c172x` approximation
of a 1982 Cessna 172P, patched as `qfl-c172p-training-2054-v1`. Its exact load,
runtime-data hashes, ABI limitations, and source record are maintained in:

- `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/FLIGHT_DYNAMICS_MODEL_PROVENANCE.json`
- `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/SOURCE_MANIFEST.json`
- `QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/Documentation~/C172P_MODEL_CONFIGURATION.md`

The target load is 2,054 lb: 1,454 lb empty aircraft, two symmetric 170 lb
front-seat occupants, and two symmetric 130 lb fuel tanks. Rear occupants,
baggage, and the upstream off-center `PesticideBomb` test payload are absent.
The prior data set loaded 2,480 lb and was not a valid deterministic baseline.

The native ABI remains version 1 and native binaries are unchanged. Product
release authority remains the Unity prototype. Earlier native scenario results
do not validate the patched model and must be rerun on an unchanged candidate.

The JSBSim library is LGPL-2.1-or-later. The aircraft XML separately declares
GPL and includes an upstream non-sale statement. Native product promotion and
commercial distribution are blocked pending an explicit license/provenance
resolution. No certified fidelity, FAA approval, navigation suitability, or
training credit is claimed.
