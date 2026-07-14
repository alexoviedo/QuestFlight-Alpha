# Native JSBSim promotion gate

The frozen R&D source at
`cd4131fa7732a6db2d7387921dfef33e2c206970` passed the unchanged native
promotion thresholds after correcting the go-around command schedule from an
8-degree to a bounded 10-degree climb target. The acceptance threshold was not
weakened.

## Gate result

- 11/11 scenarios passed; 10/10 critical scenarios passed.
- Go-around second-half altitude recovery was 78.969 ft against the unchanged
  75 ft minimum.
- No non-finite state was observed.
- Steady-state managed allocations were zero.
- Windows timing passed (well below the 0.5 ms average / 1.0 ms p95 limits).
- Android ARM64 built with NDK 27.2.12479018.
- Fresh APK extraction verified 16 KiB ELF LOAD alignment for JSBSim, the C ABI
  wrapper, and the packaged C++ shared runtime.
- Source, license, data, and binary hashes are complete in the embedded package
  `SOURCE_MANIFEST.json`.

The promoted boundary is the embedded package
`QuestFlight/Packages/com.alexoviedo.questflight.jsbsim`. It contains only the
four required native libraries, managed C ABI owner, bounded C172 runtime data,
build-time StreamingAssets injection, and source/license records. No R&D scene,
coordinator, debug UI, evidence overlay, or legacy runtime system was imported.

## Product policy

The authored prefab continues to serialize `UnityPrototype` as its requested
backend. The sole product coordinator may prefer native JSBSim only in the
Unity Editor or a `DEVELOPMENT_BUILD`. A release build resolves to the Unity
prototype regardless of serialized or runtime preference. Native initialization,
data, ABI, non-finite-state, or step failures fall back to the Unity prototype
under the existing authority lease.

This software gate does not establish Quest performance or handling quality.
Native JSBSim cannot become the release default until physical Quest 3 timing
and handling evidence is collected. No certified-fidelity or training claim is
made.

The frozen upstream C172 definition also contains an active 10 Hz
`JSBout172B.csv` diagnostic output. The promotion run produced only an ignored,
header-only file, and release builds cannot select this backend, but native
release authority remains blocked until that output is disabled or redirected
to an explicit external-evidence location and the full 11/11, allocation,
timing, Android, alignment, and manifest gates are rerun on the changed payload.
