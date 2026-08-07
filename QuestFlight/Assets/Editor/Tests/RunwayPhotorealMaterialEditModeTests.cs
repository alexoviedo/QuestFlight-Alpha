using System.IO;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class RunwayPhotorealMaterialEditModeTests
    {
        private const string Folder =
            "Assets/Production/Environment/AirportCoreV4/Runway/";

        [Test]
        public void RunwayUsesPhotorealDetailAndUniqueMacroWithinQuestBudget()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            ProductionEnvironmentRoot contract =
                environment.GetComponent<ProductionEnvironmentRoot>();
            Renderer pavement = contract.runwayPavement.GetComponent<Renderer>();
            Material material = pavement.sharedMaterial;

            Assert.That(pavement.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(material.GetFloat("_WorldTiling"), Is.EqualTo(0.18f));
            Assert.That(material.GetFloat("_SecondaryTiling"), Is.EqualTo(0.047f));

            AssertTexture("AirportCoreRunway_Albedo.png", 1024, 1024, 1024,
                TextureWrapMode.Repeat, false);
            AssertTexture("AirportCoreRunway_Normal.png", 1024, 1024, 1024,
                TextureWrapMode.Repeat, true);
            AssertTexture("AirportCoreRunway_MRAO.png", 2048, 256, 1024,
                TextureWrapMode.Clamp, false);

            string shader = File.ReadAllText(Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", Folder + "AirportCoreContinuousRunway.shader")));
            Assert.That(shader, Does.Contain("float2 macroUv"));
            Assert.That(shader, Does.Contain("runwaySurface.r"));
            Assert.That(shader, Does.Contain("runwaySurface.g"));
            Assert.That(shader, Does.Contain("runwaySurface.b"));
            Assert.That(shader, Does.Not.Contain("patchField"));
            Assert.That(shader, Does.Not.Contain("sparseJoint"));
            Assert.That(shader, Does.Not.Contain("_FeatureMask"));
        }

        private static void AssertTexture(
            string name,
            int expectedWidth,
            int expectedHeight,
            int androidMaximum,
            TextureWrapMode wrap,
            bool normal)
        {
            string path = Folder + name;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(texture, Is.Not.Null);
            Assert.That(importer, Is.Not.Null);
            importer.GetSourceTextureWidthAndHeight(
                out int sourceWidth, out int sourceHeight);
            Assert.That(sourceWidth, Is.EqualTo(expectedWidth));
            Assert.That(sourceHeight, Is.EqualTo(expectedHeight));
            float activeScale = Mathf.Min(1f, androidMaximum / (float)expectedWidth);
            Assert.That(texture.width,
                Is.EqualTo(Mathf.RoundToInt(expectedWidth * activeScale)));
            Assert.That(texture.height,
                Is.EqualTo(Mathf.RoundToInt(expectedHeight * activeScale)));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.streamingMipmaps, Is.True);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(importer.anisoLevel, Is.EqualTo(8));
            Assert.That(importer.wrapMode, Is.EqualTo(wrap));
            Assert.That(importer.textureType,
                normal ? Is.EqualTo(TextureImporterType.NormalMap) : Is.EqualTo(TextureImporterType.Default));
            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.maxTextureSize, Is.EqualTo(androidMaximum));
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
        }
    }
}
