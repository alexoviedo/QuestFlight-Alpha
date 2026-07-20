using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Input
{
    /// <summary>
    /// Product gamepad/deterministic-demo mapping. It has no USB2BLE or HOTAS discovery path and
    /// never changes scene hierarchy. Deterministic input is selected only by an explicit flag.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductControlInput : MonoBehaviour
    {
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

        public void ConfigureAuthoredSources(
            GamepadInputReader authoredGamepad,
            DeterministicGamepadInputSource authoredDeterministicInput)
        {
            gamepad = authoredGamepad;
            deterministicInput = authoredDeterministicInput;
        }

        public void SetDeterministicMode(bool enabled) => deterministicMode = enabled;

        private void Awake()
        {
            if (!HasAuthoredSources)
            {
                Debug.LogError("[QuestFlight][Input] Serialized gamepad and deterministic input references are required.", this);
                enabled = false;
                return;
            }
            _throttle = Mathf.Clamp01(defaultThrottle);
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

        private static bool WasPressed(bool current, bool previous) => current && !previous;
    }
}
