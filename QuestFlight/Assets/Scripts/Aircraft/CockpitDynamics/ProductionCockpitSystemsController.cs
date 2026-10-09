using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Serialized product adapter between the single flight authority and the
    /// allocation-free cockpit systems model. This component never creates
    /// cockpit hierarchy or writes the XR camera.
    /// </summary>
    [DefaultExecutionOrder(CockpitSystemsExecutionOrder.Systems)]
    [DisallowMultipleComponent]
    public sealed class ProductionCockpitSystemsController :
        MonoBehaviour,
        ICockpitPresentationStateProvider,
        ICockpitSystemsStateProviderV2,
        ICockpitInstrumentV2AuthorityProvider,
        ICockpitAvionicsV2AuthorityProvider
    {
        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField] private CockpitElectricalPowerInput electricalPowerInput;
        [SerializeField] private CockpitSystemsScenarioV2 systemsScenarioV2 =
            new CockpitSystemsScenarioV2
            {
                hasAuthoritativeConfiguration = true,
                // Explicit fixed scenario datum; never sampled from wall time.
                scenarioUtcUnixSeconds = 1785434400L,
                batteryMasterOn = true,
                alternatorMasterOn = true,
                avionicsMasterOn = true,
                ignitionSwitchPosition =
                    CockpitIgnitionSwitchPositionV2.Both,
                primerPositionNormalized = 0f,
                fuelSelectorPosition =
                    CockpitFuelSelectorPositionV2.Both
            };

        public CockpitSystemsSnapshot CurrentSnapshot => _model.Current;
        public bool HasValidSnapshot => _model.Current.isValid;
        public CockpitSystemsSnapshotV2 CurrentSystemsV2Snapshot =>
            _systemsV2Model.Current;
        public bool HasValidSystemsV2Snapshot =>
            _systemsV2Model.Current.isValid;

        private readonly CockpitSystemsModel _model = new CockpitSystemsModel();
        private readonly CockpitSystemsAuthorityV2Model _systemsV2Model =
            new CockpitSystemsAuthorityV2Model();

        public void ConfigureAuthoredSource(
            FlightDynamicsCoordinator authoredCoordinator,
            CockpitElectricalPowerInput authoredElectricalPowerInput)
        {
            coordinator = authoredCoordinator;
            electricalPowerInput = authoredElectricalPowerInput;
            _model.Invalidate();
            _systemsV2Model.Invalidate();
        }

        public void ConfigureAuthoredSource(
            FlightDynamicsCoordinator authoredCoordinator,
            CockpitElectricalPowerInput authoredElectricalPowerInput,
            in CockpitSystemsScenarioV2 authoredSystemsScenarioV2)
        {
            coordinator = authoredCoordinator;
            electricalPowerInput = authoredElectricalPowerInput;
            systemsScenarioV2 = authoredSystemsScenarioV2;
            _model.Invalidate();
            _systemsV2Model.Invalidate();
        }

        public void SetElectricalPowerInput(
            CockpitElectricalPowerInput authoritativeElectricalPowerInput)
        {
            electricalPowerInput = authoritativeElectricalPowerInput;
        }

        public bool HasAuthoredReferences(out string error)
        {
            if (coordinator == null)
            {
                error =
                    "ProductionCockpitSystemsController requires an authored FlightDynamicsCoordinator reference.";
                return false;
            }
            if (!systemsScenarioV2.IsValid)
            {
                error =
                    "ProductionCockpitSystemsController requires one deterministic authored V2 scenario.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            if (HasAuthoredReferences(out string error)) return;
            Debug.LogError("[QuestFlight][CockpitSystems] " + error, this);
            enabled = false;
        }

        private void OnDisable()
        {
            _model.Invalidate();
            _systemsV2Model.Invalidate();
        }

        private void LateUpdate()
        {
            if (coordinator == null || !coordinator.IsInitialized)
            {
                _model.Invalidate();
                _systemsV2Model.Invalidate();
                return;
            }

            FlightDynamicsState state = coordinator.CurrentState;
            FlightDynamicsControlSnapshot controls = coordinator.LastAppliedControls;
            StepModels(
                state,
                controls,
                electricalPowerInput,
                Time.deltaTime);
        }

        public bool StepForTest(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            CockpitElectricalPowerInput power,
            float deltaTimeSeconds)
        {
            return StepModels(
                state,
                controls,
                power,
                deltaTimeSeconds);
        }

        public bool StepForTest(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            CockpitElectricalPowerInput power,
            in CockpitSystemsScenarioV2 scenario,
            float deltaTimeSeconds)
        {
            systemsScenarioV2 = scenario;
            _systemsV2Model.Invalidate();
            return StepModels(
                state,
                controls,
                power,
                deltaTimeSeconds);
        }

        public bool TryApplyCommand(in CockpitSystemsCommandV2 command)
        {
            return _systemsV2Model.ApplyCommand(command);
        }

        public bool TryGetCockpitSystemsSnapshotV2(
            out CockpitSystemsSnapshotV2 snapshot)
        {
            snapshot = _systemsV2Model.Current;
            return snapshot.isValid;
        }

        public bool TryGetInstrumentV2Snapshot(
            out CockpitInstrumentV2Snapshot snapshot)
        {
            snapshot = default;
            CockpitSystemsSnapshot legacy = _model.Current;
            if (!legacy.isValid)
                return false;

            CockpitInstrumentV2Availability availability =
                CockpitInstrumentV2Availability.None;
            if (legacy.Has(CockpitDataAvailability.AirData))
            {
                availability |=
                    CockpitInstrumentV2Availability.AirData |
                    CockpitInstrumentV2Availability.BarometricSetting;
                snapshot.indicatedAirspeedKnots =
                    legacy.indicatedAirspeedKnots;
                snapshot.indicatedVerticalSpeedFeetPerMinute =
                    legacy.indicatedVerticalSpeedFeetPerMinute;
                // The legacy product flight snapshot always carries indicated
                // MSL altitude. Use standard pressure as a fail-safe until the
                // richer V2 barometric snapshot is available later in this
                // method, where it deterministically overrides both fields.
                snapshot.pressureAltitudeFeet =
                    legacy.indicatedAltitudeMslFeet;
                snapshot.barometricSettingInHg = 29.92f;
            }
            if (legacy.Has(CockpitDataAvailability.Attitude))
            {
                availability |= CockpitInstrumentV2Availability.Attitude;
                snapshot.pitchDegrees = legacy.indicatedPitchDegrees;
                snapshot.bankDegrees = legacy.indicatedBankDegrees;
            }
            if (legacy.Has(CockpitDataAvailability.Heading))
            {
                availability |= CockpitInstrumentV2Availability.TrueHeading;
                snapshot.trueHeadingDegrees =
                    legacy.indicatedHeadingDegrees;
            }
            if (legacy.Has(CockpitDataAvailability.BodyTurnRate) &&
                legacy.Has(CockpitDataAvailability.AerodynamicSideslip))
            {
                availability |=
                    CockpitInstrumentV2Availability.TurnAndSlip;
                snapshot.turnRateDegreesPerSecond =
                    legacy.indicatedBodyTurnRateDegreesPerSecond;
                // Presentation-grade slip-ball contract: the authored travel
                // reaches its stop at +/-8 degrees of authoritative sideslip.
                // This is not a lateral-accelerometer certification claim.
                snapshot.slipSkidNormalized = Mathf.Clamp(
                    legacy.aerodynamicSideslipDegrees / 8f, -1f, 1f);
            }

            CockpitSystemsSnapshotV2 systems = _systemsV2Model.Current;
            if (systems.isValid &&
                systems.Has(CockpitSystemsAvailabilityV2.ScenarioClock))
            {
                availability |= CockpitInstrumentV2Availability.ClockUtc;
                snapshot.clockUtcSeconds =
                    systems.scenarioUtcSecondsSinceUnixEpoch;
            }
            if (systems.isValid &&
                systems.Has(
                    CockpitSystemsAvailabilityV2.PressureAltitude |
                    CockpitSystemsAvailabilityV2.BarometricSetting))
            {
                availability |=
                    CockpitInstrumentV2Availability.AirData |
                    CockpitInstrumentV2Availability.BarometricSetting;
                snapshot.pressureAltitudeFeet =
                    systems.pressureAltitudeFeet;
                snapshot.barometricSettingInHg =
                    systems.barometricSettingInHg;
            }
            if (systems.isValid &&
                systems.Has(CockpitSystemsAvailabilityV2.MagneticVariation))
            {
                availability |=
                    CockpitInstrumentV2Availability.MagneticVariation;
                snapshot.magneticVariationEastDegrees =
                    systems.magneticVariationEastDegrees;
            }
            if (systems.isValid &&
                systems.Has(CockpitSystemsAvailabilityV2.ActualFlapPosition))
            {
                availability |=
                    CockpitInstrumentV2Availability.ActualFlapPosition;
                snapshot.actualFlapDegrees = systems.actualFlapDegrees;
            }
            if (systems.isValid &&
                systems.Has(
                    CockpitSystemsAvailabilityV2.BrakeHydraulicPressure))
            {
                availability |=
                    CockpitInstrumentV2Availability.BrakeHydraulics;
                snapshot.leftBrakePressurePsi =
                    systems.leftBrakePressurePsi;
                snapshot.rightBrakePressurePsi =
                    systems.rightBrakePressurePsi;
            }
            if (systems.isValid &&
                systems.Has(CockpitSystemsAvailabilityV2.EngineRpm))
            {
                availability |= CockpitInstrumentV2Availability.EngineRpm;
                snapshot.engineRpm = systems.engineRpm;
            }
            if (systems.isValid &&
                systems.Has(
                    CockpitSystemsAvailabilityV2.FuelQuantity |
                    CockpitSystemsAvailabilityV2.FuelFlow))
            {
                availability |= CockpitInstrumentV2Availability.FuelSystem;
                snapshot.leftFuelGallons = systems.leftFuelGallons;
                snapshot.rightFuelGallons = systems.rightFuelGallons;
                snapshot.fuelFlowGallonsPerHour =
                    systems.fuelFlowGallonsPerHour;
            }
            if (systems.isValid &&
                systems.Has(
                    CockpitSystemsAvailabilityV2.OilPressure |
                    CockpitSystemsAvailabilityV2.OilTemperature))
            {
                availability |= CockpitInstrumentV2Availability.OilSystem;
                snapshot.oilPressurePsi = systems.oilPressurePsi;
                snapshot.oilTemperatureFahrenheit =
                    systems.oilTemperatureCelsius * 1.8f + 32f;
            }
            if (systems.isValid &&
                systems.Has(
                    CockpitSystemsAvailabilityV2.ElectricalIndications))
            {
                availability |=
                    CockpitInstrumentV2Availability.ElectricalIndications;
                snapshot.electricalBusVolts = systems.electricalBusVolts;
                snapshot.alternatorAmps = systems.alternatorAmps;
            }
            if (systems.isValid &&
                systems.Has(CockpitSystemsAvailabilityV2.VacuumSuction))
            {
                availability |=
                    CockpitInstrumentV2Availability.VacuumSuction;
                snapshot.vacuumInHg = systems.vacuumInHg;
            }

            snapshot.availability = availability;
            snapshot.isValid =
                availability != CockpitInstrumentV2Availability.None;
            return snapshot.isValid;
        }

        public bool TryGetAvionicsV2Snapshot(
            out CockpitAvionicsV2Snapshot snapshot)
        {
            snapshot = default;
            CockpitSystemsSnapshotV2 systems = _systemsV2Model.Current;
            if (!systems.isValid ||
                !systems.Has(
                    CockpitSystemsAvailabilityV2.ElectricalBusPower))
            {
                return false;
            }

            snapshot.isValid = true;
            snapshot.availability =
                CockpitAvionicsV2Availability.ElectricalPower;
            snapshot.avionicsBusPowered = systems.avionicsBusPowered;
            if (systems.Has(
                    CockpitSystemsAvailabilityV2.GpsDepartureStatus))
            {
                snapshot.availability |=
                    CockpitAvionicsV2Availability.GpsNavigation;
                snapshot.gpsDestinationIdentAscii =
                    systems.gpsDestinationIdentAscii;
                snapshot.gpsBearingDegrees = systems.gpsBearingDegrees;
                snapshot.gpsDistanceNauticalMiles =
                    systems.gpsDistanceNauticalMiles;
                snapshot.gpsGroundTrackDegrees =
                    systems.gpsGroundTrackDegrees;
                snapshot.gpsGroundTrackAvailable = systems.gpsGroundTrackAvailable;
                snapshot.gpsAltitudeMslFeet = systems.gpsAltitudeMslFeet;
            }
            if (systems.Has(CockpitSystemsAvailabilityV2.ComRadios))
            {
                snapshot.availability |=
                    CockpitAvionicsV2Availability.ComRadios;
                snapshot.com1ActiveKHz = systems.com1ActiveKHz;
                snapshot.com1StandbyKHz = systems.com1StandbyKHz;
                snapshot.com2ActiveKHz = systems.com2ActiveKHz;
                snapshot.com2StandbyKHz = systems.com2StandbyKHz;
            }
            if (systems.Has(CockpitSystemsAvailabilityV2.Transponder))
            {
                snapshot.availability |=
                    CockpitAvionicsV2Availability.Transponder;
                snapshot.transponderCodeBcd = systems.transponderCodeBcd;
                snapshot.transponderMode =
                    (CockpitAvionicsV2TransponderMode)
                    systems.transponderMode;
            }
            return true;
        }

        private bool StepModels(
            in FlightDynamicsState state,
            in FlightDynamicsControlSnapshot controls,
            in CockpitElectricalPowerInput power,
            float deltaTimeSeconds)
        {
            StepSystemsV2(state, controls, power);
            CockpitElectricalPowerInput legacyPower =
                ResolveLegacyElectricalPower(power);
            return _model.IsInitialized
                ? _model.Step(
                    state,
                    controls,
                    legacyPower,
                    deltaTimeSeconds)
                : _model.Reset(state, controls, legacyPower);
        }

        private void StepSystemsV2(
            in FlightDynamicsState state,
            in FlightDynamicsControlSnapshot controls,
            in CockpitElectricalPowerInput power)
        {
            if (!systemsScenarioV2.IsValid)
            {
                _systemsV2Model.Invalidate();
                return;
            }

            CockpitSystemsAuthorityInputV2 input =
                new CockpitSystemsAuthorityInputV2
                {
                    hasAuthoritativeFlightState = true,
                    flightState = state,
                    flightControls = controls,
                    electricalPowerSources =
                        new CockpitElectricalPowerSourceInputV2
                        {
                            hasAuthoritativeState =
                                power.hasAuthoritativeState,
                            batteryCanPowerMainBus =
                                power.batteryCanPowerBus,
                            alternatorCanPowerMainBus =
                                power.alternatorProducingPower
                        }
                };
            if (!_systemsV2Model.IsInitialized ||
                state.simulationTimeSeconds + 0.000000001 <
                _systemsV2Model.Current.flightSimulationTimeSeconds)
            {
                _systemsV2Model.Reset(systemsScenarioV2, input);
                return;
            }

            _systemsV2Model.Step(input);
        }

        private CockpitElectricalPowerInput ResolveLegacyElectricalPower(
            in CockpitElectricalPowerInput source)
        {
            CockpitSystemsSnapshotV2 systems = _systemsV2Model.Current;
            if (!systems.isValid ||
                !systems.Has(
                    CockpitSystemsAvailabilityV2.ElectricalBusPower))
            {
                // Preserve the existing truthful source only when V2 has no
                // valid scenario. Once V2 is initialized, it is the sole
                // master-switch state and unavailable bus truth stays absent.
                return !_systemsV2Model.IsInitialized &&
                       source.hasAuthoritativeState
                    ? source
                    : CockpitElectricalPowerInput.Unavailable;
            }

            return new CockpitElectricalPowerInput
            {
                hasAuthoritativeState = true,
                batteryMasterOn = systems.batteryMasterOn,
                alternatorMasterOn = systems.alternatorMasterOn,
                avionicsMasterOn = systems.avionicsMasterOn,
                batteryCanPowerBus = source.batteryCanPowerBus,
                alternatorProducingPower =
                    source.alternatorProducingPower
            };
        }

        public bool TryGetCockpitPresentationValue(
            CockpitPresentationChannel channel,
            out float value)
        {
            CockpitSystemsSnapshot snapshot = _model.Current;
            CockpitSystemsSnapshotV2 systems = _systemsV2Model.Current;
            value = 0f;
            if (!snapshot.isValid) return false;

            CockpitControlPositions controls = snapshot.controlPositions;
            switch (channel)
            {
                case CockpitPresentationChannel.AileronNormalized:
                case CockpitPresentationChannel.YokeRollNormalized:
                    value = controls.aileron;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.ElevatorNormalized:
                case CockpitPresentationChannel.YokePitchNormalized:
                    value = controls.elevator;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.RudderNormalized:
                case CockpitPresentationChannel.RudderPedalsNormalized:
                    value = controls.rudder;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.LeftToeBrakeNormalized:
                    value = controls.leftToeBrake;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.RightToeBrakeNormalized:
                    value = controls.rightToeBrake;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.ThrottleNormalized:
                    value = controls.throttle;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.MixtureNormalized:
                    value = controls.mixture;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.TrimNormalized:
                    value = controls.elevatorTrim;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.FlapsCommandNormalized:
                    value = controls.flapCommand;
                    return snapshot.Has(
                        CockpitDataAvailability.FlightControlCommands);
                case CockpitPresentationChannel.SimulationTimeSeconds:
                    value = (float)snapshot.simulationTimeSeconds;
                    return true;
                case CockpitPresentationChannel.CalibratedAirspeedKnots:
                    value = snapshot.indicatedAirspeedKnots;
                    return snapshot.Has(CockpitDataAvailability.AirData);
                case CockpitPresentationChannel.AltitudeMslFeet:
                    value = snapshot.indicatedAltitudeMslFeet;
                    return snapshot.Has(CockpitDataAvailability.AirData);
                case CockpitPresentationChannel.VerticalSpeedFeetPerMinute:
                    value = snapshot.indicatedVerticalSpeedFeetPerMinute;
                    return snapshot.Has(CockpitDataAvailability.AirData);
                case CockpitPresentationChannel.HeadingDegrees:
                    value = snapshot.indicatedHeadingDegrees;
                    return snapshot.Has(CockpitDataAvailability.Heading);
                case CockpitPresentationChannel.PitchDegrees:
                    value = snapshot.indicatedPitchDegrees;
                    return snapshot.Has(CockpitDataAvailability.Attitude);
                case CockpitPresentationChannel.BankDegrees:
                    value = snapshot.indicatedBankDegrees;
                    return snapshot.Has(CockpitDataAvailability.Attitude);
                case CockpitPresentationChannel.SideslipDegrees:
                    value = snapshot.aerodynamicSideslipDegrees;
                    return snapshot.Has(
                        CockpitDataAvailability.AerodynamicSideslip);
                case CockpitPresentationChannel.EngineRpm:
                    value = snapshot.indicatedEngineRpm;
                    return snapshot.Has(CockpitDataAvailability.EngineRpm);
                case CockpitPresentationChannel.WeightOnWheels:
                    value = snapshot.weightOnWheels ? 1f : 0f;
                    return snapshot.Has(CockpitDataAvailability.WeightOnWheels);
                case CockpitPresentationChannel.TurnRateDegreesPerSecond:
                    value = snapshot.indicatedBodyTurnRateDegreesPerSecond;
                    return snapshot.Has(CockpitDataAvailability.BodyTurnRate);
                case CockpitPresentationChannel.ActualFlapDegrees:
                    value = systems.actualFlapDegrees;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .ActualFlapPosition);
                case CockpitPresentationChannel.MagneticHeadingDegrees:
                    value = systems.magneticHeadingDegrees;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.MagneticHeading);
                case CockpitPresentationChannel.BarometricSettingInHg:
                    value = systems.barometricSettingInHg;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .BarometricSetting);
                case CockpitPresentationChannel.LeftFuelGallons:
                    value = systems.leftFuelGallons;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.FuelQuantity);
                case CockpitPresentationChannel.RightFuelGallons:
                    value = systems.rightFuelGallons;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.FuelQuantity);
                case CockpitPresentationChannel.FuelFlowGallonsPerHour:
                    value = systems.fuelFlowGallonsPerHour;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.FuelFlow);
                case CockpitPresentationChannel.OilPressurePsi:
                    value = systems.oilPressurePsi;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.OilPressure);
                case CockpitPresentationChannel.OilTemperatureFahrenheit:
                    value = systems.oilTemperatureCelsius * 1.8f + 32f;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.OilTemperature);
                case CockpitPresentationChannel.ElectricalBusVolts:
                    value = systems.electricalBusVolts;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .ElectricalIndications);
                case CockpitPresentationChannel.AlternatorAmps:
                    value = systems.alternatorAmps;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .ElectricalIndications);
                case CockpitPresentationChannel.VacuumInHg:
                    value = systems.vacuumInHg;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.VacuumSuction);
                case CockpitPresentationChannel.BatteryMasterOn:
                    value = systems.batteryMasterOn ? 1f : 0f;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .ElectricalControlPositions);
                case CockpitPresentationChannel.AlternatorMasterOn:
                    value = systems.alternatorMasterOn ? 1f : 0f;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .ElectricalControlPositions);
                case CockpitPresentationChannel.AvionicsMasterOn:
                    value = systems.avionicsMasterOn ? 1f : 0f;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .ElectricalControlPositions);
                case CockpitPresentationChannel.IgnitionSwitchPosition:
                    value = (float)systems.ignitionSwitchPosition;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .IgnitionAndStarterControl);
                case CockpitPresentationChannel.PrimerPositionNormalized:
                    value = systems.primerPositionNormalized;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2.PrimerControl);
                case CockpitPresentationChannel.FuelSelectorPosition:
                    value = (float)systems.fuelSelectorPosition;
                    return systems.isValid &&
                           systems.Has(
                               CockpitSystemsAvailabilityV2
                                   .FuelSelectorControl);
                default:
                    // AGL, slip-ball acceleration, unrepresented temperature
                    // channels, lighting controls and navigation remain absent.
                    return false;
            }
        }
    }
}
