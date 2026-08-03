using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Environment.Editor.Tests
{
    public sealed class AirportNearFieldDetailV1EditModeTests
    {
        [Test]
        public void ProductPrefabContainsOneValidNearFieldDetailRoot()
        {
            GameObject product = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot environment =
                    product.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(environment, Is.Not.Null);
                Assert.That(environment.airportNearFieldDetail, Is.Not.Null);
                AirportNearFieldDetailRoot[] roots = product
                    .GetComponentsInChildren<AirportNearFieldDetailRoot>(true);
                Assert.That(roots.Length, Is.EqualTo(1));
                Assert.That(
                    roots[0].TryValidateContract(out string report),
                    Is.True,
                    report);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
        }

        [Test]
        public void GrassIsOneBoundedInstancedDrawAndAvoidsOperationalSurfaces()
        {
            GameObject product = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot environment =
                    product.GetComponent<ProductionEnvironmentRoot>();
                AirportNearFieldDetailRoot root = product
                    .GetComponentInChildren<AirportNearFieldDetailRoot>(true);
                AirportNearFieldGrassRenderer grass = root.grass;
                Assert.That(grass, Is.Not.Null);
                Assert.That(grass.Instances.Length, Is.EqualTo(1000));
                Assert.That(
                    grass.Instances.Length,
                    Is.LessThanOrEqualTo(
                        AirportNearFieldGrassRenderer.MaximumInstances));
                Assert.That(grass.AuthoredMaximumDrawCount, Is.EqualTo(1));
                Assert.That(grass.SharedClumpMesh.subMeshCount, Is.EqualTo(1));
                Assert.That(
                    grass.SharedMaterial.enableInstancing,
                    Is.True);
                Assert.That(grass.SharedMaterial, Is.SameAs(root.sharedMaterial));

                foreach (AirportNearFieldGrassRenderer.Instance instance in
                         grass.Instances)
                {
                    Vector3 point = instance.Matrix.GetColumn(3);
                    Assert.That(
                        environment.authoritativeTerrainSurface.TrySample(
                            point.x,
                            point.z,
                            out ProductionTerrainSample sample),
                        Is.True);
                    Assert.That(sample.IsRunwayOrApron, Is.False);
                    Assert.That(sample.IsWater, Is.False);
                    Assert.That(
                        sample.category,
                        Is.Not.EqualTo(ProductionSurfaceCategory.Road));
                    Assert.That(Mathf.Abs(point.z + 30f), Is.GreaterThanOrEqualTo(17.5f));
                    Assert.That(Mathf.Abs(point.z - 30f), Is.GreaterThanOrEqualTo(6.5f));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
        }

        [Test]
        public void StaticDetailIsOneGroundedOpaqueSourceWithoutRuntimeCostMultipliers()
        {
            GameObject product = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                AirportNearFieldDetailRoot root = product
                    .GetComponentInChildren<AirportNearFieldDetailRoot>(true);
                Assert.That(root.staticDetailRenderer, Is.Not.Null);
                MeshFilter filter =
                    root.staticDetailRenderer.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(1));
                Assert.That(
                    root.GetComponentsInChildren<Renderer>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    root.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    root.GetComponentsInChildren<LODGroup>(true),
                    Is.Empty);
                Assert.That(
                    root.staticDetailRenderer.sharedMaterials.Single(),
                    Is.SameAs(root.sharedMaterial));
                Assert.That(
                    root.authoredMaximumSelectedTriangles,
                    Is.LessThanOrEqualTo(
                        AirportNearFieldDetailRoot.MaximumSelectedTriangles));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
        }
    }
}
