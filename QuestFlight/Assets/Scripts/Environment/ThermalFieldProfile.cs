using System;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Offline-authored, deterministic thermal shared by flight dynamics and
    /// wildlife. Coordinates use the product ENU frame (X east, Y up, Z north).
    /// </summary>
    [CreateAssetMenu(
        fileName = "ThermalFieldProfile",
        menuName = "QuestFlight/Environment/Thermal Field Profile")]
    public sealed class ThermalFieldProfile : ScriptableObject
    {
        public string thermalId = "kbdu_sunlit_field_thermal_01";
        public Vector3 centerUnityMeters = new Vector3(1700f, 0f, 2600f);
        [Min(1f)] public float coreRadiusMeters = 70f;
        [Min(2f)] public float outerRadiusMeters = 240f;
        [Min(0f)] public float baseHeightUnityMeters = 40f;
        [Min(0f)] public float fullStrengthBottomUnityMeters = 180f;
        [Min(1f)] public float fullStrengthTopUnityMeters = 760f;
        [Min(2f)] public float capHeightUnityMeters = 1100f;
        [Min(0f)] public float peakUpdraftMetersPerSecond = 2.5f;

        public bool IsValid(out string failure)
        {
            if (string.IsNullOrWhiteSpace(thermalId))
            {
                failure = "Thermal ID is required.";
                return false;
            }
            if (!IsFinite(centerUnityMeters) || !IsFinite(coreRadiusMeters) ||
                !IsFinite(outerRadiusMeters) || !IsFinite(baseHeightUnityMeters) ||
                !IsFinite(fullStrengthBottomUnityMeters) || !IsFinite(fullStrengthTopUnityMeters) ||
                !IsFinite(capHeightUnityMeters) || !IsFinite(peakUpdraftMetersPerSecond))
            {
                failure = "Thermal profile contains a non-finite value.";
                return false;
            }
            if (coreRadiusMeters <= 0f || outerRadiusMeters <= coreRadiusMeters)
            {
                failure = "Thermal radii must satisfy 0 < core < outer.";
                return false;
            }
            if (baseHeightUnityMeters >= fullStrengthBottomUnityMeters ||
                fullStrengthBottomUnityMeters >= fullStrengthTopUnityMeters ||
                fullStrengthTopUnityMeters >= capHeightUnityMeters)
            {
                failure = "Thermal vertical gates must increase from base through cap.";
                return false;
            }
            if (peakUpdraftMetersPerSecond <= 0f)
            {
                failure = "Thermal peak updraft must be positive.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        public float SampleUpdraftMetersPerSecond(Vector3 positionUnityMeters)
        {
            float east = positionUnityMeters.x - centerUnityMeters.x;
            float north = positionUnityMeters.z - centerUnityMeters.z;
            float radius = Mathf.Sqrt(east * east + north * north);
            if (radius >= outerRadiusMeters || positionUnityMeters.y <= baseHeightUnityMeters ||
                positionUnityMeters.y >= capHeightUnityMeters)
                return 0f;

            float radial = radius <= coreRadiusMeters
                ? 1f
                : 1f - Smooth01((radius - coreRadiusMeters) / (outerRadiusMeters - coreRadiusMeters));
            float lower = Smooth01((positionUnityMeters.y - baseHeightUnityMeters) /
                                   (fullStrengthBottomUnityMeters - baseHeightUnityMeters));
            float upper = 1f - Smooth01((positionUnityMeters.y - fullStrengthTopUnityMeters) /
                                        (capHeightUnityMeters - fullStrengthTopUnityMeters));
            return peakUpdraftMetersPerSecond * radial * Mathf.Min(lower, upper);
        }

        private static float Smooth01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

#if UNITY_EDITOR
        private void OnValidate()
        {
            coreRadiusMeters = Mathf.Max(1f, coreRadiusMeters);
            outerRadiusMeters = Mathf.Max(coreRadiusMeters + 1f, outerRadiusMeters);
            baseHeightUnityMeters = Mathf.Max(0f, baseHeightUnityMeters);
            fullStrengthBottomUnityMeters = Mathf.Max(baseHeightUnityMeters + 1f, fullStrengthBottomUnityMeters);
            fullStrengthTopUnityMeters = Mathf.Max(fullStrengthBottomUnityMeters + 1f, fullStrengthTopUnityMeters);
            capHeightUnityMeters = Mathf.Max(fullStrengthTopUnityMeters + 1f, capHeightUnityMeters);
            peakUpdraftMetersPerSecond = Mathf.Max(0.01f, peakUpdraftMetersPerSecond);
        }
#endif
    }
}
