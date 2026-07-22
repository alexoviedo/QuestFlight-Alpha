#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using QuestFlightLab.PhotorealAerialSurfaceV1.Reflection;
using QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Tests.EditMode
{
    public class PhotorealAerialReflectionContractEditModeTests
    {
        private const string WindowShaderPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Shaders/ProductionCockpitWindowReflection.shader";
        private const string InstrumentShaderPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Shaders/ProductionInstrumentGlassReflection.shader";
        private const string CockpitStableInteriorShaderPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Cockpit/Shaders/ProductionCockpitStableInterior.shader";
        private const string RuntimeProfileSourcePath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Runtime/PhotorealAerialReflectionProfile.cs";

        [Test]
        public void ProfileKeepsOneStaticProbeAndDefaultOffShadowCandidate()
        {
            PhotorealAerialReflectionProfile profile =
                AssetDatabase.LoadAssetAtPath<PhotorealAerialReflectionProfile>(
                    PhotorealAerialReflectionAuthoring.ProfilePath);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.TryValidate(out string report), Is.True, report);
            Assert.That(profile.GlobalGroundSkyReflection, Is.Not.Null);
            Assert.That(profile.GlobalGroundSkyReflection.width, Is.EqualTo(128));
            Assert.That(profile.AirportLocalReflection, Is.Not.Null);
            Assert.That(profile.AirportLocalReflection.width, Is.EqualTo(128));
            Assert.That(profile.AirportLocalReflection, Is.Not.SameAs(profile.GlobalGroundSkyReflection));
            Assert.That(profile.RuntimeProbeUpdates, Is.False);
            Assert.That(profile.AirportProbeResolution, Is.EqualTo(128));
            Assert.That(profile.AirportProbeBoxProjection, Is.False);

            CockpitSunShadowCandidate shadow = profile.CockpitShadowCandidate;
            Assert.That(shadow.EnabledByDefault, Is.False);
            Assert.That(shadow.AcceptedOnQuestHardware, Is.False);
            Assert.That(shadow.CasterScope, Is.EqualTo(CockpitSunShadowCasterScope.CockpitInteriorOnly));
            Assert.That(shadow.MapResolution, Is.EqualTo(1024));
            Assert.That(shadow.CascadeCount, Is.Zero);
            Assert.That(shadow.DistanceMeters, Is.InRange(8f, 16f));
        }

        [Test]
        public void GroundSkyCubemapsUsePinnedQuestImportContract()
        {
            foreach (string path in new[]
                     {
                         PhotorealAerialReflectionAuthoring.GlobalReflectionPath,
                         PhotorealAerialReflectionAuthoring.AirportLocalReflectionPath
                     })
            {
                Cubemap cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                Assert.That(cubemap, Is.Not.Null, path);
                Assert.That(cubemap.width, Is.EqualTo(128), path);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.TextureCube), path);
                Assert.That(importer.generateCubemap,
                    Is.EqualTo(TextureImporterGenerateCubemap.Cylindrical), path);
                Assert.That(importer.sRGBTexture, Is.False, path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.streamingMipmaps, Is.False, path);
                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.That(settings.cubemapConvolution,
                    Is.EqualTo(TextureImporterCubemapConvolution.Specular), path);
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                Assert.That(android.overridden, Is.True, path);
                Assert.That(android.maxTextureSize, Is.EqualTo(128), path);
                Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_HDR_6x6), path);
            }
        }

        [Test]
        public void ReflectionSourceExrsAreDistinctAndHaveNonconstantLowerHemispheres()
        {
            string globalPath = PhotorealAerialReflectionAuthoring.GlobalReflectionPath;
            string localPath = PhotorealAerialReflectionAuthoring.AirportLocalReflectionPath;
            Assert.That(File.ReadAllBytes(globalPath), Is.Not.EqualTo(File.ReadAllBytes(localPath)));

            Color[] global = ReadLowerHemisphere(globalPath, "Global");
            Color[] local = ReadLowerHemisphere(localPath, "Local");
            Assert.That(LuminanceStandardDeviation(global), Is.GreaterThan(0.005f));
            Assert.That(LuminanceStandardDeviation(local), Is.GreaterThan(0.005f));
            Assert.That(MeanAbsoluteDifference(global, local), Is.GreaterThan(0.003f));

            string manifest = File.ReadAllText(PhotorealAerialReflectionAuthoring.ManifestPath);
            Assert.That(manifest, Does.Contain(Sha256(globalPath)));
            Assert.That(manifest, Does.Contain(Sha256(localPath)));
            Assert.That(manifest, Does.Contain(Sha256(
                PhotorealAerialReflectionAuthoring.SourceNearOrthophotoPath)));
            Assert.That(manifest, Does.Contain(Sha256(
                PhotorealAerialReflectionAuthoring.SourceMidOrthophotoPath)));
            Assert.That(manifest, Does.Contain(Sha256(
                PhotorealAerialReflectionAuthoring.SourceFarOrthophotoPath)));
            Assert.That(manifest, Does.Contain("bilinear_direction_to_ground_proxy"));
        }

        [Test]
        public void CockpitGlazingAndInteriorUseRestrainedSinglePassShaders()
        {
            Material window = AssetDatabase.LoadAssetAtPath<Material>(
                PhotorealAerialReflectionAuthoring.WindowMaterialPath);
            Material instrument = AssetDatabase.LoadAssetAtPath<Material>(
                PhotorealAerialReflectionAuthoring.InstrumentGlassMaterialPath);
            Assert.That(window, Is.Not.Null);
            Assert.That(instrument, Is.Not.Null);
            Assert.That(window.shader.name, Is.EqualTo(PhotorealAerialReflectionAuthoring.WindowShaderName));
            Assert.That(instrument.shader.name, Is.EqualTo(PhotorealAerialReflectionAuthoring.InstrumentGlassShaderName));
            Assert.That(window.passCount, Is.EqualTo(1));
            Assert.That(instrument.passCount, Is.EqualTo(1));
            Assert.That(window.renderQueue, Is.EqualTo((int)RenderQueue.Transparent + 12));
            Assert.That(instrument.renderQueue, Is.EqualTo((int)RenderQueue.Transparent + 16));
            Assert.That(window.GetTexture("_AuthoredReflectionCube"), Is.Not.Null);
            Assert.That(window.GetFloat("_WindshieldOpacity"), Is.InRange(0.09f, 0.11f));
            Assert.That(window.GetFloat("_SideWindowOpacity"), Is.InRange(0.11f, 0.13f));
            Assert.That(window.GetFloat("_ReflectionStrength"), Is.InRange(0.45f, 0.47f));
            Assert.That(window.GetFloat("_DirectGlintStrength"), Is.InRange(0.07f, 0.09f));
            Assert.That(instrument.GetFloat("_Opacity"), Is.LessThanOrEqualTo(0.02f));
            Assert.That(instrument.GetFloat("_ReflectionStrength"), Is.LessThanOrEqualTo(0.10f));

            foreach (string path in new[]
                     {
                         WindowShaderPath, InstrumentShaderPath, CockpitStableInteriorShaderPath
                     })
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Assert.That(shader, Is.Not.Null, path);
                Assert.That(ShaderUtil.GetShaderMessages(shader),
                    Has.None.Matches<ShaderMessage>(message =>
                        string.Equals(message.severity.ToString(), "Error", System.StringComparison.OrdinalIgnoreCase)),
                    path);
                string source = File.ReadAllText(path);
                Assert.That(source, Does.Contain("UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX"), path);
                if (!string.Equals(path, CockpitStableInteriorShaderPath, StringComparison.Ordinal))
                    Assert.That(source, Does.Contain("ZWrite Off"), path);
                Assert.That(source, Does.Not.Contain("GrabPass"), path);
                Assert.That(source, Does.Not.Contain("_CameraOpaqueTexture"), path);
                Assert.That(source, Does.Not.Contain("_CameraDepthTexture"), path);
                Assert.That(source, Does.Not.Contain("refraction"), path);
                Assert.That(source, Does.Not.Contain("raymarch"), path);
            }
            string windowSource = File.ReadAllText(WindowShaderPath);
            Assert.That(windowSource, Does.Contain("sunGlint"));
            Assert.That(windowSource, Does.Contain("_DirectGlintStrength"));
            Assert.That(windowSource, Does.Contain("_AuthoredReflectionCube"));
            Assert.That(windowSource, Does.Contain("texCUBE"));
            Assert.That(windowSource, Does.Contain("proceduralGround"));
            Assert.That(windowSource, Does.Contain("Blend SrcAlpha OneMinusSrcAlpha"));
            Assert.That(windowSource, Does.Not.Contain("UnityGI_IndirectSpecular"));
            string instrumentSource = File.ReadAllText(InstrumentShaderPath);
            Assert.That(instrumentSource, Does.Contain("Blend One OneMinusSrcAlpha"));
            string interiorSource = File.ReadAllText(CockpitStableInteriorShaderPath);
            Assert.That(interiorSource, Does.Contain("_CabinDepthStrength"));
            Assert.That(interiorSource, Does.Contain("_DirectionalCabinShadowStrength"));
            Assert.That(interiorSource, Does.Contain("_WorldToAircraft"));
            Assert.That(interiorSource, Does.Contain("frameShade"));
            Assert.That(interiorSource, Does.Contain("bakedOcclusion"));
            Assert.That(interiorSource, Does.Contain("Blend Off"));
            Assert.That(interiorSource, Does.Contain("ZWrite On"));
            Assert.That(interiorSource, Does.Contain("\"RenderType\"=\"Opaque\""));
            Assert.That(interiorSource, Does.Not.Contain("SHADOWS_SCREEN"));
            Assert.That(interiorSource, Does.Not.Contain("AutoLight.cginc"));
            Assert.That(interiorSource, Does.Not.Contain("AlphaToMask"));
        }

        [Test]
        public void CockpitViewKeepsExteriorGlazingAndRejectsScreenSpaceOcclusionOverlay()
        {
            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(
                PhotorealAerialReflectionAuthoring.AircraftPrefabPath);
            Assert.That(aircraft, Is.Not.Null);
            Renderer window = aircraft.GetComponentsInChildren<Renderer>(true).Single(renderer =>
                string.Equals(renderer.name,
                    PhotorealAerialReflectionAuthoring.ExteriorWindowRendererName,
                    StringComparison.Ordinal));
            Assert.That(aircraft.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                string.Equals(renderer.name,
                    PhotorealAerialReflectionAuthoring.RejectedCockpitSunOcclusionObjectName,
                    StringComparison.Ordinal)), Is.False);
            QuestFlightLab.Aircraft.ProductionAircraftLightingController lighting =
                aircraft.GetComponentInChildren<QuestFlightLab.Aircraft.ProductionAircraftLightingController>(true);
            Assert.That(lighting, Is.Not.Null);
            SerializedObject lightingSettings = new SerializedObject(lighting);
            Assert.That(lightingSettings.FindProperty("stableCockpitShader").objectReferenceValue,
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<Shader>(CockpitStableInteriorShaderPath)));
            Assert.That(lightingSettings.FindProperty("directionalCabinShadowStrength").floatValue,
                Is.EqualTo(QuestFlightLab.Aircraft.ProductionAircraftLightingController
                    .DefaultDirectionalCabinShadowStrength).Within(0.0001f));
            QuestFlightLab.Aircraft.AircraftVisibilityProfileController visibility =
                aircraft.GetComponentInChildren<QuestFlightLab.Aircraft.AircraftVisibilityProfileController>(true);
            Assert.That(visibility, Is.Not.Null);
            SerializedObject serialized = new SerializedObject(visibility);
            SerializedProperty occluders = serialized.FindProperty("cockpitOccludingExteriorRenderers");
            SerializedProperty interiors = serialized.FindProperty("interiorRenderers");
            Assert.That(Enumerable.Range(0, occluders.arraySize).Any(index =>
                ReferenceEquals(occluders.GetArrayElementAtIndex(index).objectReferenceValue, window)), Is.False);
            Assert.That(Enumerable.Range(0, interiors.arraySize).All(index =>
                interiors.GetArrayElementAtIndex(index).objectReferenceValue != null), Is.True);
        }

        [Test]
        public void OnlyAuthoritativeInstrumentCoverConsumesTransparentDraw()
        {
            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(
                PhotorealAerialReflectionAuthoring.AircraftPrefabPath);
            Assert.That(aircraft, Is.Not.Null);
            Material instrument = AssetDatabase.LoadAssetAtPath<Material>(
                PhotorealAerialReflectionAuthoring.InstrumentGlassMaterialPath);
            Renderer[] covers = aircraft.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.sharedMaterials.Contains(instrument)).ToArray();
            Assert.That(covers, Has.Length.EqualTo(2));
            Assert.That(covers.Count(renderer => renderer.enabled), Is.EqualTo(1));
            Renderer decorative = covers.Single(renderer => string.Equals(renderer.name,
                PhotorealAerialReflectionAuthoring.InstrumentCoverRendererName,
                StringComparison.Ordinal));
            Assert.That(decorative.enabled, Is.False);
        }

        [Test]
        public void RuntimeProfileCannotConstructOrUpdateProductHierarchy()
        {
            string source = File.ReadAllText(RuntimeProfileSourcePath);
            foreach (string forbidden in new[]
                     {
                         "AddComponent", "Instantiate", "new GameObject", "Camera", "RenderProbe",
                         "void Update(", "void LateUpdate(", "void FixedUpdate("
                     })
                Assert.That(source, Does.Not.Contain(forbidden), forbidden);
            Assert.That(PhotorealAerialReflectionProfile.MaximumRuntimeProbeRenderCount, Is.Zero);
            Assert.That(PhotorealAerialReflectionProfile.RequiredAirportProbeCount, Is.EqualTo(1));
        }

        private static Color[] ReadLowerHemisphere(string sourcePath, string suffix)
        {
            string temporaryPath =
                "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Textures/" +
                "__ReflectionContractTest_" + suffix + ".exr";
            AssetDatabase.DeleteAsset(temporaryPath);
            try
            {
                File.Copy(sourcePath, temporaryPath, true);
                AssetDatabase.ImportAsset(temporaryPath, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = AssetImporter.GetAtPath(temporaryPath) as TextureImporter;
                Assert.That(importer, Is.Not.Null);
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = false;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 256;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(temporaryPath);
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.width, Is.EqualTo(256));
                Assert.That(texture.height, Is.EqualTo(128));
                Color[] pixels = texture.GetPixels(0);
                return pixels.Take(256 * 64).ToArray();
            }
            finally
            {
                AssetDatabase.DeleteAsset(temporaryPath);
            }
        }

        private static float LuminanceStandardDeviation(Color[] pixels)
        {
            double mean = pixels.Average(pixel =>
                pixel.r * 0.2126d + pixel.g * 0.7152d + pixel.b * 0.0722d);
            double variance = pixels.Average(pixel =>
            {
                double luminance = pixel.r * 0.2126d + pixel.g * 0.7152d + pixel.b * 0.0722d;
                return (luminance - mean) * (luminance - mean);
            });
            return (float)Math.Sqrt(variance);
        }

        private static float MeanAbsoluteDifference(Color[] left, Color[] right)
        {
            Assert.That(left.Length, Is.EqualTo(right.Length));
            return (float)left.Zip(right, (a, b) =>
                (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)) / 3d).Average();
        }

        private static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
                return string.Concat(algorithm.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
#endif
