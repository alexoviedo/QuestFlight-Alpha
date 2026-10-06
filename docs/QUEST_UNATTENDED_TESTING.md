# Unattended Quest measurements

Keep one awake session across the complete comparison batch. Re-enabling the
physical proximity sensor between launches can put an unworn headset back to
sleep and require room relocalization. Keep the headset stationary and upright,
with uncovered cameras facing a lit, textured room. No script can manufacture
valid six-degree-of-freedom tracking if the headset loses localization.

`run_quest_temporal_visual_gate.ps1 -NoHumanWitness -KeepHeadsetAwake` and
`check_map_grove_device_visibility.ps1 -KeepHeadsetAwake` retain the awake override
after a run. Without that switch they restore the recorded virtual proximity
state, including an existing MQDH awake override. They do not change Guardian,
seat calibration, head pose, rendering quality or performance clocks.

For a batch, restore the original proximity state once in its outer `finally`,
not after every individual run. Use `quest_proximity_state.ps1` to parse and
select restoration; unknown firmware output fails closed. Restore any separately
changed Android stay-awake setting as well. Do not run Unity Android builds
during measurements: Unity can terminate the other SDK's ADB server.

Commands verified against the installed Meta Quest Developer Hub implementation:

- `am broadcast --user 0 -a com.oculus.vrpowermanager.prox_close`: keep awake.
- `am broadcast --user 0 -a com.oculus.vrpowermanager.automation_disable`: return
  control to the physical proximity sensor. The name is counterintuitive;
  `automation_enable` is not MQDH's restoration command.
- Read back `dumpsys oculus.internal.power.IVrPowerManager/default`, especially
  `Virtual proximity state`, and `dumpsys power` rather than assuming a successful
  broadcast means the device state changed.

Meta documents the development proximity override and its expiration behavior
in [Manage your Headset with MQDH](https://developers.meta.com/vr/documentation/unreal/ts-mqdh-basic-usage/).
The broadcast implementation is firmware-dependent; retain readback checks.

Reject timing windows interrupted by focus loss, unavailable tracking, a paused
flight, or missing SpaceWarp submissions when measuring ON. Do not count shell
or Guardian frame rates as product performance. Avoid screenshots/readbacks in
timed windows. A stationary-head run cannot accept head-turn reconstruction or
replace final worn visual/comfort validation.
