using System;
using UnityEngine;

namespace QuestFlightLab.Input
{
    public enum ProductExternalControllerAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 3,
        Rx = 4,
        Ry = 5,
        Rz = 6
    }

    /// <summary>
    /// Serialized semantic map for a six-axis flight controller. It describes
    /// application controls, not a firmware protocol. T2's six-axis Android
    /// persona uses this map while ordinary BLE gamepads keep the standard
    /// Unity gamepad path.
    /// </summary>
    [Serializable]
    public sealed class ProductExternalControllerMapping
    {
        public ProductExternalControllerAxis aileronAxis =
            ProductExternalControllerAxis.X;
        public bool invertAileron;
        public ProductExternalControllerAxis elevatorAxis =
            ProductExternalControllerAxis.Y;
        public bool invertElevator = true;
        public ProductExternalControllerAxis throttleAxis =
            ProductExternalControllerAxis.Z;
        public bool invertThrottle;
        public ProductExternalControllerAxis rudderAxis =
            ProductExternalControllerAxis.Rx;
        public bool invertRudder;
        public ProductExternalControllerAxis leftToeBrakeAxis =
            ProductExternalControllerAxis.Ry;
        public bool invertLeftToeBrake;
        public ProductExternalControllerAxis rightToeBrakeAxis =
            ProductExternalControllerAxis.Rz;
        public bool invertRightToeBrake;
        public ProductExternalControllerAxis mixtureAxis =
            ProductExternalControllerAxis.None;
        public bool invertMixture;
        public ProductExternalControllerAxis flapsAxis =
            ProductExternalControllerAxis.None;
        public bool invertFlaps;
        [Range(0f, 0.25f)] public float primaryAxisDeadzone = 0.04f;
        [Range(0.001f, 0.1f)] public float positionChangeEpsilon = 0.01f;

        public bool IsValid(out string error)
        {
            if (aileronAxis == ProductExternalControllerAxis.None ||
                elevatorAxis == ProductExternalControllerAxis.None ||
                throttleAxis == ProductExternalControllerAxis.None ||
                rudderAxis == ProductExternalControllerAxis.None ||
                leftToeBrakeAxis == ProductExternalControllerAxis.None ||
                rightToeBrakeAxis == ProductExternalControllerAxis.None)
            {
                error = "aileron, elevator, throttle, rudder, and both toe-brake axes are required";
                return false;
            }

            ProductExternalControllerAxis[] required =
            {
                aileronAxis,
                elevatorAxis,
                throttleAxis,
                rudderAxis,
                leftToeBrakeAxis,
                rightToeBrakeAxis
            };
            for (int i = 0; i < required.Length; i++)
            for (int j = i + 1; j < required.Length; j++)
            {
                if (required[i] != required[j])
                    continue;
                error = "the six required flight controls must use distinct axes";
                return false;
            }

            if (!IsFinite(primaryAxisDeadzone) ||
                primaryAxisDeadzone < 0f || primaryAxisDeadzone > 0.25f)
            {
                error = "primary-axis deadzone must be finite and within 0..0.25";
                return false;
            }
            if (!IsFinite(positionChangeEpsilon) ||
                positionChangeEpsilon <= 0f || positionChangeEpsilon > 0.1f)
            {
                error = "position-change epsilon must be finite and within (0, 0.1]";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static ProductExternalControllerMapping SixAxisFlightDefault() =>
            new ProductExternalControllerMapping();

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
