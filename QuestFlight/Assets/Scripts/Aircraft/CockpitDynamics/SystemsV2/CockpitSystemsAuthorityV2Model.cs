using QuestFlightLab.Flight.Backends;

namespace QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2
{
    /// <summary>
    /// Deterministic subordinate aircraft/cockpit-systems reducer. It owns only
    /// represented cockpit control positions and state explicitly supplied by
    /// the flight/electrical authority boundaries. It never steps flight
    /// physics, creates hierarchy, reads input devices, or writes a camera.
    /// </summary>
    public sealed class CockpitSystemsAuthorityV2Model
    {
        public CockpitSystemsSnapshotV2 Current => _current;
        public bool IsInitialized => _initialized;

        private CockpitSystemsScenarioV2 _scenario;
        private CockpitSystemsSnapshotV2 _current;
        private bool _initialized;
        private bool _hasAppliedCommandSequence;
        private ulong _lastAppliedCommandSequence;

        private bool _batteryMasterOn;
        private bool _alternatorMasterOn;
        private bool _avionicsMasterOn;
        private CockpitIgnitionSwitchPositionV2 _ignitionSwitchPosition;
        private float _primerPositionNormalized;
        private CockpitFuelSelectorPositionV2 _fuelSelectorPosition;

        public bool Reset(
            in CockpitSystemsScenarioV2 scenario,
            in CockpitSystemsAuthorityInputV2 input)
        {
            Invalidate();
            if (!scenario.IsValid || !CanConsume(input)) return false;

            _scenario = scenario;
            _batteryMasterOn = scenario.batteryMasterOn;
            _alternatorMasterOn = scenario.alternatorMasterOn;
            _avionicsMasterOn = scenario.avionicsMasterOn;
            _ignitionSwitchPosition = scenario.ignitionSwitchPosition;
            _primerPositionNormalized = scenario.primerPositionNormalized;
            _fuelSelectorPosition = scenario.fuelSelectorPosition;
            _hasAppliedCommandSequence = false;
            _lastAppliedCommandSequence = 0ul;
            _initialized = true;
            return Refresh(input);
        }

        public bool Step(in CockpitSystemsAuthorityInputV2 input)
        {
            if (!_initialized || !CanConsume(input) ||
                input.flightState.simulationTimeSeconds + 0.000000001 <
                _current.flightSimulationTimeSeconds)
            {
                Invalidate();
                return false;
            }

            return Refresh(input);
        }

        /// <summary>
        /// Applies a single already-debounced command. Flight-control commands,
        /// including flap-selector command, remain owned by ProductControlInput
        /// and arrive through FlightDynamicsControlSnapshot instead.
        /// </summary>
        public bool ApplyCommand(in CockpitSystemsCommandV2 command)
        {
            if (!_initialized ||
                command.kind <= CockpitSystemsCommandKindV2.None ||
                command.kind > CockpitSystemsCommandKindV2.SetFuelSelector ||
                (_hasAppliedCommandSequence &&
                 command.sequence <= _lastAppliedCommandSequence))
            {
                return false;
            }

            switch (command.kind)
            {
                case CockpitSystemsCommandKindV2.SetBatteryMaster:
                    if (!TryBoolean(command.discreteValue, out _batteryMasterOn))
                        return false;
                    break;

                case CockpitSystemsCommandKindV2.SetAlternatorMaster:
                    if (!TryBoolean(command.discreteValue, out _alternatorMasterOn))
                        return false;
                    break;

                case CockpitSystemsCommandKindV2.SetAvionicsMaster:
                    if (!TryBoolean(command.discreteValue, out _avionicsMasterOn))
                        return false;
                    break;

                case CockpitSystemsCommandKindV2.SetIgnitionSwitch:
                    CockpitIgnitionSwitchPositionV2 ignition =
                        (CockpitIgnitionSwitchPositionV2)command.discreteValue;
                    if (!CockpitSystemsAuthorityV2Math.IsIgnitionPosition(ignition))
                        return false;
                    _ignitionSwitchPosition = ignition;
                    break;

                case CockpitSystemsCommandKindV2.SetPrimerPosition:
                    if (!CockpitSystemsAuthorityV2Math.IsFinite(
                            command.normalizedValue) ||
                        command.normalizedValue < 0f ||
                        command.normalizedValue > 1f)
                    {
                        return false;
                    }
                    _primerPositionNormalized = command.normalizedValue;
                    break;

                case CockpitSystemsCommandKindV2.SetFuelSelector:
                    CockpitFuelSelectorPositionV2 selector =
                        (CockpitFuelSelectorPositionV2)command.discreteValue;
                    if (!CockpitSystemsAuthorityV2Math.IsFuelSelectorPosition(
                            selector))
                    {
                        return false;
                    }
                    _fuelSelectorPosition = selector;
                    break;

                default:
                    return false;
            }

            _lastAppliedCommandSequence = command.sequence;
            _hasAppliedCommandSequence = true;
            return true;
        }

