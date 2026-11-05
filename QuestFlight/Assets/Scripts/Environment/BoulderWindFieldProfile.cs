using System;
using UnityEngine;
using Unity.Profiling;

namespace QuestFlightLab.Environment
{
    public enum BoulderWeatherPreset { Calm = 0, FoothillSoaring = 1 }

    /// <summary>One deterministic world-space air mass for native physics and visual traffic.
    /// Terrain response is baked offline. Sampling never queries colliders or creates hierarchy.</summary>
    [CreateAssetMenu(menuName = "QuestFlight/Environment/Boulder Wind Field")]
    public sealed class BoulderWindFieldProfile : ScriptableObject
    {
        private static readonly ProfilerMarker SampleMarker=new ProfilerMarker("QuestFlight.SharedAtmosphere.Sample");
        public ThermalFieldProfile legacyThermal;
        public Vector2 minimumEnu = new Vector2(-8000, -8000);
        public float spacingMeters = 100;
        public int width = 161, height = 161;
        public float[] groundHeights = Array.Empty<float>();
        // Retained serialized legacy data for migration; the shared sampler uses directional gradients.
        public float[] ridgeResponse = Array.Empty<float>();
        public float[] gradientEast = Array.Empty<float>(), gradientNorth = Array.Empty<float>();
        public float[] exposureSectors = Array.Empty<float>(), ridgeRelief = Array.Empty<float>();
        public float windTransitionHeight = 450;
        public float thermalStrength = 1;
        public int gustSeed = 1729;
        public struct Components
        {
            public Vector3 background, gust;
            public float thermal, terrain;
            public Vector3 Total => background + gust + Vector3.up * (thermal + terrain);
        }
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
                ridgeResponse.Length != width * height ||
                gradientEast == null || gradientNorth == null || exposureSectors == null || ridgeRelief == null ||
                gradientEast.Length != width * height || gradientNorth.Length != width * height ||
                ridgeRelief.Length != width * height || exposureSectors.Length != width * height * 8 ||
                !Finite(thermalStrength) || thermalStrength < 0 || thermalStrength > 2 ||
                !Finite(windTransitionHeight) || windTransitionHeight < 20 || !Finite(ridgeInfluenceHeight) || ridgeInfluenceHeight < 1 ||
                !Finite(gustAmplitude) || gustAmplitude < 0 || gustAmplitude > 2 ||
                !Finite(surfaceWindEastNorth.x) || !Finite(surfaceWindEastNorth.y) ||
                !Finite(ridgeWindEastNorth.x) || !Finite(ridgeWindEastNorth.y) ||
                surfaceWindEastNorth.magnitude > 15 || ridgeWindEastNorth.magnitude > 20)
                error = "Wind field has invalid dimensions or wind parameters.";
            else
            {
                for (int i = 0; i < groundHeights.Length; i++)
                    if (!Finite(groundHeights[i]) || !Finite(ridgeResponse[i]) || Mathf.Abs(ridgeResponse[i]) > 3 || !Finite(gradientEast[i]) || !Finite(gradientNorth[i]) ||
                        Mathf.Abs(gradientEast[i]) > 3 || Mathf.Abs(gradientNorth[i]) > 3 || !Finite(ridgeRelief[i]) || ridgeRelief[i] < 0)
                    { error = "Wind grid contains an invalid value."; break; }
                foreach (float exposure in exposureSectors)
                    if (!Finite(exposure) || exposure < 0 || exposure > 1) { error = "Invalid directional exposure."; break; }
                if (foothillThermals == null) error = "Thermal array is missing.";
                else foreach (Thermal t in foothillThermals)
                    if (!Finite(t.centerEnu.x) || !Finite(t.centerEnu.y) || !Finite(t.groundHeight) ||
                        !Finite(t.radius) || t.radius < 20 || !Finite(t.topAgl) || t.topAgl < 100 ||
                        !Finite(t.updraft) || t.updraft < 0 || t.updraft > 5)
                        error = "Wind field has an invalid foothill thermal.";
            }
            return string.IsNullOrEmpty(error);
        }

        // Compatibility argument describes old fixtures only; it never changes atmospheric physics.
        public Vector3 Sample(Vector3 position, double simulationTime, BoulderWeatherPreset preset) =>
            Sample(position, simulationTime);

        public Vector3 Sample(Vector3 position, double simulationTime) => SampleComponents(position, simulationTime).Total;

        public Components SampleComponents(Vector3 position, double simulationTime)
            => SampleCore(position,simulationTime,surfaceWindEastNorth,ridgeWindEastNorth,gustAmplitude,true);

        public Components SampleConfiguredFlow(Vector3 position,double time,Vector2 surface,Vector2 upper,float gust)
            => SampleCore(position,time,surface,upper,gust,false);

        private Components SampleCore(Vector3 position,double simulationTime,Vector2 surface,Vector2 upper,float gustStrength,bool legacyLift)
        {
            using var sampleScope=SampleMarker.Auto();
            if (!Finite(position) || !Finite(surface.x) || !Finite(surface.y) ||
                !Finite(upper.x) || !Finite(upper.y) || double.IsNaN(simulationTime) || double.IsInfinity(simulationTime))
                return new Components { background = Vector3.one * float.NaN };
            Grid(position.x, position.z, out int i, out float tx, out float tz, out float edge);
            float ground = Bilinear(groundHeights, i, tx, tz);
            float agl = Mathf.Max(0, position.y - ground);
            Vector2 wind = Vector2.Lerp(surface, upper, Smooth(agl / windTransitionHeight));
            wind *= Mathf.Lerp(.65f, 1, Smooth(agl / 15));
            float magnitude = wind.magnitude;
            float sector = Mathf.Repeat(Mathf.Atan2(wind.y, wind.x) * 4 / Mathf.PI, 8);
            int low = (int)sector, cells = width * height;
            float exposure = Mathf.Lerp(Bilinear(exposureSectors, i + low * cells, tx, tz),
                Bilinear(exposureSectors, i + ((low + 1) % 8) * cells, tx, tz), sector - low);
            float relief = Bilinear(ridgeRelief, i, tx, tz);
            float influence = Mathf.Min(ridgeInfluenceHeight, Mathf.Max(60, relief * 1.5f));
            float ridge = (wind.x * Bilinear(gradientEast, i, tx, tz) + wind.y * Bilinear(gradientNorth, i, tx, tz)) * exposure;
            ridge = Mathf.Clamp(ridge, -3, 3) * (1 - Smooth(agl / influence)) * edge;
            float time = (float)simulationTime;
            float seed = gustSeed * .017f;
            // Smooth fixed-cost fluctuations advect with the mean air mass, including vertical motion.
            float x = (position.x - wind.x * time) * .007f;
            float z = (position.z - wind.y * time) * .006f;
            float y = position.y * .009f;
            float amplitude = gustStrength * Mathf.Clamp01(magnitude / 3) * Mathf.Lerp(.3f, 1, exposure) * Smooth(agl / 20);
            Vector3 gust = amplitude * new Vector3(
                .65f * Mathf.Sin(x + z + time * .31f + seed) + .35f * Mathf.Sin(y - z + time * .73f),
                .45f * Mathf.Sin(x - z + time * .43f + seed) + .2f * Mathf.Sin(y + z + time * 1.13f),
                .65f * Mathf.Sin(z - y + time * .37f - seed) + .35f * Mathf.Sin(x + y + time * .89f));
            float thermal = 0;
            if (legacyLift && thermalStrength > 0)
            {
                thermal = SampleThermal(AirportThermal, position, wind);
                foreach (Thermal t in foothillThermals) thermal += SampleThermal(t, position, wind);
            }
            return new Components { background = new Vector3(wind.x, 0, wind.y), gust = gust,
                terrain = ridge, thermal = Mathf.Clamp(thermal * thermalStrength, -3, 5) };
        }

        public Thermal AirportThermal => new Thermal {
            centerEnu = new Vector2(legacyThermal.centerUnityMeters.x, legacyThermal.centerUnityMeters.z),
            groundHeight = legacyThermal.baseHeightUnityMeters,
            radius = legacyThermal.outerRadiusMeters, topAgl = legacyThermal.capHeightUnityMeters - legacyThermal.baseHeightUnityMeters,
            updraft = legacyThermal.peakUpdraftMetersPerSecond };

        public static Vector2 ThermalCenter(Thermal t, float altitude, Vector2 wind) =>
            t.centerEnu + wind * Mathf.Min(90, Mathf.Max(0, altitude - t.groundHeight) / Mathf.Max(1, t.updraft));

        private static float SampleThermal(Thermal t, Vector3 position, Vector2 wind)
        {
            float agl = position.y - t.groundHeight;
            if (agl <= 0 || agl >= t.topAgl) return 0;
            float r = (new Vector2(position.x, position.z) - ThermalCenter(t, position.y, wind)).magnitude / t.radius;
            if (r >= 2) return 0;
            float core = r < 1 ? 1 - Smooth(r) : -.12f * Mathf.Sin((r - 1) * Mathf.PI);
            return t.updraft * core * Smooth(agl / 120) * (1 - Smooth((agl - t.topAgl * .65f) / (t.topAgl * .35f)));
        }

        private void Grid(float east, float north, out int i, out float tx, out float tz, out float edge)
        {
            float x = Mathf.Clamp((east - minimumEnu.x) / spacingMeters, 0, width - 1);
            float z = Mathf.Clamp((north - minimumEnu.y) / spacingMeters, 0, height - 1);
            int ix = Mathf.Min((int)x, width - 2), iz = Mathf.Min((int)z, height - 2);
            i = iz * width + ix; tx = x - ix; tz = z - iz;
            edge = Smooth(Mathf.Min(Mathf.Min(x, z), Mathf.Min(width - 1 - x, height - 1 - z)) / 3);
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
