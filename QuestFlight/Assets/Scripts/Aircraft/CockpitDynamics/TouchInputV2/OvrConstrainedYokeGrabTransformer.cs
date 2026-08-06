using Oculus.Interaction;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2
{
    /// <summary>
    /// Meta Grabbable transformer for the fixed aircraft yoke. Meta owns
    /// tracking, selection, and the grab point. This component converts only
    /// physical grab-point displacement into the existing product input
    /// authority; it never writes a hand, controller, camera, or yoke visual
    /// transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OvrConstrainedYokeGrabTransformer :
        MonoBehaviour,
        ITransformer
    {
        private const float MinimumRadialDistanceMeters = 0.035f;

        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField] private Transform stableReferenceFrame;
        [SerializeField] private Transform rollCenter;
        [SerializeField] private Vector3 pitchAxisLocal = Vector3.forward;
        [SerializeField] private Vector3 rollAxisLocal = Vector3.forward;
        [SerializeField, Min(0.01f)] private float pitchHalfTravelMeters =
            CockpitTouchInputV2Bridge.YokePitchHalfTravelMeters;
        [SerializeField, Min(1f)] private float rollMaximumDegrees =
            CockpitTouchInputV2Bridge.YokeRollMaximumDegrees;

        private IGrabbable _grabbable;
        private int _ownerToken;
        private bool _active;
        private Vector3 _startPointerLocal;
        private Vector3 _startRadialLocal;
        private float _baselineAileron;
        private float _baselineElevator;

        public FlightDynamicsCoordinator Coordinator => coordinator;
        public ProductControlInput InputAuthority =>
            coordinator != null ? coordinator.controls : null;
        public Transform StableReferenceFrame => stableReferenceFrame;
        public Transform RollCenter => rollCenter;
        public bool HasActiveGrab => _active;

        public void ConfigureAuthoredReferences(
            FlightDynamicsCoordinator authoredCoordinator,
            Transform authoredStableReferenceFrame,
            Transform authoredRollCenter,
            Vector3 authoredPitchAxisLocal,
            Vector3 authoredRollAxisLocal,
            float authoredPitchHalfTravelMeters,
            float authoredRollMaximumDegrees)
        {
            ReleaseOverride();
            coordinator = authoredCoordinator;
            stableReferenceFrame = authoredStableReferenceFrame;
            rollCenter = authoredRollCenter;
            pitchAxisLocal = authoredPitchAxisLocal;
            rollAxisLocal = authoredRollAxisLocal;
            pitchHalfTravelMeters = authoredPitchHalfTravelMeters;
            rollMaximumDegrees = authoredRollMaximumDegrees;
            EnsureOwnerToken();
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (coordinator == null ||
                stableReferenceFrame == null ||
                rollCenter == null)
            {
                error =
                    "The constrained OVR yoke requires serialized input, " +
                    "stable-frame, and roll-center references.";
                return false;
            }
            if (!CockpitTouchInputV2Kinematics.TryNormalizeAxis(
                    pitchAxisLocal, out _) ||
                !CockpitTouchInputV2Kinematics.TryNormalizeAxis(
                    rollAxisLocal, out _) ||
                !CockpitTouchInputV2Kinematics.IsFinite(
                    pitchHalfTravelMeters) ||
                pitchHalfTravelMeters <= 0f ||
                !CockpitTouchInputV2Kinematics.IsFinite(
                    rollMaximumDegrees) ||
                rollMaximumDegrees <= 0f)
            {
                error = "The constrained OVR yoke has invalid motion limits.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;
            EnsureOwnerToken();
        }

        public void BeginTransform()
        {
            ReleaseOverride();
            TryBeginFromCurrentGrabPoint();
        }

        public void UpdateTransform()
        {
            if (_grabbable == null ||
                _grabbable.GrabPoints == null ||
                _grabbable.GrabPoints.Count == 0)
            {
                ReleaseOverride();
                return;
            }

            if (!_active ||
                !InputAuthority.IsTouchOverrideOwnedBy(
                    ProductTouchControlChannel.PilotYoke,
                    _ownerToken))
            {
                ReleaseOverride();
                TryBeginFromCurrentGrabPoint();
                return;
            }

            Pose grabPoint = _grabbable.GrabPoints[0];
            Vector3 currentLocal = stableReferenceFrame
                .InverseTransformPoint(grabPoint.position);
            Vector3 rollAxis = rollAxisLocal.normalized;
            Vector3 rollCenterLocal = stableReferenceFrame
                .InverseTransformPoint(rollCenter.position);
            Vector3 currentRadial = Vector3.ProjectOnPlane(
                currentLocal - rollCenterLocal,
                rollAxis);
            if (!CockpitTouchInputV2Kinematics.IsFinite(currentLocal) ||
                currentRadial.sqrMagnitude <
                    MinimumRadialDistanceMeters *
                    MinimumRadialDistanceMeters)
                return;

            float signedRollDegrees = Vector3.SignedAngle(
                _startRadialLocal,
                currentRadial,
                rollAxis);
            float pitchDelta = Vector3.Dot(
                currentLocal - _startPointerLocal,
                pitchAxisLocal.normalized);
            float aileron = Mathf.Clamp(
                _baselineAileron -
                signedRollDegrees / rollMaximumDegrees,
                -1f,
                1f);
            float elevator = Mathf.Clamp(
                _baselineElevator -
                pitchDelta / pitchHalfTravelMeters,
                -1f,
                1f);
            InputAuthority.TryUpdateTouchOverride(
                ProductTouchControlChannel.PilotYoke,
                _ownerToken,
                aileron,
                elevator);
        }

        public void EndTransform()
        {
            ReleaseOverride();
        }

        private void OnDisable()
        {
            ReleaseOverride();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                ReleaseOverride();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                ReleaseOverride();
        }

        private bool TryBeginFromCurrentGrabPoint()
        {
            if (_grabbable == null ||
                _grabbable.GrabPoints == null ||
                _grabbable.GrabPoints.Count == 0 ||
                !ValidateAuthoredReferences(out _) ||
                InputAuthority == null ||
                !InputAuthority.CanAcceptTouchOverrides)
                return false;

            Pose grabPoint = _grabbable.GrabPoints[0];
            Vector3 local = stableReferenceFrame
                .InverseTransformPoint(grabPoint.position);
            Vector3 rollAxis = rollAxisLocal.normalized;
            Vector3 centerLocal = stableReferenceFrame
                .InverseTransformPoint(rollCenter.position);
            Vector3 radial = Vector3.ProjectOnPlane(
                local - centerLocal,
                rollAxis);
            if (!CockpitTouchInputV2Kinematics.IsFinite(local) ||
                radial.sqrMagnitude <
                    MinimumRadialDistanceMeters *
                    MinimumRadialDistanceMeters)
                return false;

            EnsureOwnerToken();
            _baselineAileron = InputAuthority.Current.aileron;
            _baselineElevator = InputAuthority.Current.elevator;
            if (!InputAuthority.TryBeginTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    _ownerToken,
                    _baselineAileron,
                    _baselineElevator))
                return false;

            _startPointerLocal = local;
            _startRadialLocal = radial.normalized;
            _active = true;
            return true;
        }

        private void ReleaseOverride()
        {
            ProductControlInput inputAuthority = InputAuthority;
            if (inputAuthority != null && _ownerToken != 0)
            {
                inputAuthority.TryEndTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    _ownerToken);
            }
            _active = false;
        }

        private void EnsureOwnerToken()
        {
            if (_ownerToken != 0) return;
            _ownerToken = GetInstanceID();
            if (_ownerToken == 0)
                _ownerToken = 1;
        }
    }
}
