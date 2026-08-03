using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline-only integration for the serialized Aerial Scenery V1 product
    /// representation. It never constructs or repairs scenery at runtime.
    /// </summary>
    public static class AerialSceneryV1IntegrationAuthoring
    {
        public const string EnvironmentPrefabPath = ProductionEnvironmentRoot.PrefabAssetPath;
        public const string ManifestAssetPath =
            "Assets/Production/Environment/AerialSceneryV1/KBDU_AerialSceneryManifestV1.asset";
        public const string LidarRoot = "Assets/Production/Environment/AerialSceneryV1/Lidar";
        public const string VegetationRoot = "Assets/Production/Environment/AerialSceneryV1/Vegetation";
        public const string BuildingRoot = "Assets/Production/Environment/AerialSceneryV1/Buildings";
        public const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        public const string LidarManifestPath = LidarRoot + "/KBDU_LidarManifestV1.json";
        public const string TerrainErrorPath = LidarRoot + "/KBDU_Lidar_NearTerrain_ErrorReport.json";
        public const string TerrainConformanceReportPath =
            AirportPatchLidarConformanceAuthoring.OutputReportPath;
        public const string TerrainCoordinateConversionReportPath =
            LidarTerrainUnityAssetAuthoring.OutputReportPath;
        public const string NearTerrainSourcePath = LidarRoot + "/KBDU_Lidar_NearTerrain_Fixed.obj";
        public const string NearTerrainPath = LidarTerrainUnityAssetAuthoring.OutputMeshPath;
        public const string SurfaceFeaturesPath = LidarRoot + "/KBDU_Lidar_SurfaceFeatures_RGBA8.png";
        public const string SurfaceNormalPath = LidarRoot + "/KBDU_Lidar_SurfaceNormal_RGBA8.png";
        public const string CanopyNormalPath = LidarRoot + "/KBDU_Lidar_CanopyNormalOcclusion_RGBA8.png";
        public const string VegetationSourceManifestPath = VegetationRoot + "/SOURCE_MANIFEST.json";
        public const string VegetationDistributionPath = VegetationRoot + "/AerialVegetation_Distribution.json";
        public const string VegetationHandoffPath =
            VegetationRoot + "/Textures/AerialVegetation_HandoffMask_R8.png";
        public const string VegetationAtlasPath =
            VegetationRoot + "/Textures/AerialVegetation_Atlas.png";
        public const string VegetationSurfacePath =
            VegetationRoot + "/Textures/AerialVegetation_Surface.png";
        public const string VegetationPrefabPath = VegetationRoot + "/Generated/AerialVegetationRoot.prefab";
        public const string BuildingSpatialPath = BuildingRoot + "/KBDU_BuildingSpatialContractV1.json";
        public const string BuildingReplacementPath = BuildingRoot + "/KBDU_BuildingBatchReplacementV1.json";
        public const string BuildingPrefabPath = BuildingRoot + "/KBDU_AerialBuildingsV1.prefab";
        public const string DeterministicBakeUtc = "2026-07-19T03:30:00Z";
        public const string MacroSourceDescription =
            "USGS/USDA NAIP 2019 public-domain bounded KBDU imagery pyramid";

        private static readonly string[] LegacyVegetationNames =
        {
            "SparseVegetationProduction",
            "vegetation_v2_hlod_1500m_xn001_zn001",
            "vegetation_v2_hlod_1500m_xn001_zp000",
            "vegetation_v2_hlod_1500m_xp000_zn001",
            "vegetation_v2_hlod_1500m_xp000_zp000"
        };

        [MenuItem("QuestFlight/Product/Integrate Aerial Scenery V1")]
        public static void RunInteractive() => Apply();

        public static void RunBatch()
        {
            try
            {
                Apply();
                Debug.Log("[QuestFlight][AerialSceneryV1] SERIALIZED_INTEGRATION_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureRuntimeTexture(SurfaceFeaturesPath, true, false, TextureImporterFormat.ASTC_6x6);
            ConfigureRuntimeTexture(SurfaceNormalPath, true, false, TextureImporterFormat.ASTC_6x6);
            ConfigureRuntimeTexture(CanopyNormalPath, true, false, TextureImporterFormat.ASTC_6x6);
            ConfigureRuntimeTexture(VegetationHandoffPath, false, false, TextureImporterFormat.R8);
            ConfigureRuntimeTexture(VegetationAtlasPath, true, true, TextureImporterFormat.ASTC_6x6,
                sRgb: true, preserveAlphaCoverage: true, anisotropy: 2);
            ConfigureRuntimeTexture(VegetationSurfacePath, true, true, TextureImporterFormat.ASTC_6x6,
                anisotropy: 2);

            TextAsset lidarSource = LoadRequired<TextAsset>(LidarManifestPath);
            TextAsset terrainError = LoadRequired<TextAsset>(TerrainErrorPath);
            Mesh conformedAirportPatch =
                AirportPatchLidarConformanceAuthoring.CompileForAerialSceneryV1Integration();
            Mesh lidarNearTerrain = LoadRequired<Mesh>(NearTerrainPath);
            TextAsset terrainCoordinateConversion =
                LoadRequired<TextAsset>(TerrainCoordinateConversionReportPath);
            TextAsset terrainConformance = LoadRequired<TextAsset>(TerrainConformanceReportPath);
            TextAsset vegetationSource = LoadRequired<TextAsset>(VegetationSourceManifestPath);
            TextAsset vegetationDistribution = LoadRequired<TextAsset>(VegetationDistributionPath);
            TextAsset buildingSpatial = LoadRequired<TextAsset>(BuildingSpatialPath);
            TextAsset buildingReplacement = LoadRequired<TextAsset>(BuildingReplacementPath);
            Texture2D surfaceFeatures = LoadRequired<Texture2D>(SurfaceFeaturesPath);
            Texture2D surfaceNormal = LoadRequired<Texture2D>(SurfaceNormalPath);
            Texture2D canopyNormal = LoadRequired<Texture2D>(CanopyNormalPath);
            Texture2D canopyHandoff = LoadRequired<Texture2D>(VegetationHandoffPath);
            GameObject vegetationPrefab = LoadRequired<GameObject>(VegetationPrefabPath);
            GameObject buildingPrefab = LoadRequired<GameObject>(BuildingPrefabPath);
            Material terrainMaterial = LoadRequired<Material>(TerrainMaterialPath);
            ConfigureTerrainMaterial(terrainMaterial, surfaceFeatures, surfaceNormal, canopyNormal, canopyHandoff);

            LidarManifestJson lidarJson = JsonUtility.FromJson<LidarManifestJson>(lidarSource.text);
            TerrainErrorJson terrainJson = JsonUtility.FromJson<TerrainErrorJson>(terrainError.text);
            VegetationDistributionJson vegetationJson =
                JsonUtility.FromJson<VegetationDistributionJson>(vegetationDistribution.text);
            BuildingSpatialJson buildingJson = JsonUtility.FromJson<BuildingSpatialJson>(buildingSpatial.text);
            BuildingReplacementJson replacementJson =
                JsonUtility.FromJson<BuildingReplacementJson>(buildingReplacement.text);
            ValidateInputRecords(lidarJson, terrainJson, vegetationJson, buildingJson, replacementJson);

            GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null) throw new InvalidOperationException("Production environment contract is missing.");
                ConfigureOrthophotoOnlyWater(environment, contract);

                AerialSceneryRoot[] existingAerialRoots =
                    environment.GetComponentsInChildren<AerialSceneryRoot>(true);
                if (existingAerialRoots.Length > 1)
                    throw new InvalidOperationException("More than one authored aerial scenery root is serialized.");
                AerialSceneryRoot existingAerial = existingAerialRoots.SingleOrDefault();
                MeshFilter nearTerrain = contract.nearTerrainMeshes.Single(filter =>
                    filter != null && string.Equals(filter.name, "Terrain_Near4km_USGS", StringComparison.Ordinal));
                MeshFilter airportPatch = contract.nearTerrainMeshes.Single(filter =>
                    filter != null && string.Equals(filter.name, "Terrain_AirportPatch_USGS", StringComparison.Ordinal));
                Mesh baselineNearTerrain = existingAerial?.baselineNearTerrainMesh ?? nearTerrain.sharedMesh;
                Mesh baselineAirportPatch = existingAerial?.baselineAirportPatchMesh ?? airportPatch.sharedMesh;
                if (baselineNearTerrain == null || ReferenceEquals(baselineNearTerrain, lidarNearTerrain))
                    throw new InvalidOperationException("The baseline/new near-terrain A/B identity is invalid.");
                if (baselineAirportPatch == null || ReferenceEquals(baselineAirportPatch, conformedAirportPatch))
                    throw new InvalidOperationException("The baseline/new airport-patch A/B identity is invalid.");

                if (existingAerial != null)
                    UnityEngine.Object.DestroyImmediate(existingAerial.gameObject);
                nearTerrain.sharedMesh = lidarNearTerrain;
                airportPatch.sharedMesh = conformedAirportPatch;
                MeshRenderer nearRenderer = nearTerrain.GetComponent<MeshRenderer>();
                if (nearRenderer == null || nearRenderer.sharedMaterial != terrainMaterial)
                    throw new InvalidOperationException("The authoritative near terrain material binding changed.");
                MeshRenderer airportPatchRenderer = airportPatch.GetComponent<MeshRenderer>();
                if (airportPatchRenderer == null || airportPatchRenderer.sharedMaterial != terrainMaterial)
                    throw new InvalidOperationException("The authoritative airport patch material binding changed.");

                Transform[] disabledVegetation = LegacyVegetationNames
                    .Select(name => FindUnique(environment.transform, name)).ToArray();
                Transform[] disabledBuildings = replacementJson.legacy_group_ids_to_disable
                    .Select(name => FindUnique(environment.transform, name)).ToArray();
                DisableAuthoredLegacy(disabledVegetation.Concat(disabledBuildings));

                GameObject aerialObject = new GameObject(AerialSceneryRoot.GameObjectName);
                aerialObject.transform.SetParent(environment.transform, false);
                AerialSceneryRoot aerial = aerialObject.AddComponent<AerialSceneryRoot>();
                aerial.tier0AirportHero = CreateAnchor(aerialObject.transform, "Tier0_AirportHeroCore");
                aerial.tier1NearScenery = CreateAnchor(aerialObject.transform, "Tier1_NearScenery");
                aerial.tier2MidScenery = CreateAnchor(aerialObject.transform, "Tier2_MidScenery");
                aerial.tier3FarScenery = CreateAnchor(aerialObject.transform, "Tier3_FarScenery");
                aerial.terrainRoot = CreateAnchor(aerialObject.transform, "FixedTerrainRepresentation");

                GameObject vegetation = InstantiateNested(vegetationPrefab, aerialObject.transform,
                    "AerialVegetationRepresentation");
                GameObject buildings = InstantiateNested(buildingPrefab, aerialObject.transform,
                    "AerialBuildingRepresentation");
                aerial.vegetationRoot = vegetation.transform;
                aerial.buildingRoot = buildings.transform;
                aerial.authoritativeAirportPatch = airportPatch;
                aerial.baselineAirportPatchMesh = baselineAirportPatch;
                aerial.authoritativeNearTerrain = nearTerrain;
                aerial.baselineNearTerrainMesh = baselineNearTerrain;
                aerial.disabledLegacyVegetationRoots = disabledVegetation;
                aerial.disabledLegacyBuildingRoots = disabledBuildings;

                AerialSceneryManifest manifest = BuildManifest(
                    lidarSource,
                    terrainError,
                    terrainCoordinateConversion,
                    terrainConformance,
                    vegetationSource,
                    buildingSpatial,
                    surfaceFeatures,
                    surfaceNormal,
                    canopyNormal,
                    canopyHandoff,
                    conformedAirportPatch,
                    lidarNearTerrain,
                    contract.midTerrainMesh != null ? contract.midTerrainMesh.sharedMesh : null,
                    lidarJson,
                    terrainJson,
                    vegetationJson,
                    buildingJson);
                aerial.manifest = manifest;

                AerialVegetationBatchRenderer vegetationBatch =
                    vegetation.GetComponent<AerialVegetationBatchRenderer>();
                if (vegetationBatch == null)
                    throw new InvalidOperationException("Authored vegetation batch renderer is missing.");
                aerial.bakedVegetationRendererCount = vegetationBatch.NonEmptyDrawCount;
                aerial.bakedVegetationTriangleCount = checked((int)vegetationBatch.FullyInstancedTriangleCount);
                aerial.bakedVegetationMaterialCount = vegetationBatch.SharedMaterial != null ? 1 : 0;
                aerial.bakeToolVersion = "aerial_scenery_representation_v1";
                aerial.bakeUtc = DeterministicBakeUtc;

                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                aerial.bakedRendererCount = budget.rendererCount;
                aerial.bakedSelectedTriangleCount = budget.triangleCount;
                aerial.bakedMaterialCount = budget.materialCount;
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakedTextureCount = CountReferencedTextures(environment);
                contract.bakeUtc = DeterministicBakeUtc;
                contract.bakeToolVersion = "aerial_scenery_representation_v1";

                if (!manifest.TryValidate(out string manifestReport))
                    throw new InvalidOperationException("Aerial manifest failed: " + manifestReport);
                if (!aerial.TryValidateContract(out string aerialReport))
                    throw new InvalidOperationException("Aerial root failed: " + aerialReport);
                if (!contract.TryValidateContract(out string environmentReport))
                    throw new InvalidOperationException("Production environment failed: " + environmentReport);

                EditorUtility.SetDirty(manifest);
                EditorUtility.SetDirty(terrainMaterial);
                EditorUtility.SetDirty(aerial);
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(environment, EnvironmentPrefabPath);
                Debug.Log($"[QuestFlight][AerialSceneryV1] {manifestReport}; {aerialReport}; {environmentReport}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static AerialSceneryManifest BuildManifest(
            TextAsset lidarSource,
            TextAsset terrainError,
            TextAsset terrainCoordinateConversion,
            TextAsset terrainConformance,
            TextAsset vegetationSource,
            TextAsset buildingSource,
            Texture2D surfaceFeatures,
            Texture2D surfaceNormal,
            Texture2D canopyNormal,
            Texture2D canopyHandoff,
            Mesh airportPatch,
            Mesh nearTerrain,
            Mesh midTerrain,
            LidarManifestJson lidar,
            TerrainErrorJson terrain,
            VegetationDistributionJson vegetation,
            BuildingSpatialJson buildings)
        {
            AerialSceneryManifest manifest = AssetDatabase.LoadAssetAtPath<AerialSceneryManifest>(ManifestAssetPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<AerialSceneryManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestAssetPath);
            }

            manifest.schemaVersion = AerialSceneryManifest.CurrentSchemaVersion;
            manifest.label = "KBDU aerial scenery representation V1 — not for navigation";
            manifest.horizontalCrs = "EPSG:32613";
            manifest.sourceLicense = "USGS 3DEP public domain; OSM derivatives ODbL-1.0; project-authored vegetation";
            manifest.generatedUtc = DeterministicBakeUtc;
            manifest.lidarSourceManifest = lidarSource;
            manifest.terrainCoordinateConversionReport = terrainCoordinateConversion;
            manifest.terrainConformanceReport = terrainConformance;
            manifest.vegetationSourceManifest = vegetationSource;
            manifest.buildingSourceManifest = buildingSource;
            manifest.lidarPipelineSha256 = Sha256(LidarManifestPath);
            manifest.vegetationPipelineSha256 = Sha256(VegetationSourceManifestPath);
            manifest.buildingPipelineSha256 = Sha256(BuildingSpatialPath);
            manifest.worldMinimumMeters = new Vector2(-2048f, -2048f);
            manifest.worldSizeMeters = new Vector2(4096f, 4096f);
            manifest.surfaceFeatures = surfaceFeatures;
            manifest.surfaceNormals = surfaceNormal;
            manifest.canopyNormals = canopyNormal;
            manifest.canopyOcclusion = canopyNormal;
            manifest.canopyGeometryHandoff = canopyHandoff;
            manifest.airportPatchMesh = airportPatch;
            manifest.nearTerrainMesh = nearTerrain;
            manifest.midTerrainMesh = midTerrain;
            manifest.boundedSourcePointCount = lidar.statistics.point_counts.inside_exact_product_bounds;
            manifest.canopyComponentCount = vegetation.statistics.rejection_counters.grove_components;
            manifest.nearTreeInstanceCount = vegetation.statistics.tier0_selected + vegetation.statistics.tier1_selected;
            manifest.midGroveClusterCount = vegetation.statistics.tier2_selected;
            manifest.representedBuildingCount = buildings.statistics.geometry_building_count;
            manifest.orthophotoOnlyBuildingCount = buildings.statistics.source_building_count -
                                                   buildings.statistics.geometry_building_count;
            manifest.buildingFootprintInflationMaximum = buildings.statistics.maximum_footprint_area_ratio;
            ErrorMetrics observed = terrain.geometric_integration_eligibility.observed_outside_declared_outliers;
            manifest.terrainMeanAbsoluteErrorMeters = observed.mean_absolute_meters;
            manifest.terrainP95ErrorMeters = observed.p95_absolute_meters;
            manifest.terrainMaximumErrorMeters = observed.maximum_absolute_meters;
            manifest.terrainErrorMetricsExcludeDeclaredOutliers = true;
            manifest.terrainDeclaredOutlierFraction =
                terrain.geometric_integration_eligibility.declared_outliers.pixel_fraction;
            manifest.operationalSurfaceRealWorldToleranceEstablished = false;
            EditorUtility.SetDirty(manifest);
            return manifest;
        }

        private static void ConfigureTerrainMaterial(
            Material material,
            Texture2D surfaceFeatures,
            Texture2D surfaceNormal,
            Texture2D canopyNormal,
            Texture2D canopyHandoff)
        {
            string[] required =
            {
                "_AerialCanopySurfaceFeatures", "_AerialTerrainNormalAndValidity",
                "_AerialCanopyNormalOcclusion", "_AerialCanopyHandoffMask",
                "_AerialCanopyLocalBounds", "_AerialCanopyHeightQuantumMeters",
                "_AerialCanopyColorStrength", "_AerialCanopyNormalStrength", "_AerialCanopyTint"
            };
            string missing = string.Join(", ", required.Where(property => !material.HasProperty(property)));
            if (!string.IsNullOrEmpty(missing))
                throw new InvalidOperationException("Terrain shader lacks aerial canopy properties: " + missing);

            material.SetTexture("_AerialCanopySurfaceFeatures", surfaceFeatures);
            material.SetTexture("_AerialTerrainNormalAndValidity", surfaceNormal);
            material.SetTexture("_AerialCanopyNormalOcclusion", canopyNormal);
            material.SetTexture("_AerialCanopyHandoffMask", canopyHandoff);
            material.SetVector("_AerialCanopyLocalBounds", new Vector4(-2048f, -2048f, 1f / 4096f, 1f / 4096f));
            material.SetFloat("_AerialCanopyHeightQuantumMeters", 0.25f);
            material.SetFloat("_AerialCanopyColorStrength", 0.44f);
            material.SetFloat("_AerialCanopyNormalStrength", 0.36f);
            material.SetColor("_AerialCanopyTint", new Color(0.28f, 0.37f, 0.18f, 1f));
            material.SetTexture("_NearSourceNormal", surfaceNormal);
            material.SetFloat("_NearSourceNormalStrength", 0.42f);
            material.SetVector("_NearSourceNormalFadeStartEnd", new Vector4(250f, 3500f, 0f, 0f));
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureRuntimeTexture(
            string path,
            bool mipmaps,
            bool streamingMipmaps,
            TextureImporterFormat androidFormat,
            bool sRgb = false,
            bool preserveAlphaCoverage = false,
            int anisotropy = 1)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer is missing: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = sRgb;
            importer.mipmapEnabled = mipmaps;
            importer.mipMapsPreserveCoverage = preserveAlphaCoverage;
            if (preserveAlphaCoverage) importer.alphaTestReferenceValue = 0.42f;
            importer.streamingMipmaps = streamingMipmaps;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
            importer.anisoLevel = anisotropy;
            importer.maxTextureSize = 1024;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = androidFormat;
            android.compressionQuality = 100;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void ConfigureOrthophotoOnlyWater(
            GameObject environment,
            ProductionEnvironmentRoot contract)
        {
            AirportCoreBenchmarkRoot benchmark = environment
                .GetComponentsInChildren<AirportCoreBenchmarkRoot>(true).Single();
            Renderer reservoir = contract.boulderReservoirSurface == null
                ? null
                : contract.boulderReservoirSurface.GetComponent<Renderer>();
            Renderer shore = contract.boulderReservoirShoreBank == null
                ? null
                : contract.boulderReservoirShoreBank.GetComponent<Renderer>();
            if (reservoir == null)
                throw new InvalidOperationException("The authoritative Boulder Reservoir renderer is missing.");

            Texture2D fullMapWaterMask = LoadRequired<Texture2D>(
                FullMapWaterReflectionAuthoring.MaskPath);
            TextAsset fullMapWaterManifest = LoadRequired<TextAsset>(
                FullMapWaterReflectionAuthoring.ManifestPath);
            Material terrainMaterial = LoadRequired<Material>(TerrainMaterialPath);
            FullMapWaterReflectionAuthoring.ConfigureMaterialState(
                terrainMaterial,
                fullMapWaterMask);
            FullMapWaterReflectionAuthoring.ConfigureEnvironmentState(
                contract,
                fullMapWaterMask,
                fullMapWaterManifest);
            EditorUtility.SetDirty(terrainMaterial);
            contract.macroSource = MacroSourceDescription;
            DisableAndClearMaterials(reservoir);
            if (shore != null) DisableAndClearMaterials(shore);

            benchmark.boulderReservoir = reservoir;
            benchmark.v2ReservoirMaterial = null;
            if (benchmark.manifest == null)
                throw new InvalidOperationException("The airport-core benchmark manifest is missing.");
            benchmark.manifest.reservoirMaterial = null;
            benchmark.manifest.waterEffectsDeferredToOrthophoto = true;
            benchmark.manifest.minorWaterUsesTerrainMask = false;

            EditorUtility.SetDirty(benchmark.manifest);
            EditorUtility.SetDirty(benchmark);
            EditorUtility.SetDirty(contract);
        }

        private static void DisableAndClearMaterials(Renderer renderer)
        {
            renderer.enabled = false;
            renderer.gameObject.SetActive(false);
            renderer.sharedMaterials = Array.Empty<Material>();
            EditorUtility.SetDirty(renderer);
        }

        private static void DisableAuthoredLegacy(IEnumerable<Transform> roots)
        {
            foreach (Transform root in roots.Distinct())
            {
                root.gameObject.SetActive(false);
                EditorUtility.SetDirty(root.gameObject);
                if (PrefabUtility.IsPartOfPrefabInstance(root.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(root.gameObject);
            }
        }

        private static Transform CreateAnchor(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
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

        private static Transform FindUnique(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one authored '{name}', found {matches.Length}.");
            return matches[0];
        }

        private static Mesh LoadSingleModelMesh(string path)
        {
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            if (meshes.Length != 1)
                throw new InvalidOperationException($"Expected one imported terrain mesh at {path}, found {meshes.Length}.");
            return meshes[0];
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return asset;
        }

        private static string Sha256(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            using SHA256 algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(File.ReadAllBytes(fullPath))
                .Select(value => value.ToString("x2")));
        }

        private static int CountReferencedTextures(GameObject root)
        {
            HashSet<Texture> textures = new HashSet<Texture>();
            IEnumerable<Material> materials = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null);
            materials = materials.Concat(root.GetComponentsInChildren<AerialVegetationBatchRenderer>(true)
                .Where(batch => batch.enabled && batch.gameObject.activeInHierarchy && batch.SharedMaterial != null)
                .Select(batch => batch.SharedMaterial));
            foreach (Material material in materials.Distinct())
            foreach (string property in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(property);
                if (texture != null) textures.Add(texture);
            }
            return textures.Count;
        }

        private static void ValidateInputRecords(
            LidarManifestJson lidar,
            TerrainErrorJson terrain,
            VegetationDistributionJson vegetation,
            BuildingSpatialJson buildings,
            BuildingReplacementJson replacement)
        {
            if (lidar?.statistics?.point_counts == null ||
                lidar.statistics.point_counts.inside_exact_product_bounds != 18834395L)
                throw new InvalidOperationException("Frozen lidar point-count identity changed.");
            if (terrain?.geometric_integration_eligibility == null ||
                !string.Equals(terrain.geometric_integration_eligibility.status, "ELIGIBLE", StringComparison.Ordinal))
                throw new InvalidOperationException("Near terrain is not geometrically eligible.");
            if (vegetation?.statistics?.rejection_counters == null ||
                vegetation.batch_count != 2 ||
                vegetation.instance_count != 22 ||
                vegetation.statistics.tier0_selected != 3 ||
                vegetation.statistics.tier1_selected != 19 ||
                vegetation.statistics.tier2_selected != 0 ||
                vegetation.statistics.tier2_geometry_draws != 0 ||
                vegetation.statistics.tier2_terrain_shading_owned_pixels <= 0)
                throw new InvalidOperationException("Vegetation distribution contract is incomplete.");
            if (buildings?.statistics == null || buildings.statistics.geometry_building_count != 2875 ||
                buildings.statistics.maximum_footprint_area_ratio > 1.100001f)
                throw new InvalidOperationException("Building spatial contract changed.");
            if (replacement?.legacy_group_ids_to_disable == null ||
                replacement.legacy_group_ids_to_disable.Length != 20)
                throw new InvalidOperationException("Legacy building replacement map is incomplete.");
        }

        [Serializable] private sealed class LidarManifestJson { public LidarStatistics statistics; }
        [Serializable] private sealed class LidarStatistics { public PointCounts point_counts; }
        [Serializable] private sealed class PointCounts { public long inside_exact_product_bounds; }
        [Serializable] private sealed class TerrainErrorJson
        {
            public GeometricIntegrationEligibility geometric_integration_eligibility;
        }
        [Serializable] private sealed class GeometricIntegrationEligibility
        {
            public string status;
            public DeclaredOutliers declared_outliers;
            public ErrorMetrics observed_outside_declared_outliers;
        }
        [Serializable] private sealed class DeclaredOutliers { public float pixel_fraction; }
        [Serializable] private sealed class ErrorMetrics
        {
            public float maximum_absolute_meters;
            public float mean_absolute_meters;
            public float p95_absolute_meters;
        }
        [Serializable] private sealed class VegetationDistributionJson
        {
            public int batch_count;
            public int instance_count;
            public VegetationStatistics statistics;
        }
        [Serializable] private sealed class VegetationStatistics
        {
            public int tier0_selected;
            public int tier1_selected;
            public int tier2_selected;
            public int tier2_geometry_draws;
            public int tier2_terrain_shading_owned_pixels;
            public VegetationRejections rejection_counters;
        }
        [Serializable] private sealed class VegetationRejections { public int grove_components; }
        [Serializable] private sealed class BuildingSpatialJson { public BuildingStatistics statistics; }
        [Serializable] private sealed class BuildingStatistics
        {
            public int source_building_count;
            public int geometry_building_count;
            public float maximum_footprint_area_ratio;
        }
        [Serializable] private sealed class BuildingReplacementJson
        {
            public string[] legacy_group_ids_to_disable;
        }
    }
}
