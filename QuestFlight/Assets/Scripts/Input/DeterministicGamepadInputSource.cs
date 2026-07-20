using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Input
{
    public class DeterministicGamepadInputSource : MonoBehaviour
    {
        public bool OverrideLiveInput { get; set; } = true;
        public float Throttle { get; private set; } = 0.72f;
        public float Mixture { get; private set; } = 1f;
        public float CarbHeat { get; private set; }
        public float Trim { get; private set; }
        public float Flaps { get; private set; }
        public GamepadInputSnapshot Current { get; private set; } = ConnectedNeutral(0f);

        private readonly GamepadInputSnapshot _controlSnapshot = ConnectedNeutral(0f);
        private readonly AircraftControlState _neutralControls = AircraftControlState.Neutral();

        public void SetSnapshot(
            GamepadInputSnapshot snapshot,
            float throttle = 0.72f,
            float mixture = 1f,
            float carbHeat = 0f,
            float trim = 0f,
            float flaps = 0f)
        {
            Current = snapshot ?? ConnectedNeutral(Time.unscaledTime);
            Current.connected = true;
            Current.timestamp = Time.unscaledTime;
            Throttle = Mathf.Clamp01(throttle);
            Mixture = Mathf.Clamp01(mixture);
            CarbHeat = Mathf.Clamp01(carbHeat);
            Trim = Mathf.Clamp(trim, -1f, 1f);
            Flaps = Mathf.Clamp01(flaps);
        }

        public void SetControls(AircraftControlState controls)
        {
            if (controls == null)
            {
                _neutralControls.SetNeutral();
                controls = _neutralControls;
            }

            GamepadInputSnapshot snapshot = _controlSnapshot;
            snapshot.connected = true;
            snapshot.deviceName = "DeterministicGamepad";
            snapshot.displayName = "Deterministic Gamepad";
            snapshot.layout = "QuestFlightLabDeterministic";
            snapshot.manufacturer = string.Empty;
            snapshot.product = string.Empty;
            snapshot.interfaceName = "TestHarness";
            snapshot.deviceId = -9001;
            snapshot.timestamp = Time.unscaledTime;
            snapshot.sampleRateHz = 72f;
            snapshot.secondsSinceLastInput = 0f;
            snapshot.leftStickX = controls.aileron;
            snapshot.leftStickY = -controls.elevator;
            snapshot.rightStickX = controls.rudder;
            snapshot.rightStickY = 0f;
            snapshot.leftTrigger = controls.leftToeBrake;
            snapshot.rightTrigger = controls.rightToeBrake;
            snapshot.dpadX = 0f;
            snapshot.dpadY = 0f;
            snapshot.buttonSouth = false;
            snapshot.buttonEast = false;
            snapshot.buttonWest = false;
            snapshot.buttonNorth = false;
            snapshot.leftShoulder = false;
            snapshot.rightShoulder = false;
            snapshot.startButton = false;
            snapshot.selectButton = false;
            snapshot.leftStickButton = false;
            snapshot.rightStickButton = false;
            Current = snapshot;
            Throttle = Mathf.Clamp01(controls.throttle);
            Mixture = Mathf.Clamp01(controls.mixture);
            CarbHeat = Mathf.Clamp01(controls.carbHeat);
            Trim = Mathf.Clamp(controls.trim, -1f, 1f);
            Flaps = Mathf.Clamp01(controls.flaps);
        }

        public static GamepadInputSnapshot ConnectedNeutral(float timestamp)
        {
            return new GamepadInputSnapshot
            {
                connected = true,
                deviceName = "DeterministicGamepad",
                displayName = "Deterministic Gamepad",
                layout = "QuestFlightLabDeterministic",
                interfaceName = "TestHarness",
                deviceId = -9001,
                timestamp = timestamp,
                sampleRateHz = 72f
            };
        }
    }
}
