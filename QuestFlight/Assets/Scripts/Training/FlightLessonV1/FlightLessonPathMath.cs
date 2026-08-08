using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    public readonly struct FlightLessonPathSample
    {
        public readonly Vector3 closestPointUnityMeters;
        public readonly Vector3 lookAheadPointUnityMeters;
        public readonly float normalizedProgress;
        public readonly float crossTrackErrorMeters;
        public readonly float desiredTrackDegreesTrue;

        public FlightLessonPathSample(
            Vector3 closestPointUnityMeters,
            Vector3 lookAheadPointUnityMeters,
            float normalizedProgress,
            float crossTrackErrorMeters,
            float desiredTrackDegreesTrue)
        {
            this.closestPointUnityMeters = closestPointUnityMeters;
            this.lookAheadPointUnityMeters = lookAheadPointUnityMeters;
            this.normalizedProgress = normalizedProgress;
            this.crossTrackErrorMeters = crossTrackErrorMeters;
            this.desiredTrackDegreesTrue = desiredTrackDegreesTrue;
        }
    }

    public static class FlightLessonPathMath
    {
        public static FlightLessonPathSample Sample(
            in FlightLessonWaypoint start,
            in FlightLessonWaypoint end,
            in FlightLessonSegment segment,
            Vector3 aircraftPositionUnityMeters)
        {
            return segment.geometry == FlightLessonPathGeometry.Arc
                ? SampleArc(start.positionUnityMeters, end.positionUnityMeters,
                    segment.arcCenterUnityMeters, segment.arcClockwise,
                    aircraftPositionUnityMeters, segment.lookAheadMeters)
                : SampleLine(start.positionUnityMeters, end.positionUnityMeters,
                    aircraftPositionUnityMeters, segment.lookAheadMeters);
        }

        public static FlightLessonPathSample SampleLine(
            Vector3 start,
            Vector3 end,
            Vector3 position,
            float lookAheadMeters)
        {
            Vector2 a = new Vector2(start.x, start.z);
            Vector2 b = new Vector2(end.x, end.z);
            Vector2 p = new Vector2(position.x, position.z);
            Vector2 delta = b - a;
            float lengthSquared = delta.sqrMagnitude;
            if (lengthSquared < 0.0001f)
            {
                return new FlightLessonPathSample(start, start, 1f, 0f, 0f);
            }

            float length = Mathf.Sqrt(lengthSquared);
            float unclamped = Vector2.Dot(p - a, delta) / lengthSquared;
            float progress = Mathf.Clamp01(unclamped);
            Vector2 closest = a + delta * progress;
            Vector2 direction = delta / length;
            float signedCrossTrack = direction.x * (p.y - closest.y) - direction.y * (p.x - closest.x);
            float lookProgress = Mathf.Clamp01(progress + Mathf.Max(0f, lookAheadMeters) / length);
            Vector2 look = a + delta * lookProgress;
            float y = Mathf.Lerp(start.y, end.y, progress);
            float lookY = Mathf.Lerp(start.y, end.y, lookProgress);
            return new FlightLessonPathSample(
                new Vector3(closest.x, y, closest.y),
                new Vector3(look.x, lookY, look.y),
                progress,
                signedCrossTrack,
                HeadingDegreesTrue(direction));
        }

        public static FlightLessonPathSample SampleArc(
            Vector3 start,
            Vector3 end,
            Vector3 center,
            bool clockwise,
            Vector3 position,
            float lookAheadMeters)
        {
            Vector2 c = new Vector2(center.x, center.z);
            Vector2 startOffset = new Vector2(start.x, start.z) - c;
            Vector2 endOffset = new Vector2(end.x, end.z) - c;
            Vector2 positionOffset = new Vector2(position.x, position.z) - c;
            float radius = Mathf.Max(0.001f, startOffset.magnitude);
            float startAngle = Mathf.Atan2(startOffset.y, startOffset.x) * Mathf.Rad2Deg;
            float endAngle = Mathf.Atan2(endOffset.y, endOffset.x) * Mathf.Rad2Deg;
            float positionAngle = positionOffset.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(positionOffset.y, positionOffset.x) * Mathf.Rad2Deg
                : startAngle;
            float totalTravel = DirectedAngle(startAngle, endAngle, clockwise);
            float currentTravel = Mathf.Clamp(DirectedAngle(startAngle, positionAngle, clockwise), 0f, totalTravel);
            float progress = totalTravel > 0.001f ? currentTravel / totalTravel : 1f;
            float closestAngle = AdvanceAngle(startAngle, currentTravel, clockwise);
            float lookTravel = Mathf.Min(totalTravel, currentTravel + Mathf.Max(0f, lookAheadMeters) / radius * Mathf.Rad2Deg);
            float lookAngle = AdvanceAngle(startAngle, lookTravel, clockwise);
            Vector2 closestOffset = DirectionFromAngle(closestAngle) * radius;
            Vector2 lookOffset = DirectionFromAngle(lookAngle) * radius;
            Vector2 tangent = clockwise
                ? new Vector2(closestOffset.y, -closestOffset.x).normalized
                : new Vector2(-closestOffset.y, closestOffset.x).normalized;
            float signedCrossTrack = positionOffset.magnitude - radius;
            return new FlightLessonPathSample(
                new Vector3(c.x + closestOffset.x, Mathf.Lerp(start.y, end.y, progress), c.y + closestOffset.y),
                new Vector3(c.x + lookOffset.x, Mathf.Lerp(start.y, end.y, totalTravel > 0.001f ? lookTravel / totalTravel : 1f), c.y + lookOffset.y),
                progress,
                signedCrossTrack,
                HeadingDegreesTrue(tangent));
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        public static float HeadingErrorDegrees(float desiredDegreesTrue, float actualDegreesTrue)
        {
            return Mathf.DeltaAngle(actualDegreesTrue, desiredDegreesTrue);
        }

        private static float HeadingDegreesTrue(Vector2 eastNorthDirection)
        {
            return Mathf.Repeat(Mathf.Atan2(eastNorthDirection.x, eastNorthDirection.y) * Mathf.Rad2Deg, 360f);
        }

        private static float DirectedAngle(float from, float to, bool clockwise)
        {
            return clockwise ? Mathf.Repeat(from - to, 360f) : Mathf.Repeat(to - from, 360f);
        }

        private static float AdvanceAngle(float start, float travel, bool clockwise)
        {
            return clockwise ? start - travel : start + travel;
        }

        private static Vector2 DirectionFromAngle(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }
    }
}
