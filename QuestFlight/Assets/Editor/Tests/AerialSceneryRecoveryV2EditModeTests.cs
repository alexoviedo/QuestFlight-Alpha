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
            "Assets/Production/Environment/AerialSurfaceV1/Ground/";
        private const string BuildingPrefabPath =
            "Assets/Production/Environment/AerialSceneryV1/Buildings/KBDU_AerialBuildingsV1.prefab";
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
        public void ProductUsesGroundedSemanticGrovesAndContinuousFarCanopy()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            Assert.That(environment, Is.Not.Null);
            PhotorealAerialSurfaceRoot surface =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            Assert.That(surface, Is.Not.Null);
            Assert.That(surface.canopySurfaceRenderers, Has.Length.EqualTo(4));
            Assert.That(surface.canopySurfaceRenderers.All(renderer =>
                renderer != null && renderer.enabled &&
                renderer.GetComponent<MeshFilter>()?.sharedMesh != null), Is.True);
            Assert.That(surface.canopySurfaceRenderers.Sum(renderer =>
                renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3),
                Is.InRange(30000, 59000));
            Assert.That(surface.canopySurfaceRenderers.Select(renderer => renderer.sharedMaterial)
                .Distinct().Count(), Is.EqualTo(1));
            Assert.That(surface.productionCanopySurfaceLayer
                .GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);
            Assert.That(surface.productionCanopySurfaceLayer
                .GetComponentsInChildren<AerialVegetationBatchRenderer>(true), Is.Empty,
                "The product canopy must be serialized semantic geometry, not callback-only draws.");
            AirportCloseTreeLodRenderer[] closeTrees = surface.productionCanopySurfaceLayer
                .GetComponentsInChildren<AirportCloseTreeLodRenderer>(true);
            Assert.That(closeTrees, Has.Length.EqualTo(1));
            Assert.That(closeTrees[0].Instances, Has.Length.EqualTo(696));
            Assert.That(closeTrees[0].CenterEyeWorldUprightImpostor, Is.True);
            Assert.That(closeTrees[0].PerEyeFacingBehavior, Is.False);
            Assert.That(closeTrees[0].ProjectedScreenSizeSelection, Is.True);
            Assert.That(closeTrees[0].PersistentHysteresis, Is.True);
            Assert.That(closeTrees[0].AlphaToCoverageTransition, Is.True);
            Assert.That(surface.canopySurfaceManifest.text,
                Does.Contain("source_derived_close_tree_lods_then_semantic_groves_over_continuous_terrain_canopy"));
            Assert.That(surface.canopySurfaceManifest.text,
                Does.Contain("\"source_coverage_fraction_0_1500m\": 0.960"),
                "Committed grove geometry must cover at least 95% of the high-confidence source footprint.");
        }

        [Test]
        public void GroundPyramidUsesOfficialBoundedTexturesWithoutAutomaticStreaming()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            Assert.That(material, Is.Not.Null);
            AssertTexture(material, "_NearAlbedo",
                GroundRoot + "KBDU_City2020_Near4km_8192.jpg",
                8192, TextureImporterFormat.ASTC_8x8);
            AssertTexture(material, "_RecoveryMidNWAlbedo", GroundRoot + "KBDU_City2020_MidNW_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_RecoveryMidNEAlbedo", GroundRoot + "KBDU_City2020_MidNE_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_RecoveryMidSWAlbedo", GroundRoot + "KBDU_City2020_MidSW_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_RecoveryMidSEAlbedo", GroundRoot + "KBDU_City2020_MidSE_2048.jpg",
                2048, TextureImporterFormat.ASTC_6x6);
            AssertTexture(material, "_MidAlbedo", GroundRoot + "KBDU_City2020_MidBase12km_8192.jpg",
                8192, TextureImporterFormat.ASTC_10x10);
            AssertTexture(material, "_FarAlbedo",
                GroundRoot + "KBDU_City2020_Far24km_4096.jpg",
                4096, TextureImporterFormat.ASTC_10x10);

            Assert.That(material.GetVector("_NearBlendStartEnd"),
                Is.EqualTo(new Vector4(1800f, 1980f, 0f, 0f)));
            Assert.That(material.GetVector("_RecoveryBlendStartEnd"),
                Is.EqualTo(new Vector4(3800f, 4000f, 0f, 0f)));
            Assert.That(material.GetVector("_MidBlendStartEnd"),
                Is.EqualTo(new Vector4(5000f, 6000f, 0f, 0f)));

            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(AssetDatabase.GetAssetPath(contract.uniqueMacroAlbedo),
                Is.EqualTo(GroundRoot + "KBDU_City2020_MidBase12km_8192.jpg"));

            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GroundRoot + "KBDU_AerialSurfaceV1_SourceManifest.json");
            Assert.That(source, Is.Not.Null);
            Assert.That(source.text, Does.Contain("22b7c93a74394696824258f807f345ba"));
            Assert.That(source.text, Does.Contain("CC0-1.0"));
            Assert.That(source.text, Does.Contain("2020"));
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
            Assert.That(importer.streamingMipmaps, Is.True,
                property + " must be managed through explicit requested-mip ownership.");
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
