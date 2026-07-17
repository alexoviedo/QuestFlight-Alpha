using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>
    /// Minimal deterministic acceptance loop. It supplies controls through the same product input
    /// boundary as a gamepad and never writes aircraft, camera, seat, or XR transforms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductDemoPilot : MonoBehaviour
    {
        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private DeterministicGamepadInputSource deterministicInput;
        [SerializeField] private bool autoStartDemo;
        [SerializeField, Min(30f)] private float loopDurationSeconds = 72f;

        public bool IsRunning { get; private set; }
        public string Phase { get; private set; } = "ready";
        public ProductControlInput AuthoredProductInput => productInput;
        public DeterministicGamepadInputSource AuthoredDeterministicInput => deterministicInput;
        public bool HasAuthoredSources => productInput != null && deterministicInput != null;
        public bool AutoStartDemo => autoStartDemo;

        private float _startedAt;
        private readonly AircraftControlState _scheduledControls = AircraftControlState.Neutral(0.10f);

        public void ConfigureAuthoredSources(
            ProductControlInput authoredProductInput,
            DeterministicGamepadInputSource authoredDeterministicInput)
        {
            productInput = authoredProductInput;
            deterministicInput = authoredDeterministicInput;
        }

        public void ConfigureAuthoredAutoStart(bool enabled) => autoStartDemo = enabled;

        private void Start()
        {
            if (autoStartDemo) StartDemo();
        }

        public void StartDemo()
        {
            if (productInput == null || deterministicInput == null)
            {
                Debug.LogError("[QuestFlight][Demo] Authored product input references are missing.", this);
                return;
            }
            _startedAt = Time.unscaledTime;
            IsRunning = true;
            Phase = "idle_brake_hold";
            productInput.SetDeterministicMode(true);
        }

        public void StopDemo()
        {
            IsRunning = false;
            Phase = "ready";
            deterministicInput?.SetControls(AircraftControlState.Neutral(0.10f));
            productInput?.SetDeterministicMode(false);
        }

        private void Update()
        {
            if (!IsRunning || deterministicInput == null) return;
            float time = Mathf.Repeat(Time.unscaledTime - _startedAt, Mathf.Max(30f, loopDurationSeconds));
            ScheduledControlsInto(time, out string phase, _scheduledControls);
            Phase = phase;
            deterministicInput.SetControls(_scheduledControls);
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
                controls.rudder = Mathf.Sin(seconds * 0.45f) * 0.08f;
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
            }
            else if (seconds < 37f)
            {
                phase = "rotation";
                controls.throttle = 1f;
                controls.elevator = 0.32f;
                controls.trim = 0.08f;
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
                controls.aileron = -0.16f;
                controls.rudder = -0.08f;
                controls.elevator = 0.05f;
                controls.trim = 0.08f;
            }
            else
            {
                phase = "climb_stabilize";
                controls.throttle = 0.88f;
                controls.elevator = 0.04f;
                controls.trim = 0.08f;
            }
        }
    }
}
