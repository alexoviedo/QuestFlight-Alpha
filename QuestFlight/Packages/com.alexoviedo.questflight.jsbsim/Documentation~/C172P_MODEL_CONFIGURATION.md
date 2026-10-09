# C172P diagnostic model configuration

## Pinned target

The native diagnostic target is the JSBSim `c172x` approximation of a 1982
Cessna 172P, from JSBSim 1.3.1 revision
`3b25f25e49b42d0489c04ac805674fc1450ca579`. It is identified in code as
`qfl-c172p-private-fidelity-v2`. The aerodynamic, engine, propeller, landing-gear,
and flight-control definitions remain those of the pinned upstream model.

This is the product's sole flight model, not a certified aircraft model
and not a claim of flight-training fidelity.

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

ABI v2 exposes reset conditions, requested controls, atmosphere, a bounded state
vector, actual control-surface and nosewheel positions, component forces and
moments, mass/CG and fuel state, gear compression and weight-on-wheels state,
in-flight terrain elevation, and trim. The managed owner also exposes the
clamped controls and atmosphere actually submitted to the native ABI.

## Integrity and promotion status

Runtime files are checked against the trusted APK hash-manifest identity before
atomic activation; native ABI version 2 and current binary hashes are recorded
in `SOURCE_MANIFEST.json`. The historical 2026-08-13 native-only scenario
run executed 12 scenarios with no failures or errors: three passed every defined
bound and nine remain honestly `Unproven` pending independent, condition-specific
C172P envelopes. The Android ARM64 build and 16 KiB alignment pass. Exact-APK
physical Quest handling, timing, thermals, stale/tear, and allocations remain
unmeasured.

## Sources and license status

- [JSBSim 1.3.1 repository](https://github.com/JSBSim-Team/jsbsim)
- [JSBSim mass-balance/point-mass documentation](https://jsbsim-team.github.io/jsbsim/classJSBSim_1_1FGMassBalance.html)
- [JSBSim construction-frame documentation](https://jsbsim-team.github.io/jsbsim-reference-manual/user/concepts/frames-of-reference/)
- [FAA TCDS lookup guidance](https://www.faa.gov/faq/how-can-i-locate-type-certificate-data-sheet-tcds-aircraft)

JSBSim's engine/library is LGPL-2.1-or-later. The `c172x.xml` file separately
declares GPL and contains an upstream non-sale statement. That discrepancy is
preserved separately from the LGPL library record. The project owner authorized
a free public alpha on 2026-09-18 and removed the project-imposed private-use
hold; that does not grant additional upstream rights. Independent rights and
publication compliance remain a release gate (FN-029/M14). No additional
public/commercial redistribution eligibility is established by this prose.
