using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Aircraft
{
    /// <summary>
    /// Allocation-free presentation for the explicitly authored C172 propeller.
    /// The audited solid blade is used only while stopped; a fixed two-triangle
    /// disc represents exposure-integrated rotation without Quest refresh-rate
    /// aliasing or per-eye camera-facing behavior.
    /// </summary>
    [DefaultExecutionOrder(275)]
    [DisallowMultipleComponent]
    public sealed class ProductionPropellerPresentationController : MonoBehaviour
    {
        private static readonly int BlurStrengthId =
            Shader.PropertyToID("_BlurStrength");

        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField] private Renderer stoppedPropellerRenderer;
        [SerializeField] private Renderer runningBlurRenderer;
        [SerializeField, Min(0f)] private float stopRpm = 60f;
        [SerializeField, Min(0f)] private float startRpm = 150f;
        [SerializeField, Min(1f)] private float fullBlurRpm = 900f;

        private MaterialPropertyBlock _propertyBlock;
        private bool _running;
        private float _lastBlurStrength = -1f;

        public FlightDynamicsCoordinator Coordinator => coordinator;
        public Renderer StoppedPropellerRenderer => stoppedPropellerRenderer;
        public Renderer RunningBlurRenderer => runningBlurRenderer;
        public float StopRpm => stopRpm;
        public float StartRpm => startRpm;
        public float FullBlurRpm => fullBlurRpm;

        public void ConfigureAuthoredBindings(
            FlightDynamicsCoordinator authoredCoordinator,
            Renderer authoredStoppedPropeller,
            Renderer authoredRunningBlur,
            float authoredStopRpm,
            float authoredStartRpm,
            float authoredFullBlurRpm)
        {
            coordinator = authoredCoordinator;
            stoppedPropellerRenderer = authoredStoppedPropeller;
            runningBlurRenderer = authoredRunningBlur;
            stopRpm = authoredStopRpm;
            startRpm = authoredStartRpm;
            fullBlurRpm = authoredFullBlurRpm;
        }

        public bool HasAuthoredReferences(out string error)
        {
            if (coordinator == null ||
                stoppedPropellerRenderer == null ||
                runningBlurRenderer == null ||
                stoppedPropellerRenderer == runningBlurRenderer)
            {
                error = "Propeller presentation requires one serialized authority and two distinct authored renderers.";
                return false;
            }

            if (stopRpm < 0f || startRpm <= stopRpm ||
                fullBlurRpm <= startRpm)
            {
                error = "Propeller RPM thresholds must be finite and strictly ordered.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            _running = false;
            ApplyPresentation(0f, true);
        }

        private void LateUpdate()
        {
            double authoritativeRpm =
                coordinator != null && coordinator.IsInitialized
                    ? coordinator.CurrentState.engineRpm
                    : 0.0;
            float rpm = double.IsNaN(authoritativeRpm) ||
                        double.IsInfinity(authoritativeRpm)
                ? 0f
                : Mathf.Max(0f, (float)authoritativeRpm);

            if (_running)
            {
                if (rpm <= stopRpm)
                    _running = false;
            }
            else if (rpm >= startRpm)
            {
                _running = true;
            }

            ApplyPresentation(rpm, false);
        }

        private void ApplyPresentation(float rpm, bool force)
        {
            if (stoppedPropellerRenderer != null &&
                (force || stoppedPropellerRenderer.enabled == _running))
            {
                stoppedPropellerRenderer.enabled = !_running;
            }

            if (runningBlurRenderer == null)
                return;

            if (force || runningBlurRenderer.enabled != _running)
                runningBlurRenderer.enabled = _running;

            float strength = _running
                ? Mathf.InverseLerp(startRpm, fullBlurRpm, rpm)
                : 0f;
            // A running C172 prop remains perceptible through the windshield at
            // idle. Keep the exposure disc above the practical cockpit-glass
            // compositing threshold while retaining RPM-driven tip emphasis.
            strength = Mathf.Lerp(0.86f, 1f, strength);
            if (!force && Mathf.Abs(strength - _lastBlurStrength) < 0.01f)
                return;

            _lastBlurStrength = strength;
            _propertyBlock ??= new MaterialPropertyBlock();
            runningBlurRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(BlurStrengthId, strength);
            runningBlurRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
