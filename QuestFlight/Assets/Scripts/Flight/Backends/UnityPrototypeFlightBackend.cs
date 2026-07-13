using System;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Adapter around the existing deterministic prototype model. The model is
    /// preserved as the explicit safe fallback and is stepped only by the
    /// backend coordinator while this adapter owns authority.
    /// </summary>
    public sealed class UnityPrototypeFlightBackend : IFlightDynamicsBackend
    {
        private FlightDynamicsBackendContext _context;
        private AircraftControlState _controls = AircraftControlState.Neutral();
        private bool _physicsWasEnabled;
        private double _simulationTime;

        public FlightDynamicsBackendKind Kind => FlightDynamicsBackendKind.UnityPrototype;
        public string DisplayName => "Unity prototype fallback";
        public bool IsAvailable => true;
        public bool IsInitialized { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public FlightDynamicsState CurrentState { get; private set; }

        public bool Initialize(FlightDynamicsBackendContext context)
        {
            if (context == null || context.simulationRoot == null || context.unityPrototype == null)
            {
                LastError = "Unity prototype backend requires a simulation root and SimpleAircraftPhysics component.";
                return false;
            }

            if (context.unityPrototype.transform != context.simulationRoot)
            {
                LastError = "SimpleAircraftPhysics must live on the authoritative simulation root.";
                return false;
            }

            _context = context;
            if (context.unityPrototype.state == null || context.aircraftState == null ||
                context.unityPrototype.state != context.aircraftState)
            {
                LastError = "Unity prototype backend requires one authored AircraftState reference.";
                _context = null;
                return false;
            }

            if (context.unityPrototype.config == null)
            {
                LastError = "Unity prototype backend requires an authored C172StyleAircraftConfig asset.";
                _context = null;
                return false;
            }
            context.unityPrototype.state.config = context.unityPrototype.config;
            _physicsWasEnabled = context.unityPrototype.enabled;
            context.unityPrototype.enabled = false;
            IsInitialized = true;
            _simulationTime = 0.0;
            CaptureState();
            return true;
        }

        public bool Reset(FlightDynamicsInitialConditions initialConditions)
        {
            if (!RequireInitialized()) return false;
            Vector3 position = FlightFrameConversions.GeodeticToUnity(
                initialConditions.latitudeDegrees,
                initialConditions.longitudeDegrees,
                initialConditions.altitudeMslMeters,
                _context.localOrigin);
            Quaternion rotation = FlightFrameConversions.JsbsimAttitudeToUnity(
                initialConditions.headingDegrees,
                initialConditions.pitchDegrees,
                initialConditions.bankDegrees);
            Vector3 velocity = rotation * Vector3.forward *
                               (float)(initialConditions.calibratedAirspeedKnots * FlightFrameConversions.KnotsToMetersPerSecond);
            _context.unityPrototype.groundHeightMeters = (float)(
                initialConditions.terrainElevationMslMeters - _context.localOrigin.altitudeMslMeters + 1.25);
            _context.unityPrototype.SetStateForTest(position, rotation.eulerAngles, velocity);
            _simulationTime = 0.0;
            CaptureState();
            return true;
        }

        public void SetControls(AircraftControlState controls)
        {
            _controls = controls ?? AircraftControlState.Neutral();
        }

        public void SetAtmosphere(FlightDynamicsAtmosphere atmosphere)
        {
            // The existing prototype has no wind model. Keeping this a no-op is
            // explicit and avoids pretending that its air mass is authoritative.
        }

        public bool Advance(double fixedDeltaTimeSeconds)
        {
            if (!RequireInitialized()) return false;
            if (fixedDeltaTimeSeconds <= 0.0)
            {
                LastError = "Fixed timestep must be positive.";
                return false;
            }

            try
            {
                _context.unityPrototype.StepSimulation(_controls, (float)fixedDeltaTimeSeconds);
                _simulationTime += fixedDeltaTimeSeconds;
                CaptureState();
                return CurrentState.IsFinite;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (_context?.unityPrototype != null)
            {
                _context.unityPrototype.enabled = _physicsWasEnabled;
            }

            IsInitialized = false;
            _context = null;
        }

        private bool RequireInitialized()
        {
            if (IsInitialized) return true;
            LastError = "Unity prototype backend is not initialized.";
            return false;
        }

        private void CaptureState()
        {
            Transform root = _context.simulationRoot;
            AircraftState legacy = _context.aircraftState != null
                ? _context.aircraftState
                : _context.unityPrototype.state;
            Vector3 velocity = legacy != null ? legacy.velocityWorld : Vector3.zero;
            GeodeticReference geodetic = FlightFrameConversions.UnityToGeodetic(root.position, _context.localOrigin);
            CurrentState = new FlightDynamicsState
            {
                simulationTimeSeconds = _simulationTime,
                latitudeDegrees = geodetic.latitudeDegrees,
                longitudeDegrees = geodetic.longitudeDegrees,
                altitudeMslMeters = geodetic.altitudeMslMeters,
                altitudeAglMeters = root.position.y - _context.unityPrototype.groundHeightMeters + 1.25,
                calibratedAirspeedKnots = legacy != null ? legacy.airspeedKts : velocity.magnitude * AircraftUnitConversions.MetersPerSecondToKnots,
                groundSpeedKnots = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude * AircraftUnitConversions.MetersPerSecondToKnots,
                verticalSpeedFeetPerMinute = legacy != null ? legacy.verticalSpeedFpm : velocity.y * AircraftUnitConversions.MetersPerSecondToFeetPerMinute,
                headingDegrees = legacy != null ? legacy.headingDeg : root.eulerAngles.y,
                pitchDegrees = legacy != null ? legacy.pitchDeg : -AircraftState.NormalizeAngle(root.eulerAngles.x),
                bankDegrees = legacy != null ? legacy.bankDeg : AircraftState.NormalizeAngle(root.eulerAngles.z),
                angleOfAttackDegrees = legacy != null ? legacy.angleOfAttackDeg : 0.0,
                sideslipDegrees = legacy != null ? legacy.slipSkid : 0.0,
                engineRpm = legacy != null ? legacy.engineRpm : 0.0,
                loadFactorG = legacy != null ? legacy.loadFactorG : 1.0,
                weightOnWheels = legacy != null && legacy.onGround,
                positionUnityMeters = root.position,
                rotationUnity = root.rotation,
                velocityUnityMetersPerSecond = velocity,
                angularVelocityBodyDegreesPerSecond = legacy != null ? legacy.angularVelocityDeg : Vector3.zero
            };
        }
    }
}
