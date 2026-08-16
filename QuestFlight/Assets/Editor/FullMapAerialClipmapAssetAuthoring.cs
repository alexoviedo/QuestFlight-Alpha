using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>Compiles offline aerial pages into Android-native AssetBundles and authors one runtime owner.</summary>
    public static class FullMapAerialClipmapAssetAuthoring
    {
        private const string GeneratedRoot = "Assets/Generated/FullMapAerialClipmap";
        private const string StreamingRoot = "Assets/StreamingAssets/QuestFlightAerialClipmap";
        private const string PrefabPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        private const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";

        [Serializable]
        private sealed class PageRecord
        {
            public string asset_name;
            public string tier;
            public int column;
            public int row;
            public int[] output_pixels;
            public string bundle_name;
        }

        [Serializable]
        private sealed class TierRecord
        {
            public int output_pixels;
            public int gutter_pixels;
            public float meters_per_texel;
            public int runtime_grid_size;
            public int bundle_region_cells;
            public int columns;
            public int rows;
            public float tile_inner_size_meters;
            public float grid_min_x;
            public float grid_min_z;
        }

        [Serializable]
        private sealed class TierContainer
        {
            public TierRecord high;
            public TierRecord medium;
        }

        [Serializable]
        private sealed class RuntimeRecord
        {
            public float prediction_seconds;
            public float high_blend_width_meters;
            public float medium_blend_width_meters;
        }

        [Serializable]
        private sealed class Manifest
        {
            public TierContainer tiers;
            public RuntimeRecord runtime;
            public PageRecord[] pages;
        }

        public static void BuildAndroidBundlesAndAuthorProductBatch()
        {
            try
            {
                BuildAndroidBundlesAndAuthorProduct();
                Debug.Log("[QuestFlight][AerialClipmap] PASS bundle compilation and product authoring.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static void BuildAndroidBundlesAndAuthorProduct()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("Unity project root is unavailable.");
            string generatedAbsolute = Path.Combine(projectRoot, GeneratedRoot.Replace('/', Path.DirectorySeparatorChar));
            string manifestAbsolute = Path.Combine(generatedAbsolute, "clipmap_manifest.json");
            if (!File.Exists(manifestAbsolute))
                throw new FileNotFoundException("Clipmap manifest was not generated.", manifestAbsolute);

            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestAbsolute));
            if (manifest?.pages == null || manifest.pages.Length == 0)
                throw new InvalidOperationException("Clipmap manifest contains no pages.");
            List<(PageRecord record, string assetPath)> pages = manifest.pages
                .Select(record =>
                {
                    string imagePath = Path.Combine(generatedAbsolute, record.tier,
                        record.asset_name + ".jpg");
                    if (!File.Exists(imagePath))
                        throw new FileNotFoundException("Manifest page is missing.", imagePath);
                    return (record, ToAssetPath(projectRoot, imagePath));
                })
                .OrderBy(item => item.record.tier, StringComparer.Ordinal)
                .ThenBy(item => item.record.row)
                .ThenBy(item => item.record.column)
                .ToList();
            if (pages.Count == 0) throw new InvalidOperationException("No generated clipmap pages were found.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach ((PageRecord record, string assetPath) in pages)
                    ConfigurePageImporter(record, assetPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();

            string streamingAbsolute = Path.Combine(projectRoot, StreamingRoot.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(streamingAbsolute)) Directory.Delete(streamingAbsolute, true);
            Directory.CreateDirectory(streamingAbsolute);

            AssetBundleBuild[] builds = pages
                .GroupBy(item => item.record.bundle_name, StringComparer.Ordinal)
                .Select(group => new AssetBundleBuild
                {
                    assetBundleName = group.Key,
                    assetNames = group.Select(item => item.assetPath).ToArray(),
                    addressableNames = group.Select(item => item.record.asset_name).ToArray()
                })
                .OrderBy(build => build.assetBundleName, StringComparer.Ordinal)
                .ToArray();
            AssetBundleManifest built = BuildPipeline.BuildAssetBundles(
                streamingAbsolute,
                builds,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.Android);
            if (built == null) throw new InvalidOperationException("Android AssetBundle compilation failed.");
            File.Copy(manifestAbsolute, Path.Combine(streamingAbsolute, "clipmap_manifest.json"), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            AuthorProductComponent(manifest);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigurePageImporter(PageRecord record, string assetPath)
        {
            if (record.output_pixels == null || record.output_pixels.Length != 2 ||
                record.output_pixels[0] <= 0 || record.output_pixels[0] != record.output_pixels[1])
            {
                throw new InvalidOperationException("Clipmap page dimensions are invalid: " + assetPath);
            }
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture import failed: " + assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = record.output_pixels[0];
            android.format = TextureImporterFormat.ASTC_10x10;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            AssetDatabase.WriteImportSettingsIfDirty(assetPath);
        }

        private static void AuthorProductComponent(Manifest manifest)
        {
            if (manifest?.tiers?.high == null || manifest.tiers.medium == null)
                throw new InvalidOperationException("Runtime tier manifest is incomplete.");
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                if (rig == null) throw new InvalidOperationException("Aircraft reference-frame authority is missing.");
                AircraftCenteredAerialClipmapController controller =
                    root.GetComponent<AircraftCenteredAerialClipmapController>() ??
                    root.AddComponent<AircraftCenteredAerialClipmapController>();
                controller.trackedRig = rig;
                controller.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
                controller.buildingRoofMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    FullMapBuildingClipmapAssetAuthoring.MaterialPath);
                if (controller.buildingRoofMaterial == null)
                    throw new InvalidOperationException("Projected-roof building material is missing.");
                controller.legacyFallbackMipController = root.GetComponent<GeospatialMipController>();
                if (controller.legacyFallbackMipController == null)
                    throw new InvalidOperationException("Fixed-pyramid startup fallback is missing.");
                controller.predictionSeconds = manifest.runtime.prediction_seconds;
                ApplyTier(controller.high, "high", manifest.tiers.high,
                    manifest.runtime.high_blend_width_meters);
                ApplyTier(controller.medium, "medium", manifest.tiers.medium,
                    manifest.runtime.medium_blend_width_meters);
                controller.authoredContractVersion = AircraftCenteredAerialClipmapController.ContractVersion;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ApplyTier(
            AircraftCenteredAerialClipmapController.TierConfiguration destination,
            string name,
            TierRecord source,
            float blendWidth)
        {
            destination.name = name;
            destination.columns = source.columns;
            destination.rows = source.rows;
            destination.pagePixels = source.output_pixels;
            destination.gutterPixels = source.gutter_pixels;
            destination.tileInnerMeters = source.tile_inner_size_meters;
            destination.gridMinimumX = source.grid_min_x;
            destination.gridMinimumZ = source.grid_min_z;
            destination.bundleRegionCells = source.bundle_region_cells;
            destination.blendWidthMeters = blendWidth;
        }

        private static string ToAssetPath(string projectRoot, string absolutePath)
        {
            string relative = Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
            if (!relative.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Generated image is outside Assets: " + absolutePath);
            return relative;
        }
    }
}
