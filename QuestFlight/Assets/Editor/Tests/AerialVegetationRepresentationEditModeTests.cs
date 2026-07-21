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
        private const string ModelPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Models/AerialVegetationMultiscale.fbx";
        private const string IncludePath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Shaders/AerialCanopyMap.cginc";
        private const string FrozenLidarManifestSha256 =
            "1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5";

        [Test]
        public void GeneratedPrefab_UsesTwoNearDrawsAndOneFixedTier2GroveDraw()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            AerialVegetationBatchRenderer renderer = prefab.GetComponent<AerialVegetationBatchRenderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.AuthoredSourcePlacementCount, Is.EqualTo(64));
            Assert.That(renderer.NonEmptyDrawCount, Is.EqualTo(3));
            Assert.That(renderer.AuthoredDrawCount, Is.EqualTo(3));
            Assert.That(renderer.FullyInstancedTriangleCount, Is.EqualTo(35540));
            Assert.That(renderer.AuthoredFullyInstancedTriangleCount, Is.EqualTo(35540));
            Assert.That(renderer.FullyInstancedTriangleCount, Is.LessThanOrEqualTo(36860));
            Assert.That(renderer.AuthoredMaterialCount, Is.EqualTo(1));
            Assert.That(renderer.FixedOrientationOnly, Is.True);
            Assert.That(renderer.CameraFacingBehavior, Is.False);
            Assert.That(renderer.LodCrossfadeBehavior, Is.False);
            Assert.That(renderer.SharedMaterial, Is.Not.Null);
            Assert.That(renderer.SharedMaterial.shader.name,
                Is.EqualTo("QuestFlight/Aerial Scenery V1/Vegetation Atlas Cutout"));
            Assert.That(renderer.Batches, Has.Length.EqualTo(3));
            Assert.That(renderer.Batches.Select(batch => batch.Matrices.Length),
                Is.EqualTo(new[] { 1, 1, 42 }));
            Assert.That(renderer.Batches.Select(batch => batch.InstanceTints.Length),
                Is.EqualTo(new[] { 1, 1, 42 }));
            Assert.That(renderer.Batches.Select(batch => batch.Mesh), Is.All.Not.Null);
            Assert.That(renderer.DistributionManifest.text, Does.Contain(FrozenLidarManifestSha256));
            Assert.That(renderer.DistributionManifest.text, Does.Contain("\"minimum_radius_m\": 500.0"));
            Assert.That(renderer.DistributionManifest.text,
                Does.Contain("\"tier2_geometry_draws\": 1"));
            Assert.That(renderer.DistributionManifest.text,
                Does.Contain("fixed_world_oriented_grove_clusters_over_orthophoto_lidar_canopy_shading"));
            Mesh tier2Mesh = renderer.Batches.Single(batch => batch.Tier == 2).Mesh;
            Assert.That(tier2Mesh.name, Does.StartWith("AerialGroveCluster_T2"));
            ModelImporter modelImporter = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(modelImporter, Is.Not.Null);
            Assert.That(modelImporter.bakeAxisConversion, Is.False,
                "Axis conversion is explicit in the deterministic batch matrices, not hidden in importer state.");
            Assert.That(tier2Mesh.bounds.size.y, Is.GreaterThan(tier2Mesh.bounds.size.z),
                "The raw Blender mesh is expected to retain a Z-up basis before explicit batch conversion.");
            Matrix4x4 tier2Matrix = renderer.Batches.Single(batch => batch.Tier == 2).Matrices[0];
            Assert.That(Vector3.Dot(tier2Matrix.MultiplyVector(Vector3.forward).normalized, Vector3.up),
                Is.GreaterThan(0.999f),
                "The raw mesh Z-height axis must map to Unity world up in every Tier-2 draw matrix.");
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(
                "Assets/Production/Environment/AerialSceneryV1/Vegetation/Generated/AerialVegetation_Tier2_Combined.asset"),
                Is.Null);
            Assert.That(prefab.transform.childCount, Is.EqualTo(4));
            Assert.That(prefab.transform.Find("Tier0_EditorBakedTrees_0m_500m"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Tier1_EditorBakedTrees_500m_1500m"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Tier2_FixedWorldOrientedGroves_500m_4000m"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Tier3_OrthophotoLidarCanopyTerrainShading_Far"), Is.Not.Null);
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
