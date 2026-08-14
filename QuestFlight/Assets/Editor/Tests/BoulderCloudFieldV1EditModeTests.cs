using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class BoulderCloudFieldV1EditModeTests
    {
        private const string ProfilePath =
            "Assets/Production/Environment/CloudsV1/KBDU_BoulderCloudProfile.asset";
        private const string DensityPath =
            "Assets/Production/Environment/CloudsV1/KBDU_CloudDensitySlices_RGBA.png";
        private const string CoveragePath =
            "Assets/Production/Environment/CloudsV1/KBDU_CloudCoverage_RGBA.png";

        [Test]
        public void ProductPrefabContainsOneSerializedSparseVolumetricField()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(prefab, Is.Not.Null);
            ProductionEnvironmentRoot environment = prefab.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(environment, Is.Not.Null);
            Assert.That(environment.boulderCloudField, Is.Not.Null);
            BoulderCloudField[] fields = environment.boulderCloudField
                .GetComponentsInChildren<BoulderCloudField>(true);
            Assert.That(fields, Has.Length.EqualTo(1));
            BoulderCloudField field = fields[0];
            Assert.That(field.TryValidate(out string report), Is.True, report);
            Assert.That(field.AuthoredDrawCount, Is.EqualTo(1));
            Assert.That(field.AuthoredTriangleCount, Is.EqualTo(96));
            Assert.That(field.TransparentBlending, Is.True);
            Assert.That(field.AlphaToCoverage, Is.False);
            Assert.That(field.SparseVolumetricField, Is.True);
            Assert.That(field.SingleContinuousField, Is.True);
            Assert.That(field.TemporalReprojection, Is.False);
            Assert.That(field.RealtimeCloudShadows, Is.False);
            Assert.That(field.PerEyeBillboarding, Is.False);
            Assert.That(environment.boulderCloudField.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(environment.boulderCloudField.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
        }

        [Test]
        public void ProfileUsesRealMslDatumAndEightWeatherMapFormations()
        {
            BoulderCloudProfile profile =
                AssetDatabase.LoadAssetAtPath<BoulderCloudProfile>(ProfilePath);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.TryValidate(out string report), Is.True, report);
            Assert.That(profile.ProfileId, Is.EqualTo("KBDU_20260814_1055Z_SCT018_V4"));
            Assert.That(profile.AirportElevationMslMeters, Is.EqualTo(1612.6f).Within(0.1f));
            Assert.That(profile.DeclaredBaseAglMeters, Is.EqualTo(550f).Within(0.1f));
            Assert.That(profile.DeclaredTopAglMeters, Is.EqualTo(1450f).Within(0.1f));
            Assert.That(profile.Volumes, Has.Length.EqualTo(8));
            Assert.That(profile.Volumes.Any(volume =>
                volume.baseMslMeters < 2600f && volume.topMslMeters > 2600f), Is.True);
        }

        [Test]
        public void CloudMaterialRayMarchesOneBatchedPremultipliedVolumeSet()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            BoulderCloudField field = prefab.GetComponent<ProductionEnvironmentRoot>()
                .boulderCloudField.GetComponent<BoulderCloudField>();
            Material material = field.SharedCloudMaterial;
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name,
                Is.EqualTo("QuestFlight/Boulder Sparse Volumetric Cloud Field"));
            Assert.That(material.renderQueue, Is.InRange(2501, 2999));
            Assert.That(material.GetTexture("_DensitySlices"), Is.Not.Null);
            Assert.That(field.SectorRenderers.All(renderer =>
                renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off &&
                !renderer.receiveShadows), Is.True);

            string shaderPath = AssetDatabase.GetAssetPath(material.shader);
            string shader = File.ReadAllText(Path.GetFullPath(shaderPath));
            StringAssert.Contains("Blend One OneMinusSrcAlpha", shader);
            StringAssert.Contains("ZWrite Off", shader);
            StringAssert.Contains("IntersectUnitBox", shader);
            StringAssert.Contains("MaximumMarchSteps", shader);
            StringAssert.Contains("volumeCenter", shader);
            StringAssert.Contains("volumeRadii", shader);
            StringAssert.Contains("Beer-Lambert Extinction", shader);
            StringAssert.DoesNotContain("AlphaToMask On", shader);
            StringAssert.DoesNotContain("GrabPass", shader);
        }

        [TestCase(DensityPath, 512, false)]
        [TestCase(CoveragePath, 256, true)]
        public void CloudDataTexturesUseBoundedAstcImports(
            string path,
            int maximumSize,
            bool expectedMipmaps)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.EqualTo(expectedMipmaps));
            Assert.That(importer.streamingMipmaps, Is.EqualTo(expectedMipmaps));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            Assert.That(android.maxTextureSize, Is.LessThanOrEqualTo(maximumSize));
        }

        [Test]
        public void OneBatchedProxyMeshBoundsCoverEveryAuthoredFormation()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            BoulderCloudField field = prefab.GetComponent<ProductionEnvironmentRoot>()
                .boulderCloudField.GetComponent<BoulderCloudField>();
            MeshRenderer renderer = field.SectorRenderers.Single();
            Assert.That(renderer.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(renderer.transform.localScale, Is.EqualTo(Vector3.one));
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(64));
            Assert.That(filter.sharedMesh.triangles.Length / 3, Is.EqualTo(96));
            Bounds localFieldBounds = filter.sharedMesh.bounds;
            foreach (BoulderCloudProfile.Volume volume in field.Profile.Volumes)
            {
                Assert.That(localFieldBounds.Contains(
                    volume.centerLocalMeters - volume.radiiMeters), Is.True, volume.id);
                Assert.That(localFieldBounds.Contains(
                    volume.centerLocalMeters + volume.radiiMeters), Is.True, volume.id);
            }
        }

        [Test]
        public void InteriorContainmentRemainsSmoothAndZeroOutside()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            BoulderCloudField field = prefab.GetComponent<ProductionEnvironmentRoot>()
                .boulderCloudField.GetComponent<BoulderCloudField>();
            BoulderCloudProfile.Volume volume = field.Profile.Volumes[0];
            Vector3 center = field.transform.TransformPoint(volume.centerLocalMeters);
            Assert.That(field.EvaluateInteriorWeight(center), Is.GreaterThan(0.5f));
            // A point outside one formation may legitimately lie inside a
            // neighbouring connected formation. Test the union boundary,
            // not the first volume in isolation.
            float outsideX = field.Profile.Volumes.Max(candidate =>
                candidate.centerLocalMeters.x + candidate.radiiMeters.x) + 1000f;
            Vector3 outside = field.transform.TransformPoint(
                new Vector3(outsideX, volume.centerLocalMeters.y, 0f));
            Assert.That(field.EvaluateInteriorWeight(outside), Is.EqualTo(0f).Within(0.001f));
        }
    }
}
