# Traffic and reservoir watercraft regression investigation

## Confirmed cause and repair

Commit `1d4a040` introduced a mandatory road-authority checksum into
`KbduTrafficV2Runtime`. Its expected SHA-256 was computed from CRLF text:
`1a7e3769c6767c02c63f54f8bd59f47aa203bb89ead5252dea948910b4c6fb36`.
The repository's `.gitattributes` stores JSON with LF endings. The committed
authority has SHA-256
`91deadee15297924d0dafbb1ff32d541e01c803a7b282da5d9f14cb5f0ddb4d3`.
Converting that exact committed file to CRLF reproduces the expected old hash.

Opening the unmodified shipping scene reproduced `grade-separated road authority
hash mismatch`. `TryValidateContract` leaves `contractValid=false`, so both
`Update` and `DrawForCamera` stop; every moving vehicle disappears. This is a
content-validation failure, not a density or distance-setting issue.

The fix pins the committed LF serialization and normalizes CRLF before checking
that road document. Content changes still fail validation. The authoring Python
tool now explicitly writes LF. The traffic prefab also lacked this new required
binding (only the shipping scene had an override); the binding is now serialized
in the prefab. No runtime hierarchy construction or rendering-budget increase
was added.

The road authoring change also left environment budget metadata at 71 renderers /
629,487 triangles despite adding two road batches. The shipping scene now records
73 / 635,941; road authoring updates those values when rebuilt.

## Watercraft findings

The feature contains 25 static watercraft, distributed across open water, shore
storage, and dry storage. It did not implement moving boats. All nine footprint
samples for each of the 25 placements were refitted against the active rendered
terrain mesh. Every center-height delta is 0.000000 m, and all normals and plane
residuals pass the existing alignment tolerances. There is no evidence of removal
or terrain-induced burial.

The terrain repair `4983987` changed the mesh dependency hash outside those
footprints, leaving the verification report stale. Its placement-manifest hash
also used pre-Git CRLF bytes. Both verification hashes were refreshed only after
checking the actual surface. The boat geometry, hierarchy, materials, and LOD
settings were retained. The editor can now revalidate existing placements without
requiring the local-only source meshes used for original authoring.

## Evidence

Local evidence root (uncommitted):
`C:/Users/ovied/Dev/T2/T2-QuestFlight-current-evidence/traffic_watercraft_regression_20260906`.

- `before-tests.xml`: 12 passed, 6 failed; reproduces missing prefab binding,
  shipping-scene checksum failure, and stale boat report.
- `diagnose.log`: unmodified scene and prefab contract failures. Its general
  terrain-query samples are diagnostic only; rendered-mesh footprint refits in
  `repair2.log` are the authority for boat alignment.
- `repair2.log`: all 25 rendered footprint fits unchanged; environment budget
  validates after refreshing scene metadata.
- `after-tests.xml`: 20 passed, 0 failed, including LF/CRLF acceptance, rejection
  of changed road data, shipping-scene validation and one-second vehicle movement.
- Meta XR simulator audit: `BLOCKED_MISSING_RECORDED_REPLAYS`; no simulator stereo
  acceptance is claimed.

- Standard visual QA: 130 captures, machine pass, no report errors. Inspected the
  contact sheet and full-size runway view. The known editor camera/menu harness
  hides the cockpit in that view; it is not cockpit acceptance evidence.
- Watercraft QA: five captures, machine pass. Inspected the mooring-field view:
  six modeled sailboats are visible on the water.
- Full-map traffic QA: all 15 captures pass across five views at 0, 60 and 120
  seconds, with no dropped instances. Airport views prepare 65–90 near vehicles;
  highway views prepare 143–151. Inspected highway images at 0 and 60 seconds:
  vehicle positions change. These counts describe prepared instances, not a
  claim that every instance is visible in the camera frustum.
- The obsolete Gate-3 harness failed because its old trajectory IDs are not
  evaluable in the current full-map manifest. Its failure is retained separately;
  `traffic_full_map_qa` contains the applicable passing check.
- The standard PowerShell build wrapper excludes `QuestFlightAerialClipmap`.
  The installed pre-fix APK contained that imagery. Its small intermediate APK
  was not installed. The final build uses the same `ProductionBuild` validation
  entry point with the current aerial StreamingAssets retained, after the wrapper
  restored and verified its temporary asset transaction.

## Android and headset handoff

The final build succeeded at 2026-09-06 17:47:36 UTC with zero errors and 19
warnings. Product integrity passed, with Android ARM64 / IL2CPP / Vulkan /
OpenXR single-pass instancing / 4x MSAA and native JSBSim required.

The 1,513,993,599-byte APK contains both aerial and building clipmap payloads.
SHA-256: `e5f7a44b5f7ec53d34d2df6fbe7347886a9354fbf4c3a6c8719d93bdf5e10f63`.
It was installed successfully on Quest `2G0YC5ZG8907TD` at 11:48:45 local time,
and the installed `base.apk` checksum matches. The application was launched
as PID 10071.

At handoff the headset reported `mWakefulness=Asleep`; wearer readiness was
requested. Physical stereo inspection, live traffic confirmation and frame/
thermal acceptance remain pending. No Quest performance pass is claimed.
