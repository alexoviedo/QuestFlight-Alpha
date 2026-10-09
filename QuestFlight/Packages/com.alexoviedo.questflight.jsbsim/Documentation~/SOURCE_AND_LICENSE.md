# Source, build, and license record

The shared JSBSim library is upstream JSBSim 1.3.1 at revision
`3b25f25e49b42d0489c04ac805674fc1450ca579`, from
<https://github.com/JSBSim-Team/jsbsim.git>, licensed GNU
LGPL-2.1-or-later. The complete license text travels with the runtime data at
`RuntimeData/JSBSim-1.3.1/COPYING.txt`.

The project-owned C ABI wrapper is maintained in `alexoviedo/T2-QuestFlight`.
`SOURCE_MANIFEST.json` records the current base commit, modified-working-tree
identity, exact header/implementation input SHA256, line-ending policy, native
binary hashes, architecture and build flags. `frozenCommit` identifies the base
of the local remediation candidate; it is not a claim that the candidate is
clean or committed. Historical Lab commits describe earlier builds only.

Android ARM64 libraries use the pinned Unity NDK and 16 KiB linker page
alignment flags recorded in that manifest. Every candidate still requires
fresh APK extraction and ELF validation; a historical APK check cannot prove a
new candidate. Windows x86_64 libraries support development and Editor tests.

The libraries remain replaceable shared objects. The package imports only the
native ABI, bounded C172 runtime data, and provenance; it does not import R&D
scenes, runtime coordinators, debug overlays, or training systems.

The `c172x` XML is a separate model payload with configuration identity
`qfl-c172p-private-fidelity-v2`. Its header declares GPL and an upstream
non-sale statement. On 2026-09-18 the owner authorized free public alpha
publication and removed the project-imposed private-use hold, as recorded in
`LICENSE.md` and `SOURCE_MANIFEST.json`. That project decision neither changes
upstream licenses nor establishes additional third-party rights. Independent
release rights evidence remains tracked by FN-029/M14. See
`C172P_MODEL_CONFIGURATION.md` and `FLIGHT_DYNAMICS_MODEL_PROVENANCE.json`.

Native JSBSim is the sole product authority, but that architecture decision does
not establish Quest performance, certified fidelity, FAA approval, or training
credit. Physical Quest timing and handling evidence remains required for the
exact APK.
