# C172 Cockpit Audio V1 — 2026-08-12

## Scope and authority

This milestone adds an explicitly authored, serialized soundscape to the
existing `ProductionC172AircraftRig`. It does not alter the XR hierarchy,
camera, flight dynamics, controls, scenery, clouds, wildlife, or lesson route.
Audio reads the single `FlightDynamicsCoordinator` and cockpit systems
snapshots, so the mapping is backend-neutral and does not create another flight
authority.

## Source and processing

- Real recording: JLWSound, `1964 Cessna 172E - Interior - Start, Idle,
  Takeoff, Flight`, Freesound sound 322321, CC0 1.0.
- Raw preview SHA-256:
  `c7494eba6878dbde3092e1180edff96d263154c091bfb043c8f490dcb3571b3e`.
- Raw material remains outside Git.
- Engine derivatives use recorded start, idle, high-prop, and cruise regions.
- Wind, ground roll, flap motor, stall horn, switch, and touchdown cues are
  deterministic synthesis with pinned seed 17208.
- The compiler resamples to 48 kHz, applies bounded filtering/crossfades, and
  emits 16-bit source WAVs. Unity imports continuous loops as compressed-in-
  memory Vorbis and short events as decompress-on-load Vorbis.

The exact source URL, segment timings, settings, and output hashes live in
`QuestFlight/Assets/Production/Audio/C172/Clips/SOURCE_MANIFEST.json`.

## Runtime architecture and Quest budget

- One `ProductionC172AudioController` in the authored aircraft prefab.
- Eight continuous sources: idle, cruise, propeller, low/high wind, ground
  roll, flap motor, and stall horn.
- Two shared one-shot sources: cockpit events and airframe events.
- Four mixer buses: Ownship, Cockpit, Warnings, and ExternalWorld.
- Ownship sources are listener-relative and have Doppler disabled. The dominant
  cockpit bed therefore avoids unnecessary per-source HRTF cost and incorrect
  Doppler between an aircraft and its own listener.
- No runtime hierarchy construction, per-frame object discovery, LINQ, string
  formatting, clip loading, or allocations.

RPM drives equal-power-style engine crossfades and restrained pitch changes.
Calibrated airspeed drives two wind bands. Ground speed is gated by weight on
wheels. Flap movement, master/ignition/fuel selector changes, starter state,
touchdown, and the stall envelope drive bounded cues.

## Validation

- Unity authoring/import: PASS.
- Focused EditMode mapping and serialized-prefab contract: 3/3 PASS.
- Android development/native comparison build: PASS. APK size and identity are
  recorded in the external build artifact; SHA-256
  `bb9895f9fb4d5b6f3fd8251b572fdd6ac43362a0f610b4b0e710593ecbda239c`.
- Exact APK installation and unattended launch on the connected Quest 3: PASS.
- Worn loudness, loop quality, comfort, and physical Quest timing: pending exact
  APK headset review.

This is development audio, not a certified acoustic model or a training-credit
claim.
