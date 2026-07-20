#if UNITY_EDITOR
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class ProductionTerrainSurfaceEditModeTests
    {
        [Test]
        public void HighestPriorityGridProvidesBilinearHeightNormalZoneAndMacroUv()
        {
            ProductionTerrainSurfaceAsset surface = ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            try
            {
                surface.macroMinimumEnuMeters = Vector2.zero;
                surface.macroSizeMeters = new Vector2(20f, 20f);
                surface.layers = new[]
                {
                    Layer("near", ProductionTerrainZone.Near, 10, 0f),
                    Layer("airport", ProductionTerrainZone.Airport, 0, 10f)
                };

                Assert.That(surface.TrySample(5f, 5f, out ProductionTerrainSample sample), Is.True);
                Assert.That(sample.heightMeters, Is.EqualTo(20f).Within(0.0001f));
                Vector3 expectedNormal = new Vector3(-1f, 1f, -1f).normalized;
                Assert.That(sample.normal.x, Is.EqualTo(expectedNormal.x).Within(0.0001f));
                Assert.That(sample.normal.y, Is.EqualTo(expectedNormal.y).Within(0.0001f));
                Assert.That(sample.normal.z, Is.EqualTo(expectedNormal.z).Within(0.0001f));
                Assert.That(sample.zone, Is.EqualTo(ProductionTerrainZone.Airport));
                Assert.That(sample.macroUv.x, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(sample.macroUv.y, Is.EqualTo(0.25f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void SurfaceRegionsUseDeterministicPriorityAndClassification()
        {
            ProductionTerrainSurfaceAsset surface = ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            try
            {
                surface.layers = new[] { Layer("near", ProductionTerrainZone.Near, 0, 0f) };
                surface.regions = new[]
                {
                    Region("apron", ProductionSurfaceCategory.Apron, 10),
                    Region("runway", ProductionSurfaceCategory.Runway, 0)
                };

                Assert.That(surface.TrySample(5f, 5f, out ProductionTerrainSample sample), Is.True);
                Assert.That(sample.category, Is.EqualTo(ProductionSurfaceCategory.Runway));
                Assert.That(sample.IsRunwayOrApron, Is.True);
                Assert.That(sample.IsWater, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void BestFitPlaneMatchesAuthoredGridWithoutPhysicsQueries()
        {
            ProductionTerrainSurfaceAsset surface = ScriptableObject.CreateInstance<ProductionTerrainSurfaceAsset>();
            try
            {
                surface.layers = new[] { Layer("near", ProductionTerrainZone.Near, 0, 0f) };
                Vector2[] footprint =
                {
                    new Vector2(1f, 1f),
                    new Vector2(9f, 1f),
                    new Vector2(9f, 9f),
                    new Vector2(1f, 9f)
                };

                Assert.That(surface.TryBestFitPlane(footprint, out ProductionTerrainPlane plane), Is.True);
                Assert.That(plane.HeightAt(5f, 5f), Is.EqualTo(10f).Within(0.0001f));
                Assert.That(plane.maximumResidualMeters, Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void PlacementAndParkingManifestsRetainRequiredAuthorityFields()
        {
            ProductionPlacementManifest placement = ScriptableObject.CreateInstance<ProductionPlacementManifest>();
            AirportParkingStandManifest parking = ScriptableObject.CreateInstance<AirportParkingStandManifest>();
            try
            {
                placement.entries = new[]
                {
                    new ProductionPlacementManifest.Entry
                    {
                        stableId = "aircraft-stand-a",
                        sourceFeatureId = "authored-stand-a",
                        category = ProductionPlacementCategory.ParkedAircraft,
                        modelForwardAxisCorrectionDegrees = 90f,
                        modelContactPoints = new[] { Vector3.zero, Vector3.right, Vector3.left },
                        foundationMode = ProductionFoundationMode.BestFitPlane,
                        lodClass = ProductionLodClass.NearInstanced,
                        sourceLicenseClass = "CC-BY-4.0"
                    }
                };
                parking.stands = new[]
                {
                    new AirportParkingStandManifest.Stand
                    {
                        standId = "stand-a",
                        headingDegrees = 83f,
                        minimumSpacingMeters = 18f,
                        apronPlaneNormal = Vector3.up
                    }
                };

                Assert.That(placement.entries[0].stableId, Is.Not.Empty);
                Assert.That(placement.entries[0].modelContactPoints, Has.Length.EqualTo(3));
                Assert.That(placement.entries[0].modelForwardAxisCorrectionDegrees, Is.EqualTo(90f));
                Assert.That(parking.stands[0].standId, Is.Not.Empty);
                Assert.That(parking.stands[0].minimumSpacingMeters, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(placement);
                Object.DestroyImmediate(parking);
            }
        }

        private static ProductionTerrainSurfaceAsset.GridLayer Layer(
            string id,
            ProductionTerrainZone zone,
            int priority,
            float offset)
        {
            return new ProductionTerrainSurfaceAsset.GridLayer
            {
                stableId = id,
                zone = zone,
                priority = priority,
                minimumEnuMeters = Vector2.zero,
                spacingMeters = 10f,
                width = 2,
                height = 2,
                heightMeters = new[] { offset, offset + 10f, offset + 10f, offset + 20f }
            };
        }

        private static ProductionTerrainSurfaceAsset.SurfaceRegion Region(
            string id,
            ProductionSurfaceCategory category,
            int priority)
        {
            return new ProductionTerrainSurfaceAsset.SurfaceRegion
            {
                stableId = id,
                category = category,
                priority = priority,
                boundaryEnuMeters = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(10f, 0f),
                    new Vector2(10f, 10f),
                    new Vector2(0f, 10f)
                }
            };
        }
    }
}
#endif
