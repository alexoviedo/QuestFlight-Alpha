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
            controls ??= AircraftControlState.Neutral();
            SetSnapshot(new GamepadInputSnapshot
            {
                connected = true,
                deviceName = "DeterministicGamepad",
                displayName = "Deterministic Gamepad",
                layout = "QuestFlightLabDeterministic",
                interfaceName = "TestHarness",
                deviceId = -9001,
                leftStickX = controls.aileron,
                leftStickY = -controls.elevator,
                rightStickX = controls.rudder,
                leftTrigger = controls.leftToeBrake,
                rightTrigger = controls.rightToeBrake
            }, controls.throttle, controls.mixture, controls.carbHeat, controls.trim, controls.flaps);
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
