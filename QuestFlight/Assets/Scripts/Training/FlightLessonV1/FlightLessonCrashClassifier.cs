using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    public enum FlightLessonCrashReason
    {
        None = 0,
        NonFiniteAuthoritativeState = 1,
        TerrainPenetration = 2,
        WaterImpact = 3,
        UnsafeSurfaceContact = 4,
        HardLanding = 5,
        UnrecoverableLowAltitudeState = 6,
        LessonAreaExit = 7
    }

    public struct FlightLessonAircraftSafetyState
    {
        public Vector3 positionUnityMeters;
        public Quaternion rotationUnity;
        public Vector3 velocityUnityMetersPerSecond;
        public float altitudeAglMeters;
        public float calibratedAirspeedKnots;
        public float groundSpeedKnots;
        public float verticalSpeedFeetPerMinute;
        public float pitchDegrees;
        public float bankDegrees;
        public bool weightOnWheels;

        public bool IsFinite =>
            IsFiniteNumber(positionUnityMeters.x) && IsFiniteNumber(positionUnityMeters.y) && IsFiniteNumber(positionUnityMeters.z) &&
            IsFiniteNumber(rotationUnity.x) && IsFiniteNumber(rotationUnity.y) && IsFiniteNumber(rotationUnity.z) && IsFiniteNumber(rotationUnity.w) &&
            IsFiniteNumber(velocityUnityMetersPerSecond.x) && IsFiniteNumber(velocityUnityMetersPerSecond.y) && IsFiniteNumber(velocityUnityMetersPerSecond.z) &&
            IsFiniteNumber(altitudeAglMeters) && IsFiniteNumber(calibratedAirspeedKnots) && IsFiniteNumber(groundSpeedKnots) &&
            IsFiniteNumber(verticalSpeedFeetPerMinute) && IsFiniteNumber(pitchDegrees) && IsFiniteNumber(bankDegrees);

        private static bool IsFiniteNumber(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public struct FlightLessonTerrainSample
    {
        public bool isValid;
        public float heightUnityMeters;
        public Vector3 normalUnity;
        public bool isRunwayOrApron;
        public bool isWater;
    }

    public struct FlightLessonCrashSettings
    {
        public float maximumTerrainPenetrationMeters;
        public float penetrationDebounceSeconds;
        public float hardLandingSinkRateFeetPerMinute;
        public float maximumTouchdownBankDegrees;
        public float maximumTouchdownPitchDegrees;
        public float unsafeSurfaceGroundSpeedKnots;
        public float unsafeSurfaceDebounceSeconds;
        public float lowAltitudeMeters;
        public float lowAirspeedKnots;
        public float lowAltitudeSinkRateFeetPerMinute;
        public float lowAltitudeDebounceSeconds;
        public float maximumLessonRadiusMeters;

        // Recovery thresholds are simulator policy, not POH limitations or certified data.
        public static FlightLessonCrashSettings ConservativeTrainingDefaults => new FlightLessonCrashSettings
        {
            maximumTerrainPenetrationMeters = 0.75f,
            penetrationDebounceSeconds = 0.15f,
            hardLandingSinkRateFeetPerMinute = 850f,
            maximumTouchdownBankDegrees = 18f,
            maximumTouchdownPitchDegrees = 16f,
            unsafeSurfaceGroundSpeedKnots = 28f,
            unsafeSurfaceDebounceSeconds = 0.35f,
            lowAltitudeMeters = 18f,
            lowAirspeedKnots = 42f,
            lowAltitudeSinkRateFeetPerMinute = -700f,
            lowAltitudeDebounceSeconds = 0.75f,
            maximumLessonRadiusMeters = 9000f
        };
    }

    /// <summary>
    /// Stateful only for contact edges and debouncing; callers provide deterministic dt.
    /// Route deviations and poor maneuver quality are intentionally not classified as crashes.
    /// </summary>
    public sealed class FlightLessonCrashClassifier
    {
        private bool hasPrevious;
        private bool previousWeightOnWheels;
        private float previousVerticalSpeedFeetPerMinute;
        private float penetrationSeconds;
        private float unsafeSurfaceSeconds;
        private float lowStateSeconds;

        public void Reset(bool weightOnWheels = false)
        {
            hasPrevious = false;
            previousWeightOnWheels = weightOnWheels;
            previousVerticalSpeedFeetPerMinute = 0f;
            penetrationSeconds = 0f;
            unsafeSurfaceSeconds = 0f;
            lowStateSeconds = 0f;
        }

        public FlightLessonCrashReason Evaluate(
            in FlightLessonAircraftSafetyState state,
            in FlightLessonTerrainSample terrain,
            float deltaTimeSeconds,
            in FlightLessonCrashSettings settings)
        {
            float dt = Mathf.Max(0f, deltaTimeSeconds);
            if (!state.IsFinite)
            {
                Remember(state.weightOnWheels, state.verticalSpeedFeetPerMinute);
                return FlightLessonCrashReason.NonFiniteAuthoritativeState;
            }

            if (new Vector2(state.positionUnityMeters.x, state.positionUnityMeters.z).magnitude > settings.maximumLessonRadiusMeters)
            {
                Remember(state.weightOnWheels, state.verticalSpeedFeetPerMinute);
                return FlightLessonCrashReason.LessonAreaExit;
            }

            if (terrain.isValid)
            {
                float penetration = terrain.heightUnityMeters - state.positionUnityMeters.y;
                penetrationSeconds = penetration > settings.maximumTerrainPenetrationMeters
                    ? penetrationSeconds + dt
                    : 0f;
                if (penetrationSeconds >= settings.penetrationDebounceSeconds)
                {
                    Remember(state.weightOnWheels, state.verticalSpeedFeetPerMinute);
                    return FlightLessonCrashReason.TerrainPenetration;
                }
            }

            bool contactEdge = state.weightOnWheels && (!hasPrevious || !previousWeightOnWheels);
            if (state.weightOnWheels && terrain.isValid && terrain.isWater)
            {
                Remember(true, state.verticalSpeedFeetPerMinute);
                return FlightLessonCrashReason.WaterImpact;
            }

            bool unsafeSurfaceContact = state.weightOnWheels && terrain.isValid &&
                !terrain.isRunwayOrApron && !terrain.isWater &&
                state.groundSpeedKnots > settings.unsafeSurfaceGroundSpeedKnots;
            unsafeSurfaceSeconds = unsafeSurfaceContact
                ? unsafeSurfaceSeconds + dt
                : 0f;
            if (unsafeSurfaceSeconds >= settings.unsafeSurfaceDebounceSeconds)
            {
                Remember(true, state.verticalSpeedFeetPerMinute);
                return FlightLessonCrashReason.UnsafeSurfaceContact;
            }

            float touchdownVerticalSpeed = hasPrevious
                ? Mathf.Min(previousVerticalSpeedFeetPerMinute,
                            state.verticalSpeedFeetPerMinute)
                : state.verticalSpeedFeetPerMinute;
            if (contactEdge &&
                (-touchdownVerticalSpeed > settings.hardLandingSinkRateFeetPerMinute ||
                 Mathf.Abs(state.bankDegrees) > settings.maximumTouchdownBankDegrees ||
                 Mathf.Abs(state.pitchDegrees) > settings.maximumTouchdownPitchDegrees))
            {
                Remember(true, state.verticalSpeedFeetPerMinute);
                return FlightLessonCrashReason.HardLanding;
            }

            bool unrecoverableLowState = !state.weightOnWheels &&
                state.altitudeAglMeters < settings.lowAltitudeMeters &&
                state.calibratedAirspeedKnots < settings.lowAirspeedKnots &&
                state.verticalSpeedFeetPerMinute < settings.lowAltitudeSinkRateFeetPerMinute;
            lowStateSeconds = unrecoverableLowState ? lowStateSeconds + dt : 0f;
            if (lowStateSeconds >= settings.lowAltitudeDebounceSeconds)
            {
                Remember(false, state.verticalSpeedFeetPerMinute);
                return FlightLessonCrashReason.UnrecoverableLowAltitudeState;
            }

            Remember(state.weightOnWheels, state.verticalSpeedFeetPerMinute);
            return FlightLessonCrashReason.None;
        }

        private void Remember(
            bool weightOnWheels,
            float verticalSpeedFeetPerMinute)
        {
            hasPrevious = true;
            previousWeightOnWheels = weightOnWheels;
            previousVerticalSpeedFeetPerMinute = verticalSpeedFeetPerMinute;
        }
    }
}
