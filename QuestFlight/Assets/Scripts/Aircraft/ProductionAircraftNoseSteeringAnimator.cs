using System;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Aircraft
{
    /// <summary>
    /// Allocation-free nose-steering presentation for the positively identified
    /// fixed C172 nose-gear assembly. Every transform is serialized by an Editor
    /// authoring pass; this component never discovers or creates hierarchy.
    ///
    /// Source geometry below AircraftVisualRoot/ImportedAircraftExterior:
    /// - steering strut/fairing:
    ///   Cessna_Exterior/Cessna_Exterior_Body_MAT_0.019/
    ///   Cessna_Exterior_Body_MAT_0.019_Body_MAT_0
    /// - combined nose wheel and fixed aerodynamic pant:
    ///   Cessna_Exterior/Cessna_Exterior_Body_MAT_0.021/
    ///   Cessna_Exterior_Body_MAT_0.021_Body_MAT_0
    ///
    /// Both source nodes yaw together beneath one AircraftVisualRoot-aligned
    /// pivot using local +Y. The source meshes combine rotating tires with fixed
    /// wheel pants, so neither nose nor main-wheel spin is represented. The
    /// fixed-pitch propeller and spinner also remain static because exact phase
    /// can reverse-strobe at the 72 Hz Quest target. No renderer, material,
    /// texture, transparent pass, hierarchy repair, or fabricated motion is
    /// introduced for those excluded systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionAircraftNoseSteeringAnimator : MonoBehaviour
    {
        [Serializable]
        public sealed class SteeringBinding
        {
            [Tooltip("Authored AircraftVisualRoot-aligned nose-steering pivot.")]
            public Transform steeringPivot;

            [Tooltip("Identity-audited .019 strut/fairing below steeringPivot.")]
            public Transform steeringStrutFairing;

            [Tooltip("Identity-audited .021 combined wheel/pant below steeringPivot.")]
            public Transform noseWheelPant;

            [Tooltip("Steering axis in the authored pivot's local frame.")]
            public Vector3 localRotationAxis = Vector3.up;

            [Range(0f, 30f)]
            public float maximumSteeringAngleDegrees = 10f;

            [Min(0f)]
            public float fullAuthorityAtOrBelowKnots = 5f;

            [Min(0.1f)]
            public float zeroAuthorityAtOrAboveKnots = 35f;

            [Tooltip("Exactly +1 or -1 to match the authored steering orientation.")]
            public float direction = 1f;

            [NonSerialized] internal Quaternion restLocalRotation;
            [NonSerialized] internal Vector3 normalizedAxis;
            [NonSerialized] internal float appliedAngleDegrees;
            [NonSerialized] internal bool poseCached;

            internal bool TryCachePose()
            {
                poseCached = false;
                if (!HasValidReferencesAndParameters())
                    return false;

                restLocalRotation = steeringPivot.localRotation;
                normalizedAxis = localRotationAxis.normalized;
                appliedAngleDegrees = 0f;
                poseCached = true;
                return true;
            }

            internal bool HasValidReferencesAndParameters()
            {
                return steeringPivot != null &&
                       steeringStrutFairing != null &&
                       noseWheelPant != null &&
                       steeringStrutFairing != noseWheelPant &&
                       steeringStrutFairing != steeringPivot &&
                       noseWheelPant != steeringPivot &&
                       steeringStrutFairing.IsChildOf(steeringPivot) &&
                       noseWheelPant.IsChildOf(steeringPivot) &&
                       IsFiniteNonZero(localRotationAxis) &&
                       IsFinitePositive(maximumSteeringAngleDegrees) &&
                       maximumSteeringAngleDegrees <= 30f &&
                       IsFiniteNonNegative(fullAuthorityAtOrBelowKnots) &&
                       IsFinitePositive(zeroAuthorityAtOrAboveKnots) &&
                       zeroAuthorityAtOrAboveKnots >
                       fullAuthorityAtOrBelowKnots &&
                       IsUnitDirection(direction);
            }

            internal void Apply(
                float rudder,
                double groundSpeedKnots,
                bool weightOnWheels)
            {
                float authority = 0f;
                if (weightOnWheels)
                {
                    authority = 1f - Mathf.InverseLerp(
                        fullAuthorityAtOrBelowKnots,
                        zeroAuthorityAtOrAboveKnots,
                        (float)groundSpeedKnots);
                }

                appliedAngleDegrees =
                    Mathf.Clamp(rudder, -1f, 1f) *
                    maximumSteeringAngleDegrees *
                    direction *
                    authority;
                steeringPivot.localRotation = restLocalRotation *
                    Quaternion.AngleAxis(
                        appliedAngleDegrees,
                        normalizedAxis);
            }
        }

        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField] private SteeringBinding noseSteering;

        private bool _bindingCached;

        public FlightDynamicsCoordinator Coordinator => coordinator;
        public bool HasAuthoritativeState { get; private set; }
        public float AppliedSteeringAngleDegrees =>
            noseSteering != null
                ? noseSteering.appliedAngleDegrees
                : 0f;
        public Transform SteeringPivot =>
            noseSteering != null ? noseSteering.steeringPivot : null;
        public Transform SteeringStrutFairing =>
            noseSteering != null
                ? noseSteering.steeringStrutFairing
                : null;
        public Transform NoseWheelPant =>
            noseSteering != null ? noseSteering.noseWheelPant : null;
        public Vector3 LocalRotationAxis =>
            noseSteering != null
                ? noseSteering.localRotationAxis
                : Vector3.zero;
        public float MaximumSteeringAngleDegrees =>
            noseSteering != null
                ? noseSteering.maximumSteeringAngleDegrees
                : 0f;
        public float FullAuthorityAtOrBelowKnots =>
            noseSteering != null
                ? noseSteering.fullAuthorityAtOrBelowKnots
                : 0f;
        public float ZeroAuthorityAtOrAboveKnots =>
            noseSteering != null
                ? noseSteering.zeroAuthorityAtOrAboveKnots
                : 0f;
        public float Direction =>
            noseSteering != null ? noseSteering.direction : 0f;

        public void ConfigureAuthoredBindings(
            FlightDynamicsCoordinator authoredCoordinator,
            SteeringBinding authoredNoseSteering)
        {
            coordinator = authoredCoordinator;
            noseSteering = authoredNoseSteering;
            CacheAuthoredRestPose();
        }

        public bool HasAuthoredReferences(out string error)
        {
            if (coordinator == null)
            {
                error =
                    "ProductionAircraftNoseSteeringAnimator requires a serialized FlightDynamicsCoordinator.";
                return false;
            }

            if (noseSteering == null ||
                !noseSteering.HasValidReferencesAndParameters())
            {
                error =
                    "Nose steering requires one authored pivot containing the distinct .019 strut/fairing and .021 wheel/pant sources.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void CaptureAuthoredRestPoseForValidation()
        {
            CacheAuthoredRestPose();
        }

        private void Awake()
        {
            CacheAuthoredRestPose();
        }

        private void OnDisable()
        {
            HasAuthoritativeState = false;
        }

        private void LateUpdate()
        {
            if (coordinator == null || !coordinator.IsInitialized)
            {
                HasAuthoritativeState = false;
                return;
            }

            HasAuthoritativeState = Step(
                coordinator.CurrentState,
                coordinator.LastAppliedControls,
                Time.deltaTime);
        }

        /// <summary>
        /// Deterministic seam for focused tests. It bypasses only coordinator
        /// initialization; all state and authored-reference validation remains.
        /// </summary>
        public bool StepForTest(
            FlightDynamicsState authoritativeState,
            FlightDynamicsControlSnapshot authoritativeControls,
            float deltaTimeSeconds)
        {
            return Step(
                authoritativeState,
                authoritativeControls,
                deltaTimeSeconds);
        }

        private bool Step(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            float deltaTimeSeconds)
        {
            if (!_bindingCached)
                CacheAuthoredRestPose();

            if (!_bindingCached ||
                !state.IsFinite ||
                state.groundSpeedKnots < 0.0 ||
                !IsFinite(controls.rudder) ||
                !IsFinitePositive(deltaTimeSeconds))
            {
                return false;
            }

            noseSteering.Apply(
                controls.rudder,
                state.groundSpeedKnots,
                state.weightOnWheels);
            return true;
        }

        private void CacheAuthoredRestPose()
        {
            _bindingCached =
                noseSteering != null &&
                noseSteering.TryCachePose();
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) && value >= 0f;
        }

        private static bool IsFiniteNonZero(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   value.sqrMagnitude > 0.000001f;
        }

        private static bool IsUnitDirection(float value)
        {
            return value == 1f || value == -1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
