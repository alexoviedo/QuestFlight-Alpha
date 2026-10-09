using System;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    public enum FlightDynamicsBackendKind
    {
        JSBSimNativeDevelopment = 1
    }

    [Serializable]
    public struct GeodeticReference
    {
        public double latitudeDegrees;
        public double longitudeDegrees;
        public double altitudeMslMeters;

        public GeodeticReference(double latitudeDegrees, double longitudeDegrees, double altitudeMslMeters)
        {
            this.latitudeDegrees = latitudeDegrees;
            this.longitudeDegrees = longitudeDegrees;
            this.altitudeMslMeters = altitudeMslMeters;
        }

        // FAA ADIP airport reference point/elevation, effective 2026-04-16.
        public static GeodeticReference Kbdu => new GeodeticReference(40.03936527, -105.22608958, 1611.7824);
    }

    [Serializable]
    public struct FlightDynamicsInitialConditions
    {
        // FAA runway geometry is approximately 89.972 degrees true. The 08/26
        // runway designation is magnetic and must not be used as an ENU yaw.
        public const double KbduRunwayTrueHeadingDegrees = 89.972;
        public double latitudeDegrees;
        public double longitudeDegrees;
        public double altitudeMslMeters;
        public double terrainElevationMslMeters;
        public double calibratedAirspeedKnots;
        public double headingDegrees;
        public double pitchDegrees;
        public double bankDegrees;
        public double flightPathAngleDegrees;
        public bool engineRunning;

        public static FlightDynamicsInitialConditions KbduRunway(double headingDegrees = KbduRunwayTrueHeadingDegrees)
        {
            GeodeticReference origin = GeodeticReference.Kbdu;
            return new FlightDynamicsInitialConditions
            {
                latitudeDegrees = origin.latitudeDegrees,
                longitudeDegrees = origin.longitudeDegrees,
                altitudeMslMeters = origin.altitudeMslMeters + 1.25,
                terrainElevationMslMeters = origin.altitudeMslMeters,
                calibratedAirspeedKnots = 0.0,
                headingDegrees = headingDegrees,
                pitchDegrees = 0.0,
                bankDegrees = 0.0,
                flightPathAngleDegrees = 0.0,
                engineRunning = true
            };
        }
    }

    [Serializable]
    public struct FlightDynamicsAtmosphere
    {
        public double windNorthMetersPerSecond;
        public double windEastMetersPerSecond;
        public double windDownMetersPerSecond;

        public static FlightDynamicsAtmosphere Calm => default;
    }

    /// <summary>
    /// Allocation-free value snapshot of the exact analog controls handed to
    /// the active authoritative backend. Returning this struct by value keeps
    /// presentation/animation consumers from mutating simulation input.
    /// </summary>
    [Serializable]
    public struct FlightDynamicsControlSnapshot
    {
        public float aileron;
        public float elevator;
        public float rudder;
        public float throttle;
        public float mixture;
        public float carbHeat;
        public float trim;
        public float flaps;
        public float leftToeBrake;
        public float rightToeBrake;

        public static FlightDynamicsControlSnapshot NeutralIdle => new FlightDynamicsControlSnapshot
        {
            throttle = 0.10f,
            mixture = 1f
        };

        public static FlightDynamicsControlSnapshot From(AircraftControlState controls)
        {
            if (controls == null) return NeutralIdle;
            return new FlightDynamicsControlSnapshot
            {
                aileron = controls.aileron,
                elevator = controls.elevator,
                rudder = controls.rudder,
                throttle = controls.throttle,
                mixture = controls.mixture,
                carbHeat = controls.carbHeat,
                trim = controls.trim,
                flaps = controls.flaps,
                leftToeBrake = controls.leftToeBrake,
                rightToeBrake = controls.rightToeBrake
            };
        }
    }

    [Serializable]
    public struct FlightDynamicsState
    {
        public double simulationTimeSeconds;
        public double latitudeDegrees;
        public double longitudeDegrees;
        public double altitudeMslMeters;
        public double altitudeAglMeters;
        public double calibratedAirspeedKnots;
        public double groundSpeedKnots;
        public double verticalSpeedFeetPerMinute;
        public double headingDegrees;
        public double pitchDegrees;
        public double bankDegrees;
        public double angleOfAttackDegrees;
        public double sideslipDegrees;
        public double engineRpm;
        public double loadFactorG;
        public bool weightOnWheels;
        public Vector3 positionUnityMeters;
        public Quaternion rotationUnity;
        public Vector3 velocityUnityMetersPerSecond;
        public Vector3 angularVelocityBodyDegreesPerSecond;
        // Version 1: actuator diagnostics and physical true-NED ground velocity.
        // Zero means unavailable, so a command can never stand in for telemetry.
        public int nativeTelemetryVersion;
        public double actualFlapNormalized;
        public Vector3 groundVelocityNedMetersPerSecond;

        public bool HasNativeTelemetry => nativeTelemetryVersion == 1;

        public bool IsFinite =>
            IsFiniteNumber(simulationTimeSeconds) &&
            IsFiniteNumber(latitudeDegrees) &&
            IsFiniteNumber(longitudeDegrees) &&
            IsFiniteNumber(altitudeMslMeters) &&
            IsFiniteNumber(altitudeAglMeters) &&
            IsFiniteNumber(calibratedAirspeedKnots) &&
            IsFiniteNumber(groundSpeedKnots) &&
            IsFiniteNumber(verticalSpeedFeetPerMinute) &&
            IsFiniteNumber(headingDegrees) &&
            IsFiniteNumber(pitchDegrees) &&
            IsFiniteNumber(bankDegrees) &&
            IsFiniteNumber(angleOfAttackDegrees) &&
            IsFiniteNumber(sideslipDegrees) &&
            IsFiniteNumber(engineRpm) &&
            IsFiniteNumber(loadFactorG) &&
            IsFiniteNumber(positionUnityMeters.x) &&
            IsFiniteNumber(positionUnityMeters.y) &&
            IsFiniteNumber(positionUnityMeters.z) &&
            IsFiniteNumber(rotationUnity.x) &&
            IsFiniteNumber(rotationUnity.y) &&
            IsFiniteNumber(rotationUnity.z) &&
            IsFiniteNumber(rotationUnity.w) &&
            IsFiniteNumber(velocityUnityMetersPerSecond.x) &&
            IsFiniteNumber(velocityUnityMetersPerSecond.y) &&
            IsFiniteNumber(velocityUnityMetersPerSecond.z) &&
            IsFiniteNumber(angularVelocityBodyDegreesPerSecond.x) &&
            IsFiniteNumber(angularVelocityBodyDegreesPerSecond.y) &&
            IsFiniteNumber(angularVelocityBodyDegreesPerSecond.z) &&
            (!HasNativeTelemetry ||
             (IsFiniteNumber(actualFlapNormalized) && actualFlapNormalized >= 0.0 && actualFlapNormalized <= 1.0 &&
              IsFiniteNumber(groundVelocityNedMetersPerSecond.x) &&
              IsFiniteNumber(groundVelocityNedMetersPerSecond.y) &&
              IsFiniteNumber(groundVelocityNedMetersPerSecond.z)));

        private static bool IsFiniteNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class FlightDynamicsBackendContext
    {
        public Transform simulationRoot;
        public AircraftState aircraftState;
        public ProductionTerrainSurfaceAsset terrainSurface;
        public GeodeticReference localOrigin = GeodeticReference.Kbdu;
        public double fixedDeltaTimeSeconds = 1.0 / 120.0;
    }
}
