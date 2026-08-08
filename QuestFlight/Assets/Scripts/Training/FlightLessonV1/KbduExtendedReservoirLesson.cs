using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    /// <summary>
    /// Deterministic authoring defaults for an extended reservoir lesson.
    /// This is explicitly not a standard KBDU traffic pattern and is not for navigation.
    /// Local X/Z coordinates are east/north ENU relative to the product KBDU origin.
    /// </summary>
    public static class KbduExtendedReservoirLesson
    {
        public const float AirportElevationMslFeet = 5288f;
        public const float PublishedTrafficPatternAltitudeMslFeet = 6300f;
        public const float PublishedMinimumTurnAltitudeMslFeet = 5800f;
        public const string PublishedTrafficDirectionFact = "Runway 08 uses left traffic.";
        public const string PublishedDownwindLocationFact = "Keep the downwind leg south of Jay Road.";
        public const string OfficialNoiseAbatementUrl =
            "https://bouldercolorado.gov/sites/default/files/2021-01/noise-abatement-procedures-traffic-patterns.pdf";
        public const string OfficialAircraftNoiseUrl =
            "https://bouldercolorado.gov/services/aircraft-noise";

        public static readonly Vector2 ReservoirBoundsEastMeters = new Vector2(-813.5f, 1316.5f);
        public static readonly Vector2 ReservoirBoundsNorthMeters = new Vector2(3450f, 5166.5f);

        public static FlightLessonRouteDefinition CreateDefinition()
        {
            FlightLessonWaypoint[] waypoints =
            {
                Point("runway_08_start", -629.8f, -30.7f, 5289f, 55f, 90f),
                Point("departure_turn_gate", 900f, -30f, 6000f, 74f, 180f),
                Point("climb_northeast", 2000f, 1700f, 6800f, 85f, 260f),
                Point("reservoir_entry", 1400f, 3500f, 7500f, 95f, 300f),
                Point("reservoir_overlook", 251.5f, 4308.25f, 7500f, 95f, 350f),
                Point("reservoir_exit", -900f, 4000f, 7400f, 95f, 300f),
                Point("return_transition", 2100f, 2200f, 6800f, 92f, 300f),
                Point("downwind_entry", 1500f, 1500f, 6300f, 90f, 220f),
                Point("downwind_end", -1500f, 1500f, 6300f, 85f, 220f),
                Point("base_end", -1750f, 300f, 5900f, 75f, 180f),
                Point("final_intercept", -1800f, -30f, 5700f, 70f, 160f),
                Point("runway_threshold", -630f, -30f, 5300f, 65f, 100f),
                Point("rollout", 350f, -30f, 5289f, 35f, 120f)
            };

            FlightLessonSegment[] segments =
            {
                Leg("departure", FlightLessonSegmentKind.Departure, 0, 1, 180f, 8f, 800f, 250f, 1),
                Leg("climb_to_reservoir", FlightLessonSegmentKind.Climb, 1, 2, 240f, 18f, 650f),
                Leg("reservoir_entry", FlightLessonSegmentKind.Climb, 2, 3, 300f, 22f, 600f, 300f, 2),
                Leg("reservoir_overflight", FlightLessonSegmentKind.ScenicOverflight, 3, 4, 320f, 20f, 400f),
                Leg("reservoir_exit", FlightLessonSegmentKind.ScenicOverflight, 4, 5, 320f, 20f, 400f),
                Leg("return", FlightLessonSegmentKind.Return, 5, 6, 350f, 25f, 500f, 350f, 3),
                Leg("pattern_entry", FlightLessonSegmentKind.PatternEntry, 6, 7, 250f, 20f, 500f, 250f, 4),
                Leg("downwind", FlightLessonSegmentKind.Downwind, 7, 8, 220f, 18f, 400f),
                Leg("base", FlightLessonSegmentKind.Base, 8, 9, 180f, 20f, 500f),
                Leg("final_turn", FlightLessonSegmentKind.Final, 9, 10, 140f, 15f, 450f),
                Leg("final", FlightLessonSegmentKind.Final, 10, 11, 120f, 10f, 400f),
                Leg("rollout", FlightLessonSegmentKind.Rollout, 11, 12, 80f, 6f, 300f)
            };

            FlightLessonRecoveryCheckpoint[] checkpoints =
            {
                Checkpoint("runway_08_restart", 0, -629.8f, -30.7f, 5292.1f, 0f, 90f),
                Checkpoint("departure_climb", 1, 1200f, 350f, 6200f, 80f, 35f, 5f),
                Checkpoint("reservoir_cruise", 3, 1400f, 3500f, 7500f, 95f, 305f),
                Checkpoint("return_cruise", 6, 2100f, 2200f, 6800f, 92f, 215f),
                Checkpoint("pattern_downwind", 7, 1500f, 1500f, 6300f, 90f, 270f)
            };

            return new FlightLessonRouteDefinition
            {
                lessonId = "kbdu_extended_reservoir_v1",
                displayName = "KBDU Extended Reservoir Guided Lesson",
                operationalDisclaimer =
                    "Extended training route, not a standard traffic pattern, not an official procedure, and not for navigation or training credit.",
                provenanceUrls = new[] { OfficialNoiseAbatementUrl, OfficialAircraftNoiseUrl },
                officialOperatingFacts = new[]
                {
                    $"Traffic-pattern altitude: {PublishedTrafficPatternAltitudeMslFeet:0} feet MSL.",
                    $"Do not begin the first turn below {PublishedMinimumTurnAltitudeMslFeet:0} feet MSL.",
                    PublishedTrafficDirectionFact,
                    PublishedDownwindLocationFact
                },
                initialRecoveryCheckpointIndex = 0,
                waypoints = waypoints,
                segments = segments,
                recoveryCheckpoints = checkpoints
            };
        }

        private static FlightLessonWaypoint Point(
            string id, float east, float north, float altitude, float speed, float radius)
        {
            return new FlightLessonWaypoint(id, new Vector3(east, 0f, north), altitude, speed, radius);
        }

        private static FlightLessonSegment Leg(
            string id,
            FlightLessonSegmentKind kind,
            int start,
            int end,
            float lookAhead,
            float maxBank,
            float maxClimb,
            float altitudeTolerance = 0f,
            int checkpoint = -1)
        {
            return new FlightLessonSegment(
                id, kind, start, end, lookAhead, maxBank, maxClimb,
                altitudeTolerance, checkpoint);
        }

        private static FlightLessonRecoveryCheckpoint Checkpoint(
            string id,
            int segment,
            float east,
            float north,
            float altitude,
            float speed,
            float heading,
            float pitch = 0f)
        {
            return new FlightLessonRecoveryCheckpoint(
                id, segment, new Vector3(east, 0f, north), altitude, speed, heading, pitch);
        }
    }
}
