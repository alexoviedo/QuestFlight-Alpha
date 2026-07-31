using System;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.Presentation
{
    /// <summary>
    /// Allocation-free, single-loop presentation of positively identified,
    /// serialized cockpit controls and analog indications. This component never
    /// creates or discovers product hierarchy objects and never writes a camera.
    /// </summary>
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class ProductionCockpitDynamicsController : MonoBehaviour
    {
        private const float MetersToFeet = 3.280839895f;
        private const float MinimumRange = 0.000001f;
        private const float FullCircleDegrees = 360f;
        private const float NoseSteeringFullAuthorityKnots = 5f;
        private const float NoseSteeringZeroAuthorityKnots = 35f;

        public enum InputRemapMode
        {
            Clamp = 0,
            Wrap = 1,
            Unclamped = 2
        }

        [Serializable]
        public sealed class MotionTerm
        {
            [Tooltip("Human-readable identity from the authored cockpit-control manifest.")]
            public string label;
            public CockpitPresentationChannel channel;
            public InputRemapMode inputRemap = InputRemapMode.Clamp;
            public float inputMinimum;
            public float inputMaximum = 1f;
            public bool invert;

            [Tooltip("Zero applies the sampled value immediately. Positive values use an exponential response.")]
            [Min(0f)]
            public float responseRatePerSecond;

            [Tooltip("Optional normalized response curve for non-linear instrument faces.")]
            public bool useResponseCurve;
            public AnimationCurve responseCurve;

            public Vector3 localRotationAxis;
            public float minimumAngleDegrees;
            public float maximumAngleDegrees;
            public Vector3 localTranslationAxis;
            public float minimumTranslationMeters;
            public float maximumTranslationMeters;

            [NonSerialized] internal Vector3 normalizedRotationAxis;
            [NonSerialized] internal Vector3 normalizedTranslationAxis;
            [NonSerialized] internal float filteredNormalizedValue;
            [NonSerialized] internal bool hasFilteredValue;

            internal void CacheRuntimeData()
            {
                normalizedRotationAxis = localRotationAxis.sqrMagnitude > MinimumRange
                    ? localRotationAxis.normalized
                    : Vector3.zero;
                normalizedTranslationAxis = localTranslationAxis.sqrMagnitude > MinimumRange
                    ? localTranslationAxis.normalized
                    : Vector3.zero;
                filteredNormalizedValue = 0f;
                hasFilteredValue = false;
            }

            internal float EvaluateNormalized(float rawValue, float deltaTime)
            {
                float target = Remap(rawValue);
                if (!hasFilteredValue)
                {
                    filteredNormalizedValue = target;
                    hasFilteredValue = true;
                }
                else if (responseRatePerSecond <= 0f)
                {
                    filteredNormalizedValue = target;
                }
                else if (deltaTime > 0f)
                {
                    float alpha = 1f - Mathf.Exp(-responseRatePerSecond * deltaTime);
                    if (inputRemap == InputRemapMode.Wrap)
                    {
                        float delta = Mathf.DeltaAngle(
                            filteredNormalizedValue * FullCircleDegrees,
                            target * FullCircleDegrees) / FullCircleDegrees;
                        filteredNormalizedValue = Mathf.Repeat(
                            filteredNormalizedValue + delta * alpha, 1f);
                    }
                    else
                    {
                        filteredNormalizedValue = Mathf.LerpUnclamped(
                            filteredNormalizedValue, target, alpha);
                    }
                }

                float value = invert ? 1f - filteredNormalizedValue : filteredNormalizedValue;
                if (useResponseCurve && responseCurve != null && responseCurve.length > 0)
                    value = responseCurve.Evaluate(value);
                return value;
            }

            private float Remap(float rawValue)
            {
                float range = inputMaximum - inputMinimum;
                if (Mathf.Abs(range) < MinimumRange) return 0f;
                switch (inputRemap)
                {
                    case InputRemapMode.Wrap:
                        return Mathf.Repeat(rawValue - inputMinimum, Mathf.Abs(range)) /
                               Mathf.Abs(range);
                    case InputRemapMode.Unclamped:
                        return (rawValue - inputMinimum) / range;
                    default:
                        return Mathf.InverseLerp(inputMinimum, inputMaximum, rawValue);
                }
            }
        }

        [Serializable]
        public sealed class TransformBinding
        {
            [Tooltip("Stable identity from the authored cockpit-control manifest.")]
            public string label;
            public Transform target;
            public MotionTerm[] motions;

            [NonSerialized] internal Vector3 restLocalPosition;
            [NonSerialized] internal Quaternion restLocalRotation;
            [NonSerialized] internal bool hasRestPose;

            internal void CaptureRestPose()
            {
                if (target == null) return;
                restLocalPosition = target.localPosition;
                restLocalRotation = target.localRotation;
                hasRestPose = true;
                if (motions == null) return;
                for (int i = 0; i < motions.Length; i++)
                    motions[i]?.CacheRuntimeData();
            }
        }

        [Header("Authored authority references")]
        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [Tooltip("Optional serialized component implementing ICockpitPresentationStateProvider.")]
        [SerializeField] private MonoBehaviour authoritativeStateProvider;

        [Header("Positively identified model bindings")]
        [SerializeField] private TransformBinding[] bindings;

        private ICockpitPresentationStateProvider _stateProvider;
        private FlightDynamicsState _testState;
        private FlightDynamicsControlSnapshot _testControls;
        private bool _useTestSnapshots;

        public int BindingCount => bindings?.Length ?? 0;
        public bool UsesTestSnapshots => _useTestSnapshots;
        public FlightDynamicsCoordinator Coordinator => coordinator;
        public MonoBehaviour AuthoritativeStateProvider => authoritativeStateProvider;

        private void Awake()
        {
            CacheProvider();
            CaptureAuthoredRestPoses();
        }

        private void LateUpdate()
        {
            FlightDynamicsState state;
            FlightDynamicsControlSnapshot controls;
            bool hasFlightDynamics;
            if (_useTestSnapshots)
            {
                state = _testState;
                controls = _testControls;
                hasFlightDynamics = true;
            }
            else if (coordinator != null && coordinator.IsInitialized)
            {
                state = coordinator.CurrentState;
                controls = coordinator.LastAppliedControls;
                hasFlightDynamics = true;
            }
            else
            {
                state = default;
                controls = FlightDynamicsControlSnapshot.NeutralIdle;
                hasFlightDynamics = false;
            }

            Apply(state, controls, Time.deltaTime, hasFlightDynamics);
        }

        /// <summary>
        /// Authoring/test seam. Product references must still be serialized into
        /// the prefab; this method does not discover or construct them.
        /// </summary>
        public void ConfigureAuthoredReferences(
            FlightDynamicsCoordinator authoredCoordinator,
            MonoBehaviour authoredStateProvider,
            TransformBinding[] authoredBindings)
        {
            coordinator = authoredCoordinator;
            authoritativeStateProvider = authoredStateProvider;
            bindings = authoredBindings;
            CacheProvider();
            CaptureAuthoredRestPoses();
        }

        public void CaptureAuthoredRestPosesForValidation() => CaptureAuthoredRestPoses();

        public void SetSnapshotsForTest(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls)
        {
            _testState = state;
            _testControls = controls;
            _useTestSnapshots = true;
        }

        public void ClearSnapshotsForTest() => _useTestSnapshots = false;

        public void ApplySnapshotsForTest(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            float deltaTime)
        {
            Apply(state, controls, Mathf.Max(0f, deltaTime), true);
        }

        public void ResetToAuthoredRestPose()
        {
            if (bindings == null) return;
            for (int i = 0; i < bindings.Length; i++)
            {
                TransformBinding binding = bindings[i];
                if (binding?.target == null || !binding.hasRestPose) continue;
                binding.target.localPosition = binding.restLocalPosition;
                binding.target.localRotation = binding.restLocalRotation;
                if (binding.motions == null) continue;
                for (int motionIndex = 0; motionIndex < binding.motions.Length; motionIndex++)
                    binding.motions[motionIndex]?.CacheRuntimeData();
            }
        }

        public bool ValidateAuthoredBindings(out string error)
        {
            if (authoritativeStateProvider != null &&
                !(authoritativeStateProvider is ICockpitPresentationStateProvider))
            {
                error = "The authored state provider must implement ICockpitPresentationStateProvider.";
                return false;
            }
            if (bindings == null || bindings.Length == 0)
            {
                error = "At least one positively identified cockpit binding is required.";
                return false;
            }

            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                TransformBinding binding = bindings[bindingIndex];
                if (binding == null || binding.target == null)
                {
                    error = $"Cockpit binding {bindingIndex} has no serialized target.";
                    return false;
                }
                if (binding.motions == null || binding.motions.Length == 0)
                {
                    error = $"Cockpit binding '{binding.label}' has no motion terms.";
                    return false;
                }
                for (int prior = 0; prior < bindingIndex; prior++)
                {
                    if (bindings[prior]?.target == binding.target)
                    {
                        error = $"Cockpit target '{binding.target.name}' is bound more than once.";
                        return false;
                    }
                }
                for (int motionIndex = 0; motionIndex < binding.motions.Length; motionIndex++)
                {
                    MotionTerm motion = binding.motions[motionIndex];
                    if (motion == null || motion.channel == CockpitPresentationChannel.None)
                    {
                        error = $"Cockpit binding '{binding.label}' has an unidentified motion term.";
                        return false;
                    }
                    if (Mathf.Abs(motion.inputMaximum - motion.inputMinimum) < MinimumRange)
                    {
                        error = $"Cockpit motion '{motion.label}' has a zero input range.";
                        return false;
                    }
                    bool rotates = Mathf.Abs(motion.maximumAngleDegrees -
                                             motion.minimumAngleDegrees) >= MinimumRange;
                    bool translates = Mathf.Abs(motion.maximumTranslationMeters -
                                                motion.minimumTranslationMeters) >= MinimumRange;
                    if (!rotates && !translates)
                    {
                        error = $"Cockpit motion '{motion.label}' has no authored travel.";
                        return false;
                    }
                    if (rotates && motion.localRotationAxis.sqrMagnitude < MinimumRange)
                    {
                        error = $"Cockpit motion '{motion.label}' has no rotation axis.";
                        return false;
                    }
                    if (translates && motion.localTranslationAxis.sqrMagnitude < MinimumRange)
                    {
                        error = $"Cockpit motion '{motion.label}' has no translation axis.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private void CacheProvider()
        {
            _stateProvider = authoritativeStateProvider as ICockpitPresentationStateProvider;
        }

        private void CaptureAuthoredRestPoses()
        {
            if (bindings == null) return;
            for (int i = 0; i < bindings.Length; i++)
                bindings[i]?.CaptureRestPose();
        }

        private void Apply(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            float deltaTime,
            bool hasFlightDynamics)
        {
            if (bindings == null) return;
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                TransformBinding binding = bindings[bindingIndex];
                if (binding?.target == null || !binding.hasRestPose || binding.motions == null)
                    continue;

                Vector3 position = binding.restLocalPosition;
                Quaternion rotation = binding.restLocalRotation;
                bool hasResolvedMotion = false;
                for (int motionIndex = 0; motionIndex < binding.motions.Length; motionIndex++)
                {
                    MotionTerm motion = binding.motions[motionIndex];
                    if (motion == null ||
                        !TryResolveChannel(
                            motion.channel, state, controls, hasFlightDynamics, out float rawValue))
                        continue;

                    float normalized = motion.EvaluateNormalized(rawValue, deltaTime);
                    if (motion.normalizedRotationAxis.sqrMagnitude >= MinimumRange)
                    {
                        float angle = Mathf.LerpUnclamped(
                            motion.minimumAngleDegrees,
                            motion.maximumAngleDegrees,
                            normalized);
                        rotation *= Quaternion.AngleAxis(angle, motion.normalizedRotationAxis);
                    }
                    if (motion.normalizedTranslationAxis.sqrMagnitude >= MinimumRange)
                    {
                        float distance = Mathf.LerpUnclamped(
                            motion.minimumTranslationMeters,
                            motion.maximumTranslationMeters,
                            normalized);
                        position += motion.normalizedTranslationAxis * distance;
                    }
                    hasResolvedMotion = true;
                }

                if (!hasResolvedMotion) continue;
                binding.target.localPosition = position;
                binding.target.localRotation = rotation;
            }
        }

        private bool TryResolveChannel(
            CockpitPresentationChannel channel,
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            bool hasFlightDynamics,
            out float value)
        {
            if (authoritativeStateProvider != null)
            {
                if (_stateProvider != null &&
                    _stateProvider.TryGetCockpitPresentationValue(channel, out value))
                    return IsFinite(value);
                value = 0f;
                return false;
            }

            switch (channel)
            {
                case CockpitPresentationChannel.AileronNormalized:
                case CockpitPresentationChannel.YokeRollNormalized:
                    value = controls.aileron;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.ElevatorNormalized:
                case CockpitPresentationChannel.YokePitchNormalized:
                    value = controls.elevator;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.RudderNormalized:
                case CockpitPresentationChannel.RudderPedalsNormalized:
                    value = controls.rudder;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.LeftToeBrakeNormalized:
                    value = controls.leftToeBrake;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.RightToeBrakeNormalized:
                    value = controls.rightToeBrake;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.ThrottleNormalized:
                    value = controls.throttle;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.MixtureNormalized:
                    value = controls.mixture;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.AlternateAirNormalized:
                    value = controls.carbHeat;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.TrimNormalized:
                    value = controls.trim;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.FlapsCommandNormalized:
                    value = controls.flaps;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.NoseSteeringNormalized:
                    value = state.weightOnWheels
                        ? controls.rudder * Mathf.InverseLerp(
                            NoseSteeringZeroAuthorityKnots,
                            NoseSteeringFullAuthorityKnots,
                            (float)state.groundSpeedKnots)
                        : 0f;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.SimulationTimeSeconds:
                    value = (float)state.simulationTimeSeconds;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.CalibratedAirspeedKnots:
                    value = (float)state.calibratedAirspeedKnots;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.GroundSpeedKnots:
                    value = (float)state.groundSpeedKnots;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.AltitudeMslFeet:
                    value = (float)state.altitudeMslMeters * MetersToFeet;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.AltitudeAglFeet:
                    value = (float)state.altitudeAglMeters * MetersToFeet;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.VerticalSpeedFeetPerMinute:
                    value = (float)state.verticalSpeedFeetPerMinute;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.HeadingDegrees:
                    value = (float)state.headingDegrees;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.PitchDegrees:
                    value = (float)state.pitchDegrees;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.BankDegrees:
                    value = (float)state.bankDegrees;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.AngleOfAttackDegrees:
                    value = (float)state.angleOfAttackDegrees;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.SideslipDegrees:
                    value = (float)state.sideslipDegrees;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.EngineRpm:
                    value = (float)state.engineRpm;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.LoadFactorG:
                    value = (float)state.loadFactorG;
                    return hasFlightDynamics && IsFinite(value);
                case CockpitPresentationChannel.WeightOnWheels:
                    value = state.weightOnWheels ? 1f : 0f;
                    return hasFlightDynamics;
                default:
                    value = 0f;
                    return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
