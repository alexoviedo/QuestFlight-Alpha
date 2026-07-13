using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Authored scene source for the one authoritative aircraft reset pose.
    /// Production scenes assign this explicitly; legacy scenes may omit it and
    /// retain FlightDynamicsInitialConditions.KbduRunway().
    /// </summary>
    public sealed class FlightDynamicsInitialConditionProvider : MonoBehaviour
    {
        [Header("Authored geodetic state")]
        public double latitudeDegrees = 40.03936527;
        public double longitudeDegrees = -105.22608958;
        public double altitudeMslMeters = 1613.0324;
        public double terrainElevationMslMeters = 1611.7824;

        [Header("Optional authored transform pose")]
        public bool derivePositionFromSpawnTransform;
        public bool deriveHeadingFromSpawnTransform = true;
        public Transform spawnTransform;
        [Min(0f)] public float spawnHeightAboveTerrainMeters = 1.25f;

        [Header("Aircraft state")]
        public double calibratedAirspeedKnots;
        public double headingDegrees = FlightDynamicsInitialConditions.KbduRunwayTrueHeadingDegrees;
        public double pitchDegrees;
        public double bankDegrees;
        public double flightPathAngleDegrees;
        public bool engineRunning = true;

        public FlightDynamicsInitialConditions Build(GeodeticReference localOrigin)
        {
            double latitude = latitudeDegrees;
            double longitude = longitudeDegrees;
            double altitude = altitudeMslMeters;
            double heading = headingDegrees;
            Transform authoredTransform = spawnTransform != null ? spawnTransform : transform;
            if (derivePositionFromSpawnTransform)
            {
                GeodeticReference geodetic = FlightFrameConversions.UnityToGeodetic(authoredTransform.position, localOrigin);
                latitude = geodetic.latitudeDegrees;
                longitude = geodetic.longitudeDegrees;
                altitude = geodetic.altitudeMslMeters;
            }

            if (deriveHeadingFromSpawnTransform)
            {
                Vector3 forward = Vector3.ProjectOnPlane(authoredTransform.forward, Vector3.up);
                if (forward.sqrMagnitude > 1e-8f)
                {
                    heading = Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
                }
            }

            double terrain = derivePositionFromSpawnTransform
                ? altitude - spawnHeightAboveTerrainMeters
                : terrainElevationMslMeters;
            return new FlightDynamicsInitialConditions
            {
                latitudeDegrees = latitude,
                longitudeDegrees = longitude,
                altitudeMslMeters = altitude,
                terrainElevationMslMeters = terrain,
                calibratedAirspeedKnots = calibratedAirspeedKnots,
                headingDegrees = heading,
                pitchDegrees = pitchDegrees,
                bankDegrees = bankDegrees,
                flightPathAngleDegrees = flightPathAngleDegrees,
                engineRunning = engineRunning
            };
        }
    }
}
