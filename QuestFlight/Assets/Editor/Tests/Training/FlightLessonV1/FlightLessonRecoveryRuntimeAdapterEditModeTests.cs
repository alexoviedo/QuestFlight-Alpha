using System.IO;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests.Training.FlightLessonV1
{
    public sealed class FlightLessonRecoveryRuntimeAdapterEditModeTests
    {
        private ProductionTerrainSurfaceAsset surface;

        [SetUp]
        public void SetUp()
        {
            surface = ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            surface.layers = new[]
            {
                new ProductionTerrainSurfaceAsset.GridLayer
                {
                    stableId = "test_grid",
                    zone = ProductionTerrainZone.Airport,
                    priority = 0,
                    minimumEnuMeters = new Vector2(-2000f, -2000f),
                    spacingMeters = 2000f,
                    width = 3,
                    height = 3,
                    heightMeters = new[]
                    {
                        3f, 3f, 3f,
                        3f, 3f, 3f,
                        3f, 3f, 3f
                    },
                    normals = new[]
                    {
                        Vector3.up, Vector3.up, Vector3.up,
                        Vector3.up, Vector3.up, Vector3.up,
                        Vector3.up, Vector3.up, Vector3.up
                    }
                }
            };
            surface.regions = new[]
            {
                new ProductionTerrainSurfaceAsset.SurfaceRegion
                {
                    stableId = "runway",
                    category = ProductionSurfaceCategory.Runway,
                    priority = 0,
                    boundaryEnuMeters = new[]
                    {
                        new Vector2(-100f, -100f),
                        new Vector2(100f, -100f),
                        new Vector2(100f, 100f),
                        new Vector2(-100f, 100f)
                    }
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(surface);
        }

        [Test]
        public void MapsAuthoritativeStateWithoutChangingAxesOrUnits()
        {
            FlightDynamicsState state = new FlightDynamicsState
            {
                positionUnityMeters = new Vector3(12f, 34f, 56f),
                rotationUnity = Quaternion.Euler(1f, 2f, 3f),
                velocityUnityMetersPerSecond = new Vector3(7f, 8f, 9f),
                altitudeAglMeters = 45d,
                calibratedAirspeedKnots = 88d,
                groundSpeedKnots = 84d,
                verticalSpeedFeetPerMinute = -321d,
                pitchDegrees = 4d,
                bankDegrees = -7d,
                weightOnWheels = true
            };

            FlightLessonAircraftSafetyState mapped =
                FlightLessonRecoveryRuntimeAdapter.BuildSafetyState(state);

            Assert.That(mapped.positionUnityMeters, Is.EqualTo(state.positionUnityMeters));
            Assert.That(mapped.rotationUnity, Is.EqualTo(state.rotationUnity));
            Assert.That(mapped.velocityUnityMetersPerSecond, Is.EqualTo(state.velocityUnityMetersPerSecond));
            Assert.That(mapped.altitudeAglMeters, Is.EqualTo(45f));
            Assert.That(mapped.calibratedAirspeedKnots, Is.EqualTo(88f));
            Assert.That(mapped.groundSpeedKnots, Is.EqualTo(84f));
            Assert.That(mapped.verticalSpeedFeetPerMinute, Is.EqualTo(-321f));
            Assert.That(mapped.pitchDegrees, Is.EqualTo(4f));
            Assert.That(mapped.bankDegrees, Is.EqualTo(-7f));
            Assert.That(mapped.weightOnWheels, Is.True);
        }

        [Test]
        public void SamplesTerrainByEnuEastAndNorthAndMapsSurfacePolicy()
        {
            bool found = FlightLessonRecoveryRuntimeAdapter.TryMapTerrainSample(
                surface,
                new Vector3(25f, 999f, -40f),
                out FlightLessonTerrainSample terrain);

            Assert.That(found, Is.True);
            Assert.That(terrain.isValid, Is.True);
            Assert.That(terrain.heightUnityMeters, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(terrain.normalUnity, Is.EqualTo(Vector3.up));
            Assert.That(terrain.isRunwayOrApron, Is.True);
            Assert.That(terrain.isWater, Is.False);
        }

        [Test]
        public void ConvertsCheckpointEnuAndMslIntoBackendInitialConditions()
        {
            FlightLessonRecoveryCheckpoint checkpoint = new FlightLessonRecoveryCheckpoint(
                "test_checkpoint",
                2,
                new Vector3(1200f, 0f, 350f),
                6200f,
                82f,
                35f,
                4f,
                -2f,
                true);

            bool converted = FlightLessonRecoveryRuntimeAdapter.TryBuildInitialConditions(
                checkpoint,
                surface,
                out FlightDynamicsInitialConditions initial);

            Assert.That(converted, Is.True);
            Assert.That(initial.altitudeMslMeters,
                Is.EqualTo(6200d * FlightFrameConversions.FeetToMeters).Within(0.0001d));
            Assert.That(initial.terrainElevationMslMeters,
                Is.LessThan(initial.altitudeMslMeters));
            Assert.That(initial.calibratedAirspeedKnots, Is.EqualTo(82d));
            Assert.That(initial.headingDegrees, Is.EqualTo(35d));
            Assert.That(initial.pitchDegrees, Is.EqualTo(4d));
            Assert.That(initial.bankDegrees, Is.EqualTo(-2d));
            Assert.That(initial.flightPathAngleDegrees, Is.EqualTo(0d));
            Assert.That(initial.engineRunning, Is.True);

            Vector3 recovered = FlightFrameConversions.GeodeticToUnity(
                initial.latitudeDegrees,
                initial.longitudeDegrees,
                initial.altitudeMslMeters,
                GeodeticReference.Kbdu);
            Assert.That(recovered.x, Is.EqualTo(checkpoint.positionUnityMeters.x).Within(0.05f));
            Assert.That(recovered.z, Is.EqualTo(checkpoint.positionUnityMeters.z).Within(0.05f));
        }

        [Test]
        public void RecoveryLivenessRequiresConsecutiveBackendAdvancementAndMotion()
        {
            FlightLessonRecoveryLivenessGate gate =
                new FlightLessonRecoveryLivenessGate();
            FlightDynamicsState state = LiveState(10d, Vector3.zero, 30f);
            gate.Begin(state, true);

            for (int step = 1;
                 step < 25;
                 step++)
            {
                state = LiveState(
                    10d + step * 0.02d,
                    new Vector3(0f, 0f, step * 0.03f),
                    30f);
                Assert.That(gate.Observe(
                        state,
                        true,
                        true,
                        0.02f,
                        out string pendingFailure),
                    Is.EqualTo(FlightLessonRecoveryLivenessStatus.Pending),
                    pendingFailure);
            }

            state = LiveState(10.52d, new Vector3(0f, 0f, 15.6f), 30f);
            Assert.That(gate.Observe(
                    state,
                    true,
                    true,
                    0.04f,
                    out string failure),
                Is.EqualTo(FlightLessonRecoveryLivenessStatus.Passed),
                failure);
        }

        [Test]
        public void RecoveryLivenessRejectsDisabledOrStaticAuthority()
        {
            FlightDynamicsState state = LiveState(4d, Vector3.zero, 25f);
            FlightLessonRecoveryLivenessGate disabledGate =
                new FlightLessonRecoveryLivenessGate();
            disabledGate.Begin(state, true);
            Assert.That(disabledGate.Observe(
                    state,
                    false,
                    true,
                    0.02f,
                    out string disabledFailure),
                Is.EqualTo(FlightLessonRecoveryLivenessStatus.Failed));
            StringAssert.Contains("disabled", disabledFailure);

            FlightLessonRecoveryLivenessGate staticGate =
                new FlightLessonRecoveryLivenessGate();
            staticGate.Begin(state, true);
            FlightLessonRecoveryLivenessStatus status =
                FlightLessonRecoveryLivenessStatus.Pending;
            string failure = string.Empty;
            for (int i = 0; i < 60 && status == FlightLessonRecoveryLivenessStatus.Pending; i++)
            {
                status = staticGate.Observe(
                    state,
                    true,
                    true,
                    0.02f,
                    out failure);
            }
            Assert.That(status, Is.EqualTo(FlightLessonRecoveryLivenessStatus.Failed));
            StringAssert.Contains("did not advance", failure);
        }

        [Test]
        public void RuntimeAdapterContainsNoHierarchyRepairCameraWritesOrPhysicsQueries()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Scripts/Training/FlightLessonV1/FlightLessonRecoveryRuntimeAdapter.cs");
            string source = File.ReadAllText(path);

            StringAssert.DoesNotContain("new GameObject(", source);
            StringAssert.DoesNotContain("Instantiate(", source);
            StringAssert.DoesNotContain("FindObject", source);
            StringAssert.DoesNotContain("Camera.", source);
            StringAssert.DoesNotContain(".transform.", source);
            StringAssert.DoesNotContain("Physics.", source);
            StringAssert.Contains("OVRManager.SetColorScaleAndOffset", source);
            StringAssert.Contains("TryRecoverToInitialConditions", source);
        }

        private static FlightDynamicsState LiveState(
            double simulationTimeSeconds,
            Vector3 position,
            float forwardVelocityMetersPerSecond)
        {
            return new FlightDynamicsState
            {
                simulationTimeSeconds = simulationTimeSeconds,
                latitudeDegrees = 40d,
                longitudeDegrees = -105d,
                altitudeMslMeters = 1600d,
                altitudeAglMeters = 100d,
                calibratedAirspeedKnots = 60d,
                groundSpeedKnots = 60d,
                headingDegrees = 80d,
                engineRpm = 2200d,
                loadFactorG = 1d,
                positionUnityMeters = position,
                rotationUnity = Quaternion.identity,
                velocityUnityMetersPerSecond =
                    new Vector3(0f, 0f, forwardVelocityMetersPerSecond)
            };
        }
    }
}
