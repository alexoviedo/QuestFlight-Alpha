using System;
using System.Collections.Generic;
using QuestFlightLab.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace QuestFlightLab.Input
{
    /// <summary>
    /// Reads standard Gamepads unchanged and, when explicitly enabled, maps a
    /// six-axis Android controller into flight-control semantics. The reader is
    /// still the sole live-device source consumed by ProductControlInput.
    /// </summary>
    public class GamepadInputReader : MonoBehaviour
    {
        public bool preferMostRecentGamepad = true;
        [SerializeField] private bool externalFlightControllersEnabled = true;
        [SerializeField] private ProductExternalControllerMapping externalMapping =
            new ProductExternalControllerMapping();

        public GamepadInputSnapshot Current { get; private set; } =
            GamepadInputSnapshot.Disconnected(0f, 0f);
        public string LastDeviceChange { get; private set; } = "none";
        public float LastInputTimestamp { get; private set; }
        public bool ExternalFlightControllersEnabled =>
            externalFlightControllersEnabled;
        public ProductExternalControllerMapping ExternalMapping => externalMapping;
        public bool HasValidExternalMapping =>
            externalMapping != null && externalMapping.IsValid(out _);
        public bool UsingExternalFlightController =>
            _activeGamepad != null &&
            externalFlightControllersEnabled &&
            IsSixAxis(_activeGamepad);

        private Gamepad _activeGamepad;
        private readonly GamepadInputSnapshot _lastSnapshot =
            GamepadInputSnapshot.Disconnected(0f, 0f);
        private float _sampleWindowStart;
        private int _sampleWindowCount;
        private float _sampleRateHz;
        private bool _hasExternalPositions;
        private float _previousThrottlePosition;
        private float _previousMixturePosition;
        private float _previousFlapsPosition;
        private readonly Dictionary<int, bool> _sixAxisByDeviceId =
            new Dictionary<int, bool>();

        [Serializable]
        private sealed class AndroidControllerCapabilities
        {
            public int[] motionAxes;
        }

        public void ConfigureExternalFlightControllers(
            bool enabled,
            ProductExternalControllerMapping mapping)
        {
            externalFlightControllersEnabled = enabled;
            externalMapping = mapping ??
                ProductExternalControllerMapping.SixAxisFlightDefault();
            if (isActiveAndEnabled)
                SelectActiveGamepad(force: true);
        }

        private void OnEnable()
        {
            ProductExternalControllerLayout.EnsureRegistered();
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
            SampleInput();
        }

        public void SampleInput()
        {
            SelectActiveGamepad();
            if (_activeGamepad == null)
            {
                Current.SetDisconnected(Time.unscaledTime, _sampleRateHz);
                _hasExternalPositions = false;
            }
            else if (externalFlightControllersEnabled &&
                     IsSixAxis(_activeGamepad))
            {
                ReadExternalFlightSnapshot(_activeGamepad, Current);
            }
            else
            {
                ReadStandardGamepadSnapshot(_activeGamepad, Current);
                _hasExternalPositions = false;
            }

            if (Current.HasDifferentInput(_lastSnapshot))
                LastInputTimestamp = Time.unscaledTime;

            Current.secondsSinceLastInput =
                Time.unscaledTime - LastInputTimestamp;
            _lastSnapshot.CopyInputFrom(Current);
            UpdateSampleRate();
        }

        private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            LastDeviceChange =
                $"{Time.unscaledTime:F2}s {change}: {device.displayName} ({device.layout})";
            _sixAxisByDeviceId.Remove(device.deviceId);
            if (device is Gamepad)
                SelectActiveGamepad(force: true);
        }

        private void HandleInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!(device is Gamepad gamepad) || !CanUse(gamepad))
                return;
            if (preferMostRecentGamepad || _activeGamepad == null)
                SetActiveGamepad(gamepad);
        }

        private void SelectActiveGamepad(bool force = false)
        {
            if (!force && _activeGamepad != null &&
                _activeGamepad.added && CanUse(_activeGamepad))
                return;

            if (preferMostRecentGamepad &&
                Gamepad.current != null && CanUse(Gamepad.current))
            {
                SetActiveGamepad(Gamepad.current);
                return;
            }

            ReadOnlyArray<Gamepad> all = Gamepad.all;
            for (int i = 0; i < all.Count; i++)
            {
                if (!CanUse(all[i]))
                    continue;
                SetActiveGamepad(all[i]);
                return;
            }

            SetActiveGamepad(null);
        }

        private bool CanUse(Gamepad gamepad)
        {
            if (gamepad == null)
                return false;
            return externalFlightControllersEnabled ||
                   !IsSixAxis(gamepad);
        }

        private void SetActiveGamepad(Gamepad gamepad)
        {
            if (_activeGamepad == gamepad)
                return;
            _activeGamepad = gamepad;
            _hasExternalPositions = false;
        }

        private void ReadStandardGamepadSnapshot(
            Gamepad gamepad,
            GamepadInputSnapshot snapshot)
        {
            Vector2 left = gamepad.leftStick.ReadValue();
            Vector2 right = gamepad.rightStick.ReadValue();
            Vector2 dpad = gamepad.dpad.ReadValue();

            PopulateIdentity(gamepad, snapshot);
            snapshot.leftStickX = left.x;
            snapshot.leftStickY = left.y;
            snapshot.rightStickX = right.x;
            snapshot.rightStickY = right.y;
            snapshot.leftTrigger = gamepad.leftTrigger.ReadValue();
            snapshot.rightTrigger = gamepad.rightTrigger.ReadValue();
            snapshot.dpadX = dpad.x;
            snapshot.dpadY = dpad.y;
            PopulateButtons(gamepad, snapshot);
            ClearPositionControls(snapshot);
        }

        private void ReadExternalFlightSnapshot(
            Gamepad gamepad,
            GamepadInputSnapshot snapshot)
        {
            string mappingError = "mapping reference is missing";
            if (externalMapping == null ||
                !externalMapping.IsValid(out mappingError))
            {
                snapshot.SetDisconnected(Time.unscaledTime, _sampleRateHz);
                LastDeviceChange = "invalid external-controller mapping: " + mappingError;
                return;
            }

            PopulateIdentity(gamepad, snapshot);
            snapshot.externalFlightController = true;
            snapshot.leftStickX = ApplySignedMapping(
                ReadMappedAxis(gamepad, externalMapping.aileronAxis),
                externalMapping.invertAileron,
                externalMapping.primaryAxisDeadzone);
            snapshot.leftStickY = ApplySignedMapping(
                ReadMappedAxis(gamepad, externalMapping.elevatorAxis),
                externalMapping.invertElevator,
                externalMapping.primaryAxisDeadzone);
            snapshot.rightStickX = ApplySignedMapping(
                ReadMappedAxis(gamepad, externalMapping.rudderAxis),
                externalMapping.invertRudder,
                externalMapping.primaryAxisDeadzone);
            snapshot.rightStickY = 0f;
            snapshot.leftTrigger = ToUnitPosition(
                ReadMappedAxis(gamepad, externalMapping.leftToeBrakeAxis),
                externalMapping.invertLeftToeBrake);
            snapshot.rightTrigger = ToUnitPosition(
                ReadMappedAxis(gamepad, externalMapping.rightToeBrakeAxis),
                externalMapping.invertRightToeBrake);

            Vector2 dpad = gamepad.dpad.ReadValue();
            snapshot.dpadX = dpad.x;
            snapshot.dpadY = dpad.y;
            PopulateButtons(gamepad, snapshot);

            float throttle = ToUnitPosition(
                ReadMappedAxis(gamepad, externalMapping.throttleAxis),
                externalMapping.invertThrottle);
            snapshot.hasThrottlePosition = true;
            snapshot.throttlePosition = throttle;
            snapshot.throttlePositionChanged =
                _hasExternalPositions &&
                Mathf.Abs(throttle - _previousThrottlePosition) >=
                externalMapping.positionChangeEpsilon;

            snapshot.hasMixturePosition =
                externalMapping.mixtureAxis != ProductExternalControllerAxis.None;
            snapshot.mixturePosition = snapshot.hasMixturePosition
                ? ToUnitPosition(
                    ReadMappedAxis(gamepad, externalMapping.mixtureAxis),
                    externalMapping.invertMixture)
                : 0f;
            snapshot.mixturePositionChanged =
                snapshot.hasMixturePosition && _hasExternalPositions &&
                Mathf.Abs(snapshot.mixturePosition - _previousMixturePosition) >=
                externalMapping.positionChangeEpsilon;

            snapshot.hasFlapsPosition =
                externalMapping.flapsAxis != ProductExternalControllerAxis.None;
            snapshot.flapsPosition = snapshot.hasFlapsPosition
                ? ToUnitPosition(
                    ReadMappedAxis(gamepad, externalMapping.flapsAxis),
                    externalMapping.invertFlaps)
                : 0f;
            snapshot.flapsPositionChanged =
                snapshot.hasFlapsPosition && _hasExternalPositions &&
                Mathf.Abs(snapshot.flapsPosition - _previousFlapsPosition) >=
                externalMapping.positionChangeEpsilon;

            _previousThrottlePosition = snapshot.throttlePosition;
            _previousMixturePosition = snapshot.mixturePosition;
            _previousFlapsPosition = snapshot.flapsPosition;
            _hasExternalPositions = true;
        }

        private void PopulateIdentity(
            Gamepad gamepad,
            GamepadInputSnapshot snapshot)
        {
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
            snapshot.secondsSinceLastInput =
                Time.unscaledTime - LastInputTimestamp;
        }

        private static void PopulateButtons(
            Gamepad gamepad,
            GamepadInputSnapshot snapshot)
        {
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

        private static void ClearPositionControls(GamepadInputSnapshot snapshot)
        {
            snapshot.externalFlightController = false;
            snapshot.hasThrottlePosition = false;
            snapshot.throttlePosition = 0f;
            snapshot.throttlePositionChanged = false;
            snapshot.hasMixturePosition = false;
            snapshot.mixturePosition = 0f;
            snapshot.mixturePositionChanged = false;
            snapshot.hasFlapsPosition = false;
            snapshot.flapsPosition = 0f;
            snapshot.flapsPositionChanged = false;
        }

        public static bool IsSixAxisFlightController(InputDevice device)
        {
            if (!(device is Gamepad) ||
                !string.Equals(device.description.interfaceName, "Android",
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(device.description.capabilities))
                return false;

            AndroidControllerCapabilities capabilities;
            try
            {
                capabilities = JsonUtility.FromJson<AndroidControllerCapabilities>(
                    device.description.capabilities);
            }
            catch (ArgumentException)
            {
                return false;
            }

            int[] axes = capabilities?.motionAxes;
            if (axes == null)
                return false;
            return ContainsAxis(axes, 0) &&
                   ContainsAxis(axes, 1) &&
                   ContainsAxis(axes, 11) &&
                   ContainsAxis(axes, 12) &&
                   ContainsAxis(axes, 13) &&
                   ContainsAxis(axes, 14) &&
                   FindAxisControl(device,
                       ProductExternalControllerLayout.AileronAxisControl) != null &&
                   FindAxisControl(device,
                       ProductExternalControllerLayout.ElevatorAxisControl) != null &&
                   FindAxisControl(device,
                       ProductExternalControllerLayout.ThrottleAxisControl) != null &&
                   FindAxisControl(device,
                       ProductExternalControllerLayout.RudderAxisControl) != null &&
                   FindAxisControl(device,
                       ProductExternalControllerLayout.LeftToeBrakeAxisControl) != null &&
                   FindAxisControl(device,
                       ProductExternalControllerLayout.RightToeBrakeAxisControl) != null;
        }

        private bool IsSixAxis(InputDevice device)
        {
            if (device == null)
                return false;
            if (_sixAxisByDeviceId.TryGetValue(device.deviceId, out bool cached))
                return cached;
            bool result = IsSixAxisFlightController(device);
            _sixAxisByDeviceId[device.deviceId] = result;
            return result;
        }

        private static bool ContainsAxis(int[] axes, int expected)
        {
            for (int i = 0; i < axes.Length; i++)
                if (axes[i] == expected)
                    return true;
            return false;
        }

        private static float ReadMappedAxis(
            InputDevice device,
            ProductExternalControllerAxis axis)
        {
            string controlName;
            switch (axis)
            {
                case ProductExternalControllerAxis.X:
                    controlName = ProductExternalControllerLayout.AileronAxisControl;
                    break;
                case ProductExternalControllerAxis.Y:
                    controlName = ProductExternalControllerLayout.ElevatorAxisControl;
                    break;
                case ProductExternalControllerAxis.Z:
                    controlName = ProductExternalControllerLayout.ThrottleAxisControl;
                    break;
                case ProductExternalControllerAxis.Rx:
                    controlName = ProductExternalControllerLayout.RudderAxisControl;
                    break;
                case ProductExternalControllerAxis.Ry:
                    controlName = ProductExternalControllerLayout.LeftToeBrakeAxisControl;
                    break;
                case ProductExternalControllerAxis.Rz:
                    controlName = ProductExternalControllerLayout.RightToeBrakeAxisControl;
                    break;
                default:
                    return 0f;
            }

            AxisControl control = FindAxisControl(device, controlName);
            return control != null ? Mathf.Clamp(control.ReadValue(), -1f, 1f) : 0f;
        }

        private static AxisControl FindAxisControl(
            InputDevice device,
            string controlName) =>
            device != null
                ? device.TryGetChildControl<AxisControl>(controlName)
                : null;

        private static float ApplySignedMapping(
            float value,
            bool invert,
            float deadzone)
        {
            float mapped = invert ? -value : value;
            return Mathf.Abs(mapped) < Mathf.Clamp(deadzone, 0f, 0.25f)
                ? 0f
                : Mathf.Clamp(mapped, -1f, 1f);
        }

        private static float ToUnitPosition(float value, bool invert)
        {
            float mapped = invert ? -value : value;
            return Mathf.Clamp01((mapped + 1f) * 0.5f);
        }

        private void UpdateSampleRate()
        {
            _sampleWindowCount++;
            float elapsed = Time.unscaledTime - _sampleWindowStart;
            if (elapsed < 1f)
                return;

            _sampleRateHz = _sampleWindowCount / Mathf.Max(0.001f, elapsed);
            _sampleWindowCount = 0;
            _sampleWindowStart = Time.unscaledTime;
        }
    }
}
