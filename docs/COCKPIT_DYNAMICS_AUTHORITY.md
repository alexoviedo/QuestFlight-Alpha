# Cockpit dynamics authority

The production cockpit is derived from the explicitly authored C172 visual in
`Assets/Production/Prefabs/ProductionC172AircraftRig.prefab`. The source GLB
contains no animation clips, so every moving part in the product must have both:

1. a positively identified, serialized piece of source geometry; and
2. an authoritative value at the product flight-dynamics boundary.

Declaring a potential channel in code does not make that system present. When
either condition is missing, the object remains at its authored rest pose.

## Completion status

The current authored product prefab contains the consolidated SystemsV2
authority adapter and the Touch interaction path described below. This
document records implementation truth only. It does not claim that the current
working tree has passed product validation, EditMode, PlayMode, stereo Visual
QA, an Android build, or worn Quest acceptance; those results must be recorded
separately after they actually run.

## Implemented authority boundary

`ProductionCockpitSystemsController` is the single serialized adapter from the
one `FlightDynamicsCoordinator`. It steps the existing cockpit presentation
model and the subordinate allocation-free
`CockpitSystemsAuthorityV2Model`; no second flight authority exists.

The flight snapshot may present only:

- calibrated airspeed;
- MSL altitude;
- vertical speed;
- pitch, bank, heading, turn rate, and aerodynamic sideslip;
- engine RPM;
- weight on wheels;
- aileron, elevator, rudder, throttle, mixture, trim, flap-command, and toe-brake
  command positions.

It applies deterministic mechanical response to indicated values and performs
no steady-state managed allocation. It does not infer values from visuals or
invent aircraft systems.

SystemsV2 additionally owns these deterministic channels:

- scenario UTC derived from one authored scenario epoch plus simulation time;
- flight-control commands and engine RPM from the flight authority;
- battery-master, alternator-master, avionics-master, ignition/start, primer,
  fuel-selector, and flap-selector command positions;
- main-bus, avionics-bus, and starter-motor power only when external
  electrical-source state is authoritative, or when both electrical masters
  conclusively prove the buses off.

SystemsV2 intentionally contains no fuel quantity or flow, oil state, vacuum,
voltage/current measurement, barometric setting, actual flap position,
measured brake pressure, magnetic variation, GPS/navigation, COM, DME,
transponder, or audio-panel state. Missing channels are unavailable, not zero.

The V2 instrument adapter can truthfully expose pitch/bank, true heading, and
scenario UTC. That availability does not activate an instrument: the matching
geometry, calibration, and serialized presentation binding must also be
proven. The V2 avionics adapter exposes electrical power only. It does not
claim live avionics content.

## Positively represented moving geometry

The serialized product currently animates only:

- pilot and copilot control yokes;
- the represented throttle and mixture push/pull knobs;
- ailerons, elevators, flaps, and rudder;
- low-speed nose steering while weight is on the wheels;
- the principal airspeed and vertical-speed pointers.

The serialized Touch path is bounded to three positively represented controls:

- pilot yoke grab, mapped to authoritative aileron/elevator input;
- throttle grab, mapped to authoritative throttle input;
- mixture grab, mapped to authoritative mixture input.

`CockpitTouchInteractionV2Controller` owns the one bounded, allocation-free
OpenXR Touch ray/command loop. `CockpitTouchInputV2Bridge` arbitrates those
three commands into the existing `ProductControlInput` through the sole
`FlightDynamicsCoordinator`. It does not write visual transforms directly.
Unsupported controls have no Touch binding. Runtime never discovers or creates
cockpit hierarchy.

Existing cabin-glass reflection, instrument-lens response, stable cockpit
shading, and the engine-cowl light enclosure remain independent accepted
presentation systems.

## Explicitly static or unavailable

The following visible geometry is not animated in this milestone:

- the represented wing-flap selector (`Dynamic_Switch_007`), because its
  isolated geometry and animation-free source establish its identity but do
  not conclusively establish the real hinge, angular arc, or detent angles;
- ALT/BAT master, primer, ignition key, avionics-power control, and fuel
  selector remain visually static. SystemsV2 owns their deterministic control
  positions, but no reviewed serialized motion binding is active. Electrical
  bus and starter power remain conditional rather than inferred;
- light-switch banks, circuit breakers, and the alternate-static control remain
  static because complete component identity and corresponding system
  authority are absent;
- merged rudder/toe-brake pedals until their four assemblies can be separated
  with a deterministic, visually validated offline derivation;
- the fixed-pitch propeller and spinner, because direct phase integration can
  reverse-strobe at the 72 Hz Quest target and no alias-safe blur representation
  has passed stereo/hardware review;
- fixed-gear wheel roll, because the imported wheel nodes combine rotating
  tires/rims with fixed aerodynamic pants that may not rotate together;
- generic fuel, oil, electrical, vacuum, and engine-system indications because
  the active backend does not expose those quantities;
- generic or fantasy duplicate gauge faces, including `MACH/AS`, `GYRO/LOC`,
  and unlabeled rectangular indicators;
- radio, DME, transponder, audio-panel, and GPS indications/controls remain
  static. The provider exposes electrical power only; it has no authoritative
  navigation, frequency, DME, transponder, or audio state. The existing
  readable static display is preserved instead of being covered by invented
  live text;
- the altimeter remains static because pressure altitude, barometric setting,
  and a complete separable needle/drum set are not all available. MSL altitude
  is not relabeled as pressure altitude;
- the attitude indicator, directional gyro, and clock remain static even
  though pitch/bank, true heading, and scenario UTC are available: their exact
  separable topology and/or absolute registration are not proven;
- the magnetic compass, flap-position indicator, and brake-pressure gauge
  remain static because magnetic variation, actual flap position, and measured
  brake pressure are unavailable;
- doors, side windows, seat latches, belts, vents, trim wheel, parking brake,
  cabin heat/air, carburetor heat, propeller-pitch control, and autopilot
  controls because separate, positively identified operable geometry is absent.

The C172 remains fixed gear. No retractable-gear behavior is permitted.

## Quest 3 presentation contract

- All references and pivots are authored into the serialized prefab in the
  Editor; runtime code never discovers or constructs product hierarchy.
- No component writes the Main Camera, XR Origin, seat calibration, aircraft
  root, or flight authority.
- Existing renderers are transformed in place wherever possible.
- Analog instruments use a single batched renderer and do not add per-gauge
  GameObjects, materials, textures, transparent passes, or `Update` methods.
- Control and mechanical animation adds no renderer, draw call, material, or
  texture.
- The complete feature must allocate zero managed memory in steady state.

This is a visual and systems-presentation contract. It does not establish
certified instrument accuracy, training credit, FAA approval, or Quest hardware
performance.
