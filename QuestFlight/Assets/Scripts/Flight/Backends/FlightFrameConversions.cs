using System;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// WGS84 ellipsoidal AEQD at the authored origin, matching scenery PROJ.
    /// Unity x/z are projected east/north; y is source MSL minus origin MSL.
    /// JSBSim body axes are forward/right/down and its ASL export is radial.
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
        private const double Wgs84SemiMinorMeters = Wgs84SemiMajorMeters * (1.0 - Wgs84Flattening);

        public static Vector3 GeodeticToUnity(
            double latitudeDegrees,
            double longitudeDegrees,
            double altitudeMslMeters,
            GeodeticReference origin)
        {
            Horizontal point = Project(latitudeDegrees, longitudeDegrees, origin);
            return new Vector3((float)point.east, (float)(altitudeMslMeters - origin.altitudeMslMeters), (float)point.north);
        }

        public static GeodeticReference UnityToGeodetic(Vector3 unityPositionMeters, GeodeticReference origin)
        {
            return Unproject(unityPositionMeters.x, unityPositionMeters.z,
                origin.altitudeMslMeters + unityPositionMeters.y, origin);
        }

        public static double GeodeticAltitudeToNativeRadialMeters(double latitudeDegrees, double altitudeMslMeters)
        {
            Double3 point = GeodeticToEcef(latitudeDegrees, 0, altitudeMslMeters);
            double radius = Math.Sqrt(point.x * point.x + point.z * point.z);
            double seaRadius = Wgs84SemiMajorMeters * Wgs84SemiMinorMeters * radius /
                Math.Sqrt(Wgs84SemiMinorMeters * Wgs84SemiMinorMeters * point.x * point.x +
                          Wgs84SemiMajorMeters * Wgs84SemiMajorMeters * point.z * point.z);
            return radius - seaRadius;
        }

        public static double NativeRadialAltitudeToGeodeticMeters(double latitudeDegrees, double radialAltitudeMeters)
        {
            double height = radialAltitudeMeters;
            for (int i = 0; i < 5; i++)
                height += radialAltitudeMeters - GeodeticAltitudeToNativeRadialMeters(latitudeDegrees, height);
            return height;
        }

        public static Vector3 NedFeetPerSecondToProjectedMetersPerSecond(double north, double east, double down,
            double latitudeDegrees, double longitudeDegrees, double altitudeMslMeters, GeodeticReference origin)
        {
            ProjectionJacobian(latitudeDegrees, longitudeDegrees, altitudeMslMeters, origin,
                out Horizontal n, out Horizontal e);
            return new Vector3((float)((n.east * north + e.east * east) * FeetToMeters),
                (float)(-down * FeetToMeters), (float)((n.north * north + e.north * east) * FeetToMeters));
        }

        public static Vector3 ProjectedMetersPerSecondToNed(Vector3 projected, double latitudeDegrees,
            double longitudeDegrees, double altitudeMslMeters, GeodeticReference origin)
        {
            ProjectionJacobian(latitudeDegrees, longitudeDegrees, altitudeMslMeters, origin,
                out Horizontal n, out Horizontal e);
            double determinant = n.north * e.east - e.north * n.east;
            return new Vector3((float)((projected.z * e.east - projected.x * e.north) / determinant),
                (float)((projected.x * n.north - projected.z * n.east) / determinant), -projected.y);
        }

        public static Quaternion JsbsimAttitudeToProjectedUnity(double headingDegrees, double pitchDegrees,
            double bankDegrees, double latitudeDegrees, double longitudeDegrees, GeodeticReference origin)
        {
            ProjectionJacobian(latitudeDegrees, longitudeDegrees, 0, origin, out Horizontal n, out Horizontal e);
            // Orthogonal physical attitude uses the projected true-north basis;
            // projection scale/shear belongs to velocity, not to the aircraft mesh.
            Quaternion basis = Quaternion.LookRotation(new Vector3((float)n.east, 0, (float)n.north), Vector3.up);
            return basis * JsbsimAttitudeToUnity(headingDegrees, pitchDegrees, bankDegrees);
        }

        public static double UnityHeadingToTrue(double gridHeadingDegrees, double latitudeDegrees,
            double longitudeDegrees, GeodeticReference origin)
        {
            ProjectionJacobian(latitudeDegrees, longitudeDegrees, 0, origin, out Horizontal n, out Horizontal e);
            double convergence = Math.Atan2(n.east, n.north) * RadiansToDegrees;
            return (gridHeadingDegrees - convergence + 360.0) % 360.0;
        }

        private static void ProjectionJacobian(double latitudeDegrees, double longitudeDegrees, double altitudeMeters,
            GeodeticReference origin, out Horizontal north, out Horizontal east)
        {
            double lat = latitudeDegrees * DegreesToRadians;
            double factor = 1.0 - Wgs84EccentricitySquared * Math.Sin(lat) * Math.Sin(lat);
            double meridian = Wgs84SemiMajorMeters * (1.0 - Wgs84EccentricitySquared) / Math.Pow(factor, 1.5);
            double primeVertical = Wgs84SemiMajorMeters / Math.Sqrt(factor);
            // Central differences over one physical metre, using double projected
            // coordinates so float world-position rounding cannot corrupt the basis.
            double dLat = RadiansToDegrees / (meridian + altitudeMeters);
            double dLon = RadiansToDegrees / ((primeVertical + altitudeMeters) * Math.Cos(lat));
            Horizontal np = Project(latitudeDegrees + dLat, longitudeDegrees, origin);
            Horizontal nm = Project(latitudeDegrees - dLat, longitudeDegrees, origin);
            Horizontal ep = Project(latitudeDegrees, longitudeDegrees + dLon, origin);
            Horizontal em = Project(latitudeDegrees, longitudeDegrees - dLon, origin);
            north = new Horizontal((np.east - nm.east) * 0.5, (np.north - nm.north) * 0.5);
            east = new Horizontal((ep.east - em.east) * 0.5, (ep.north - em.north) * 0.5);
        }

        private static Horizontal Project(double latitudeDegrees, double longitudeDegrees, GeodeticReference origin)
        {
            double u1 = Math.Atan((1.0 - Wgs84Flattening) * Math.Tan(origin.latitudeDegrees * DegreesToRadians));
            double u2 = Math.Atan((1.0 - Wgs84Flattening) * Math.Tan(latitudeDegrees * DegreesToRadians));
            double sinU1 = Math.Sin(u1), cosU1 = Math.Cos(u1), sinU2 = Math.Sin(u2), cosU2 = Math.Cos(u2);
            double l = (longitudeDegrees - origin.longitudeDegrees) * DegreesToRadians;
            double lambda = l, sinSigma = 0, cosSigma = 0, sigma = 0, cosSqAlpha = 0, cos2SigmaM = 0;
            bool converged = false;
            for (int i = 0; i < 100; i++)
            {
                double sinLambda = Math.Sin(lambda), cosLambda = Math.Cos(lambda);
                double p = cosU2 * sinLambda, q = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;
                sinSigma = Math.Sqrt(p * p + q * q);
                if (sinSigma < 1e-15) return new Horizontal(0, 0);
                cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
                sigma = Math.Atan2(sinSigma, cosSigma);
                double sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
                cosSqAlpha = 1 - sinAlpha * sinAlpha;
                cos2SigmaM = cosSqAlpha > 1e-15 ? cosSigma - 2 * sinU1 * sinU2 / cosSqAlpha : 0;
                double c = Wgs84Flattening / 16 * cosSqAlpha * (4 + Wgs84Flattening * (4 - 3 * cosSqAlpha));
                double next = l + (1 - c) * Wgs84Flattening * sinAlpha * (sigma + c * sinSigma *
                    (cos2SigmaM + c * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));
                if (Math.Abs(next - lambda) < 1e-13) { lambda = next; converged = true; break; }
                lambda = next;
            }
            if (!converged) throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), "Position outside supported geodesic projection domain.");
            GeodesicSeries(cosSqAlpha, out double a, out double b);
            double delta = SigmaCorrection(b, sinSigma, cosSigma, cos2SigmaM);
            double distance = Wgs84SemiMinorMeters * a * (sigma - delta);
            double bearing = Math.Atan2(cosU2 * Math.Sin(lambda), cosU1 * sinU2 - sinU1 * cosU2 * Math.Cos(lambda));
            return new Horizontal(distance * Math.Sin(bearing), distance * Math.Cos(bearing));
        }

        private static GeodeticReference Unproject(double east, double north, double altitude, GeodeticReference origin)
        {
            double distance = Math.Sqrt(east * east + north * north);
            if (distance < 1e-9) return new GeodeticReference(origin.latitudeDegrees, origin.longitudeDegrees, altitude);
            double bearing = Math.Atan2(east, north), sinBearing = Math.Sin(bearing), cosBearing = Math.Cos(bearing);
            double tanU1 = (1 - Wgs84Flattening) * Math.Tan(origin.latitudeDegrees * DegreesToRadians);
            double cosU1 = 1 / Math.Sqrt(1 + tanU1 * tanU1), sinU1 = tanU1 * cosU1;
            double sigma1 = Math.Atan2(tanU1, cosBearing), sinAlpha = cosU1 * sinBearing;
            double cosSqAlpha = 1 - sinAlpha * sinAlpha;
            GeodesicSeries(cosSqAlpha, out double a, out double b);
            double baseSigma = distance / (Wgs84SemiMinorMeters * a), sigma = baseSigma;
            for (int i = 0; i < 100; i++)
            {
                double next = baseSigma + SigmaCorrection(b, Math.Sin(sigma), Math.Cos(sigma), Math.Cos(2 * sigma1 + sigma));
                if (Math.Abs(next - sigma) < 1e-13) { sigma = next; break; }
                sigma = next;
                if (i == 99) throw new ArgumentOutOfRangeException(nameof(distance), "Position outside supported geodesic projection domain.");
            }
            double sinSigma = Math.Sin(sigma), cosSigma = Math.Cos(sigma), cos2SigmaM = Math.Cos(2 * sigma1 + sigma);
            double t = sinU1 * sinSigma - cosU1 * cosSigma * cosBearing;
            double lat = Math.Atan2(sinU1 * cosSigma + cosU1 * sinSigma * cosBearing,
                (1 - Wgs84Flattening) * Math.Sqrt(sinAlpha * sinAlpha + t * t));
            double lambda = Math.Atan2(sinSigma * sinBearing, cosU1 * cosSigma - sinU1 * sinSigma * cosBearing);
            double c = Wgs84Flattening / 16 * cosSqAlpha * (4 + Wgs84Flattening * (4 - 3 * cosSqAlpha));
            double l = lambda - (1 - c) * Wgs84Flattening * sinAlpha * (sigma + c * sinSigma *
                (cos2SigmaM + c * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));
            return new GeodeticReference(lat * RadiansToDegrees, origin.longitudeDegrees + l * RadiansToDegrees, altitude);
        }

        private static void GeodesicSeries(double cosSqAlpha, out double a, out double b)
        {
            double uSq = cosSqAlpha * (Wgs84SemiMajorMeters * Wgs84SemiMajorMeters - Wgs84SemiMinorMeters * Wgs84SemiMinorMeters) /
                (Wgs84SemiMinorMeters * Wgs84SemiMinorMeters);
            a = 1 + uSq / 16384 * (4096 + uSq * (-768 + uSq * (320 - 175 * uSq)));
            b = uSq / 1024 * (256 + uSq * (-128 + uSq * (74 - 47 * uSq)));
        }

        private static double SigmaCorrection(double b, double sinSigma, double cosSigma, double cos2SigmaM)
        {
            double square = cos2SigmaM * cos2SigmaM;
            return b * sinSigma * (cos2SigmaM + b / 4 * (cosSigma * (-1 + 2 * square) -
                b / 6 * cos2SigmaM * (-3 + 4 * sinSigma * sinSigma) * (-3 + 4 * square)));
        }

        private readonly struct Horizontal
        {
            public readonly double east, north;
            public Horizontal(double east, double north) { this.east = east; this.north = north; }
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
