using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class AirportCloseTreeLodEditModeTests
    {
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string SourceManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/" +
            "AirportCloseTreeLods_SourceManifest.json";
        private const string PlacementManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/" +
            "ProductionAirportCloseTreeInstances.json";

        [Test]
        public void ProductUsesOneBoundedSourceDerivedCloseTreeRenderer()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            Assert.That(environment, Is.Not.Null);
            AirportCloseTreeLodRenderer[] renderers =
                environment.GetComponentsInChildren<AirportCloseTreeLodRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(1));

            AirportCloseTreeLodRenderer renderer = renderers[0];
            Assert.That(renderer.Instances, Has.Length.EqualTo(696));
            Assert.That(renderer.Lod0Mesh, Is.Not.Null);
            Assert.That(renderer.Lod1Mesh, Is.Not.Null);
            Assert.That(renderer.Lod2Mesh, Is.Not.Null);
            Assert.That(TriangleCount(renderer.Lod0Mesh), Is.EqualTo(2337));
            Assert.That(TriangleCount(renderer.Lod1Mesh), Is.EqualTo(194));
            Assert.That(TriangleCount(renderer.Lod2Mesh), Is.EqualTo(2));
            Assert.That(renderer.AuthoredMaximumDrawCount, Is.EqualTo(3));
            Assert.That(renderer.AuthoredMaximumSelectedTriangleCount,
                Is.EqualTo(22066));
            Assert.That(renderer.ProjectedScreenSizeSelection, Is.True);
            Assert.That(renderer.PersistentHysteresis, Is.True);
            Assert.That(renderer.CenterEyeWorldUprightImpostor, Is.True);
            Assert.That(renderer.PerEyeFacingBehavior, Is.False);
            Assert.That(renderer.AlphaToCoverageTransition, Is.True);
            Assert.That(renderer.Lod0EnterScreenPixels,
                Is.GreaterThan(renderer.Lod0ExitScreenPixels));
            Assert.That(renderer.Lod1EnterScreenPixels,
                Is.GreaterThan(renderer.Lod1ExitScreenPixels));
            Assert.That(renderer.MinimumResidencySeconds,
                Is.GreaterThanOrEqualTo(0.7f));
            Assert.That(renderer.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(renderer.GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);
            Assert.That(renderer.transform.childCount, Is.EqualTo(0),
                "Close-tree instances must not become one GameObject per tree.");
        }

        [Test]
        public void CloseTreeAssetsRetainLicenseAndDeterministicPlacementEvidence()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(SourceManifestPath);
            TextAsset placements =
                AssetDatabase.LoadAssetAtPath<TextAsset>(PlacementManifestPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(placements, Is.Not.Null);
            Assert.That(source.text, Does.Contain("\"asset\": \"Tree Small 02\""));
            Assert.That(source.text, Does.Contain("\"license\": \"CC0 1.0 Universal\""));
            Assert.That(source.text, Does.Contain("\"raw_source_committed\": false"));
            Assert.That(source.text,
                Does.Contain("\"schema\": \"questflight.airport-tree-impostor-source.v2\""));
            Assert.That(source.text, Does.Contain("\"xr_center_eye_impostor\": true"));
            Assert.That(source.text, Does.Contain("\"azimuth_frames\": 8"));
            Assert.That(placements.text,
                Does.Contain("\"schema\": \"questflight.airport-close-tree-instances.v1\""));
            Assert.That(placements.text, Does.Contain("\"instance_count\": 696"));
        }

        [Test]
        public void CloseTreesReuseCanopyMaterialAndStayInsideProductBudget()
        {
            GameObject environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            ProductionEnvironmentRoot root =
                environment.GetComponent<ProductionEnvironmentRoot>();
            PhotorealAerialSurfaceRoot surface =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            AirportCloseTreeLodRenderer closeTrees =
                environment.GetComponentInChildren<AirportCloseTreeLodRenderer>(true);
            Assert.That(root, Is.Not.Null);
            Assert.That(surface, Is.Not.Null);
            Assert.That(closeTrees, Is.Not.Null);
            Assert.That(surface.canopySurfaceRenderers
                .Select(item => item.sharedMaterial).Distinct().Single(),
                Is.SameAs(closeTrees.SharedCanopyMaterial));
            Assert.That(closeTrees.SharedCanopyMaterial.GetTexture("_HeroLeafAtlas"),
                Is.Not.Null);
            Assert.That(closeTrees.SharedCanopyMaterial.GetTexture("_TreeImpostorAtlas"),
                Is.Not.Null);

            ProductionEnvironmentBudget budget = root.CalculateBudget();
            Assert.That(budget.rendererCount, Is.LessThanOrEqualTo(75));
            Assert.That(budget.triangleCount, Is.LessThanOrEqualTo(650000));
            Assert.That(budget.materialCount, Is.LessThanOrEqualTo(29));
        }

        private static int TriangleCount(Mesh mesh)
        {
            return checked((int)(mesh.GetIndexCount(0) / 3u));
        }
    }
}
