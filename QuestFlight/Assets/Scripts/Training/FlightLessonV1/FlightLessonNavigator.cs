using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    public struct FlightLessonAircraftNavigationState
    {
        public Vector3 positionUnityMeters;
        public float altitudeMslFeet;
        public float calibratedAirspeedKnots;
        public float headingDegreesTrue;
    }

    public struct FlightLessonProgressState
    {
        public int currentSegmentIndex;
        public int latestRecoveryCheckpointIndex;
        public bool lessonComplete;

        public static FlightLessonProgressState Initial => new FlightLessonProgressState
        {
            currentSegmentIndex = 0,
            latestRecoveryCheckpointIndex = -1,
            lessonComplete = false
        };

        public static FlightLessonProgressState InitialFor(
            FlightLessonRouteDefinition route)
        {
            int checkpoint = route != null
                ? route.initialRecoveryCheckpointIndex
                : -1;
            if (route?.recoveryCheckpoints == null || checkpoint < 0 ||
                checkpoint >= route.recoveryCheckpoints.Length ||
                route.recoveryCheckpoints[checkpoint].resumeSegmentIndex != 0)
            {
                checkpoint = -1;
            }

            return new FlightLessonProgressState
            {
                currentSegmentIndex = 0,
                latestRecoveryCheckpointIndex = checkpoint,
                lessonComplete = false
            };
        }
    }

    public readonly struct FlightLessonGuidanceIntent
    {
        public readonly string segmentId;
        public readonly FlightLessonSegmentKind segmentKind;
        public readonly Vector3 lookAheadPointUnityMeters;
        public readonly float desiredTrackDegreesTrue;
        public readonly float crossTrackErrorMeters;
        public readonly float targetAltitudeMslFeet;
        public readonly float targetCalibratedAirspeedKnots;
        public readonly float maximumBankDegrees;
        public readonly float maximumClimbRateFeetPerMinute;
        public readonly float normalizedSegmentProgress;

        public FlightLessonGuidanceIntent(
            string segmentId,
            FlightLessonSegmentKind segmentKind,
            Vector3 lookAheadPointUnityMeters,
            float desiredTrackDegreesTrue,
            float crossTrackErrorMeters,
            float targetAltitudeMslFeet,
            float targetCalibratedAirspeedKnots,
            float maximumBankDegrees,
            float maximumClimbRateFeetPerMinute,
            float normalizedSegmentProgress)
        {
            this.segmentId = segmentId;
            this.segmentKind = segmentKind;
            this.lookAheadPointUnityMeters = lookAheadPointUnityMeters;
            this.desiredTrackDegreesTrue = desiredTrackDegreesTrue;
            this.crossTrackErrorMeters = crossTrackErrorMeters;
            this.targetAltitudeMslFeet = targetAltitudeMslFeet;
            this.targetCalibratedAirspeedKnots = targetCalibratedAirspeedKnots;
            this.maximumBankDegrees = maximumBankDegrees;
            this.maximumClimbRateFeetPerMinute = maximumClimbRateFeetPerMinute;
            this.normalizedSegmentProgress = normalizedSegmentProgress;
        }
    }

    public static class FlightLessonNavigator
    {
        public static bool TryEvaluate(
            FlightLessonRouteDefinition route,
            in FlightLessonProgressState progress,
            in FlightLessonAircraftNavigationState aircraft,
            out FlightLessonGuidanceIntent intent)
        {
            if (route == null || route.segments == null || route.waypoints == null ||
                progress.lessonComplete || progress.currentSegmentIndex < 0 ||
                progress.currentSegmentIndex >= route.segments.Length)
            {
                intent = default;
                return false;
            }

            FlightLessonSegment segment = route.segments[progress.currentSegmentIndex];
            FlightLessonWaypoint start = route.waypoints[segment.startWaypointIndex];
            FlightLessonWaypoint end = route.waypoints[segment.endWaypointIndex];
            FlightLessonPathSample path = FlightLessonPathMath.Sample(start, end, segment, aircraft.positionUnityMeters);
            float targetAltitude = Mathf.Lerp(start.altitudeMslFeet, end.altitudeMslFeet, path.normalizedProgress);
            float targetSpeed = Mathf.Lerp(start.calibratedAirspeedKnots, end.calibratedAirspeedKnots, path.normalizedProgress);
            intent = new FlightLessonGuidanceIntent(
                segment.id,
                segment.kind,
                path.lookAheadPointUnityMeters,
                path.desiredTrackDegreesTrue,
                path.crossTrackErrorMeters,
                targetAltitude,
                targetSpeed,
                segment.maximumBankDegrees,
                segment.maximumClimbRateFeetPerMinute,
                path.normalizedProgress);
            return true;
        }

        public static FlightLessonProgressState AdvanceIfCaptured(
            FlightLessonRouteDefinition route,
            in FlightLessonProgressState current,
            in FlightLessonAircraftNavigationState aircraft)
        {
            if (route == null || current.lessonComplete || current.currentSegmentIndex < 0 ||
                current.currentSegmentIndex >= route.segments.Length)
            {
                return current;
            }

            FlightLessonSegment segment = route.segments[current.currentSegmentIndex];
            FlightLessonWaypoint start = route.waypoints[segment.startWaypointIndex];
            FlightLessonWaypoint end = route.waypoints[segment.endWaypointIndex];
            FlightLessonPathSample path = FlightLessonPathMath.Sample(
                start,
                end,
                segment,
                aircraft.positionUnityMeters);
            bool insideCaptureSphere = FlightLessonPathMath.HorizontalDistance(
                aircraft.positionUnityMeters,
                end.positionUnityMeters) <= end.captureRadiusMeters;
            bool crossedLineEndInsideCorridor =
                segment.geometry == FlightLessonPathGeometry.Line &&
                HasCrossedLineEndPlane(
                    start.positionUnityMeters,
                    end.positionUnityMeters,
                    aircraft.positionUnityMeters) &&
                Mathf.Abs(path.crossTrackErrorMeters) <= end.captureRadiusMeters;
            if (!insideCaptureSphere && !crossedLineEndInsideCorridor)
            {
                return current;
            }

            if (segment.completionAltitudeToleranceFeet > 0f &&
                Mathf.Abs(aircraft.altitudeMslFeet - end.altitudeMslFeet) > segment.completionAltitudeToleranceFeet &&
                !crossedLineEndInsideCorridor)
            {
                return current;
            }

            FlightLessonProgressState next = current;
            if (segment.recoveryCheckpointOnCompletion >= 0)
            {
                next.latestRecoveryCheckpointIndex = segment.recoveryCheckpointOnCompletion;
            }

            next.currentSegmentIndex++;
            if (next.currentSegmentIndex >= route.segments.Length)
            {
                next.currentSegmentIndex = route.segments.Length - 1;
                next.lessonComplete = true;
            }

            return next;
        }

        private static bool HasCrossedLineEndPlane(
            Vector3 start,
            Vector3 end,
            Vector3 position)
        {
            Vector2 direction = new Vector2(end.x - start.x, end.z - start.z);
            if (direction.sqrMagnitude < 0.0001f)
                return false;
            Vector2 beyondEnd = new Vector2(position.x - end.x, position.z - end.z);
            return Vector2.Dot(beyondEnd, direction) >= 0f;
        }
    }
}
