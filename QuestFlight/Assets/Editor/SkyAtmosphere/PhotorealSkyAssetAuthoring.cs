using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.EditorTools
{
    /// <summary>
    /// Configures the selected HDRI as a Quest-safe cubemap and authors its skybox material.
    /// It deliberately does not modify any scene, prefab, RenderSettings, or light.
    /// </summary>
    public static class PhotorealSkyAssetAuthoring
    {
        public const string HdriPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr";
        public const string MaterialPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_Skybox.mat";

        [MenuItem("QuestFlight/Environment/Configure Photoreal Sky Assets")]
        public static void ConfigureFromMenu()
        {
            Configure();
        }

        public static void ConfigureBatch()
        {
            try
            {
                Configure();
                Debug.Log("[QuestFlight][SkyAtmosphere] PASS: HDR cubemap and material configured without scene mutation.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Configure()
        {
            if (!File.Exists(HdriPath))
                throw new FileNotFoundException("The optimized HDR derivative is missing", HdriPath);

            AssetDatabase.ImportAsset(HdriPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(HdriPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("The selected HDRI did not resolve to a TextureImporter.");

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.cubemapConvolution = TextureImporterCubemapConvolution.None;
            importer.SetTextureSettings(textureSettings);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_HDR_6x6;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();

            Cubemap cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(HdriPath);
            if (cubemap == null)
                throw new InvalidOperationException("The HDRI importer did not produce a cubemap.");

            Shader shader = Shader.Find("Skybox/Cubemap");
            if (shader == null)
                throw new InvalidOperationException("Unity's Skybox/Cubemap shader is unavailable.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "Kloppenheim03_PureSky_Skybox" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_Tex", cubemap);
            material.SetFloat("_Exposure", 1.0f);
            material.SetFloat("_Rotation", 0.0f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Validate(importer, material, cubemap);
        }

        private static void Validate(TextureImporter importer, Material material, Cubemap cubemap)
        {
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            if (importer.textureShape != TextureImporterShape.TextureCube ||
                importer.generateCubemap != TextureImporterGenerateCubemap.Cylindrical ||
                textureSettings.cubemapConvolution != TextureImporterCubemapConvolution.None ||
                !importer.mipmapEnabled || importer.sRGBTexture)
                throw new InvalidOperationException("HDR cubemap import contract is incomplete.");
            if (!android.overridden || android.maxTextureSize != 1024 ||
                android.format != TextureImporterFormat.ASTC_HDR_6x6)
                throw new InvalidOperationException("Android HDR ASTC override is incomplete.");
            if (material.shader == null || material.shader.name != "Skybox/Cubemap" ||
                material.GetTexture("_Tex") != cubemap)
                throw new InvalidOperationException("Skybox material does not reference the imported cubemap.");
        }
    }
}
