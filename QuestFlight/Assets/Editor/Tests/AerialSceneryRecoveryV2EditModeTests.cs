using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class AerialSceneryRecoveryV2EditModeTests
    {
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        private const string TerrainShaderPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader";
        private const string GroundRoot =
            "Assets/Production/Environment/AerialSceneryRecoveryV2/Ground/";
        private const string BuildingPrefabPath =
            "Assets/Production/Environment/AerialSceneryV1/Buildings/KBDU_AerialBuildingsV1.prefab";
        private const string VegetationPrefabPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Generated/AerialVegetationRoot.prefab";
        private const string SeatProfilePath =
            "Assets/Production/Profiles/ImportedC172PilotSeatProfile.asset";
        private const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        [Test]
        public void ProductUsesClosedFixedContextBuildingShells()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(16));
            Assert.That(prefab.GetComponentsInChildren<LODGroup>(true), Is.Empty,
                "Context buildings must not swap to elevated roof cards or scale-changing LODs.");
            Assert.That(renderers.All(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh != null), Is.True);
            Assert.That(renderers.Select(renderer => renderer.sharedMaterial).Distinct().Count(), Is.EqualTo(1));
            Assert.That(renderers[0].sharedMaterial.shader.name,
                Is.EqualTo("QuestFlight/KBDU Aerial Building Shell"));
            Assert.That(prefab.GetComponentsInChildren<Transform>(true).Any(item =>
                item.name.IndexOf("roof_only", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.name.IndexOf("roof-card", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
        }

        [Test]
        public void ProductUsesBoundedTwoDrawNearGrovesAndMapOnlyTier2()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VegetationPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            AerialVegetationBatchRenderer batch =
                prefab.GetComponentsInChildren<AerialVegetationBatchRenderer>(true).Single();
            Assert.That(batch.NonEmptyDrawCount, Is.EqualTo(2));
            Assert.That(batch.FullyInstancedTriangleCount, Is.EqualTo(12860));
            Assert.That(batch.SharedMaterial, Is.Not.Null);
            Assert.That(batch.FixedOrientationOnly, Is.True);
            Assert.That(batch.CameraFacingBehavior, Is.False);
            Assert.That(batch.LodCrossfadeBehavior, Is.False);
            Assert.That(prefab.GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Transform>(true).Any(item =>
                item.name == "Tier2_OrthophotoLidarCanopyTerrainShading_500m_4000m"), Is.True);
            Assert.That(batch.Batches.Any(item => item.Tier == 2), Is.False);
        }

        [Test]
        public void GroundPyramidUsesOfficialBoundedTexturesWithoutAutomaticStreaming()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            Assert.That(material, Is.Not.Null);
            AssertTexture(material, "_NearAlbedo",
                "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Near4km_4096.jpg",
                4096, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_RecoveryMidNWAlbedo", GroundRoot + "KBDU_NAIP_2019_MidNW_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_RecoveryMidNEAlbedo", GroundRoot + "KBDU_NAIP_2019_MidNE_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_RecoveryMidSWAlbedo", GroundRoot + "KBDU_NAIP_2019_MidSW_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_RecoveryMidSEAlbedo", GroundRoot + "KBDU_NAIP_2019_MidSE_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_MidAlbedo", GroundRoot + "KBDU_NAIP_2019_MidBase12km_4096.jpg",
                4096, TextureImporterFormat.ASTC_10x10);
            AssertTexture(material, "_FarAlbedo",
                "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Far24km_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);

            Assert.That(material.GetVector("_NearBlendStartEnd"),
                Is.EqualTo(new Vector4(1800f, 1980f, 0f, 0f)));
            Assert.That(material.GetVector("_RecoveryBlendStartEnd"),
                Is.EqualTo(new Vector4(3800f, 4000f, 0f, 0f)));
            Assert.That(material.GetVector("_MidBlendStartEnd"),
                Is.EqualTo(new Vector4(5000f, 6000f, 0f, 0f)));

            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(AssetDatabase.GetAssetPath(contract.uniqueMacroAlbedo),
                Is.EqualTo(GroundRoot + "KBDU_NAIP_2019_MidBase12km_4096.jpg"));

            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GroundRoot + "KBDU_GroundImageryRecoveryV2_SourceManifest.json");
            Assert.That(source, Is.Not.Null);
            Assert.That(source.text, Does.Contain("USGSNAIPPlus"));
            Assert.That(source.text, Does.Contain("public domain"));
            Assert.That(source.text, Does.Contain("2019"));
        }

        [Test]
        public void TerrainShaderCompilesAndWaterIsOrthophotoOnly()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(TerrainShaderPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.GetShaderMessages(shader)
                .Where(message => string.Equals(message.severity.ToString(), "Error",
                    StringComparison.OrdinalIgnoreCase)), Is.Empty);
            string source = Read(TerrainShaderPath);
            Assert.That(source, Does.Contain("SampleRecovery"));
            Assert.That(source, Does.Not.Contain("KBDUMinorWaterTerrainEffect"));
            Assert.That(source, Does.Not.Contain("_AirportCoreMinorWaterMask"));

            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.waterPresentationMode,
                Is.EqualTo(ProductionWaterPresentationMode.OrthophotoOnly));
            Renderer reservoir = contract.boulderReservoirSurface.GetComponent<Renderer>();
            Renderer shore = contract.boulderReservoirShoreBank.GetComponent<Renderer>();
            Assert.That(reservoir.enabled, Is.False);
            Assert.That(reservoir.gameObject.activeInHierarchy, Is.False);
            Assert.That(reservoir.sharedMaterials, Is.Empty);
            Assert.That(shore.enabled, Is.False);
            Assert.That(shore.gameObject.activeInHierarchy, Is.False);
            Assert.That(shore.sharedMaterials, Is.Empty);
            AirportCoreBenchmarkRoot benchmark = environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            Assert.That(benchmark.v2ReservoirMaterial, Is.Null);
            Assert.That(benchmark.manifest.waterEffectsDeferredToOrthophoto, Is.True);
            Assert.That(benchmark.manifest.reservoirMaterial, Is.Null);
            Assert.That(benchmark.manifest.minorWaterUsesTerrainMask, Is.False);
            Assert.That(benchmark.minorWaterTerrainEffect.sharedMaterial
                .HasProperty("_AirportCoreMinorWaterMask"), Is.False);
        }

        [Test]
        public void AuthoredNeutralEyeIsFiveCentimetersFartherAft()
        {
            PilotSeatProfile profile = AssetDatabase.LoadAssetAtPath<PilotSeatProfile>(SeatProfilePath);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.nominalEyeLocalPosition.z, Is.EqualTo(-0.23f).Within(0.0001f));
            Assert.That(profile.targetEyeToPanelDistanceMeters, Is.EqualTo(0.547f).Within(0.0001f));
            Assert.That(profile.EyeToPanelForwardDistanceMeters(),
                Is.EqualTo(profile.targetEyeToPanelDistanceMeters).Within(0.0001f));

            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPrefabPath);
            Transform anchor = aircraft.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "PilotSeatAnchor");
            Assert.That(anchor.localPosition, Is.EqualTo(profile.nominalEyeLocalPosition));
        }

        private static void AssertTexture(
            Material material,
            string property,
            string expectedPath,
            int expectedSize,
            TextureImporterFormat expectedAndroidFormat)
        {
            Texture texture = material.GetTexture(property);
            Assert.That(texture, Is.Not.Null, property);
            Assert.That(AssetDatabase.GetAssetPath(texture), Is.EqualTo(expectedPath), property);
            Assert.That(texture.width, Is.EqualTo(expectedSize), property);
            Assert.That(texture.height, Is.EqualTo(expectedSize), property);
            TextureImporter importer = AssetImporter.GetAtPath(expectedPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, property);
            Assert.That(importer.mipmapEnabled, Is.True, property);
            Assert.That(importer.streamingMipmaps, Is.False,
                property + " uses custom world-space UVs, so automatic streaming residency is invalid.");
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), property);
            Assert.That(importer.anisoLevel, Is.GreaterThanOrEqualTo(8), property);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), property);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True, property);
            Assert.That(android.format, Is.EqualTo(expectedAndroidFormat), property);
        }

        private static string Read(string path) => File.ReadAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)));
    }
}
