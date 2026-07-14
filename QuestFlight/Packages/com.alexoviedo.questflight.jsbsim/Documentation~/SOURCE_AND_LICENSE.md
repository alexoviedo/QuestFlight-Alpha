# Source, build, and license record

The shared JSBSim library is upstream JSBSim 1.3.1 at revision
`3b25f25e49b42d0489c04ac805674fc1450ca579`, from
<https://github.com/JSBSim-Team/jsbsim.git>, licensed GNU
LGPL-2.1-or-later. The complete license text travels with the runtime data at
`RuntimeData/JSBSim-1.3.1/COPYING.txt`.

The small project-owned C ABI wrapper source is frozen in the private
`alexoviedo/T2-QuestFlightLab` repository at commit
`cd4131fa7732a6db2d7387921dfef33e2c206970`. The exact paths and source hashes
are recorded in `SOURCE_MANIFEST.json`.

The wrapper record includes both the CRLF Windows build-input SHA256 and the
canonical LF SHA256/Git blob ID. This makes the provenance independent of a
checkout's line-ending policy without obscuring the exact compiled bytes.

The Android ARM64 libraries were rebuilt with Unity's NDK 27.2.12479018 and
both `-Wl,-z,max-page-size=16384` and
`-Wl,-z,common-page-size=16384`. Fresh APK extraction verified that every ELF
LOAD segment in both libraries is aligned to 16 KiB. Windows x86_64 libraries
remain development/editor support only.

The libraries remain replaceable shared objects. The package imports only the
native ABI, bounded C172 runtime data, and provenance; it does not import R&D
scenes, runtime coordinators, debug overlays, or training systems.

Passing the software promotion gate does not establish Quest performance,
certified fidelity, FAA approval, or training credit. Physical Quest timing and
handling evidence is still required before native JSBSim can be considered for
a release default.
