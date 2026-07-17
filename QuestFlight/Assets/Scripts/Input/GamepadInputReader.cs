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
        private readonly GamepadInputSnapshot _lastSnapshot = GamepadInputSnapshot.Disconnected(0f, 0f);
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
            if (_activeGamepad == null)
                Current.SetDisconnected(Time.unscaledTime, _sampleRateHz);
            else
                ReadSnapshot(_activeGamepad, Current);

            if (Current.HasDifferentInput(_lastSnapshot))
            {
                LastInputTimestamp = Time.unscaledTime;
            }

            Current.secondsSinceLastInput = Time.unscaledTime - LastInputTimestamp;
            _lastSnapshot.CopyInputFrom(Current);
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

        private void ReadSnapshot(Gamepad gamepad, GamepadInputSnapshot snapshot)
        {
            Vector2 left = gamepad.leftStick.ReadValue();
            Vector2 right = gamepad.rightStick.ReadValue();
            Vector2 dpad = gamepad.dpad.ReadValue();

            snapshot.connected = true;
            snapshot.deviceName = gamepad.name ?? string.Empty;
            snapshot.displayName = gamepad.displayName ?? string.Empty;
            snapshot.layout = gamepad.layout ?? string.Empty;
            snapshot.manufacturer = gamepad.description.manufacturer ?? string.Empty;
            snapshot.product = gamepad.description.product ?? string.Empty;
            snapshot.interfaceName = gamepad.description.interfaceName ?? string.Empty;
            snapshot.deviceId = gamepad.deviceId;
            snapshot.timestamp = Time.unscaledTime;
            snapshot.sampleRateHz = _sampleRateHz;
            snapshot.secondsSinceLastInput = Time.unscaledTime - LastInputTimestamp;
            snapshot.leftStickX = left.x;
            snapshot.leftStickY = left.y;
            snapshot.rightStickX = right.x;
            snapshot.rightStickY = right.y;
            snapshot.leftTrigger = gamepad.leftTrigger.ReadValue();
            snapshot.rightTrigger = gamepad.rightTrigger.ReadValue();
            snapshot.dpadX = dpad.x;
            snapshot.dpadY = dpad.y;
            snapshot.buttonSouth = gamepad.buttonSouth.isPressed;
            snapshot.buttonEast = gamepad.buttonEast.isPressed;
            snapshot.buttonWest = gamepad.buttonWest.isPressed;
            snapshot.buttonNorth = gamepad.buttonNorth.isPressed;
            snapshot.leftShoulder = gamepad.leftShoulder.isPressed;
            snapshot.rightShoulder = gamepad.rightShoulder.isPressed;
            snapshot.startButton = gamepad.startButton.isPressed;
            snapshot.selectButton = gamepad.selectButton.isPressed;
            snapshot.leftStickButton = gamepad.leftStickButton.isPressed;
            snapshot.rightStickButton = gamepad.rightStickButton.isPressed;
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

    }
}
