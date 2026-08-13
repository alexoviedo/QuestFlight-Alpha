using QuestFlightLab.Input;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>
    /// Minimal deterministic acceptance loop. It supplies controls through the same product input
    /// boundary as a gamepad and never writes aircraft, camera, seat, or XR transforms.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class ProductDemoPilot : MonoBehaviour
    {
        public const float PrimaryControlRatePerSecond = 0.9f;
        public const float EngineControlRatePerSecond = 0.65f;
        public const float ToeBrakeRatePerSecond = 1.8f;
        public const float TakeoffRotationPitchDegrees = 8f;
        public const float TakeoffRotationLeadKnots = 0f;
        public const float TakeoffMinimumGroundRotationElevator = 0.30f;
        public const float TakeoffAirborneConfirmationSeconds = 0.60f;
        public const float TakeoffGuidanceHandoffSeconds = 2f;
        public const float TakeoffHandoffMinimumAglMeters = 15.24f;
        public const float TakeoffHandoffMinimumClimbFeetPerMinute = 200f;

        private enum GuidedTakeoffPhase
        {
            GroundRoll = 0,
            Rotation = 1,
            InitialClimb = 2,
            GuidanceHandoff = 3,
            Complete = 4
        }

        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private DeterministicGamepadInputSource deterministicInput;
        [SerializeField] private FlightDynamicsCoordinator flightCoordinator;
        [SerializeField] private FlightLessonRouteProfile routeProfile;
        [SerializeField] private bool autoStartDemo;
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
        public FlightLessonRouteProfile AuthoredRouteProfile => routeProfile;
        public FlightLessonRouteDefinition RouteDefinition => _routeDefinition;
        public FlightLessonProgressState ProgressState => _progress;
        public bool GuidanceSuppressed => _guidanceSuppressed;

        private readonly AircraftControlState _scheduledControls = AircraftControlState.Neutral(0.10f);
        private readonly AircraftControlState _appliedControls = AircraftControlState.Neutral(0.10f);
        private readonly FlightLessonGuidanceController _guidanceController =
            new FlightLessonGuidanceController();
        private FlightLessonRouteDefinition _routeDefinition;
        private FlightLessonProgressState _progress;
        private bool _guidanceSuppressed;
        private int _lastInputResetVersion;
        private GuidedTakeoffPhase _takeoffPhase;
        private float _takeoffAirborneSeconds;
        private float _takeoffHandoffSeconds;
        private int _guidanceApplyCount;
        private float _guidanceAccumulatedDeltaTime;
        private int _controlResetCount;
        private string _lastControlResetReason = "not_started";
        private FlightLessonControlCommand _lastGuidanceTarget;

        public string BuildFailureDiagnostic()
        {
            return
                $"Demo[Phase={Phase} Takeoff={_takeoffPhase} Segment={_progress.currentSegmentIndex} " +
                $"ApplyCount={_guidanceApplyCount} ApplyDt={_guidanceAccumulatedDeltaTime:F3} " +
                $"ResetCount={_controlResetCount} LastReset={_lastControlResetReason} " +
                $"Target=A{_lastGuidanceTarget.aileron:F3}/E{_lastGuidanceTarget.elevator:F3}/" +
                $"R{_lastGuidanceTarget.rudder:F3}/T{_lastGuidanceTarget.throttle:F3} " +
                $"Applied=A{_appliedControls.aileron:F3}/E{_appliedControls.elevator:F3}/" +
                $"R{_appliedControls.rudder:F3}/T{_appliedControls.throttle:F3}]";
        }

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

        public void ConfigureRouteProfileForAuthoring(
            FlightLessonRouteProfile authoredRouteProfile) =>
            routeProfile = authoredRouteProfile;

        private void Start()
        {
            if (autoStartDemo) StartDemo();
        }

        private void OnDisable()
        {
            if (IsRunning)
                StopDemo();
        }

        public void StartDemo()
        {
            if (!HasAuthoredSources)
            {
                Debug.LogError("[QuestFlight][Demo] Authored product input references are missing.", this);
                return;
            }
            if (!EnsureRouteDefinition(out string routeError))
            {
                Debug.LogError("[QuestFlight][Demo] " + routeError, this);
                return;
            }
            _scheduledControls.SetNeutral(0.10f);
            _appliedControls.SetNeutral(0.10f);
            RecordControlReset("StartDemo");
            _progress = FlightLessonProgressState.InitialFor(_routeDefinition);
            _guidanceController.Reset();
            ResetTakeoffGuidance(true);
            _guidanceSuppressed = false;
            _lastInputResetVersion = productInput.FlightLessonResetVersion;
            deterministicInput.SetControls(_appliedControls);
            IsRunning = true;
            Phase = "awaiting_flight_state";
            // Preserve the established demo lifecycle contract for QA callers. While
            // flight-lesson guidance is active ProductControlInput still samples the
            // live gamepad for shared authority; the deterministic source mirrors the
            // bounded guidance command as the no-player fallback.
            productInput.SetDeterministicMode(true);
            productInput.SetFlightLessonInputSuppressed(false);
            productInput.SetFlightLessonGuidance(
                true,
                ProductControlInput.ToFlightLessonCommand(_appliedControls));
        }

        public void StopDemo()
        {
            IsRunning = false;
            Phase = "ready";
            _guidanceSuppressed = false;
            _guidanceController.Reset();
            ResetTakeoffGuidance(true);
            deterministicInput?.SetControls(AircraftControlState.Neutral(0.10f));
            if (productInput != null)
            {
                productInput.SetFlightLessonInputSuppressed(false);
                productInput.DisableFlightLessonGuidance();
                productInput.SetDeterministicMode(false);
            }
        }

        private void Update()
        {
            if (!IsRunning || deterministicInput == null || productInput == null)
                return;
            if (_guidanceSuppressed)
                return;

            if (_lastInputResetVersion != productInput.FlightLessonResetVersion)
            {
                _lastInputResetVersion = productInput.FlightLessonResetVersion;
                _progress = FlightLessonProgressState.InitialFor(_routeDefinition);
                _guidanceController.Reset();
                ResetTakeoffGuidance(true);
                _scheduledControls.SetNeutral(0.10f);
                _appliedControls.SetNeutral(0.10f);
                RecordControlReset("InputResetVersionChanged");
            }

            if (flightCoordinator == null || !flightCoordinator.IsInitialized)
            {
                Phase = "awaiting_flight_state";
                return;
            }

            FlightDynamicsState state = flightCoordinator.CurrentState;
            if (!state.IsFinite)
            {
                Phase = "invalid_flight_state";
                return;
            }

            FlightLessonAircraftNavigationState navigation =
                BuildNavigationState(state);
            _progress = FlightLessonNavigator.AdvanceIfCaptured(
                _routeDefinition,
                _progress,
                navigation);
            if (_progress.lessonComplete)
            {
                FlightLessonControlCommand complete = LessonCompleteCommand();
                ApplyGuidanceCommand(complete, "lesson_complete");
                return;
            }

            float guidanceDeltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            if (!FlightLessonNavigator.TryEvaluate(
                    _routeDefinition,
                    _progress,
                    navigation,
                    out FlightLessonGuidanceIntent intent))
            {
                Phase = "guidance_unavailable";
                return;
            }

            if (TryBuildGuidedTakeoffCommand(
                    intent,
                    state,
                    guidanceDeltaTime,
                    out FlightLessonControlCommand takeoffCommand,
                    out string takeoffPhase))
            {
                ApplyGuidanceCommand(takeoffCommand, takeoffPhase);
                return;
            }

            if (!_guidanceController.TryStep(
                    intent,
                    BuildGuidanceAircraftState(state),
                    guidanceDeltaTime,
                    FlightLessonGuidanceControllerSettings.Default,
                    out FlightLessonControlCommand command))
            {
                Phase = "guidance_unavailable";
                return;
            }

            ConfigureCommandForSegment(intent, ref command);
            ApplyGuidanceCommand(command, intent.segmentId);
        }

        private void ApplyGuidanceCommand(
            in FlightLessonControlCommand command,
            string phase)
        {
            _lastGuidanceTarget = command;
            _guidanceApplyCount++;
            _guidanceAccumulatedDeltaTime += Mathf.Max(0f, Time.unscaledDeltaTime);
            ProductControlInput.CopyFlightLessonCommandInto(
                command,
                _scheduledControls);
            RateLimitControlsInto(
                _scheduledControls,
                _appliedControls,
                Time.unscaledDeltaTime);
            Phase = phase;
            deterministicInput.SetControls(_appliedControls);
            productInput.SetFlightLessonGuidance(
                true,
                ProductControlInput.ToFlightLessonCommand(_appliedControls));
        }

        public bool RestoreLessonSegment(int segmentIndex)
        {
            if (!EnsureRouteDefinition(out _) ||
                segmentIndex < 0 ||
                segmentIndex >= _routeDefinition.segments.Length)
                return false;

            _progress.currentSegmentIndex = segmentIndex;
            _progress.lessonComplete = false;
            _progress.latestRecoveryCheckpointIndex = LatestCheckpointBefore(
                _routeDefinition,
                segmentIndex);
            _guidanceController.Reset();
            ResetTakeoffGuidance(segmentIndex == 0);
            _scheduledControls.SetNeutral(0.10f);
            _appliedControls.SetNeutral(0.10f);
            RecordControlReset("RestoreLessonSegment");
            if (productInput != null)
            {
                _lastInputResetVersion = productInput.FlightLessonResetVersion;
                productInput.SetFlightLessonGuidance(
                    true,
                    ProductControlInput.ToFlightLessonCommand(_appliedControls));
            }
            Phase = _routeDefinition.segments[segmentIndex].id;
            return true;
        }

        public void SetGuidanceSuppressed(bool suppressed)
        {
            if (_guidanceSuppressed == suppressed)
                return;
            _guidanceSuppressed = suppressed;
            _guidanceController.Reset();
            if (suppressed)
                ResetTakeoffGuidance(_progress.currentSegmentIndex == 0);
            _scheduledControls.SetNeutral(0.10f);
            _appliedControls.SetNeutral(0.10f);
            RecordControlReset(suppressed
                ? "GuidanceSuppressed"
                : "GuidanceResumed");
            if (productInput != null)
                productInput.SetFlightLessonInputSuppressed(suppressed);
            Phase = suppressed ? "recovery_suppressed" :
                (_routeDefinition != null && !_progress.lessonComplete &&
                 _progress.currentSegmentIndex >= 0 &&
                 _progress.currentSegmentIndex < _routeDefinition.segments.Length
                    ? _routeDefinition.segments[_progress.currentSegmentIndex].id
                    : "awaiting_flight_state");
        }

        public static FlightLessonAircraftNavigationState BuildNavigationState(
            in FlightDynamicsState state)
        {
            return new FlightLessonAircraftNavigationState
            {
                positionUnityMeters = state.positionUnityMeters,
                altitudeMslFeet = (float)(state.altitudeMslMeters *
                                           FlightFrameConversions.MetersToFeet),
                calibratedAirspeedKnots = (float)state.calibratedAirspeedKnots,
                headingDegreesTrue = (float)state.headingDegrees
            };
        }

        public static FlightLessonGuidanceAircraftState BuildGuidanceAircraftState(
            in FlightDynamicsState state)
        {
            Vector3 rates = state.angularVelocityBodyDegreesPerSecond;
            return new FlightLessonGuidanceAircraftState
            {
                headingDegreesTrue = (float)state.headingDegrees,
                bankDegrees = (float)state.bankDegrees,
                pitchDegrees = (float)state.pitchDegrees,
                rollRateDegreesPerSecond = -rates.z,
                pitchRateDegreesPerSecond = -rates.x,
                yawRateDegreesPerSecond = rates.y,
                altitudeMslFeet = (float)(state.altitudeMslMeters *
                                           FlightFrameConversions.MetersToFeet),
                verticalSpeedFeetPerMinute = (float)state.verticalSpeedFeetPerMinute,
                calibratedAirspeedKnots = (float)state.calibratedAirspeedKnots,
                sideslipDegrees = (float)state.sideslipDegrees
            };
        }

        private bool EnsureRouteDefinition(out string error)
        {
            if (_routeDefinition == null)
            {
                if (routeProfile == null)
                {
                    error = "The authored flight-lesson route profile is missing.";
                    return false;
                }
                _routeDefinition = routeProfile.Definition;
            }
            if (_routeDefinition == null)
            {
                error = "The route definition is missing.";
                return false;
            }
            return _routeDefinition.TryValidate(out error);
        }

        private static int LatestCheckpointBefore(
            FlightLessonRouteDefinition route,
            int segmentIndex)
        {
            int checkpoint = route != null
                ? route.initialRecoveryCheckpointIndex
                : -1;
            for (int i = 0; i < segmentIndex; i++)
            {
                int candidate = route.segments[i].recoveryCheckpointOnCompletion;
                if (candidate >= 0)
                    checkpoint = candidate;
            }
            return checkpoint;
        }

        private float AuthoredRotationSpeedKnots()
        {
            C172StyleAircraftConfig config = flightCoordinator != null
                ? flightCoordinator.aircraftConfiguration
                : null;
            return config != null
                ? Mathf.Max(1f, config.Speeds.rotationKts)
                : 55f;
        }

        private static void ConfigureCommandForSegment(
            in FlightLessonGuidanceIntent intent,
            ref FlightLessonControlCommand command)
        {
            command.mixture = 1f;
            switch (intent.segmentKind)
            {
                case FlightLessonSegmentKind.Departure:
                    // A normally aspirated C172 departure remains at takeoff power.
                    // Airspeed is controlled with pitch until the departure leg is
                    // captured; handing throttle to the cruise-speed loop here caused
                    // the aircraft to settle back onto the runway.
                    command.throttle = 1f;
                    break;
                case FlightLessonSegmentKind.Downwind:
                    command.flaps = 0.25f;
                    break;
                case FlightLessonSegmentKind.Base:
                    command.flaps = 0.50f;
                    break;
                case FlightLessonSegmentKind.Final:
                    command.flaps = intent.normalizedSegmentProgress > 0.45f
                        ? 1f
                        : 0.75f;
                    break;
                case FlightLessonSegmentKind.Rollout:
                    command.throttle = 0.10f;
                    command.flaps = 1f;
                    command.leftToeBrake = 0.45f;
                    command.rightToeBrake = 0.45f;
                    break;
                default:
                    command.flaps = 0f;
                    break;
            }
        }

        private bool TryBuildGuidedTakeoffCommand(
            in FlightLessonGuidanceIntent intent,
            in FlightDynamicsState state,
            float deltaTime,
            out FlightLessonControlCommand command,
            out string phase)
        {
            command = default;
            phase = intent.segmentId;
            if (intent.segmentKind != FlightLessonSegmentKind.Departure)
                return false;

            float rotationSpeed = AuthoredRotationSpeedKnots();
            if (state.weightOnWheels)
            {
                // Wheel contact during the departure segment always re-arms the
                // takeoff controller. Previously Complete was checked first, so a
                // brief liftoff followed by runway contact permanently selected the
                // cruise controller and its reduced power command.
                _takeoffAirborneSeconds = 0f;
                _takeoffHandoffSeconds = 0f;
                _takeoffPhase = state.calibratedAirspeedKnots >=
                    rotationSpeed - TakeoffRotationLeadKnots
                    ? GuidedTakeoffPhase.Rotation
                    : GuidedTakeoffPhase.GroundRoll;
                command = BuildTakeoffCommand(
                    intent,
                    state,
                    _takeoffPhase == GuidedTakeoffPhase.Rotation,
                    runwayHeadingGain,
                    runwayCrossTrackGain,
                    maximumRunwayRudder);
                phase = intent.segmentId + (_takeoffPhase == GuidedTakeoffPhase.Rotation
                    ? "_rotation"
                    : "_ground_roll");
                return true;
            }

            if (_takeoffPhase == GuidedTakeoffPhase.Complete)
                return false;

            if (_takeoffPhase == GuidedTakeoffPhase.GroundRoll ||
                _takeoffPhase == GuidedTakeoffPhase.Rotation)
            {
                _takeoffPhase = GuidedTakeoffPhase.InitialClimb;
                _takeoffAirborneSeconds = 0f;
            }

            FlightLessonControlCommand stabilized = BuildTakeoffCommand(
                intent,
                state,
                true,
                runwayHeadingGain,
                runwayCrossTrackGain,
                maximumRunwayRudder);

            bool establishedClimb =
                state.altitudeAglMeters >= TakeoffHandoffMinimumAglMeters &&
                state.verticalSpeedFeetPerMinute >=
                    TakeoffHandoffMinimumClimbFeetPerMinute &&
                state.calibratedAirspeedKnots >= rotationSpeed;
            if (!establishedClimb)
            {
                _takeoffPhase = GuidedTakeoffPhase.InitialClimb;
                _takeoffAirborneSeconds = 0f;
                _takeoffHandoffSeconds = 0f;
                command = stabilized;
                phase = intent.segmentId + "_initial_climb";
                return true;
            }

            _takeoffAirborneSeconds += Mathf.Max(0f, deltaTime);
            if (_takeoffAirborneSeconds < TakeoffAirborneConfirmationSeconds)
            {
                command = stabilized;
                phase = intent.segmentId + "_initial_climb";
                return true;
            }

            if (_takeoffPhase != GuidedTakeoffPhase.GuidanceHandoff)
            {
                _takeoffPhase = GuidedTakeoffPhase.GuidanceHandoff;
                _takeoffHandoffSeconds = 0f;
                _guidanceController.Reset();
            }

            if (!_guidanceController.TryStep(
                    intent,
                    BuildGuidanceAircraftState(state),
                    deltaTime,
                    FlightLessonGuidanceControllerSettings.Default,
                    out FlightLessonControlCommand guided))
            {
                command = stabilized;
                phase = intent.segmentId + "_initial_climb";
                return true;
            }

            ConfigureCommandForSegment(intent, ref guided);

            _takeoffHandoffSeconds += Mathf.Max(0f, deltaTime);
            float blend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(_takeoffHandoffSeconds /
                              TakeoffGuidanceHandoffSeconds));
            command = LerpCommand(stabilized, guided, blend);
            phase = intent.segmentId + "_guidance_handoff";
            if (blend >= 1f)
                _takeoffPhase = GuidedTakeoffPhase.Complete;
            return true;
        }

        public static FlightLessonControlCommand BuildTakeoffCommand(
            in FlightLessonGuidanceIntent intent,
            in FlightDynamicsState state,
            bool rotating,
            float headingGain = 0.025f,
            float crossTrackGain = 0.015f,
            float maximumRudder = 0.14f)
        {
            float pitchRate = -state.angularVelocityBodyDegreesPerSecond.x;
            float elevator = rotating
                ? Mathf.Clamp(
                    (TakeoffRotationPitchDegrees - (float)state.pitchDegrees) * 0.055f -
                    pitchRate * 0.018f,
                    0f,
                    0.42f)
                : 0.02f;
            if (rotating && state.weightOnWheels)
            {
                // Maintain enough elevator authority to establish the authored
                // rotation attitude and wing loading until wheel contact is
                // actually released. The validated prototype takeoff scenario
                // uses the same bounded 0.30 command. Liftoff remains aerodynamic;
                // this does not inject position or vertical velocity.
                elevator = Mathf.Max(
                    elevator,
                    TakeoffMinimumGroundRotationElevator);
            }
            return new FlightLessonControlCommand
            {
                aileron = Mathf.Clamp(
                    -(float)state.bankDegrees * 0.035f -
                    (-state.angularVelocityBodyDegreesPerSecond.z) * 0.015f,
                    -0.20f,
                    0.20f),
                elevator = elevator,
                rudder = Mathf.Clamp(
                    FlightLessonPathMath.HeadingErrorDegrees(
                        intent.desiredTrackDegreesTrue,
                        (float)state.headingDegrees) * headingGain -
                    intent.crossTrackErrorMeters * crossTrackGain,
                    -maximumRudder,
                    maximumRudder),
                throttle = 1f,
                mixture = 1f,
                trim = 0.08f,
                flaps = 0f,
                leftToeBrake = 0f,
                rightToeBrake = 0f
            };
        }

        private static FlightLessonControlCommand LerpCommand(
            in FlightLessonControlCommand from,
            in FlightLessonControlCommand to,
            float amount)
        {
            float t = Mathf.Clamp01(amount);
            return new FlightLessonControlCommand
            {
                aileron = Mathf.Lerp(from.aileron, to.aileron, t),
                elevator = Mathf.Lerp(from.elevator, to.elevator, t),
                rudder = Mathf.Lerp(from.rudder, to.rudder, t),
                throttle = Mathf.Lerp(from.throttle, to.throttle, t),
                mixture = Mathf.Lerp(from.mixture, to.mixture, t),
                trim = Mathf.Lerp(from.trim, to.trim, t),
                flaps = Mathf.Lerp(from.flaps, to.flaps, t),
                leftToeBrake = Mathf.Lerp(from.leftToeBrake, to.leftToeBrake, t),
                rightToeBrake = Mathf.Lerp(from.rightToeBrake, to.rightToeBrake, t)
            };
        }

        private void ResetTakeoffGuidance(bool enabledForDeparture)
        {
            _takeoffPhase = enabledForDeparture
                ? GuidedTakeoffPhase.GroundRoll
                : GuidedTakeoffPhase.Complete;
            _takeoffAirborneSeconds = 0f;
            _takeoffHandoffSeconds = 0f;
        }

        private void RecordControlReset(string reason)
        {
            _controlResetCount++;
            _guidanceApplyCount = 0;
            _guidanceAccumulatedDeltaTime = 0f;
            _lastControlResetReason = reason ?? "unknown";
            _lastGuidanceTarget = default;
        }

        private static FlightLessonControlCommand LessonCompleteCommand()
        {
            return new FlightLessonControlCommand
            {
                throttle = 0.10f,
                mixture = 1f,
                flaps = 1f,
                leftToeBrake = 0.65f,
                rightToeBrake = 0.65f
            };
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
