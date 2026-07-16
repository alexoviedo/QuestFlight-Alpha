using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor.Tests
{
    public sealed class ReservoirWaterContractEditModeTests
    {
        private const string MeshPath =
            "Assets/Production/Environment/Generated/BoulderReservoir_OSM_AuthoritativeWater_Mesh.asset";

        [Test]
        public void AuthoritativeSurfaceRemainsOneImmutablePlaneAndSubmesh()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.vertexCount, Is.EqualTo(254));
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(252));
            Assert.That(mesh.bounds.center.x, Is.EqualTo(249.5625f).Within(0.001f));
            Assert.That(mesh.bounds.center.z, Is.EqualTo(4307.6875f).Within(0.001f));
            Assert.That(mesh.bounds.extents.x, Is.EqualTo(1062.5625f).Within(0.001f));
            Assert.That(mesh.bounds.extents.z, Is.EqualTo(857.1875f).Within(0.001f));
            Assert.That(mesh.bounds.extents.y, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(mesh.normals, Has.All.Matches<Vector3>(normal => normal.y > 0.999f));
        }

        [TestCase(ReservoirWaterMaterialAuthoring.BroadNormalPath, TextureImporterType.NormalMap, TextureWrapMode.Repeat)]
        [TestCase(ReservoirWaterMaterialAuthoring.RippleNormalPath, TextureImporterType.NormalMap, TextureWrapMode.Repeat)]
        [TestCase(ReservoirWaterMaterialAuthoring.PackedMaskPath, TextureImporterType.Default, TextureWrapMode.Clamp)]
        public void GeneratedTexturesAreMipmappedLinearAstcAndQuestBounded(
            string path,
            TextureImporterType expectedType,
            TextureWrapMode expectedWrap)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(512));
            Assert.That(texture.height, Is.EqualTo(512));
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(expectedType));
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.streamingMipmaps, Is.False);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(importer.wrapMode, Is.EqualTo(expectedWrap));
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.maxTextureSize, Is.EqualTo(512));
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
        }

        [Test]
        public void ConfiguratorRequiresAndInjectsTheSelectedSkyCubemap()
        {
            Shader shader = Shader.Find(ReservoirWaterMaterialAuthoring.ShaderName);
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            Cubemap selectedSky = new Cubemap(4, TextureFormat.RGBA32, mipChain: true);
            try
            {
                Assert.Throws<ArgumentNullException>(() =>
                    ReservoirWaterMaterialAuthoring.ConfigureMaterialState(material, null, 0f, 0.9f));
                ReservoirWaterMaterialAuthoring.ConfigureMaterialState(material, selectedSky, 35f, 0.9f);
                Assert.That(material.GetTexture("_EnvironmentCube"), Is.SameAs(selectedSky));
                Assert.That(material.GetTexture("_BroadNormal"), Is.Not.Null);
                Assert.That(material.GetTexture("_RippleNormal"), Is.Not.Null);
                Assert.That(material.GetTexture("_DepthShoreRoughness"), Is.Not.Null);
                Assert.That(material.GetFloat("_ZWrite"), Is.GreaterThan(0.5f));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry + 5));
                Assert.That(material.passCount, Is.EqualTo(1));
                Vector4 rotation = material.GetVector("_EnvironmentRotationSinCos");
                Assert.That(rotation.x, Is.EqualTo(Mathf.Sin(35f * Mathf.Deg2Rad)).Within(0.0001f));
                Assert.That(rotation.y, Is.EqualTo(Mathf.Cos(35f * Mathf.Deg2Rad)).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(selectedSky);
            }
        }

        [Test]
        public void ProductionMaterialHasOneOpaquePassAndAllOfflineMaps()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ReservoirWaterMaterialAuthoring.MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(ReservoirWaterMaterialAuthoring.ShaderName));
            Assert.That(material.passCount, Is.EqualTo(1));
            Assert.That(material.renderQueue, Is.LessThan((int)RenderQueue.Transparent));
            Assert.That(material.GetFloat("_ZWrite"), Is.GreaterThan(0.5f));
            Assert.That(material.GetTexture("_BroadNormal"), Is.Not.Null);
            Assert.That(material.GetTexture("_RippleNormal"), Is.Not.Null);
            Assert.That(material.GetTexture("_DepthShoreRoughness"), Is.Not.Null);
        }

        [Test]
        public void ShaderSourceContainsNoScreenCameraTransparencyOrRealtimeProbePath()
        {
            Shader shader = Shader.Find(ReservoirWaterMaterialAuthoring.ShaderName);
            string assetPath = AssetDatabase.GetAssetPath(shader);
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            string source = File.ReadAllText(absolutePath);
            Assert.That(source, Does.Contain("ZWrite On"));
            Assert.That(source, Does.Contain("SchlickFresnel"));
            Assert.That(source, Does.Contain("texCUBElod(_EnvironmentCube"));
            Assert.That(source, Does.Not.Contain("GrabPass"));
            Assert.That(source, Does.Not.Contain("Blend SrcAlpha"));
            Assert.That(source, Does.Not.Contain("unity_SpecCube0"));
            Assert.That(source, Does.Not.Contain("_CameraOpaqueTexture"));
            Assert.That(source, Does.Not.Contain("_CameraDepthTexture"));
        }
    }
}
