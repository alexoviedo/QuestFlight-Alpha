using System;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Aircraft
{
    [DisallowMultipleComponent]
    public sealed class ProductionAircraftControlAnimator : MonoBehaviour
    {
        public enum ControlChannel
        {
            Aileron,
            Elevator,
            Rudder,
            Flaps,
            YokeRoll,
            YokePitch,
            RudderPedals,
            Throttle,
            Mixture,
            Trim,
            NoseSteering
        }

        [Serializable]
        public sealed class Binding
        {
            public string label;
            public Transform target;
            public ControlChannel channel;
            public Vector3 localRotationAxis = Vector3.right;
            public float minimumAngleDegrees;
            public float maximumAngleDegrees;
            public Vector3 localTranslationAxis;
            public float minimumTranslationMeters;
            public float maximumTranslationMeters;

            [NonSerialized] public Vector3 restLocalPosition;
            [NonSerialized] public Quaternion restLocalRotation;
        }

        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField] private ProductControlInput mapperFallback;
        [SerializeField] private Binding[] bindings;

        public string ActiveControlSource { get; private set; } = "neutral";
        public FlightDynamicsControlSnapshot LastAnimatedControls { get; private set; } = FlightDynamicsControlSnapshot.NeutralIdle;

        private FlightDynamicsControlSnapshot _testControls;
        private bool _useTestControls;

        public void ConfigureAuthoredBindings(
            FlightDynamicsCoordinator authoredCoordinator,
            ProductControlInput authoredFallback,
            Binding[] authoredBindings)
        {
            coordinator = authoredCoordinator;
            mapperFallback = authoredFallback;
            bindings = authoredBindings;
        }

        public void ConfigureControlSources(
            FlightDynamicsCoordinator authoredCoordinator,
            ProductControlInput authoredFallback)
        {
            coordinator = authoredCoordinator;
            mapperFallback = authoredFallback;
        }

        private void Awake()
        {
            CacheRestPoses();
        }

        private void LateUpdate()
        {
            FlightDynamicsControlSnapshot controls = ResolveControls();
            LastAnimatedControls = controls;
            Apply(controls);
        }

        public void SetControlStateForTest(AircraftControlState controls)
        {
            _testControls = FlightDynamicsControlSnapshot.From(controls);
            _useTestControls = controls != null;
            if (_useTestControls)
            {
                ActiveControlSource = "test";
                LastAnimatedControls = _testControls;
                Apply(_testControls);
            }
        }

        public void ClearControlStateForTest() => _useTestControls = false;

        public void CaptureAuthoredRestPosesForValidation() => CacheRestPoses();

        private void CacheRestPoses()
        {
            if (bindings == null) return;
            for (int i = 0; i < bindings.Length; i++)
            {
                Binding binding = bindings[i];
                if (binding?.target == null) continue;
                binding.restLocalPosition = binding.target.localPosition;
                binding.restLocalRotation = binding.target.localRotation;
            }
        }

        private FlightDynamicsControlSnapshot ResolveControls()
        {
            if (_useTestControls)
            {
                ActiveControlSource = "test";
                return _testControls;
            }

            if (coordinator != null)
            {
                ActiveControlSource = "authoritative backend boundary";
                return coordinator.LastAppliedControls;
            }

            if (mapperFallback != null)
            {
                ActiveControlSource = "input mapper fallback";
                return FlightDynamicsControlSnapshot.From(mapperFallback.Current);
            }

            ActiveControlSource = "neutral";
            return FlightDynamicsControlSnapshot.NeutralIdle;
        }

        private void Apply(FlightDynamicsControlSnapshot controls)
        {
            if (bindings == null) return;
            for (int i = 0; i < bindings.Length; i++)
            {
                Binding binding = bindings[i];
                if (binding?.target == null) continue;

                float raw = ChannelValue(binding.channel, controls);
                bool unipolar = binding.channel == ControlChannel.Flaps ||
                                binding.channel == ControlChannel.Throttle ||
                                binding.channel == ControlChannel.Mixture;
                float t = unipolar ? Mathf.Clamp01(raw) : Mathf.InverseLerp(-1f, 1f, Mathf.Clamp(raw, -1f, 1f));
                float angle = Mathf.Lerp(binding.minimumAngleDegrees, binding.maximumAngleDegrees, t);
                float distance = Mathf.Lerp(binding.minimumTranslationMeters, binding.maximumTranslationMeters, t);
                Vector3 axis = binding.localRotationAxis.sqrMagnitude > 0.000001f
                    ? binding.localRotationAxis.normalized
                    : Vector3.right;
                binding.target.localRotation = binding.restLocalRotation * Quaternion.AngleAxis(angle, axis);
                binding.target.localPosition = binding.restLocalPosition + binding.localTranslationAxis.normalized * distance;
            }
        }

        private static float ChannelValue(ControlChannel channel, FlightDynamicsControlSnapshot controls)
        {
            return channel switch
            {
                ControlChannel.Aileron => controls.aileron,
                ControlChannel.Elevator => controls.elevator,
                ControlChannel.Rudder => controls.rudder,
                ControlChannel.Flaps => controls.flaps,
                ControlChannel.YokeRoll => controls.aileron,
                ControlChannel.YokePitch => controls.elevator,
                ControlChannel.RudderPedals => controls.rudder,
                ControlChannel.Throttle => controls.throttle,
                ControlChannel.Mixture => controls.mixture,
                ControlChannel.Trim => controls.trim,
                ControlChannel.NoseSteering => controls.rudder,
                _ => 0f
            };
        }
    }
}
