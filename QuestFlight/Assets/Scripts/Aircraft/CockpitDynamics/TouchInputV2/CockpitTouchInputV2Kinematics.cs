using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2
{
    /// <summary>
    /// Allocation-free conversion from an authored OpenXR pointer pose into
    /// the three represented cockpit-control values. All calculations occur
    /// in stable aircraft-local reference frames so aircraft motion does not
    /// become false control input.
    /// </summary>
    public static class CockpitTouchInputV2Kinematics
    {
        private const float MinimumMagnitude = 0.000001f;

        public static bool TryPointerPoseInFrame(
            Transform frame,
            Vector3 pointerWorldPosition,
            Quaternion pointerWorldRotation,
            out Vector3 pointerLocalPosition,
            out Quaternion pointerLocalRotation)
        {
            pointerLocalPosition = Vector3.zero;
            pointerLocalRotation = Quaternion.identity;
            if (frame == null ||
                !IsFinite(pointerWorldPosition) ||
                !IsFinite(pointerWorldRotation))
                return false;

            pointerLocalPosition =
                frame.InverseTransformPoint(pointerWorldPosition);
            pointerLocalRotation =
                Quaternion.Inverse(frame.rotation) *
                pointerWorldRotation.normalized;
            return IsFinite(pointerLocalPosition) &&
                   IsFinite(pointerLocalRotation);
        }

        public static bool TryEvaluateYoke(
            Transform stableReferenceFrame,
            Vector3 pitchAxisLocal,
            Vector3 rollAxisLocal,
            float pitchHalfTravelMeters,
            float rollMaximumDegrees,
            Vector3 startPointerLocalPosition,
            Quaternion startPointerLocalRotation,
            Vector3 currentPointerWorldPosition,
            Quaternion currentPointerWorldRotation,
            float baselineAileron,
            float baselineElevator,
            out float aileron,
            out float elevator)
        {
            aileron = 0f;
            elevator = 0f;
            if (!TryPointerPoseInFrame(
                    stableReferenceFrame,
                    currentPointerWorldPosition,
                    currentPointerWorldRotation,
                    out Vector3 currentLocalPosition,
                    out Quaternion currentLocalRotation) ||
                !TryNormalizeAxis(
                    pitchAxisLocal,
                    out Vector3 pitchAxis) ||
                !TryNormalizeAxis(
                    rollAxisLocal,
                    out Vector3 rollAxis) ||
                !IsFinite(startPointerLocalPosition) ||
                !IsFinite(startPointerLocalRotation) ||
                !IsFinite(pitchHalfTravelMeters) ||
                pitchHalfTravelMeters <= MinimumMagnitude ||
                !IsFinite(rollMaximumDegrees) ||
                rollMaximumDegrees <= MinimumMagnitude ||
                !IsFinite(baselineAileron) ||
                !IsFinite(baselineElevator) ||
                !TrySignedTwistDegrees(
                    startPointerLocalRotation,
                    currentLocalRotation,
                    rollAxis,
                    out float signedRollDegrees))
                return false;

            float pitchDelta = Vector3.Dot(
                currentLocalPosition -
                startPointerLocalPosition,
                pitchAxis);
            // The authored pilot-yoke presentation maps positive aileron to
            // negative local roll, hence the deliberate sign inversion.
            aileron = Mathf.Clamp(
                baselineAileron -
                signedRollDegrees / rollMaximumDegrees,
                -1f,
                1f);
            elevator = Mathf.Clamp(
                // The authored +axis points into the panel: pushing forward
                // commands nose-down, while pulling aft commands nose-up.
                baselineElevator -
                pitchDelta / pitchHalfTravelMeters,
                -1f,
                1f);
            return true;
        }

        public static bool TryEvaluateLinearControl(
            Transform stableReferenceFrame,
            Vector3 fullForwardAxisLocal,
            float fullTravelMeters,
            Vector3 startPointerLocalPosition,
            Vector3 currentPointerWorldPosition,
            float baselineNormalized,
            out float normalized)
        {
            normalized = 0f;
            if (stableReferenceFrame == null ||
                !TryNormalizeAxis(
                    fullForwardAxisLocal,
                    out Vector3 axis) ||
                !IsFinite(fullTravelMeters) ||
                fullTravelMeters <= MinimumMagnitude ||
                !IsFinite(startPointerLocalPosition) ||
                !IsFinite(currentPointerWorldPosition) ||
                !IsFinite(baselineNormalized))
                return false;

            Vector3 currentLocal =
                stableReferenceFrame.InverseTransformPoint(
                    currentPointerWorldPosition);
            if (!IsFinite(currentLocal))
                return false;

            float travel = Vector3.Dot(
                currentLocal - startPointerLocalPosition,
                axis);
            normalized = Mathf.Clamp01(
                baselineNormalized +
                travel / fullTravelMeters);
            return true;
        }

        public static bool TryNormalizeAxis(
            Vector3 value,
            out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value) ||
                value.sqrMagnitude <= MinimumMagnitude)
                return false;
            normalized = value.normalized;
            return true;
        }

        private static bool TrySignedTwistDegrees(
            Quaternion startLocalRotation,
            Quaternion currentLocalRotation,
            Vector3 axis,
            out float signedDegrees)
        {
            signedDegrees = 0f;
            Vector3 startReference =
                Vector3.ProjectOnPlane(
                    startLocalRotation * Vector3.up,
                    axis);
            Vector3 currentReference =
                Vector3.ProjectOnPlane(
                    currentLocalRotation * Vector3.up,
                    axis);
            if (startReference.sqrMagnitude <= MinimumMagnitude ||
                currentReference.sqrMagnitude <= MinimumMagnitude)
            {
                startReference =
                    Vector3.ProjectOnPlane(
                        startLocalRotation * Vector3.right,
                        axis);
                currentReference =
                    Vector3.ProjectOnPlane(
                        currentLocalRotation * Vector3.right,
                        axis);
            }
            if (startReference.sqrMagnitude <= MinimumMagnitude ||
                currentReference.sqrMagnitude <= MinimumMagnitude)
                return false;

            signedDegrees = Vector3.SignedAngle(
                startReference,
                currentReference,
                axis);
            return IsFinite(signedDegrees);
        }

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        public static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w) &&
                   value.x * value.x +
                   value.y * value.y +
                   value.z * value.z +
                   value.w * value.w > MinimumMagnitude;
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
