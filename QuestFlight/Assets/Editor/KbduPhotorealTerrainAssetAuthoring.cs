using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Authors only reusable KBDU terrain-pyramid and context assets. It deliberately
    /// does not open or mutate the product scene or environment prefab.
    /// </summary>
    public static class KbduPhotorealTerrainAssetAuthoring
    {
        private const string Root = "Assets/Production/Environment/PhotorealTerrainV1";
        private const string Generated = Root + "/Generated";
        private const string ContextPath =
            "Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json";
        private const string RecipePath = Root + "/KBDU_ContextBatchRecipe.json";
        private const string AtlasPath = Root + "/KBDU_ContextAtlas.png";
        private const string NearPath =
            "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Near4km_4096.jpg";
        private const string MidPath =
            "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Mid12km_4096.jpg";
        private const string FarPath =
            "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Far24km_2048.jpg";
        private const string DetailPath =
            "Assets/Resources/QuestFlightLab/Environment/GroundMaterials/withered_grass_diff_1k.jpg";
        private const string ParkedAircraftModelPath = Root + "/KBDU_ParkedAircraft_LODs.glb";

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

        private static void AuthorAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
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
            ContextRecipe recipe = ReadJson<ContextRecipe>(RecipePath);
            int batchCount = recipe.building_batches.Length + recipe.vegetation_batches.Length +
                             recipe.parked_aircraft_batches.Length;
            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { Generated });
            if (meshGuids.Length != batchCount * 2)
                throw new InvalidOperationException(
                    "Generated context mesh count is " + meshGuids.Length + ", expected " + batchCount * 2);
            long lod0Triangles = 0;
            long lod1Triangles = 0;
            foreach (string guid in meshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Mesh mesh = LoadRequired<Mesh>(path);
                if (mesh.subMeshCount != 1 || mesh.vertexCount == 0 || mesh.GetIndexCount(0) == 0)
                    throw new InvalidOperationException("Generated mesh is empty or split: " + path);
                if (mesh.GetIndexCount(0) % 3 != 0)
                    throw new InvalidOperationException("Generated mesh has non-triangle indices: " + path);
                if (mesh.vertexCount > 60000)
                    throw new InvalidOperationException("Generated batch is too large for bounded culling: " + path);
                Bounds bounds = mesh.bounds;
                if (!Finite(bounds.center) || !Finite(bounds.extents))
                    throw new InvalidOperationException("Generated mesh has nonfinite bounds: " + path);
                long triangles = (long)mesh.GetIndexCount(0) / 3L;
                if (path.EndsWith("_LOD0.asset", StringComparison.Ordinal)) lod0Triangles += triangles;
                else if (path.EndsWith("_LOD1.asset", StringComparison.Ordinal)) lod1Triangles += triangles;
                else throw new InvalidOperationException("Generated mesh has an unexpected LOD name: " + path);
            }
            if (lod0Triangles > 74000 || lod1Triangles > 35000)
                throw new InvalidOperationException(
                    "Generated context triangle budget exceeded: LOD0=" + lod0Triangles + ", LOD1=" + lod1Triangles);

            GameObject prefab = LoadRequired<GameObject>(Root + "/KBDU_BatchedContext.prefab");
            LODGroup[] groups = prefab.GetComponentsInChildren<LODGroup>(true);
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            if (groups.Length != batchCount || renderers.Length != batchCount * 2)
                throw new InvalidOperationException(
                    "Batched context prefab hierarchy is incomplete: groups=" + groups.Length +
                    ", renderers=" + renderers.Length);
            foreach (LODGroup group in groups)
            {
                if (group.fadeMode != LODFadeMode.None || group.animateCrossFading || group.GetLODs().Length != 2)
                    throw new InvalidOperationException("Context LOD must have two non-crossfaded levels: " + group.name);
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
                ", LOD0 triangles=" + lod0Triangles + ", LOD1 triangles=" + lod1Triangles +
                ", materials=" + materials.Count + ".");
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
            ContextDocument context = ReadJson<ContextDocument>(ContextPath);
            ContextRecipe recipe = ReadJson<ContextRecipe>(RecipePath);
            if (context.openstreetmap == null || context.openstreetmap.features == null)
                throw new InvalidOperationException("OSM context does not contain features");
            if (recipe.building_batches == null || recipe.vegetation_batches == null ||
                recipe.parked_aircraft_batches == null)
                throw new InvalidOperationException("KBDU context recipe does not contain batches");

            int buildingCount = 0;
            int vegetationCount = 0;
            int parkedAircraftCount = 0;
            HashSet<string> desiredMeshPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (BuildingBatch batch in recipe.building_batches)
            {
                Mesh lod0 = BuildBuildingBatch(batch, context, false);
                Mesh lod1 = BuildBuildingBatch(batch, context, true);
                string lod0Path = Generated + "/" + batch.id + "_LOD0.asset";
                string lod1Path = Generated + "/" + batch.id + "_LOD1.asset";
                SaveMesh(lod0, lod0Path);
                SaveMesh(lod1, lod1Path);
                desiredMeshPaths.Add(lod0Path);
                desiredMeshPaths.Add(lod1Path);
                buildingCount += batch.buildings.Length;
            }
            foreach (VegetationBatch batch in recipe.vegetation_batches)
            {
                Mesh lod0 = BuildVegetationBatch(batch, false);
                Mesh lod1 = BuildVegetationBatch(batch, true);
                string lod0Path = Generated + "/" + batch.id + "_LOD0.asset";
                string lod1Path = Generated + "/" + batch.id + "_LOD1.asset";
                SaveMesh(lod0, lod0Path);
                SaveMesh(lod1, lod1Path);
                desiredMeshPaths.Add(lod0Path);
                desiredMeshPaths.Add(lod1Path);
                vegetationCount += batch.placements.Length;
            }
            GameObject parkedAircraftSource = LoadRequired<GameObject>(ParkedAircraftModelPath);
            foreach (ParkedAircraftBatch batch in recipe.parked_aircraft_batches)
            {
                Mesh lod0 = BuildParkedAircraftBatch(batch, parkedAircraftSource, false);
                Mesh lod1 = BuildParkedAircraftBatch(batch, parkedAircraftSource, true);
                string lod0Path = Generated + "/" + batch.id + "_LOD0.asset";
                string lod1Path = Generated + "/" + batch.id + "_LOD1.asset";
                SaveMesh(lod0, lod0Path);
                SaveMesh(lod1, lod1Path);
                desiredMeshPaths.Add(lod0Path);
                desiredMeshPaths.Add(lod1Path);
                parkedAircraftCount += batch.placements.Length;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Generated }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!desiredMeshPaths.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            Debug.Log(
                "[QuestFlight][KBDUPhotorealTerrain] Authored " + recipe.building_batches.Length +
                " two-LOD OSM building batches (" + buildingCount + " footprints) and " +
                recipe.vegetation_batches.Length + " two-LOD opaque vegetation batches (" +
                vegetationCount + " trees), plus " + parkedAircraftCount +
                " exterior-only static parked aircraft in one two-LOD batch; no product scene or environment prefab was modified.");
        }

        private static void CreateContextPrefab()
        {
            ContextRecipe recipe = ReadJson<ContextRecipe>(RecipePath);
            Material buildingMaterial = LoadRequired<Material>(Root + "/KBDU_OsmBuildings.mat");
            Material vegetationMaterial = LoadRequired<Material>(Root + "/KBDU_ContextVegetation.mat");
            Material parkedAircraftMaterial = LoadRequired<Material>(Root + "/KBDU_ParkedAircraft.mat");
            GameObject root = new GameObject("KBDU_BatchedContext");
            try
            {
                foreach (BuildingBatch batch in recipe.building_batches)
                    AddBatchLod(root.transform, batch.id, buildingMaterial, 0.08f, 0.025f);
                foreach (VegetationBatch batch in recipe.vegetation_batches)
                    AddBatchLod(root.transform, batch.id, vegetationMaterial, 0.10f, 0.035f);
                foreach (ParkedAircraftBatch batch in recipe.parked_aircraft_batches)
                    AddBatchLod(root.transform, batch.id, parkedAircraftMaterial, 0.12f, 0.03f);
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
            float lod0Height,
            float lod1Height)
        {
            GameObject batch = new GameObject(batchId);
            batch.transform.SetParent(parent, false);
            Renderer lod0 = AddMeshRenderer(batch.transform, "LOD0", batchId + "_LOD0", material);
            Renderer lod1 = AddMeshRenderer(batch.transform, "LOD1", batchId + "_LOD1", material);
            LODGroup group = batch.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            group.SetLODs(new[]
            {
                new LOD(lod0Height, new[] { lod0 }),
                new LOD(lod1Height, new[] { lod1 })
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

        private static Mesh BuildBuildingBatch(BuildingBatch batch, ContextDocument context, bool lowDetail)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> indices = new List<int>();
            float quantization = context.coordinate_quantization_meters;
            foreach (BuildingRecord record in batch.buildings)
            {
                if (record.feature_index < 0 || record.feature_index >= context.openstreetmap.features.Length)
                    throw new InvalidOperationException("Building feature index is out of range: " + record.feature_index);
                ContextFeature feature = context.openstreetmap.features[record.feature_index];
                List<Vector2> footprint = DecodeFootprint(feature.points_q, quantization);
                if (footprint.Count < 3) continue;
                if (lowDetail)
                {
                    float minX = footprint.Min(point => point.x);
                    float maxX = footprint.Max(point => point.x);
                    float minZ = footprint.Min(point => point.y);
                    float maxZ = footprint.Max(point => point.y);
                    footprint = new List<Vector2>
                    {
                        new Vector2(minX, minZ), new Vector2(minX, maxZ),
                        new Vector2(maxX, maxZ), new Vector2(maxX, minZ)
                    };
                }
                EnsureClockwise(footprint);
                AddExtrudedBuilding(
                    footprint,
                    record.base_y_meters,
                    Mathf.Max(3.2f, feature.render_height_meters),
                    record.facade_tile,
                    record.roof_tile,
                    vertices,
                    normals,
                    uvs,
                    indices);
            }
            Mesh mesh = new Mesh { name = batch.id + (lowDetail ? "_LOD1" : "_LOD0") };
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
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
            GameObject sourceModel,
            bool lowDetail)
        {
            string lodName = lowDetail ? "ParkedAircraft_LOD1" : "ParkedAircraft_LOD0";
            MeshFilter sourceFilter = sourceModel.GetComponentsInChildren<MeshFilter>(true)
                .SingleOrDefault(filter => filter.name.StartsWith(lodName, StringComparison.Ordinal));
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                throw new InvalidOperationException("Parked-aircraft source LOD is missing: " + lodName);

            Mesh source = sourceFilter.sharedMesh;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            int[] sourceTriangles = source.triangles;
            if (sourceNormals.Length != sourceVertices.Length)
                throw new InvalidOperationException("Parked-aircraft source normals are incomplete: " + lodName);

            List<Vector3> vertices = new List<Vector3>(sourceVertices.Length * batch.placements.Length);
            List<Vector3> normals = new List<Vector3>(sourceVertices.Length * batch.placements.Length);
            List<Color32> colors = new List<Color32>(sourceVertices.Length * batch.placements.Length);
            List<int> indices = new List<int>(sourceTriangles.Length * batch.placements.Length);
            Matrix4x4 sourceToRoot = sourceModel.transform.worldToLocalMatrix * sourceFilter.transform.localToWorldMatrix;
            foreach (ParkedAircraftPlacement placement in batch.placements)
            {
                if (placement.color_rgba == null || placement.color_rgba.Length != 4)
                    throw new InvalidOperationException("Parked-aircraft color contract is invalid in " + batch.id);
                Color32 color = new Color32(
                    (byte)placement.color_rgba[0], (byte)placement.color_rgba[1],
                    (byte)placement.color_rgba[2], (byte)placement.color_rgba[3]);
                Matrix4x4 placementMatrix = Matrix4x4.TRS(
                    new Vector3(placement.x_meters, placement.base_y_meters, placement.z_meters),
                    Quaternion.Euler(0f, placement.yaw_degrees, 0f),
                    Vector3.one) * sourceToRoot;
                Matrix4x4 normalMatrix = placementMatrix.inverse.transpose;
                int start = vertices.Count;
                for (int index = 0; index < sourceVertices.Length; index++)
                {
                    vertices.Add(placementMatrix.MultiplyPoint3x4(sourceVertices[index]));
                    normals.Add(normalMatrix.MultiplyVector(sourceNormals[index]).normalized);
                    colors.Add(color);
                }
                foreach (int sourceIndex in sourceTriangles) indices.Add(start + sourceIndex);
            }

            Mesh mesh = new Mesh { name = batch.id + (lowDetail ? "_LOD1" : "_LOD0") };
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildVegetationBatch(VegetationBatch batch, bool lowDetail)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Color32> colors = new List<Color32>();
            List<int> indices = new List<int>();
            for (int placementIndex = 0; placementIndex < batch.placements.Length; placementIndex++)
            {
                VegetationPlacement placement = batch.placements[placementIndex];
                Vector3 origin = new Vector3(placement.x_meters, placement.base_y_meters, placement.z_meters);
                if (lowDetail)
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
            Mesh mesh = new Mesh { name = batch.id + (lowDetail ? "_LOD1" : "_LOD0") };
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
            public float x_meters;
            public float base_y_meters;
            public float z_meters;
            public float yaw_degrees;
            public int[] color_rgba;
        }
    }
}
