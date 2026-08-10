using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Thin product-frame adapter over the package-owned native ABI. It owns no
    /// Unity component and can only be instantiated by the sole serialized
    /// FlightDynamicsCoordinator authority.
    /// </summary>
    public sealed class JSBSimNativeDevelopmentBackend : IFlightDynamicsBackend
    {
        private readonly string _dataRoot;
        private readonly JSBSimNativeSession _session = new JSBSimNativeSession();
        private readonly bool _libraryAvailable;
        private FlightDynamicsBackendContext _context;

        public JSBSimNativeDevelopmentBackend(string dataRoot)
        {
            _dataRoot = dataRoot;
            _libraryAvailable = JSBSimNativeSession.TryProbe(out string error);
            LastError = error;
        }

        public FlightDynamicsBackendKind Kind => FlightDynamicsBackendKind.JSBSimNativeDevelopment;
        public string DisplayName => "JSBSim 1.3.1 native (development gate)";
        public bool IsAvailable => _libraryAvailable;
        public bool IsInitialized => _session.IsInitialized;
        public string LastError { get; private set; } = string.Empty;
        public FlightDynamicsState CurrentState { get; private set; }

        public bool Initialize(FlightDynamicsBackendContext context)
        {
            if (!IsAvailable) return Fail(string.IsNullOrWhiteSpace(LastError)
                ? "Native JSBSim plugin is unavailable on this platform."
                : LastError);
            if (context == null || context.simulationRoot == null || context.aircraftState == null)
                return Fail("Native JSBSim requires the authored simulation root and AircraftState.");
            if (context.terrainSurface == null)
                return Fail("Native JSBSim requires the serialized production terrain surface.");
            if (!JSBSimRuntimeData.ValidateDataRoot(_dataRoot, out string dataError))
                return Fail(dataError);

            _context = context;
            if (!_session.Initialize(_dataRoot))
            {
                _context = null;
                return Fail(_session.LastError);
            }
            LastError = string.Empty;
            return true;
        }

        public bool Reset(FlightDynamicsInitialConditions initialConditions)
        {
            if (!RequireInitialized()) return false;
            if (!TryTerrainElevationAt(
                    initialConditions.latitudeDegrees,
                    initialConditions.longitudeDegrees,
                    initialConditions.altitudeMslMeters,
                    out double terrainElevationMslMeters))
                return false;
            JSBSimNativeInitialConditions native = new JSBSimNativeInitialConditions
            {
                latitudeDegrees = initialConditions.latitudeDegrees,
                longitudeDegrees = initialConditions.longitudeDegrees,
                altitudeMslFeet = initialConditions.altitudeMslMeters * FlightFrameConversions.MetersToFeet,
                terrainElevationMslFeet = terrainElevationMslMeters * FlightFrameConversions.MetersToFeet,
                calibratedAirspeedKnots = initialConditions.calibratedAirspeedKnots,
                headingDegrees = initialConditions.headingDegrees,
                pitchDegrees = initialConditions.pitchDegrees,
                bankDegrees = initialConditions.bankDegrees,
                flightPathAngleDegrees = initialConditions.flightPathAngleDegrees,
                engineRunning = initialConditions.engineRunning ? 1 : 0
            };
            if (!_session.Reset(native)) return Fail(_session.LastError);
            return CaptureState();
        }

        public void SetControls(AircraftControlState controls)
        {
            controls ??= AircraftControlState.Neutral();
            _session.SetControls(new JSBSimNativeControls
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
            });
        }

        public void SetAtmosphere(FlightDynamicsAtmosphere atmosphere)
        {
            _session.SetAtmosphere(new JSBSimNativeAtmosphere
            {
                windNorthFeetPerSecond = atmosphere.windNorthMetersPerSecond * FlightFrameConversions.MetersToFeet,
                windEastFeetPerSecond = atmosphere.windEastMetersPerSecond * FlightFrameConversions.MetersToFeet,
                windDownFeetPerSecond = atmosphere.windDownMetersPerSecond * FlightFrameConversions.MetersToFeet
            });
        }

        public bool Advance(double fixedDeltaTimeSeconds)
        {
            if (!RequireInitialized()) return false;
            if (!TryTerrainElevationAt(
                    CurrentState.latitudeDegrees,
                    CurrentState.longitudeDegrees,
                    CurrentState.altitudeMslMeters,
                    out double terrainElevationMslMeters) ||
                !_session.SetTerrainElevation(
                    terrainElevationMslMeters * FlightFrameConversions.MetersToFeet))
                return Fail(string.IsNullOrWhiteSpace(_session.LastError)
                    ? "Could not update JSBSim from the authored terrain surface."
                    : _session.LastError);
            if (!_session.Advance(fixedDeltaTimeSeconds)) return Fail(_session.LastError);
            return CaptureState();
        }

        private bool TryTerrainElevationAt(
            double latitudeDegrees,
            double longitudeDegrees,
            double altitudeMslMeters,
            out double terrainElevationMslMeters)
        {
            terrainElevationMslMeters = 0.0;
            Vector3 enu = FlightFrameConversions.GeodeticToUnity(
                latitudeDegrees,
                longitudeDegrees,
                altitudeMslMeters,
                _context.localOrigin);
            if (!_context.terrainSurface.TrySample(
                    enu.x,
                    enu.z,
                    out ProductionTerrainSample sample))
                return Fail($"Authored terrain has no sample at ENU ({enu.x:0.###}, {enu.z:0.###}) m.");
            terrainElevationMslMeters = _context.localOrigin.altitudeMslMeters + sample.heightMeters;
            return true;
        }

        public void Dispose()
        {
            _session.Dispose();
            _context = null;
        }

        private bool CaptureState()
        {
            JSBSimNativeState native = _session.State;
            FlightDynamicsState state = new FlightDynamicsState
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
                    _context.localOrigin),
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
            if (!state.IsFinite) return Fail("Native JSBSim returned non-finite product state.");
            CurrentState = state;
            LastError = string.Empty;
            return true;
        }

        private bool RequireInitialized()
        {
            return IsInitialized || Fail("Native JSBSim product adapter is not initialized.");
        }

        private bool Fail(string message)
        {
            LastError = string.IsNullOrWhiteSpace(message) ? "Native JSBSim product adapter failed." : message;
            return false;
        }
    }
}
