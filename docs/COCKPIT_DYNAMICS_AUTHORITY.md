# Cockpit dynamics authority

The production cockpit is explicitly serialized in
`Assets/Production/Prefabs/ProductionC172AircraftRig.prefab`. The imported
KOG_THORNS C172 contains no animation clips. A product motion is therefore
allowed only when:

1. the source geometry is positively identified and independently movable;
2. the product has an authoritative or explicitly documented deterministic
   development value;
3. one serialized presenter owns the transform or rendered indication; and
4. the result passes focused tests and inspected Visual QA.

Runtime discovery or repair may not construct cockpit hierarchy.

## Completion V3 authority boundary

`ProductionCockpitSystemsController` is the single cockpit adapter from the
one `FlightDynamicsCoordinator`. Its allocation-free
`CockpitSystemsAuthorityV2Model` supplies:

- authoritative flight-control commands, attitude/air data, engine RPM, weight
  on wheels, and simulation time from the existing flight snapshot;
- deterministic battery, alternator, avionics, ignition/start, primer,
  fuel-selector, and flap-selector state;
- a ten-second flap actuator model;
- pressure altitude and an authored barometric setting;
- left/right brake pressure from authoritative brake commands;
- KBDU-local magnetic heading using a frozen 7.66 degrees east development
  variation;
- deterministic 50-gallon fuel state, burn, and flow;
- oil pressure/temperature, vacuum suction, bus voltage, and alternator
  current derived from engine/electrical state;
- UTC scenario time;
- bounded KBDU departure GPS content, COM1/COM2 defaults, and transponder code
  1200 in altitude mode.

These are coherent development simulation values, not certified C172 systems,
surveyed magnetic data, or training-grade engine/electrical/fuel fidelity.
DME, NAV deviation, audio routing, CHT/EGT, circuit-breaker faults, and
certified system behavior remain unavailable.

## Serialized moving controls

The product animates:

- pilot and copilot yokes;
- throttle and mixture;
- the combined ALT/BAT master;
- primer;
- ignition/start key;
- avionics master;
- wing-flap selector;
- fuel selector;
- exterior ailerons, elevators, rudder, flaps, and low-speed nose steering.

The imported ALT/BAT source is one combined object, so its halves cannot move
independently without remodeling. The source pedal mesh does not contain four
complete independent rudder/toe-brake pedal assemblies and remains static.

## Touch interaction

`CockpitTouchInteractionV2Controller` owns one bounded, allocation-free OpenXR
interaction loop. Either Touch controller may use direct/near grip or the
OpenXR aim-ray fallback. Trigger/grip hysteresis, held-select acquisition, and
safe release prevent duplicate or stuck commands.

Nine serialized bindings route through `CockpitTouchInputV2Bridge` and the
existing `ProductControlInput` boundary:

- pilot yoke;
- throttle;
- mixture;
- ALT/BAT master;
- primer;
- ignition/start;
- avionics master;
- flap selector;
- fuel selector.

No Touch code writes the Main Camera or a cockpit visual transform directly.

## Instruments and avionics

One opaque instrument batch, one material, and thirteen bindings present:

- airspeed and vertical speed;
- the directional-gyro card and curved magnetic-compass card;
- three altimeter pointers plus its barometric-setting pointer;
- flap position;
- brake pressure;
- tachometer;
- turn-coordinator airplane and slip/skid ball.

The frozen attitude gyro sphere is animated in the same material partition by
one shader-space quaternion. The magnetic compass rotates the complete audited
curved source card behind a fixed lubber mark using true heading minus the
frozen KBDU east-variation contract; its response is shortest-path damped.

The imported combined meter renderer uses a dedicated generated neutralized
mesh with a zeroed UV4 selector, preventing source geometry from accidentally
sampling the V2 atlas.

One opaque avionics glyph batch and one material present:

- KBDU departure GPS status;
- COM1 and COM2;
- transponder code 1200 / ALT.

An authored powered fallback preserves readable content before the first valid
snapshot. A valid unpowered snapshot blanks the displays.

## Fail-closed represented items

The following remain static after source-topology and authority audits:

- rudder/toe-brake pedals, trim, parking brake;
- clock;
- fuel, oil, electrical, vacuum, and CHT/EGT faces;
- NAV, DME, and audio-panel content;
- light switches, circuit breakers, alternate static, carburetor heat, cabin
  heat/air, and vents;
- doors, latches, operable windows, seats, and belts.

Most have merged or unidentifiable geometry; some also lack truthful system
authority. Earlier false attitude/clock/compass overlays discovered during
full-resolution QA were removed rather than shipped; the attitude sphere and
curved compass card were enabled only after their complete frozen topology was
identified. The detailed
item-by-item result is in
`tools/cockpit_dynamics/cockpit_representation_manifest_v2.json`.

## Quest presentation contract

- Instrument and avionics changes add two opaque batches total.
- There is no per-instrument GameObject, material, or `Update`.
- Presenters reuse serialized meshes, materials, and preallocated arrays.
- Existing accepted cabin glass, instrument-lens response, cockpit shading,
  cowl light enclosure, and seat calibration remain independent systems.
- Simulator evidence is stereo/composition evidence only. Standalone Quest
  timing, thermal, comfort, and worn interaction acceptance require hardware.
