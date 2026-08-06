using UnityEngine;

namespace QuestFlightLab.Runtime
{
    [DefaultExecutionOrder(20000)]
    public sealed class AircraftReferenceFrameRig : MonoBehaviour
    {
        public const string CenterOfGravityName = "CenterOfGravityReference";
        public const string VisualRootName = "AircraftVisualRoot";
        public const string PilotSeatName = "PilotSeatAnchor";
        public const string CalibrationOffsetName = "UserViewCalibrationOffset";

        public Transform AircraftSimulationRoot => transform;
        [field: SerializeField] public Transform CenterOfGravityReference { get; private set; }
        [field: SerializeField] public Transform AircraftVisualRoot { get; private set; }
        [field: SerializeField] public Transform PilotSeatAnchor { get; private set; }
        [field: SerializeField] public Transform UserViewCalibrationOffset { get; private set; }
        [field: SerializeField] public Transform XrOrigin { get; private set; }
        [field: SerializeField] public Transform LeftController { get; private set; }
        [field: SerializeField] public Transform RightController { get; private set; }
        [field: SerializeField] public Camera TrackedCamera { get; private set; }
        public Vector3 CalibrationOffset { get; private set; }
        public float CalibrationYawDegrees { get; private set; }
        public bool HierarchyReady { get; private set; }

        [SerializeField] private bool interpolatePresentation = true;
        [SerializeField] private float teleportDistanceMeters = 100f;
        [SerializeField] private float teleportAngleDegrees = 80f;

        private Vector3 _previousSimulationPosition;
        private Quaternion _previousSimulationRotation;
        private Vector3 _currentSimulationPosition;
        private Quaternion _currentSimulationRotation;
        private bool _hasSimulationSamples;

        private void Awake()
        {
            if (!HasAuthoredReferences()) return;

            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null) body.interpolation = RigidbodyInterpolation.Interpolate;
            HierarchyReady = ValidateHierarchy();
            if (HierarchyReady) ResetPresentationHistory();
            else Debug.LogError("[QuestFlightLab][ProductionRig] Authored reference-frame hierarchy is invalid.", this);
        }

        public void ConfigureAuthoredHierarchy(
            Transform centerOfGravity,
            Transform visualRoot,
            Transform pilotSeat,
            Transform calibrationOffset,
            Transform xrOrigin,
            Transform leftController,
            Transform rightController,
            Camera trackedCamera)
        {
            CenterOfGravityReference = centerOfGravity;
            AircraftVisualRoot = visualRoot;
            PilotSeatAnchor = pilotSeat;
            UserViewCalibrationOffset = calibrationOffset;
            XrOrigin = xrOrigin;
            LeftController = leftController;
            RightController = rightController;
            TrackedCamera = trackedCamera;
            HierarchyReady = ValidateHierarchy();
            if (HierarchyReady) ResetPresentationHistory();
        }

        public void SetPilotSeatLocalPose(Vector3 localPosition, Quaternion localRotation)
        {
            if (PilotSeatAnchor == null) return;
            PilotSeatAnchor.SetLocalPositionAndRotation(localPosition, localRotation);
        }

        public void ApplyCalibration(Vector3 localOffset, float yawDegrees)
        {
            CalibrationOffset = localOffset;
            CalibrationYawDegrees = NormalizeAngle(yawDegrees);
            if (UserViewCalibrationOffset != null)
            {
                UserViewCalibrationOffset.SetLocalPositionAndRotation(
                    CalibrationOffset,
                    Quaternion.Euler(0f, CalibrationYawDegrees, 0f));
            }
        }

        public bool RecenterTrackingSpaceToSeat()
        {
            if (XrOrigin == null || TrackedCamera == null || XrOrigin.parent != UserViewCalibrationOffset)
            {
                return false;
            }

            Vector3 trackedPosition = XrOrigin.InverseTransformPoint(TrackedCamera.transform.position);
            Quaternion trackedRotation = Quaternion.Inverse(XrOrigin.rotation) * TrackedCamera.transform.rotation;
            float trackedYaw = NormalizeAngle(trackedRotation.eulerAngles.y);
            Quaternion originCorrection = Quaternion.Euler(0f, -trackedYaw, 0f);

            // The XR Origin may move for an explicit recenter; the tracked camera is never written.
            XrOrigin.localRotation = originCorrection;
            XrOrigin.localPosition = -(originCorrection * trackedPosition);
            return true;
        }

