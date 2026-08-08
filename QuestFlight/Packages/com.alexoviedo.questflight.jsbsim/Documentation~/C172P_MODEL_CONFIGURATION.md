# C172P diagnostic model configuration

## Pinned target

The native diagnostic target is the JSBSim `c172x` approximation of a 1982
Cessna 172P, from JSBSim 1.3.1 revision
`3b25f25e49b42d0489c04ac805674fc1450ca579`. It is identified in code as
`qfl-c172p-training-2054-v1`. The aerodynamic, engine, propeller, landing-gear,
and flight-control definitions remain those of the pinned upstream model.

This is a development diagnostic model, not a certified aircraft model and not
a claim of flight-training fidelity.

## Deterministic load

The upstream file loaded every declared point mass by default: 1,454 lb empty,
190 lb pilot, 140 lb copilot, 236 lb rear occupants, 120 lb baggage, an 80 lb
`PesticideBomb` test payload located 144 inches right of centerline, and 260 lb
fuel. That produced 2,480 lb at startup and a large unintended lateral moment.

QuestFlight's bounded diagnostic derivative uses:

| Item | Weight | Location/intent |
|---|---:|---|
| Empty aircraft | 1,454 lb | Upstream empty model |
| Left front occupant | 170 lb | Station x=36 in, y=-14 in |
| Right front occupant | 170 lb | Station x=36 in, y=+14 in |
| Left fuel | 130 lb | Upstream left tank |
| Right fuel | 130 lb | Upstream right tank |
| Rear occupants, baggage, test payload | 0 lb / absent | Deliberately excluded |
| **Initial total** | **2,054 lb** | Symmetric diagnostic baseline |

The FAA Type Certificate Data Sheet 3A12 lists 2,400 lb as the normal-category
maximum weight for a 172P landplane. The 2,054 lb baseline is below that limit,
but this table is not an operational weight-and-balance calculation.

## Output and diagnostic contract

The active 10 Hz `JSBout172B.csv` block and its unused standalone output file
are removed. The wrapper already disables JSBSim input/output, but eliminating
the declarations makes the no-file-I/O contract explicit and deterministic.

ABI v1 exposes reset conditions, requested controls, atmosphere, and a bounded
state vector. The managed wrapper now exposes the clamped controls and
atmosphere actually submitted to ABI v1 plus the configured initial conditions.
ABI v1 does **not** expose actual control-surface positions, forces, moments,
mass/CG, fuel state, or an in-flight terrain update. Those values must not be
inferred from commands. Adding them requires a versioned native ABI rebuild and
fresh Windows/Android scenario evidence.

## Integrity and promotion status

Every extracted runtime file is SHA-256 checked at preparation time. Native ABI
version remains 1 and the binary hashes are unchanged. A Windows package-owned
smoke loaded the derivative, reset it, and advanced 120 fixed steps at 120 Hz
with finite state. Changing model data still invalidates the broader earlier
scenario claims: the 11-scenario software gate, allocation gate, Android build,
and physical Quest handling/timing gate must be rerun on the unchanged candidate
before any promotion decision.

## Sources and license status

- [JSBSim 1.3.1 repository](https://github.com/JSBSim-Team/jsbsim)
- [JSBSim mass-balance/point-mass documentation](https://jsbsim-team.github.io/jsbsim/classJSBSim_1_1FGMassBalance.html)
- [JSBSim construction-frame documentation](https://jsbsim-team.github.io/jsbsim-reference-manual/user/concepts/frames-of-reference/)
- [FAA TCDS lookup guidance](https://www.faa.gov/faq/how-can-i-locate-type-certificate-data-sheet-tcds-aircraft)

JSBSim's engine/library is LGPL-2.1-or-later. The `c172x.xml` file separately
declares GPL and contains an upstream non-sale statement. That discrepancy is
an unresolved product-distribution blocker. The derivative may be used only as
a private development diagnostic until the project owner resolves it; no
commercial/release eligibility is claimed.
