using System;
using System.IO;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline-only binding for the full-map terrain-owned water mask.
    /// It creates no runtime hierarchy, renderer, material, camera, or script.
    /// </summary>
    public static class FullMapWaterReflectionAuthoring
    {
        public const string MaskPath =
            "Assets/Production/Environment/PhotorealTerrainV1/Water/KBDU_FullMapWaterMask.png";
        public const string ManifestPath =
            "Assets/Production/Environment/PhotorealTerrainV1/Water/KBDU_FullMapWaterReflectionManifest.json";
        public const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        public const int ReflectedFeatureCount = 1464;
        public const float MinimumEffectiveAreaSquareMeters = 250f;

        [MenuItem("QuestFlight/Product/Water/Bind Full-Map Terrain Reflection")]
        public static void RunInteractive()
        {
            BindAndValidate();
        }

        public static void RunBatch()
        {
            try
            {
                BindAndValidate();
                Debug.Log("[QuestFlight][FullMapWaterReflection] SUCCESS");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void BindAndValidate()
        {
            ConfigureMaskImporter();
            Texture2D mask = LoadRequired<Texture2D>(MaskPath);
            TextAsset manifest = LoadRequired<TextAsset>(ManifestPath);
            Material terrain = LoadRequired<Material>(TerrainMaterialPath);
            if (!terrain.HasProperty("_FullMapWaterMask"))
            {
                throw new InvalidOperationException(
                    "The shared terrain shader does not expose the full-map water contract.");
            }

            ConfigureMaterialState(terrain, mask);
            EditorUtility.SetDirty(terrain);

            GameObject prefab = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot root =
                    prefab.GetComponent<ProductionEnvironmentRoot>();
                if (root == null)
                {
                    throw new InvalidOperationException(
                        "Production environment prefab has no root contract.");
                }

                ConfigureEnvironmentState(root, mask, manifest);
                EditorUtility.SetDirty(root);
                PrefabUtility.SaveAsPrefabAsset(
                    prefab,
                    ProductionEnvironmentRoot.PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (terrain.GetTexture("_FullMapWaterMask") != mask)
            {
                throw new InvalidOperationException(
                    "Full-map water mask did not persist on the shared terrain material.");
            }
            Debug.Log(
                $"[QuestFlight][FullMapWaterReflection] Bound {ReflectedFeatureCount} " +
                "water features to the existing opaque terrain pass; added draws=0, renderers=0.");
        }

        public static void ConfigureMaterialState(
            Material terrain,
            Texture2D mask)
        {
            if (terrain == null || mask == null)
            {
                throw new ArgumentNullException(
                    terrain == null ? nameof(terrain) : nameof(mask));
            }
            terrain.SetTexture("_FullMapWaterMask", mask);
            terrain.SetVector(
                "_FullMapWaterWorldMinInvSize",
                new Vector4(-12000f, -12000f, 1f / 24000f, 1f / 24000f));
            terrain.SetFloat("_FullMapWaterReflectionStrength", 0.095f);
            terrain.SetFloat("_FullMapWaterFresnelStrength", 0.14f);
            terrain.SetFloat("_FullMapWaterSunGlintStrength", 0.24f);
            terrain.SetFloat("_FullMapWaterCrestGlintStrength", 0.12f);
            terrain.SetFloat("_FullMapWaterDistortionStrength", 0.022f);
            terrain.SetFloat("_FullMapWaterRoughness", 0.52f);
        }

        public static void ConfigureEnvironmentState(
            ProductionEnvironmentRoot root,
            Texture2D mask,
            TextAsset manifest)
        {
            root.waterPresentationMode =
                ProductionWaterPresentationMode.OrthophotoMaskedReflection;
            root.fullMapWaterMask = mask;
            root.fullMapWaterReflectionManifest = manifest;
            root.reflectedWaterFeatureCount = ReflectedFeatureCount;
            root.minimumReflectedWaterAreaSquareMeters =
                MinimumEffectiveAreaSquareMeters;
        }

        private static void ConfigureMaskImporter()
        {
            AssetDatabase.ImportAsset(
                MaskPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            TextureImporter importer =
                AssetImporter.GetAtPath(MaskPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Full-map water mask importer is unavailable.");
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = TextureImporterFormat.ASTC_12x12;
            android.compressionQuality = 100;
            android.allowsAlphaSplitting = false;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    $"Required full-map water asset is missing: {path}");
            }
            return asset;
        }
    }
}
