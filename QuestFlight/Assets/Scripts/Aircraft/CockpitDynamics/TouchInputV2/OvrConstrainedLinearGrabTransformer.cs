using Oculus.Interaction;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2
{
    /// <summary>
    /// Single-owner Meta Grabbable transformer for the fixed throttle and
    /// mixture controls. Meta owns tracking, selection, and the physical hand
    /// pose. This class maps grab-point translation along an authored control
    /// axis into the existing ProductControlInput authority without moving a
    /// hand, controller, camera, or cockpit visual directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OvrConstrainedLinearGrabTransformer :
        MonoBehaviour,
        ITransformer
    {
        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField] private Transform stableReferenceFrame;
        [SerializeField] private CockpitTouchInteractionV2Target target;
        [SerializeField] private Vector3 fullForwardAxisLocal =
            new Vector3(0f, 0.08715622f, 0.9961947f);
        [SerializeField, Min(0.01f)] private float fullTravelMeters =
            CockpitTouchInputV2Bridge.EngineControlFullTravelMeters;

        private IGrabbable _grabbable;
        private int _ownerToken;
        private bool _active;
        private Vector3 _startPointerLocal;
        private float _baseline;
        private float _lastTraceTime = float.NegativeInfinity;

        public CockpitTouchInteractionV2Target Target => target;
        public FlightDynamicsCoordinator Coordinator => coordinator;
        public Transform StableReferenceFrame => stableReferenceFrame;
        public bool HasActiveGrab => _active;
        public ProductControlInput InputAuthority =>
            coordinator != null ? coordinator.controls : null;

        private ProductTouchControlChannel Channel =>
            target == CockpitTouchInteractionV2Target.Throttle
                ? ProductTouchControlChannel.Throttle
                : ProductTouchControlChannel.Mixture;

        public void ConfigureAuthoredReferences(
            FlightDynamicsCoordinator authoredCoordinator,
            Transform authoredStableReferenceFrame,
            CockpitTouchInteractionV2Target authoredTarget,
            Vector3 authoredFullForwardAxisLocal,
            float authoredFullTravelMeters)
        {
            ReleaseOverride();
            coordinator = authoredCoordinator;
            stableReferenceFrame = authoredStableReferenceFrame;
            target = authoredTarget;
            fullForwardAxisLocal = authoredFullForwardAxisLocal;
            fullTravelMeters = authoredFullTravelMeters;
            EnsureOwnerToken();
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (coordinator == null || stableReferenceFrame == null)
            {
                error = "The constrained OVR engine control requires serialized input and stable-frame references.";
                return false;
            }
            if (target != CockpitTouchInteractionV2Target.Throttle &&
                target != CockpitTouchInteractionV2Target.Mixture)
            {
                error = "The constrained OVR linear transformer supports only throttle or mixture.";
                return false;
            }
            if (!CockpitTouchInputV2Kinematics.TryNormalizeAxis(
                    fullForwardAxisLocal, out _) ||
                !CockpitTouchInputV2Kinematics.IsFinite(fullTravelMeters) ||
                fullTravelMeters <= 0f)
            {
                error = "The constrained OVR engine control has invalid motion limits.";
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
            OvrConcurrentInputTrace.RecordTransformer(
                target, "BeginTransform", true, GrabPointCount, 0f, 0f);
            ReleaseOverride();
            TryBeginFromCurrentGrabPoint();
        }

        public void UpdateTransform()
        {
            if (_grabbable?.GrabPoints == null ||
                _grabbable.GrabPoints.Count == 0)
            {
                ReleaseOverride();
                return;
            }

            ProductControlInput authority = InputAuthority;
            if (!_active || authority == null ||
                !authority.IsTouchOverrideOwnedBy(Channel, _ownerToken))
            {
                ReleaseOverride();
                TryBeginFromCurrentGrabPoint();
                return;
            }

            Pose point = _grabbable.GrabPoints[0];
            bool evaluated = CockpitTouchInputV2Kinematics
                .TryEvaluateLinearControl(
                    stableReferenceFrame,
                    fullForwardAxisLocal,
                    fullTravelMeters,
                    _startPointerLocal,
                    point.position,
                    _baseline,
                    out float normalized);
            bool accepted = evaluated && authority.TryUpdateTouchOverride(
                Channel, _ownerToken, normalized);
            if (!accepted || Time.unscaledTime - _lastTraceTime >= 0.25f)
            {
                _lastTraceTime = Time.unscaledTime;
                OvrConcurrentInputTrace.RecordTransformer(
                    target,
                    "UpdateAuthority",
                    accepted,
                    GrabPointCount,
                    evaluated ? normalized : 0f,
                    0f,
                    accepted ? string.Empty :
                        (evaluated ? "AuthorityRejected" : "InvalidPose"));
            }
        }

        public void EndTransform()
        {
            OvrConcurrentInputTrace.RecordTransformer(
                target, "EndTransform", true, GrabPointCount, 0f, 0f);
            ReleaseOverride();
        }

        private void OnDisable() => ReleaseOverride();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ReleaseOverride();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) ReleaseOverride();
        }

        private bool TryBeginFromCurrentGrabPoint()
        {
            if (_grabbable?.GrabPoints == null ||
                _grabbable.GrabPoints.Count == 0)
                return RejectBegin("NoGrabPoint");
            if (!ValidateAuthoredReferences(out string error))
                return RejectBegin(error);
            ProductControlInput authority = InputAuthority;
            if (authority == null || !authority.CanAcceptTouchOverrides)
                return RejectBegin("InputAuthorityUnavailable");

            Pose point = _grabbable.GrabPoints[0];
            Vector3 local = stableReferenceFrame
                .InverseTransformPoint(point.position);
            if (!CockpitTouchInputV2Kinematics.IsFinite(local))
                return RejectBegin("NonFiniteGrabPoint");

            EnsureOwnerToken();
            _baseline = target == CockpitTouchInteractionV2Target.Throttle
                ? authority.Current.throttle
                : authority.Current.mixture;
            if (!authority.TryBeginTouchOverride(
                    Channel, _ownerToken, _baseline))
                return RejectBegin("AuthorityRejected");

            _startPointerLocal = local;
            _active = true;
            _lastTraceTime = float.NegativeInfinity;
            OvrConcurrentInputTrace.RecordTransformer(
                target, "BeginAuthority", true, GrabPointCount, _baseline, 0f);
            return true;
        }

        private bool RejectBegin(string reason)
        {
            OvrConcurrentInputTrace.RecordTransformer(
                target, "BeginRejected", false, GrabPointCount, 0f, 0f, reason);
            return false;
        }

        private void ReleaseOverride()
        {
            ProductControlInput authority = InputAuthority;
            bool wasActive = _active;
            bool released = false;
            if (authority != null && _ownerToken != 0)
                released = authority.TryEndTouchOverride(Channel, _ownerToken);
            _active = false;
            if (wasActive)
            {
                OvrConcurrentInputTrace.RecordTransformer(
                    target,
                    "ReleaseAuthority",
                    released,
                    GrabPointCount,
                    0f,
                    0f,
                    released ? string.Empty : "AuthorityReleaseRejected");
            }
        }

        private int GrabPointCount => _grabbable?.GrabPoints?.Count ?? 0;

        private void EnsureOwnerToken()
        {
            if (_ownerToken != 0) return;
            _ownerToken = GetInstanceID();
            if (_ownerToken == 0) _ownerToken = 1;
        }
    }
}
