#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Environment;
using QuestFlightLab.PhotorealAerialSurfaceV1.Reflection;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor
{
    /// <summary>
    /// Deterministic offline authoring for the reflection/cockpit handoff. The assets-only
    /// entry point is safe for workstream generation. IntegrateForLead is deliberately
    /// separate because it serializes explicit prefab and scene bindings.
    /// </summary>
    public static class PhotorealAerialReflectionAuthoring
    {
        public const string Root =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection";
        public const string TextureFolder = Root + "/Textures";
        public const string ProfileFolder = Root + "/Profiles";
        public const string CockpitMaterialFolder = Root + "/Cockpit/Materials";
        public const string GlobalReflectionPath =
            TextureFolder + "/KBDU_GlobalGroundSkyReflection_256.exr";
        public const string AirportLocalReflectionPath =
            TextureFolder + "/KBDU_AirportLocalReflection_256.exr";
        public const string ManifestPath =
            Root + "/KBDU_PhotorealAerialReflectionManifestV1.json";
        public const string ProfilePath =
            ProfileFolder + "/KBDU_PhotorealAerialReflectionProfile.asset";
        public const string WindowMaterialPath =
            CockpitMaterialFolder + "/ProductionCockpitWindowReflection.mat";
        public const string InstrumentGlassMaterialPath =
            CockpitMaterialFolder + "/ProductionInstrumentGlassReflection.mat";
        public const string CockpitStableInteriorShaderPath =
            Root + "/Cockpit/Shaders/ProductionCockpitStableInterior.shader";

        public const string WindowShaderName =
            "QuestFlight/Photoreal Aerial Surface V1/Cockpit Window Reflection";
        public const string InstrumentGlassShaderName =
            "QuestFlight/Photoreal Aerial Surface V1/Instrument Glass Reflection";
        public const string CockpitStableInteriorShaderName =
            QuestFlightLab.Aircraft.ProductionAircraftLightingController.StableCockpitShaderName;

        public const string SourceSkyPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr";
        public const string SourceNearOrthophotoPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_City2020_Near4km_8192.jpg";
        public const string SourceMidOrthophotoPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_City2020_MidBase12km_8192.jpg";
        public const string SourceFarOrthophotoPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_City2020_Far24km_4096.jpg";
        public const string SourceOrthophotoManifestPath =
            "Assets/Production/Environment/AerialSurfaceV1/Ground/KBDU_AerialSurfaceV1_SourceManifest.json";
        public const string EnvironmentPrefabPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string ProductionScenePath =
            "Assets/Scenes/ProductionVerticalSlice.unity";
        public const string ProductionWorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        public const string MainSunName = "Sun";
        public const string CockpitShadowSunName = "CockpitShadowSun";
        public const string CockpitShadowLayerName = "CockpitSunShadow";
        public const int CockpitShadowLayer = 30;
        public const string SourceWindowMaterialPath =
            "Assets/Production/Materials/Cockpit/Cockpit_GlassMat_New_-8047257934222504514.mat";
        public const string SourceInstrumentGlassMaterialPath =
            "Assets/Production/Materials/Cockpit/Cockpit_MI_Glass1_-7336442739054310687.mat";

        public const string FoundationObjectName = "PhotorealAerialReflectionFoundation";
        public const string AirportProbeObjectName = "KBDU_AirportReflectionProbe";
        public const string CockpitRootName = "ImportedCockpitInterior";
        public const string ExteriorWindowRendererName =
            "Cessna_Exterior_Glass_MAT_0_Glass_MAT_0";
        public const string InstrumentCoverRendererName =
            "GlassPartNew_GlassMat_New_0_GlassMat_New_0";
        public const string RejectedCockpitSunOcclusionObjectName =
            "ProductionCockpitBakedSunOcclusion";

        private const int ReflectionEquirectangularWidth = 256;
        private const int ReflectionEquirectangularHeight = 128;
        private const float LocalProxyHeightMeters = 72f;
        private const float LocalProxyMaximumRadiusMeters = 1700f;
        private const float GlobalProxyHeightMeters = 720f;
        private const float GlobalProxyMaximumRadiusMeters = 11200f;
        private static readonly Vector2 AirportReflectionCenter = new Vector2(-385f, -260f);
        private static readonly Color GroundRadiance = new Color(0.115f, 0.128f, 0.095f, 1f);

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Author Reflection Runtime Assets Only")]
        public static void AuthorRuntimeAssetsOnly()
        {
            EnsureFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ReflectionAssetPair reflections = GenerateGroundSkyReflections();
            EnsureMaterials();
            EnsureProfile(reflections.Global, reflections.AirportLocal);
            AssetDatabase.SaveAssets();
            WriteManifest(reflections);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateRuntimeAssets();
            Debug.Log(
                "[QuestFlight][PhotorealAerialReflection] Authored assets only: distinct 128-face static " +
                "global and airport-local imagery-aware ground-sky cubemaps, two single-pass reflection materials, " +
                "one geometry-bound cockpit depth shader, and one default-off shadow profile. " +
                "No prefab, scene, camera, probe hierarchy, or project quality setting was changed.");
        }

        public static void AuthorRuntimeAssetsOnlyBatch()
        {
            try
            {
                AuthorRuntimeAssetsOnly();
                Debug.Log("[QuestFlight][PhotorealAerialReflection] ASSETS_ONLY_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Integrate Reflection And Cockpit Foundation")]
        public static void IntegrateForLead()
        {
            AuthorRuntimeAssetsOnly();
            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            IntegrateEnvironmentPrefab(profile);
            IntegrateAircraftPrefab();
            RemoveRejectedCockpitShadowSun();
            IntegrateSceneReflection(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateIntegratedFoundation();
            Debug.Log(
                "[QuestFlight][PhotorealAerialReflection] LEAD_INTEGRATION_SUCCESS. " +
                "The rejected realtime cockpit shadow sun is absent; static material depth remains authoritative.");
        }

        public static void IntegrateForLeadBatch()
        {
            try
            {
                IntegrateForLead();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static CockpitShadowCandidateScope BeginCockpitShadowCandidateForDevelopment(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("A loaded scene is required for the transient cockpit shadow A/B.", nameof(scene));
            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            if (!profile.TryValidate(out string report))
                throw new InvalidOperationException("Reflection profile is invalid: " + report);
            return new CockpitShadowCandidateScope(scene, profile.CockpitShadowCandidate);
        }

        public static void WithCockpitShadowCandidateForDevelopment(Scene scene, Action capture)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            using (BeginCockpitShadowCandidateForDevelopment(scene)) capture();
        }

        private static ReflectionAssetPair GenerateGroundSkyReflections()
        {
            foreach (string path in new[]
                     {
                         SourceSkyPath, SourceNearOrthophotoPath, SourceMidOrthophotoPath,
                         SourceFarOrthophotoPath, SourceOrthophotoManifestPath
                     })
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("Pinned reflection source is missing.", path);
            }

            var temporaryPaths = new[]
            {
                TextureFolder + "/KBDU_ReflectionSkySource_TEMP.hdr",
                TextureFolder + "/KBDU_ReflectionNearSource_TEMP.jpg",
                TextureFolder + "/KBDU_ReflectionMidSource_TEMP.jpg",
                TextureFolder + "/KBDU_ReflectionFarSource_TEMP.jpg"
            };
            foreach (string path in temporaryPaths) AssetDatabase.DeleteAsset(path);

            Texture2D sky = null;
            Texture2D near = null;
            Texture2D mid = null;
            Texture2D far = null;
            Texture2D globalOutput = null;
            Texture2D localOutput = null;
            ReflectionStatistics globalStatistics;
            ReflectionStatistics localStatistics;
            try
            {
                sky = ImportReadableTemporaryCopy(SourceSkyPath, temporaryPaths[0], false, 2048);
                near = ImportReadableTemporaryCopy(SourceNearOrthophotoPath, temporaryPaths[1], true, 2048);
                mid = ImportReadableTemporaryCopy(SourceMidOrthophotoPath, temporaryPaths[2], true, 2048);
                far = ImportReadableTemporaryCopy(SourceFarOrthophotoPath, temporaryPaths[3], true, 2048);

                Color[] globalPixels = BuildReflectionPixels(sky, near, mid, far, false);
                Color[] localPixels = BuildReflectionPixels(sky, near, mid, far, true);
                globalStatistics = ReflectionStatistics.FromLowerHemisphere(globalPixels);
                localStatistics = ReflectionStatistics.FromLowerHemisphere(localPixels);
                if (globalStatistics.LuminanceStandardDeviation < 0.005f ||
                    localStatistics.LuminanceStandardDeviation < 0.005f)
                    throw new InvalidOperationException("Orthophoto lower hemisphere is unexpectedly constant.");
                if (MeanAbsoluteDifference(globalPixels, localPixels) < 0.003f)
                    throw new InvalidOperationException("Global and airport-local reflections are not distinct enough.");

                globalOutput = CreateReflectionTexture(
                    "KBDU_GlobalGroundSkyReflection_256", globalPixels);
                localOutput = CreateReflectionTexture(
                    "KBDU_AirportLocalReflection_256", localPixels);
                WriteIfChanged(GlobalReflectionPath,
                    globalOutput.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                WriteIfChanged(AirportLocalReflectionPath,
                    localOutput.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
            }
            finally
            {
                foreach (Texture2D texture in new[] { sky, near, mid, far })
                    if (texture != null) Resources.UnloadAsset(texture);
                if (globalOutput != null) UnityEngine.Object.DestroyImmediate(globalOutput);
                if (localOutput != null) UnityEngine.Object.DestroyImmediate(localOutput);
                foreach (string path in temporaryPaths) AssetDatabase.DeleteAsset(path);
            }

            Cubemap global = ConfigureReflectionImport(GlobalReflectionPath);
            Cubemap airportLocal = ConfigureReflectionImport(AirportLocalReflectionPath);
            if (global == airportLocal)
                throw new InvalidOperationException("Global and airport-local cubemaps resolved to the same asset.");
            return new ReflectionAssetPair(global, airportLocal, globalStatistics, localStatistics);
        }

        private static Texture2D ImportReadableTemporaryCopy(
            string sourcePath,
            string temporaryPath,
            bool sRgb,
            int maximumSize)
        {
            File.Copy(sourcePath, temporaryPath, true);
            AssetDatabase.ImportAsset(temporaryPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(temporaryPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Temporary reflection source did not import: " + sourcePath);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = sRgb;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maximumSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(temporaryPath);
            if (texture == null || !texture.isReadable)
                throw new InvalidOperationException("Temporary reflection source is not CPU-readable: " + sourcePath);
            return texture;
        }

        private static Color[] BuildReflectionPixels(
            Texture2D sky,
            Texture2D near,
            Texture2D mid,
            Texture2D far,
            bool airportLocal)
        {
            Color[] pixels = new Color[ReflectionEquirectangularWidth * ReflectionEquirectangularHeight];
            float proxyHeight = airportLocal ? LocalProxyHeightMeters : GlobalProxyHeightMeters;
            float maximumRadius = airportLocal ? LocalProxyMaximumRadiusMeters : GlobalProxyMaximumRadiusMeters;
            Vector2 center = airportLocal ? AirportReflectionCenter : Vector2.zero;
            for (int y = 0; y < ReflectionEquirectangularHeight; y++)
            {
                float v = (y + 0.5f) / ReflectionEquirectangularHeight;
                for (int x = 0; x < ReflectionEquirectangularWidth; x++)
                {
                    float u = (x + 0.5f) / ReflectionEquirectangularWidth;
                    Color color = sky.GetPixelBilinear(u, v);
                    if (v < 0.5f)
                    {
                        float elevation = (v - 0.5f) * Mathf.PI;
                        float downward = Mathf.Max(0.0001f, -Mathf.Sin(elevation));
                        float horizontal = Mathf.Max(0f, Mathf.Cos(elevation));
                        float distance = Mathf.Min(maximumRadius, proxyHeight * horizontal / downward);
                        float azimuth = (u - 0.5f) * Mathf.PI * 2f;
                        Vector2 world = center + new Vector2(
                            Mathf.Sin(azimuth), Mathf.Cos(azimuth)) * distance;
                        Color ground = airportLocal
                            ? SampleWorldOrthophoto(near, world, -2000f, 4000f)
                            : SampleGlobalOrthophoto(mid, far, world);
                        float radianceScale = airportLocal ? 0.46f : 0.40f;
                        ground = Color.Lerp(GroundRadiance * 0.30f, ground * radianceScale, 0.92f);
                        Color horizon = sky.GetPixelBilinear(u, 0.50390625f);
                        float seam = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(0.006f, 0.045f, downward));
                        color = Color.Lerp(horizon, ground, seam);
                    }
                    color.r = Mathf.Max(0f, color.r);
                    color.g = Mathf.Max(0f, color.g);
                    color.b = Mathf.Max(0f, color.b);
                    color.a = 1f;
                    pixels[y * ReflectionEquirectangularWidth + x] = color;
                }
            }
            return pixels;
        }

        private static Color SampleGlobalOrthophoto(Texture2D mid, Texture2D far, Vector2 world)
        {
            Color midColor = SampleWorldOrthophoto(mid, world, -6000f, 12000f);
            Color farColor = SampleWorldOrthophoto(far, world, -12000f, 24000f);
            float radial = world.magnitude;
            float farWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4600f, 5800f, radial));
            return Color.Lerp(midColor, farColor, farWeight);
        }

        private static Color SampleWorldOrthophoto(
            Texture2D texture,
            Vector2 world,
            float worldMinimum,
            float worldSize)
        {
            float u = Mathf.Clamp01((world.x - worldMinimum) / worldSize);
            float v = Mathf.Clamp01((world.y - worldMinimum) / worldSize);
            return texture.GetPixelBilinear(u, v);
        }

        private static Texture2D CreateReflectionTexture(string name, Color[] pixels)
        {
            Texture2D output = new Texture2D(
                ReflectionEquirectangularWidth,
                ReflectionEquirectangularHeight,
                TextureFormat.RGBAHalf,
                false,
                true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            output.SetPixels(pixels);
            output.Apply(false, false);
            return output;
        }

        private static Cubemap ConfigureReflectionImport(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Generated ground-sky EXR did not import: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            importer.SetTextureSettings(textureSettings);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = PhotorealAerialReflectionProfile.RequiredCubemapFaceSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = PhotorealAerialReflectionProfile.RequiredCubemapFaceSize;
            android.format = TextureImporterFormat.ASTC_HDR_6x6;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();

            Cubemap cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cubemap == null || cubemap.width != PhotorealAerialReflectionProfile.RequiredCubemapFaceSize)
                throw new InvalidOperationException("Generated cubemap is not 128 pixels per face: " + path);
            return cubemap;
        }

        private static float MeanAbsoluteDifference(Color[] left, Color[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                throw new ArgumentException("Reflection comparison arrays must have equal lengths.");
            double sum = 0d;
            int samples = ReflectionEquirectangularWidth * ReflectionEquirectangularHeight / 2;
            for (int index = 0; index < samples; index++)
            {
                Color a = left[index];
                Color b = right[index];
                sum += (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)) / 3d;
            }
            return (float)(sum / samples);
        }

        private static void WriteManifest(ReflectionAssetPair reflections)
        {
            string globalHash = Sha256(GlobalReflectionPath);
            string localHash = Sha256(AirportLocalReflectionPath);
            StringBuilder json = new StringBuilder(8192);
            json.AppendLine("{");
            json.AppendLine("  \"schema\": \"questflight.photoreal_aerial_surface.reflection_manifest.v1\",");
            json.AppendLine("  \"status\": \"FOUNDATION_AUTHORED_NOT_VISUALLY_ACCEPTED\",");
            json.AppendLine("  \"shipping_target\": \"Standalone Quest 3 / Android ARM64 / Vulkan / OpenXR\",");
            json.AppendLine("  \"source\": {");
            AppendJsonString(json, "sky_asset", RepoPath(SourceSkyPath), true, 4);
            AppendJsonString(json, "sky_source_manifest",
                "QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json", true, 4);
            AppendJsonString(json, "sky_license", "CC0-1.0", true, 4);
            AppendJsonString(json, "sky_sha256", Sha256(SourceSkyPath), true, 4);
            AppendJsonString(json, "orthophoto_source_manifest", RepoPath(SourceOrthophotoManifestPath), true, 4);
            AppendJsonString(json, "orthophoto_license", "CC0-1.0", true, 4);
            AppendJsonString(json, "near_orthophoto", RepoPath(SourceNearOrthophotoPath), true, 4);
            AppendJsonString(json, "near_orthophoto_sha256", Sha256(SourceNearOrthophotoPath), true, 4);
            AppendJsonString(json, "mid_orthophoto", RepoPath(SourceMidOrthophotoPath), true, 4);
            AppendJsonString(json, "mid_orthophoto_sha256", Sha256(SourceMidOrthophotoPath), true, 4);
            AppendJsonString(json, "far_orthophoto", RepoPath(SourceFarOrthophotoPath), true, 4);
            AppendJsonString(json, "far_orthophoto_sha256", Sha256(SourceFarOrthophotoPath), true, 4);
            AppendJsonString(json, "upper_hemisphere",
                "Exact bilinear sample of the committed HDR sky; no alternate sky or runtime capture.", true, 4);
            AppendJsonString(json, "lower_hemisphere",
                "bilinear_direction_to_ground_proxy over committed City AerialSurfaceV1 orthophotos", true, 4);
            AppendJsonString(json, "authoring_code", RepoPath(
                Root + "/Editor/PhotorealAerialReflectionAuthoring.cs"), false, 4);
            json.AppendLine("  },");
            json.AppendLine("  \"runtime_assets\": {");
            AppendReflectionManifestAsset(json, "global_ground_sky_reflection", GlobalReflectionPath,
                globalHash, reflections.GlobalStatistics, "mid+far", Vector2.zero,
                GlobalProxyHeightMeters, GlobalProxyMaximumRadiusMeters, true);
            AppendReflectionManifestAsset(json, "airport_local_reflection", AirportLocalReflectionPath,
                localHash, reflections.LocalStatistics, "near", AirportReflectionCenter,
                LocalProxyHeightMeters, LocalProxyMaximumRadiusMeters, true);
            AppendFileManifestAsset(json, "profile", ProfilePath, true);
            AppendFileManifestAsset(json, "window_material", WindowMaterialPath, true);
            AppendFileManifestAsset(json, "instrument_glass_material", InstrumentGlassMaterialPath, true);
            AppendFileManifestAsset(json, "window_shader",
                Root + "/Cockpit/Shaders/ProductionCockpitWindowReflection.shader", true);
            AppendFileManifestAsset(json, "instrument_glass_shader",
                Root + "/Cockpit/Shaders/ProductionInstrumentGlassReflection.shader", true);
            AppendFileManifestAsset(json, "cockpit_stable_interior_shader",
                CockpitStableInteriorShaderPath, false);
            json.AppendLine("  },");
            json.AppendLine("  \"integration_contract\": {");
            AppendJsonString(json, "global_reflection_mode",
                "Custom static imagery-aware global cubemap", true, 4);
            AppendJsonString(json, "airport_probe_texture",
                RepoPath(AirportLocalReflectionPath), true, 4);
            json.AppendLine("    \"airport_probe_count\": 1,");
            AppendJsonString(json, "airport_probe_mode",
                "Custom distinct static texture, ViaScripting refresh mode, culling mask zero", true, 4);
            json.AppendLine("    \"runtime_probe_render_count\": 0,");
            json.AppendLine("    \"screen_space_reflections\": false,");
            json.AppendLine("    \"reflection_camera\": false,");
            json.AppendLine("    \"refraction_camera\": false,");
            AppendJsonString(json, "aircraft_probe_anchor",
                "AircraftSimulationRoot/CenterOfGravityReference", true, 4);
            AppendJsonString(json, "aircraft_probe_usage", "BlendProbesAndSkybox", false, 4);
            json.AppendLine("  },");
            json.AppendLine("  \"android_budget\": {");
            json.AppendLine("    \"new_reflection_probes\": 1,");
            json.AppendLine("    \"new_runtime_probe_cameras\": 0,");
            json.AppendLine("    \"new_runtime_probe_updates\": 0,");
            json.AppendLine("    \"new_renderers\": 0,");
            json.AppendLine("    \"new_draw_calls\": 0,");
            json.AppendLine("    \"new_materials\": 2,");
            json.AppendLine("    \"disabled_duplicate_exterior_glass_renderers\": 1,");
            json.AppendLine("    \"static_cubemap_count\": 2,");
            json.AppendLine("    \"estimated_combined_cubemap_bytes\": 126144");
            json.AppendLine("  },");
            json.AppendLine("  \"cockpit_directional_shading\": {");
            json.AppendLine("    \"enabled_by_default\": true,");
            AppendJsonString(json, "representation",
                "single-pass analytic aircraft-local windshield-frame shade", true, 4);
            json.AppendLine("    \"strength\": 0.18,");
            json.AppendLine("    \"additional_draw_calls\": 0,");
            json.AppendLine("    \"shadow_map\": false,");
            json.AppendLine("    \"projector_geometry\": false,");
            json.AppendLine("    \"physical_acceptance_pending\": true");
            json.AppendLine("  },");
            json.AppendLine("  \"cockpit_shadow_candidate\": {");
            json.AppendLine("    \"enabled_by_default\": false,");
            json.AppendLine("    \"accepted_on_quest_hardware\": false,");
            AppendJsonString(json, "selection",
                "Editor-only scoped A/B; exact prior state is restored on dispose", true, 4);
            json.AppendLine("    \"map_resolution\": 1024,");
            json.AppendLine("    \"cascade_count\": 0,");
            json.AppendLine("    \"distance_meters\": 14.0");
            json.AppendLine("  },");
            AppendJsonString(json, "acceptance_boundary",
                "Assets-only authoring modifies no product scene or prefab. Lead integration, inspected stereo Visual QA, Android build validation, and physical Quest frame/thermal evidence remain required.", false, 2);
            json.AppendLine("}");
            WriteIfChanged(ManifestPath, Encoding.UTF8.GetBytes(json.ToString().Replace("\r\n", "\n")));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void AppendReflectionManifestAsset(
            StringBuilder json,
            string id,
            string path,
            string hash,
            ReflectionStatistics statistics,
            string groundSources,
            Vector2 center,
            float proxyHeight,
            float maximumRadius,
            bool trailingComma)
        {
            json.Append("    \"").Append(id).AppendLine("\": {");
            AppendJsonString(json, "path", RepoPath(path), true, 6);
            json.AppendLine("      \"equirectangular_pixels\": [256, 128],");
            json.AppendLine("      \"cubemap_face_pixels\": 128,");
            AppendJsonString(json, "convolution", "Specular", true, 6);
            AppendJsonString(json, "android_format", "ASTC_HDR_6x6", true, 6);
            json.AppendLine("      \"mipmaps\": true,");
            AppendJsonString(json, "ground_sources", groundSources, true, 6);
            AppendJsonString(json, "sampling", "bilinear_direction_to_ground_proxy", true, 6);
            json.Append("      \"proxy_center_xz_meters\": [")
                .Append(JsonNumber(center.x)).Append(", ").Append(JsonNumber(center.y)).AppendLine("],");
            json.Append("      \"proxy_height_meters\": ").Append(JsonNumber(proxyHeight)).AppendLine(",");
            json.Append("      \"maximum_radius_meters\": ").Append(JsonNumber(maximumRadius)).AppendLine(",");
            json.Append("      \"lower_hemisphere_rgb_mean_linear\": [")
                .Append(JsonNumber(statistics.Mean.r)).Append(", ")
                .Append(JsonNumber(statistics.Mean.g)).Append(", ")
                .Append(JsonNumber(statistics.Mean.b)).AppendLine("],");
            json.Append("      \"lower_hemisphere_rgb_standard_deviation_linear\": [")
                .Append(JsonNumber(statistics.StandardDeviation.r)).Append(", ")
                .Append(JsonNumber(statistics.StandardDeviation.g)).Append(", ")
                .Append(JsonNumber(statistics.StandardDeviation.b)).AppendLine("],");
            json.Append("      \"lower_hemisphere_luminance_standard_deviation_linear\": ")
                .Append(JsonNumber(statistics.LuminanceStandardDeviation)).AppendLine(",");
            AppendJsonString(json, "sha256", hash, true, 6);
            json.Append("      \"bytes\": ").Append(new FileInfo(path).Length).AppendLine();
            json.Append("    }").AppendLine(trailingComma ? "," : string.Empty);
        }

        private static void AppendFileManifestAsset(
            StringBuilder json,
            string id,
            string path,
            bool trailingComma)
        {
            json.Append("    \"").Append(id).AppendLine("\": {");
            AppendJsonString(json, "path", RepoPath(path), true, 6);
            AppendJsonString(json, "sha256", Sha256(path), File.Exists(path), 6);
            if (File.Exists(path))
                json.Append("      \"bytes\": ").Append(new FileInfo(path).Length).AppendLine();
            json.Append("    }").AppendLine(trailingComma ? "," : string.Empty);
        }

        private static void AppendJsonString(
            StringBuilder json,
            string key,
            string value,
            bool trailingComma,
            int indentation)
        {
            json.Append(' ', indentation).Append('"').Append(key).Append("\": \"")
                .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                .Append('"').AppendLine(trailingComma ? "," : string.Empty);
        }

        private static string RepoPath(string projectPath) => "QuestFlight/" + projectPath;

        private static string JsonNumber(float value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
                return string.Concat(algorithm.ComputeHash(stream).Select(item => item.ToString("x2")));
        }

        private static void EnsureMaterials()
        {
            Shader windowShader = Shader.Find(WindowShaderName);
            Shader instrumentShader = Shader.Find(InstrumentGlassShaderName);
            Shader stableInteriorShader = Shader.Find(CockpitStableInteriorShaderName);
            if (windowShader == null || instrumentShader == null || stableInteriorShader == null)
                throw new InvalidOperationException("Reflection shaders did not import before material authoring.");

            Material window = LoadOrCreateMaterial(WindowMaterialPath, windowShader,
                "ProductionCockpitWindowReflection");
            window.SetTexture("_AuthoredReflectionCube", LoadRequired<Cubemap>(GlobalReflectionPath));
            window.SetColor("_WindshieldTint", new Color(0.77f, 0.86f, 0.95f, 1f));
            window.SetColor("_SideWindowTint", new Color(0.70f, 0.82f, 0.92f, 1f));
            window.SetFloat("_WindshieldOpacity", 0.10f);
            window.SetFloat("_SideWindowOpacity", 0.12f);
            window.SetFloat("_FresnelOpacity", 0.14f);
            window.SetFloat("_ReflectionStrength", 0.46f);
            window.SetFloat("_DirectGlintStrength", 0.08f);
            window.SetFloat("_PerceptualRoughness", 0.18f);
            window.SetFloat("_SideAxisStart", 0.52f);
            window.SetFloat("_SideAxisEnd", 0.82f);
            window.renderQueue = (int)RenderQueue.Transparent + 12;
            window.enableInstancing = false;
            window.doubleSidedGI = false;
            EditorUtility.SetDirty(window);

            Material instrument = LoadOrCreateMaterial(InstrumentGlassMaterialPath, instrumentShader,
                "ProductionInstrumentGlassReflection");
            instrument.SetColor("_Tint", new Color(0.90f, 0.94f, 0.98f, 1f));
            instrument.SetFloat("_Opacity", 0.018f);
            instrument.SetFloat("_FresnelOpacity", 0.035f);
            instrument.SetFloat("_ReflectionStrength", 0.10f);
            instrument.SetFloat("_PerceptualRoughness", 0.42f);
            instrument.renderQueue = (int)RenderQueue.Transparent + 16;
            instrument.enableInstancing = false;
            instrument.doubleSidedGI = false;
            EditorUtility.SetDirty(instrument);

        }

        private static void EnsureProfile(Cubemap globalCubemap, Cubemap airportLocalCubemap)
        {
            PhotorealAerialReflectionProfile profile =
                AssetDatabase.LoadAssetAtPath<PhotorealAerialReflectionProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PhotorealAerialReflectionProfile>();
                profile.name = "KBDU_PhotorealAerialReflectionProfile";
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            SerializedObject serialized = new SerializedObject(profile);
            SetString(serialized, "schema", PhotorealAerialReflectionProfile.Schema);
            SetString(serialized, "status", "FOUNDATION_AUTHORED_NOT_VISUALLY_ACCEPTED");
            SetObject(serialized, "globalGroundSkyReflection", globalCubemap);
            SetObject(serialized, "airportLocalReflection", airportLocalCubemap);
            SetColor(serialized, "lowerHemisphereGroundColor", GroundRadiance);
            SetFloat(serialized, "globalReflectionIntensity", 0.68f);
            SetVector3(serialized, "airportProbeLocalPosition", new Vector3(-385f, 5f, -260f));
            SetVector3(serialized, "airportProbeSize", new Vector3(260f, 35f, 240f));
            SetFloat(serialized, "airportProbeBlendDistance", 24f);
            SetFloat(serialized, "airportProbeIntensity", 0.72f);
            SetInt(serialized, "airportProbeResolution", 128);
            SetBool(serialized, "airportProbeBoxProjection", false);
            SetBool(serialized, "runtimeProbeUpdates", false);
            SetBool(serialized, "cockpitSunShadowCandidate.enabledByDefault", false);
            SetBool(serialized, "cockpitSunShadowCandidate.acceptedOnQuestHardware", false);
            SetInt(serialized, "cockpitSunShadowCandidate.casterScope",
                (int)CockpitSunShadowCasterScope.CockpitInteriorOnly);
            SetInt(serialized, "cockpitSunShadowCandidate.mapResolution", 1024);
            SetInt(serialized, "cockpitSunShadowCandidate.cascadeCount", 0);
            SetFloat(serialized, "cockpitSunShadowCandidate.distanceMeters", 14f);
            SetFloat(serialized, "cockpitSunShadowCandidate.strength", 0.55f);
            SetFloat(serialized, "cockpitSunShadowCandidate.bias", 0.06f);
            SetFloat(serialized, "cockpitSunShadowCandidate.normalBias", 0.30f);
            SetFloat(serialized, "cockpitSunShadowCandidate.nearPlaneMeters", 0.20f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        private static void IntegrateEnvironmentPrefab(PhotorealAerialReflectionProfile profile)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                ProductionEnvironmentRoot environment = root.GetComponent<ProductionEnvironmentRoot>();
                if (environment == null)
                    throw new InvalidOperationException("ProductionEnvironmentRoot contract is missing.");

                Transform foundation = EnsureUniqueDirectChild(root.transform, FoundationObjectName);
                foundation.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                foundation.localScale = Vector3.one;
                Transform probeTransform = EnsureUniqueDirectChild(foundation, AirportProbeObjectName);
                probeTransform.SetLocalPositionAndRotation(profile.AirportProbeLocalPosition, Quaternion.identity);
                probeTransform.localScale = Vector3.one;
                ReflectionProbe probe = probeTransform.GetComponent<ReflectionProbe>();
                if (probe == null) probe = probeTransform.gameObject.AddComponent<ReflectionProbe>();
                foreach (ReflectionProbe duplicate in probeTransform.GetComponents<ReflectionProbe>().Skip(1).ToArray())
                    UnityEngine.Object.DestroyImmediate(duplicate);
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = profile.AirportLocalReflection;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.cullingMask = 0;
                probe.resolution = profile.AirportProbeResolution;
                probe.size = profile.AirportProbeSize;
                probe.center = Vector3.zero;
                probe.blendDistance = profile.AirportProbeBlendDistance;
                probe.intensity = profile.AirportProbeIntensity;
                probe.importance = 2;
                probe.boxProjection = profile.AirportProbeBoxProjection;
                probe.hdr = true;
                probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
                probe.backgroundColor = profile.LowerHemisphereGroundColor;
                GameObjectUtility.SetStaticEditorFlags(
                    probeTransform.gameObject, StaticEditorFlags.ReflectionProbeStatic);

                AirportCoreBenchmarkRoot benchmark = root.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
                if (benchmark == null)
                    throw new InvalidOperationException("AirportCoreBenchmarkRoot is missing.");
                int heroConsumers = 0;
                heroConsumers += ConfigureHeroConsumers(benchmark.executiveTerminal);
                heroConsumers += ConfigureHeroConsumers(benchmark.type2HangarRow);
                heroConsumers += ConfigureHeroConsumers(benchmark.fuelServiceContext);
                heroConsumers += ConfigureHeroConsumers(benchmark.authoredApronProps);
                if (benchmark.vegetationHeroCluster != null)
                {
                    foreach (Renderer renderer in benchmark.vegetationHeroCluster.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        renderer.probeAnchor = null;
                        EditorUtility.SetDirty(renderer);
                    }
                }
                if (heroConsumers == 0)
                    throw new InvalidOperationException("No existing airport hero renderer accepted the bounded probe.");
                if (foundation.GetComponentsInChildren<Camera>(true).Length != 0)
                    throw new InvalidOperationException("Reflection foundation may not contain a camera.");
                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void IntegrateAircraftPrefab()
        {
            Material sourceWindow = LoadRequired<Material>(SourceWindowMaterialPath);
            Material sourceInstrument = LoadRequired<Material>(SourceInstrumentGlassMaterialPath);
            Material window = LoadRequired<Material>(WindowMaterialPath);
            Material instrument = LoadRequired<Material>(InstrumentGlassMaterialPath);
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                if (rig == null || rig.CenterOfGravityReference == null || rig.AircraftVisualRoot == null)
                    throw new InvalidOperationException("Authored aircraft CG/visual references are missing.");
                QuestFlightLab.Aircraft.ProductionAircraftLightingController lighting =
                    root.GetComponentInChildren<QuestFlightLab.Aircraft.ProductionAircraftLightingController>(true);
                if (lighting == null)
                    throw new InvalidOperationException("Authored aircraft lighting controller is missing.");
                SerializedObject lightingSettings = new SerializedObject(lighting);
                SetObject(lightingSettings, "stableCockpitShader",
                    LoadRequired<Shader>(CockpitStableInteriorShaderPath));
                SetFloat(lightingSettings, "staticDepthStrength",
                    QuestFlightLab.Aircraft.ProductionAircraftLightingController.DefaultStaticDepthStrength);
                SetFloat(lightingSettings, "directionalCabinShadowStrength",
                    QuestFlightLab.Aircraft.ProductionAircraftLightingController.DefaultDirectionalCabinShadowStrength);
                lightingSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(lighting);
                if (rig.CenterOfGravityReference.parent != rig.transform ||
                    rig.CenterOfGravityReference.localPosition.sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException("Reflection anchor must be the existing aircraft CG reference.");
                Transform cockpit = FindDescendant(rig.AircraftVisualRoot, CockpitRootName);
                if (cockpit == null)
                    throw new InvalidOperationException("Authored cockpit root is missing.");
                foreach (Transform item in cockpit.GetComponentsInChildren<Transform>(true))
                    item.gameObject.layer = 0;

                Transform rejectedShadow = FindDescendant(
                    rig.AircraftVisualRoot, RejectedCockpitSunOcclusionObjectName);
                if (rejectedShadow != null)
                    UnityEngine.Object.DestroyImmediate(rejectedShadow.gameObject);

                int windowSlots = 0;
                int instrumentSlots = 0;
                int activeInstrumentSlots = 0;
                int exteriorWindowRenderers = 0;
                Renderer[] renderers = rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    renderer.probeAnchor = rig.CenterOfGravityReference;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    Material[] materials = renderer.sharedMaterials;
                    bool exteriorWindow = string.Equals(renderer.name, ExteriorWindowRendererName,
                        StringComparison.Ordinal);
                    bool instrumentCover = string.Equals(renderer.name, InstrumentCoverRendererName,
                        StringComparison.Ordinal);
                    for (int index = 0; index < materials.Length; index++)
                    {
                        if (exteriorWindow)
                        {
                            materials[index] = window;
                            windowSlots++;
                        }
                        else if (instrumentCover)
                        {
                            materials[index] = instrument;
                            instrumentSlots++;
                        }
                        else if (materials[index] == sourceWindow)
                        {
                            materials[index] = instrument;
                            instrumentSlots++;
                        }
                        else if (materials[index] == window)
                        {
                            windowSlots++;
                        }
                        else if (materials[index] == sourceInstrument)
                        {
                            materials[index] = instrument;
                            instrumentSlots++;
                        }
                        else if (materials[index] == instrument)
                        {
                            instrumentSlots++;
                        }
                    }
                    renderer.sharedMaterials = materials;
                    // The imported GlassPartNew shell duplicates the actual MI
                    // instrument cover. Keep its deterministic binding for source
                    // traceability, but do not spend a second transparent draw on it.
                    // The authoritative MI cover remains enabled and reflective.
                    if (instrumentCover)
                        renderer.enabled = false;
                    if (exteriorWindow)
                    {
                        renderer.enabled = true;
                        Mesh windowMesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (windowMesh == null || windowMesh.bounds.size.x < 1.0f ||
                            windowMesh.bounds.size.y < 0.5f || windowMesh.bounds.size.z < 2.0f)
                            throw new InvalidOperationException(
                                "The authoritative exterior cockpit glazing mesh has implausible bounds.");
                        exteriorWindowRenderers++;
                    }
                    if (renderer.enabled)
                        activeInstrumentSlots += materials.Count(material => material == instrument);
                    bool cockpitOwned = renderer.transform == cockpit ||
                                        renderer.transform.IsChildOf(cockpit);
                    bool transparentGlass = renderer.sharedMaterials.Any(material =>
                        material == window || material == instrument);
                    // The worn candidate proved that a tiny cockpit-only realtime
                    // shadow map creates unstable overlapping triangle silhouettes in
                    // stereo. Cockpit depth is therefore static/material-authored; no
                    // cockpit renderer participates in realtime shadowing.
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    EditorUtility.SetDirty(renderer);
                }

                QuestFlightLab.Aircraft.AircraftVisibilityProfileController visibility =
                    root.GetComponentInChildren<QuestFlightLab.Aircraft.AircraftVisibilityProfileController>(true);
                if (visibility == null)
                    throw new InvalidOperationException("Authored aircraft visibility profile is missing.");
                SerializedObject visibilitySettings = new SerializedObject(visibility);
                Renderer exteriorWindowRenderer = renderers.Single(renderer =>
                    string.Equals(renderer.name, ExteriorWindowRendererName, StringComparison.Ordinal));
                RewriteRendererArray(visibilitySettings, "cockpitOccludingExteriorRenderers",
                    renderer => renderer != exteriorWindowRenderer, null);
                RewriteRendererArray(visibilitySettings, "interiorRenderers",
                    renderer => true, null);
                visibilitySettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(visibility);
                if (windowSlots != 1 || instrumentSlots != 2 || activeInstrumentSlots != 1 ||
                    exteriorWindowRenderers != 1)
                    throw new InvalidOperationException(
                        "Expected one full-cabin exterior window slot, two serialized/one active " +
                        "instrument-glass slots, " +
                        $"and one enabled exterior glazing renderer; found {windowSlots}/" +
                        $"{instrumentSlots}/{activeInstrumentSlots}/{exteriorWindowRenderers}.");
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RewriteRendererArray(
            SerializedObject serialized,
            string propertyName,
            Func<Renderer, bool> keep,
            Renderer append)
        {
            SerializedProperty property = RequiredProperty(serialized, propertyName);
            List<Renderer> values = new List<Renderer>();
            for (int index = 0; index < property.arraySize; index++)
            {
                Renderer renderer = property.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
                if (renderer != null && keep(renderer) && !values.Contains(renderer)) values.Add(renderer);
            }
            if (append != null && !values.Contains(append)) values.Add(append);
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        private static void RemoveRejectedCockpitShadowSun()
        {
            UnityEngine.Object tagManager = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset").Single();
            SerializedObject tagSettings = new SerializedObject(tagManager);
            SerializedProperty layers = tagSettings.FindProperty("layers");
            if (layers == null || layers.arraySize <= CockpitShadowLayer)
                throw new InvalidOperationException("Unity layer table is incomplete.");
            if (string.Equals(layers.GetArrayElementAtIndex(CockpitShadowLayer).stringValue,
                    CockpitShadowLayerName, StringComparison.Ordinal))
                layers.GetArrayElementAtIndex(CockpitShadowLayer).stringValue = string.Empty;
            tagSettings.ApplyModifiedPropertiesWithoutUndo();

            GameObject root = PrefabUtility.LoadPrefabContents(ProductionWorldPrefabPath);
            try
            {
                Transform mainSunTransform = FindDescendant(root.transform, MainSunName);
                if (mainSunTransform == null)
                    throw new InvalidOperationException("The authored product sun is missing.");
                Light mainSun = mainSunTransform.GetComponent<Light>();
                if (mainSun == null || mainSun.type != LightType.Directional)
                    throw new InvalidOperationException("The authored product sun is not directional.");
                Transform existing = FindDescendant(root.transform, CockpitShadowSunName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                mainSun.cullingMask |= 1 << CockpitShadowLayer;
                EditorUtility.SetDirty(mainSun);
                PrefabUtility.SaveAsPrefabAsset(root, ProductionWorldPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void IntegrateSceneReflection(PhotorealAerialReflectionProfile profile)
        {
            Scene scene = EditorSceneManager.OpenScene(ProductionScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Production scene could not be opened.");
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = profile.GlobalGroundSkyReflection;
            RenderSettings.reflectionIntensity = profile.GlobalReflectionIntensity;
            RenderSettings.reflectionBounces = 1;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Failed to serialize global reflection settings.");
        }

        private static int ConfigureHeroConsumers(Component root)
        {
            if (root == null) return 0;
            return ConfigureHeroConsumers(root.transform);
        }

        private static int ConfigureHeroConsumers(Transform root)
        {
            if (root == null) return 0;
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                renderer.probeAnchor = null;
                EditorUtility.SetDirty(renderer);
                count++;
            }
            return count;
        }

        private static void ValidateRuntimeAssets()
        {
            Cubemap global = ValidateReflectionImport(GlobalReflectionPath);
            Cubemap airportLocal = ValidateReflectionImport(AirportLocalReflectionPath);
            if (global == airportLocal || File.ReadAllBytes(GlobalReflectionPath)
                    .SequenceEqual(File.ReadAllBytes(AirportLocalReflectionPath)))
                throw new InvalidOperationException("Global and airport-local reflections must be distinct.");

            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            if (!profile.TryValidate(out string report))
                throw new InvalidOperationException("Reflection profile failed: " + report);
            if (profile.GlobalGroundSkyReflection != global || profile.AirportLocalReflection != airportLocal)
                throw new InvalidOperationException("Reflection profile cubemap bindings are incomplete.");
            string manifest = File.ReadAllText(ManifestPath);
            foreach (string required in new[]
                     {
                         Sha256(GlobalReflectionPath), Sha256(AirportLocalReflectionPath),
                         Sha256(SourceNearOrthophotoPath), Sha256(SourceMidOrthophotoPath),
                         Sha256(SourceFarOrthophotoPath), "bilinear_direction_to_ground_proxy"
                     })
            {
                if (!manifest.Contains(required, StringComparison.Ordinal))
                    throw new InvalidOperationException("Reflection manifest is stale: " + required);
            }
            Material window = LoadRequired<Material>(WindowMaterialPath);
            Material instrument = LoadRequired<Material>(InstrumentGlassMaterialPath);
            Shader stableInterior = LoadRequired<Shader>(CockpitStableInteriorShaderPath);
            if (window.shader == null || window.shader.name != WindowShaderName || window.passCount != 1 ||
                window.renderQueue != (int)RenderQueue.Transparent + 12)
                throw new InvalidOperationException("Window material is not the bounded single-pass shader.");
            if (instrument.shader == null || instrument.shader.name != InstrumentGlassShaderName ||
                instrument.passCount != 1 || instrument.renderQueue != (int)RenderQueue.Transparent + 16)
                throw new InvalidOperationException("Instrument glass is not the restrained single-pass shader.");
            if (stableInterior.name != CockpitStableInteriorShaderName ||
                stableInterior.passCount != 1)
                throw new InvalidOperationException("Cockpit stable-interior shader is not bounded and single-pass.");
        }

        private static Cubemap ValidateReflectionImport(string path)
        {
            Cubemap cubemap = LoadRequired<Cubemap>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer?.ReadTextureSettings(settings);
            TextureImporterPlatformSettings android = importer != null
                ? importer.GetPlatformTextureSettings("Android")
                : null;
            if (importer == null || cubemap.width != 128 ||
                importer.textureShape != TextureImporterShape.TextureCube ||
                importer.generateCubemap != TextureImporterGenerateCubemap.Cylindrical ||
                settings.cubemapConvolution != TextureImporterCubemapConvolution.Specular ||
                importer.sRGBTexture || !importer.mipmapEnabled || importer.streamingMipmaps ||
                android == null || !android.overridden || android.maxTextureSize != 128 ||
                android.format != TextureImporterFormat.ASTC_HDR_6x6)
                throw new InvalidOperationException("Ground-sky cubemap import contract is incomplete: " + path);
            return cubemap;
        }

        private static void ValidateIntegratedFoundation()
        {
            ValidateRuntimeAssets();
            PhotorealAerialReflectionProfile profile = LoadRequired<PhotorealAerialReflectionProfile>(ProfilePath);
            GameObject environmentAsset = LoadRequired<GameObject>(EnvironmentPrefabPath);
            ReflectionProbe[] probes = environmentAsset.GetComponentsInChildren<ReflectionProbe>(true)
                .Where(item => string.Equals(item.name, AirportProbeObjectName, StringComparison.Ordinal))
                .ToArray();
            if (probes.Length != 1 || probes[0].mode != ReflectionProbeMode.Custom ||
                probes[0].customBakedTexture != profile.AirportLocalReflection ||
                probes[0].refreshMode != ReflectionProbeRefreshMode.ViaScripting ||
                probes[0].cullingMask != 0 || probes[0].resolution != 128 || probes[0].boxProjection ||
                probes[0].GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Serialized airport reflection probe is invalid.");
            if (environmentAsset.GetComponentsInChildren<ReflectionProbe>(true)
                .Any(item => item.mode == ReflectionProbeMode.Realtime))
                throw new InvalidOperationException("Realtime reflection probes are forbidden.");

            GameObject aircraftAsset = LoadRequired<GameObject>(AircraftPrefabPath);
            AircraftReferenceFrameRig rig = aircraftAsset.GetComponent<AircraftReferenceFrameRig>();
            Renderer[] aircraftRenderers = rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
            if (aircraftRenderers.Length == 0 || aircraftRenderers.Any(renderer =>
                    renderer.probeAnchor != rig.CenterOfGravityReference ||
                    renderer.reflectionProbeUsage != ReflectionProbeUsage.BlendProbesAndSkybox))
                throw new InvalidOperationException("Aircraft reflection anchor/blending contract is incomplete.");
            if (aircraftRenderers.SelectMany(renderer => renderer.sharedMaterials).Count(material =>
                    material == LoadRequired<Material>(WindowMaterialPath)) != 1 ||
                aircraftRenderers.SelectMany(renderer => renderer.sharedMaterials).Count(material =>
                    material == LoadRequired<Material>(InstrumentGlassMaterialPath)) != 2)
                throw new InvalidOperationException("Dedicated cockpit glass bindings are incomplete.");
            Renderer[] activeInstrumentCovers = aircraftRenderers.Where(renderer => renderer.enabled &&
                renderer.sharedMaterials.Any(material =>
                    material == LoadRequired<Material>(InstrumentGlassMaterialPath))).ToArray();
            Renderer decorativeInstrumentCover = aircraftRenderers.Single(renderer =>
                string.Equals(renderer.name, InstrumentCoverRendererName,
                    StringComparison.Ordinal));
            if (activeInstrumentCovers.Length != 1 || decorativeInstrumentCover.enabled)
                throw new InvalidOperationException(
                    "Only the authoritative MI instrument cover may consume a transparent draw.");
            Renderer[] exteriorWindowRenderers = aircraftRenderers.Where(renderer =>
                string.Equals(renderer.name, ExteriorWindowRendererName,
                    StringComparison.Ordinal)).ToArray();
            if (exteriorWindowRenderers.Length != 1 || !exteriorWindowRenderers[0].enabled ||
                exteriorWindowRenderers[0].sharedMaterials.Single() !=
                LoadRequired<Material>(WindowMaterialPath))
                throw new InvalidOperationException(
                    "The full-cabin exterior glazing mesh must be uniquely enabled and reflective.");

            Transform cockpit = FindDescendant(rig.AircraftVisualRoot, CockpitRootName);
            if (cockpit == null || cockpit.GetComponentsInChildren<Transform>(true)
                    .Any(item => item.gameObject.layer != 0) ||
                cockpit.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                    renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows))
                throw new InvalidOperationException("Static-only cockpit depth contract is incomplete.");
            Renderer exteriorWindow = aircraftRenderers.Single(renderer =>
                string.Equals(renderer.name, ExteriorWindowRendererName, StringComparison.Ordinal));
            if (FindDescendant(rig.AircraftVisualRoot,
                    RejectedCockpitSunOcclusionObjectName) != null)
                throw new InvalidOperationException("Rejected cockpit sun-occlusion overlay remains active.");
            QuestFlightLab.Aircraft.ProductionAircraftLightingController lighting =
                aircraftAsset.GetComponentInChildren<QuestFlightLab.Aircraft.ProductionAircraftLightingController>(true);
            SerializedObject lightingSettings = new SerializedObject(lighting);
            if (lightingSettings.FindProperty("stableCockpitShader")?.objectReferenceValue !=
                LoadRequired<Shader>(CockpitStableInteriorShaderPath))
                throw new InvalidOperationException("Geometry-bound cockpit depth shader is not serialized.");
            SerializedProperty directionalShade = lightingSettings.FindProperty(
                "directionalCabinShadowStrength");
            if (directionalShade == null || Mathf.Abs(directionalShade.floatValue -
                    QuestFlightLab.Aircraft.ProductionAircraftLightingController
                        .DefaultDirectionalCabinShadowStrength) > 0.0001f)
                throw new InvalidOperationException("Directional cockpit shade is not serialized.");
            QuestFlightLab.Aircraft.AircraftVisibilityProfileController visibility =
                aircraftAsset.GetComponentInChildren<QuestFlightLab.Aircraft.AircraftVisibilityProfileController>(true);
            SerializedObject visibilitySettings = new SerializedObject(visibility);
            Renderer[] cockpitOccluders = ReadRendererArray(visibilitySettings,
                "cockpitOccludingExteriorRenderers");
            Renderer[] interiorRenderers = ReadRendererArray(visibilitySettings, "interiorRenderers");
            if (cockpitOccluders.Contains(exteriorWindow) || interiorRenderers.Any(renderer => renderer == null))
                throw new InvalidOperationException(
                    "Cockpit view would hide glazing or retains a stale renderer reference.");
            GameObject worldAsset = LoadRequired<GameObject>(ProductionWorldPrefabPath);
            Light mainSun = FindDescendant(worldAsset.transform, MainSunName)?.GetComponent<Light>();
            Light cockpitSun = FindDescendant(worldAsset.transform,
                CockpitShadowSunName)?.GetComponent<Light>();
            if (mainSun == null || cockpitSun != null ||
                (mainSun.cullingMask & (1 << CockpitShadowLayer)) == 0)
                throw new InvalidOperationException("Rejected cockpit shadow sun remains active.");

            Scene scene = EditorSceneManager.OpenScene(ProductionScenePath, OpenSceneMode.Single);
            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom ||
                RenderSettings.customReflectionTexture != profile.GlobalGroundSkyReflection ||
                Mathf.Abs(RenderSettings.reflectionIntensity - profile.GlobalReflectionIntensity) > 0.0001f ||
                RenderSettings.reflectionBounces != 1)
                throw new InvalidOperationException("Global ground-sky reflection binding is incomplete.");
            if (scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<ReflectionProbe>(true))
                .Any(item => item.mode == ReflectionProbeMode.Realtime))
                throw new InvalidOperationException("Integrated scene contains a realtime reflection probe.");
        }

        private static Renderer[] ReadRendererArray(SerializedObject serialized, string propertyName)
        {
            SerializedProperty property = RequiredProperty(serialized, propertyName);
            Renderer[] result = new Renderer[property.arraySize];
            for (int index = 0; index < property.arraySize; index++)
                result[index] = property.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
            return result;
        }

        private readonly struct ReflectionAssetPair
        {
            public ReflectionAssetPair(
                Cubemap global,
                Cubemap airportLocal,
                ReflectionStatistics globalStatistics,
                ReflectionStatistics localStatistics)
            {
                Global = global;
                AirportLocal = airportLocal;
                GlobalStatistics = globalStatistics;
                LocalStatistics = localStatistics;
            }

            public Cubemap Global { get; }
            public Cubemap AirportLocal { get; }
            public ReflectionStatistics GlobalStatistics { get; }
            public ReflectionStatistics LocalStatistics { get; }
        }

        private readonly struct ReflectionStatistics
        {
            private ReflectionStatistics(
                Color mean,
                Color standardDeviation,
                float luminanceStandardDeviation)
            {
                Mean = mean;
                StandardDeviation = standardDeviation;
                LuminanceStandardDeviation = luminanceStandardDeviation;
            }

            public Color Mean { get; }
            public Color StandardDeviation { get; }
            public float LuminanceStandardDeviation { get; }

            public static ReflectionStatistics FromLowerHemisphere(Color[] pixels)
            {
                int count = ReflectionEquirectangularWidth * ReflectionEquirectangularHeight / 2;
                if (pixels == null || pixels.Length != count * 2)
                    throw new ArgumentException("Reflection source has unexpected dimensions.", nameof(pixels));
                double r = 0d;
                double g = 0d;
                double b = 0d;
                double luminance = 0d;
                for (int index = 0; index < count; index++)
                {
                    Color pixel = pixels[index];
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    luminance += pixel.r * 0.2126d + pixel.g * 0.7152d + pixel.b * 0.0722d;
                }
                double meanR = r / count;
                double meanG = g / count;
                double meanB = b / count;
                double meanLuminance = luminance / count;
                double varianceR = 0d;
                double varianceG = 0d;
                double varianceB = 0d;
                double varianceLuminance = 0d;
                for (int index = 0; index < count; index++)
                {
                    Color pixel = pixels[index];
                    double luma = pixel.r * 0.2126d + pixel.g * 0.7152d + pixel.b * 0.0722d;
                    varianceR += (pixel.r - meanR) * (pixel.r - meanR);
                    varianceG += (pixel.g - meanG) * (pixel.g - meanG);
                    varianceB += (pixel.b - meanB) * (pixel.b - meanB);
                    varianceLuminance += (luma - meanLuminance) * (luma - meanLuminance);
                }
                return new ReflectionStatistics(
                    new Color((float)meanR, (float)meanG, (float)meanB, 1f),
                    new Color(
                        (float)Math.Sqrt(varianceR / count),
                        (float)Math.Sqrt(varianceG / count),
                        (float)Math.Sqrt(varianceB / count),
                        0f),
                    (float)Math.Sqrt(varianceLuminance / count));
            }
        }

        public sealed class CockpitShadowCandidateScope : IDisposable
        {
            private readonly Light sun;
            private readonly bool originalSunEnabled;
            private readonly LightShadows originalLightShadows;
            private readonly int originalCustomResolution;
            private readonly float originalStrength;
            private readonly float originalBias;
            private readonly float originalNormalBias;
            private readonly float originalNearPlane;
            private readonly ShadowQuality originalQuality;
            private readonly ShadowResolution originalResolution;
            private readonly int originalCascades;
            private readonly float originalDistance;
            private readonly RendererState[] rendererStates;
            private bool disposed;

            internal CockpitShadowCandidateScope(Scene scene, CockpitSunShadowCandidate candidate)
            {
                AircraftReferenceFrameRig[] rigs = scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<AircraftReferenceFrameRig>(true))
                    .ToArray();
                if (rigs.Length != 1)
                    throw new InvalidOperationException("Shadow candidate requires exactly one authored aircraft rig.");
                Transform cockpit = FindDescendant(rigs[0].AircraftVisualRoot, CockpitRootName);
                if (cockpit == null)
                    throw new InvalidOperationException("Authored cockpit root is missing.");
                Light authoredSun = RenderSettings.sun;
                if (authoredSun == null || authoredSun.type != LightType.Directional ||
                    !authoredSun.gameObject.activeInHierarchy)
                {
                    Light[] enabledSuns = scene.GetRootGameObjects()
                        .SelectMany(item => item.GetComponentsInChildren<Light>(true))
                        .Where(item => item.enabled && item.gameObject.activeInHierarchy &&
                                       item.type == LightType.Directional)
                        .ToArray();
                    if (enabledSuns.Length != 1)
                        throw new InvalidOperationException(
                            "Shadow candidate requires one RenderSettings sun or one enabled directional sun.");
                    authoredSun = enabledSuns[0];
                }
                sun = authoredSun;
                originalSunEnabled = sun.enabled;
                originalLightShadows = sun.shadows;
                originalCustomResolution = sun.shadowCustomResolution;
                originalStrength = sun.shadowStrength;
                originalBias = sun.shadowBias;
                originalNormalBias = sun.shadowNormalBias;
                originalNearPlane = sun.shadowNearPlane;
                originalQuality = QualitySettings.shadows;
                originalResolution = QualitySettings.shadowResolution;
                originalCascades = QualitySettings.shadowCascades;
                originalDistance = QualitySettings.shadowDistance;

                Renderer[] renderers = scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
                    .ToArray();
                rendererStates = renderers.Select(renderer => new RendererState(renderer)).ToArray();
                foreach (Renderer renderer in renderers)
                {
                    bool cockpitOwned = renderer.transform == cockpit || renderer.transform.IsChildOf(cockpit);
                    bool transparentGlass = renderer.sharedMaterials.Any(material => material != null &&
                        material.shader != null &&
                        (material.shader.name == WindowShaderName ||
                         material.shader.name == InstrumentGlassShaderName));
                    renderer.shadowCastingMode = cockpitOwned && !transparentGlass
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = cockpitOwned && !transparentGlass;
                }

                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowResolution = ShadowResolution.High;
                QualitySettings.shadowCascades = candidate.CascadeCount;
                QualitySettings.shadowDistance = candidate.DistanceMeters;
                sun.enabled = true;
                sun.shadows = LightShadows.Hard;
                sun.shadowCustomResolution = candidate.MapResolution;
                sun.shadowStrength = candidate.Strength;
                sun.shadowBias = candidate.Bias;
                sun.shadowNormalBias = candidate.NormalBias;
                sun.shadowNearPlane = candidate.NearPlaneMeters;
            }

            public void Dispose()
            {
                if (disposed) return;
                foreach (RendererState state in rendererStates) state.Restore();
                if (sun != null)
                {
                    sun.enabled = originalSunEnabled;
                    sun.shadows = originalLightShadows;
                    sun.shadowCustomResolution = originalCustomResolution;
                    sun.shadowStrength = originalStrength;
                    sun.shadowBias = originalBias;
                    sun.shadowNormalBias = originalNormalBias;
                    sun.shadowNearPlane = originalNearPlane;
                }
                QualitySettings.shadows = originalQuality;
                QualitySettings.shadowResolution = originalResolution;
                QualitySettings.shadowCascades = originalCascades;
                QualitySettings.shadowDistance = originalDistance;
                disposed = true;
            }
        }

        private readonly struct RendererState
        {
            private readonly Renderer renderer;
            private readonly ShadowCastingMode castingMode;
            private readonly bool receiveShadows;

            public RendererState(Renderer renderer)
            {
                this.renderer = renderer;
                castingMode = renderer.shadowCastingMode;
                receiveShadows = renderer.receiveShadows;
            }

            public void Restore()
            {
                if (renderer == null) return;
                renderer.shadowCastingMode = castingMode;
                renderer.receiveShadows = receiveShadows;
            }
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader, string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
                material.name = name;
            }
            return material;
        }

        private static Transform EnsureUniqueDirectChild(Transform parent, string name)
        {
            Transform[] matches = parent.Cast<Transform>()
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Transform result = matches.FirstOrDefault();
            if (result == null)
            {
                result = new GameObject(name).transform;
                result.SetParent(parent, false);
            }
            foreach (Transform duplicate in matches.Skip(1).ToArray())
                UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            return result;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(TextureFolder);
            EnsureFolder(ProfileFolder);
            EnsureFolder(CockpitMaterialFolder);
        }

        private static void EnsureFolder(string path)
        {
            string[] components = path.Split('/');
            string current = components[0];
            for (int index = 1; index < components.Length; index++)
            {
                string next = current + "/" + components[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, components[index]);
                current = next;
            }
        }

        private static void WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return;
            File.WriteAllBytes(path, bytes);
        }

        private static void SetString(SerializedObject serialized, string path, string value) =>
            RequiredProperty(serialized, path).stringValue = value;
        private static void SetBool(SerializedObject serialized, string path, bool value) =>
            RequiredProperty(serialized, path).boolValue = value;
        private static void SetInt(SerializedObject serialized, string path, int value) =>
            RequiredProperty(serialized, path).intValue = value;
        private static void SetFloat(SerializedObject serialized, string path, float value) =>
            RequiredProperty(serialized, path).floatValue = value;
        private static void SetColor(SerializedObject serialized, string path, Color value) =>
            RequiredProperty(serialized, path).colorValue = value;
        private static void SetVector3(SerializedObject serialized, string path, Vector3 value) =>
            RequiredProperty(serialized, path).vector3Value = value;
        private static void SetObject(SerializedObject serialized, string path, UnityEngine.Object value) =>
            RequiredProperty(serialized, path).objectReferenceValue = value;

        private static SerializedProperty RequiredProperty(SerializedObject serialized, string path)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property == null)
                throw new MissingMemberException(serialized.targetObject.GetType().Name, path);
            return property;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException("Required " + typeof(T).Name + " is missing: " + path);
            return asset;
        }
    }
}
#endif
