using System;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Compact, authored weather preset for the bounded KBDU product world.
    /// Positions are local ENU metres; reported altitudes retain the real KBDU
    /// MSL datum so the visual layer can be audited against aviation weather.
    /// </summary>
    [CreateAssetMenu(
        fileName = "KBDU_BoulderCloudProfile",
        menuName = "QuestFlight/Environment/Boulder Cloud Profile")]
    public sealed class BoulderCloudProfile : ScriptableObject
    {
        [Serializable]
        public struct Volume
        {
            public string id;
            public Vector3 centerLocalMeters;
            public Vector3 radiiMeters;
            public float baseMslMeters;
            public float topMslMeters;
            [Range(0f, 1f)] public float interiorDensity;

            public Volume(
                string volumeId,
                Vector3 center,
                Vector3 radii,
                float baseMsl,
                float topMsl,
                float density)
            {
                id = volumeId;
                centerLocalMeters = center;
                radiiMeters = radii;
                baseMslMeters = baseMsl;
                topMslMeters = topMsl;
                interiorDensity = density;
            }
        }

        [SerializeField] private string profileId = "KBDU_SUMMER_SCATTERED_V1";
        [SerializeField] private string weatherClass = "Front Range fair-weather cumulus";
        [SerializeField] private float airportElevationMslMeters = 1612.6f;
        [SerializeField] private float declaredBaseAglMeters = 1450f;
        [SerializeField] private float declaredTopAglMeters = 2550f;
        [SerializeField] private float coverageFraction = 0.32f;
        [SerializeField] private float visibilityMeters = 24000f;
        [SerializeField] private Color interiorFogColor = new Color(0.76f, 0.80f, 0.83f, 1f);
        [SerializeField] private float interiorFogDensity = 0.026f;
        [SerializeField] private float interiorSunIntensityMultiplier = 0.38f;
        [SerializeField] private float interiorSkyExposureMultiplier = 0.72f;
        [SerializeField] private Volume[] volumes = Array.Empty<Volume>();

        public string ProfileId => profileId;
        public string WeatherClass => weatherClass;
        public float AirportElevationMslMeters => airportElevationMslMeters;
        public float DeclaredBaseAglMeters => declaredBaseAglMeters;
        public float DeclaredTopAglMeters => declaredTopAglMeters;
        public float DeclaredBaseMslMeters => airportElevationMslMeters + declaredBaseAglMeters;
        public float DeclaredTopMslMeters => airportElevationMslMeters + declaredTopAglMeters;
        public float CoverageFraction => coverageFraction;
        public float VisibilityMeters => visibilityMeters;
        public Color InteriorFogColor => interiorFogColor;
        public float InteriorFogDensity => interiorFogDensity;
        public float InteriorSunIntensityMultiplier => interiorSunIntensityMultiplier;
        public float InteriorSkyExposureMultiplier => interiorSkyExposureMultiplier;
        public Volume[] Volumes => volumes;

#if UNITY_EDITOR
        public void Configure(
            string authoredProfileId,
            string authoredWeatherClass,
            float airportMsl,
            float baseAgl,
            float topAgl,
            float coverage,
            float visibility,
            Color fogColor,
            float fogDensity,
            float sunMultiplier,
            float skyMultiplier,
            Volume[] authoredVolumes)
        {
            profileId = authoredProfileId;
            weatherClass = authoredWeatherClass;
            airportElevationMslMeters = airportMsl;
            declaredBaseAglMeters = baseAgl;
            declaredTopAglMeters = topAgl;
            coverageFraction = coverage;
            visibilityMeters = visibility;
            interiorFogColor = fogColor;
            interiorFogDensity = fogDensity;
            interiorSunIntensityMultiplier = sunMultiplier;
            interiorSkyExposureMultiplier = skyMultiplier;
            volumes = authoredVolumes ?? Array.Empty<Volume>();
        }
#endif

        public bool TryValidate(out string report)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                report = "profile id is missing";
                return false;
            }
            if (airportElevationMslMeters < 1500f || airportElevationMslMeters > 1750f)
            {
                report = "KBDU MSL datum is outside its accepted range";
                return false;
            }
            if (declaredBaseAglMeters < 500f || declaredTopAglMeters <= declaredBaseAglMeters)
            {
                report = "declared cloud layer altitude is invalid";
                return false;
            }
            if (coverageFraction < 0.05f || coverageFraction > 0.60f)
            {
                report = "coverage is outside the bounded scattered-cloud contract";
                return false;
            }
            if (volumes == null || volumes.Length < 8 || volumes.Length > 24)
            {
                report = "cloud field must contain 8-24 authored volumes";
                return false;
            }
            for (int index = 0; index < volumes.Length; index++)
            {
                Volume volume = volumes[index];
                if (string.IsNullOrWhiteSpace(volume.id) ||
                    volume.radiiMeters.x < 80f || volume.radiiMeters.y < 80f ||
                    volume.radiiMeters.z < 80f ||
                    volume.baseMslMeters < airportElevationMslMeters + 400f ||
                    volume.topMslMeters <= volume.baseMslMeters ||
                    volume.interiorDensity <= 0f)
                {
                    report = $"cloud volume {index} is invalid";
                    return false;
                }
            }
            report = $"{profileId}: {volumes.Length} volumes, " +
                     $"base {DeclaredBaseMslMeters:0} m MSL, top {DeclaredTopMslMeters:0} m MSL";
            return true;
        }
    }
}
