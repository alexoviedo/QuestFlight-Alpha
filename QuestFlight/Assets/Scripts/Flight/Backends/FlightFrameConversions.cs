using System;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// JSBSim/geodetic conversions. Unity world axes are local ENU expressed as
    /// x=east, y=up, z=north. JSBSim body axes are forward/right/down.
    /// </summary>
    public static class FlightFrameConversions
    {
        public const double FeetToMeters = 0.3048;
        public const double MetersToFeet = 1.0 / FeetToMeters;
        public const double KnotsToMetersPerSecond = 0.5144444444444445;
        public const double MetersPerSecondToKnots = 1.0 / KnotsToMetersPerSecond;
        public const double FeetPerSecondToMetersPerSecond = FeetToMeters;
        public const double FeetPerSecondToFeetPerMinute = 60.0;
        public const double RadiansToDegrees = 180.0 / Math.PI;
        public const double DegreesToRadians = Math.PI / 180.0;

        private const double Wgs84SemiMajorMeters = 6378137.0;
        private const double Wgs84Flattening = 1.0 / 298.257223563;
        private const double Wgs84EccentricitySquared = Wgs84Flattening * (2.0 - Wgs84Flattening);

        public static Vector3 GeodeticToUnity(
            double latitudeDegrees,
            double longitudeDegrees,
            double altitudeMslMeters,
            GeodeticReference origin)
        {
            Double3 point = GeodeticToEcef(latitudeDegrees, longitudeDegrees, altitudeMslMeters);
            Double3 reference = GeodeticToEcef(origin.latitudeDegrees, origin.longitudeDegrees, origin.altitudeMslMeters);
            double dx = point.x - reference.x;
            double dy = point.y - reference.y;
            double dz = point.z - reference.z;

            double lat = origin.latitudeDegrees * DegreesToRadians;
            double lon = origin.longitudeDegrees * DegreesToRadians;
            double sinLat = Math.Sin(lat);
            double cosLat = Math.Cos(lat);
            double sinLon = Math.Sin(lon);
            double cosLon = Math.Cos(lon);

            double east = -sinLon * dx + cosLon * dy;
            double north = -sinLat * cosLon * dx - sinLat * sinLon * dy + cosLat * dz;
            double up = cosLat * cosLon * dx + cosLat * sinLon * dy + sinLat * dz;
            return new Vector3((float)east, (float)up, (float)north);
        }

        public static GeodeticReference UnityToGeodetic(Vector3 unityPositionMeters, GeodeticReference origin)
        {
            Double3 reference = GeodeticToEcef(origin.latitudeDegrees, origin.longitudeDegrees, origin.altitudeMslMeters);
            double lat = origin.latitudeDegrees * DegreesToRadians;
            double lon = origin.longitudeDegrees * DegreesToRadians;
            double sinLat = Math.Sin(lat);
            double cosLat = Math.Cos(lat);
            double sinLon = Math.Sin(lon);
            double cosLon = Math.Cos(lon);
            double east = unityPositionMeters.x;
            double north = unityPositionMeters.z;
            double up = unityPositionMeters.y;

            Double3 point = new Double3(
                reference.x - sinLon * east - sinLat * cosLon * north + cosLat * cosLon * up,
                reference.y + cosLon * east - sinLat * sinLon * north + cosLat * sinLon * up,
                reference.z + cosLat * north + sinLat * up);
            return EcefToGeodetic(point);
        }

        public static Vector3 NedFeetPerSecondToUnityMetersPerSecond(double north, double east, double down)
        {
            return new Vector3(
                (float)(east * FeetPerSecondToMetersPerSecond),
                (float)(-down * FeetPerSecondToMetersPerSecond),
                (float)(north * FeetPerSecondToMetersPerSecond));
        }

        public static Vector3 BodyFrdFeetPerSecondToUnityLocalMetersPerSecond(double forward, double right, double down)
        {
            return new Vector3(
                (float)(right * FeetPerSecondToMetersPerSecond),
                (float)(-down * FeetPerSecondToMetersPerSecond),
                (float)(forward * FeetPerSecondToMetersPerSecond));
        }

        public static Vector3 BodyRatesRadiansToUnityDegrees(double rollP, double pitchQ, double yawR)
        {
            // Unity's positive x/z visual rotations are opposite JSBSim's
            // positive nose-up/right-wing-down body-rate signs.
            return new Vector3(
                (float)(-pitchQ * RadiansToDegrees),
                (float)(yawR * RadiansToDegrees),
                (float)(-rollP * RadiansToDegrees));
        }

        public static Quaternion JsbsimAttitudeToUnity(double headingDegrees, double pitchDegrees, double bankDegrees)
        {
            Quaternion heading = Quaternion.AngleAxis((float)headingDegrees, Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis((float)-pitchDegrees, Vector3.right);
            Quaternion bank = Quaternion.AngleAxis((float)-bankDegrees, Vector3.forward);
            return (heading * pitch * bank).normalized;
        }

        private static Double3 GeodeticToEcef(double latitudeDegrees, double longitudeDegrees, double altitudeMeters)
        {
            double lat = latitudeDegrees * DegreesToRadians;
            double lon = longitudeDegrees * DegreesToRadians;
            double sinLat = Math.Sin(lat);
            double cosLat = Math.Cos(lat);
            double radius = Wgs84SemiMajorMeters / Math.Sqrt(1.0 - Wgs84EccentricitySquared * sinLat * sinLat);
            return new Double3(
                (radius + altitudeMeters) * cosLat * Math.Cos(lon),
                (radius + altitudeMeters) * cosLat * Math.Sin(lon),
                (radius * (1.0 - Wgs84EccentricitySquared) + altitudeMeters) * sinLat);
        }

        private static GeodeticReference EcefToGeodetic(Double3 point)
        {
            double longitude = Math.Atan2(point.y, point.x);
            double horizontal = Math.Sqrt(point.x * point.x + point.y * point.y);
            double latitude = Math.Atan2(point.z, horizontal * (1.0 - Wgs84EccentricitySquared));
            double altitude = 0.0;

            for (int i = 0; i < 8; i++)
            {
                double sinLat = Math.Sin(latitude);
                double radius = Wgs84SemiMajorMeters / Math.Sqrt(1.0 - Wgs84EccentricitySquared * sinLat * sinLat);
                altitude = horizontal / Math.Max(1e-12, Math.Cos(latitude)) - radius;
                latitude = Math.Atan2(
                    point.z,
                    horizontal * (1.0 - Wgs84EccentricitySquared * radius / Math.Max(1.0, radius + altitude)));
            }

            return new GeodeticReference(
                latitude * RadiansToDegrees,
                longitude * RadiansToDegrees,
                altitude);
        }

        private readonly struct Double3
        {
            public readonly double x;
            public readonly double y;
            public readonly double z;

            public Double3(double x, double y, double z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }
    }
}
