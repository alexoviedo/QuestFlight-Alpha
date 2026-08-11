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
        private const string TexturePath =
            "Assets/Production/Environment/CloudsV1/KBDU_CloudImpostorAtlasV3.png";

        [Test]
        public void ProductPrefabContainsOneBoundedAuthoredCloudField()
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
            Assert.That(fields[0].TryValidate(out string report), Is.True, report);
            Assert.That(fields[0].AuthoredDrawCount, Is.EqualTo(1));
            Assert.That(fields[0].AuthoredTriangleCount, Is.InRange(1, 12000));
            Assert.That(fields[0].TransparentBlending, Is.False);
            Assert.That(fields[0].RealtimeCloudShadows, Is.False);
            Assert.That(fields[0].PerEyeBillboarding, Is.False);
            Assert.That(fields[0].AlphaToCoverage, Is.True);
            Assert.That(environment.boulderCloudField.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(environment.boulderCloudField.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
        }

        [Test]
        public void ProfileUsesRealMslDatumAndContainsReachableSpatialVolumes()
        {
            BoulderCloudProfile profile =
                AssetDatabase.LoadAssetAtPath<BoulderCloudProfile>(ProfilePath);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.TryValidate(out string report), Is.True, report);
            Assert.That(profile.AirportElevationMslMeters, Is.EqualTo(1612.6f).Within(0.1f));
            Assert.That(profile.DeclaredBaseMslMeters,
                Is.EqualTo(profile.AirportElevationMslMeters + profile.DeclaredBaseAglMeters).Within(0.01f));
            Assert.That(profile.Volumes, Has.Length.InRange(8, 24));
            Assert.That(profile.Volumes.Any(volume =>
                volume.baseMslMeters < 3500f && volume.topMslMeters > 3500f), Is.True,
                "At least one authored cloud must be physically traversable in the supported flight envelope.");
        }

        [Test]
        public void CloudMaterialIsDepthWritingAlphaToCoverageNotTransparentBlend()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            BoulderCloudField field = prefab.GetComponent<ProductionEnvironmentRoot>()
                .boulderCloudField.GetComponent<BoulderCloudField>();
            Material material = field.SharedCloudMaterial;
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("QuestFlight/Boulder Cloud Shell"));
            Assert.That(material.renderQueue, Is.GreaterThanOrEqualTo(2450).And.LessThan(3000));
            Assert.That(field.SectorRenderers.All(renderer =>
                renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off &&
                !renderer.receiveShadows), Is.True);

            string shaderPath = AssetDatabase.GetAssetPath(material.shader);
            string shader = File.ReadAllText(Path.GetFullPath(shaderPath));
            StringAssert.Contains("AlphaToMask On", shader);
            StringAssert.Contains("ZWrite On", shader);
            StringAssert.Contains("Blend Off", shader);
            StringAssert.Contains("clip(atlas.a - _CoverageCutoff)", shader);
            StringAssert.DoesNotContain("clip(luminance", shader);
            StringAssert.DoesNotContain("GrabPass", shader);
        }

        [Test]
        public void CloudTextureIsMipmappedStreamingAstcOnAndroid()
        {
            TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.streamingMipmaps, Is.True);
            Assert.That(importer.DoesSourceTextureHaveAlpha(), Is.True);
            Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.mipMapsPreserveCoverage, Is.True);
            Assert.That(importer.alphaTestReferenceValue, Is.EqualTo(0.02f).Within(0.001f));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            Assert.That(android.maxTextureSize, Is.LessThanOrEqualTo(512));
        }

        [Test]
        public void CloudMeshBoundsContainEveryShaderExpandedBillboard()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            BoulderCloudField field = prefab.GetComponent<ProductionEnvironmentRoot>()
                .boulderCloudField.GetComponent<BoulderCloudField>();
            MeshFilter filter = field.GetComponentInChildren<MeshFilter>(true);
            Assert.That(filter, Is.Not.Null);
            Bounds bounds = filter.sharedMesh.bounds;
            foreach (BoulderCloudProfile.Volume volume in field.Profile.Volumes)
            {
                float halfWidth = Mathf.Max(volume.radiiMeters.x, volume.radiiMeters.z);
                float cornerRadius = Mathf.Sqrt(
                    halfWidth * halfWidth + volume.radiiMeters.y * volume.radiiMeters.y);
                Vector3 expansion = Vector3.one * cornerRadius;
                Assert.That(bounds.Contains(volume.centerLocalMeters - expansion), Is.True,
                    volume.id + " minimum shader-expanded corner is outside CPU bounds.");
                Assert.That(bounds.Contains(volume.centerLocalMeters + expansion), Is.True,
                    volume.id + " maximum shader-expanded corner is outside CPU bounds.");
            }
        }

        [Test]
        public void InteriorContainmentIsSmoothAndZeroOutside()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            BoulderCloudField field = prefab.GetComponent<ProductionEnvironmentRoot>()
                .boulderCloudField.GetComponent<BoulderCloudField>();
            BoulderCloudProfile.Volume volume = field.Profile.Volumes[0];
            Vector3 center = field.transform.TransformPoint(volume.centerLocalMeters);
            Assert.That(field.EvaluateInteriorWeight(center), Is.GreaterThan(0.5f));
            Vector3 outside = field.transform.TransformPoint(
                volume.centerLocalMeters + Vector3.right * volume.radiiMeters.x * 2f);
            Assert.That(field.EvaluateInteriorWeight(outside), Is.EqualTo(0f).Within(0.001f));
        }
    }
}
