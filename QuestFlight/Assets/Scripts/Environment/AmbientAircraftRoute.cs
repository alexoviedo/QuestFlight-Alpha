using System;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    public enum AmbientFlightPhase { Parked, Starting, TaxiOut, HoldShort, LineUp, Takeoff, Climb, Circuit, Approach, Landing, Rollout, TaxiIn, Shutdown }

    [CreateAssetMenu(menuName = "QuestFlight/Environment/Ambient Aircraft Route")]
    public sealed class AmbientAircraftRoute : ScriptableObject
    {
        [Serializable] public sealed class Segment
        {
            public AmbientFlightPhase phase;
            public bool onGround, protectsRunway;
            public float startSpeed, endSpeed, dwellSeconds;
            public Vector3[] points = Array.Empty<Vector3>();
            public float[] distance = Array.Empty<float>();
            public float Length => distance.Length == 0 ? 0 : distance[distance.Length - 1];
            public Vector3 Position(float along, out Vector3 tangent)
            {
                int lo = 0, hi = distance.Length - 1;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (distance[mid] < along) lo = mid; else hi = mid; }
                tangent = (points[hi] - points[lo]).normalized;
                return Vector3.Lerp(points[lo], points[hi], Mathf.InverseLerp(distance[lo], distance[hi], along));
            }
        }
        public int runwayDirection = 8;
        public Segment[] segments = Array.Empty<Segment>();
        public bool IsValid(out string error)
        {
            error = null;
            if (segments == null || segments.Length < 2) { error = "Missing route segments."; return false; }
            if (segments[0] == null || segments[0].points == null || segments[0].points.Length == 0)
            { error = "Missing initial route point."; return false; }
            Vector3 prior = segments[0].points[0];
            foreach (Segment s in segments)
            {
                if (s == null || s.points == null || s.distance == null || s.points.Length < 2 || s.distance.Length != s.points.Length ||
                    s.startSpeed < 0 || s.endSpeed < 0 || !BoulderWindFieldProfile.Finite(s.startSpeed + s.endSpeed + s.dwellSeconds))
                { error = "Invalid route segment."; return false; }
                if (Vector3.Distance(prior, s.points[0]) > 0.05f) { error = "Route position discontinuity."; return false; }
                for (int i = 0; i < s.points.Length; i++)
                    if (!BoulderWindFieldProfile.Finite(s.points[i]) || !BoulderWindFieldProfile.Finite(s.distance[i]) ||
                        (i > 0 && s.distance[i] < s.distance[i - 1])) { error = "Invalid path sample."; return false; }
                prior = s.points[s.points.Length - 1];
            }
            if (Vector3.Distance(prior, segments[0].points[0]) > 0.05f)
            { error = "Route loop discontinuity."; return false; }
            return true;
        }
    }
}
