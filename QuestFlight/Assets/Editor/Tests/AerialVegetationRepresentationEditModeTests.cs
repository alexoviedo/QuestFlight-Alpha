using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Environment.Editor;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Tests.Editor
{
    public sealed class AerialVegetationRepresentationEditModeTests
    {
        private const string PrefabPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Generated/AerialVegetationRoot.prefab";
        private const string HandoffPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Textures/AerialVegetation_HandoffMask_R8.png";
        private const string IncludePath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Shaders/AerialCanopyMap.cginc";
        private const string FrozenLidarManifestSha256 =
            "1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5";

        [Test]
        public void GeneratedPrefab_UsesThreeFixedOrientationDrawsWithinBudget()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            AerialVegetationBatchRenderer renderer = prefab.GetComponent<AerialVegetationBatchRenderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.AuthoredSourcePlacementCount, Is.EqualTo(270));
            Assert.That(renderer.NonEmptyDrawCount, Is.EqualTo(3));
            Assert.That(renderer.AuthoredDrawCount, Is.EqualTo(3));
            Assert.That(renderer.FullyInstancedTriangleCount, Is.EqualTo(17324));
            Assert.That(renderer.AuthoredFullyInstancedTriangleCount, Is.EqualTo(17324));
            Assert.That(renderer.FullyInstancedTriangleCount, Is.LessThanOrEqualTo(19648));
            Assert.That(renderer.AuthoredMaterialCount, Is.EqualTo(1));
            Assert.That(renderer.FixedOrientationOnly, Is.True);
            Assert.That(renderer.CameraFacingBehavior, Is.False);
            Assert.That(renderer.LodCrossfadeBehavior, Is.False);
            Assert.That(renderer.SharedMaterial, Is.Not.Null);
            Assert.That(renderer.SharedMaterial.shader.name,
                Is.EqualTo("QuestFlight/Aerial Scenery V1/Vegetation Atlas Cutout"));
            Assert.That(renderer.Batches, Has.Length.EqualTo(3));
            Assert.That(renderer.Batches.Select(batch => batch.Matrices.Length), Is.All.EqualTo(1));
            Assert.That(renderer.Batches.Select(batch => batch.InstanceTints.Length), Is.All.EqualTo(1));
            Assert.That(renderer.Batches.Select(batch => batch.Mesh), Is.All.Not.Null);
            Assert.That(renderer.DistributionManifest.text, Does.Contain(FrozenLidarManifestSha256));
            Assert.That(prefab.transform.childCount, Is.EqualTo(4));
            Assert.That(prefab.transform.Find("Tier0_EditorBakedTrees_0m_500m"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Tier1_EditorBakedTrees_500m_1500m"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Tier2_EditorBakedUpperCanopyCells_1500m_4000m"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Tier3_EligibleCanopyGeometryComplement_LidarBounds"), Is.Not.Null);
        }

        [Test]
        public void Tier3Handoff_IsLinearR8WithoutMipsAndNoScalarFallback()
        {
            Texture2D handoff = AssetDatabase.LoadAssetAtPath<Texture2D>(HandoffPath);
            Assert.That(handoff, Is.Not.Null);
            Assert.That(handoff.width, Is.EqualTo(1024));
            Assert.That(handoff.height, Is.EqualTo(1024));
            TextureImporter importer = AssetImporter.GetAtPath(HandoffPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.False);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.R8));

            string include = File.ReadAllText(IncludePath);
            Assert.That(include, Does.Contain("sampler2D _AerialCanopyHandoffMask"));
            Assert.That(include, Does.Contain("sample.coverage = geometryToMapHandoff"));
            Assert.That(include, Does.Not.Contain("_AerialCanopyMapHandoffWeight"));
        }

        [Test]
        public void LegacyReplacementInventory_IsExactAndNotSceneIntegratedByAuthorer()
        {
            Assert.That(AerialVegetationPrefabAuthoring.LegacyRendererRootsToDisable, Has.Length.EqualTo(12));
            Assert.That(
                AerialVegetationPrefabAuthoring.LegacyRendererRootsToDisable.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(12));
            Assert.That(
                AerialVegetationPrefabAuthoring.LegacyRendererRootsToDisable.Count(path =>
                    path.Contains("KBDU_BatchedContext/vegetation_v2_hlod_1500m", StringComparison.Ordinal)),
                Is.EqualTo(4));
            Assert.That(
                AerialVegetationPrefabAuthoring.LegacyRendererRootsToDisable.Count(path =>
                    path.Contains("SparseVegetationProduction/CottonwoodCluster_", StringComparison.Ordinal)),
                Is.EqualTo(8));
        }
    }
}
