using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.PhotorealAerialSurfaceV1.Reflection
{
    public enum CockpitSunShadowCasterScope
    {
        CockpitInteriorOnly = 0
    }

    [Serializable]
    public struct CockpitSunShadowCandidate
    {
        [SerializeField] private bool enabledByDefault;
        [SerializeField] private bool acceptedOnQuestHardware;
        [SerializeField] private CockpitSunShadowCasterScope casterScope;
        [SerializeField] private int mapResolution;
        [SerializeField] private int cascadeCount;
        [SerializeField] private float distanceMeters;
        [SerializeField] private float strength;
        [SerializeField] private float bias;
        [SerializeField] private float normalBias;
        [SerializeField] private float nearPlaneMeters;

        public bool EnabledByDefault => enabledByDefault;
        public bool AcceptedOnQuestHardware => acceptedOnQuestHardware;
        public CockpitSunShadowCasterScope CasterScope => casterScope;
        public int MapResolution => mapResolution;
        public int CascadeCount => cascadeCount;
        public float DistanceMeters => distanceMeters;
        public float Strength => strength;
        public float Bias => bias;
        public float NormalBias => normalBias;
        public float NearPlaneMeters => nearPlaneMeters;
    }

    /// <summary>
    /// Immutable-at-runtime data contract for the Photoreal Aerial Surface V1 reflection
    /// foundation. Runtime systems may read this asset, but it never creates hierarchy,
    /// renders a probe, changes a camera, or switches the development shadow candidate.
    /// </summary>
    [CreateAssetMenu(
        fileName = "KBDU_PhotorealAerialReflectionProfile",
        menuName = "QuestFlight/Photoreal Aerial Surface V1/Reflection Profile")]
    public sealed class PhotorealAerialReflectionProfile : ScriptableObject
    {
        public const string Schema = "questflight.photoreal_aerial_surface.reflection.v1";
        public const int RequiredCubemapFaceSize = 128;
        public const int RequiredAirportProbeCount = 1;
        public const int MaximumRuntimeProbeRenderCount = 0;

        [Header("Contract identity")]
        [SerializeField] private string schema = Schema;
        [SerializeField] private string status = "FOUNDATION_AUTHORED_NOT_VISUALLY_ACCEPTED";

        [Header("Global ground + sky reflection")]
        [SerializeField] private Cubemap globalGroundSkyReflection;
        [SerializeField] private Color lowerHemisphereGroundColor =
            new Color(0.115f, 0.128f, 0.095f, 1f);
        [SerializeField, Range(0f, 1f)] private float globalReflectionIntensity = 0.68f;

        [Header("Bounded airport probe")]
        [SerializeField] private Cubemap airportLocalReflection;
        [SerializeField] private Vector3 airportProbeLocalPosition = new Vector3(-385f, 5f, -260f);
        [SerializeField] private Vector3 airportProbeSize = new Vector3(260f, 35f, 240f);
        [SerializeField, Min(0f)] private float airportProbeBlendDistance = 24f;
        [SerializeField, Range(0f, 2f)] private float airportProbeIntensity = 0.72f;
        [SerializeField] private int airportProbeResolution = RequiredCubemapFaceSize;
        [SerializeField] private bool airportProbeBoxProjection;
        [SerializeField] private bool runtimeProbeUpdates;

        [Header("Cockpit-only development shadow candidate")]
        [SerializeField] private CockpitSunShadowCandidate cockpitSunShadowCandidate;

        public string ContractSchema => schema;
        public string Status => status;
        public Cubemap GlobalGroundSkyReflection => globalGroundSkyReflection;
        public Color LowerHemisphereGroundColor => lowerHemisphereGroundColor;
        public float GlobalReflectionIntensity => globalReflectionIntensity;
        public Cubemap AirportLocalReflection => airportLocalReflection;
        public Vector3 AirportProbeLocalPosition => airportProbeLocalPosition;
        public Vector3 AirportProbeSize => airportProbeSize;
        public float AirportProbeBlendDistance => airportProbeBlendDistance;
        public float AirportProbeIntensity => airportProbeIntensity;
        public int AirportProbeResolution => airportProbeResolution;
        public bool AirportProbeBoxProjection => airportProbeBoxProjection;
        public bool RuntimeProbeUpdates => runtimeProbeUpdates;
        public CockpitSunShadowCandidate CockpitShadowCandidate => cockpitSunShadowCandidate;

        public bool TryValidate(out string report)
        {
            List<string> failures = new List<string>();
            Require(string.Equals(schema, Schema, StringComparison.Ordinal),
                "reflection schema changed", failures);
            Require(!string.IsNullOrWhiteSpace(status) &&
                    status.IndexOf("NOT_VISUALLY_ACCEPTED", StringComparison.Ordinal) >= 0,
                "acceptance boundary is missing", failures);
            Require(globalGroundSkyReflection != null &&
                    globalGroundSkyReflection.dimension == TextureDimension.Cube,
                "global reflection cubemap is missing", failures);
            Require(globalGroundSkyReflection == null ||
                    globalGroundSkyReflection.width == RequiredCubemapFaceSize,
                "global reflection cubemap must be 128 pixels per face", failures);
            Require(airportLocalReflection != null &&
                    airportLocalReflection.dimension == TextureDimension.Cube,
                "airport-local reflection cubemap is missing", failures);
            Require(airportLocalReflection == null ||
                    airportLocalReflection.width == RequiredCubemapFaceSize,
                "airport-local reflection cubemap must be 128 pixels per face", failures);
            Require(globalGroundSkyReflection == null || airportLocalReflection == null ||
                    globalGroundSkyReflection != airportLocalReflection,
                "global and airport-local reflections must be distinct assets", failures);
            Require(globalReflectionIntensity > 0f && globalReflectionIntensity <= 1f,
                "global reflection intensity is outside the bounded range", failures);
            Require(IsFinite(airportProbeLocalPosition) && IsFinite(airportProbeSize) &&
                    airportProbeSize.x > 0f && airportProbeSize.y > 0f && airportProbeSize.z > 0f,
                "airport probe bounds are invalid", failures);
            Require(airportProbeBlendDistance >= 0f &&
                    airportProbeBlendDistance * 2f < Mathf.Min(airportProbeSize.x, airportProbeSize.z),
                "airport probe blend distance is invalid", failures);
            Require(airportProbeResolution == RequiredCubemapFaceSize,
                "airport probe resolution must remain 128", failures);
            Require(!airportProbeBoxProjection,
                "generic ground-sky reflection may not use misleading box projection", failures);
            Require(!runtimeProbeUpdates,
                "runtime reflection probe updates are forbidden", failures);

            CockpitSunShadowCandidate shadow = cockpitSunShadowCandidate;
            Require(!shadow.EnabledByDefault && !shadow.AcceptedOnQuestHardware,
                "development shadow candidate must remain default-off and unaccepted", failures);
            Require(shadow.CasterScope == CockpitSunShadowCasterScope.CockpitInteriorOnly,
                "shadow candidate scope must remain cockpit-only", failures);
            Require(shadow.MapResolution == 1024 && shadow.CascadeCount == 0,
                "shadow candidate must remain 1024 with no cascades", failures);
            Require(shadow.DistanceMeters >= 8f && shadow.DistanceMeters <= 16f,
                "shadow candidate distance is not short/bounded", failures);
            Require(shadow.Strength > 0f && shadow.Strength <= 0.65f &&
                    shadow.Bias >= 0f && shadow.NormalBias >= 0f && shadow.NearPlaneMeters > 0f,
                "shadow candidate tuning is invalid", failures);

            report = failures.Count == 0
                ? "PASS: distinct static 128-face global and airport-local ground-sky reflections, one bounded airport influence, " +
                  "zero runtime probe renders, and one default-off cockpit shadow candidate"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static void Require(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
