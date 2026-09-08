using System;
using System.IO;
using UnityEditor;

namespace QuestFlightLab.Editor.Tests
{
    public static class RemasterTerrainCompressionSmoke
    {
        public static void Run()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_REMASTER_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REMASTER_ROOT required");
            const string path = "Assets/Generated/RemasterCompressionSmoke/KBDU_2020_high_016_016.jpg";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Copy(Path.Combine(root, "terrain_recovery/pages/high/KBDU_2020_high_016_016.jpg"), path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = TextureImporterFormat.ASTC_10x10;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            string output = Path.Combine(root, "terrain_recovery/compressed_smoke");
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] {
                new AssetBundleBuild { assetBundleName = "remaster_compression_smoke", assetNames = new[] { path } }
            }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.Android);
            if (manifest == null) throw new InvalidOperationException("Android compression smoke failed");
            OfflineRemasterIntegrateTraffic.ValidateAndroidPlayerScripts();
        }
    }
}
