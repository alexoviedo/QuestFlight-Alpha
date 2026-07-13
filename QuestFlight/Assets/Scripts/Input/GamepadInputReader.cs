using QuestFlightLab.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace QuestFlightLab.Input
{
    public class GamepadInputReader : MonoBehaviour
    {
        public bool preferMostRecentGamepad = true;
        public GamepadInputSnapshot Current { get; private set; } = GamepadInputSnapshot.Disconnected(0f, 0f);
        public string LastDeviceChange { get; private set; } = "none";
        public float LastInputTimestamp { get; private set; }

        private Gamepad _activeGamepad;
        private GamepadInputSnapshot _lastSnapshot;
        private float _sampleWindowStart;
        private int _sampleWindowCount;
        private float _sampleRateHz;

        private void OnEnable()
        {
            InputSystem.onDeviceChange += HandleDeviceChange;
            InputSystem.onEvent += HandleInputEvent;
            SelectActiveGamepad();
            LastInputTimestamp = Time.unscaledTime;
            _sampleWindowStart = Time.unscaledTime;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= HandleDeviceChange;
            InputSystem.onEvent -= HandleInputEvent;
        }

        private void Update()
        {
            SelectActiveGamepad();
            Current = _activeGamepad == null
                ? GamepadInputSnapshot.Disconnected(Time.unscaledTime, _sampleRateHz)
                : ReadSnapshot(_activeGamepad);

            if (Current.HasDifferentInput(_lastSnapshot))
            {
                LastInputTimestamp = Time.unscaledTime;
            }

            Current.secondsSinceLastInput = Time.unscaledTime - LastInputTimestamp;
            _lastSnapshot = CloneForComparison(Current);
            UpdateSampleRate();
        }

        private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            LastDeviceChange = $"{Time.unscaledTime:F2}s {change}: {device.displayName} ({device.layout})";
            if (device is Gamepad)
            {
                SelectActiveGamepad(force: true);
            }
        }

        private void HandleInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device is Gamepad gamepad && (preferMostRecentGamepad || _activeGamepad == null))
            {
                _activeGamepad = gamepad;
            }
        }

        private void SelectActiveGamepad(bool force = false)
        {
            if (!force && _activeGamepad != null && _activeGamepad.added)
            {
                return;
            }

            if (preferMostRecentGamepad && Gamepad.current != null)
            {
                _activeGamepad = Gamepad.current;
                return;
            }

            ReadOnlyArray<Gamepad> all = Gamepad.all;
            _activeGamepad = all.Count > 0 ? all[0] : null;
        }

        private GamepadInputSnapshot ReadSnapshot(Gamepad gamepad)
        {
            Vector2 left = gamepad.leftStick.ReadValue();
            Vector2 right = gamepad.rightStick.ReadValue();
            Vector2 dpad = gamepad.dpad.ReadValue();

            return new GamepadInputSnapshot
            {
                connected = true,
                deviceName = gamepad.name ?? "",
                displayName = gamepad.displayName ?? "",
                layout = gamepad.layout ?? "",
                manufacturer = gamepad.description.manufacturer ?? "",
                product = gamepad.description.product ?? "",
                interfaceName = gamepad.description.interfaceName ?? "",
                deviceId = gamepad.deviceId,
                timestamp = Time.unscaledTime,
                sampleRateHz = _sampleRateHz,
                secondsSinceLastInput = Time.unscaledTime - LastInputTimestamp,
                leftStickX = left.x,
                leftStickY = left.y,
                rightStickX = right.x,
                rightStickY = right.y,
                leftTrigger = gamepad.leftTrigger.ReadValue(),
                rightTrigger = gamepad.rightTrigger.ReadValue(),
                dpadX = dpad.x,
                dpadY = dpad.y,
                buttonSouth = gamepad.buttonSouth.isPressed,
                buttonEast = gamepad.buttonEast.isPressed,
                buttonWest = gamepad.buttonWest.isPressed,
                buttonNorth = gamepad.buttonNorth.isPressed,
                leftShoulder = gamepad.leftShoulder.isPressed,
                rightShoulder = gamepad.rightShoulder.isPressed,
                startButton = gamepad.startButton.isPressed,
                selectButton = gamepad.selectButton.isPressed,
                leftStickButton = gamepad.leftStickButton.isPressed,
                rightStickButton = gamepad.rightStickButton.isPressed
            };
        }

        private void UpdateSampleRate()
        {
            _sampleWindowCount++;
            float elapsed = Time.unscaledTime - _sampleWindowStart;
            if (elapsed < 1f) return;

            _sampleRateHz = _sampleWindowCount / Mathf.Max(0.001f, elapsed);
            _sampleWindowCount = 0;
            _sampleWindowStart = Time.unscaledTime;
        }

        private static GamepadInputSnapshot CloneForComparison(GamepadInputSnapshot source)
        {
            if (source == null) return null;
            return new GamepadInputSnapshot
            {
                connected = source.connected,
                deviceId = source.deviceId,
                leftStickX = source.leftStickX,
                leftStickY = source.leftStickY,
                rightStickX = source.rightStickX,
                rightStickY = source.rightStickY,
                leftTrigger = source.leftTrigger,
                rightTrigger = source.rightTrigger,
                dpadX = source.dpadX,
                dpadY = source.dpadY,
                buttonSouth = source.buttonSouth,
                buttonEast = source.buttonEast,
                buttonWest = source.buttonWest,
                buttonNorth = source.buttonNorth,
                leftShoulder = source.leftShoulder,
                rightShoulder = source.rightShoulder,
                startButton = source.startButton,
                selectButton = source.selectButton,
                leftStickButton = source.leftStickButton,
                rightStickButton = source.rightStickButton
            };
        }
    }
}
