using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor.Tests
{
    public sealed class AirportCoreV43ArtifactEditModeTests
    {
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string RunwayShaderPath =
            "Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreContinuousRunway.shader";
        private const string TerrainShaderPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader";
        private const string WaterSourceManifestPath =
            "Assets/Production/Environment/AirportCoreV4/Water/Reports/AirportCoreWaterVegetation_SourceManifest.json";

        [Test]
        public void RunwayUsesOneAuthoritativeRendererMaterialAndContinuousLocalCoordinates()
        {
            GameObject environment = LoadEnvironment();
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Renderer pavement = contract.runwayPavement.GetComponent<Renderer>();
            Renderer[] renderers = contract.authoritativeRunwaySystem.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(2), "Only pavement and combined markings are allowed.");
            Assert.That(pavement.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(pavement.sharedMaterial),
                Is.EqualTo(AirportCoreV43ArtifactAuthoring.RunwayMaterialPath));
            Assert.That(contract.runwayCollisionSurface.sharedMesh, Is.SameAs(contract.runwayPavement.sharedMesh));
            Assert.That(contract.measuredRunwayToTerrainMaximumGapMeters, Is.LessThanOrEqualTo(0.025f));
            Assert.That(contract.measuredMarkingToRunwayMaximumGapMeters, Is.LessThanOrEqualTo(0.003f));
            Assert.That(contract.measuredCollisionSurfaceDisagreementMeters, Is.LessThanOrEqualTo(0.0001f));

            Material material = pavement.sharedMaterial;
            Vector4 axis = material.GetVector("_RunwayAxis");
            Vector4 size = material.GetVector("_RunwayLengthWidth");
            Assert.That(new Vector2(axis.x, axis.y).magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(size.x, Is.EqualTo(contract.runwayLengthMeters).Within(0.01f));
            Assert.That(size.y, Is.EqualTo(contract.runwayWidthMeters).Within(0.001f));

            string source = ReadAsset(RunwayShaderPath);
            Assert.That(source, Does.Contain("float longitudinal = dot(from08, axis)"));
            Assert.That(source, Does.Contain("float lateral = dot(from08, lateralAxis)"));
            Assert.That(source, Does.Not.Contain("LIGHTMAP_ON"), "Discontinuous lightmap charts caused V4.2 slabs.");
            Assert.That(source, Does.Not.Contain("_FeatureMask"));
            Assert.That(source, Does.Not.Contain("input.uv"));
            Assert.That(source, Does.Not.Contain("frac("));
            Assert.That(source, Does.Not.Contain("floor("));
        }

        [Test]
        public void ReservoirEffectsAreDeferredWithoutLosingTheAuditedSourceAsset()
        {
            GameObject environment = LoadEnvironment();
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Renderer renderer = contract.boulderReservoirSurface.GetComponent<Renderer>();
            AirportCoreBenchmarkRoot root = environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                AirportCoreV43ArtifactAuthoring.ReservoirMaterialPath);
            Assert.That(contract.waterPresentationMode,
                Is.EqualTo(ProductionWaterPresentationMode.OrthophotoOnly));
            Assert.That(renderer.enabled, Is.False);
            Assert.That(renderer.gameObject.activeSelf, Is.False);
            Assert.That(renderer.sharedMaterials, Is.Empty);
            Assert.That(root.v2ReservoirMaterial, Is.Null);
            Assert.That(root.manifest.waterEffectsDeferredToOrthophoto, Is.True);
            Assert.That(root.manifest.reservoirMaterial, Is.Null);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(ProductionEnvironmentRoot.ProductionReservoirShaderName));
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry + 5));
            Assert.That(material.passCount, Is.EqualTo(1));
            Assert.That(material.GetTexture("_DepthShoreRoughness"), Is.Not.Null);
            Assert.That(material.GetTexture("_TerrainMacroAlbedo"), Is.Not.Null);
            Assert.That(material.GetFloat("_OrthophotoInfluence"), Is.InRange(0.65f, 0.85f));
            Assert.That(environment.GetComponentsInChildren<Renderer>(true)
                .SelectMany(item => item.sharedMaterials)
                .Count(item => item == material), Is.Zero);
            Assert.That(renderer.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(renderer.GetComponentsInChildren<ReflectionProbe>(true)
                .Where(item => item.mode == ReflectionProbeMode.Realtime), Is.Empty);

            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "Assets/Production/Environment/AirportCoreV4/Water/Shaders/KBDUReservoirGroundSuppression.hlsl"),
                Is.Null);
            string terrainSource = ReadAsset(TerrainShaderPath);
            Assert.That(terrainSource, Does.Not.Contain("ReservoirSuppression"));
            Assert.That(terrainSource, Does.Not.Contain("ReservoirGroundSuppression"));
        }

        [Test]
        public void MinorWaterIsDeferredToOrthophotoOnlyAndHasNoRuntimeSamplingPath()
        {
            GameObject environment = LoadEnvironment();
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            AirportCoreBenchmarkRoot root = environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            Assert.That(contract.otherWaterFeaturesSurfaceV2, Is.Null);
            Assert.That(contract.otherWaterFeaturesShoreBankV2, Is.Null);
            Assert.That(environment.GetComponentsInChildren<Renderer>(true)
                .Where(item => item.name.IndexOf("OtherWaterFeatures", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.Empty);
            Assert.That(root.minorWaterTerrainEffect, Is.Not.Null);
            Material terrain = root.minorWaterTerrainEffect.sharedMaterial;
            Assert.That(contract.waterPresentationMode,
                Is.EqualTo(ProductionWaterPresentationMode.OrthophotoOnly));
            Assert.That(terrain.HasProperty("_AirportCoreMinorWaterMask"), Is.False);
            Assert.That(ReadAsset(TerrainShaderPath), Does.Not.Contain("KBDUMinorWaterTerrainEffect"));

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                AirportCoreV43ArtifactAuthoring.MinorWaterMaskPath);
            TextureImporter importer = AssetImporter.GetAtPath(
                AirportCoreV43ArtifactAuthoring.MinorWaterMaskPath) as TextureImporter;
            Assert.That(texture.width, Is.EqualTo(AirportCoreV43ArtifactAuthoring.MaskResolution));
            Assert.That(texture.height, Is.EqualTo(AirportCoreV43ArtifactAuthoring.MaskResolution));
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.streamingMipmaps, Is.True,
                "The preserved authoring mask is not an active runtime terrain dependency.");
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.maxTextureSize, Is.EqualTo(512));
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
            Assert.That(ReadAsset(WaterSourceManifestPath), Does.Contain("TERRAIN_MASK_NEAR_MID_RGBA"));
        }

        [Test]
        public void V43RecoversSerializedRendererTriangleAndMaterialCost()
        {
            GameObject environment = LoadEnvironment();
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            ProductionEnvironmentBudget budget = contract.CalculateBudget();
            Assert.That(budget.rendererCount, Is.LessThanOrEqualTo(115));
            Assert.That(budget.triangleCount, Is.LessThanOrEqualTo(377828));
            Assert.That(budget.materialCount, Is.LessThanOrEqualTo(11));
            Assert.That(contract.bakedRendererCount, Is.EqualTo(budget.rendererCount));
            Assert.That(contract.bakedTriangleCount, Is.EqualTo(budget.triangleCount));
            Assert.That(contract.bakedMaterialCount, Is.EqualTo(budget.materialCount));
        }

        private static GameObject LoadEnvironment()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            Assert.That(environment, Is.Not.Null);
            return environment;
        }

        private static string ReadAsset(string path) => File.ReadAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)));
    }
}
