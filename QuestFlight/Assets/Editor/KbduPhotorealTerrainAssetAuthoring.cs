using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Authors reusable KBDU terrain-pyramid and context assets. Focused repair entry
    /// points may refresh the product environment's serialized budget after replacing
    /// a nested prefab, but never reconstruct or otherwise mutate the product hierarchy.
    /// </summary>
    public static class KbduPhotorealTerrainAssetAuthoring
    {
        private const string Root = "Assets/Production/Environment/PhotorealTerrainV1";
        private const string Generated = Root + "/Generated";
        private const string ContextPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ReferenceContextV2.json";
        private const string RecipePath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ContextBatchRecipeV2.json";
        private const string RuntimeRecipePath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_RuntimeHlodRecipeV2.json";
        private const string AtlasPath = Root + "/KBDU_ContextAtlas.png";
        private const string NearPath =
            "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Near4km_4096.jpg";
        private const string MidPath =
            "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Mid12km_4096.jpg";
        private const string FarPath =
            "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Far24km_2048.jpg";
        private const string DetailPath =
            "Assets/Resources/QuestFlightLab/Environment/GroundMaterials/withered_grass_diff_1k.jpg";
        private const string ParkedC172ModelPath =
            "Assets/Production/Environment/AirportParkedAircraftV1/Models/KBDU_ParkedC172_FixedDetail.glb";
        private const string ParkedSailplaneModelPath =
            "Assets/Production/Environment/AirportParkedAircraftV1/Models/KBDU_ParkedSailplane_FixedDetail.glb";
        private const int NonContextDrawSlots = 99;
        private const long NonContextTriangles = 551958;
        private const int MaximumVisibleDrawSlots = 130;
        private const long MaximumVisibleTriangles = 650000;
        private const int MaximumSingleMeshVertices = 200000;
        private const int NonContextSerializedRenderers = 99;
        private const long NonContextSerializedTriangles = 551958;
        private const int MaximumSerializedRenderers = 180;
        private const long MaximumSerializedTriangles = 700000;
        private const int PlannedWaterRendererReserve = 2;

        [MenuItem("QuestFlight/Product/Author KBDU Photoreal Terrain Assets")]
        public static void RunInteractive() => AuthorAssets();

        public static void RunBatch()
        {
            try
            {
                AuthorAssets();
                Debug.Log("[QuestFlight][KBDUPhotorealTerrain] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ValidateBatch()
        {
            try
            {
                ValidateAssets();
                Debug.Log("[QuestFlight][KBDUPhotorealTerrainValidation] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Regenerates only the reusable batched context assets after a visual-art
        /// source repair. It never opens or saves the product environment, world,
        /// or scene and does not rewrite the terrain-pyramid material.
        /// </summary>
        public static void RepairContextVisualAssetsBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ValidateRuntimeSourcePlan();
                EnsureFolder(Generated);
                CreateContextMaterials();
                CreateContextMeshes();
                AssetDatabase.SaveAssets();
                CreateContextPrefab();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[QuestFlight][KBDUPhotorealTerrainVisualRepair] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Regenerates and rebinds only the parked-aircraft batch. This preserves the
        /// already-filtered V4 building context and stable prefab object identities.
        /// </summary>
        public static void RepairParkedAircraftVisualAssetsBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                RuntimeInputs inputs = ValidateRuntimeSourcePlan();
                CreateParkedAircraftMeshes(inputs);
                AssetDatabase.SaveAssets();
                UpdateParkedAircraftPrefab(inputs);
                AssetDatabase.SaveAssets();
                RefreshSerializedEnvironmentBudget();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[QuestFlight][KBDUParkedAircraftVisualRepair] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void RefreshSerializedEnvironmentBudget()
        {
            GameObject environment = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot contract =
                    environment.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null)
                    throw new InvalidOperationException(
                        "Production environment contract is missing while refreshing the parked-aircraft budget.");
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                if (!contract.TryValidateContract(out string report))
                    throw new InvalidOperationException(
                        "Parked-aircraft integration leaves the product environment invalid: " + report);
                PrefabUtility.SaveAsPrefabAsset(
                    environment, ProductionEnvironmentRoot.PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }
        }

        private static void AuthorAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateRuntimeSourcePlan();
            EnsureFolder(Generated);
            ConfigureTexture(NearPath, 4096, true, 8, true, -0.15f);
            ConfigureTexture(MidPath, 4096, true, 8, true, -0.15f);
            ConfigureTexture(FarPath, 2048, true, 8, true, -0.10f);
            ConfigureTexture(DetailPath, 1024, true, 8, false, -0.15f);
            ConfigureTexture(AtlasPath, 1024, false, 2, true);
            CreateTerrainMaterial();
            CreateContextMaterials();
            CreateContextMeshes();
            AssetDatabase.SaveAssets();
            CreateContextPrefab();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ValidateAssets()
        {
            RuntimeInputs inputs = ValidateRuntimeSourcePlan();
            int batchCount = inputs.Runtime.building_runtime_batches.Length +
                             inputs.Runtime.vegetation_runtime_batches.Length +
                             inputs.Runtime.parked_aircraft_runtime_batches.Length;
            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { Generated });
            if (meshGuids.Length != batchCount * 3)
                throw new InvalidOperationException(
                    "Generated context mesh count is " + meshGuids.Length + ", expected " + batchCount * 3);
            Dictionary<string, long[]> trianglesByBatch = new Dictionary<string, long[]>(StringComparer.Ordinal);
            foreach (string guid in meshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Mesh mesh = LoadRequired<Mesh>(path);
                if (mesh.subMeshCount != 1 || mesh.vertexCount == 0 || mesh.GetIndexCount(0) == 0)
                    throw new InvalidOperationException("Generated mesh is empty or split: " + path);
                if (mesh.GetIndexCount(0) % 3 != 0)
                    throw new InvalidOperationException("Generated mesh has non-triangle indices: " + path);
                if (mesh.vertexCount > MaximumSingleMeshVertices)
                    throw new InvalidOperationException("Generated batch is too large for bounded culling: " + path);
                Bounds bounds = mesh.bounds;
                if (!Finite(bounds.center) || !Finite(bounds.extents))
                    throw new InvalidOperationException("Generated mesh has nonfinite bounds: " + path);
                long triangles = (long)mesh.GetIndexCount(0) / 3L;
                int lod = path.EndsWith("_LOD0.asset", StringComparison.Ordinal) ? 0 :
                    path.EndsWith("_LOD1.asset", StringComparison.Ordinal) ? 1 :
                    path.EndsWith("_LOD2.asset", StringComparison.Ordinal) ? 2 : -1;
                if (lod < 0) throw new InvalidOperationException("Generated mesh has an unexpected LOD name: " + path);
                string batchId = Path.GetFileNameWithoutExtension(path);
                batchId = batchId.Substring(0, batchId.Length - 5);
                if (!trianglesByBatch.TryGetValue(batchId, out long[] values))
                {
                    values = new long[3];
                    trianglesByBatch.Add(batchId, values);
                }
                values[lod] = triangles;
            }
            long contextWorstCaseTriangles = trianglesByBatch.Values.Sum(values => values.Max());
            long contextAggregateTriangles = trianglesByBatch.Values.Sum(values => values.Sum());
            if (trianglesByBatch.Count != batchCount ||
                NonContextTriangles + contextWorstCaseTriangles > MaximumVisibleTriangles)
                throw new InvalidOperationException(
                    "Generated context triangle budget exceeded: context=" + contextWorstCaseTriangles +
                    ", integrated=" + (NonContextTriangles + contextWorstCaseTriangles));
            if (NonContextDrawSlots + batchCount + PlannedWaterRendererReserve > MaximumVisibleDrawSlots)
                throw new InvalidOperationException("Generated context renderer-slot budget exceeded: " + batchCount);
            if (NonContextSerializedRenderers + batchCount * 3 + PlannedWaterRendererReserve > MaximumSerializedRenderers ||
                NonContextSerializedTriangles + contextAggregateTriangles > MaximumSerializedTriangles)
                throw new InvalidOperationException(
                    "Generated aggregate serialized budget exceeded: renderers=" +
                    (NonContextSerializedRenderers + batchCount * 3 + PlannedWaterRendererReserve) + ", triangles=" +
                    (NonContextSerializedTriangles + contextAggregateTriangles));

            GameObject prefab = LoadRequired<GameObject>(Root + "/KBDU_BatchedContext.prefab");
            LODGroup[] groups = prefab.GetComponentsInChildren<LODGroup>(true);
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            if (groups.Length != batchCount || renderers.Length != batchCount * 3)
                throw new InvalidOperationException(
                    "Batched context prefab hierarchy is incomplete: groups=" + groups.Length +
                    ", renderers=" + renderers.Length);
            foreach (LODGroup group in groups)
            {
                if (group.fadeMode != LODFadeMode.None || group.animateCrossFading || group.GetLODs().Length != 3)
                    throw new InvalidOperationException("Context LOD must have three non-crossfaded levels: " + group.name);
            }
            HashSet<Material> materials = new HashSet<Material>(renderers.Select(renderer => renderer.sharedMaterial));
            if (materials.Count != 3 || materials.Any(material => material == null))
                throw new InvalidOperationException("Context prefab must use exactly three non-null shared materials");

            ValidateTextureImport(NearPath, 4096);
            ValidateTextureImport(MidPath, 4096);
            ValidateTextureImport(FarPath, 2048);
            ValidateDetailTextureImport();
            Debug.Log(
                "[QuestFlight][KBDUPhotorealTerrainValidation] batches=" + batchCount +
                ", context worst-case triangles=" + contextWorstCaseTriangles +
                ", integrated planning triangles=" + (NonContextTriangles + contextWorstCaseTriangles) +
                ", aggregate serialized triangles=" + (NonContextSerializedTriangles + contextAggregateTriangles) +
                ", materials=" + materials.Count + ".");
        }

        private static RuntimeInputs ValidateRuntimeSourcePlan()
        {
            ContextDocument context = ReadJson<ContextDocument>(ContextPath);
            ContextRecipe logical = ReadJson<ContextRecipe>(RecipePath);
            RuntimeHlodRecipe runtime = ReadJson<RuntimeHlodRecipe>(RuntimeRecipePath);
            if (context.openstreetmap == null || context.openstreetmap.features == null)
                throw new InvalidOperationException("V2 reference context does not contain OSM features");
            if (logical.building_batches == null || logical.vegetation_batches == null ||
                logical.parked_aircraft_batches == null)
                throw new InvalidOperationException("V2 logical recipe does not contain all context categories");
            if (runtime.building_runtime_batches == null || runtime.vegetation_runtime_batches == null ||
                runtime.parked_aircraft_runtime_batches == null || runtime.static_budget == null)
                throw new InvalidOperationException("Runtime HLOD recipe is incomplete");
            if (!string.Equals(runtime.source_logical_recipe_sha256, ComputeSha256(RecipePath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(runtime.source_context_sha256, ComputeSha256(ContextPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Runtime HLOD recipe is stale relative to its logical recipe or context source");

            Dictionary<string, BuildingBatch> logicalBuildings = logical.building_batches
                .ToDictionary(batch => batch.id, StringComparer.Ordinal);
            HashSet<string> coveredBuildings = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuntimeBuildingBatch batch in runtime.building_runtime_batches)
            {
                if (string.IsNullOrWhiteSpace(batch.id) || batch.logical_batch_ids == null || batch.logical_batch_ids.Length == 0)
                    throw new InvalidOperationException("Runtime building batch identity/coverage is incomplete");
                ValidateLodPolicy(batch.id, batch.lod_policy);
                foreach (string logicalId in batch.logical_batch_ids)
                {
                    if (!logicalBuildings.ContainsKey(logicalId))
                        throw new InvalidOperationException("Runtime HLOD references an unknown logical building cell: " + logicalId);
                    if (!coveredBuildings.Add(logicalId))
                        throw new InvalidOperationException("Runtime HLOD duplicates a logical building cell: " + logicalId);
                }
            }
            if (!coveredBuildings.SetEquals(logicalBuildings.Keys))
                throw new InvalidOperationException("Runtime HLOD does not preserve every 250 m logical building cell");

            ValidateSimpleRuntimeCoverage(
                "vegetation",
                logical.vegetation_batches.Select(batch => batch.id),
                runtime.vegetation_runtime_batches);
            VegetationPlacement[] logicalVegetation = logical.vegetation_batches
                .SelectMany(batch => batch.placements).ToArray();
            HashSet<string> stableVegetationIds = new HashSet<string>(
                logicalVegetation.Select(placement => placement.stable_id), StringComparer.Ordinal);
            HashSet<string> logicalSpecies = new HashSet<string>(
                logicalVegetation.Select(placement => placement.species_id), StringComparer.Ordinal);
            if (logicalVegetation.Length != runtime.vegetation_placement_count ||
                stableVegetationIds.Count != logicalVegetation.Length ||
                runtime.vegetation_species_ids == null ||
                !logicalSpecies.SetEquals(runtime.vegetation_species_ids))
                throw new InvalidOperationException(
                    "Runtime HLOD does not preserve vegetation placement/species identity");
            ValidateSimpleRuntimeCoverage(
                "parked aircraft",
                logical.parked_aircraft_batches.Select(batch => batch.id),
                runtime.parked_aircraft_runtime_batches);

            int batchCount = runtime.building_runtime_batches.Length +
                             runtime.vegetation_runtime_batches.Length +
                             runtime.parked_aircraft_runtime_batches.Length;
            long plannedAggregateTriangles = runtime.building_runtime_batches
                .Sum(batch => batch.lod_policy.Sum(policy => policy.estimated_triangles)) +
                runtime.vegetation_runtime_batches
                    .Sum(batch => batch.lod_policy.Sum(policy => policy.estimated_triangles)) +
                runtime.parked_aircraft_runtime_batches
                    .Sum(batch => batch.lod_policy.Sum(policy => policy.estimated_triangles));
            BudgetEstimates estimates = runtime.static_budget.estimates;
            BudgetLimits limits = runtime.static_budget.limits;
            if (!string.Equals(runtime.static_budget.status, "PASS", StringComparison.Ordinal) ||
                estimates == null || limits == null || estimates.context_batch_count != batchCount ||
                estimates.integrated_visible_draw_slots != NonContextDrawSlots + batchCount + PlannedWaterRendererReserve ||
                estimates.integrated_aggregate_serialized_renderers !=
                    NonContextSerializedRenderers + batchCount * 3 + PlannedWaterRendererReserve ||
                estimates.integrated_aggregate_serialized_triangles != NonContextSerializedTriangles + plannedAggregateTriangles ||
                estimates.integrated_visible_draw_slots > limits.visible_draw_slots ||
                estimates.integrated_visible_triangles > limits.visible_triangles ||
                estimates.integrated_visible_materials > limits.visible_materials ||
                estimates.integrated_texture_memory_bytes > limits.texture_memory_bytes ||
                estimates.integrated_transparent_material_slots > limits.transparent_material_slots ||
                estimates.integrated_aggregate_serialized_renderers > limits.aggregate_serialized_renderers ||
                estimates.integrated_aggregate_serialized_triangles > limits.aggregate_serialized_triangles ||
                estimates.maximum_single_mesh_vertices > MaximumSingleMeshVertices ||
                limits.visible_draw_slots > MaximumVisibleDrawSlots ||
                limits.visible_triangles > MaximumVisibleTriangles ||
                limits.aggregate_serialized_renderers > MaximumSerializedRenderers ||
                limits.aggregate_serialized_triangles > MaximumSerializedTriangles)
                throw new InvalidOperationException("Runtime HLOD static budget contract is invalid or over budget");

            return new RuntimeInputs(context, logical, runtime, logicalBuildings);
        }

        private static void ValidateSimpleRuntimeCoverage(
            string label,
            IEnumerable<string> logicalIds,
            RuntimeSimpleBatch[] runtimeBatches)
        {
            HashSet<string> expected = new HashSet<string>(logicalIds, StringComparer.Ordinal);
            HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuntimeSimpleBatch batch in runtimeBatches)
            {
                ValidateLodPolicy(batch.id, batch.lod_policy);
                if (batch.logical_batch_ids == null)
                    throw new InvalidOperationException("Runtime " + label + " coverage is missing");
                foreach (string logicalId in batch.logical_batch_ids)
                {
                    if (!expected.Contains(logicalId) || !covered.Add(logicalId))
                        throw new InvalidOperationException("Runtime " + label + " coverage is unknown or duplicated: " + logicalId);
                }
            }
            if (!covered.SetEquals(expected))
                throw new InvalidOperationException("Runtime HLOD does not preserve every logical " + label + " batch");
        }

        private static void ValidateLodPolicy(string batchId, RuntimeLodPolicy[] policies)
        {
            if (policies == null || policies.Length != 3)
                throw new InvalidOperationException("Runtime batch must define exactly three LOD policies: " + batchId);
            float previousHeight = 2f;
            for (int index = 0; index < policies.Length; index++)
            {
                RuntimeLodPolicy policy = policies[index];
                if (!string.Equals(policy.level, "LOD" + index, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(policy.algorithm) || policy.estimated_triangles <= 0 ||
                    policy.estimated_vertices <= 0 || policy.screen_relative_height <= 0f ||
                    policy.screen_relative_height >= previousHeight)
                    throw new InvalidOperationException("Runtime LOD policy is invalid: " + batchId + " LOD" + index);
                previousHeight = policy.screen_relative_height;
            }
        }

        private static string ComputeSha256(string assetPath)
        {
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(fullPath))
            {
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder value = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest) value.Append(item.ToString("x2"));
                return value.ToString();
            }
        }

        private static void ValidateTextureImport(string path, int maximumSize)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.maxTextureSize != maximumSize ||
                !importer.mipmapEnabled || !importer.streamingMipmaps ||
                importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 8 ||
                importer.mipMapBias > -0.09f ||
                importer.wrapMode != TextureWrapMode.Clamp)
                throw new InvalidOperationException("Terrain texture import contract failed: " + path);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.maxTextureSize != maximumSize ||
                android.format != TextureImporterFormat.ASTC_6x6)
                throw new InvalidOperationException("Terrain Android ASTC contract failed: " + path);
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (!standalone.overridden || standalone.maxTextureSize != maximumSize)
                throw new InvalidOperationException("Terrain simulator/Standalone resolution contract failed: " + path);
        }

        private static void ValidateDetailTextureImport()
        {
            TextureImporter importer = AssetImporter.GetAtPath(DetailPath) as TextureImporter;
            if (importer == null || importer.maxTextureSize != 1024 ||
                !importer.mipmapEnabled || !importer.streamingMipmaps ||
                importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 8 ||
                importer.mipMapBias > -0.14f || importer.wrapMode != TextureWrapMode.Repeat)
                throw new InvalidOperationException("Terrain microdetail import contract failed: " + DetailPath);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.maxTextureSize != 1024 ||
                android.format != TextureImporterFormat.ASTC_6x6)
                throw new InvalidOperationException("Terrain microdetail Android ASTC contract failed: " + DetailPath);
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static void ConfigureTexture(
            string path,
            int maximumSize,
            bool streaming,
            int anisotropy,
            bool clamp,
            float mipBias = 0f)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer is missing: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = streaming;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = anisotropy;
            importer.mipMapBias = mipBias;
            importer.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.maxTextureSize = maximumSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = maximumSize,
                format = TextureImporterFormat.Automatic,
                textureCompression = TextureImporterCompression.CompressedHQ,
                compressionQuality = 75
            });
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = maximumSize,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 75
            });
            importer.SaveAndReimport();
        }

        private static void CreateTerrainMaterial()
        {
            Shader shader = Shader.Find("QuestFlight/KBDU Terrain Pyramid");
            if (shader == null) throw new InvalidOperationException("KBDU terrain-pyramid shader did not import");
            Material material = LoadOrCreateMaterial(Root + "/KBDU_TerrainPyramid.mat", shader);
            material.name = "KBDU_TerrainPyramid";
            material.SetTexture("_NearAlbedo", LoadRequired<Texture2D>(NearPath));
            material.SetTexture("_MidAlbedo", LoadRequired<Texture2D>(MidPath));
            material.SetTexture("_FarAlbedo", LoadRequired<Texture2D>(FarPath));
            material.SetTexture("_DetailAlbedo", LoadRequired<Texture2D>(DetailPath));
            material.SetVector("_NearWorldMinInvSize", new Vector4(-2000f, -2000f, 1f / 4000f, 1f / 4000f));
            material.SetVector("_MidWorldMinInvSize", new Vector4(-6000f, -6000f, 1f / 12000f, 1f / 12000f));
            material.SetVector("_FarWorldMinInvSize", new Vector4(-12000f, -12000f, 1f / 24000f, 1f / 24000f));
            material.SetVector("_NearBlendStartEnd", new Vector4(1650f, 2000f, 0f, 0f));
            material.SetVector("_MidBlendStartEnd", new Vector4(5000f, 6000f, 0f, 0f));
            material.SetVector("_DetailFadeStartEnd", new Vector4(250f, 2600f, 0f, 0f));
            material.SetVector("_DetailRadiusFadeStartEnd", new Vector4(4500f, 6800f, 0f, 0f));
            material.SetFloat("_DetailScale", 1f / 15f);
            material.SetFloat("_Roughness", 0.92f);
            material.SetFloat("_DiagnosticMode", 0f);
            material.SetColor("_Tint", Color.white);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        private static void CreateContextMaterials()
        {
            Shader buildingShader = Shader.Find("QuestFlight/KBDU OSM Building Context");
            if (buildingShader == null) throw new InvalidOperationException("KBDU building shader did not import");
            Material building = LoadOrCreateMaterial(Root + "/KBDU_OsmBuildings.mat", buildingShader);
            building.name = "KBDU_OsmBuildings";
            building.SetTexture("_MainTex", LoadRequired<Texture2D>(AtlasPath));
            building.SetColor("_Tint", Color.white);
            building.SetFloat("_Roughness", 0.86f);
            building.enableInstancing = true;
            EditorUtility.SetDirty(building);

            Shader vegetationShader = Shader.Find("QuestFlight/KBDU Opaque Vegetation Context");
            if (vegetationShader == null) throw new InvalidOperationException("KBDU vegetation shader did not import");
            Material vegetation = LoadOrCreateMaterial(Root + "/KBDU_ContextVegetation.mat", vegetationShader);
            vegetation.name = "KBDU_ContextVegetation";
            vegetation.SetColor("_Tint", Color.white);
            vegetation.enableInstancing = true;
            EditorUtility.SetDirty(vegetation);

            Material parkedAircraft = LoadOrCreateMaterial(Root + "/KBDU_ParkedAircraft.mat", vegetationShader);
            parkedAircraft.name = "KBDU_ParkedAircraft";
            parkedAircraft.SetColor("_Tint", Color.white);
            parkedAircraft.enableInstancing = true;
            EditorUtility.SetDirty(parkedAircraft);
        }

        private static void CreateContextMeshes()
        {
            RuntimeInputs inputs = ValidateRuntimeSourcePlan();
            Dictionary<string, VegetationBatch> vegetationById = inputs.Logical.vegetation_batches
                .ToDictionary(batch => batch.id, StringComparer.Ordinal);
            Dictionary<string, ParkedAircraftBatch> aircraftById = inputs.Logical.parked_aircraft_batches
                .ToDictionary(batch => batch.id, StringComparer.Ordinal);
            List<PendingMesh> pending = new List<PendingMesh>();
            int buildingCount = 0;
            int vegetationCount = 0;
            int parkedAircraftCount = 0;
            try
            {
                foreach (RuntimeBuildingBatch runtimeBatch in inputs.Runtime.building_runtime_batches)
                {
                    BuildingRecord[] records = runtimeBatch.logical_batch_ids
                        .SelectMany(id => inputs.LogicalBuildings[id].buildings).ToArray();
                    for (int lod = 0; lod < 3; lod++)
                    {
                        RuntimeLodPolicy policy = runtimeBatch.lod_policy[lod];
                        AddPending(
                            pending,
                            runtimeBatch.id,
                            lod,
                            policy,
                            BuildBuildingBatch(runtimeBatch.id, records, inputs.Context, policy.algorithm));
                    }
                    buildingCount += records.Length;
                }
                foreach (RuntimeSimpleBatch runtimeBatch in inputs.Runtime.vegetation_runtime_batches)
                {
                    VegetationBatch batch = new VegetationBatch
                    {
                        id = runtimeBatch.id,
                        placements = runtimeBatch.logical_batch_ids
                            .SelectMany(id => vegetationById[id].placements).ToArray()
                    };
                    if (batch.placements.Length != runtimeBatch.placement_count)
                        throw new InvalidOperationException(
                            "Runtime vegetation batch placement count is stale: " + runtimeBatch.id);
                    for (int lod = 0; lod < 3; lod++)
                        AddPending(pending, runtimeBatch.id, lod, runtimeBatch.lod_policy[lod], BuildVegetationBatch(batch, lod));
                    vegetationCount += batch.placements.Length;
                }
                Dictionary<string, GameObject> parkedAircraftSources =
                    new Dictionary<string, GameObject>(StringComparer.Ordinal)
                    {
                        { "powered_c172", LoadRequired<GameObject>(ParkedC172ModelPath) },
                        { "sailplane", LoadRequired<GameObject>(ParkedSailplaneModelPath) }
                    };
                foreach (RuntimeSimpleBatch runtimeBatch in inputs.Runtime.parked_aircraft_runtime_batches)
                {
                    ParkedAircraftBatch batch = aircraftById[runtimeBatch.logical_batch_ids.Single()];
                    for (int lod = 0; lod < 3; lod++)
                        AddPending(
                            pending, runtimeBatch.id, lod, runtimeBatch.lod_policy[lod],
                            BuildParkedAircraftBatch(batch, parkedAircraftSources, lod));
                    parkedAircraftCount += batch.placements.Length;
                }

                long contextWorstCaseTriangles = pending.GroupBy(item => item.BatchId)
                    .Sum(group => group.Max(item => item.Triangles));
                long contextAggregateTriangles = pending.Sum(item => item.Triangles);
                int batchCount = pending.Select(item => item.BatchId).Distinct(StringComparer.Ordinal).Count();
                if (NonContextDrawSlots + batchCount + PlannedWaterRendererReserve > MaximumVisibleDrawSlots ||
                    NonContextTriangles + contextWorstCaseTriangles > MaximumVisibleTriangles ||
                    NonContextSerializedRenderers + batchCount * 3 + PlannedWaterRendererReserve > MaximumSerializedRenderers ||
                    NonContextSerializedTriangles + contextAggregateTriangles > MaximumSerializedTriangles)
                    throw new InvalidOperationException(
                        "Generated runtime HLOD exceeds the integrated static budget: slots=" +
                        (NonContextDrawSlots + batchCount + PlannedWaterRendererReserve) + ", triangles=" +
                        (NonContextTriangles + contextWorstCaseTriangles) + ", serialized renderers=" +
                        (NonContextSerializedRenderers + batchCount * 3 + PlannedWaterRendererReserve) + ", serialized triangles=" +
                        (NonContextSerializedTriangles + contextAggregateTriangles));

                HashSet<string> desiredMeshPaths = new HashSet<string>(pending.Select(item => item.Path), StringComparer.Ordinal);
                foreach (PendingMesh item in pending)
                {
                    SaveMesh(item.Mesh, item.Path);
                    item.Mesh = null;
                }
                foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Generated }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!desiredMeshPaths.Contains(path)) AssetDatabase.DeleteAsset(path);
                }
                Debug.Log(
                    "[QuestFlight][KBDUPhotorealTerrain] Authored " + batchCount +
                    " deterministic three-level runtime HLOD batches preserving " + buildingCount +
                    " buildings, " + vegetationCount + " vegetation placements, and " + parkedAircraftCount +
                    " parked aircraft; planning slots=" +
                    (NonContextDrawSlots + batchCount + PlannedWaterRendererReserve) +
                    ", planning triangles=" + (NonContextTriangles + contextWorstCaseTriangles) +
                    "; no product scene or environment prefab was modified.");
            }
            finally
            {
                foreach (PendingMesh item in pending)
                    if (item.Mesh != null) UnityEngine.Object.DestroyImmediate(item.Mesh);
            }
        }

        private static void AddPending(
            ICollection<PendingMesh> pending,
            string batchId,
            int lod,
            RuntimeLodPolicy policy,
            Mesh mesh)
        {
            long triangles = (long)mesh.GetIndexCount(0) / 3L;
            int vertexCount = mesh.vertexCount;
            if (triangles != policy.estimated_triangles || vertexCount != policy.estimated_vertices)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw new InvalidOperationException(
                    "Runtime HLOD estimate mismatch for " + batchId + " LOD" + lod +
                    ": actual=" + triangles + "/" + vertexCount +
                    ", expected=" + policy.estimated_triangles + "/" + policy.estimated_vertices);
            }
            if (mesh.vertexCount > MaximumSingleMeshVertices)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw new InvalidOperationException("Runtime HLOD mesh exceeds the vertex cap: " + batchId + " LOD" + lod);
            }
            // Mid/far context and simple batches historically use their asset-level LOD
            // names. Near building meshes retain the algorithm name used by the V4
            // filtering evidence and must not be renamed during focused regeneration.
            if (!batchId.StartsWith("buildings_0-2km_", StringComparison.Ordinal))
                mesh.name = batchId + "_LOD" + lod;
            pending.Add(new PendingMesh
            {
                BatchId = batchId,
                Lod = lod,
                Mesh = mesh,
                Path = Generated + "/" + batchId + "_LOD" + lod + ".asset",
                Triangles = triangles
            });
        }

        private static void CreateParkedAircraftMeshes(RuntimeInputs inputs)
        {
            Dictionary<string, ParkedAircraftBatch> aircraftById = inputs.Logical.parked_aircraft_batches
                .ToDictionary(batch => batch.id, StringComparer.Ordinal);
            Dictionary<string, GameObject> sources = new Dictionary<string, GameObject>(StringComparer.Ordinal)
            {
                { "powered_c172", LoadRequired<GameObject>(ParkedC172ModelPath) },
                { "sailplane", LoadRequired<GameObject>(ParkedSailplaneModelPath) }
            };
            List<PendingMesh> pending = new List<PendingMesh>();
            try
            {
                foreach (RuntimeSimpleBatch runtimeBatch in inputs.Runtime.parked_aircraft_runtime_batches)
                {
                    ParkedAircraftBatch batch = aircraftById[runtimeBatch.logical_batch_ids.Single()];
                    for (int lod = 0; lod < 3; lod++)
                        AddPending(
                            pending,
                            runtimeBatch.id,
                            lod,
                            runtimeBatch.lod_policy[lod],
                            BuildParkedAircraftBatch(batch, sources, lod));
                }
                foreach (PendingMesh item in pending)
                {
                    SaveMesh(item.Mesh, item.Path);
                    item.Mesh = null;
                }
            }
            finally
            {
                foreach (PendingMesh item in pending)
                    if (item.Mesh != null) UnityEngine.Object.DestroyImmediate(item.Mesh);
            }
        }

        private static void UpdateParkedAircraftPrefab(RuntimeInputs inputs)
        {
            const string contextPath = Root + "/KBDU_BatchedContext.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(contextPath);
            try
            {
                Material material = LoadRequired<Material>(Root + "/KBDU_ParkedAircraft.mat");
                foreach (RuntimeSimpleBatch runtimeBatch in inputs.Runtime.parked_aircraft_runtime_batches)
                {
                    Transform batch = root.transform.Find(runtimeBatch.id);
                    if (batch == null)
                        throw new InvalidOperationException(
                            "Serialized parked-aircraft batch is missing: " + runtimeBatch.id);
                    LODGroup group = batch.GetComponent<LODGroup>();
                    if (group == null || group.GetLODs().Length != 3)
                        throw new InvalidOperationException(
                            "Serialized parked-aircraft LOD contract is invalid: " + runtimeBatch.id);

                    LOD[] lods = new LOD[3];
                    for (int lod = 0; lod < 3; lod++)
                    {
                        Transform child = batch.Find("LOD" + lod);
                        MeshFilter filter = child == null ? null : child.GetComponent<MeshFilter>();
                        MeshRenderer renderer = child == null ? null : child.GetComponent<MeshRenderer>();
                        if (filter == null || renderer == null)
                            throw new InvalidOperationException(
                                "Serialized parked-aircraft renderer is missing: " + runtimeBatch.id + " LOD" + lod);
                        filter.sharedMesh = LoadRequired<Mesh>(
                            Generated + "/" + runtimeBatch.id + "_LOD" + lod + ".asset");
                        renderer.sharedMaterial = material;
                        lods[lod] = new LOD(
                            runtimeBatch.lod_policy[lod].screen_relative_height,
                            new Renderer[] { renderer });
                    }
                    group.fadeMode = LODFadeMode.None;
                    group.animateCrossFading = false;
                    group.SetLODs(lods);
                    group.RecalculateBounds();
                }
                PrefabUtility.SaveAsPrefabAsset(root, contextPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateContextPrefab()
        {
            RuntimeInputs inputs = ValidateRuntimeSourcePlan();
            Material buildingMaterial = LoadRequired<Material>(Root + "/KBDU_OsmBuildings.mat");
            Material vegetationMaterial = LoadRequired<Material>(Root + "/KBDU_ContextVegetation.mat");
            Material parkedAircraftMaterial = LoadRequired<Material>(Root + "/KBDU_ParkedAircraft.mat");
            GameObject root = new GameObject("KBDU_BatchedContext");
            try
            {
                foreach (RuntimeBuildingBatch batch in inputs.Runtime.building_runtime_batches)
                    AddBatchLod(root.transform, batch.id, buildingMaterial, batch.lod_policy);
                foreach (RuntimeSimpleBatch batch in inputs.Runtime.vegetation_runtime_batches)
                    AddBatchLod(root.transform, batch.id, vegetationMaterial, batch.lod_policy);
                foreach (RuntimeSimpleBatch batch in inputs.Runtime.parked_aircraft_runtime_batches)
                    AddBatchLod(root.transform, batch.id, parkedAircraftMaterial, batch.lod_policy);
                PrefabUtility.SaveAsPrefabAsset(root, Root + "/KBDU_BatchedContext.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AddBatchLod(
            Transform parent,
            string batchId,
            Material material,
            RuntimeLodPolicy[] policies)
        {
            GameObject batch = new GameObject(batchId);
            batch.transform.SetParent(parent, false);
            Renderer lod0 = AddMeshRenderer(batch.transform, "LOD0", batchId + "_LOD0", material);
            Renderer lod1 = AddMeshRenderer(batch.transform, "LOD1", batchId + "_LOD1", material);
            Renderer lod2 = AddMeshRenderer(batch.transform, "LOD2", batchId + "_LOD2", material);
            LODGroup group = batch.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            group.SetLODs(new[]
            {
                new LOD(policies[0].screen_relative_height, new[] { lod0 }),
                new LOD(policies[1].screen_relative_height, new[] { lod1 }),
                new LOD(policies[2].screen_relative_height, new[] { lod2 })
            });
            group.RecalculateBounds();
        }

        private static Renderer AddMeshRenderer(
            Transform parent,
            string name,
            string meshName,
            Material material)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = LoadRequired<Mesh>(Generated + "/" + meshName + ".asset");
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private static Mesh BuildBuildingBatch(
            string batchId,
            IReadOnlyList<BuildingRecord> records,
            ContextDocument context,
            string algorithm)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> indices = new List<int>();
            float quantization = context.coordinate_quantization_meters;
            if (algorithm.StartsWith("cluster_boxes_", StringComparison.Ordinal) &&
                algorithm.EndsWith("m", StringComparison.Ordinal))
            {
                string numeric = algorithm.Substring("cluster_boxes_".Length);
                numeric = numeric.Substring(0, numeric.Length - 1);
                if (!float.TryParse(numeric, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float clusterSize) || clusterSize <= 0f)
                    throw new InvalidOperationException("Invalid runtime cluster algorithm: " + algorithm);
                SortedDictionary<long, BuildingCluster> clusters = new SortedDictionary<long, BuildingCluster>();
                foreach (BuildingRecord record in records)
                {
                    ContextFeature feature = BuildingFeature(record, context);
                    List<Vector2> footprint = DecodeFootprint(feature.points_q, quantization);
                    if (footprint.Count < 3) continue;
                    Vector2 centroid = footprint.Aggregate(Vector2.zero, (sum, point) => sum + point) / footprint.Count;
                    int cellX = Mathf.FloorToInt(centroid.x / clusterSize);
                    int cellZ = Mathf.FloorToInt(centroid.y / clusterSize);
                    long key = ((long)cellX << 32) ^ (uint)cellZ;
                    if (!clusters.TryGetValue(key, out BuildingCluster cluster))
                    {
                        cluster = new BuildingCluster(record.facade_tile, record.roof_tile);
                        clusters.Add(key, cluster);
                    }
                    cluster.Add(footprint, record.base_y_meters, Mathf.Max(3.2f, feature.render_height_meters));
                }
                foreach (BuildingCluster cluster in clusters.Values)
                    AddExtrudedBuilding(cluster.Rectangle(), cluster.AverageBase,
                        Mathf.Max(3.2f, cluster.MaximumTop - cluster.AverageBase),
                        cluster.FacadeTile, cluster.RoofTile, vertices, normals, uvs, indices);
            }
            else
            {
                bool boxes = string.Equals(algorithm, "building_boxes", StringComparison.Ordinal);
                if (!boxes && !string.Equals(algorithm, "detailed_footprints", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsupported runtime building algorithm: " + algorithm);
                foreach (BuildingRecord record in records)
                {
                    ContextFeature feature = BuildingFeature(record, context);
                    List<Vector2> footprint = DecodeFootprint(feature.points_q, quantization);
                    if (footprint.Count < 3) continue;
                    if (boxes) footprint = BoundingRectangle(footprint);
                    EnsureClockwise(footprint);
                    AddExtrudedBuilding(
                        footprint, record.base_y_meters, Mathf.Max(3.2f, feature.render_height_meters),
                        record.facade_tile, record.roof_tile, vertices, normals, uvs, indices);
                }
            }
            Mesh mesh = new Mesh { name = batchId + "_" + algorithm };
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static ContextFeature BuildingFeature(BuildingRecord record, ContextDocument context)
        {
            if (record.feature_index < 0 || record.feature_index >= context.openstreetmap.features.Length)
                throw new InvalidOperationException("Building feature index is out of range: " + record.feature_index);
            return context.openstreetmap.features[record.feature_index];
        }

        private static List<Vector2> BoundingRectangle(IReadOnlyList<Vector2> footprint)
        {
            float minX = footprint.Min(point => point.x);
            float maxX = footprint.Max(point => point.x);
            float minZ = footprint.Min(point => point.y);
            float maxZ = footprint.Max(point => point.y);
            return new List<Vector2>
            {
                new Vector2(minX, minZ), new Vector2(minX, maxZ),
                new Vector2(maxX, maxZ), new Vector2(maxX, minZ)
            };
        }

        private static void AddExtrudedBuilding(
            List<Vector2> footprint,
            float baseY,
            float height,
            int facadeTile,
            int roofTile,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> indices)
        {
            int roofStart = vertices.Count;
            float minX = footprint.Min(point => point.x);
            float maxX = footprint.Max(point => point.x);
            float minZ = footprint.Min(point => point.y);
            float maxZ = footprint.Max(point => point.y);
            Rect roofRect = AtlasRect(roofTile);
            foreach (Vector2 point in footprint)
            {
                vertices.Add(new Vector3(point.x, baseY + height, point.y));
                normals.Add(Vector3.up);
                float u = Mathf.InverseLerp(minX, maxX, point.x);
                float v = Mathf.InverseLerp(minZ, maxZ, point.y);
                uvs.Add(new Vector2(Mathf.Lerp(roofRect.xMin, roofRect.xMax, u), Mathf.Lerp(roofRect.yMin, roofRect.yMax, v)));
            }
            foreach (int localIndex in TriangulateClockwise(footprint)) indices.Add(roofStart + localIndex);

            Rect facadeRect = AtlasRect(facadeTile);
            for (int edge = 0; edge < footprint.Count; edge++)
            {
                Vector2 a = footprint[edge];
                Vector2 b = footprint[(edge + 1) % footprint.Count];
                Vector2 edgeVector = b - a;
                Vector3 outward = new Vector3(-edgeVector.y, 0f, edgeVector.x).normalized;
                int start = vertices.Count;
                vertices.Add(new Vector3(a.x, baseY, a.y));
                vertices.Add(new Vector3(a.x, baseY + height, a.y));
                vertices.Add(new Vector3(b.x, baseY + height, b.y));
                vertices.Add(new Vector3(b.x, baseY, b.y));
                normals.Add(outward);
                normals.Add(outward);
                normals.Add(outward);
                normals.Add(outward);
                uvs.Add(new Vector2(facadeRect.xMin, facadeRect.yMin));
                uvs.Add(new Vector2(facadeRect.xMin, facadeRect.yMax));
                uvs.Add(new Vector2(facadeRect.xMax, facadeRect.yMax));
                uvs.Add(new Vector2(facadeRect.xMax, facadeRect.yMin));
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 1);
                indices.Add(start);
                indices.Add(start + 3);
                indices.Add(start + 2);
            }
        }

        private static Mesh BuildParkedAircraftBatch(
            ParkedAircraftBatch batch,
            IReadOnlyDictionary<string, GameObject> sourceModels,
            int lod)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Color32> colors = new List<Color32>();
            List<int> indices = new List<int>();
            if (lod >= 1)
            {
                foreach (ParkedAircraftPlacement placement in batch.placements)
                {
                    SourceAircraft sourceAircraft = ResolveAircraftSource(sourceModels, placement, 1);
                    Color32 color = PlacementColor(placement, batch.id);
                    Quaternion rotation = AircraftRotation(placement);
                    float groundingOffset = AircraftGroundingOffset(sourceAircraft, rotation);
                    Matrix4x4 placementMatrix = Matrix4x4.TRS(
                        new Vector3(
                            placement.x_meters,
                            placement.base_y_meters + groundingOffset,
                            placement.z_meters),
                        rotation,
                        Vector3.one);
                    AddAircraftPlanformProxy(
                        sourceAircraft.Bounds, placementMatrix, color,
                        vertices, normals, colors, indices);
                }
                Mesh proxyMesh = new Mesh { name = batch.id + "_LOD" + lod };
                proxyMesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
                proxyMesh.SetVertices(vertices);
                proxyMesh.SetNormals(normals);
                proxyMesh.SetColors(colors);
                proxyMesh.SetTriangles(indices, 0, true);
                proxyMesh.RecalculateBounds();
                return proxyMesh;
            }
            foreach (ParkedAircraftPlacement placement in batch.placements)
            {
                SourceAircraft sourceAircraft = ResolveAircraftSource(sourceModels, placement, lod);
                Mesh source = sourceAircraft.Mesh;
                Vector3[] sourceVertices = source.vertices;
                Vector3[] sourceNormals = source.normals;
                Color32[] sourceColors = source.colors32;
                int[] sourceTriangles = source.triangles;
                if (sourceNormals.Length != sourceVertices.Length)
                    throw new InvalidOperationException(
                        "Parked-aircraft source normals are incomplete: " + sourceAircraft.Name);
                Color32 color = PlacementColor(placement, batch.id);
                Quaternion rotation = AircraftRotation(placement);
                float groundingOffset = AircraftGroundingOffset(sourceAircraft, rotation);
                Matrix4x4 placementMatrix = Matrix4x4.TRS(
                    new Vector3(
                        placement.x_meters, placement.base_y_meters + groundingOffset, placement.z_meters),
                    rotation,
                    Vector3.one) * sourceAircraft.SourceToRoot;
                Matrix4x4 normalMatrix = placementMatrix.inverse.transpose;
                int start = vertices.Count;
                for (int index = 0; index < sourceVertices.Length; index++)
                {
                    vertices.Add(placementMatrix.MultiplyPoint3x4(sourceVertices[index]));
                    normals.Add(normalMatrix.MultiplyVector(sourceNormals[index]).normalized);
                    colors.Add(sourceColors.Length == sourceVertices.Length
                        ? MultiplyColor(sourceColors[index], color)
                        : color);
                }
                foreach (int sourceIndex in sourceTriangles) indices.Add(start + sourceIndex);
            }

            Mesh mesh = new Mesh { name = batch.id + "_LOD" + lod };
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Quaternion AircraftRotation(ParkedAircraftPlacement placement)
        {
            Quaternion yaw = Quaternion.AngleAxis(placement.yaw_degrees, Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(placement.pitch_degrees, Vector3.right);
            Quaternion roll = Quaternion.AngleAxis(placement.roll_degrees, Vector3.forward);
            return yaw * pitch * roll;
        }

        private static float AircraftGroundingOffset(SourceAircraft sourceAircraft, Quaternion rotation)
        {
            Matrix4x4 rotatedSource = Matrix4x4.Rotate(rotation) * sourceAircraft.SourceToRoot;
            float minimumY = float.PositiveInfinity;
            foreach (Vector3 vertex in sourceAircraft.Mesh.vertices)
                minimumY = Mathf.Min(minimumY, rotatedSource.MultiplyPoint3x4(vertex).y);
            if (!float.IsFinite(minimumY))
                throw new InvalidOperationException(
                    "Parked-aircraft rotated grounding calculation is nonfinite: " + sourceAircraft.Name);
            return -minimumY + 0.025f;
        }

        private static void AddAircraftPlanformProxy(
            Bounds bounds,
            Matrix4x4 placement,
            Color32 color,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color32> colors,
            ICollection<int> indices)
        {
            float span = bounds.size.x;
            float length = bounds.size.z;
            float surfaceY = bounds.min.y + bounds.size.y * 0.42f;
            AddAircraftProxyQuad(
                bounds.center.x, surfaceY, bounds.center.z + length * 0.06f,
                span, Mathf.Max(0.45f, length * 0.14f),
                placement, color, vertices, normals, colors, indices);
            AddAircraftProxyQuad(
                bounds.center.x, surfaceY + 0.04f, bounds.center.z,
                Mathf.Max(0.65f, span * 0.085f), length,
                placement, color, vertices, normals, colors, indices);
            AddAircraftProxyQuad(
                bounds.center.x, surfaceY + 0.02f, bounds.center.z - length * 0.38f,
                span * 0.32f, Mathf.Max(0.35f, length * 0.09f),
                placement, color, vertices, normals, colors, indices);
        }

        private static void AddAircraftProxyQuad(
            float centerX,
            float y,
            float centerZ,
            float width,
            float depth,
            Matrix4x4 placement,
            Color32 color,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color32> colors,
            ICollection<int> indices)
        {
            int start = vertices.Count;
            Vector3 normal = placement.MultiplyVector(Vector3.up).normalized;
            Vector3[] local =
            {
                new Vector3(centerX - width * 0.5f, y, centerZ - depth * 0.5f),
                new Vector3(centerX + width * 0.5f, y, centerZ - depth * 0.5f),
                new Vector3(centerX + width * 0.5f, y, centerZ + depth * 0.5f),
                new Vector3(centerX - width * 0.5f, y, centerZ + depth * 0.5f)
            };
            foreach (Vector3 point in local)
            {
                vertices.Add(placement.MultiplyPoint3x4(point));
                normals.Add(normal);
                colors.Add(color);
            }
            indices.Add(start);
            indices.Add(start + 2);
            indices.Add(start + 1);
            indices.Add(start);
            indices.Add(start + 3);
            indices.Add(start + 2);
        }

        private static SourceAircraft ResolveAircraftSource(
            IReadOnlyDictionary<string, GameObject> sourceModels,
            ParkedAircraftPlacement placement,
            int lod)
        {
            string family = string.IsNullOrWhiteSpace(placement.model_family)
                ? "powered_c172"
                : placement.model_family;
            if (!sourceModels.TryGetValue(family, out GameObject sourceModel))
                throw new InvalidOperationException("Unknown parked-aircraft family: " + family);
            string prefix = family == "sailplane" ? "ParkedSailplane_LOD" : "ParkedC172_LOD";
            string lodName = prefix + Math.Min(lod, 1);
            MeshFilter sourceFilter = sourceModel.GetComponentsInChildren<MeshFilter>(true)
                .SingleOrDefault(filter => filter.name.StartsWith(lodName, StringComparison.Ordinal));
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                throw new InvalidOperationException("Parked-aircraft source LOD is missing: " + lodName);
            Matrix4x4 sourceToRoot =
                sourceModel.transform.worldToLocalMatrix * sourceFilter.transform.localToWorldMatrix;
            return new SourceAircraft
            {
                Name = lodName,
                Mesh = sourceFilter.sharedMesh,
                SourceToRoot = sourceToRoot,
                Bounds = TransformBounds(sourceFilter.sharedMesh.bounds, sourceToRoot)
            };
        }

        private static Color32 MultiplyColor(Color32 source, Color32 placement)
        {
            return new Color32(
                (byte)((source.r * placement.r + 127) / 255),
                (byte)((source.g * placement.g + 127) / 255),
                (byte)((source.b * placement.b + 127) / 255),
                255);
        }

        private static Color32 PlacementColor(ParkedAircraftPlacement placement, string batchId)
        {
            if (placement.color_rgba == null || placement.color_rgba.Length != 4)
                throw new InvalidOperationException("Parked-aircraft color contract is invalid in " + batchId);
            return new Color32(
                (byte)placement.color_rgba[0], (byte)placement.color_rgba[1],
                (byte)placement.color_rgba[2], (byte)placement.color_rgba[3]);
        }

        private static Bounds TransformBounds(Bounds source, Matrix4x4 matrix)
        {
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(source.center), Vector3.zero);
            Vector3 extents = source.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(matrix.MultiplyPoint3x4(source.center + Vector3.Scale(extents, new Vector3(x, y, z))));
            return result;
        }

        private static Mesh BuildVegetationBatch(VegetationBatch batch, int lod)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Color32> colors = new List<Color32>();
            List<int> indices = new List<int>();
            for (int placementIndex = 0; placementIndex < batch.placements.Length; placementIndex++)
            {
                VegetationPlacement placement = batch.placements[placementIndex];
                Vector3 origin = new Vector3(placement.x_meters, placement.base_y_meters, placement.z_meters);
                if (lod == 2)
                {
                    AddOctahedralCrown(
                        origin,
                        placement.height_meters,
                        placement.crown_radius_meters,
                        vertices,
                        colors,
                        indices);
                }
                else if (lod == 1)
                {
                    AddColoredBox(
                        origin + Vector3.up * placement.height_meters * 0.23f,
                        new Vector3(0.28f, placement.height_meters * 0.46f, 0.28f),
                        new Color32(78, 65, 47, 255),
                        vertices,
                        colors,
                        indices);
                    Vector3 lowCrownCenter = origin + Vector3.up * placement.height_meters * 0.69f;
                    AddDeciduousCrown(
                        lowCrownCenter,
                        new Vector3(
                            placement.crown_radius_meters * 0.92f,
                            placement.height_meters * 0.25f,
                            placement.crown_radius_meters),
                        7,
                        new Color32(56, 88, 48, 255),
                        vertices,
                        colors,
                        indices);
                    AddDeciduousCrown(
                        lowCrownCenter + new Vector3(-placement.crown_radius_meters * 0.45f, -0.12f, 0.14f),
                        new Vector3(
                            placement.crown_radius_meters * 0.54f,
                            placement.height_meters * 0.16f,
                            placement.crown_radius_meters * 0.50f),
                        5,
                        new Color32(64, 98, 52, 255),
                        vertices,
                        colors,
                        indices);
                    AddDeciduousCrown(
                        lowCrownCenter + new Vector3(placement.crown_radius_meters * 0.42f, 0.10f, -0.18f),
                        new Vector3(
                            placement.crown_radius_meters * 0.50f,
                            placement.height_meters * 0.15f,
                            placement.crown_radius_meters * 0.55f),
                        5,
                        new Color32(70, 105, 56, 255),
                        vertices,
                        colors,
                        indices);
                }
                else
                {
                    AddColoredBox(
                        origin + Vector3.up * placement.height_meters * 0.24f,
                        new Vector3(0.34f, placement.height_meters * 0.48f, 0.34f),
                        new Color32(92, 76, 54, 255),
                        vertices,
                        colors,
                        indices);
                    Vector3 crownCenter = origin + Vector3.up * placement.height_meters * 0.69f;
                    bool nearZone = string.Equals(placement.zone, "near", StringComparison.Ordinal);
                    if (nearZone)
                    {
                        AddRoundedDeciduousCrown(
                            crownCenter,
                            new Vector3(
                                placement.crown_radius_meters * 1.08f,
                                placement.height_meters * 0.25f,
                                placement.crown_radius_meters),
                            8,
                            new Color32(58, 93, 50, 255),
                            vertices,
                            colors,
                            indices);
                        AddRoundedDeciduousCrown(
                            crownCenter + new Vector3(-placement.crown_radius_meters * 0.48f, -0.10f, 0.18f),
                            new Vector3(
                                placement.crown_radius_meters * 0.58f,
                                placement.height_meters * 0.17f,
                                placement.crown_radius_meters * 0.56f),
                            6,
                            new Color32(66, 101, 55, 255),
                            vertices,
                            colors,
                            indices);
                        AddRoundedDeciduousCrown(
                            crownCenter + new Vector3(placement.crown_radius_meters * 0.46f, 0.10f, -0.22f),
                            new Vector3(
                                placement.crown_radius_meters * 0.56f,
                                placement.height_meters * 0.16f,
                                placement.crown_radius_meters * 0.60f),
                            6,
                            new Color32(73, 109, 59, 255),
                            vertices,
                            colors,
                            indices);
                    }
                    else
                    {
                        AddDeciduousCrown(
                            crownCenter,
                            new Vector3(
                                placement.crown_radius_meters * 1.08f,
                                placement.height_meters * 0.25f,
                                placement.crown_radius_meters),
                            6,
                            new Color32(73, 111, 58, 255),
                            vertices,
                            colors,
                            indices);
                        AddDeciduousCrown(
                            crownCenter + new Vector3(-placement.crown_radius_meters * 0.55f, -0.15f, 0.18f),
                            new Vector3(
                                placement.crown_radius_meters * 0.64f,
                                placement.height_meters * 0.18f,
                                placement.crown_radius_meters * 0.60f),
                            5,
                            new Color32(85, 122, 64, 255),
                            vertices,
                            colors,
                            indices);
                        AddDeciduousCrown(
                            crownCenter + new Vector3(placement.crown_radius_meters * 0.50f, 0.12f, -0.24f),
                            new Vector3(
                                placement.crown_radius_meters * 0.60f,
                                placement.height_meters * 0.17f,
                                placement.crown_radius_meters * 0.64f),
                            5,
                            new Color32(96, 132, 70, 255),
                            vertices,
                            colors,
                            indices);
                    }
                    if (placementIndex % 12 == 0)
                    {
                        AddDeciduousCrown(
                            origin + new Vector3(placement.crown_radius_meters * 1.4f, 0.62f, -0.45f),
                            new Vector3(0.95f, 0.62f, 0.82f),
                            4,
                            new Color32(91, 112, 63, 255),
                            vertices,
                            colors,
                            indices);
                    }
                }
            }
            Mesh mesh = new Mesh { name = batch.id + "_LOD" + lod };
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddDeciduousCrown(
            Vector3 center,
            Vector3 radius,
            int segments,
            Color32 color,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> indices)
        {
            int start = vertices.Count;
            vertices.Add(center + Vector3.up * radius.y);
            vertices.Add(center - Vector3.up * radius.y);
            colors.Add(Lighten(color, 12));
            colors.Add(Darken(color, 11));
            for (int index = 0; index < segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                vertices.Add(center + new Vector3(
                    Mathf.Cos(angle) * radius.x,
                    Mathf.Sin(angle * 2f) * radius.y * 0.08f,
                    Mathf.Sin(angle) * radius.z));
                colors.Add(index % 2 == 0 ? color : Darken(color, 5));
            }
            for (int index = 0; index < segments; index++)
            {
                int current = start + 2 + index;
                int next = start + 2 + (index + 1) % segments;
                indices.Add(start); indices.Add(next); indices.Add(current);
                indices.Add(start + 1); indices.Add(current); indices.Add(next);
            }
        }

        private static void AddRoundedDeciduousCrown(
            Vector3 center,
            Vector3 radius,
            int segments,
            Color32 color,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> indices)
        {
            int start = vertices.Count;
            vertices.Add(center + Vector3.up * radius.y);
            vertices.Add(center - Vector3.up * radius.y);
            colors.Add(Lighten(color, 16));
            colors.Add(Darken(color, 14));
            for (int ring = 0; ring < 2; ring++)
            {
                float y = ring == 0 ? radius.y * 0.30f : -radius.y * 0.30f;
                float horizontalScale = ring == 0 ? 0.88f : 0.96f;
                for (int index = 0; index < segments; index++)
                {
                    float angle = index * Mathf.PI * 2f / segments;
                    vertices.Add(center + new Vector3(
                        Mathf.Cos(angle) * radius.x * horizontalScale,
                        y + Mathf.Sin(angle * 2f) * radius.y * 0.035f,
                        Mathf.Sin(angle) * radius.z * horizontalScale));
                    Color32 ringColor = ring == 0 ? Lighten(color, 5) : Darken(color, 4);
                    colors.Add(index % 2 == 0 ? ringColor : Darken(ringColor, 3));
                }
            }

            int upperStart = start + 2;
            int lowerStart = upperStart + segments;
            for (int index = 0; index < segments; index++)
            {
                int next = (index + 1) % segments;
                indices.Add(start); indices.Add(upperStart + next); indices.Add(upperStart + index);
                indices.Add(upperStart + index); indices.Add(upperStart + next); indices.Add(lowerStart + next);
                indices.Add(upperStart + index); indices.Add(lowerStart + next); indices.Add(lowerStart + index);
                indices.Add(start + 1); indices.Add(lowerStart + index); indices.Add(lowerStart + next);
            }
        }

        private static Color32 Lighten(Color32 color, byte amount) => new Color32(
            (byte)Mathf.Min(255, color.r + amount),
            (byte)Mathf.Min(255, color.g + amount),
            (byte)Mathf.Min(255, color.b + amount),
            color.a);

        private static Color32 Darken(Color32 color, byte amount) => new Color32(
            (byte)Mathf.Max(0, color.r - amount),
            (byte)Mathf.Max(0, color.g - amount),
            (byte)Mathf.Max(0, color.b - amount),
            color.a);

        private static void AddHexCrown(
            Vector3 origin,
            float height,
            float radius,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> indices)
        {
            int start = vertices.Count;
            float centerY = origin.y + height * 0.72f;
            vertices.Add(new Vector3(origin.x, origin.y + height, origin.z));
            vertices.Add(new Vector3(origin.x, origin.y + height * 0.48f, origin.z));
            colors.Add(new Color32(63, 105, 49, 255));
            colors.Add(new Color32(48, 80, 39, 255));
            for (int index = 0; index < 6; index++)
            {
                float angle = index * Mathf.PI * 2f / 6f;
                vertices.Add(new Vector3(origin.x + Mathf.Cos(angle) * radius, centerY, origin.z + Mathf.Sin(angle) * radius));
                colors.Add(new Color32((byte)(67 + index % 3 * 5), (byte)(111 + index % 2 * 7), 51, 255));
            }
            for (int index = 0; index < 6; index++)
            {
                int current = start + 2 + index;
                int next = start + 2 + (index + 1) % 6;
                indices.Add(start);
                indices.Add(next);
                indices.Add(current);
                indices.Add(start + 1);
                indices.Add(current);
                indices.Add(next);
            }
        }

        private static void AddOctahedralCrown(
            Vector3 origin,
            float height,
            float radius,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> indices)
        {
            int start = vertices.Count;
            float centerY = origin.y + height * 0.70f;
            vertices.Add(new Vector3(origin.x, origin.y + height, origin.z));
            vertices.Add(new Vector3(origin.x, origin.y + height * 0.48f, origin.z));
            vertices.Add(new Vector3(origin.x + radius, centerY, origin.z));
            vertices.Add(new Vector3(origin.x, centerY, origin.z + radius));
            vertices.Add(new Vector3(origin.x - radius, centerY, origin.z));
            vertices.Add(new Vector3(origin.x, centerY, origin.z - radius));
            for (int index = 0; index < 6; index++) colors.Add(new Color32(61, 104, 48, 255));
            int[] local = { 0, 3, 2, 0, 4, 3, 0, 5, 4, 0, 2, 5, 1, 2, 3, 1, 3, 4, 1, 4, 5, 1, 5, 2 };
            foreach (int value in local) indices.Add(start + value);
        }

        private static void AddColoredBox(
            Vector3 center,
            Vector3 size,
            Color32 color,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> indices)
        {
            Vector3 half = size * 0.5f;
            Vector3[] corners =
            {
                center + new Vector3(-half.x,-half.y,-half.z), center + new Vector3(half.x,-half.y,-half.z),
                center + new Vector3(half.x,-half.y,half.z), center + new Vector3(-half.x,-half.y,half.z),
                center + new Vector3(-half.x,half.y,-half.z), center + new Vector3(half.x,half.y,-half.z),
                center + new Vector3(half.x,half.y,half.z), center + new Vector3(-half.x,half.y,half.z)
            };
            int[,] faces = { {0,1,5,4}, {1,2,6,5}, {2,3,7,6}, {3,0,4,7}, {4,5,6,7}, {3,2,1,0} };
            for (int face = 0; face < 6; face++)
            {
                int start = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices.Add(corners[faces[face, corner]]);
                    colors.Add(color);
                }
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 3);
            }
        }

        private static List<Vector2> DecodeFootprint(int[] values, float quantization)
        {
            List<Vector2> points = new List<Vector2>();
            if (values == null) return points;
            for (int index = 0; index + 1 < values.Length; index += 2)
                points.Add(new Vector2(values[index] * quantization, values[index + 1] * quantization));
            if (points.Count > 1 && (points[0] - points[points.Count - 1]).sqrMagnitude < 0.0001f)
                points.RemoveAt(points.Count - 1);
            return points;
        }

        private static void EnsureClockwise(List<Vector2> points)
        {
            if (SignedArea(points) > 0f) points.Reverse();
        }

        private static float SignedArea(IReadOnlyList<Vector2> points)
        {
            float value = 0f;
            for (int index = 0; index < points.Count; index++)
            {
                Vector2 a = points[index];
                Vector2 b = points[(index + 1) % points.Count];
                value += a.x * b.y - b.x * a.y;
            }
            return value * 0.5f;
        }

        private static List<int> TriangulateClockwise(IReadOnlyList<Vector2> points)
        {
            List<int> remaining = Enumerable.Range(0, points.Count).ToList();
            List<int> result = new List<int>();
            int guard = points.Count * points.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int cursor = 0; cursor < remaining.Count; cursor++)
                {
                    int previous = remaining[(cursor + remaining.Count - 1) % remaining.Count];
                    int current = remaining[cursor];
                    int next = remaining[(cursor + 1) % remaining.Count];
                    if (Cross(points[current] - points[previous], points[next] - points[current]) >= -0.00001f)
                        continue;
                    bool contains = false;
                    for (int other = 0; other < remaining.Count; other++)
                    {
                        int candidate = remaining[other];
                        if (candidate == previous || candidate == current || candidate == next) continue;
                        if (PointInTriangle(points[candidate], points[previous], points[current], points[next]))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (contains) continue;
                    result.Add(previous);
                    result.Add(current);
                    result.Add(next);
                    remaining.RemoveAt(cursor);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (remaining.Count == 3)
            {
                result.Add(remaining[0]);
                result.Add(remaining[1]);
                result.Add(remaining[2]);
            }
            if (result.Count < (points.Count - 2) * 3)
            {
                result.Clear();
                for (int index = 1; index + 1 < points.Count; index++)
                {
                    result.Add(0);
                    result.Add(index);
                    result.Add(index + 1);
                }
            }
            return result;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float first = Cross(b - a, point - a);
            float second = Cross(c - b, point - b);
            float third = Cross(a - c, point - c);
            bool hasNegative = first < -0.00001f || second < -0.00001f || third < -0.00001f;
            bool hasPositive = first > 0.00001f || second > 0.00001f || third > 0.00001f;
            return !(hasNegative && hasPositive);
        }

        private static Rect AtlasRect(int tile)
        {
            const float cell = 0.25f;
            int column = Mathf.Abs(tile) % 4;
            int rowFromTop = (Mathf.Abs(tile) / 4) % 4;
            float minX = column * cell + 0.003f;
            float maxX = (column + 1) * cell - 0.003f;
            float minY = 1f - (rowFromTop + 1) * cell + 0.003f;
            float maxY = 1f - rowFromTop * cell - 0.003f;
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static T ReadJson<T>(string path) where T : class
        {
            TextAsset asset = LoadRequired<TextAsset>(path);
            T value = JsonUtility.FromJson<T>(asset.text);
            if (value == null) throw new InvalidOperationException("Could not parse JSON asset: " + path);
            return value;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            return material;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return value;
        }

        private static void SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
            }
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (string segment in path.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
        }

        private sealed class RuntimeInputs
        {
            public RuntimeInputs(
                ContextDocument context,
                ContextRecipe logical,
                RuntimeHlodRecipe runtime,
                Dictionary<string, BuildingBatch> logicalBuildings)
            {
                Context = context;
                Logical = logical;
                Runtime = runtime;
                LogicalBuildings = logicalBuildings;
            }

            public ContextDocument Context { get; }
            public ContextRecipe Logical { get; }
            public RuntimeHlodRecipe Runtime { get; }
            public Dictionary<string, BuildingBatch> LogicalBuildings { get; }
        }

        private sealed class PendingMesh
        {
            public string BatchId;
            public int Lod;
            public Mesh Mesh;
            public string Path;
            public long Triangles;
        }

        private sealed class BuildingCluster
        {
            private float minX = float.PositiveInfinity;
            private float minZ = float.PositiveInfinity;
            private float maxX = float.NegativeInfinity;
            private float maxZ = float.NegativeInfinity;
            private float baseSum;
            private int count;

            public BuildingCluster(int facadeTile, int roofTile)
            {
                FacadeTile = facadeTile;
                RoofTile = roofTile;
            }

            public int FacadeTile { get; }
            public int RoofTile { get; }
            public float MaximumTop { get; private set; } = float.NegativeInfinity;
            public float AverageBase => baseSum / count;

            public void Add(IReadOnlyList<Vector2> footprint, float baseY, float height)
            {
                foreach (Vector2 point in footprint)
                {
                    minX = Mathf.Min(minX, point.x);
                    minZ = Mathf.Min(minZ, point.y);
                    maxX = Mathf.Max(maxX, point.x);
                    maxZ = Mathf.Max(maxZ, point.y);
                }
                baseSum += baseY;
                count++;
                MaximumTop = Mathf.Max(MaximumTop, baseY + height);
            }

            public List<Vector2> Rectangle() => new List<Vector2>
            {
                new Vector2(minX, minZ), new Vector2(minX, maxZ),
                new Vector2(maxX, maxZ), new Vector2(maxX, minZ)
            };
        }

        [Serializable]
        private sealed class RuntimeHlodRecipe
        {
            public string source_logical_recipe_sha256;
            public string source_context_sha256;
            public RuntimeBuildingBatch[] building_runtime_batches;
            public RuntimeSimpleBatch[] vegetation_runtime_batches;
            public RuntimeSimpleBatch[] parked_aircraft_runtime_batches;
            public int vegetation_placement_count;
            public string[] vegetation_species_ids;
            public StaticBudget static_budget;
        }

        [Serializable]
        private sealed class RuntimeBuildingBatch
        {
            public string id;
            public string[] logical_batch_ids;
            public RuntimeLodPolicy[] lod_policy;
        }

        [Serializable]
        private sealed class RuntimeSimpleBatch
        {
            public string id;
            public string[] logical_batch_ids;
            public RuntimeLodPolicy[] lod_policy;
            public int placement_count;
        }

        [Serializable]
        private sealed class RuntimeLodPolicy
        {
            public string level;
            public string algorithm;
            public long estimated_triangles;
            public int estimated_vertices;
            public float screen_relative_height;
        }

        [Serializable]
        private sealed class StaticBudget
        {
            public string status;
            public BudgetLimits limits;
            public BudgetEstimates estimates;
        }

        [Serializable]
        private sealed class BudgetLimits
        {
            public int aggregate_serialized_renderers;
            public long aggregate_serialized_triangles;
            public int visible_draw_slots;
            public long visible_triangles;
            public int visible_materials;
            public long texture_memory_bytes;
            public int transparent_material_slots;
        }

        [Serializable]
        private sealed class BudgetEstimates
        {
            public int context_batch_count;
            public long maximum_single_mesh_vertices;
            public int integrated_aggregate_serialized_renderers;
            public long integrated_aggregate_serialized_triangles;
            public int integrated_visible_draw_slots;
            public long integrated_visible_triangles;
            public int integrated_visible_materials;
            public long integrated_texture_memory_bytes;
            public int integrated_transparent_material_slots;
        }

        [Serializable]
        private sealed class ContextDocument
        {
            public float coordinate_quantization_meters;
            public OsmPayload openstreetmap;
        }

        [Serializable]
        private sealed class OsmPayload
        {
            public ContextFeature[] features;
        }

        [Serializable]
        private sealed class ContextFeature
        {
            public string category;
            public string geometry_type;
            public int[] points_q;
            public float render_height_meters;
        }

        [Serializable]
        private sealed class ContextRecipe
        {
            public BuildingBatch[] building_batches;
            public VegetationBatch[] vegetation_batches;
            public ParkedAircraftBatch[] parked_aircraft_batches;
        }

        [Serializable]
        private sealed class BuildingBatch
        {
            public string id;
            public BuildingRecord[] buildings;
        }

        [Serializable]
        private sealed class BuildingRecord
        {
            public int feature_index;
            public float base_y_meters;
            public int facade_tile;
            public int roof_tile;
        }

        [Serializable]
        private sealed class VegetationBatch
        {
            public string id;
            public VegetationPlacement[] placements;
        }

        [Serializable]
        private sealed class VegetationPlacement
        {
            public float x_meters;
            public float base_y_meters;
            public float z_meters;
            public float height_meters;
            public float crown_radius_meters;
            public string zone;
            public string stable_id;
            public string species_id;
        }

        [Serializable]
        private sealed class ParkedAircraftBatch
        {
            public string id;
            public ParkedAircraftPlacement[] placements;
        }

        [Serializable]
        private sealed class ParkedAircraftPlacement
        {
            public string model_family;
            public float x_meters;
            public float base_y_meters;
            public float z_meters;
            public float yaw_degrees;
            public float pitch_degrees;
            public float roll_degrees;
            public string wing_down_side;
            public int[] color_rgba;
        }

        private sealed class SourceAircraft
        {
            public string Name;
            public Mesh Mesh;
            public Matrix4x4 SourceToRoot;
            public Bounds Bounds;
        }
    }
}
