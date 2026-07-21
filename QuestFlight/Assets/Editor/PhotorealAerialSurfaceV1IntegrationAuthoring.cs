using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline-only compiler that binds the pinned aerial-surface derivatives into the
    /// explicitly authored product prefab. It is never included in a player build.
    /// </summary>
    public static class PhotorealAerialSurfaceV1IntegrationAuthoring
    {
        public const string GroundRoot =
            "Assets/Production/Environment/AerialSurfaceV1/Ground";
        public const string SurfaceRoot =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1";
        public const string BuildingRoot = SurfaceRoot + "/Buildings";
        public const string CanopyRoot = SurfaceRoot + "/Canopy";
        public const string MaterialRoot = SurfaceRoot + "/Materials";
        public const string ImageryManifestPath = GroundRoot + "/KBDU_AerialSurfaceV1_SourceManifest.json";
        public const string LidarManifestPath =
            SurfaceRoot + "/Lidar/KBDU_AerialSurface_ClassificationManifestV1.json";
        public const string BuildingManifestPath =
            BuildingRoot + "/ProductionBuildingSurfaceLayer_Manifest.json";
        public const string CanopyManifestPath =
            CanopyRoot + "/ProductionCanopySurfaceLayer_Manifest.json";
        public const string OwnershipManifestPath =
            SurfaceRoot + "/KBDU_AerialSurface_OwnershipExclusionManifestV1.json";
        public const string BuildingMaskPath =
            BuildingRoot + "/ProductionBuildingSurfaceLayer_FootprintMask_R8.png";
        public const string CanopyGeometryMaskPath =
            CanopyRoot + "/ProductionCanopySurfaceLayer_GeometryOwnership_R8.png";
        public const string CanopyMapHandoffPath =
            CanopyRoot + "/ProductionCanopySurfaceLayer_TerrainMapHandoff_R8.png";
        public const string NormalizedHeightPath =
            SurfaceRoot + "/Lidar/KBDU_AerialSurface_nDSM_R16.png";
        public const string SurfaceClassPath =
            SurfaceRoot + "/Lidar/KBDU_AerialSurface_SurfaceClass_R8.png";
        public const string SemanticShaderPath = MaterialRoot + "/ProductionSemanticSurface.shader";
        public const string BuildingMaterialPath = MaterialRoot + "/ProductionBuildingSurfaceLayer.mat";
        public const string CanopyMaterialPath = MaterialRoot + "/ProductionCanopySurfaceLayer.mat";
        public const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        public const string VegetationPrefabPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Generated/AerialVegetationRoot.prefab";
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string AerialManifestPath =
            "Assets/Production/Environment/AerialSceneryV1/KBDU_AerialSceneryManifestV1.asset";
        public const string CanopyNormalOcclusionPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_CanopyNormalOcclusion_RGBA8.png";
        public const string SurfaceFeaturesPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_SurfaceFeatures_RGBA8.png";
        public const string SurfaceNormalPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_SurfaceNormal_RGBA8.png";
        public const string FacadeAtlasPath =
            "Assets/Production/Environment/AirportCoreV4/Materials/AirportCoreHangarKit_Albedo.png";
        public const string FacadeMraoPath =
            "Assets/Production/Environment/AirportCoreV4/Materials/AirportCoreHangarKit_MRAO.png";
        public const string FacadeNormalPath =
            "Assets/Production/Environment/AirportCoreV4/Materials/AirportCoreHangarKit_Normal.png";
        public const string FacadeReflectionPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/Textures/" +
            "KBDU_GlobalGroundSkyReflection_256.exr";
        public const string NearPath = GroundRoot + "/KBDU_City2020_Near4km_8192.jpg";
        public const string MidNwPath = GroundRoot + "/KBDU_City2020_MidNW_2048.jpg";
        public const string MidNePath = GroundRoot + "/KBDU_City2020_MidNE_2048.jpg";
        public const string MidSwPath = GroundRoot + "/KBDU_City2020_MidSW_2048.jpg";
        public const string MidSePath = GroundRoot + "/KBDU_City2020_MidSE_2048.jpg";
        public const string MidPath = GroundRoot + "/KBDU_City2020_MidBase12km_8192.jpg";
        public const string FarPath = GroundRoot + "/KBDU_City2020_Far24km_4096.jpg";
        public const string BakeUtc = "2026-07-20T18:30:00Z";
        public const string BakeToolVersion = "photoreal_aerial_surface_v1";

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Integrate Semantic Surface")]
        public static void RunInteractive() => Apply();

        public static void RunBatch()
        {
            try
            {
                Apply();
                Debug.Log("[QuestFlight][PhotorealAerialSurfaceV1] SERIALIZED_INTEGRATION_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PhotorealAerialSurfaceNativeMeshAuthoring.CompileAll();
            ConfigureInputs();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Texture2D near = LoadRequired<Texture2D>(NearPath);
            Texture2D midNw = LoadRequired<Texture2D>(MidNwPath);
            Texture2D midNe = LoadRequired<Texture2D>(MidNePath);
            Texture2D midSw = LoadRequired<Texture2D>(MidSwPath);
            Texture2D midSe = LoadRequired<Texture2D>(MidSePath);
            Texture2D mid = LoadRequired<Texture2D>(MidPath);
            Texture2D far = LoadRequired<Texture2D>(FarPath);
            ConfigureMipController(near, midNw, midNe, midSw, midSe, mid, far);
            Texture2D canopyNormal = LoadRequired<Texture2D>(CanopyNormalOcclusionPath);
            Texture2D normalizedHeight = LoadRequired<Texture2D>(NormalizedHeightPath);

            TextAsset imageryManifest = LoadRequired<TextAsset>(ImageryManifestPath);
            TextAsset lidarManifest = LoadRequired<TextAsset>(LidarManifestPath);
            TextAsset buildingManifest = LoadRequired<TextAsset>(BuildingManifestPath);
            TextAsset canopyManifest = LoadRequired<TextAsset>(CanopyManifestPath);
            TextAsset ownershipManifest = LoadRequired<TextAsset>(OwnershipManifestPath);
            ValidatePinnedRecords(imageryManifest.text, buildingManifest.text, canopyManifest.text,
                ownershipManifest.text);
            RuntimeFrameAffine runtimeFrame = ReadRuntimeFrameAffine(lidarManifest.text);

            Material buildingMaterial = CreateOrUpdateMaterial(BuildingMaterialPath, 0f,
                new Color(0.76f, 0.78f, 0.75f, 1f), near, midNw, midNe, midSw, midSe,
                canopyNormal, normalizedHeight, runtimeFrame);
            Material canopyMaterial = CreateOrUpdateMaterial(CanopyMaterialPath, 1f,
                new Color(0.16f, 0.27f, 0.09f, 1f), near, midNw, midNe, midSw, midSe,
                canopyNormal, normalizedHeight, runtimeFrame);

            Material terrainMaterial = LoadRequired<Material>(TerrainMaterialPath);
            ConfigureTerrainMaterial(terrainMaterial, near, midNw, midNe, midSw, midSe, mid, far,
                runtimeFrame);

            GameObject environment = PrefabUtility.LoadPrefabContents(ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot environmentContract =
                    environment.GetComponent<ProductionEnvironmentRoot>();
                if (environmentContract == null)
                    throw new InvalidOperationException("ProductionEnvironmentRoot contract is missing.");
                BindNativeNearTerrain(environmentContract);
                AerialSceneryRoot aerial = environment.GetComponentsInChildren<AerialSceneryRoot>(true).Single();
                AirportCoreBenchmarkRoot airportHero =
                    environment.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true).Single();

                DestroyPreviousRepresentation(aerial);

                Transform bareEarthLayer = CreateLayer(aerial.transform,
                    PhotorealAerialSurfaceRoot.BareEarthLayerName);
                Transform buildingLayer = CreateLayer(aerial.transform,
                    PhotorealAerialSurfaceRoot.BuildingLayerName);
                Transform vegetationRepresentation = CreateLayer(aerial.transform,
                    "ProductionVegetationRepresentation");
                Transform canopyLayer = CreateLayer(vegetationRepresentation,
                    PhotorealAerialSurfaceRoot.CanopyLayerName);
                Transform heroLayer = CreateLayer(vegetationRepresentation,
                    PhotorealAerialSurfaceRoot.HeroLayerName);

                Renderer[] buildingRenderers = InstantiateNativeMeshBatches(
                    PhotorealAerialSurfaceNativeMeshAuthoring.BuildingNativeRoot,
                    buildingLayer, buildingMaterial, "BuildingSurfaceBatch");
                Renderer[] canopyRenderers = InstantiateNativeMeshBatches(
                    PhotorealAerialSurfaceNativeMeshAuthoring.CanopyNativeRoot,
                    canopyLayer, canopyMaterial, "CanopySurfaceBatch");

                aerial.buildingRoot = buildingLayer;
                aerial.vegetationRoot = vegetationRepresentation;

                AerialSceneryManifest aerialManifest = LoadRequired<AerialSceneryManifest>(AerialManifestPath);
                UpdateAerialManifest(aerialManifest, environmentContract, imageryManifest, lidarManifest,
                    buildingManifest, canopyManifest);
                aerial.manifest = aerialManifest;

                int canopyTriangles = CountTriangles(canopyRenderers);
                aerial.bakedVegetationRendererCount = canopyRenderers.Length;
                aerial.bakedVegetationTriangleCount = canopyTriangles;
                aerial.bakedVegetationMaterialCount = canopyRenderers
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null).Distinct().Count();
                aerial.bakeToolVersion = BakeToolVersion;
                aerial.bakeUtc = BakeUtc;

                PhotorealAerialSurfaceRoot surface =
                    aerial.GetComponent<PhotorealAerialSurfaceRoot>() ??
                    aerial.gameObject.AddComponent<PhotorealAerialSurfaceRoot>();
                ConfigureSurfaceContract(surface, environmentContract, airportHero.transform,
                    bareEarthLayer, buildingLayer, canopyLayer, heroLayer, buildingRenderers, canopyRenderers,
                    imageryManifest, lidarManifest, buildingManifest, canopyManifest, ownershipManifest,
                    near, mid, far);

                environmentContract.uniqueMacroAlbedo = mid;
                environmentContract.macroSource =
                    "City of Boulder 2020 9-inch orthogonal imagery, CC0-1.0, deterministic bounded derivatives";
                ProductionEnvironmentBudget budget = environmentContract.CalculateBudget();
                environmentContract.bakedRendererCount = budget.rendererCount;
                environmentContract.bakedTriangleCount = budget.triangleCount;
                environmentContract.bakedMaterialCount = budget.materialCount;
                environmentContract.bakedTextureCount = CountReferencedTextures(environment);
                environmentContract.bakeToolVersion = BakeToolVersion;
                environmentContract.bakeUtc = BakeUtc;
                aerial.bakedRendererCount = budget.rendererCount;
                aerial.bakedSelectedTriangleCount = budget.triangleCount;
                aerial.bakedMaterialCount = budget.materialCount;

                if (!surface.TryValidateContract(out string surfaceReport))
                    throw new InvalidOperationException("Photoreal surface contract failed: " + surfaceReport);
                if (!aerial.TryValidateContract(out string aerialReport))
                    throw new InvalidOperationException("Aerial scenery contract failed: " + aerialReport);
                if (!environmentContract.TryValidateContract(out string environmentReport))
                    throw new InvalidOperationException("Environment contract failed: " + environmentReport);
                if (budget.rendererCount > 75 || budget.triangleCount > 650000 || budget.materialCount > 28)
                    throw new InvalidOperationException(
                        $"Photoreal surface budget failed: {budget.rendererCount}/75 draws, " +
                        $"{budget.triangleCount}/650000 triangles, {budget.materialCount}/28 materials.");

                EditorUtility.SetDirty(terrainMaterial);
                EditorUtility.SetDirty(aerialManifest);
                EditorUtility.SetDirty(aerial);
                EditorUtility.SetDirty(surface);
                EditorUtility.SetDirty(environmentContract);
                PrefabUtility.SaveAsPrefabAsset(environment, ProductionEnvironmentRoot.PrefabAssetPath);
                Debug.Log($"[QuestFlight][PhotorealAerialSurfaceV1] {surfaceReport}; {aerialReport}; " +
                          environmentReport);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureInputs()
        {
            ConfigureTexture(NearPath, 8192, TextureImporterFormat.ASTC_8x8, true, -0.15f, 8, true);
            foreach (string path in new[] { MidNwPath, MidNePath, MidSwPath, MidSePath })
                ConfigureTexture(path, 2048, TextureImporterFormat.ASTC_6x6, true, -0.15f, 8, true);
            ConfigureTexture(MidPath, 8192, TextureImporterFormat.ASTC_10x10, true, 0f, 8, true);
            ConfigureTexture(FarPath, 4096, TextureImporterFormat.ASTC_10x10, true, 0f, 8, true);
            ConfigureTexture(BuildingMaskPath, 1024, TextureImporterFormat.R8, false, 0f, 1, false, false);
            ConfigureTexture(CanopyGeometryMaskPath, 1024, TextureImporterFormat.R8, false, 0f, 1, false, false);
            ConfigureTexture(CanopyMapHandoffPath, 1024, TextureImporterFormat.R8, false, 0f, 1, false, false);
            ConfigureTexture(NormalizedHeightPath, 1024, TextureImporterFormat.R16, false, 0f, 1,
                false, false, true, FilterMode.Point);
            ConfigureTexture(SurfaceClassPath, 1024, TextureImporterFormat.R8, false, 0f, 1,
                false, false, true, FilterMode.Point);
        }

        private static void ConfigureTexture(
            string path,
            int maximumSize,
            TextureImporterFormat androidFormat,
            bool mipmaps,
            float mipBias,
            int anisotropy,
            bool streamingMipmaps = false,
            bool sRgb = true,
            bool forceUncompressed = false,
            FilterMode? filterModeOverride = null)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer is missing: " + path);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            FilterMode filterMode = filterModeOverride ?? (mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear);
            bool changed = importer.textureType != TextureImporterType.Default ||
                           importer.sRGBTexture != sRgb ||
                           importer.alphaSource != TextureImporterAlphaSource.None ||
                           importer.mipmapEnabled != mipmaps ||
                           importer.streamingMipmaps != (mipmaps && streamingMipmaps) ||
                           Mathf.Abs(importer.mipMapBias - mipBias) > 0.0001f ||
                           importer.wrapMode != TextureWrapMode.Clamp ||
                           importer.filterMode != filterMode ||
                           importer.anisoLevel != anisotropy ||
                           importer.npotScale != TextureImporterNPOTScale.None ||
                           importer.maxTextureSize != maximumSize ||
                           !android.overridden || android.maxTextureSize != maximumSize ||
                           android.format != androidFormat || android.compressionQuality != 100 ||
                           (forceUncompressed &&
                            (importer.textureCompression != TextureImporterCompression.Uncompressed ||
                             android.textureCompression != TextureImporterCompression.Uncompressed));
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = sRgb;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = mipmaps;
            importer.streamingMipmaps = mipmaps && streamingMipmaps;
            importer.mipMapBias = mipBias;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = filterMode;
            importer.anisoLevel = anisotropy;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maximumSize;
            if (forceUncompressed)
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = maximumSize;
            android.format = androidFormat;
            android.compressionQuality = 100;
            if (forceUncompressed)
                android.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(android);
            if (changed) importer.SaveAndReimport();
        }

        private static void ConfigureMipController(
            Texture2D near,
            Texture2D midNw,
            Texture2D midNe,
            Texture2D midSw,
            Texture2D midSe,
            Texture2D mid,
            Texture2D far)
        {
            GameObject aircraft = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = aircraft.GetComponent<AircraftReferenceFrameRig>();
                if (rig == null) throw new InvalidOperationException("Aircraft reference-frame rig is missing.");
                GeospatialMipController controller = aircraft.GetComponent<GeospatialMipController>() ??
                                                     aircraft.AddComponent<GeospatialMipController>();
                foreach (GeospatialMipController duplicate in
                         aircraft.GetComponents<GeospatialMipController>().Skip(1).ToArray())
                    UnityEngine.Object.DestroyImmediate(duplicate);
                controller.trackedRig = rig;
                controller.near4Km = near;
                controller.midNorthWest = midNw;
                controller.midNorthEast = midNe;
                controller.midSouthWest = midSw;
                controller.midSouthEast = midSe;
                controller.midBase12Km = mid;
                controller.far24Km = far;
                controller.predictionSeconds = 2f;
                controller.preloadMarginMeters = 650f;
                controller.projectedTexelSafetyFactor = 0.72f;
                controller.logMipChanges = true;
                controller.authoredContractVersion = GeospatialMipController.ContractVersion;
                if (!controller.TryValidateContract(out string report))
                    throw new InvalidOperationException("Geospatial mip controller failed: " + report);
                EditorUtility.SetDirty(controller);
                PrefabUtility.SaveAsPrefabAsset(aircraft, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(aircraft);
            }
        }

        private static Material CreateOrUpdateMaterial(
            string path,
            float semanticMode,
            Color sideTint,
            Texture2D near,
            Texture2D midNw,
            Texture2D midNe,
            Texture2D midSw,
            Texture2D midSe,
            Texture2D canopyNormal,
            Texture2D normalizedHeight,
            RuntimeFrameAffine runtimeFrame)
        {
            Shader shader = LoadRequired<Shader>(SemanticShaderPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.enableInstancing = true;
            material.SetFloat("_SemanticMode", semanticMode);
            material.SetTexture("_NearAlbedo", near);
            material.SetTexture("_RecoveryMidNWAlbedo", midNw);
            material.SetTexture("_RecoveryMidNEAlbedo", midNe);
            material.SetTexture("_RecoveryMidSWAlbedo", midSw);
            material.SetTexture("_RecoveryMidSEAlbedo", midSe);
            material.SetTexture("_FacadeAtlas", LoadRequired<Texture2D>(FacadeAtlasPath));
            material.SetTexture("_FacadeMRAO", LoadRequired<Texture2D>(FacadeMraoPath));
            material.SetTexture("_FacadeNormal", LoadRequired<Texture2D>(FacadeNormalPath));
            material.SetTexture("_FacadeReflectionCube", LoadRequired<Cubemap>(FacadeReflectionPath));
            material.SetTexture("_CanopyNormalOcclusion", canopyNormal);
            material.SetTexture("_NormalizedSurfaceHeight", normalizedHeight);
            material.SetVector("_NearWorldMinInvSize", new Vector4(-2000f, -2000f, 1f / 4000f, 1f / 4000f));
            material.SetVector("_RecoveryMidNWWorldMinInvSize", new Vector4(-4032f, -32f, 1f / 4064f, 1f / 4064f));
            material.SetVector("_RecoveryMidNEWorldMinInvSize", new Vector4(-32f, -32f, 1f / 4064f, 1f / 4064f));
            material.SetVector("_RecoveryMidSWWorldMinInvSize", new Vector4(-4032f, -4032f, 1f / 4064f, 1f / 4064f));
            material.SetVector("_RecoveryMidSEWorldMinInvSize", new Vector4(-32f, -4032f, 1f / 4064f, 1f / 4064f));
            material.SetVector("_CanopyWorldMinInvSize", new Vector4(-2048f, -2048f, 1f / 4096f, 1f / 4096f));
            material.SetVector("_CanopyProductToSourceRow0", runtimeFrame.productToSourceRow0);
            material.SetVector("_CanopyProductToSourceRow1", runtimeFrame.productToSourceRow1);
            material.SetVector("_CanopySourceToProductRow0", runtimeFrame.sourceToProductNormalRow0);
            material.SetVector("_CanopySourceToProductRow1", runtimeFrame.sourceToProductNormalRow1);
            material.SetVector("_NearBlendStartEnd", new Vector4(1800f, 1980f, 0f, 0f));
            material.SetColor("_TopTint", Color.white);
            material.SetColor("_SideTint", semanticMode < 0.5f
                ? new Color(0.92f, 0.92f, 0.90f, 1f)
                : sideTint);
            material.SetFloat("_TopRoughness", semanticMode < 0.5f ? 0.78f : 0.88f);
            material.SetFloat("_SideRoughness", semanticMode < 0.5f ? 0.82f : 0.92f);
            material.SetFloat("_CanopyNormalStrength", 0.42f);
            material.SetColor("_CanopySideTint", new Color(0.22f, 0.30f, 0.14f, 1f));
            material.SetFloat("_AmbientFloor", 0.22f);
            material.SetFloat("_DiagnosticMode", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTerrainMaterial(
            Material material,
            Texture2D near,
            Texture2D midNw,
            Texture2D midNe,
            Texture2D midSw,
            Texture2D midSe,
            Texture2D mid,
            Texture2D far,
            RuntimeFrameAffine runtimeFrame)
        {
            material.SetTexture("_NearAlbedo", near);
            material.SetTexture("_RecoveryMidNWAlbedo", midNw);
            material.SetTexture("_RecoveryMidNEAlbedo", midNe);
            material.SetTexture("_RecoveryMidSWAlbedo", midSw);
            material.SetTexture("_RecoveryMidSEAlbedo", midSe);
            material.SetTexture("_MidAlbedo", mid);
            material.SetTexture("_FarAlbedo", far);
            material.SetTexture("_AerialCanopyHandoffMask", LoadRequired<Texture2D>(CanopyMapHandoffPath));
            material.SetVector("_AerialCanopyProductToSourceRow0", runtimeFrame.productToSourceRow0);
            material.SetVector("_AerialCanopyProductToSourceRow1", runtimeFrame.productToSourceRow1);
            material.SetVector("_AerialCanopySourceToProductRow0", runtimeFrame.sourceToProductNormalRow0);
            material.SetVector("_AerialCanopySourceToProductRow1", runtimeFrame.sourceToProductNormalRow1);
            EditorUtility.SetDirty(material);
        }

        private static void DestroyPreviousRepresentation(AerialSceneryRoot aerial)
        {
            foreach (Transform value in new[] { aerial.buildingRoot, aerial.vegetationRoot })
                if (value != null && value != aerial.transform)
                    UnityEngine.Object.DestroyImmediate(value.gameObject);
            foreach (string name in new[]
                     {
                         PhotorealAerialSurfaceRoot.BareEarthLayerName,
                         PhotorealAerialSurfaceRoot.BuildingLayerName,
                         "ProductionVegetationRepresentation",
                         PhotorealAerialSurfaceRoot.CanopyLayerName,
                         PhotorealAerialSurfaceRoot.HeroLayerName
                     })
            {
                Transform[] matches = aerial.GetComponentsInChildren<Transform>(true)
                    .Where(item => item != aerial.transform && string.Equals(item.name, name, StringComparison.Ordinal))
                    .ToArray();
                foreach (Transform match in matches)
                    if (match != null) UnityEngine.Object.DestroyImmediate(match.gameObject);
            }
        }

        private static Transform CreateLayer(Transform parent, string name)
        {
            GameObject layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            return layer.transform;
        }

        private static GameObject InstantiateNested(GameObject prefab, Transform parent, string name)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null) throw new InvalidOperationException("Failed to instantiate " + prefab.name);
            instance.name = name;
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static Renderer[] InstantiateNativeMeshBatches(
            string folder,
            Transform parent,
            Material material,
            string label,
            bool allowEmpty = false)
        {
            List<Renderer> result = new List<Renderer>();
            string[] paths = AssetDatabase.FindAssets("t:Mesh", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), folder,
                                   StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (paths.Length == 0 && !allowEmpty)
                throw new InvalidOperationException("No native " + label + " mesh batches were found.");
            foreach (string path in paths)
            {
                Mesh mesh = LoadRequired<Mesh>(path);
                GameObject instance = new GameObject(mesh.name);
                instance.transform.SetParent(parent, false);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                MeshFilter filter = instance.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = true;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                EditorUtility.SetDirty(filter);
                EditorUtility.SetDirty(renderer);
                result.Add(renderer);
            }
            return result.ToArray();
        }

        private static void BindNativeNearTerrain(ProductionEnvironmentRoot environment)
        {
            if (environment.nearTerrainMeshes == null)
                throw new InvalidOperationException("Authored near-terrain mesh references are missing.");
            MeshFilter near = environment.nearTerrainMeshes.Single(item =>
                item != null && item.name == "Terrain_Near4km_USGS");
            Transform transform = near.transform;
            if (transform.localPosition != Vector3.zero ||
                transform.localRotation != Quaternion.identity ||
                transform.localScale != Vector3.one)
                throw new InvalidOperationException(
                    "Terrain_Near4km_USGS must remain identity-transformed product geometry.");

            Mesh mesh = LoadRequired<Mesh>(
                PhotorealAerialSurfaceNativeMeshAuthoring.TerrainNativePath);
            if (mesh.GetIndexCount(0) / 3u >
                PhotorealAerialSurfaceNativeMeshAuthoring.MaximumTerrainTriangleCount)
                throw new InvalidOperationException("Native product-AEQD terrain exceeds 92,000 triangles.");
            near.sharedMesh = mesh;
            EditorUtility.SetDirty(near);
        }

        private static void UpdateAerialManifest(
            AerialSceneryManifest manifest,
            ProductionEnvironmentRoot environment,
            TextAsset imagery,
            TextAsset lidar,
            TextAsset buildings,
            TextAsset canopy)
        {
            manifest.label = "KBDU photoreal aerial semantic surface V1 — not for navigation";
            manifest.sourceLicense =
                "City of Boulder 2020 imagery CC0-1.0; USGS 3DEP public domain; OSM derivatives ODbL-1.0";
            manifest.generatedUtc = BakeUtc;
            manifest.lidarSourceManifest = lidar;
            manifest.vegetationSourceManifest = canopy;
            manifest.buildingSourceManifest = buildings;
            manifest.lidarPipelineSha256 = Sha256(LidarManifestPath);
            manifest.vegetationPipelineSha256 = Sha256(CanopyManifestPath);
            manifest.buildingPipelineSha256 = Sha256(BuildingManifestPath);
            manifest.surfaceFeatures = LoadRequired<Texture2D>(SurfaceFeaturesPath);
            manifest.surfaceNormals = LoadRequired<Texture2D>(SurfaceNormalPath);
            manifest.canopyNormals = LoadRequired<Texture2D>(CanopyNormalOcclusionPath);
            manifest.canopyOcclusion = manifest.canopyNormals;
            manifest.canopyGeometryHandoff = LoadRequired<Texture2D>(CanopyMapHandoffPath);
            manifest.airportPatchMesh = environment.nearTerrainMeshes.Single(item =>
                item.name == "Terrain_AirportPatch_USGS").sharedMesh;
            manifest.nearTerrainMesh = environment.nearTerrainMeshes.Single(item =>
                item.name == "Terrain_Near4km_USGS").sharedMesh;
            manifest.midTerrainMesh = environment.midTerrainMesh.sharedMesh;
            manifest.canopyComponentCount = ReadInt(canopy.text, "component_count");
            manifest.midGroveClusterCount = manifest.canopyComponentCount;
            manifest.nearTreeInstanceCount = 22;
            manifest.representedBuildingCount = ReadInt(buildings.text, "owned_source_footprint_count");
            manifest.orthophotoOnlyBuildingCount = Math.Max(0, 27679 - manifest.representedBuildingCount);
            manifest.buildingFootprintInflationMaximum = 1f +
                Mathf.Max(0f, ReadFloat(buildings.text, "maximum_allowed_inflation_ratio", 0f));
            EditorUtility.SetDirty(manifest);
        }

        private static void ConfigureSurfaceContract(
            PhotorealAerialSurfaceRoot surface,
            ProductionEnvironmentRoot environment,
            Transform airportHero,
            Transform bareEarth,
            Transform buildings,
            Transform canopy,
            Transform heroes,
            Renderer[] buildingRenderers,
            Renderer[] canopyRenderers,
            TextAsset imageryManifest,
            TextAsset lidarManifest,
            TextAsset buildingManifest,
            TextAsset canopyManifest,
            TextAsset ownershipManifest,
            Texture2D near,
            Texture2D mid,
            Texture2D far)
        {
            surface.productionBareEarthTerrain = bareEarth;
            surface.productionBuildingSurfaceLayer = buildings;
            surface.productionCanopySurfaceLayer = canopy;
            surface.productionHeroObjectLayer = heroes;
            surface.authoritativeBareEarthMeshes = environment.nearTerrainMeshes
                .Concat(new[] { environment.midTerrainMesh, environment.farTerrainMesh }).ToArray();
            surface.buildingSurfaceRenderers = buildingRenderers;
            surface.canopySurfaceRenderers = canopyRenderers;
            surface.airportHeroSourceRoot = airportHero;
            surface.imagerySourceManifest = imageryManifest;
            surface.lidarSurfaceManifest = lidarManifest;
            surface.buildingSurfaceManifest = buildingManifest;
            surface.canopySurfaceManifest = canopyManifest;
            surface.ownershipExclusionManifest = ownershipManifest;
            surface.nearOrthophoto = near;
            surface.midOrthophoto = mid;
            surface.farOrthophoto = far;
            surface.normalizedSurfaceHeight = LoadRequired<Texture2D>(NormalizedHeightPath);
            surface.surfaceClassification = LoadRequired<Texture2D>(SurfaceClassPath);
            surface.buildingOwnershipMask = LoadRequired<Texture2D>(BuildingMaskPath);
            surface.canopyOwnershipMask = LoadRequired<Texture2D>(CanopyGeometryMaskPath);
            surface.nearMetersPerTexel = ReadFloat(imageryManifest.text, "near_meters_per_texel");
            surface.patternMetersPerTexel = ReadFloat(imageryManifest.text, "pattern_meters_per_texel");
            surface.farMetersPerTexel = ReadFloat(imageryManifest.text, "far_meters_per_texel");
            surface.nearBuildingSourceCount = ReadBandInt(buildingManifest.text, "0-2km",
                "source_footprint_count");
            surface.midBuildingSourceCount = ReadBandInt(buildingManifest.text, "2-4km",
                "source_footprint_count");
            surface.representedBuildingCount = ReadInt(buildingManifest.text, "owned_source_footprint_count");
            surface.nearBuildingCoveragePercent = ReadBandFloat(buildingManifest.text, "0-2km",
                "coverage_fraction") * 100f;
            surface.midBuildingCoveragePercent = ReadBandFloat(buildingManifest.text, "2-4km",
                "coverage_fraction") * 100f;
            surface.maximumBuildingFootprintInflationPercent =
                ReadFloat(buildingManifest.text, "maximum_allowed_inflation_ratio", 0f) * 100f;
            surface.representedCanopyComponentCount = ReadInt(canopyManifest.text, "component_count");
            surface.representedCanopyAreaPercentOfSource =
                ReadFloat(canopyManifest.text, "combined_representation_area_agreement_percent", 100f);
            surface.medianCanopyHeightAgreementPercent =
                ReadFloat(canopyManifest.text, "median_height_agreement_percent", 100f);
            AerialSceneryManifest aerialManifest = LoadRequired<AerialSceneryManifest>(AerialManifestPath);
            surface.terrainMeanAbsoluteErrorMeters = aerialManifest.terrainMeanAbsoluteErrorMeters;
            surface.terrainP95ErrorMeters = aerialManifest.terrainP95ErrorMeters;
            surface.terrainMaximumErrorOutsideDeclaredOutliersMeters = aerialManifest.terrainMaximumErrorMeters;
            surface.operationalSurfaceFiveCentimeterToleranceEstablished = false;
            surface.bakedBuildingDrawCount = buildingRenderers.Length;
            surface.bakedBuildingTriangleCount = CountTriangles(buildingRenderers);
            surface.bakedCanopyDrawCount = canopyRenderers.Length;
            surface.bakedCanopyTriangleCount = CountTriangles(canopyRenderers);
            surface.bakedTextureResidentBytes =
                ReadLong(imageryManifest.text, "projected_scene_android_texture_bytes");
            surface.bakeToolVersion = BakeToolVersion;
            surface.bakeUtc = BakeUtc;
        }

        private static void ValidatePinnedRecords(
            string imagery,
            string buildings,
            string canopy,
            string ownership)
        {
            RequireContains(imagery, "CC0-1.0", "imagery license");
            RequireContains(imagery, "22b7c93a74394696824258f807f345ba", "City imagery item identity");
            RequireContains(buildings, "ODbL-1.0", "building license");
            RequireContains(buildings, "0-2km", "near building coverage band");
            RequireContains(buildings, "2-4km", "mid building coverage band");
            RequireContains(canopy,
                "semantic_rounded_groves_0_1500m_over_continuous_terrain_canopy",
                "semantic rounded-grove canopy representation");
            RequireContains(canopy, "\"mesh_batch_count\": 4",
                "four bounded raised-canopy mesh batches");
            RequireContains(canopy, "REJECTED_BLOCK_AND_CLIFF_SILHOUETTES",
                "rejected raised-canopy candidate decision");
            RequireContains(canopy, "\"vertical_boundary_skirts\": false",
                "canopy boundary-skirt policy");
            RequireContains(ownership, "ProductionBuildingSurfaceLayer", "building ownership");
            RequireContains(ownership, "ProductionCanopySurfaceLayer", "canopy ownership");
            RequireContains(ownership, "ProductionHeroObjectLayer", "hero ownership");
            if (ReadFloat(imagery, "near_meters_per_texel") > 0.75f ||
                ReadFloat(imagery, "pattern_meters_per_texel") > 2f ||
                ReadFloat(imagery, "far_meters_per_texel") > 6f)
                throw new InvalidOperationException("Imagery texel-density contract is not met.");
            if (ReadBandFloat(buildings, "0-2km", "coverage_fraction") < 0.99f ||
                ReadBandFloat(buildings, "2-4km", "coverage_fraction") < 0.98f)
                throw new InvalidOperationException("Building coverage contract is not met.");
            if (ReadLong(imagery, "projected_scene_android_texture_bytes") >
                ReadLong(imagery, "hard_ceiling_bytes"))
                throw new InvalidOperationException("Projected Android texture residency exceeds the pinned ceiling.");
        }

        private static int CountTriangles(IEnumerable<Renderer> renderers)
        {
            long triangles = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int index = 0; index < mesh.subMeshCount; index++)
                    triangles += (long)mesh.GetIndexCount(index) / 3L;
            }
            return checked((int)triangles);
        }

        private static int CountReferencedTextures(GameObject root)
        {
            HashSet<Texture> textures = new HashSet<Texture>();
            IEnumerable<Material> materials = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && IsAuthoredActive(renderer.transform))
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null);
            materials = materials.Concat(root.GetComponentsInChildren<AerialVegetationBatchRenderer>(true)
                .Where(batch => batch.enabled && IsAuthoredActive(batch.transform) && batch.SharedMaterial != null)
                .Select(batch => batch.SharedMaterial));
            foreach (Material material in materials.Distinct())
            foreach (string property in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(property);
                if (texture != null) textures.Add(texture);
            }
            return textures.Count;
        }

        private static bool IsAuthoredActive(Transform value)
        {
            for (Transform current = value; current != null; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return value;
        }

        private static string Sha256(string path)
        {
            using SHA256 algorithm = SHA256.Create();
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            return string.Concat(algorithm.ComputeHash(File.ReadAllBytes(fullPath))
                .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static int ReadInt(string json, string key) => checked((int)ReadLong(json, key));

        private static long ReadLong(string json, string key)
        {
            Match match = Regex.Match(json,
                "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(?<value>-?[0-9]+)",
                RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidOperationException("JSON numeric key is missing: " + key);
            return long.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }

        private static float ReadFloat(string json, string key, float fallback = float.NaN)
        {
            Match match = Regex.Match(json,
                "\\\"" + Regex.Escape(key) +
                "\\\"\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                if (!float.IsNaN(fallback)) return fallback;
                throw new InvalidOperationException("JSON numeric key is missing: " + key);
            }
            return float.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }

        private static RuntimeFrameAffine ReadRuntimeFrameAffine(string json)
        {
            float maximumResidual = ReadFloat(json, "position_maximum_residual_meters");
            float maximumAllowed = ReadFloat(json, "position_maximum_allowed_residual_meters");
            if (!float.IsFinite(maximumResidual) || !float.IsFinite(maximumAllowed) ||
                maximumResidual < 0f || maximumAllowed <= 0f || maximumResidual > maximumAllowed ||
                maximumAllowed > 0.0051f)
                throw new InvalidOperationException("Runtime lidar-frame affine exceeds its pinned residual contract.");

            RuntimeFrameAffine result = new RuntimeFrameAffine
            {
                productToSourceRow0 = new Vector4(
                    ReadFloat(json, "product_to_utm_x_from_product_x"),
                    ReadFloat(json, "product_to_utm_x_from_product_z"),
                    ReadFloat(json, "product_to_utm_x_offset_meters"), 0f),
                productToSourceRow1 = new Vector4(
                    ReadFloat(json, "product_to_utm_z_from_product_x"),
                    ReadFloat(json, "product_to_utm_z_from_product_z"),
                    ReadFloat(json, "product_to_utm_z_offset_meters"), 0f),
                sourceToProductNormalRow0 = new Vector4(
                    ReadFloat(json, "utm_normal_to_product_x_from_utm_x"),
                    ReadFloat(json, "utm_normal_to_product_x_from_utm_z"), 0f, 0f),
                sourceToProductNormalRow1 = new Vector4(
                    ReadFloat(json, "utm_normal_to_product_z_from_utm_x"),
                    ReadFloat(json, "utm_normal_to_product_z_from_utm_z"), 0f, 0f)
            };
            float determinant = result.productToSourceRow0.x * result.productToSourceRow1.y -
                                result.productToSourceRow0.y * result.productToSourceRow1.x;
            float normalLength0 = new Vector2(result.sourceToProductNormalRow0.x,
                result.sourceToProductNormalRow0.y).magnitude;
            float normalLength1 = new Vector2(result.sourceToProductNormalRow1.x,
                result.sourceToProductNormalRow1.y).magnitude;
            if (!float.IsFinite(determinant) || Mathf.Abs(determinant - 0.99921f) > 0.002f ||
                Mathf.Abs(normalLength0 - 1f) > 0.0001f || Mathf.Abs(normalLength1 - 1f) > 0.0001f)
                throw new InvalidOperationException("Runtime lidar-frame affine is degenerate or non-normalized.");
            return result;
        }

        private struct RuntimeFrameAffine
        {
            public Vector4 productToSourceRow0;
            public Vector4 productToSourceRow1;
            public Vector4 sourceToProductNormalRow0;
            public Vector4 sourceToProductNormalRow1;
        }

        private static int ReadBandInt(string json, string band, string key) =>
            checked((int)ReadBandLong(json, band, key));

        private static long ReadBandLong(string json, string band, string key)
        {
            string section = JsonSectionAfter(json, band);
            return ReadLong(section, key);
        }

        private static float ReadBandFloat(string json, string band, string key)
        {
            string section = JsonSectionAfter(json, band);
            return ReadFloat(section, key);
        }

        private static string JsonSectionAfter(string json, string anchor)
        {
            int searchStart = json.IndexOf("\"per_band\"", StringComparison.Ordinal);
            if (searchStart < 0) searchStart = 0;
            int index = json.IndexOf("\"" + anchor + "\"", searchStart, StringComparison.Ordinal);
            if (index < 0) throw new InvalidOperationException("JSON section is missing: " + anchor);
            int end = Math.Min(json.Length, index + 1200);
            return json.Substring(index, end - index);
        }

        private static void RequireContains(string value, string expected, string label)
        {
            if (value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(label + " is missing from its pinned manifest.");
        }
    }
}
