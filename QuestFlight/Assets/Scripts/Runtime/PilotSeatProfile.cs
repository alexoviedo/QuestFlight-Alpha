using UnityEngine;

namespace QuestFlightLab.Runtime
{
    [CreateAssetMenu(fileName = "PilotSeatProfile", menuName = "Quest Flight Lab/Pilot Seat Profile")]
    public sealed class PilotSeatProfile : ScriptableObject
    {
        [Header("Aircraft identity")]
        public string aircraftId = CockpitViewpointPersistence.DefaultAircraftId;

        [Header("Authored aircraft-local references")]
        [Tooltip("Imported left-seat geometry reference before the eye-height/aft posture offset.")]
        public Vector3 seatAnchorLocalPosition = new Vector3(-0.28f, 0.72f, 0f);
        [Tooltip("Corrected neutral eye position used by PilotSeatAnchor.")]
        public Vector3 nominalEyeLocalPosition = new Vector3(-0.28f, 0.94f, -0.18f);

        [Header("Additive user-calibration bounds")]
        public Vector3 minimumCalibrationOffset = new Vector3(-0.25f, -0.30f, -0.35f);
        public Vector3 maximumCalibrationOffset = new Vector3(0.25f, 0.30f, 0.35f);
        [Min(0f)] public float maximumCalibrationYawDegrees = 15f;

        [Header("Measured cockpit references")]
        [Tooltip("Point on the nearest instrument-panel plane in aircraft-local coordinates.")]
        public Vector3 instrumentPanelReferencePoint = new Vector3(-0.28f, 0.94f, 0.317f);
        [Tooltip("Panel normal facing the seated pilot.")]
        public Vector3 instrumentPanelReferenceNormal = Vector3.back;
        public Vector3 glareShieldReferencePoint = new Vector3(-0.28f, 0.98f, 0.30f);
        public Vector3 windshieldHorizonReferencePoint = new Vector3(-0.28f, 1.12f, 0.72f);
        [Range(0f, 1f)] public float minimumOutsideViewRatio = 0.20f;
        [Min(0f)] public float targetEyeToPanelDistanceMeters = 0.497f;

        public Vector3 ClampCalibrationOffset(Vector3 offset)
        {
            return new Vector3(
                Mathf.Clamp(offset.x, minimumCalibrationOffset.x, maximumCalibrationOffset.x),
                Mathf.Clamp(offset.y, minimumCalibrationOffset.y, maximumCalibrationOffset.y),
                Mathf.Clamp(offset.z, minimumCalibrationOffset.z, maximumCalibrationOffset.z));
        }

        public float ClampCalibrationYaw(float yawDegrees)
        {
            float normalized = Mathf.Repeat(yawDegrees + 180f, 360f) - 180f;
            return Mathf.Clamp(normalized, -maximumCalibrationYawDegrees, maximumCalibrationYawDegrees);
        }

        public float EyeToPanelForwardDistanceMeters()
        {
            return instrumentPanelReferencePoint.z - nominalEyeLocalPosition.z;
        }
    }
}
