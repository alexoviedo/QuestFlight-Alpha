using System;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    [CreateAssetMenu(fileName = "AirportParkingStandManifest", menuName = "QuestFlight/Airport Parking Stand Manifest")]
    public sealed class AirportParkingStandManifest : ScriptableObject
    {
        [Serializable]
        public sealed class Stand
        {
            public string standId;
            public Vector2 localEnuMeters;
            public float headingDegrees;
            public Vector3 apronPlanePoint;
            public Vector3 apronPlaneNormal = Vector3.up;
            public string permittedAircraftClass = "single_engine_piston";
            public float minimumSpacingMeters;
            public Vector2[] apronBoundaryEnuMeters = Array.Empty<Vector2>();
            public string sourceReference;
        }

        public string schemaVersion = "1";
        public string coordinateFrame = "KBDU local tangent ENU; Unity +X=east, +Z=north";
        public Stand[] stands = Array.Empty<Stand>();
    }
}
