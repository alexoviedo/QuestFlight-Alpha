using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Environment.Editor.Tests
{
    public sealed class FullMapWaterReflectionEditModeTests
    {
        private GameObject prefab;
        private ProductionEnvironmentRoot root;

        [SetUp]
        public void SetUp()
        {
            prefab = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            root = prefab.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(root, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            if (prefab != null)
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        [Test]
        public void ProductUsesOneFullMapTerrainMaskAndNoWaterRenderers()
        {
            Assert.That(root.waterPresentationMode,
                Is.EqualTo(ProductionWaterPresentationMode.OrthophotoMaskedReflection));
            Assert.That(root.fullMapWaterMask, Is.Not.Null);
            Assert.That(root.fullMapWaterReflectionManifest, Is.Not.Null);
            Assert.That(root.reflectedWaterFeatureCount,
                Is.EqualTo(FullMapWaterReflectionAuthoring.ReflectedFeatureCount));
            Assert.That(root.minimumReflectedWaterAreaSquareMeters,
                Is.EqualTo(FullMapWaterReflectionAuthoring.MinimumEffectiveAreaSquareMeters));

            Renderer reservoir =
                root.boulderReservoirSurface.GetComponent<Renderer>();
            Renderer bank = root.boulderReservoirShoreBank.GetComponent<Renderer>();
            Assert.That(reservoir.enabled, Is.False);
            Assert.That(reservoir.gameObject.activeSelf, Is.False);
            Assert.That(reservoir.sharedMaterials, Is.Empty);
            Assert.That(bank.enabled, Is.False);
            Assert.That(bank.gameObject.activeSelf, Is.False);
            Assert.That(bank.sharedMaterials, Is.Empty);
            Assert.That(root.otherWaterFeaturesSurfaceV2, Is.Null);
            Assert.That(root.otherWaterFeaturesShoreBankV2, Is.Null);
        }

        [Test]
        public void SharedOpaqueTerrainPassOwnsTheReflectionResponse()
        {
            Material terrain = AssetDatabase.LoadAssetAtPath<Material>(
                FullMapWaterReflectionAuthoring.TerrainMaterialPath);
            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.renderQueue, Is.LessThan(2500));
            Assert.That(terrain.passCount, Is.EqualTo(1));
            Assert.That(terrain.GetTexture("_FullMapWaterMask"),
                Is.SameAs(root.fullMapWaterMask));
            Vector4 bounds = terrain.GetVector("_FullMapWaterWorldMinInvSize");
            Assert.That(bounds.x, Is.EqualTo(-12000f).Within(0.001f));
            Assert.That(bounds.y, Is.EqualTo(-12000f).Within(0.001f));
            Assert.That(bounds.z, Is.EqualTo(1f / 24000f).Within(1e-8f));
            Assert.That(bounds.w, Is.EqualTo(1f / 24000f).Within(1e-8f));
            Assert.That(terrain.GetFloat("_FullMapWaterReflectionStrength"),
                Is.InRange(0.03f, 0.1f));
            Assert.That(terrain.GetFloat("_FullMapWaterDistortionStrength"),
                Is.InRange(0.005f, 0.05f));
            Assert.That(terrain.GetFloat("_FullMapWaterCrestGlintStrength"),
                Is.InRange(0.08f, 0.16f));

            string source = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader"));
            Assert.That(source, Does.Contain("UNITY_SAMPLE_TEXCUBE_LOD"));
            Assert.That(source, Does.Contain("color += neutralReflection"));
            Assert.That(source, Does.Contain("crestGlint"));
            Assert.That(source, Does.Not.Contain("color = lerp(color, skyReflection"));
            Assert.That(source, Does.Not.Contain("lerp(normal, waterNormal, waterCoverage)"));
            Assert.That(source, Does.Contain("_FullMapWaterMask"));
            Assert.That(source, Does.Not.Contain("_Time"));
            Assert.That(source, Does.Not.Contain("GrabPass"));
            Assert.That(source, Does.Not.Contain("Blend SrcAlpha"));
        }

        [Test]
        public void MaskImporterIsMipmappedLinearClampedAndAndroidAstc()
        {
            TextureImporter importer = AssetImporter.GetAtPath(
                FullMapWaterReflectionAuthoring.MaskPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_12x12));
            Assert.That(android.maxTextureSize, Is.EqualTo(2048));
        }

        [Test]
        public void WaterAddsNoRendererMaterialOrRuntimeComponent()
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Any(item =>
                item.name.IndexOf("FullMapWater", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.False);
            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            Assert.That(behaviours.Any(item =>
                item != null &&
                item.GetType().Name.IndexOf("WaterReflection", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.False);
        }
    }
}
