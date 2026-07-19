using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor.Tests
{
    public sealed class ReservoirWaterContractEditModeTests
    {
        private const string MeshPath =
            "Assets/Production/Environment/Generated/BoulderReservoir_OSM_AuthoritativeWater_Mesh.asset";
        private const string V4ShaderPath =
            "Assets/Production/Environment/AirportCoreV4/Water/Shaders/KBDUReservoirWaterV4.shader";

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

        [Test]
        public void OrthophotoOnlyProductHasNoReservoirMaterialDependencyAndSourceRemainsAudited()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(environment, Is.Not.Null);
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            AirportCoreBenchmarkRoot root = environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
            AirportCoreBenchmarkManifest manifest = AssetDatabase.LoadAssetAtPath<AirportCoreBenchmarkManifest>(
                AirportCoreBenchmarkManifest.AssetPath);
            Assert.That(contract, Is.Not.Null);
            Assert.That(root, Is.Not.Null);
            Assert.That(manifest, Is.Not.Null);

            Renderer renderer = contract.boulderReservoirSurface.GetComponent<Renderer>();
            Mesh mesh = contract.boulderReservoirSurface.sharedMesh;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                AirportCoreV43ArtifactAuthoring.ReservoirMaterialPath);
            Assert.That(renderer, Is.SameAs(root.boulderReservoir));
            Assert.That(contract.waterPresentationMode,
                Is.EqualTo(ProductionWaterPresentationMode.OrthophotoOnly));
            Assert.That(renderer.enabled, Is.False);
            Assert.That(renderer.gameObject.activeSelf, Is.False);
            Assert.That(renderer.sharedMaterials, Is.Empty);
            Assert.That(root.v2ReservoirMaterial, Is.Null);
            Assert.That(manifest.waterEffectsDeferredToOrthophoto, Is.True);
            Assert.That(manifest.reservoirMaterial, Is.Null);
            Assert.That(material, Is.Not.Null,
                "The unbound V4 water source remains project-owned, licensed, and available for final polish.");
            Assert.That(AssetDatabase.GetAssetPath(mesh), Is.EqualTo(MeshPath));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(material.shader.name, Is.EqualTo(ProductionEnvironmentRoot.ProductionReservoirShaderName));
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry + 5));
            Assert.That(material.passCount, Is.EqualTo(1));
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.GetTexture("_WaveNormalA"), Is.Not.Null);
            Assert.That(material.GetTexture("_WaveNormalB"), Is.Not.Null);
            Assert.That(material.GetTexture("_DepthShoreRoughness"), Is.Not.Null);
            Assert.That(material.GetTexture("_TerrainMacroAlbedo"), Is.Not.Null);
            Assert.That(material.GetTexture("_EnvironmentCube"), Is.Not.Null);
            Assert.That(environment.GetComponentsInChildren<Renderer>(true)
                .SelectMany(item => item.sharedMaterials)
                .Count(item => item == material), Is.Zero);
            Assert.That(renderer.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(renderer.GetComponentsInChildren<ReflectionProbe>(true)
                .Where(probe => probe.mode == ReflectionProbeMode.Realtime), Is.Empty);

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(V4ShaderPath);
            Assert.That(shader, Is.SameAs(material.shader));
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(shader)
                .Where(message => string.Equals(message.severity.ToString(), "Error", StringComparison.OrdinalIgnoreCase)),
                Is.Empty);
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", V4ShaderPath));
            string source = File.ReadAllText(absolutePath);
            Assert.That(source, Does.Contain("\"RenderType\"=\"Opaque\""));
            Assert.That(source, Does.Contain("\"Queue\"=\"Geometry+5\""));
            Assert.That(source, Does.Contain("ZWrite On"));
            Assert.That(source, Does.Contain("Fallback Off"));
            Assert.That(source, Does.Contain("texCUBElod(_EnvironmentCube"));
            Assert.That(source, Does.Contain("UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO"));
            Assert.That(source, Does.Contain("UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX"));
            Assert.That(Regex.Matches(source, @"(?m)^\s*Pass\s*$").Count, Is.EqualTo(1));
            foreach (string forbidden in new[]
                     {
                         @"(?m)^\s*Blend\b", @"\bGrabPass\b", @"\bUsePass\b", @"ZWrite\s+Off",
                         @"_CameraOpaqueTexture", @"_CameraDepthTexture", @"unity_SpecCube0",
                         @"\bSSR\b", @"refraction", @"tessell"
                     })
                Assert.That(Regex.IsMatch(source, forbidden, RegexOptions.IgnoreCase), Is.False, forbidden);
        }
    }
}