        public void Invalidate()
        {
            _scenario = default;
            _current = default;
            _initialized = false;
            _hasAppliedCommandSequence = false;
            _lastAppliedCommandSequence = 0ul;
            _batteryMasterOn = false;
            _alternatorMasterOn = false;
            _avionicsMasterOn = false;
            _ignitionSwitchPosition = CockpitIgnitionSwitchPositionV2.Off;
            _primerPositionNormalized = 0f;
            _fuelSelectorPosition = CockpitFuelSelectorPositionV2.Off;
        }

        private bool Refresh(in CockpitSystemsAuthorityInputV2 input)
        {
            FlightDynamicsState state = input.flightState;
            FlightDynamicsControlSnapshot controls = input.flightControls;
            CockpitSystemsAvailabilityV2 availability =
                CockpitSystemsAvailabilityV2.ScenarioClock |
                CockpitSystemsAvailabilityV2.FlightControlCommands |
                CockpitSystemsAvailabilityV2.EngineRpm |
                CockpitSystemsAvailabilityV2.ElectricalControlPositions |
                CockpitSystemsAvailabilityV2.IgnitionAndStarterControl |
                CockpitSystemsAvailabilityV2.PrimerControl |
                CockpitSystemsAvailabilityV2.FuelSelectorControl |
                CockpitSystemsAvailabilityV2.FlapSelectorCommand;

            CockpitElectricalPowerSourceInputV2 sources =
                input.electricalPowerSources;
            bool mastersGuaranteePowerOff =
                !_batteryMasterOn && !_alternatorMasterOn;
            bool busStateAvailable =
                sources.hasAuthoritativeState || mastersGuaranteePowerOff;
            bool mainBusPowered = false;
            bool avionicsBusPowered = false;
            if (busStateAvailable)
            {
                if (sources.hasAuthoritativeState)
                {
                    mainBusPowered =
                        (_batteryMasterOn &&
                         sources.batteryCanPowerMainBus) ||
                        (_alternatorMasterOn &&
                         sources.alternatorCanPowerMainBus);
                }

                avionicsBusPowered = mainBusPowered && _avionicsMasterOn;
                availability |= CockpitSystemsAvailabilityV2.ElectricalBusPower |
                                CockpitSystemsAvailabilityV2.StarterMotorPower;
            }

            bool starterSwitchEngaged =
                _ignitionSwitchPosition ==
                CockpitIgnitionSwitchPositionV2.Start;
            _current = new CockpitSystemsSnapshotV2
            {
                isValid = true,
                availability = availability,
                flightSimulationTimeSeconds = state.simulationTimeSeconds,
                scenarioUtcSecondsSinceUnixEpoch =
                    _scenario.scenarioUtcUnixSeconds +
                    state.simulationTimeSeconds,
                lastAppliedCommandSequence = _hasAppliedCommandSequence
                    ? _lastAppliedCommandSequence
                    : 0ul,
                engineRpm = (float)state.engineRpm,
                flapSelectorCommandNormalized =
                    CockpitSystemsAuthorityV2Math.Clamp01(controls.flaps),
                batteryMasterOn = _batteryMasterOn,
                alternatorMasterOn = _alternatorMasterOn,
                avionicsMasterOn = _avionicsMasterOn,
                mainBusPowered = mainBusPowered,
                avionicsBusPowered = avionicsBusPowered,
                ignitionSwitchPosition = _ignitionSwitchPosition,
                starterSwitchEngaged = starterSwitchEngaged,
                starterMotorPowered =
                    busStateAvailable &&
                    mainBusPowered &&
                    starterSwitchEngaged,
                primerPositionNormalized = _primerPositionNormalized,
                fuelSelectorPosition = _fuelSelectorPosition
            };
            return true;
        }

        private static bool CanConsume(in CockpitSystemsAuthorityInputV2 input)
        {
            if (!input.hasAuthoritativeFlightState ||
                !input.flightState.IsFinite ||
                !CockpitSystemsAuthorityV2Math.IsFinite(
                    input.flightState.simulationTimeSeconds) ||
                input.flightState.simulationTimeSeconds < 0.0)
            {
                return false;
            }

            FlightDynamicsControlSnapshot controls = input.flightControls;
            return CockpitSystemsAuthorityV2Math.IsFinite(controls.aileron) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(controls.elevator) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(controls.rudder) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(controls.throttle) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(controls.mixture) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(controls.carbHeat) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(controls.trim) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(controls.flaps) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(
                       controls.leftToeBrake) &&
                   CockpitSystemsAuthorityV2Math.IsFinite(
                       controls.rightToeBrake);
        }

        private static bool TryBoolean(int value, out bool result)
        {
            if (value == 0)
            {
                result = false;
                return true;
            }
            if (value == 1)
            {
                result = true;
                return true;
            }

            result = false;
            return false;
        }
    }
}
