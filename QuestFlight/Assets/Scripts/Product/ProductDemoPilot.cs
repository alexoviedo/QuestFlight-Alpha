using QuestFlightLab.Input;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>
    /// Minimal deterministic acceptance loop. It supplies controls through the same product input
    /// boundary as a gamepad and never writes aircraft, camera, seat, or XR transforms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductDemoPilot : MonoBehaviour
    {
        public const float PrimaryControlRatePerSecond = 0.9f;
        public const float EngineControlRatePerSecond = 0.65f;
        public const float ToeBrakeRatePerSecond = 1.8f;

        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private DeterministicGamepadInputSource deterministicInput;
        [SerializeField] private FlightDynamicsCoordinator flightCoordinator;
        [SerializeField] private bool autoStartDemo;
        [SerializeField, Min(30f)] private float loopDurationSeconds = 72f;
        [SerializeField, Min(0f)] private float runwayHeadingGain = 0.025f;
        [SerializeField, Min(0f)] private float runwayCrossTrackGain = 0.015f;
        [SerializeField, Range(0.02f, 0.25f)] private float maximumRunwayRudder = 0.14f;

        public bool IsRunning { get; private set; }
        public string Phase { get; private set; } = "ready";
        public ProductControlInput AuthoredProductInput => productInput;
        public DeterministicGamepadInputSource AuthoredDeterministicInput => deterministicInput;
        public bool HasAuthoredSources => productInput != null &&
                                          deterministicInput != null &&
                                          flightCoordinator != null;
        public bool AutoStartDemo => autoStartDemo;

        private float _startedAt;
        private readonly AircraftControlState _scheduledControls = AircraftControlState.Neutral(0.10f);
        private readonly AircraftControlState _appliedControls = AircraftControlState.Neutral(0.10f);
        private bool _runwayReferenceCaptured;
        private double _runwayReferenceHeadingDegrees;
        private Vector3 _runwayReferencePosition;
        private Vector3 _runwayRight;

        public void ConfigureAuthoredSources(
            ProductControlInput authoredProductInput,
            DeterministicGamepadInputSource authoredDeterministicInput,
            FlightDynamicsCoordinator authoredFlightCoordinator)
        {
            productInput = authoredProductInput;
            deterministicInput = authoredDeterministicInput;
            flightCoordinator = authoredFlightCoordinator;
        }

        public void ConfigureAuthoredAutoStart(bool enabled) => autoStartDemo = enabled;

        private void Start()
        {
            if (autoStartDemo) StartDemo();
        }

        public void StartDemo()
        {
            if (!HasAuthoredSources)
            {
                Debug.LogError("[QuestFlight][Demo] Authored product input references are missing.", this);
                return;
            }
            _startedAt = Time.unscaledTime;
            _scheduledControls.SetNeutral(0.10f);
            _appliedControls.SetNeutral(0.10f);
            _runwayReferenceCaptured = false;
            deterministicInput.SetControls(_appliedControls);
            IsRunning = true;
            Phase = "idle_brake_hold";
            productInput.SetDeterministicMode(true);
        }

        public void StopDemo()
        {
            IsRunning = false;
            Phase = "ready";
            deterministicInput?.SetControls(AircraftControlState.Neutral(0.10f));
            productInput?.SetDeterministicMode(false);
        }

        private void Update()
        {
            if (!IsRunning || deterministicInput == null) return;
            float time = Mathf.Repeat(Time.unscaledTime - _startedAt, Mathf.Max(30f, loopDurationSeconds));
            ScheduledControlsInto(time, out string phase, _scheduledControls);
            ApplyRunwayTracking(phase, _scheduledControls);
            RateLimitControlsInto(
                _scheduledControls,
                _appliedControls,
                Time.unscaledDeltaTime);
            Phase = phase;
            deterministicInput.SetControls(_appliedControls);
        }

        public static AircraftControlState ScheduledControls(float seconds, out string phase)
        {
            AircraftControlState controls = new AircraftControlState();
            ScheduledControlsInto(seconds, out phase, controls);
            return controls;
        }

        public static void ScheduledControlsInto(
            float seconds,
            out string phase,
            AircraftControlState controls)
        {
            if (controls == null)
            {
                phase = "invalid_controls_target";
                return;
            }

            controls.SetNeutral(0.10f);
            if (seconds < 3f)
            {
                phase = "idle_brake_hold";
                controls.leftToeBrake = 1f;
                controls.rightToeBrake = 1f;
            }
            else if (seconds < 11f)
            {
                phase = "taxi";
                controls.throttle = 0.28f;
                controls.rudder = 0f;
            }
            else if (seconds < 14f)
            {
                phase = "taxi_brake";
                controls.throttle = 0.10f;
                controls.leftToeBrake = 0.65f;
                controls.rightToeBrake = 0.65f;
            }
            else if (seconds < 31f)
            {
                phase = "takeoff_roll";
                controls.throttle = 1f;
                controls.trim = 0.08f;
                controls.rudder = 0f;
            }
            else if (seconds < 37f)
            {
                phase = "rotation";
                controls.throttle = 1f;
                controls.elevator = 0.32f;
                controls.trim = 0.08f;
                controls.rudder = 0f;
            }
            else if (seconds < 50f)
            {
                phase = "vy_climb";
                controls.throttle = 1f;
                controls.elevator = 0.10f;
                controls.trim = 0.10f;
            }
            else if (seconds < 62f)
            {
                phase = "shallow_left_turn";
                controls.throttle = 0.86f;
                controls.elevator = 0.05f;
                controls.trim = 0.08f;
                float turnTime = seconds - 50f;
                if (turnTime < 4f)
                {
                    float rollIn = Smooth01(turnTime / 4f);
                    controls.aileron = Mathf.Lerp(0f, -0.18f, rollIn);
                    controls.rudder = Mathf.Lerp(0f, -0.17f, rollIn);
                }
                else if (turnTime < 9f)
                {
                    controls.aileron = -0.05f;
                    controls.rudder = -0.055f;
                }
                else
                {
                    float rollOut = Smooth01((turnTime - 9f) / 3f);
                    controls.aileron = Mathf.Lerp(-0.05f, 0.13f, rollOut);
                    controls.rudder = Mathf.Lerp(-0.055f, 0.11f, rollOut);
                }
            }
            else
            {
                phase = "climb_stabilize";
                controls.throttle = 0.88f;
                controls.elevator = 0.04f;
                controls.trim = 0.08f;
            }
        }

        private void ApplyRunwayTracking(
            string phase,
            AircraftControlState controls)
        {
            if (controls == null || flightCoordinator == null ||
                !flightCoordinator.IsInitialized ||
                (phase != "taxi" && phase != "taxi_brake" &&
                 phase != "takeoff_roll" && phase != "rotation"))
                return;

            FlightDynamicsState state = flightCoordinator.CurrentState;
            if (!_runwayReferenceCaptured)
            {
                _runwayReferenceHeadingDegrees = state.headingDegrees;
                _runwayReferencePosition = state.positionUnityMeters;
                _runwayRight = Vector3.ProjectOnPlane(
                    state.rotationUnity * Vector3.right,
                    Vector3.up).normalized;
                _runwayReferenceCaptured = _runwayRight.sqrMagnitude > 0.5f;
            }
            if (!_runwayReferenceCaptured)
                return;

            float crossTrackMeters = Vector3.Dot(
                state.positionUnityMeters - _runwayReferencePosition,
                _runwayRight);
            controls.rudder = ComputeRunwayTrackingRudder(
                state.headingDegrees,
                _runwayReferenceHeadingDegrees,
                crossTrackMeters,
                runwayHeadingGain,
                runwayCrossTrackGain,
                maximumRunwayRudder);
        }

        public static float ComputeRunwayTrackingRudder(
            double currentHeadingDegrees,
            double referenceHeadingDegrees,
            float crossTrackMeters,
            float headingGain,
            float crossTrackGain,
            float maximumRudder)
        {
            float headingError = Mathf.DeltaAngle(
                (float)currentHeadingDegrees,
                (float)referenceHeadingDegrees);
            float command = headingError * Mathf.Max(0f, headingGain) -
                            crossTrackMeters * Mathf.Max(0f, crossTrackGain);
            return Mathf.Clamp(
                command,
                -Mathf.Abs(maximumRudder),
                Mathf.Abs(maximumRudder));
        }

        /// <summary>
        /// Allocation-free pilot-input rate limiter. The deterministic route
        /// and cockpit presentation consume the same resulting control state.
        /// </summary>
        public static void RateLimitControlsInto(
            AircraftControlState target,
            AircraftControlState current,
            float deltaTime)
        {
            if (target == null || current == null || deltaTime <= 0f) return;
            float primaryStep = PrimaryControlRatePerSecond * deltaTime;
            float engineStep = EngineControlRatePerSecond * deltaTime;
            float brakeStep = ToeBrakeRatePerSecond * deltaTime;
            current.aileron = Mathf.MoveTowards(current.aileron, target.aileron, primaryStep);
            current.elevator = Mathf.MoveTowards(current.elevator, target.elevator, primaryStep);
            current.rudder = Mathf.MoveTowards(current.rudder, target.rudder, primaryStep);
            current.throttle = Mathf.MoveTowards(current.throttle, target.throttle, engineStep);
            current.mixture = Mathf.MoveTowards(current.mixture, target.mixture, engineStep);
            current.carbHeat = Mathf.MoveTowards(current.carbHeat, target.carbHeat, engineStep);
            current.trim = Mathf.MoveTowards(current.trim, target.trim, primaryStep);
            current.flaps = Mathf.MoveTowards(current.flaps, target.flaps, engineStep);
            current.leftToeBrake = Mathf.MoveTowards(
                current.leftToeBrake, target.leftToeBrake, brakeStep);
            current.rightToeBrake = Mathf.MoveTowards(
                current.rightToeBrake, target.rightToeBrake, brakeStep);
            current.markerPressed = target.markerPressed;
            current.resetPressed = target.resetPressed;
            current.pausePressed = target.pausePressed;
            current.telemetryTogglePressed = target.telemetryTogglePressed;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
