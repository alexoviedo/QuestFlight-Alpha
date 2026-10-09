using System;
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
        private float _actualFlapDegrees;
        private float _leftBrakePressurePsi;
        private float _rightBrakePressurePsi;
        private float _leftFuelGallons;
        private float _rightFuelGallons;
        private float _oilTemperatureCelsius;

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
            _actualFlapDegrees = 0f;
            _leftBrakePressurePsi = 0f;
            _rightBrakePressurePsi = 0f;
            _leftFuelGallons = 0f;
            _rightFuelGallons = 0f;
            _oilTemperatureCelsius =
                input.flightState.engineRpm > 500.0
                    ? CockpitSystemsPhysicalConfigurationV2
                        .NormalOilTemperatureCelsius
                    : CockpitSystemsPhysicalConfigurationV2
                        .AmbientOilTemperatureCelsius;
            _hasAppliedCommandSequence = false;
            _lastAppliedCommandSequence = 0ul;
            _initialized = true;
            return Refresh(input, 0f);
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

            float deltaTimeSeconds = (float)(
                input.flightState.simulationTimeSeconds -
                _current.flightSimulationTimeSeconds);
            return Refresh(input, deltaTimeSeconds);
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
                command.kind > CockpitSystemsCommandKindV2.SetFlapSelector ||
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

                case CockpitSystemsCommandKindV2.SetFlapSelector:
                    if (!CockpitSystemsAuthorityV2Math.IsFinite(
                            command.normalizedValue) ||
                        command.normalizedValue < 0f ||
                        command.normalizedValue > 1f)
                    {
                        return false;
                    }
                    // Echo the already-arbitrated requested selector position.
                    // The next coordinator sample remains authoritative and
                    // actual flap travel never follows this echo directly.
                    _current.flapSelectorCommandNormalized =
                        command.normalizedValue;
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
            _actualFlapDegrees = 0f;
            _leftBrakePressurePsi = 0f;
            _rightBrakePressurePsi = 0f;
            _leftFuelGallons = 0f;
            _rightFuelGallons = 0f;
            _oilTemperatureCelsius = 0f;
        }

        private bool Refresh(
            in CockpitSystemsAuthorityInputV2 input,
            float deltaTimeSeconds)
        {
            FlightDynamicsState state = input.flightState;
            FlightDynamicsControlSnapshot controls = input.flightControls;
            float engineRpm = NonNegative((float)state.engineRpm);
            float flapCommand = CockpitSystemsAuthorityV2Math.Clamp01(
                controls.flaps);
            _actualFlapDegrees = state.HasNativeTelemetry
                ? (float)state.actualFlapNormalized * CockpitSystemsPhysicalConfigurationV2.MaximumFlapDegrees : 0f;
            UpdateActuators(controls, deltaTimeSeconds);
            // No active fuel gauge consumes these channels. Native fuel-flow/volume
            // conversion is not supplied; keep them unavailable rather than simulate it.
            float fuelFlowGallonsPerHour = 0f;
            float oilPressurePsi = OilPressure(engineRpm);
            UpdateOilTemperature(controls, engineRpm, deltaTimeSeconds);
            float barometricSettingInHg =
                CockpitSystemsPhysicalConfigurationV2
                    .StandardBarometricSettingInHg;
            float altitudeMslFeet =
                (float)(state.altitudeMslMeters *
                        FlightFrameConversions.MetersToFeet);
            float pressureAltitudeFeet = altitudeMslFeet -
                (barometricSettingInHg -
                 CockpitSystemsPhysicalConfigurationV2
                    .StandardBarometricSettingInHg) * 1000f;
            float magneticVariation =
                CockpitSystemsPhysicalConfigurationV2
                    .KbduMagneticVariationEastDegrees;
            float magneticHeading = NormalizeHeading(
                (float)state.headingDegrees - magneticVariation);
            CockpitSystemsAvailabilityV2 availability =
                CockpitSystemsAvailabilityV2.ScenarioClock |
                CockpitSystemsAvailabilityV2.FlightControlCommands |
                CockpitSystemsAvailabilityV2.EngineRpm |
                CockpitSystemsAvailabilityV2.ElectricalControlPositions |
                CockpitSystemsAvailabilityV2.IgnitionAndStarterControl |
                CockpitSystemsAvailabilityV2.PrimerControl |
                CockpitSystemsAvailabilityV2.FuelSelectorControl |
                CockpitSystemsAvailabilityV2.FlapSelectorCommand |
                CockpitSystemsAvailabilityV2.PressureAltitude |
                CockpitSystemsAvailabilityV2.BarometricSetting |
                CockpitSystemsAvailabilityV2.BrakeHydraulicPressure |
                CockpitSystemsAvailabilityV2.MagneticVariation |
                CockpitSystemsAvailabilityV2.MagneticHeading |
                CockpitSystemsAvailabilityV2.VacuumSuction |
                CockpitSystemsAvailabilityV2.OilPressure |
                CockpitSystemsAvailabilityV2.OilTemperature |
                CockpitSystemsAvailabilityV2.GpsDepartureStatus |
                CockpitSystemsAvailabilityV2.ComRadios |
                CockpitSystemsAvailabilityV2.Transponder;

            if (state.HasNativeTelemetry)
                availability |= CockpitSystemsAvailabilityV2.ActualFlapPosition;
            bool trackAvailable = TryGroundTrack(state, out float groundTrack);

            CockpitElectricalPowerSourceInputV2 sources =
                input.electricalPowerSources;
            bool mastersGuaranteePowerOff =
                !_batteryMasterOn && !_alternatorMasterOn;
            bool busStateAvailable =
                sources.hasAuthoritativeState || mastersGuaranteePowerOff;
            bool mainBusPowered = false;
            bool avionicsBusPowered = false;
            float electricalBusVolts = 0f;
            float alternatorAmps = 0f;
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
                                CockpitSystemsAvailabilityV2.StarterMotorPower |
                                CockpitSystemsAvailabilityV2
                                    .ElectricalIndications;

                if (mainBusPowered)
                {
                    bool alternatorSupplying =
                        _alternatorMasterOn &&
                        sources.hasAuthoritativeState &&
                        sources.alternatorCanPowerMainBus;
                    electricalBusVolts = alternatorSupplying
                        ? CockpitSystemsPhysicalConfigurationV2
                            .NominalAlternatorBusVolts
                        : CockpitSystemsPhysicalConfigurationV2
                            .NominalBatteryBusVolts;
                    if (alternatorSupplying)
                    {
                        alternatorAmps = avionicsBusPowered ? 18f : 8f;
                    }
                }
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
                engineRpm = engineRpm,
                flapSelectorCommandNormalized = flapCommand,
                pressureAltitudeFeet = pressureAltitudeFeet,
                barometricSettingInHg = barometricSettingInHg,
                magneticVariationEastDegrees = magneticVariation,
                magneticHeadingDegrees = magneticHeading,
                actualFlapDegrees = _actualFlapDegrees,
                leftBrakePressurePsi = _leftBrakePressurePsi,
                rightBrakePressurePsi = _rightBrakePressurePsi,
                vacuumInHg = 5f * Clamp(engineRpm / 1800f, 0f, 1f),
                leftFuelGallons = _leftFuelGallons,
                rightFuelGallons = _rightFuelGallons,
                fuelFlowGallonsPerHour = fuelFlowGallonsPerHour,
                oilPressurePsi = oilPressurePsi,
                oilTemperatureCelsius = _oilTemperatureCelsius,
                electricalBusVolts = electricalBusVolts,
                alternatorAmps = alternatorAmps,
                // Packed "KBDU", least-significant byte first.
                gpsDestinationIdentAscii = 0x5542444Bu,
                gpsBearingDegrees = BearingToKbdu(
                    state.latitudeDegrees, state.longitudeDegrees),
                gpsDistanceNauticalMiles = DistanceToKbduNauticalMiles(
                    state.latitudeDegrees, state.longitudeDegrees),
                gpsGroundTrackDegrees = groundTrack,
                gpsGroundTrackAvailable = trackAvailable,
                gpsAltitudeMslFeet = altitudeMslFeet,
                com1ActiveKHz =
                    CockpitSystemsPhysicalConfigurationV2.KbduCtafKHz,
                com1StandbyKHz =
                    CockpitSystemsPhysicalConfigurationV2.KbduAwosKHz,
                com2ActiveKHz =
                    CockpitSystemsPhysicalConfigurationV2
                        .EmergencyFrequencyKHz,
                com2StandbyKHz =
                    CockpitSystemsPhysicalConfigurationV2.KbduCtafKHz,
                transponderCodeBcd =
                    CockpitSystemsPhysicalConfigurationV2
                        .VisualFlightRulesTransponderCodeBcd,
                transponderMode = CockpitTransponderModeV2.Altitude,
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

        private void UpdateActuators(
            in FlightDynamicsControlSnapshot controls,
            float deltaTimeSeconds)
        {

            float leftTarget = CockpitSystemsAuthorityV2Math.Clamp01(
                controls.leftToeBrake) *
                CockpitSystemsPhysicalConfigurationV2.MaximumBrakePressurePsi;
            float rightTarget = CockpitSystemsAuthorityV2Math.Clamp01(
                controls.rightToeBrake) *
                CockpitSystemsPhysicalConfigurationV2.MaximumBrakePressurePsi;
            _leftBrakePressurePsi = HydraulicResponse(
                _leftBrakePressurePsi, leftTarget, deltaTimeSeconds);
            _rightBrakePressurePsi = HydraulicResponse(
                _rightBrakePressurePsi, rightTarget, deltaTimeSeconds);
        }

        private void UpdateOilTemperature(
            in FlightDynamicsControlSnapshot controls,
            float engineRpm,
            float deltaTimeSeconds)
        {
            float running = Clamp(engineRpm / 1800f, 0f, 1f);
            float load = CockpitSystemsAuthorityV2Math.Clamp01(
                controls.throttle);
            float target =
                CockpitSystemsPhysicalConfigurationV2
                    .AmbientOilTemperatureCelsius +
                (CockpitSystemsPhysicalConfigurationV2
                    .NormalOilTemperatureCelsius -
                 CockpitSystemsPhysicalConfigurationV2
                    .AmbientOilTemperatureCelsius) *
                running * (0.8f + 0.2f * load);
            float rate = target >= _oilTemperatureCelsius ? 0.05f : 0.025f;
            _oilTemperatureCelsius = MoveTowards(
                _oilTemperatureCelsius, target, rate * deltaTimeSeconds);
        }

        private static float OilPressure(float engineRpm)
        {
            if (engineRpm <= 300f) return 0f;
            return 25f + 50f * Clamp(
                (engineRpm - 500f) / 2000f, 0f, 1f);
        }

        private static float HydraulicResponse(
            float current,
            float target,
            float deltaTimeSeconds)
        {
            float responseSeconds = target >= current
                ? CockpitSystemsPhysicalConfigurationV2
                    .BrakePressureRiseSeconds
                : CockpitSystemsPhysicalConfigurationV2
                    .BrakePressureReleaseSeconds;
            float rate =
                CockpitSystemsPhysicalConfigurationV2.MaximumBrakePressurePsi /
                responseSeconds;
            return MoveTowards(current, target, rate * deltaTimeSeconds);
        }

        private static float MoveTowards(
            float current,
            float target,
            float maximumDelta)
        {
            float delta = target - current;
            if (delta > maximumDelta) return current + maximumDelta;
            if (delta < -maximumDelta) return current - maximumDelta;
            return target;
        }

        public static bool TryGroundTrack(in FlightDynamicsState state, out float degrees)
        {
            degrees = 0f;
            if (!state.HasNativeTelemetry || !state.IsFinite) return false;
            double north = state.groundVelocityNedMetersPerSecond.x;
            double east = state.groundVelocityNedMetersPerSecond.y;
            // True track, matching the existing true GPS bearing. Below 0.5m/s
            // direction is deliberately unavailable; heading is never substituted.
            if (north * north + east * east < 0.25) return false;
            degrees = NormalizeHeading((float)(Math.Atan2(east, north) * 180.0 / Math.PI));
            return true;
        }

        private static float NormalizeHeading(float value)
        {
            value %= 360f;
            return value < 0f ? value + 360f : value;
        }

        private static float DistanceToKbduNauticalMiles(
            double latitudeDegrees,
            double longitudeDegrees)
        {
            const double EarthRadiusNauticalMiles = 3440.065;
            double latitude1 = latitudeDegrees * Math.PI / 180.0;
            double latitude2 =
                CockpitSystemsPhysicalConfigurationV2.KbduLatitudeDegrees *
                Math.PI / 180.0;
            double latitudeDelta = latitude2 - latitude1;
            double longitudeDelta =
                (CockpitSystemsPhysicalConfigurationV2.KbduLongitudeDegrees -
                 longitudeDegrees) * Math.PI / 180.0;
            double sinLatitude = Math.Sin(latitudeDelta * 0.5);
            double sinLongitude = Math.Sin(longitudeDelta * 0.5);
            double a = sinLatitude * sinLatitude +
                Math.Cos(latitude1) * Math.Cos(latitude2) *
                sinLongitude * sinLongitude;
            double centralAngle = 2.0 * Math.Atan2(
                Math.Sqrt(a), Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            return (float)(EarthRadiusNauticalMiles * centralAngle);
        }

        private static float BearingToKbdu(
            double latitudeDegrees,
            double longitudeDegrees)
        {
            double latitude1 = latitudeDegrees * Math.PI / 180.0;
            double latitude2 =
                CockpitSystemsPhysicalConfigurationV2.KbduLatitudeDegrees *
                Math.PI / 180.0;
            double longitudeDelta =
                (CockpitSystemsPhysicalConfigurationV2.KbduLongitudeDegrees -
                 longitudeDegrees) * Math.PI / 180.0;
            double y = Math.Sin(longitudeDelta) * Math.Cos(latitude2);
            double x = Math.Cos(latitude1) * Math.Sin(latitude2) -
                Math.Sin(latitude1) * Math.Cos(latitude2) *
                Math.Cos(longitudeDelta);
            if (Math.Abs(x) < 0.000000001 &&
                Math.Abs(y) < 0.000000001)
            {
                return 0f;
            }
            return NormalizeHeading(
                (float)(Math.Atan2(y, x) * 180.0 / Math.PI));
        }

        private static float NonNegative(float value)
        {
            return value <= 0f ? 0f : value;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value <= minimum) return minimum;
            return value >= maximum ? maximum : value;
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
