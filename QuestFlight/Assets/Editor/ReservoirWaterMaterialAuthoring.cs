using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline-only material binding for the immutable Boulder Reservoir surface.
    /// It never creates or repairs scene objects and requires an explicitly selected sky cubemap.
    /// </summary>
    public static class ReservoirWaterMaterialAuthoring
    {
        public const string ShaderName = "QuestFlightLab/Production Stable Water";
        public const string MaterialPath =
            "Assets/Production/Environment/Materials/Production_BoulderReservoir_Opaque.mat";
        public const string BroadNormalPath =
            "Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_BroadWave_NM.png";
        public const string RippleNormalPath =
            "Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_Ripple_NM.png";
        public const string PackedMaskPath =
            "Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_DepthShoreRoughness.png";

        [MenuItem("QuestFlight/Product/Reservoir/Bind Selected Sky Cubemap")]
        public static void BindSelectedSkyCubemap()
        {
            Cubemap selectedSky = Selection.activeObject as Cubemap;
            if (selectedSky == null)
                throw new InvalidOperationException(
                    "Select the integrated HDR sky Cubemap asset before binding Boulder Reservoir reflections.");
            ConfigureProductionMaterial(selectedSky, ResolveSelectedSkyRotation(selectedSky), 0.9f);
            Debug.Log(
                $"[QuestFlight][ReservoirWater] Bound selected sky cubemap '{selectedSky.name}' to the opaque reservoir material.");
        }

        [MenuItem("QuestFlight/Product/Reservoir/Bind Selected Sky Cubemap", true)]
        private static bool ValidateBindSelectedSkyCubemap() => Selection.activeObject is Cubemap;

        /// <summary>
        /// Batch entry point. Usage:
        /// -executeMethod QuestFlight.Editor.ReservoirWaterMaterialAuthoring.RunBatch
        /// -reservoirSkyCubemapPath Assets/.../SelectedSky.cubemap
        /// [-reservoirSkyRotationDegrees 0] [-reservoirReflectionIntensity 0.9]
        /// </summary>
        public static void RunBatch()
        {
            try
            {
                string cubemapPath = RequireCommandLineValue("-reservoirSkyCubemapPath");
                float rotation = ParseOptionalFloat("-reservoirSkyRotationDegrees", 0f);
                float intensity = ParseOptionalFloat("-reservoirReflectionIntensity", 0.9f);
                Cubemap selectedSky = AssetDatabase.LoadAssetAtPath<Cubemap>(cubemapPath);
                if (selectedSky == null)
                    throw new InvalidOperationException("Selected sky Cubemap asset is missing: " + cubemapPath);
                ConfigureProductionMaterial(selectedSky, rotation, intensity);
                Debug.Log("[QuestFlight][ReservoirWater] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static Material ConfigureProductionMaterial(
            Cubemap selectedSky,
            float skyRotationDegrees,
            float reflectionIntensity)
        {
            ConfigureTextureImporter(BroadNormalPath, normalMap: true, TextureWrapMode.Repeat);
            ConfigureTextureImporter(RippleNormalPath, normalMap: true, TextureWrapMode.Repeat);
            ConfigureTextureImporter(PackedMaskPath, normalMap: false, TextureWrapMode.Clamp);
            Material material = LoadRequired<Material>(MaterialPath);
            ConfigureMaterialState(material, selectedSky, skyRotationDegrees, reflectionIntensity);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return material;
        }

        public static void ConfigureMaterialState(
            Material material,
            Cubemap selectedSky,
            float skyRotationDegrees,
            float reflectionIntensity)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (selectedSky == null)
                throw new ArgumentNullException(
                    nameof(selectedSky),
                    "The selected HDR sky cubemap must be injected; realtime probes and unrelated fallbacks are not allowed.");
            if (!float.IsFinite(skyRotationDegrees))
                throw new ArgumentOutOfRangeException(nameof(skyRotationDegrees));
            if (!float.IsFinite(reflectionIntensity) || reflectionIntensity < 0f || reflectionIntensity > 2f)
                throw new ArgumentOutOfRangeException(nameof(reflectionIntensity));

            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Quest reservoir shader is unavailable: " + ShaderName);
            material.shader = shader;
            material.name = "Production_BoulderReservoir_Opaque";
            material.SetTexture("_BroadNormal", LoadRequired<Texture2D>(BroadNormalPath));
            material.SetTexture("_RippleNormal", LoadRequired<Texture2D>(RippleNormalPath));
            material.SetTexture("_DepthShoreRoughness", LoadRequired<Texture2D>(PackedMaskPath));
            material.SetTexture("_EnvironmentCube", selectedSky);

            material.SetColor("_Color", new Color(0.045f, 0.17f, 0.24f, 1f));
            material.SetColor("_ShoreColor", new Color(0.19f, 0.38f, 0.38f, 1f));
            material.SetColor("_ShoreBandColor", new Color(0.62f, 0.67f, 0.52f, 1f));
            material.SetColor("_HorizonColor", new Color(0.82f, 0.90f, 0.94f, 1f));
            material.SetVector("_BasinCenterExtent", new Vector4(249.5625f, 4307.6875f, 1062.5625f, 857.1875f));

            float radians = skyRotationDegrees * Mathf.Deg2Rad;
            material.SetVector(
                "_EnvironmentRotationSinCos",
                new Vector4(Mathf.Sin(radians), Mathf.Cos(radians), 0f, 0f));
            material.SetVector("_BroadNormalScroll", new Vector4(0.0011f, 0.00045f, 0f, 0f));
            material.SetVector("_RippleNormalScroll", new Vector4(-0.0022f, 0.0031f, 0f, 0f));

            material.SetFloat("_BroadNormalMeters", 240f);
            material.SetFloat("_RippleNormalMeters", 72f);
            material.SetFloat("_BroadNormalStrength", 0.48f);
            material.SetFloat("_FineNormalStrength", 0.30f);
            material.SetFloat("_RippleStrength", 0.68f);
            material.SetFloat("_DepthContrast", 1f);
            material.SetFloat("_SurfaceVariation", 0.08f);
            material.SetFloat("_ShoreBandStrength", 0.16f);
            material.SetFloat("_Roughness", 0.46f);
            material.SetFloat("_RoughnessMin", 0.22f);
            material.SetFloat("_RoughnessMax", 0.58f);
            material.SetFloat("_EnvironmentMaxMip", 6f);
            material.SetFloat("_EnvironmentIntensity", reflectionIntensity);
            material.SetFloat("_ReflectionFloor", 0.10f);
            material.SetFloat("_FresnelF0", 0.02f);
            material.SetFloat("_GlintStrength", 0.42f);
            material.SetFloat("_ZWrite", 1f);

            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry + 5;
            material.enableInstancing = true;
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureTextureImporter(string path, bool normalMap, TextureWrapMode wrapMode)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Reservoir texture importer is missing: " + path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.wrapMode = wrapMode;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 512;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 512,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 75
            });
            importer.SaveAndReimport();
        }

        private static float ResolveSelectedSkyRotation(Cubemap selectedSky)
        {
            Material skybox = RenderSettings.skybox;
            if (skybox != null && skybox.HasProperty("_Tex") && skybox.GetTexture("_Tex") == selectedSky &&
                skybox.HasProperty("_Rotation"))
                return skybox.GetFloat("_Rotation");
            return 0f;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"Required {typeof(T).Name} asset is missing: {path}");
            return asset;
        }

        private static string RequireCommandLineValue(string key)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, key);
            if (index < 0 || index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
                throw new InvalidOperationException("Missing required command-line argument: " + key);
            return arguments[index + 1];
        }

        private static float ParseOptionalFloat(string key, float fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, key);
            if (index < 0) return fallback;
            if (index + 1 >= arguments.Length ||
                !float.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw new InvalidOperationException("Invalid numeric command-line argument: " + key);
            return value;
        }
    }
}
