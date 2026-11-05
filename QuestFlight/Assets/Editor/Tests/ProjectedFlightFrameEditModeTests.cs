#if UNITY_EDITOR
using System;
using AlexOviedo.QuestFlight.JSBSim;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProjectedFlightFrameEditModeTests
    {
        // Independent PROJ 9.5.1 / pyproj 3.7.2 ellipsoidal AEQD fixtures,
        // +proj=aeqd +lat_0=40.03936527 +lon_0=-105.22608958 +datum=WGS84.
        [TestCase(0, 0, -105.22608958, 40.03936527)]
        [TestCase(8000, 0, -105.1323522111769, 40.03932735495884)]
        [TestCase(8000, 8000, -105.13225340149765, 40.111375948420275)]
        [TestCase(10400, 10400, -105.10406396556301, 40.13296413734027)]
        [TestCase(-12000, -12000, -105.3664741073099, 39.931205495001564)]
        [TestCase(12000, -12000, -105.08570505269009, 39.931205495001564)]
        [TestCase(-800, 0, -105.23546332030882, 40.03936489084943)]
        [TestCase(800, 0, -105.21671583969118, 40.03936489084943)]
        public void AuthoredProjectionMatchesIndependentProjAndPreservesMsl(double east, double north, double longitude, double latitude)
        {
            GeodeticReference origin = GeodeticReference.Kbdu;
            const float height = -98.1f;
            var at = new Vector3((float)east, height, (float)north);
            GeodeticReference geographic = FlightFrameConversions.UnityToGeodetic(at, origin);
            Assert.That(geographic.latitudeDegrees, Is.EqualTo(latitude).Within(2e-9));
            Assert.That(geographic.longitudeDegrees, Is.EqualTo(longitude).Within(2e-9));
            Assert.That(geographic.altitudeMslMeters, Is.EqualTo(origin.altitudeMslMeters + height).Within(1e-9));
            Vector3 projected = FlightFrameConversions.GeodeticToUnity(latitude, longitude, geographic.altitudeMslMeters, origin);
            Assert.That(Vector3.Distance(projected, at), Is.LessThan(0.05f));
        }

        [TestCase(0, 0)]
        [TestCase(8000, 0)]
        [TestCase(8000, 8000)]
        [TestCase(10400, 10400)]
        public void PhysicalVelocityWindAndAttitudeUseProjectedBasis(float east, float north)
        {
            GeodeticReference origin = GeodeticReference.Kbdu;
            GeodeticReference at = FlightFrameConversions.UnityToGeodetic(new Vector3(east, 150, north), origin);
            Vector3 velocity = FlightFrameConversions.NedFeetPerSecondToProjectedMetersPerSecond(120, 40, -8,
                at.latitudeDegrees, at.longitudeDegrees, at.altitudeMslMeters, origin);
            Vector3 inverse = FlightFrameConversions.ProjectedMetersPerSecondToNed(velocity,
                at.latitudeDegrees, at.longitudeDegrees, at.altitudeMslMeters, origin);
            Assert.That(inverse.x, Is.EqualTo(120 * 0.3048).Within(1e-5));
            Assert.That(inverse.y, Is.EqualTo(40 * 0.3048).Within(1e-5));
            Assert.That(inverse.z, Is.EqualTo(-8 * 0.3048).Within(1e-5));
            double lat = at.latitudeDegrees * Math.PI / 180, sine = Math.Sin(lat);
            double factor = 1 - (1 / 298.257223563) * (2 - 1 / 298.257223563) * sine * sine;
            double m = 6378137 * (1 - (1 / 298.257223563) * (2 - 1 / 298.257223563)) / Math.Pow(factor, 1.5);
            double n = 6378137 / Math.Sqrt(factor);
            const double dt = 0.1;
            Vector3 displaced = FlightFrameConversions.GeodeticToUnity(
                at.latitudeDegrees + 120 * 0.3048 * dt / (m + at.altitudeMslMeters) * 180 / Math.PI,
                at.longitudeDegrees + 40 * 0.3048 * dt / ((n + at.altitudeMslMeters) * Math.Cos(lat)) * 180 / Math.PI,
                at.altitudeMslMeters + 8 * 0.3048 * dt, origin);
            Vector3 current = FlightFrameConversions.GeodeticToUnity(at.latitudeDegrees, at.longitudeDegrees, at.altitudeMslMeters, origin);
            Assert.That(Vector3.Distance((displaced - current) / (float)dt, velocity), Is.LessThan(0.02));
            Quaternion rotation = FlightFrameConversions.JsbsimAttitudeToProjectedUnity(0, 0, 0,
                at.latitudeDegrees, at.longitudeDegrees, origin);
            Vector3 projectedNorth = FlightFrameConversions.NedFeetPerSecondToProjectedMetersPerSecond(1, 0, 0,
                at.latitudeDegrees, at.longitudeDegrees, at.altitudeMslMeters, origin).normalized;
            Assert.That(Vector3.Angle(rotation * Vector3.forward, projectedNorth), Is.LessThan(0.02));
            double gridHeading = Math.Atan2(projectedNorth.x, projectedNorth.z) * 180 / Math.PI;
            Assert.That(Math.Abs(Mathf.DeltaAngle((float)FlightFrameConversions.UnityHeadingToTrue(gridHeading,
                at.latitudeDegrees, at.longitudeDegrees, origin), 0)), Is.LessThan(0.001));
        }

        [TestCase(0, 0)]
        [TestCase(8000, 0)]
        [TestCase(8000, 8000)]
        [TestCase(10400, 10400)]
        [TestCase(-600, 0)]
        [TestCase(600, 0)]
        public void ActualNativeResetUsesGeodeticContactAndMslPresentation(float east, float north)
        {
            ProductionTerrainSurfaceAsset terrain = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.TrySample(east, north, out ProductionTerrainSample ground), Is.True);
            GeodeticReference origin = GeodeticReference.Kbdu;
            GameObject fixture = new GameObject("ProjectedNativeTestFixture");
            using (var backend = new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
            {
                try
                {
                    Assert.That(backend.Initialize(new FlightDynamicsBackendContext
                    {
                        aircraftDefinition = FlightAircraftDefinition.EditorC172,
                        simulationRoot = fixture.transform,
                        aircraftState = fixture.AddComponent<AircraftState>(),
                        terrainSurface = terrain,
                        localOrigin = origin
                    }), Is.True, backend.LastError);
                    foreach (double heading in new[] {89.972, 269.972})
                    {
                        var point = new Vector3(east, ground.heightMeters + 1.25f, north);
                        GeodeticReference geographic = FlightFrameConversions.UnityToGeodetic(point, origin);
                        Assert.That(backend.Reset(new FlightDynamicsInitialConditions
                        {
                            latitudeDegrees = geographic.latitudeDegrees, longitudeDegrees = geographic.longitudeDegrees,
                            altitudeMslMeters = geographic.altitudeMslMeters, terrainElevationMslMeters = origin.altitudeMslMeters + ground.heightMeters,
                            calibratedAirspeedKnots = 0, headingDegrees = heading, engineRunning = false
                        }), Is.True, backend.LastError);
                        Assert.That(Vector3.Distance(backend.CurrentState.positionUnityMeters, point), Is.LessThan(0.01),
                            "Actual native radial ASL must map to geodetic MSL and the same projected XZ.");
                        Assert.That(backend.CurrentState.altitudeAglMeters, Is.EqualTo(1.25).Within(0.01),
                            "Actual native geodetic ground callback must agree with authored query height.");
                        Assert.That(backend.Advance(1.0 / 120.0), Is.True, backend.LastError);
                        Assert.That(backend.CurrentState.IsFinite, Is.True);
                        Assert.That(backend.CurrentState.positionUnityMeters.y,
                            Is.EqualTo(ground.heightMeters + 1.25).Within(0.02));
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(fixture); }
            }
        }
    }
}
#endif
