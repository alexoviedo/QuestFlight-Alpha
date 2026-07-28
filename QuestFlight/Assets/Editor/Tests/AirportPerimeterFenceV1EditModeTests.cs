using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment.Editor.Tests
{
    public sealed class AirportPerimeterFenceV1EditModeTests
    {
        [Test]
        public void ProductContainsOneValidatedAuthoredFence()
        {
            GameObject product = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot environment =
                    product.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(environment, Is.Not.Null);
                Assert.That(environment.airportPerimeterFence, Is.Not.Null);
                AirportPerimeterFenceRoot[] fences = product
                    .GetComponentsInChildren<AirportPerimeterFenceRoot>(true);
                Assert.That(fences, Has.Length.EqualTo(1));
                Assert.That(fences[0].TryValidateContract(out string report), Is.True, report);
                Assert.That(
                    fences[0].transform.IsChildOf(environment.airportContext),
                    Is.True,
                    "Fence must remain in the explicitly authored airport context.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
        }

        [Test]
        public void FenceReusesOneOpaqueMaterialAndAddsNoPhysicsOrShadows()
        {
            GameObject fence = PrefabUtility.LoadPrefabContents(
                AirportPerimeterFenceAuthoring.PrefabPath);
            try
            {
                AirportPerimeterFenceRoot marker =
                    fence.GetComponent<AirportPerimeterFenceRoot>();
                Assert.That(marker, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(marker.sharedMaterial),
                    Is.EqualTo(AirportPerimeterFenceAuthoring.SharedMaterialPath));
                Renderer[] renderers = fence.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Has.Length.EqualTo(4));
                Assert.That(
                    renderers.Select(renderer => renderer.sharedMaterial).Distinct().Count(),
                    Is.EqualTo(1));
                Assert.That(
                    renderers.All(renderer =>
                        renderer.shadowCastingMode == ShadowCastingMode.Off &&
                        !renderer.receiveShadows),
                    Is.True);
                Assert.That(fence.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(fence.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fence);
            }
        }

        [Test]
        public void FenceUsesNoLodSwitchAndStaysInsideSerializedClosure()
        {
            GameObject fence = PrefabUtility.LoadPrefabContents(
                AirportPerimeterFenceAuthoring.PrefabPath);
            try
            {
                AirportPerimeterFenceRoot marker =
                    fence.GetComponent<AirportPerimeterFenceRoot>();
                Assert.That(marker.authoredPerimeterLengthMeters,
                    Is.InRange(3000f, 6000f));
                Assert.That(marker.authoredMaximumSelectedTriangles,
                    Is.LessThanOrEqualTo(AirportPerimeterFenceRoot.MaximumSelectedTriangles));
                Assert.That(marker.authoredSerializedTriangles,
                    Is.LessThanOrEqualTo(AirportPerimeterFenceRoot.MaximumSerializedTriangles));
                Assert.That(fence.GetComponentsInChildren<LODGroup>(true), Is.Empty);
                Assert.That(marker.sections, Has.Length.EqualTo(4));
                Assert.That(marker.authoredMaximumSelectedTriangles,
                    Is.EqualTo(marker.authoredSerializedTriangles));
                Assert.That(marker.authoredSerializedTriangles,
                    Is.GreaterThan(0).And.LessThanOrEqualTo(104));
                Assert.That(marker.authoredSerializedTriangles, Is.EqualTo(104));
                Assert.That(
                    marker.sections.All(renderer =>
                        renderer.GetComponent<MeshFilter>().sharedMesh.colors.All(
                            color => color.a < 0.5f)),
                    Is.True,
                    "Every fence vertex must select the analytic cutout path.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fence);
            }
        }
    }
}
