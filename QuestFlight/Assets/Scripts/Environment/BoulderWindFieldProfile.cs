using System;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    public enum BoulderWeatherPreset { Calm = 0, FoothillSoaring = 1 }

    /// <summary>One deterministic world-space air mass for native physics and visual traffic.
    /// Terrain response is baked offline. Sampling never queries colliders or creates hierarchy.</summary>
    [CreateAssetMenu(menuName = "QuestFlight/Environment/Boulder Wind Field")]
    public sealed class BoulderWindFieldProfile : ScriptableObject
    {
        public ThermalFieldProfile legacyThermal;
        public Vector2 minimumEnu = new Vector2(-8000, -8000);
        public float spacingMeters = 100;
        public int width = 161, height = 161;
        public float[] groundHeights = Array.Empty<float>();
        // Smoothed slope deflection for the authored easterly wind, including upwind shelter.
        public float[] ridgeResponse = Array.Empty<float>();
        public Vector2 surfaceWindEastNorth = new Vector2(-2.5f, 0);
        public Vector2 ridgeWindEastNorth = new Vector2(-5.5f, 0);
        public float ridgeInfluenceHeight = 450;
        public float gustAmplitude = 0.35f;
        public Thermal[] foothillThermals = Array.Empty<Thermal>();

        [Serializable] public struct Thermal
        {
            public Vector2 centerEnu;
            public float groundHeight, radius, topAgl, updraft;
        }

        public bool IsValid(out string error)
        {
            error = null;
            if (legacyThermal == null || !legacyThermal.IsValid(out error))
                error = "Wind field requires the accepted thermal source: " + error;
            else if (width < 2 || height < 2 || width > 512 || height > 512 ||
                !Finite(spacingMeters) || spacingMeters <= 0 || !Finite(minimumEnu.x) || !Finite(minimumEnu.y) ||
                groundHeights == null || ridgeResponse == null || groundHeights.Length != width * height ||
                ridgeResponse.Length != width * height || !Finite(ridgeInfluenceHeight) || ridgeInfluenceHeight < 1 ||
                !Finite(gustAmplitude) || gustAmplitude < 0 || gustAmplitude > 2 ||
                !Finite(surfaceWindEastNorth.x) || !Finite(surfaceWindEastNorth.y) ||
                !Finite(ridgeWindEastNorth.x) || !Finite(ridgeWindEastNorth.y) ||
                surfaceWindEastNorth.magnitude > 15 || ridgeWindEastNorth.magnitude > 20)
                error = "Wind field has invalid dimensions or wind parameters.";
            else
            {
                for (int i = 0; i < groundHeights.Length; i++)
                    if (!Finite(groundHeights[i]) || !Finite(ridgeResponse[i]) || Mathf.Abs(ridgeResponse[i]) > 3)
                    { error = "Wind grid contains an invalid value."; break; }
                if (foothillThermals == null) error = "Thermal array is missing.";
                else foreach (Thermal t in foothillThermals)
                    if (!Finite(t.centerEnu.x) || !Finite(t.centerEnu.y) || !Finite(t.groundHeight) ||
                        !Finite(t.radius) || t.radius < 20 || !Finite(t.topAgl) || t.topAgl < 100 ||
                        !Finite(t.updraft) || t.updraft < 0 || t.updraft > 5)
                        error = "Wind field has an invalid foothill thermal.";
            }
            return string.IsNullOrEmpty(error);
        }

        public Vector3 Sample(Vector3 position, double simulationTime, BoulderWeatherPreset preset)
        {
            if (!Finite(position) || double.IsNaN(simulationTime) || double.IsInfinity(simulationTime))
                return new Vector3(float.NaN, float.NaN, float.NaN);
            float up = legacyThermal.SampleUpdraftMetersPerSecond(position);
            if (preset == BoulderWeatherPreset.Calm) return new Vector3(0, up, 0);
            if (preset != BoulderWeatherPreset.FoothillSoaring) return Vector3.one * float.NaN;
            SampleGrid(position.x, position.z, out float ground, out float ridge);
            float agl = Mathf.Max(0, position.y - ground);
            Vector2 wind = Vector2.Lerp(surfaceWindEastNorth, ridgeWindEastNorth, Smooth(agl / 900));
            float gust = gustAmplitude * (0.65f * Mathf.Sin((float)(simulationTime * 0.17) + position.x * 0.0008f) +
                0.35f * Mathf.Sin((float)(simulationTime * 0.071) + position.z * 0.001f));
            wind *= 1 + gust / Mathf.Max(1, wind.magnitude);
            wind *= Mathf.Lerp(0.65f, 1, Smooth(agl / 15));
            float fade = 1 - Smooth(agl / ridgeInfluenceHeight);
            up += Mathf.Clamp(ridge * wind.magnitude, -3, 3) * fade;
            foreach (Thermal t in foothillThermals)
            {
                float thermalAgl = position.y - t.groundHeight;
                if (thermalAgl <= 0 || thermalAgl >= t.topAgl) continue;
                // Tilt the rising column with its age since leaving its ground heat source.
                Vector2 center = t.centerEnu + wind * Mathf.Min(90, thermalAgl / Mathf.Max(1, t.updraft));
                float r = (new Vector2(position.x, position.z) - center).magnitude / t.radius;
                if (r >= 2) continue;
                float core = r <= 1 ? 1 - Smooth(r) : -0.12f * Mathf.Sin((r - 1) * Mathf.PI);
                up += t.updraft * core * Smooth(thermalAgl / 120) * (1 - Smooth((thermalAgl - t.topAgl * 0.65f) / (t.topAgl * 0.35f)));
            }
            return new Vector3(wind.x, Mathf.Clamp(up, -5, 5), wind.y);
        }

        public void SampleGrid(float east, float north, out float ground, out float ridge)
        {
            float x = Mathf.Clamp((east - minimumEnu.x) / spacingMeters, 0, width - 1);
            float z = Mathf.Clamp((north - minimumEnu.y) / spacingMeters, 0, height - 1);
            int ix = Mathf.Min((int)x, width - 2), iz = Mathf.Min((int)z, height - 2);
            int i = iz * width + ix;
            float tx = x - ix, tz = z - iz;
            ground = Bilinear(groundHeights, i, tx, tz);
            ridge = Bilinear(ridgeResponse, i, tx, tz);
            float edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(width - 1 - x, height - 1 - z));
            ridge *= Smooth(edge / 3); // No lift discontinuity at the baked domain boundary.
        }

        private float Bilinear(float[] values, int i, float x, float z) =>
            Mathf.Lerp(Mathf.Lerp(values[i], values[i + 1], x), Mathf.Lerp(values[i + width], values[i + width + 1], x), z);
        public static float Smooth(float value) { float t = Mathf.Clamp01(value); return t * t * (3 - 2 * t); }
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
