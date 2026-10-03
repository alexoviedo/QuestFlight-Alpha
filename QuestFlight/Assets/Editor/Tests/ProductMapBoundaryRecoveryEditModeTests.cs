#if UNITY_INCLUDE_TESTS
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductMapBoundaryRecoveryEditModeTests
    {
        [Test]
        public void GuardTriggersInsideEveryTerrainEdgeBeforeSamplesEnd()
        {
            Vector2 minimum = new Vector2(-12000f, -12000f);
            Vector2 size = new Vector2(24000f, 24000f);

            Assert.That(ProductMapBoundaryRecoveryPolicy.ShouldRecover(
                new Vector3(0f, 1000f, 0f), minimum, size, 1500f), Is.False);
            foreach (Vector3 position in new[]
                     {
                         new Vector3(-10500f, 1000f, 0f),
                         new Vector3(10500f, 1000f, 0f),
                         new Vector3(0f, 1000f, -10500f),
                         new Vector3(0f, 1000f, 10500f)
                     })
            {
                Assert.That(ProductMapBoundaryRecoveryPolicy.ShouldRecover(
                    position, minimum, size, 1500f), Is.True, position.ToString());
            }
        }

        [Test]
        public void RecoveryIsInsideTerrainFacesCenterAndPreservesSafeFlight()
        {
            ProductionTerrainSurfaceAsset terrain = CreateFlatTerrain();
            try
            {
                FlightDynamicsState state = new FlightDynamicsState
                {
                    simulationTimeSeconds = 10d,
                    latitudeDegrees = 40d,
                    longitudeDegrees = -105d,
                    altitudeMslMeters = GeodeticReference.Kbdu.altitudeMslMeters + 500d,
                    altitudeAglMeters = 500d,
                    calibratedAirspeedKnots = 92d,
                    groundSpeedKnots = 90d,
                    verticalSpeedFeetPerMinute = 0d,
                    headingDegrees = 90d,
                    pitchDegrees = 0d,
                    bankDegrees = 0d,
                    angleOfAttackDegrees = 2d,
                    sideslipDegrees = 0d,
                    engineRpm = 2300d,
                    loadFactorG = 1d,
                    positionUnityMeters = new Vector3(10800f, 500f, 1200f),
                    rotationUnity = Quaternion.identity,
                    velocityUnityMetersPerSecond = Vector3.right * 45f,
                    angularVelocityBodyDegreesPerSecond = Vector3.zero
                };

                bool built = ProductMapBoundaryRecoveryPolicy.TryBuildRecoveryInitialConditions(
                    state, terrain, 1800f, 300f, 80f,
                    out FlightDynamicsInitialConditions result,
                    out string failure);

                Assert.That(built, Is.True, failure);
                Vector3 recovered = FlightFrameConversions.GeodeticToUnity(
                    result.latitudeDegrees,
                    result.longitudeDegrees,
                    result.altitudeMslMeters,
                    GeodeticReference.Kbdu);
                Assert.That(recovered.x, Is.EqualTo(10200f).Within(0.5f));
                Assert.That(recovered.z, Is.EqualTo(1200f).Within(0.5f));
                Assert.That(result.headingDegrees, Is.InRange(260d, 280d));
                Assert.That(result.altitudeMslMeters,
                    Is.GreaterThanOrEqualTo(state.altitudeMslMeters));
                Assert.That(result.calibratedAirspeedKnots, Is.EqualTo(92d));
                Assert.That(result.engineRunning, Is.True);
                Assert.That(result.bankDegrees, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(terrain);
            }
        }

        [Test]
        public void SerializedProductHasExactlyOneFullyBoundBoundaryRecovery()
        {
            GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductFlightModesFoundationAuthoring.WorldPrefabPath);
            ProductMapBoundaryRecovery[] recoveries = world
                .GetComponentsInChildren<ProductMapBoundaryRecovery>(true);
            Assert.That(recoveries, Has.Length.EqualTo(1));
            Assert.That(recoveries[0].transform.name, Is.EqualTo("ProductionInputRoot"));
            Assert.That(recoveries[0].HasAuthoredReferences, Is.True);
            Assert.That(recoveries[0].AuthoredModeCoordinator,
                Is.EqualTo(world.GetComponentsInChildren<ProductFlightModeCoordinator>(true).Single()));
            Assert.That(recoveries[0].TriggerInsetMeters,
                Is.LessThan(recoveries[0].ReturnInsetMeters));
        }

        [Test]
        public void RuntimeGuardUsesAuthoritySeamWithoutHierarchyOrTransformWrites()
        {
            string source = File.ReadAllText(
                "Assets/Scripts/Product/ProductMapBoundaryRecovery.cs");
            foreach (string forbidden in new[]
                     {
                         "Find(", "FindObjectOfType", "FindObjectsOfType", "Resources.Load",
                         "Instantiate(", "new GameObject", "AddComponent", "SetPositionAndRotation",
                         ".position =", ".rotation =", ".localPosition =", ".localRotation ="
                     })
                Assert.That(source, Does.Not.Contain(forbidden), forbidden);
            Assert.That(source, Does.Contain("TryRecoverToInitialConditions"));
            Assert.That(source, Does.Not.Contain("ResetToConfiguredInitialConditions"));
        }

        private static ProductionTerrainSurfaceAsset CreateFlatTerrain()
        {
            ProductionTerrainSurfaceAsset terrain =
                ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            terrain.macroMinimumEnuMeters = new Vector2(-12000f, -12000f);
            terrain.macroSizeMeters = new Vector2(24000f, 24000f);
            terrain.layers = new[]
            {
                new ProductionTerrainSurfaceAsset.GridLayer
                {
                    stableId = "flat_test",
                    zone = ProductionTerrainZone.Far,
                    priority = 0,
                    minimumEnuMeters = new Vector2(-12000f, -12000f),
                    spacingMeters = 24000f,
                    width = 2,
                    height = 2,
                    heightMeters = new[] { 0f, 0f, 0f, 0f },
                    normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up }
                }
            };
            return terrain;
        }
    }
}
#endif
