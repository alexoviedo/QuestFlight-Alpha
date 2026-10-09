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
            public string mode;
            public PageSource source;
            public TierContainer tiers;
            public RuntimeRecord runtime;
            public PageRecord[] pages;
        }

        [Serializable] private sealed class PageSource { public Item item; }
        [Serializable] private sealed class Item { public string id; }
        [Serializable] private sealed class ReviewedSource { public OfflineInputs offline_derivatives; }
        [Serializable] private sealed class OfflineInputs { public string page_manifest_sha256; }
        private static string RequireReviewedManifest(string directory)
        {
            string project=Directory.GetParent(Application.dataPath).FullName;
            string generated=Path.Combine(project,GeneratedRoot,"clipmap_manifest.json");
            string source=File.Exists(generated)?generated:Path.Combine(directory,"clipmap_manifest.json");
            if (!File.Exists(source)) throw new InvalidOperationException("Reviewed full aerial page manifest is missing.");
            var reviewed=JsonUtility.FromJson<ReviewedSource>(File.ReadAllText(
                "Assets/Production/Environment/FullMapAerialClipmapV1/SOURCE_MANIFEST.json"));
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(source));
            if (reviewed?.offline_derivatives?.page_manifest_sha256 != AuthoringBundleVerifier.Sha256(source) ||
                manifest?.mode != "full" || manifest.pages?.Length != 1873 ||
                manifest.source?.item?.id != "22b7c93a74394696824258f807f345ba")
                throw new InvalidOperationException("Full aerial source manifest differs from the reviewed current product.");
            return source;
        }
        public static void VerifyExistingAndroidBundlesBatch()
        {
            try
            {
                string directory=Path.Combine(Directory.GetParent(Application.dataPath).FullName,StreamingRoot);
                string source=RequireReviewedManifest(directory);
                var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(source));
                var builds=manifest.pages.GroupBy(page=>page.bundle_name).Select(group=>new AssetBundleBuild {
                    assetBundleName=group.Key,addressableNames=group.Select(page=>page.asset_name).ToArray() }).ToArray();
                var sizes=manifest.pages.ToDictionary(page=>page.asset_name.ToLowerInvariant(),page=>new Vector2Int(page.output_pixels[0],page.output_pixels[1]));
                var evidence=AuthoringBundleVerifier.Verify(directory,builds,"qf_aerial_",true,sizes);
                var report=AuthoringBundleVerifier.WriteReport(directory,evidence,BuildTarget.Android,source,
                    "../QuestFlight/Assets/Editor/FullMapAerialClipmapAssetAuthoring.cs","../QuestFlight/Assets/Editor/AuthoringBundleVerifier.cs");
                report.operation="verify_existing_loaded_artifacts";
                File.WriteAllText(Path.Combine(directory,"bundle_verification.json"),JsonUtility.ToJson(report,true)+"\n");
                EnsureVerifiedBuildArtifacts();
                Debug.Log("[QuestFlight][Aerial] All existing Android bundles/assets loaded and verified; no source texture regeneration claimed.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) { Debug.LogException(error);EditorApplication.Exit(1); }
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

            RequireReviewedManifest(Path.Combine(projectRoot,StreamingRoot));
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
            string stagingParent=Path.Combine(projectRoot,"Library","QuestFlightBundleStaging");
            Directory.CreateDirectory(stagingParent);
            string staging=Path.Combine(stagingParent,Path.GetFileName(streamingAbsolute)+".staging-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);

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
                staging,
                builds,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.Android);
            if (built == null) throw new InvalidOperationException("Android AssetBundle compilation failed.");
            var dimensions=pages.ToDictionary(item=>item.record.asset_name.ToLowerInvariant(),
                item=>new Vector2Int(item.record.output_pixels[0],item.record.output_pixels[1]));
            var evidence=AuthoringBundleVerifier.Verify(staging,builds,"qf_aerial_",true,dimensions);
            File.Copy(manifestAbsolute, Path.Combine(staging, "clipmap_manifest.json"), true);
            AuthoringBundleVerifier.WriteReport(staging,evidence,BuildTarget.Android,manifestAbsolute,
                "../QuestFlight/Assets/Editor/FullMapAerialClipmapAssetAuthoring.cs",
                "../QuestFlight/Assets/Editor/AuthoringBundleVerifier.cs");
            AuthoringBundleVerifier.Activate(staging,streamingAbsolute,stagingParent);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            AuthorProductComponent(manifest);
            AssetDatabase.SaveAssets();
        }

        public static void EnsureVerifiedBuildArtifacts()
        {
            string directory=Path.Combine(Directory.GetParent(Application.dataPath).FullName,StreamingRoot);
            string source=RequireReviewedManifest(directory);
            string reportPath=Path.Combine(directory,"bundle_verification.json");
            if (!File.Exists(source) || !File.Exists(reportPath))
                throw new InvalidOperationException("Aerial source pages and byte-bound Unity Android verification are required before packaging.");
            var report=JsonUtility.FromJson<AuthoringBundleVerifier.Report>(File.ReadAllText(reportPath));
            if (report == null || !report.verified || report.build_target != BuildTarget.Android.ToString() ||
                report.unity_version != Application.unityVersion || report.manifest_sha256 != AuthoringBundleVerifier.Sha256(source) ||
                report.compiler_inputs == null || report.compiler_inputs.Length != 2)
                throw new InvalidOperationException("Aerial verification identity is stale.");
            foreach (string path in new[] { "QuestFlight/Assets/Editor/FullMapAerialClipmapAssetAuthoring.cs", "QuestFlight/Assets/Editor/AuthoringBundleVerifier.cs" })
                if (report.compiler_inputs.Count(record=>record.path==path && record.sha256==AuthoringBundleVerifier.Sha256("../"+path))!=1)
                    throw new InvalidOperationException("Aerial compiler verification is stale.");
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(source));
            var builds=manifest.pages.GroupBy(page=>page.bundle_name).Select(group=>new AssetBundleBuild {
                assetBundleName=group.Key, addressableNames=group.Select(page=>page.asset_name).ToArray() }).ToArray();
            var sizes=manifest.pages.ToDictionary(page=>page.asset_name.ToLowerInvariant(),page=>new Vector2Int(page.output_pixels[0],page.output_pixels[1]));
            var actual=AuthoringBundleVerifier.Verify(directory,builds,"qf_aerial_",true,sizes);
            if (report.bundles == null || report.bundles.Length != actual.Length || actual.Any(current=>
                report.bundles.Count(old=>old.name==current.name && old.sha256==current.sha256 && old.bytes==current.bytes && old.crc==current.crc)!=1))
                throw new InvalidOperationException("Aerial bundles changed after verification.");
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
