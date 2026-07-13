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

        private GamepadInputSnapshot _previous;
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
            GamepadInputSnapshot snapshot = useDeterministic
                ? deterministicInput.Current
                : gamepad != null ? gamepad.Current : GamepadInputSnapshot.Disconnected(Time.unscaledTime, 0f);

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

            bool flapDown = WasPressed(snapshot.buttonWest, _previous?.buttonWest ?? false);
            bool flapUp = WasPressed(snapshot.buttonNorth, _previous?.buttonNorth ?? false);
            bool reset = WasPressed(snapshot.buttonEast, _previous?.buttonEast ?? false);
            bool pause = WasPressed(snapshot.startButton, _previous?.startButton ?? false);
            if (flapDown) _flaps = Mathf.Clamp01(_flaps + flapStep);
            if (flapUp) _flaps = Mathf.Clamp01(_flaps - flapStep);
            if (reset) _resetQueued = true;
            if (pause) Paused = !Paused;

            Current = MapSnapshot(snapshot, invertElevator, _throttle, _trim, _flaps, _mixture, _carbHeat);
            Current.resetPressed = reset;
            Current.pausePressed = pause;
            _previous = snapshot;
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
            if (snapshot == null) return AircraftControlState.Neutral(throttle);
            return new AircraftControlState
            {
                aileron = Mathf.Clamp(snapshot.leftStickX, -1f, 1f),
                elevator = Mathf.Clamp(invertElevator ? -snapshot.leftStickY : snapshot.leftStickY, -1f, 1f),
                rudder = Mathf.Clamp(snapshot.rightStickX, -1f, 1f),
                throttle = Mathf.Clamp01(throttle),
                mixture = Mathf.Clamp01(mixture),
                carbHeat = Mathf.Clamp01(carbHeat),
                trim = Mathf.Clamp(trim, -1f, 1f),
                flaps = Mathf.Clamp01(flaps),
                leftToeBrake = Mathf.Clamp01(snapshot.leftTrigger),
                rightToeBrake = Mathf.Clamp01(snapshot.rightTrigger)
            };
        }

        private static bool WasPressed(bool current, bool previous) => current && !previous;
    }
}
