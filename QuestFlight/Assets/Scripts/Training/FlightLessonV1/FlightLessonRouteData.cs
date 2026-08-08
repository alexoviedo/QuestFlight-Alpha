using System;
using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    public enum FlightLessonPathGeometry
    {
        Line = 0,
        Arc = 1
    }

    public enum FlightLessonSegmentKind
    {
        Departure = 0,
        Climb = 1,
        Cruise = 2,
        ScenicOverflight = 3,
        Return = 4,
        PatternEntry = 5,
        Downwind = 6,
        Base = 7,
        Final = 8,
        Rollout = 9
    }

    [Serializable]
    public struct FlightLessonWaypoint
    {
        public string id;
        public Vector3 positionUnityMeters;
        public float altitudeMslFeet;
        public float calibratedAirspeedKnots;
        public float captureRadiusMeters;

        public FlightLessonWaypoint(
            string id,
            Vector3 positionUnityMeters,
            float altitudeMslFeet,
            float calibratedAirspeedKnots,
            float captureRadiusMeters)
        {
            this.id = id;
            this.positionUnityMeters = positionUnityMeters;
            this.altitudeMslFeet = altitudeMslFeet;
            this.calibratedAirspeedKnots = calibratedAirspeedKnots;
            this.captureRadiusMeters = captureRadiusMeters;
        }
    }

    [Serializable]
    public struct FlightLessonSegment
    {
        public string id;
        public FlightLessonSegmentKind kind;
        public FlightLessonPathGeometry geometry;
        public int startWaypointIndex;
        public int endWaypointIndex;
        public Vector3 arcCenterUnityMeters;
        public bool arcClockwise;
        public float lookAheadMeters;
        public float maximumBankDegrees;
        public float maximumClimbRateFeetPerMinute;
        public float completionAltitudeToleranceFeet;
        public int recoveryCheckpointOnCompletion;

        public FlightLessonSegment(
            string id,
            FlightLessonSegmentKind kind,
            int startWaypointIndex,
            int endWaypointIndex,
            float lookAheadMeters,
            float maximumBankDegrees,
            float maximumClimbRateFeetPerMinute,
            float completionAltitudeToleranceFeet = 0f,
            int recoveryCheckpointOnCompletion = -1)
        {
            this.id = id;
            this.kind = kind;
            geometry = FlightLessonPathGeometry.Line;
            this.startWaypointIndex = startWaypointIndex;
            this.endWaypointIndex = endWaypointIndex;
            arcCenterUnityMeters = default;
            arcClockwise = false;
            this.lookAheadMeters = lookAheadMeters;
            this.maximumBankDegrees = maximumBankDegrees;
            this.maximumClimbRateFeetPerMinute = maximumClimbRateFeetPerMinute;
            this.completionAltitudeToleranceFeet = completionAltitudeToleranceFeet;
            this.recoveryCheckpointOnCompletion = recoveryCheckpointOnCompletion;
        }
    }

    [Serializable]
    public struct FlightLessonRecoveryCheckpoint
    {
        public string id;
        public int resumeSegmentIndex;
        public Vector3 positionUnityMeters;
        public float altitudeMslFeet;
        public float calibratedAirspeedKnots;
        public float headingDegreesTrue;
        public float pitchDegrees;
        public float bankDegrees;
        public bool engineRunning;

        public FlightLessonRecoveryCheckpoint(
            string id,
            int resumeSegmentIndex,
            Vector3 positionUnityMeters,
            float altitudeMslFeet,
            float calibratedAirspeedKnots,
            float headingDegreesTrue,
            float pitchDegrees = 0f,
            float bankDegrees = 0f,
            bool engineRunning = true)
        {
            this.id = id;
            this.resumeSegmentIndex = resumeSegmentIndex;
            this.positionUnityMeters = positionUnityMeters;
            this.altitudeMslFeet = altitudeMslFeet;
            this.calibratedAirspeedKnots = calibratedAirspeedKnots;
            this.headingDegreesTrue = headingDegreesTrue;
            this.pitchDegrees = pitchDegrees;
            this.bankDegrees = bankDegrees;
            this.engineRunning = engineRunning;
        }
    }

    [Serializable]
    public sealed class FlightLessonRouteDefinition
    {
        public string schemaVersion = "1.0";
        public string lessonId;
        public string displayName;
        [TextArea] public string operationalDisclaimer;
        public string[] provenanceUrls = Array.Empty<string>();
        public string[] officialOperatingFacts = Array.Empty<string>();
        public int initialRecoveryCheckpointIndex = -1;
        public FlightLessonWaypoint[] waypoints = Array.Empty<FlightLessonWaypoint>();
        public FlightLessonSegment[] segments = Array.Empty<FlightLessonSegment>();
        public FlightLessonRecoveryCheckpoint[] recoveryCheckpoints = Array.Empty<FlightLessonRecoveryCheckpoint>();

        public bool TryValidate(out string failure)
        {
            if (string.IsNullOrWhiteSpace(lessonId))
            {
                failure = "Lesson ID is required.";
                return false;
            }

            if (waypoints == null || waypoints.Length < 2)
            {
                failure = "At least two waypoints are required.";
                return false;
            }

            if (segments == null || segments.Length == 0)
            {
                failure = "At least one segment is required.";
                return false;
            }

            if (initialRecoveryCheckpointIndex < -1 ||
                initialRecoveryCheckpointIndex >= (recoveryCheckpoints?.Length ?? 0))
            {
                failure = "The initial recovery checkpoint index is invalid.";
                return false;
            }

            if (initialRecoveryCheckpointIndex >= 0 &&
                recoveryCheckpoints[initialRecoveryCheckpointIndex].resumeSegmentIndex != 0)
            {
                failure = "The initial recovery checkpoint must resume the first segment.";
                return false;
            }

            for (int i = 0; i < waypoints.Length; i++)
            {
                FlightLessonWaypoint waypoint = waypoints[i];
                if (string.IsNullOrWhiteSpace(waypoint.id) ||
                    !IsFinite(waypoint.positionUnityMeters) ||
                    !IsFinite(waypoint.altitudeMslFeet) ||
                    !IsFinite(waypoint.calibratedAirspeedKnots) ||
                    waypoint.captureRadiusMeters <= 0f)
                {
                    failure = $"Waypoint {i} is invalid.";
                    return false;
                }
            }

            for (int i = 0; i < segments.Length; i++)
            {
                FlightLessonSegment segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment.id) ||
                    (segment.geometry != FlightLessonPathGeometry.Line && segment.geometry != FlightLessonPathGeometry.Arc) ||
                    segment.startWaypointIndex < 0 || segment.startWaypointIndex >= waypoints.Length ||
                    segment.endWaypointIndex < 0 || segment.endWaypointIndex >= waypoints.Length ||
                    segment.startWaypointIndex == segment.endWaypointIndex ||
                    segment.lookAheadMeters <= 0f || segment.maximumBankDegrees <= 0f)
                {
                    failure = $"Segment {i} is invalid.";
                    return false;
                }

                if (i > 0 && segments[i - 1].endWaypointIndex != segment.startWaypointIndex)
                {
                    failure = $"Segment {i} does not continue from segment {i - 1}.";
                    return false;
                }

                if (segment.geometry == FlightLessonPathGeometry.Arc)
                {
                    Vector3 start = waypoints[segment.startWaypointIndex].positionUnityMeters;
                    Vector3 end = waypoints[segment.endWaypointIndex].positionUnityMeters;
                    float startRadius = FlightLessonPathMath.HorizontalDistance(start, segment.arcCenterUnityMeters);
                    float endRadius = FlightLessonPathMath.HorizontalDistance(end, segment.arcCenterUnityMeters);
                    if (startRadius < 1f || Mathf.Abs(startRadius - endRadius) > Mathf.Max(1f, startRadius * 0.01f))
                    {
                        failure = $"Arc segment {i} has inconsistent endpoint radii.";
                        return false;
                    }
                }

                int checkpoint = segment.recoveryCheckpointOnCompletion;
                if (checkpoint < -1 || checkpoint >= (recoveryCheckpoints?.Length ?? 0))
                {
                    failure = $"Segment {i} refers to an invalid recovery checkpoint.";
                    return false;
                }
            }

            for (int i = 0; i < (recoveryCheckpoints?.Length ?? 0); i++)
            {
                FlightLessonRecoveryCheckpoint checkpoint = recoveryCheckpoints[i];
                bool isInitialGroundCheckpoint = i == initialRecoveryCheckpointIndex;
                if (string.IsNullOrWhiteSpace(checkpoint.id) ||
                    checkpoint.resumeSegmentIndex < 0 || checkpoint.resumeSegmentIndex >= segments.Length ||
                    !IsFinite(checkpoint.positionUnityMeters) ||
                    !IsFinite(checkpoint.altitudeMslFeet) ||
                    !IsFinite(checkpoint.calibratedAirspeedKnots) ||
                    !IsFinite(checkpoint.headingDegreesTrue) ||
                    !IsFinite(checkpoint.pitchDegrees) ||
                    !IsFinite(checkpoint.bankDegrees) ||
                    checkpoint.altitudeMslFeet < KbduExtendedReservoirLesson.AirportElevationMslFeet ||
                    checkpoint.calibratedAirspeedKnots < 0f ||
                    (!isInitialGroundCheckpoint &&
                     (checkpoint.altitudeMslFeet <= KbduExtendedReservoirLesson.AirportElevationMslFeet ||
                      checkpoint.calibratedAirspeedKnots <= 0f)))
                {
                    failure = $"Recovery checkpoint {i} is invalid.";
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

}
