using System;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlight.Editor.FlightDynamicsValidation
{
    public struct FlightScenarioInitialState
    {
        public double altitudeAglMeters;
        public double terrainElevationMslMeters;
        public double calibratedAirspeedKnots;
        public double headingDegrees;
        public double pitchDegrees;
        public double bankDegrees;
        public double flightPathAngleDegrees;
        public bool engineRunning;

        public static FlightScenarioInitialState Airborne(
            double speedKnots = 75.0,
            double altitudeAglMeters = 600.0,
            double headingDegrees = 90.0,
            double flightPathAngleDegrees = 0.0,
            bool engineRunning = true)
        {
            return new FlightScenarioInitialState
            {
                altitudeAglMeters = altitudeAglMeters,
                terrainElevationMslMeters = GeodeticReference.Kbdu.altitudeMslMeters,
                calibratedAirspeedKnots = speedKnots,
                headingDegrees = headingDegrees,
                pitchDegrees = flightPathAngleDegrees,
                bankDegrees = 0.0,
                flightPathAngleDegrees = flightPathAngleDegrees,
                engineRunning = engineRunning
            };
        }

        public static FlightScenarioInitialState Ground()
        {
            FlightScenarioInitialState value = Airborne(
                0.0,
                FlightDynamicsAcceptanceContract.ProductGroundSpawnHeightMeters,
                FlightDynamicsInitialConditions.KbduRunwayTrueHeadingDegrees);
            value.pitchDegrees = 0.0;
            value.flightPathAngleDegrees = 0.0;
            return value;
        }
    }

    internal struct FlightScenarioMassConfiguration
    {
        public double pilotPounds;
        public double copilotPounds;
        public double rearLeftPounds;
        public double rearRightPounds;
        public double baggagePounds;
        public double leftFuelPounds;
        public double rightFuelPounds;

        public double PayloadAndFuelPounds =>
            pilotPounds + copilotPounds + rearLeftPounds + rearRightPounds +
            baggagePounds + leftFuelPounds + rightFuelPounds;

        public static FlightScenarioMassConfiguration Default =>
            new FlightScenarioMassConfiguration
            {
                pilotPounds = 170.0,
                copilotPounds = 170.0,
                leftFuelPounds = 130.0,
                rightFuelPounds = 130.0
            };
    }

    internal interface IFlightDynamicsTraceBackend : IDisposable
    {
        string Id { get; }
        bool SupportsWind { get; }
        bool SupportsMassAndCg { get; }
        bool SupportsTrim { get; }
        bool SupportsPhysicalGroundContact { get; }
        bool SupportsNoseWheelSteeringDiagnostics { get; }
        string LastError { get; }
        FlightDynamicsState State { get; }
        double TrueAirspeedKnots { get; }
        double WeightPounds { get; }
        double CgXInches { get; }
        double GearVerticalForcePounds { get; }
        double GearCompressionFeet { get; }
        double NoseGearSteeringDegrees { get; }
        bool ConfigureMass(FlightScenarioMassConfiguration configuration);
        bool Reset(FlightScenarioInitialState initialState);
        bool TryTrim(out AircraftControlState controls);
        void SetControls(AircraftControlState controls);
        bool SetWind(double northMetersPerSecond, double eastMetersPerSecond, double downMetersPerSecond);
        bool Step(double fixedDeltaTimeSeconds);
    }

    internal sealed class UnityPrototypeTraceBackend : IFlightDynamicsTraceBackend
    {
        private readonly GameObject _root;
        private readonly AircraftState _legacyState;
        private readonly SimpleAircraftPhysics _physics;
        private readonly UnityPrototypeFlightBackend _backend =
            new UnityPrototypeFlightBackend();

        public UnityPrototypeTraceBackend()
        {
            _root = new GameObject("FlightDynamicsValidation_UnityPrototype");
            _root.SetActive(false);
            _legacyState = _root.AddComponent<AircraftState>();
            _physics = _root.AddComponent<SimpleAircraftPhysics>();
            _physics.state = _legacyState;
            _physics.config = Resources.Load<C172StyleAircraftConfig>(
                ProductionFlightConfigInspector.ResourcePath);
            if (_physics.config == null)
                LastError = "The production C172StyleAircraftConfig resource is unavailable.";
            else
            {
                _legacyState.config = _physics.config;
                if (!_backend.Initialize(new FlightDynamicsBackendContext
                    {
                        simulationRoot = _root.transform,
                        aircraftState = _legacyState,
                        unityPrototype = _physics,
                        localOrigin = GeodeticReference.Kbdu,
                        fixedDeltaTimeSeconds =
                            FlightDynamicsAcceptanceContract.FixedStepSeconds
                    }))
                    LastError = _backend.LastError;
            }
        }

        public string Id => "UnityPrototype";
        public bool SupportsWind => false;
        public bool SupportsMassAndCg => false;
        public bool SupportsTrim => false;
        public bool SupportsPhysicalGroundContact => false;
        public bool SupportsNoseWheelSteeringDiagnostics => false;
        public string LastError { get; private set; } = string.Empty;
        public FlightDynamicsState State => _backend.CurrentState;
        public double TrueAirspeedKnots => State.calibratedAirspeedKnots;
        public double WeightPounds => double.NaN;
        public double CgXInches => double.NaN;
        public double GearVerticalForcePounds => double.NaN;
        public double GearCompressionFeet => double.NaN;
        public double NoseGearSteeringDegrees => double.NaN;

        public bool ConfigureMass(FlightScenarioMassConfiguration configuration) => false;

        public bool TryTrim(out AircraftControlState controls)
        {
            controls = null;
            return false;
        }

        public bool Reset(FlightScenarioInitialState initialState)
        {
            if (!_backend.IsInitialized) return false;
            GeodeticReference origin = GeodeticReference.Kbdu;
            bool reset = _backend.Reset(new FlightDynamicsInitialConditions
            {
                latitudeDegrees = origin.latitudeDegrees,
                longitudeDegrees = origin.longitudeDegrees,
                altitudeMslMeters = initialState.terrainElevationMslMeters +
                                    Math.Max(0.0, initialState.altitudeAglMeters),
                terrainElevationMslMeters = initialState.terrainElevationMslMeters,
                calibratedAirspeedKnots = initialState.calibratedAirspeedKnots,
                headingDegrees = initialState.headingDegrees,
                pitchDegrees = initialState.pitchDegrees,
                bankDegrees = initialState.bankDegrees,
                flightPathAngleDegrees = initialState.flightPathAngleDegrees,
                engineRunning = initialState.engineRunning
            });
            LastError = reset ? string.Empty : _backend.LastError;
            return reset;
        }

        public void SetControls(AircraftControlState controls)
        {
            _backend.SetControls(controls ?? AircraftControlState.Neutral());
        }

        public bool SetWind(double northMetersPerSecond, double eastMetersPerSecond, double downMetersPerSecond)
        {
            LastError = "UnityPrototypeFlightBackend has no atmosphere/wind model.";
            return false;
        }

        public bool Step(double fixedDeltaTimeSeconds)
        {
            if (fixedDeltaTimeSeconds <= 0.0)
            {
                LastError = "The fixed step must be positive.";
                return false;
            }
            try
            {
                bool advanced = _backend.Advance(fixedDeltaTimeSeconds);
                LastError = advanced ? string.Empty : _backend.LastError;
                return advanced;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                return false;
            }
        }

        public void Dispose()
        {
            _backend.Dispose();
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }
    }

    internal sealed class NativeJsbsimTraceBackend : IFlightDynamicsTraceBackend
    {
        private readonly JSBSimNativeSession _session = new JSBSimNativeSession();
        private JSBSimNativeControls _controls;
        private JSBSimNativeAtmosphere _atmosphere;

        public NativeJsbsimTraceBackend()
        {
            if (!_session.Initialize(JSBSimRuntimeData.EditorPackageDataRoot))
                LastError = _session.LastError;
        }

        public string Id => "JSBSimNativeDevelopment";
        public bool SupportsWind => true;
        public bool SupportsMassAndCg => true;
        public bool SupportsTrim => true;
        public bool SupportsPhysicalGroundContact => true;
        public bool SupportsNoseWheelSteeringDiagnostics => true;
        public string LastError { get; private set; } = string.Empty;
        public FlightDynamicsState State { get; private set; }
        public double TrueAirspeedKnots => _session.Diagnostics.trueAirspeedKnots;
        public double WeightPounds => _session.Diagnostics.weightPounds;
        public double CgXInches => _session.Diagnostics.cgXInches;
        public double GearVerticalForcePounds => _session.Diagnostics.gearForceZPounds;
        public double GearCompressionFeet =>
            _session.Diagnostics.gear0CompressionFeet +
            _session.Diagnostics.gear1CompressionFeet +
            _session.Diagnostics.gear2CompressionFeet;
        public double NoseGearSteeringDegrees => _session.Diagnostics.gear0SteeringDegrees;
        public bool IsAvailable => _session.IsInitialized;

        public bool ConfigureMass(FlightScenarioMassConfiguration configuration)
        {
            bool configured = _session.SetMassConfiguration(new JSBSimNativeMassConfiguration
            {
                pilotPounds = configuration.pilotPounds,
                copilotPounds = configuration.copilotPounds,
                rearLeftPounds = configuration.rearLeftPounds,
                rearRightPounds = configuration.rearRightPounds,
                baggagePounds = configuration.baggagePounds,
                leftFuelPounds = configuration.leftFuelPounds,
                rightFuelPounds = configuration.rightFuelPounds
            });
            LastError = configured ? string.Empty : _session.LastError;
            return configured;
        }

        public bool TryTrim(out AircraftControlState controls)
        {
            controls = null;
            if (!_session.Trim(
                    JSBSimNativeTrimRequest.StandardFull,
                    out JSBSimNativeTrimResult trim))
            {
                LastError = _session.LastError;
                return false;
            }

            controls = new AircraftControlState
            {
                aileron = (float)trim.aileron,
                elevator = (float)trim.elevatorNoseUp,
                rudder = (float)trim.rudderYawRight,
                throttle = (float)trim.throttle,
                mixture = 1f,
                trim = (float)trim.elevatorTrimNoseUp
            };
            CaptureState();
            LastError = string.Empty;
            return true;
        }

        public bool Reset(FlightScenarioInitialState initialState)
        {
            if (!_session.IsInitialized) return false;
            GeodeticReference origin = GeodeticReference.Kbdu;
            JSBSimNativeInitialConditions native = new JSBSimNativeInitialConditions
            {
                latitudeDegrees = origin.latitudeDegrees,
                longitudeDegrees = origin.longitudeDegrees,
                altitudeMslFeet = (initialState.terrainElevationMslMeters +
                                   Math.Max(0.0, initialState.altitudeAglMeters)) *
                                  FlightFrameConversions.MetersToFeet,
                terrainElevationMslFeet = initialState.terrainElevationMslMeters *
                                          FlightFrameConversions.MetersToFeet,
                calibratedAirspeedKnots = initialState.calibratedAirspeedKnots,
                headingDegrees = initialState.headingDegrees,
                pitchDegrees = initialState.pitchDegrees,
                bankDegrees = initialState.bankDegrees,
                flightPathAngleDegrees = initialState.flightPathAngleDegrees,
                engineRunning = initialState.engineRunning ? 1 : 0
            };
            if (!_session.Reset(native))
            {
                LastError = _session.LastError;
                return false;
            }
            _controls = default;
            _controls.mixture = initialState.engineRunning ? 1.0 : 0.0;
            _controls.throttle = initialState.engineRunning ? 0.10 : 0.0;
            _session.SetControls(_controls);
            _session.SetAtmosphere(_atmosphere);
            CaptureState();
            LastError = string.Empty;
            return true;
        }

        public void SetControls(AircraftControlState controls)
        {
            controls ??= AircraftControlState.Neutral();
            _controls = new JSBSimNativeControls
            {
                aileron = controls.aileron,
                elevatorNoseUp = controls.elevator,
                rudderYawRight = controls.rudder,
                throttle = controls.throttle,
                mixture = controls.mixture,
                carbHeat = controls.carbHeat,
                elevatorTrimNoseUp = controls.trim,
                flaps = controls.flaps,
                leftBrake = controls.leftToeBrake,
                rightBrake = controls.rightToeBrake
            };
            _session.SetControls(_controls);
        }

        public bool SetWind(double northMetersPerSecond, double eastMetersPerSecond, double downMetersPerSecond)
        {
            if (!_session.IsInitialized) return false;
            _atmosphere = new JSBSimNativeAtmosphere
            {
                windNorthFeetPerSecond = northMetersPerSecond * FlightFrameConversions.MetersToFeet,
                windEastFeetPerSecond = eastMetersPerSecond * FlightFrameConversions.MetersToFeet,
                windDownFeetPerSecond = downMetersPerSecond * FlightFrameConversions.MetersToFeet
            };
            _session.SetAtmosphere(_atmosphere);
            return true;
        }

        public bool Step(double fixedDeltaTimeSeconds)
        {
            if (!_session.Advance(fixedDeltaTimeSeconds))
            {
                LastError = _session.LastError;
                return false;
            }
            CaptureState();
            return State.IsFinite;
        }

        public void Dispose()
        {
            _session.Dispose();
        }

        private void CaptureState()
        {
            JSBSimNativeState native = _session.State;
            State = new FlightDynamicsState
            {
                simulationTimeSeconds = native.simulationTimeSeconds,
                latitudeDegrees = native.latitudeDegrees,
                longitudeDegrees = native.longitudeDegrees,
                altitudeMslMeters = native.altitudeMslFeet * FlightFrameConversions.FeetToMeters,
                altitudeAglMeters = native.altitudeAglFeet * FlightFrameConversions.FeetToMeters,
                calibratedAirspeedKnots = native.calibratedAirspeedKnots,
                groundSpeedKnots = native.groundSpeedKnots,
                verticalSpeedFeetPerMinute = native.verticalSpeedFeetPerMinute,
                headingDegrees = native.headingDegrees,
                pitchDegrees = native.pitchDegrees,
                bankDegrees = native.bankDegrees,
                angleOfAttackDegrees = native.angleOfAttackDegrees,
                sideslipDegrees = native.sideslipDegrees,
                engineRpm = native.engineRpm,
                loadFactorG = native.loadFactorG,
                weightOnWheels = native.weightOnWheels != 0,
                positionUnityMeters = FlightFrameConversions.GeodeticToUnity(
                    native.latitudeDegrees,
                    native.longitudeDegrees,
                    native.altitudeMslFeet * FlightFrameConversions.FeetToMeters,
                    GeodeticReference.Kbdu),
                rotationUnity = FlightFrameConversions.JsbsimAttitudeToUnity(
                    native.headingDegrees,
                    native.pitchDegrees,
                    native.bankDegrees),
                velocityUnityMetersPerSecond = FlightFrameConversions.NedFeetPerSecondToUnityMetersPerSecond(
                    native.velocityNorthFeetPerSecond,
                    native.velocityEastFeetPerSecond,
                    native.velocityDownFeetPerSecond),
                angularVelocityBodyDegreesPerSecond = FlightFrameConversions.BodyRatesRadiansToUnityDegrees(
                    native.rollRateRadiansPerSecond,
                    native.pitchRateRadiansPerSecond,
                    native.yawRateRadiansPerSecond)
            };
        }
    }
}
