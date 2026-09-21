using QuestFlightLab.Runtime;
using QuestFlight.Product;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlightLab.Input
{
    /// <summary>
    /// Cockpit flight-control channels positively represented and safe for
    /// Touch arbitration. Continuous grabs use an owner-checked lease; the
    /// represented flap selector writes the same persistent command value the
    /// gamepad buttons already own.
    /// </summary>
    public enum ProductTouchControlChannel
    {
        None = 0,
        PilotYoke = 1,
        Throttle = 2,
        Mixture = 3,
        FlapSelector = 4
    }

    /// <summary>
    /// Product gamepad/deterministic-demo mapping. It has no USB2BLE or HOTAS discovery path and
    /// never changes scene hierarchy. Deterministic input is selected only by an explicit flag.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductControlInput : MonoBehaviour
    {
        public const float TouchOverrideLeaseTimeoutSeconds = 0.25f;

        [SerializeField] private GamepadInputReader gamepad;
        [SerializeField] private DeterministicGamepadInputSource deterministicInput;
        [SerializeField] private bool deterministicMode;
        [SerializeField] private bool invertElevator = true;
        [SerializeField, Range(0f, 1f)] private float defaultThrottle = 0.10f;
        [SerializeField, Min(0.05f)] private float throttleRatePerSecond = 0.35f;
        [SerializeField, Min(0.05f)] private float trimRatePerSecond = 0.35f;
        [SerializeField, Range(0.1f, 0.5f)] private float flapStep = 0.25f;
        [Header("Guided lesson handover")]
        [SerializeField, Range(0.25f, 4f)] private float guidanceReacquisitionRatePerSecond = 1.25f;
        [SerializeField, Range(0.01f, 0.25f)] private float manualEngineAxisDeadzone = 0.08f;
        [SerializeField, Range(0f, 3f)] private float manualThrottleHoldSeconds = 1.25f;

        public AircraftControlState Current { get; private set; } = AircraftControlState.Neutral(0.10f);
        public bool Paused { get; private set; }
        public event System.Action TrainingRestartRequested;
        public bool DeterministicMode => deterministicMode;
        public bool FlightLessonGuidanceActive => _flightLessonGuidanceActive;
        public bool FlightLessonInputSuppressed => _flightLessonInputSuppressed;
        public int FlightLessonResetVersion => _flightLessonResetVersion;
        public GamepadInputReader AuthoredGamepad => gamepad;
        public DeterministicGamepadInputSource AuthoredDeterministicInput => deterministicInput;
        public bool HasAuthoredSources => gamepad != null && deterministicInput != null;

        private readonly GamepadInputSnapshot _disconnectedFallback = GamepadInputSnapshot.Disconnected(0f, 0f);
        private bool _previousButtonWest;
        private bool _previousButtonNorth;
        private bool _previousButtonEast;
        private bool _previousStartButton;
        private float _throttle;
        private float _mixture = 1f;
        private float _carbHeat;
        private float _trim;
        private float _flaps;
        private bool _resetQueued;

        private struct TouchOverrideState
        {
            public bool active;
            public int ownerToken;
            public float primary;
            public float secondary;
            public float lastRefreshUnscaledTime;
        }

        private TouchOverrideState _touchYoke;
        private TouchOverrideState _touchThrottle;
        private TouchOverrideState _touchMixture;
        private float _baseAileron;
        private float _baseElevator;
        private float _baseThrottle = 0.10f;
        private float _baseMixture = 1f;
        private FlightLessonControlCommand _flightLessonGuidance;
        private FlightLessonControlCommand _recoverySuppressedCommand;
        private bool _flightLessonGuidanceActive;
        private bool _flightLessonInputSuppressed;
        private bool _hasRecoverySuppressedCommand;
        private int _flightLessonResetVersion;
        private float _aileronGuidanceBlend = 1f;
        private float _elevatorGuidanceBlend = 1f;
        private float _rudderGuidanceBlend = 1f;
        private float _throttleGuidanceBlend = 1f;
        private float _mixtureGuidanceBlend = 1f;
        private float _aileronReacquireFrom;
        private float _elevatorReacquireFrom;
        private float _rudderReacquireFrom;
        private float _throttleReacquireFrom = 0.10f;
        private float _mixtureReacquireFrom = 1f;
        private float _manualThrottleOwnedUntil;
        private bool _trimManuallyOwned;
        private bool _flapsManuallyOwned;
        private ProductFlightModeCoordinator _touchFlightMode;
        private bool _touchRudderArmed;
        private bool _touchControlsRequireRelease;
        public void RequireTouchRelease() { _touchControlsRequireRelease=true; _touchRudderArmed=false; ReleaseAllTouchOverrides(); }

        public bool CanAcceptTouchOverrides =>
            enabled &&
            gameObject.activeInHierarchy &&
            !_flightLessonInputSuppressed &&
            !_touchControlsRequireRelease &&
            HasAuthoredSources;

        public int ActiveTouchOverrideCount =>
            (_touchYoke.active ? 1 : 0) +
            (_touchThrottle.active ? 1 : 0) +
            (_touchMixture.active ? 1 : 0);

        /// <summary>
        /// Builds a recovery-only snapshot of every input stage that can replace or
        /// attenuate lesson guidance. This is intentionally called only after a
        /// classified failure so the production update loop remains allocation-free.
        /// </summary>
        public string BuildFlightLessonFailureDiagnostic()
        {
            GamepadInputSnapshot snapshot = gamepad != null
                ? gamepad.Current
                : null;
            string device = snapshot != null && snapshot.connected
                ? $"{snapshot.displayName}/{snapshot.product}/{snapshot.layout}"
                : "disconnected";
            return
                $"Input[Device={device} " +
                $"Axes=L({snapshot?.leftStickX ?? 0f:F3},{snapshot?.leftStickY ?? 0f:F3}) " +
                $"R({snapshot?.rightStickX ?? 0f:F3},{snapshot?.rightStickY ?? 0f:F3}) " +
                $"Triggers=({snapshot?.leftTrigger ?? 0f:F3},{snapshot?.rightTrigger ?? 0f:F3}) " +
                $"Guidance=A{_flightLessonGuidance.aileron:F3}/E{_flightLessonGuidance.elevator:F3}/" +
                $"R{_flightLessonGuidance.rudder:F3}/T{_flightLessonGuidance.throttle:F3} " +
                $"Blend=A{_aileronGuidanceBlend:F3}/E{_elevatorGuidanceBlend:F3}/" +
                $"R{_rudderGuidanceBlend:F3}/T{_throttleGuidanceBlend:F3} " +
                $"AccumulatorThrottle={_throttle:F3} ManualThrottleUntil={_manualThrottleOwnedUntil:F3} " +
                $"PositionThrottle={snapshot?.hasThrottlePosition ?? false}/" +
                $"{snapshot?.throttlePosition ?? 0f:F3} " +
                $"Touch=Y{_touchYoke.active}({ _touchYoke.primary:F3},{_touchYoke.secondary:F3})/" +
                $"T{_touchThrottle.active}({_touchThrottle.primary:F3})/" +
                $"M{_touchMixture.active}({_touchMixture.primary:F3})]";
        }

        public void ConfigureAuthoredSources(
            GamepadInputReader authoredGamepad,
            DeterministicGamepadInputSource authoredDeterministicInput)
        {
            gamepad = authoredGamepad;
            deterministicInput = authoredDeterministicInput;
        }

        public void SetDeterministicMode(bool enabled) => deterministicMode = enabled;

        /// <summary>
        /// Supplies backend-neutral lesson guidance without replacing the product input
        /// boundary. Gamepad input is still sampled, and Touch/hand/controller leases are
        /// applied after shared-control mixing so an owned cockpit control stays authoritative.
        /// </summary>
        public void SetFlightLessonGuidance(
            bool enabled,
            in FlightLessonControlCommand command)
        {
            _flightLessonGuidance = command;
            if (_flightLessonGuidanceActive == enabled)
                return;

            _flightLessonGuidanceActive = enabled;
            ResetFlightLessonHandoverState();
        }

        public void DisableFlightLessonGuidance()
        {
            _flightLessonGuidanceActive = false;
            ResetFlightLessonHandoverState();
        }

        /// <summary>
        /// Recovery-only safety gate. Suppression releases every interaction lease and
        /// publishes neutral controls without disabling or reconfiguring any input device.
        /// </summary>
        public void SetFlightLessonInputSuppressed(bool suppressed)
        {
            if (_flightLessonInputSuppressed == suppressed)
                return;
            if (suppressed)
                ReleaseAllTouchOverrides();
            _flightLessonInputSuppressed = suppressed;
            ResetFlightLessonHandoverState();
            if (suppressed)
                PublishSuppressedControls();
            else
                _hasRecoverySuppressedCommand = false;
        }

        /// <summary>
        /// Atomically clears every input latch that may prevent the sole flight
        /// authority from advancing after an authored recovery. The command is
        /// held only while recovery suppression is active; normal lesson mixing
        /// resumes when suppression is released.
        /// </summary>
        public void PrepareForFlightLessonRecovery(
            in FlightLessonControlCommand recoveryCommand)
        {
            ReleaseAllTouchOverrides();
            Paused = false;
            _resetQueued = false;

            GamepadInputSnapshot snapshot = gamepad != null
                ? gamepad.Current
                : _disconnectedFallback;
            _previousButtonWest = snapshot != null && snapshot.buttonWest;
            _previousButtonNorth = snapshot != null && snapshot.buttonNorth;
            _previousButtonEast = snapshot != null && snapshot.buttonEast;
            _previousStartButton = snapshot != null && snapshot.startButton;

            _throttle = Mathf.Clamp01(recoveryCommand.throttle);
            _mixture = Mathf.Clamp01(recoveryCommand.mixture);
            _trim = Mathf.Clamp(recoveryCommand.trim, -1f, 1f);
            _flaps = Mathf.Clamp01(recoveryCommand.flaps);
            _recoverySuppressedCommand = recoveryCommand;
            _hasRecoverySuppressedCommand = true;
            ResetFlightLessonHandoverState();
            PublishSuppressedControls();
        }

        public int ReleaseAllTouchOverrides()
        {
            int released = 0;
            if (_touchYoke.active)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    ref _touchYoke);
                released++;
            }
            if (_touchThrottle.active)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    ref _touchThrottle);
                released++;
            }
            if (_touchMixture.active)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.Mixture,
                    ref _touchMixture);
                released++;
            }
            return released;
        }

        /// <summary>
        /// Begins an owner-checked override for one represented cockpit
        /// control. The current gamepad/deterministic value is retained as the
        /// underlying base. Unguided position-holding engine controls commit
        /// their final value on release; self-centering controls restore the base.
        /// </summary>
        public bool TryBeginTouchOverride(
            ProductTouchControlChannel channel,
            int ownerToken,
            float primary,
            float secondary = 0f)
        {
            if (!CanAcceptTouchOverrides ||
                ownerToken == 0 ||
                !TrySanitizeTouchValues(
                    channel,
                    primary,
                    secondary,
                    out float sanitizedPrimary,
                    out float sanitizedSecondary))
                return false;

            ref TouchOverrideState state =
                ref TouchState(channel);
            if (state.active)
                return false;

            CaptureBaseForChannel(channel);
            state.active = true;
            state.ownerToken = ownerToken;
            state.primary = sanitizedPrimary;
            state.secondary = sanitizedSecondary;
            state.lastRefreshUnscaledTime = Time.unscaledTime;
            ApplyTouchOverrides(Current);
            return true;
        }

        public bool TryUpdateTouchOverride(
            ProductTouchControlChannel channel,
            int ownerToken,
            float primary,
            float secondary = 0f)
        {
            if (!CanAcceptTouchOverrides ||
                ownerToken == 0 ||
                !TrySanitizeTouchValues(
                    channel,
                    primary,
                    secondary,
                    out float sanitizedPrimary,
                    out float sanitizedSecondary))
                return false;

            ref TouchOverrideState state =
                ref TouchState(channel);
            if (!state.active || state.ownerToken != ownerToken)
                return false;

            state.primary = sanitizedPrimary;
            state.secondary = sanitizedSecondary;
            state.lastRefreshUnscaledTime = Time.unscaledTime;
            ApplyTouchOverrides(Current);
            return true;
        }

        /// <summary>
        /// Release is allowed even when the input component is being disabled,
        /// so a lost controller or bridge cannot strand an authoritative
        /// control override.
        /// </summary>
        public bool TryEndTouchOverride(
            ProductTouchControlChannel channel,
            int ownerToken)
        {
            if (!IsTouchChannel(channel) || ownerToken == 0)
                return false;

            ref TouchOverrideState state =
                ref TouchState(channel);
            if (!state.active || state.ownerToken != ownerToken)
                return false;

            ReleaseTouchOverride(channel, ref state);
            return true;
        }

        public int ReleaseTouchOverridesOwnedBy(int ownerToken)
        {
            if (ownerToken == 0)
                return 0;

            int released = 0;
            if (_touchYoke.active &&
                _touchYoke.ownerToken == ownerToken)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    ref _touchYoke);
                released++;
            }
            if (_touchThrottle.active &&
                _touchThrottle.ownerToken == ownerToken)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    ref _touchThrottle);
                released++;
            }
            if (_touchMixture.active &&
                _touchMixture.ownerToken == ownerToken)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.Mixture,
                    ref _touchMixture);
                released++;
            }
            return released;
        }

        public bool TryGetTouchOverride(
            ProductTouchControlChannel channel,
            out int ownerToken,
            out float primary,
            out float secondary)
        {
            if (!IsTouchChannel(channel))
            {
                ownerToken = 0;
                primary = 0f;
                secondary = 0f;
                return false;
            }

            TouchOverrideState state = TouchState(channel);
            ownerToken = state.ownerToken;
            primary = state.primary;
            secondary = state.secondary;
            return state.active;
        }

        /// <summary>
        /// Returns whether the specified owner still holds the authoritative
        /// lease for a continuous cockpit control. Interaction adapters use
        /// this to reconcile their event-owned selection after the safety
        /// lease expires during a tracking loss or long frame stall.
        /// </summary>
        public bool IsTouchOverrideOwnedBy(
            ProductTouchControlChannel channel,
            int ownerToken)
        {
            if (!IsTouchChannel(channel) || ownerToken == 0)
                return false;
            TouchOverrideState state = TouchState(channel);
            return state.active && state.ownerToken == ownerToken;
        }

        public bool TryApplyTouchDiscreteValue(
            ProductTouchControlChannel channel,
            int ownerToken,
            float normalizedValue)
        {
            if (!CanAcceptTouchOverrides ||
                ownerToken == 0 ||
                !IsFinite(normalizedValue) ||
                channel !=
                ProductTouchControlChannel.FlapSelector)
                return false;

            _flaps = Mathf.Clamp01(normalizedValue);
            _flapsManuallyOwned = true;
            Current.flaps = _flaps;
            return true;
        }

        private void Awake()
        {
            if (!HasAuthoredSources)
            {
                Debug.LogError("[QuestFlight][Input] Serialized gamepad and deterministic input references are required.", this);
                enabled = false;
                return;
            }
            _throttle = Mathf.Clamp01(defaultThrottle);
            _touchFlightMode = FindFirstObjectByType<ProductFlightModeCoordinator>();
            CaptureBaseControls(Current);
        }

        private void OnDisable()
        {
            _touchRudderArmed = false;
            Current.rudder = 0f;
            ReleaseTouchOverridesOwnedBy(_touchYoke.ownerToken);
            ReleaseTouchOverridesOwnedBy(_touchThrottle.ownerToken);
            ReleaseTouchOverridesOwnedBy(_touchMixture.ownerToken);
        }

        private void Update()
        {
            if (_touchControlsRequireRelease &&
                OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger)<.1f && OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger)<.1f &&
                OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger)<.1f && OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger)<.1f &&
                OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).sqrMagnitude<.04f && OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).sqrMagnitude<.04f &&
                !OVRInput.Get(OVRInput.RawButton.A|OVRInput.RawButton.B|OVRInput.RawButton.X|OVRInput.RawButton.Y|OVRInput.RawButton.Start))
                _touchControlsRequireRelease=false;
            bool useDeterministic = deterministicMode &&
                                    !_flightLessonGuidanceActive &&
                                    deterministicInput != null &&
                                    deterministicInput.isActiveAndEnabled;
            if (gamepad == null)
                _disconnectedFallback.SetDisconnected(Time.unscaledTime, 0f);
            GamepadInputSnapshot snapshot = useDeterministic
                ? deterministicInput.Current
                : gamepad != null ? gamepad.Current : _disconnectedFallback;

            if (_flightLessonInputSuppressed)
            {
                _touchRudderArmed = false;
                PublishSuppressedControls();
                _previousButtonWest = snapshot.buttonWest;
                _previousButtonNorth = snapshot.buttonNorth;
                _previousButtonEast = snapshot.buttonEast;
                _previousStartButton = snapshot.startButton;
                return;
            }

            if (useDeterministic)
            {
                _throttle = deterministicInput.Throttle;
                _mixture = deterministicInput.Mixture;
                _carbHeat = deterministicInput.CarbHeat;
                _trim = deterministicInput.Trim;
                _flaps = deterministicInput.Flaps;
            }
            else
            {
                // Standard gamepads retain the established rate control. A
                // serialized external flight-controller map may instead
                // provide physical position controls that must hold without
                // continuous user force.
                bool absoluteThrottle = snapshot.connected &&
                                        snapshot.hasThrottlePosition;
                ApplyPositionControls(
                    snapshot,
                    ref _throttle,
                    ref _mixture,
                    ref _flaps);
                if (!absoluteThrottle)
                    _throttle = Mathf.Clamp01(
                        _throttle + snapshot.rightStickY *
                        throttleRatePerSecond * Time.unscaledDeltaTime);
                _trim = Mathf.Clamp(_trim + snapshot.dpadY * trimRatePerSecond * Time.unscaledDeltaTime, -1f, 1f);
            }

            bool flapDown = WasPressed(snapshot.buttonWest, _previousButtonWest);
            bool flapUp = WasPressed(snapshot.buttonNorth, _previousButtonNorth);
            bool reset = WasPressed(snapshot.buttonEast, _previousButtonEast);
            bool pause = WasPressed(snapshot.startButton, _previousStartButton);
            if (flapDown) _flaps = Mathf.Clamp01(_flaps + flapStep);
            if (flapUp) _flaps = Mathf.Clamp01(_flaps - flapStep);
            if (flapDown || flapUp) _flapsManuallyOwned = true;
            if (snapshot.flapsPositionChanged) _flapsManuallyOwned = true;
            if (Mathf.Abs(snapshot.dpadY) > 0.01f) _trimManuallyOwned = true;
            if (reset)
            {
                if (_touchFlightMode != null && _touchFlightMode.State == ProductFlightModeCoordinator.FlightModeState.Training)
                    TrainingRestartRequested?.Invoke();
                else _resetQueued = true;
                _flightLessonResetVersion++;
                ResetFlightLessonHandoverState();
            }
            if (pause) Paused = !Paused;

            MapSnapshotInto(snapshot, invertElevator, _throttle, _trim, _flaps, _mixture, _carbHeat, Current);
            bool touchFlightControlsAvailable = !useDeterministic && !_touchControlsRequireRelease && !_flightLessonGuidanceActive && !Paused &&
                Application.isFocused && OVRManager.hasInputFocus &&
                _touchFlightMode != null &&
                (_touchFlightMode.State == ProductFlightModeCoordinator.FlightModeState.FreeFlight ||
                 _touchFlightMode.State == ProductFlightModeCoordinator.FlightModeState.Training) &&
                _touchFlightMode.AuthoredSeatCalibration != null &&
                !_touchFlightMode.AuthoredSeatCalibration.IsOpen;
            bool touchRudderAvailable = touchFlightControlsAvailable &&
                OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) &&
                OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
            float touchRudderX = touchRudderAvailable
                ? OVRInput.Get(OVRInput.RawAxis2D.RThumbstick, OVRInput.Controller.RTouch).x
                : 0f;
            Current.rudder = ResolveTouchThumbstickRudder(
                Current.rudder, touchRudderX, touchRudderAvailable, ref _touchRudderArmed);
            // Each Touch index trigger applies its corresponding toe brake.
            if(touchFlightControlsAvailable && OVRInput.IsControllerConnected(OVRInput.Controller.LTouch) &&
                OVRInput.GetControllerOrientationTracked(OVRInput.Controller.LTouch))
            {
                float trimInput=OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;
                if(Mathf.Abs(trimInput)>.25f)
                {
                    _trim=Mathf.Clamp(_trim+trimInput*trimRatePerSecond*Mathf.Min(Time.unscaledDeltaTime,.05f),-1f,1f);
                    Current.trim=_trim; _trimManuallyOwned=true;
                }
            }
            if (touchFlightControlsAvailable &&
                OVRInput.IsControllerConnected(OVRInput.Controller.LTouch) &&
                OVRInput.GetControllerOrientationTracked(OVRInput.Controller.LTouch))
                Current.leftToeBrake = Mathf.Max(Current.leftToeBrake, Mathf.Clamp01(
                    OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger, OVRInput.Controller.LTouch)));
            if (touchRudderAvailable)
                Current.rightToeBrake = Mathf.Max(Current.rightToeBrake, Mathf.Clamp01(
                    OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger, OVRInput.Controller.RTouch)));
            if (_flightLessonGuidanceActive)
                ApplyFlightLessonGuidance(snapshot, Time.unscaledTime, Time.unscaledDeltaTime, Current);
            CaptureBaseControls(Current);
            ReleaseStaleTouchOverrides(Time.unscaledTime);
            ApplyTouchOverrides(Current);
            Current.resetPressed = reset;
            Current.pausePressed = pause;
            _previousButtonWest = snapshot.buttonWest;
            _previousButtonNorth = snapshot.buttonNorth;
            _previousButtonEast = snapshot.buttonEast;
            _previousStartButton = snapshot.startButton;
        }

        /// <summary>
        /// Right Touch stick temporarily owns rudder while deflected. Centering
        /// restores the existing source. Require neutral after UI/focus/tracking
        /// loss so an input held during menu navigation cannot steer the aircraft.
        /// </summary>
        public static float ResolveTouchThumbstickRudder(
            float baseRudder, float stickX, bool available, ref bool armed)
        {
            const float deadzone = 0.12f;
            if (!available || !IsFinite(stickX))
            {
                armed = false;
                return baseRudder;
            }
            float magnitude = Mathf.Abs(stickX);
            if (magnitude <= deadzone)
            {
                armed = true;
                return baseRudder;
            }
            return armed ? Mathf.Sign(stickX) * Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone)) : baseRudder;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                _touchRudderArmed = false;
                Current.rudder = 0f;
            }
        }

        public bool ConsumeResetRequest()
        {
            if (!_resetQueued) return false;
            _resetQueued = false;
            return true;
        }

        public static AircraftControlState MapSnapshot(
            GamepadInputSnapshot snapshot,
            bool invertElevator,
            float throttle,
            float trim,
            float flaps,
            float mixture,
            float carbHeat)
        {
            AircraftControlState result = new AircraftControlState();
            MapSnapshotInto(snapshot, invertElevator, throttle, trim, flaps, mixture, carbHeat, result);
            return result;
        }

        public static void ApplyPositionControls(
            GamepadInputSnapshot snapshot,
            ref float throttle,
            ref float mixture,
            ref float flaps)
        {
            if (snapshot == null || !snapshot.connected)
                return;
            if (snapshot.hasThrottlePosition)
                throttle = Mathf.Clamp01(snapshot.throttlePosition);
            if (snapshot.hasMixturePosition)
                mixture = Mathf.Clamp01(snapshot.mixturePosition);
            if (snapshot.hasFlapsPosition)
                flaps = Mathf.Clamp01(snapshot.flapsPosition);
        }

        public static void MapSnapshotInto(
            GamepadInputSnapshot snapshot,
            bool invertElevator,
            float throttle,
            float trim,
            float flaps,
            float mixture,
            float carbHeat,
            AircraftControlState result)
        {
            if (result == null) return;
            if (snapshot == null)
            {
                result.SetNeutral(throttle);
                return;
            }

            result.aileron = Mathf.Clamp(snapshot.leftStickX, -1f, 1f);
            result.elevator = Mathf.Clamp(invertElevator ? -snapshot.leftStickY : snapshot.leftStickY, -1f, 1f);
            result.rudder = Mathf.Clamp(snapshot.rightStickX, -1f, 1f);
            result.throttle = Mathf.Clamp01(throttle);
            result.mixture = Mathf.Clamp01(mixture);
            result.carbHeat = Mathf.Clamp01(carbHeat);
            result.trim = Mathf.Clamp(trim, -1f, 1f);
            result.flaps = Mathf.Clamp01(flaps);
            result.leftToeBrake = Mathf.Clamp01(snapshot.leftTrigger);
            result.rightToeBrake = Mathf.Clamp01(snapshot.rightTrigger);
            result.markerPressed = false;
            result.resetPressed = false;
            result.pausePressed = false;
            result.telemetryTogglePressed = false;
        }

        private static bool TrySanitizeTouchValues(
            ProductTouchControlChannel channel,
            float primary,
            float secondary,
            out float sanitizedPrimary,
            out float sanitizedSecondary)
        {
            sanitizedPrimary = 0f;
            sanitizedSecondary = 0f;
            if (!IsFinite(primary) || !IsFinite(secondary))
                return false;

            switch (channel)
            {
                case ProductTouchControlChannel.PilotYoke:
                    sanitizedPrimary = Mathf.Clamp(
                        primary,
                        -1f,
                        1f);
                    sanitizedSecondary = Mathf.Clamp(
                        secondary,
                        -1f,
                        1f);
                    return true;
                case ProductTouchControlChannel.Throttle:
                case ProductTouchControlChannel.Mixture:
                    sanitizedPrimary = Mathf.Clamp01(primary);
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsTouchChannel(
            ProductTouchControlChannel channel)
        {
            return channel >= ProductTouchControlChannel.PilotYoke &&
                   channel <= ProductTouchControlChannel.Mixture;
        }

        private ref TouchOverrideState TouchState(
            ProductTouchControlChannel channel)
        {
            switch (channel)
            {
                case ProductTouchControlChannel.PilotYoke:
                    return ref _touchYoke;
                case ProductTouchControlChannel.Throttle:
                    return ref _touchThrottle;
                case ProductTouchControlChannel.Mixture:
                    return ref _touchMixture;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(channel));
            }
        }

        private void CaptureBaseControls(AircraftControlState controls)
        {
            if (controls == null)
                return;
            _baseAileron = Mathf.Clamp(
                controls.aileron,
                -1f,
                1f);
            _baseElevator = Mathf.Clamp(
                controls.elevator,
                -1f,
                1f);
            _baseThrottle = Mathf.Clamp01(controls.throttle);
            _baseMixture = Mathf.Clamp01(controls.mixture);
        }

        private void CaptureBaseForChannel(
            ProductTouchControlChannel channel)
        {
            switch (channel)
            {
                case ProductTouchControlChannel.PilotYoke:
                    _baseAileron = Mathf.Clamp(
                        Current.aileron,
                        -1f,
                        1f);
                    _baseElevator = Mathf.Clamp(
                        Current.elevator,
                        -1f,
                        1f);
                    break;
                case ProductTouchControlChannel.Throttle:
                    _baseThrottle =
                        Mathf.Clamp01(Current.throttle);
                    break;
                case ProductTouchControlChannel.Mixture:
                    _baseMixture =
                        Mathf.Clamp01(Current.mixture);
                    break;
            }
        }

        private void ApplyTouchOverrides(
            AircraftControlState controls)
        {
            if (controls == null)
                return;
            if (_touchYoke.active)
            {
                controls.aileron = _touchYoke.primary;
                controls.elevator = _touchYoke.secondary;
            }
            if (_touchThrottle.active)
                controls.throttle = _touchThrottle.primary;
            if (_touchMixture.active)
                controls.mixture = _touchMixture.primary;
        }

        private void RestoreBaseForChannel(
            ProductTouchControlChannel channel)
        {
            switch (channel)
            {
                case ProductTouchControlChannel.PilotYoke:
                    Current.aileron = _baseAileron;
                    Current.elevator = _baseElevator;
                    break;
                case ProductTouchControlChannel.Throttle:
                    Current.throttle = _baseThrottle;
                    break;
                case ProductTouchControlChannel.Mixture:
                    Current.mixture = _baseMixture;
                    break;
            }
        }

        private void ReleaseTouchOverride(
            ProductTouchControlChannel channel,
            ref TouchOverrideState state)
        {
            if (_flightLessonGuidanceActive)
            {
                switch (channel)
                {
                    case ProductTouchControlChannel.PilotYoke:
                        _aileronReacquireFrom = state.primary;
                        _elevatorReacquireFrom = state.secondary;
                        _aileronGuidanceBlend = 0f;
                        _elevatorGuidanceBlend = 0f;
                        Current.aileron = state.primary;
                        Current.elevator = state.secondary;
                        break;
                    case ProductTouchControlChannel.Throttle:
                        _throttleReacquireFrom = state.primary;
                        _throttleGuidanceBlend = 0f;
                        _manualThrottleOwnedUntil = Time.unscaledTime;
                        Current.throttle = state.primary;
                        break;
                    case ProductTouchControlChannel.Mixture:
                        _mixtureReacquireFrom = state.primary;
                        _mixtureGuidanceBlend = 0f;
                        Current.mixture = state.primary;
                        break;
                }
            }
            if (!_flightLessonGuidanceActive)
            {
                switch (channel)
                {
                    case ProductTouchControlChannel.Throttle:
                        _throttle = Mathf.Clamp01(state.primary);
                        _baseThrottle = _throttle;
                        Current.throttle = _throttle;
                        break;
                    case ProductTouchControlChannel.Mixture:
                        _mixture = Mathf.Clamp01(state.primary);
                        _baseMixture = _mixture;
                        Current.mixture = _mixture;
                        break;
                    default:
                        RestoreBaseForChannel(channel);
                        break;
                }
            }
            state = default;
        }

        private void ApplyFlightLessonGuidance(
            GamepadInputSnapshot snapshot,
            float unscaledTime,
            float deltaTime,
            AircraftControlState controls)
        {
            if (controls == null)
                return;

            bool manualPrimaryAvailable = snapshot != null && snapshot.connected;
            FlightLessonControlCommand manual = ToFlightLessonCommand(controls);
            FlightLessonControlCommand guidance = _flightLessonGuidance;
            FlightLessonAssistanceMixSettings mixSettings =
                FlightLessonAssistanceMixSettings.Default;
            float reacquisitionStep = Mathf.Max(0.01f, guidanceReacquisitionRatePerSecond) *
                                      Mathf.Max(0f, deltaTime);

            PrepareGuidedAxis(
                manual.aileron,
                guidance.aileron,
                manualPrimaryAvailable,
                mixSettings,
                reacquisitionStep,
                ref _aileronReacquireFrom,
                ref _aileronGuidanceBlend,
                out guidance.aileron);
            PrepareGuidedAxis(
                manual.elevator,
                guidance.elevator,
                manualPrimaryAvailable,
                mixSettings,
                reacquisitionStep,
                ref _elevatorReacquireFrom,
                ref _elevatorGuidanceBlend,
                out guidance.elevator);
            PrepareGuidedAxis(
                manual.rudder,
                guidance.rudder,
                manualPrimaryAvailable,
                mixSettings,
                reacquisitionStep,
                ref _rudderReacquireFrom,
                ref _rudderGuidanceBlend,
                out guidance.rudder);

            bool throttleMoved = snapshot != null &&
                                 (snapshot.hasThrottlePosition
                                     ? snapshot.throttlePositionChanged
                                     : Mathf.Abs(snapshot.rightStickY) >=
                                       Mathf.Max(0.01f,
                                           manualEngineAxisDeadzone));
            if (throttleMoved)
            {
                _manualThrottleOwnedUntil = unscaledTime +
                                            Mathf.Max(0f, manualThrottleHoldSeconds);
                _throttleReacquireFrom = manual.throttle;
                _throttleGuidanceBlend = 0f;
            }
            bool throttleOwned = throttleMoved || unscaledTime < _manualThrottleOwnedUntil;
            if (!throttleOwned)
            {
                _throttleGuidanceBlend = Mathf.MoveTowards(
                    _throttleGuidanceBlend,
                    1f,
                    reacquisitionStep);
                guidance.throttle = Mathf.Lerp(
                    _throttleReacquireFrom,
                    guidance.throttle,
                    _throttleGuidanceBlend);
            }

            bool mixtureMoved = snapshot != null &&
                                snapshot.hasMixturePosition &&
                                snapshot.mixturePositionChanged;
            if (mixtureMoved)
            {
                _mixtureReacquireFrom = manual.mixture;
                _mixtureGuidanceBlend = 0f;
            }
            else
            {
                _mixtureGuidanceBlend = Mathf.MoveTowards(
                    _mixtureGuidanceBlend,
                    1f,
                    reacquisitionStep);
                guidance.mixture = Mathf.Lerp(
                    _mixtureReacquireFrom,
                    guidance.mixture,
                    _mixtureGuidanceBlend);
            }

            FlightLessonPlayerInput player = new FlightLessonPlayerInput
            {
                command = manual,
                primaryAxesAvailable = manualPrimaryAvailable,
                throttleOwned = throttleOwned,
                mixtureOwned = mixtureMoved,
                trimOwned = _trimManuallyOwned,
                flapsOwned = _flapsManuallyOwned,
                brakesOwned = manual.leftToeBrake > 0.01f ||
                              manual.rightToeBrake > 0.01f
            };
            FlightLessonControlCommand mixed = FlightLessonAssistanceMixer.Mix(
                guidance,
                player,
                mixSettings);
            CopyFlightLessonCommandInto(mixed, controls);
        }

        private static void PrepareGuidedAxis(
            float manual,
            float guidance,
            bool manualAvailable,
            in FlightLessonAssistanceMixSettings settings,
            float reacquisitionStep,
            ref float reacquireFrom,
            ref float blend,
            out float preparedGuidance)
        {
            float authority = manualAvailable
                ? FlightLessonAssistanceMixer.PlayerAuthority(manual, settings)
                : 0f;
            if (authority > 0.001f)
            {
                reacquireFrom = Mathf.Lerp(
                    guidance,
                    Mathf.Clamp(manual, -1f, 1f),
                    authority);
                blend = 0f;
                preparedGuidance = guidance;
            }
            else
            {
                blend = Mathf.MoveTowards(blend, 1f, reacquisitionStep);
                preparedGuidance = Mathf.Lerp(reacquireFrom, guidance, blend);
            }
        }

        public static FlightLessonControlCommand ToFlightLessonCommand(
            AircraftControlState controls)
        {
            if (controls == null)
                return default;
            return new FlightLessonControlCommand
            {
                aileron = controls.aileron,
                elevator = controls.elevator,
                rudder = controls.rudder,
                throttle = controls.throttle,
                mixture = controls.mixture,
                trim = controls.trim,
                flaps = controls.flaps,
                leftToeBrake = controls.leftToeBrake,
                rightToeBrake = controls.rightToeBrake
            };
        }

        public static void CopyFlightLessonCommandInto(
            in FlightLessonControlCommand command,
            AircraftControlState controls)
        {
            if (controls == null)
                return;
            controls.aileron = Mathf.Clamp(command.aileron, -1f, 1f);
            controls.elevator = Mathf.Clamp(command.elevator, -1f, 1f);
            controls.rudder = Mathf.Clamp(command.rudder, -1f, 1f);
            controls.throttle = Mathf.Clamp01(command.throttle);
            controls.mixture = Mathf.Clamp01(command.mixture);
            controls.trim = Mathf.Clamp(command.trim, -1f, 1f);
            controls.flaps = Mathf.Clamp01(command.flaps);
            controls.leftToeBrake = Mathf.Clamp01(command.leftToeBrake);
            controls.rightToeBrake = Mathf.Clamp01(command.rightToeBrake);
        }

        private void ResetFlightLessonHandoverState()
        {
            _aileronGuidanceBlend = 1f;
            _elevatorGuidanceBlend = 1f;
            _rudderGuidanceBlend = 1f;
            _throttleGuidanceBlend = 1f;
            _mixtureGuidanceBlend = 1f;
            _aileronReacquireFrom = Current != null ? Current.aileron : 0f;
            _elevatorReacquireFrom = Current != null ? Current.elevator : 0f;
            _rudderReacquireFrom = Current != null ? Current.rudder : 0f;
            _throttleReacquireFrom = Current != null ? Current.throttle : defaultThrottle;
            _mixtureReacquireFrom = Current != null ? Current.mixture : 1f;
            _manualThrottleOwnedUntil = 0f;
            _trimManuallyOwned = false;
            _flapsManuallyOwned = false;
        }

        private void PublishSuppressedControls()
        {
            if (_hasRecoverySuppressedCommand)
                CopyFlightLessonCommandInto(
                    _recoverySuppressedCommand,
                    Current);
            else
                Current.SetNeutral(Mathf.Clamp01(defaultThrottle));
        }

        private void ReleaseStaleTouchOverrides(
            float unscaledTime)
        {
            if (!IsFinite(unscaledTime))
                return;
            if (_touchYoke.active &&
                unscaledTime -
                _touchYoke.lastRefreshUnscaledTime >
                TouchOverrideLeaseTimeoutSeconds)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    ref _touchYoke);
            }
            if (_touchThrottle.active &&
                unscaledTime -
                _touchThrottle.lastRefreshUnscaledTime >
                TouchOverrideLeaseTimeoutSeconds)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.Throttle,
                    ref _touchThrottle);
            }
            if (_touchMixture.active &&
                unscaledTime -
                _touchMixture.lastRefreshUnscaledTime >
                TouchOverrideLeaseTimeoutSeconds)
            {
                ReleaseTouchOverride(
                    ProductTouchControlChannel.Mixture,
                    ref _touchMixture);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static bool WasPressed(bool current, bool previous) => current && !previous;
    }
}
