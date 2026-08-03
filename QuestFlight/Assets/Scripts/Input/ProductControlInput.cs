using QuestFlightLab.Runtime;
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

        public AircraftControlState Current { get; private set; } = AircraftControlState.Neutral(0.10f);
        public bool Paused { get; private set; }
        public bool DeterministicMode => deterministicMode;
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

        public bool CanAcceptTouchOverrides =>
            enabled &&
            gameObject.activeInHierarchy &&
            HasAuthoredSources;

        public int ActiveTouchOverrideCount =>
            (_touchYoke.active ? 1 : 0) +
            (_touchThrottle.active ? 1 : 0) +
            (_touchMixture.active ? 1 : 0);

        public void ConfigureAuthoredSources(
            GamepadInputReader authoredGamepad,
            DeterministicGamepadInputSource authoredDeterministicInput)
        {
            gamepad = authoredGamepad;
            deterministicInput = authoredDeterministicInput;
        }

        public void SetDeterministicMode(bool enabled) => deterministicMode = enabled;

        /// <summary>
        /// Begins an owner-checked override for one represented cockpit
        /// control. The current gamepad/deterministic value is retained as the
        /// underlying base and is restored immediately on release.
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

            state = default;
            RestoreBaseForChannel(channel);
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
                _touchYoke = default;
                RestoreBaseForChannel(
                    ProductTouchControlChannel.PilotYoke);
                released++;
            }
            if (_touchThrottle.active &&
                _touchThrottle.ownerToken == ownerToken)
            {
                _touchThrottle = default;
                RestoreBaseForChannel(
                    ProductTouchControlChannel.Throttle);
                released++;
            }
            if (_touchMixture.active &&
                _touchMixture.ownerToken == ownerToken)
            {
                _touchMixture = default;
                RestoreBaseForChannel(
                    ProductTouchControlChannel.Mixture);
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
            CaptureBaseControls(Current);
        }

        private void OnDisable()
        {
            ReleaseTouchOverridesOwnedBy(_touchYoke.ownerToken);
            ReleaseTouchOverridesOwnedBy(_touchThrottle.ownerToken);
            ReleaseTouchOverridesOwnedBy(_touchMixture.ownerToken);
        }

        private void Update()
        {
            bool useDeterministic = deterministicMode && deterministicInput != null && deterministicInput.isActiveAndEnabled;
            if (gamepad == null)
                _disconnectedFallback.SetDisconnected(Time.unscaledTime, 0f);
            GamepadInputSnapshot snapshot = useDeterministic
                ? deterministicInput.Current
                : gamepad != null ? gamepad.Current : _disconnectedFallback;

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
                // Right-stick Y is intentionally rate-based so it can share the stick with rudder X.
                _throttle = Mathf.Clamp01(_throttle + snapshot.rightStickY * throttleRatePerSecond * Time.unscaledDeltaTime);
                _trim = Mathf.Clamp(_trim + snapshot.dpadY * trimRatePerSecond * Time.unscaledDeltaTime, -1f, 1f);
            }

            bool flapDown = WasPressed(snapshot.buttonWest, _previousButtonWest);
            bool flapUp = WasPressed(snapshot.buttonNorth, _previousButtonNorth);
            bool reset = WasPressed(snapshot.buttonEast, _previousButtonEast);
            bool pause = WasPressed(snapshot.startButton, _previousStartButton);
            if (flapDown) _flaps = Mathf.Clamp01(_flaps + flapStep);
            if (flapUp) _flaps = Mathf.Clamp01(_flaps - flapStep);
            if (reset) _resetQueued = true;
            if (pause) Paused = !Paused;

            MapSnapshotInto(snapshot, invertElevator, _throttle, _trim, _flaps, _mixture, _carbHeat, Current);
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
                _touchYoke = default;
                RestoreBaseForChannel(
                    ProductTouchControlChannel.PilotYoke);
            }
            if (_touchThrottle.active &&
                unscaledTime -
                _touchThrottle.lastRefreshUnscaledTime >
                TouchOverrideLeaseTimeoutSeconds)
            {
                _touchThrottle = default;
                RestoreBaseForChannel(
                    ProductTouchControlChannel.Throttle);
            }
            if (_touchMixture.active &&
                unscaledTime -
                _touchMixture.lastRefreshUnscaledTime >
                TouchOverrideLeaseTimeoutSeconds)
            {
                _touchMixture = default;
                RestoreBaseForChannel(
                    ProductTouchControlChannel.Mixture);
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