        public void ResetTrackingSpaceOffset()
        {
            if (XrOrigin == null) return;
            XrOrigin.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public bool ValidateHierarchy()
        {
            if (CenterOfGravityReference == null || CenterOfGravityReference.parent != transform) return false;
            if (AircraftVisualRoot == null || AircraftVisualRoot.parent != transform) return false;
            if (PilotSeatAnchor == null || PilotSeatAnchor.parent != AircraftVisualRoot) return false;
            if (UserViewCalibrationOffset == null || UserViewCalibrationOffset.parent != PilotSeatAnchor) return false;
            if (XrOrigin == null || XrOrigin.parent != UserViewCalibrationOffset) return false;
            if (TrackedCamera == null || !TrackedCamera.transform.IsChildOf(XrOrigin)) return false;
            if (!TrackedXrCameraPoseDriver.HasRequiredBindings(TrackedCamera)) return false;
            if (LeftController == null || !LeftController.IsChildOf(XrOrigin) ||
                !TrackedXrControllerPoseDrivers.HasRequiredBindings(LeftController, true)) return false;
            if (RightController == null || !RightController.IsChildOf(XrOrigin) ||
                !TrackedXrControllerPoseDrivers.HasRequiredBindings(RightController, false)) return false;
            return CenterOfGravityReference.localPosition.sqrMagnitude < 0.000001f;
        }

        private bool HasAuthoredReferences()
        {
            return CenterOfGravityReference != null &&
                   AircraftVisualRoot != null &&
                   PilotSeatAnchor != null &&
                   UserViewCalibrationOffset != null &&
                   XrOrigin != null &&
                   LeftController != null &&
                   RightController != null &&
                   TrackedCamera != null;
        }

        public void ResetPresentationHistory()
        {
            _previousSimulationPosition = transform.position;
            _previousSimulationRotation = transform.rotation;
            _currentSimulationPosition = transform.position;
            _currentSimulationRotation = transform.rotation;
            _hasSimulationSamples = true;
            ApplyPresentationPose(_currentSimulationPosition, _currentSimulationRotation);
        }

        public void SetPresentationInterpolationForTest(bool enabled)
        {
            interpolatePresentation = enabled;
        }

        public void ApplyPresentationPoseForTest(Vector3 worldPosition, Quaternion worldRotation)
        {
            ApplyPresentationPose(worldPosition, worldRotation);
        }

        private void FixedUpdate()
        {
            if (!HierarchyReady || AircraftVisualRoot == null) return;

            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            if (!_hasSimulationSamples ||
                Vector3.Distance(_currentSimulationPosition, position) > teleportDistanceMeters ||
                Quaternion.Angle(_currentSimulationRotation, rotation) > teleportAngleDegrees)
            {
                _previousSimulationPosition = position;
                _previousSimulationRotation = rotation;
            }
            else
            {
                _previousSimulationPosition = _currentSimulationPosition;
                _previousSimulationRotation = _currentSimulationRotation;
            }

            _currentSimulationPosition = position;
            _currentSimulationRotation = rotation;
            _hasSimulationSamples = true;
        }

        private void LateUpdate()
        {
            if (!HierarchyReady || AircraftVisualRoot == null || !_hasSimulationSamples) return;

            if (!interpolatePresentation || Time.fixedDeltaTime <= 0f)
            {
                ApplyPresentationPose(_currentSimulationPosition, _currentSimulationRotation);
                return;
            }

            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            Vector3 position = Vector3.Lerp(_previousSimulationPosition, _currentSimulationPosition, alpha);
            Quaternion rotation = Quaternion.Slerp(_previousSimulationRotation, _currentSimulationRotation, alpha);
            ApplyPresentationPose(position, rotation);
        }

        private void ApplyPresentationPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (AircraftVisualRoot == null) return;
            AircraftVisualRoot.position = worldPosition;
            AircraftVisualRoot.rotation = worldRotation;
        }

        private void OnDisable()
        {
            if (AircraftVisualRoot != null)
            {
                AircraftVisualRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
        }

        private static float NormalizeAngle(float angle)
        {
            return Mathf.Repeat(angle + 180f, 360f) - 180f;
        }
    }
}
