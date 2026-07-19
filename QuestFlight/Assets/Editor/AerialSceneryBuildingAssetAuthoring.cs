using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Authors the standalone Aerial Scenery V1 building prefab from committed
    /// footprint contracts. It never opens or mutates the product scene or the
    /// legacy combined context prefab.
    /// </summary>
    public static class AerialSceneryBuildingAssetAuthoring
    {
        public const string Root = "Assets/Production/Environment/AerialSceneryV1/Buildings";
        public const string GeneratedRoot = Root + "/Generated";
        public const string SpatialContractPath = Root + "/KBDU_BuildingSpatialContractV1.json";
        public const string BatchReplacementPath = Root + "/KBDU_BuildingBatchReplacementV1.json";
        public const string PrefabPath = Root + "/KBDU_AerialBuildingsV1.prefab";
        public const string BuildingMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_OsmBuildings.mat";
        public const string LegacyCombinedPrefabPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab";

        [MenuItem("QuestFlight/Product/Author Aerial Scenery V1 Buildings")]
        public static void RunInteractive() => AuthorAssets();

        public static void RunBatch()
        {
            try
            {
                AuthorAssets();
                Debug.Log("[QuestFlight][AerialBuildingsV1] SUCCESS");
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
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ValidateAuthoredAssets(LoadInputs());
                Debug.Log("[QuestFlight][AerialBuildingsV1Validation] SUCCESS");
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
            Inputs inputs = LoadInputs();
            EnsureFolder(GeneratedRoot);
            CreateMeshes(inputs);
            AssetDatabase.SaveAssets();
            CreatePrefab(inputs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateAuthoredAssets(inputs);
        }

        private static Inputs LoadInputs()
        {
            SpatialContractDocument spatial = ReadJson<SpatialContractDocument>(SpatialContractPath);
            BatchReplacementDocument batches = ReadJson<BatchReplacementDocument>(BatchReplacementPath);
            if (spatial.records == null || spatial.records.Length == 0 ||
                spatial.far_orthophoto_feature_indices == null ||
                spatial.hero_replacement_source_feature_indices == null ||
                spatial.authored_scale_xyz == null || spatial.authored_scale_xyz.Length != 3 ||
                spatial.authored_scale_xyz.Any(value => Mathf.Abs(value - 1f) > 0.0001f) ||
                batches.batches == null || batches.batches.Length == 0)
                throw new InvalidOperationException("Aerial building contracts are incomplete.");
            if (!string.Equals(batches.source_spatial_contract_sha256, ComputeSha256(SpatialContractPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Building batch replacement manifest is stale.");
            Dictionary<int, SpatialRecord> byFeature = new Dictionary<int, SpatialRecord>();
            HashSet<int> heroReplacementSources =
                new HashSet<int>(spatial.hero_replacement_source_feature_indices);
            foreach (SpatialRecord record in spatial.records)
            {
                if (!byFeature.TryAdd(record.feature_index, record))
                    throw new InvalidOperationException("Building spatial identity is missing or duplicated.");
                if (record.geometry_points_q == null || record.geometry_points_q.Length < 6 ||
                    record.geometry_points_q.Length % 2 != 0 ||
                    !Finite(record.base_y_meters) || !Finite(record.height_meters) || record.height_meters <= 0f)
                    throw new InvalidOperationException("Compact building authoring record is invalid: " + record.feature_index);
                if (record.lidar_height_used &&
                    (record.lidar_selected_relative_error < 0f || record.lidar_selected_relative_error > 0.100001f))
                    throw new InvalidOperationException("Qualified lidar height exceeded the 10 percent gate.");
                if (string.Equals(record.tier, "far_orthophoto", StringComparison.Ordinal))
                    throw new InvalidOperationException("Ordinary far building contains forbidden mesh geometry.");
            }
            if (spatial.far_orthophoto_feature_indices.Any(byFeature.ContainsKey) ||
                heroReplacementSources.Any(index => !byFeature.ContainsKey(index)))
                throw new InvalidOperationException("Compact far/hero identity sets overlap or reference missing records.");
            HashSet<string> legacyGroups = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> newGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (BatchReplacement batch in batches.batches)
            {
                if (string.IsNullOrWhiteSpace(batch.legacy_group_id_to_disable) ||
                    !legacyGroups.Add(batch.legacy_group_id_to_disable) ||
                    batch.legacy_renderer_ids_to_disable == null || batch.legacy_renderer_ids_to_disable.Length != 3)
                    throw new InvalidOperationException("Legacy building disable mapping is incomplete.");
                bool rendered = batch.represented_feature_indices != null && batch.represented_feature_indices.Length > 0;
                if (rendered)
                {
                    if (string.IsNullOrWhiteSpace(batch.new_group_id) || !newGroups.Add(batch.new_group_id) ||
                        batch.new_renderer_ids == null || batch.lods == null ||
                        batch.lods.Length < 1 || batch.lods.Length > 2 ||
                        batch.new_renderer_ids.Length != batch.lods.Length ||
                        batch.stable_bounds_min_max_xyz_meters == null ||
                        batch.stable_bounds_min_max_xyz_meters.Length != 6)
                        throw new InvalidOperationException("Replacement building batch is incomplete: " + batch.legacy_group_id_to_disable);
                    foreach (int featureIndex in batch.represented_feature_indices)
                    {
                        if (!byFeature.TryGetValue(featureIndex, out SpatialRecord record) ||
                            heroReplacementSources.Contains(featureIndex) ||
                            string.Equals(record.tier, "far_orthophoto", StringComparison.Ordinal))
                            throw new InvalidOperationException("Replacement batch includes forbidden feature " + featureIndex);
                    }
                    bool needsNearReduction = batch.represented_feature_indices
                        .Select(index => byFeature[index])
                        .Any(record => string.Equals(record.tier, "hero_core", StringComparison.Ordinal) ||
                                       string.Equals(record.tier, "near", StringComparison.Ordinal) ||
                                       string.Equals(record.tier, "far_landmark", StringComparison.Ordinal));
                    if (batch.lods.Length != (needsNearReduction ? 2 : 1) ||
                        !needsNearReduction && !string.Equals(batch.lods[0].level, "FIXED", StringComparison.Ordinal))
                        throw new InvalidOperationException("Replacement LOD count does not represent a real reduction.");
                }
                else if (!string.IsNullOrEmpty(batch.new_group_id) ||
                         batch.new_renderer_ids != null && batch.new_renderer_ids.Length != 0)
                {
                    throw new InvalidOperationException("Orthophoto-only batch must not declare a renderer.");
                }
            }
            if (batches.legacy_group_ids_to_disable == null ||
                batches.legacy_group_ids_to_disable.Length != legacyGroups.Count ||
                batches.legacy_renderer_ids_to_disable == null ||
                batches.legacy_renderer_ids_to_disable.Length != legacyGroups.Count * 3)
                throw new InvalidOperationException("Top-level legacy disable inventory is incomplete.");
            if (batches.statistics == null || batches.statistics.ordinary_far_geometry_count != 0)
                throw new InvalidOperationException("Ordinary-far building geometry entered the replacement contract.");
            return new Inputs(spatial, batches, byFeature);
        }

        private static void CreateMeshes(Inputs inputs)
        {
            HashSet<string> desired = new HashSet<string>(StringComparer.Ordinal);
            foreach (BatchReplacement batch in inputs.RenderedBatches)
            {
                for (int lod = 0; lod < batch.lods.Length; lod++)
                {
                    Mesh mesh = BuildMesh(batch, inputs.ByFeature, inputs.Spatial.coordinate_quantization_meters, lod);
                    LodPolicy policy = batch.lods[lod];
                    long triangles = (long)mesh.GetIndexCount(0) / 3L;
                    if (triangles != policy.triangles || mesh.vertexCount != policy.vertices)
                    {
                        UnityEngine.Object.DestroyImmediate(mesh);
                        throw new InvalidOperationException(
                            batch.new_group_id + " LOD" + lod + " estimate mismatch: " +
                            triangles + "/" + mesh.vertexCount + " vs " + policy.triangles + "/" + policy.vertices);
                    }
                    string path = MeshPath(batch, lod);
                    desired.Add(path);
                    SaveMesh(mesh, path);
                }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { GeneratedRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!desired.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        private static Mesh BuildMesh(
            BatchReplacement batch,
            IReadOnlyDictionary<int, SpatialRecord> byFeature,
            float quantization,
            int lod)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> indices = new List<int>();
            foreach (int featureIndex in batch.represented_feature_indices)
            {
                SpatialRecord record = byFeature[featureIndex];
                List<Vector2> footprint = DecodeFootprint(record.geometry_points_q, quantization);
                EnsureClockwise(footprint);
                bool prism = lod == 0 &&
                    (string.Equals(record.tier, "hero_core", StringComparison.Ordinal) ||
                     string.Equals(record.tier, "near", StringComparison.Ordinal) ||
                     string.Equals(record.tier, "far_landmark", StringComparison.Ordinal));
                try
                {
                    if (prism)
                        AddPrism(record, footprint, vertices, normals, uvs, indices);
                    else
                        AddRoof(record, footprint, vertices, normals, uvs, indices);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Failed to author source footprint feature " + record.feature_index + " in " +
                        batch.new_group_id + " LOD" + lod + ".", exception);
                }
            }
            Mesh mesh = new Mesh
            {
                name = batch.new_group_id + "_LOD" + lod,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = StableBounds(batch);
            return mesh;
        }

        private static void AddPrism(
            SpatialRecord record,
            List<Vector2> footprint,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> indices)
        {
            AddRoof(record, footprint, vertices, normals, uvs, indices);
            Rect facade = AtlasRect(record.facade_tile);
            float top = record.base_y_meters + record.height_meters;
            for (int edge = 0; edge < footprint.Count; edge++)
            {
                Vector2 a = footprint[edge];
                Vector2 b = footprint[(edge + 1) % footprint.Count];
                Vector2 direction = b - a;
                Vector3 outward = new Vector3(-direction.y, 0f, direction.x).normalized;
                int start = vertices.Count;
                vertices.Add(new Vector3(a.x, record.base_y_meters, a.y));
                vertices.Add(new Vector3(a.x, top, a.y));
                vertices.Add(new Vector3(b.x, top, b.y));
                vertices.Add(new Vector3(b.x, record.base_y_meters, b.y));
                for (int index = 0; index < 4; index++) normals.Add(outward);
                uvs.Add(new Vector2(facade.xMin, facade.yMin));
                uvs.Add(new Vector2(facade.xMin, facade.yMax));
                uvs.Add(new Vector2(facade.xMax, facade.yMax));
                uvs.Add(new Vector2(facade.xMax, facade.yMin));
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 1);
                indices.Add(start);
                indices.Add(start + 3);
                indices.Add(start + 2);
            }
        }

        private static void AddRoof(
            SpatialRecord record,
            List<Vector2> footprint,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> indices)
        {
            int start = vertices.Count;
            float top = record.base_y_meters + record.height_meters;
            float minX = footprint.Min(point => point.x);
            float maxX = footprint.Max(point => point.x);
            float minZ = footprint.Min(point => point.y);
            float maxZ = footprint.Max(point => point.y);
            Rect roof = AtlasRect(record.roof_tile);
            foreach (Vector2 point in footprint)
            {
                vertices.Add(new Vector3(point.x, top, point.y));
                normals.Add(Vector3.up);
                float u = Mathf.InverseLerp(minX, maxX, point.x);
                float v = Mathf.InverseLerp(minZ, maxZ, point.y);
                uvs.Add(new Vector2(Mathf.Lerp(roof.xMin, roof.xMax, u), Mathf.Lerp(roof.yMin, roof.yMax, v)));
            }
            foreach (int index in TriangulateClockwise(footprint)) indices.Add(start + index);
        }

        private static void CreatePrefab(Inputs inputs)
        {
            Material material = LoadRequired<Material>(BuildingMaterialPath);
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = existing
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("KBDU_AerialBuildingsV1");
            try
            {
                root.name = "KBDU_AerialBuildingsV1";
                HashSet<string> desiredGroups = new HashSet<string>(
                    inputs.RenderedBatches.Select(batch => batch.new_group_id),
                    StringComparer.Ordinal);
                foreach (Transform stale in root.transform.Cast<Transform>()
                             .Where(child => !desiredGroups.Contains(child.name)).ToArray())
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                foreach (BatchReplacement batch in inputs.RenderedBatches)
                {
                    Transform groupTransform = root.transform.Find(batch.new_group_id);
                    GameObject groupObject;
                    if (groupTransform == null)
                    {
                        groupObject = new GameObject(batch.new_group_id);
                        groupObject.transform.SetParent(root.transform, false);
                    }
                    else
                    {
                        groupObject = groupTransform.gameObject;
                    }
                    groupObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    groupObject.transform.localScale = Vector3.one;
                    HashSet<string> desiredRenderers = new HashSet<string>(
                        Enumerable.Range(0, batch.lods.Length).Select(lod => "LOD" + lod),
                        StringComparer.Ordinal);
                    foreach (Transform stale in groupObject.transform.Cast<Transform>()
                                 .Where(child => !desiredRenderers.Contains(child.name)).ToArray())
                        UnityEngine.Object.DestroyImmediate(stale.gameObject);
                    Renderer[] renderers = Enumerable.Range(0, batch.lods.Length)
                        .Select(lod => ConfigureRenderer(groupObject.transform, batch, lod, material))
                        .ToArray();
                    if (batch.lods.Length == 2)
                    {
                        LODGroup group = groupObject.GetComponent<LODGroup>();
                        if (group == null) group = groupObject.AddComponent<LODGroup>();
                        group.fadeMode = LODFadeMode.None;
                        group.animateCrossFading = false;
                        group.SetLODs(Enumerable.Range(0, batch.lods.Length)
                            .Select(lod => new LOD(
                                batch.lods[lod].screen_relative_height,
                                new[] { renderers[lod] }))
                            .ToArray());
                        group.RecalculateBounds();
                    }
                    else
                    {
                        LODGroup staleGroup = groupObject.GetComponent<LODGroup>();
                        if (staleGroup != null) UnityEngine.Object.DestroyImmediate(staleGroup);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (existing)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Renderer ConfigureRenderer(
            Transform parent, BatchReplacement batch, int lod, Material material)
        {
            string name = "LOD" + lod;
            Transform existing = parent.Find(name);
            GameObject child;
            if (existing == null)
            {
                child = new GameObject(name);
                child.transform.SetParent(parent, false);
            }
            else
            {
                child = existing.gameObject;
            }
            child.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            child.transform.localScale = Vector3.one;
            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (filter == null) filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = LoadRequired<Mesh>(MeshPath(batch, lod));
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private static void ValidateAuthoredAssets(Inputs inputs)
        {
            ValidateLegacyDisableInventory(inputs.Batches);
            GameObject prefab = LoadRequired<GameObject>(PrefabPath);
            string[] dependencies = AssetDatabase.GetDependencies(PrefabPath, true);
            if (dependencies.Contains("Assets/Production/Environment/ProductionEnvironmentRoot.prefab") ||
                dependencies.Contains("Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab") ||
                dependencies.Any(path =>
                    path.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal) &&
                    path.IndexOf("4-6km", StringComparison.OrdinalIgnoreCase) >= 0))
                throw new InvalidOperationException(
                    "Standalone building prefab dependency closure contains a product root, legacy context, or apparent far mesh.");
            LODGroup[] groups = prefab.GetComponentsInChildren<LODGroup>(true);
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            BatchReplacement[] expected = inputs.RenderedBatches.ToArray();
            HashSet<string> expectedMeshes = new HashSet<string>(
                expected.SelectMany(batch =>
                    Enumerable.Range(0, batch.lods.Length).Select(lod => MeshPath(batch, lod))),
                StringComparer.Ordinal);
            HashSet<string> authoredMeshes = new HashSet<string>(
                AssetDatabase.FindAssets("t:Mesh", new[] { GeneratedRoot })
                    .Select(AssetDatabase.GUIDToAssetPath),
                StringComparer.Ordinal);
            if (!authoredMeshes.SetEquals(expectedMeshes))
                throw new InvalidOperationException(
                    "Generated building meshes contain missing, stale, or orthophoto-only assets.");
            int expectedLodGroups = expected.Count(batch => batch.lods.Length == 2);
            int expectedRenderers = expected.Sum(batch => batch.lods.Length);
            if (groups.Length != expectedLodGroups || renderers.Length != expectedRenderers)
                throw new InvalidOperationException(
                    "Standalone building prefab hierarchy mismatch: " + groups.Length + "/" + renderers.Length);
            long aggregateTriangles = 0;
            long worstCaseTriangles = 0;
            foreach (BatchReplacement batch in expected)
            {
                Transform transform = prefab.transform.Find(batch.new_group_id);
                if (transform == null || transform.localPosition != Vector3.zero || transform.localRotation != Quaternion.identity ||
                    transform.localScale != Vector3.one)
                    throw new InvalidOperationException("Replacement building group transform is unstable: " + batch.new_group_id);
                LODGroup group = transform.GetComponent<LODGroup>();
                Renderer[] batchRenderers;
                if (batch.lods.Length == 2)
                {
                    LOD[] lods = group == null ? Array.Empty<LOD>() : group.GetLODs();
                    if (lods.Length != 2 || group.fadeMode != LODFadeMode.None || group.animateCrossFading ||
                        lods.Any(item => item.renderers == null || item.renderers.Length != 1))
                        throw new InvalidOperationException("Replacement building LOD contract is invalid: " + batch.new_group_id);
                    batchRenderers = lods.Select(item => item.renderers[0]).ToArray();
                }
                else
                {
                    Transform fixedRenderer = transform.Find("LOD0");
                    Renderer renderer = fixedRenderer == null ? null : fixedRenderer.GetComponent<Renderer>();
                    if (group != null || renderer == null ||
                        transform.GetComponentsInChildren<MeshRenderer>(true).Length != 1)
                        throw new InvalidOperationException(
                            "Mid-only building group must contain one fixed renderer and no LODGroup: " +
                            batch.new_group_id);
                    batchRenderers = new[] { renderer };
                }
                Bounds stable = StableBounds(batch);
                long batchWorst = 0;
                for (int lod = 0; lod < batch.lods.Length; lod++)
                {
                    Renderer renderer = batchRenderers[lod];
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter == null ? null : filter.sharedMesh;
                    if (renderer.transform.localPosition != Vector3.zero ||
                        renderer.transform.localRotation != Quaternion.identity || renderer.transform.localScale != Vector3.one ||
                        mesh == null || mesh.subMeshCount != 1 || !BoundsEqual(mesh.bounds, stable) ||
                        mesh.vertexCount != batch.lods[lod].vertices ||
                        (long)mesh.GetIndexCount(0) / 3L != batch.lods[lod].triangles ||
                        renderer.sharedMaterial != LoadRequired<Material>(BuildingMaterialPath) ||
                        renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows)
                        throw new InvalidOperationException("Replacement mesh spatial contract failed: " + batch.new_group_id + " LOD" + lod);
                    long triangles = (long)mesh.GetIndexCount(0) / 3L;
                    aggregateTriangles += triangles;
                    batchWorst = Math.Max(batchWorst, triangles);
                }
                worstCaseTriangles += batchWorst;
            }
            if (aggregateTriangles != inputs.Batches.statistics.replacement_serialized_triangles ||
                worstCaseTriangles != inputs.Batches.statistics.replacement_worst_case_triangles)
                throw new InvalidOperationException("Replacement building triangle evidence is stale.");
            if (prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                throw new InvalidOperationException("Standalone building prefab contains forbidden runtime behavior.");
            Debug.Log(
                "[QuestFlight][AerialBuildingsV1Validation] groups=" + expected.Length +
                ", lodGroups=" + groups.Length + ", renderers=" + renderers.Length +
                ", aggregate triangles=" + aggregateTriangles +
                ", worst case=" + worstCaseTriangles + ", legacy groups mapped=" +
                inputs.Batches.legacy_group_ids_to_disable.Length + ".");
        }

        private static void ValidateLegacyDisableInventory(BatchReplacementDocument manifest)
        {
            GameObject legacy = LoadRequired<GameObject>(LegacyCombinedPrefabPath);
            HashSet<string> actualGroups = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> actualRenderers = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> renderersByGroup =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (LODGroup group in legacy.GetComponentsInChildren<LODGroup>(true))
            {
                string groupId = group.gameObject.name;
                if (!groupId.StartsWith("buildings_", StringComparison.Ordinal)) continue;
                if (!actualGroups.Add(groupId))
                    throw new InvalidOperationException("Legacy building group ID is duplicated: " + groupId);
                HashSet<string> groupRenderers = new HashSet<string>(StringComparer.Ordinal);
                renderersByGroup.Add(groupId, groupRenderers);
                foreach (LOD lod in group.GetLODs())
                foreach (Renderer renderer in lod.renderers)
                {
                    string relativePath = AnimationUtility.CalculateTransformPath(
                        renderer.transform, group.transform);
                    string rendererId = groupId + "/" + relativePath;
                    if (string.IsNullOrEmpty(relativePath) || !groupRenderers.Add(rendererId) ||
                        !actualRenderers.Add(rendererId))
                        throw new InvalidOperationException(
                            "Legacy building renderer ID is missing or duplicated: " + groupId);
                }
            }
            if (!actualGroups.SetEquals(manifest.legacy_group_ids_to_disable) ||
                !actualRenderers.SetEquals(manifest.legacy_renderer_ids_to_disable))
                throw new InvalidOperationException(
                    "Legacy building disable inventory does not exactly match the serialized combined prefab.");
            foreach (BatchReplacement batch in manifest.batches)
                if (!renderersByGroup.TryGetValue(batch.legacy_group_id_to_disable, out HashSet<string> groupRenderers) ||
                    !groupRenderers.SetEquals(batch.legacy_renderer_ids_to_disable))
                    throw new InvalidOperationException(
                        "Per-group legacy renderer disable inventory is not exact: " +
                        batch.legacy_group_id_to_disable);
        }

        private static string MeshPath(BatchReplacement batch, int lod) =>
            GeneratedRoot + "/" + batch.new_group_id + "_LOD" + lod + ".asset";

        private static Bounds StableBounds(BatchReplacement batch)
        {
            float[] value = batch.stable_bounds_min_max_xyz_meters;
            Vector3 minimum = new Vector3(value[0], value[1], value[2]);
            Vector3 maximum = new Vector3(value[3], value[4], value[5]);
            return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
        }

        private static bool BoundsEqual(Bounds first, Bounds second) =>
            (first.center - second.center).sqrMagnitude < 0.0001f &&
            (first.size - second.size).sqrMagnitude < 0.0001f;

        private static List<Vector2> DecodeFootprint(int[] values, float quantization)
        {
            if (values == null || values.Length < 6 || values.Length % 2 != 0)
                throw new InvalidOperationException("Replacement footprint is invalid.");
            List<Vector2> result = new List<Vector2>(values.Length / 2);
            for (int index = 0; index < values.Length; index += 2)
                result.Add(new Vector2(values[index] * quantization, values[index + 1] * quantization));
            return result;
        }

        private static void EnsureClockwise(List<Vector2> points)
        {
            if (SignedArea(points) > 0f) points.Reverse();
        }

        private static float SignedArea(IReadOnlyList<Vector2> points)
        {
            float result = 0f;
            for (int index = 0; index < points.Count; index++)
            {
                Vector2 a = points[index];
                Vector2 b = points[(index + 1) % points.Count];
                result += a.x * b.y - b.x * a.y;
            }
            return result * 0.5f;
        }

        private static List<int> TriangulateClockwise(IReadOnlyList<Vector2> points)
        {
            List<int> remaining = Enumerable.Range(0, points.Count).ToList();
            List<int> result = new List<int>((points.Count - 2) * 3);
            int guard = points.Count * points.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int cursor = 0; cursor < remaining.Count; cursor++)
                {
                    int previous = remaining[(cursor + remaining.Count - 1) % remaining.Count];
                    int current = remaining[cursor];
                    int next = remaining[(cursor + 1) % remaining.Count];
                    float turn = Cross(points[current] - points[previous], points[next] - points[current]);
                    if (turn > 0.00001f)
                        continue;
                    bool contains = turn < -0.00001f && remaining.Any(candidate =>
                        candidate != previous && candidate != current && candidate != next &&
                        PointStrictlyInTriangle(points[candidate], points[previous], points[current], points[next]));
                    if (contains) continue;
                    result.Add(previous);
                    result.Add(current);
                    result.Add(next);
                    remaining.RemoveAt(cursor);
                    clipped = true;
                    break;
                }
                if (!clipped)
                    throw new InvalidOperationException("Footprint triangulation would require a fan outside the source polygon.");
            }
            if (remaining.Count != 3)
                throw new InvalidOperationException("Footprint triangulation did not terminate.");
            result.Add(remaining[0]);
            result.Add(remaining[1]);
            result.Add(remaining[2]);
            return result;
        }

        private static float Cross(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;

        private static bool PointStrictlyInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float first = Cross(b - a, point - a);
            float second = Cross(c - b, point - b);
            float third = Cross(a - c, point - c);
            const float epsilon = 0.00001f;
            return first < -epsilon && second < -epsilon && third < -epsilon ||
                   first > epsilon && second > epsilon && third > epsilon;
        }

        private static Rect AtlasRect(int tile)
        {
            const float cell = 0.25f;
            int column = Mathf.Abs(tile) % 4;
            int rowFromTop = (Mathf.Abs(tile) / 4) % 4;
            return Rect.MinMaxRect(
                column * cell + 0.003f,
                1f - (rowFromTop + 1) * cell + 0.003f,
                (column + 1) * cell - 0.003f,
                1f - rowFromTop * cell - 0.003f);
        }

        private static void SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        private static string ComputeSha256(string assetPath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            using SHA256 algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(File.ReadAllBytes(fullPath)).Select(value => value.ToString("x2")));
        }

        private static T ReadJson<T>(string path) where T : class
        {
            TextAsset asset = LoadRequired<TextAsset>(path);
            T value = JsonUtility.FromJson<T>(asset.text);
            if (value == null) throw new InvalidOperationException("Could not parse JSON asset: " + path);
            return value;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return value;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class Inputs
        {
            public Inputs(
                SpatialContractDocument spatial,
                BatchReplacementDocument batches,
                Dictionary<int, SpatialRecord> byFeature)
            {
                Spatial = spatial;
                Batches = batches;
                ByFeature = byFeature;
            }

            public SpatialContractDocument Spatial { get; }
            public BatchReplacementDocument Batches { get; }
            public Dictionary<int, SpatialRecord> ByFeature { get; }
            public IEnumerable<BatchReplacement> RenderedBatches =>
                Batches.batches.Where(batch => batch.represented_feature_indices != null &&
                                                batch.represented_feature_indices.Length > 0);
        }

        [Serializable]
        public sealed class SpatialContractDocument
        {
            public float[] authored_scale_xyz;
            public float coordinate_quantization_meters;
            public int[] far_orthophoto_feature_indices;
            public int[] hero_replacement_source_feature_indices;
            public SpatialRecord[] records;
            public SpatialComponent[] multi_part_components;
        }

        [Serializable]
        public sealed class SpatialRecord
        {
            public float base_y_meters;
            public int facade_tile;
            public int feature_index;
            public float footprint_area_ratio;
            public int[] geometry_points_q;
            public float height_meters;
            public bool lidar_height_used;
            public float lidar_selected_relative_error;
            public int roof_tile;
            public string tier;
        }

        [Serializable]
        public sealed class SpatialComponent
        {
            public int[] connectivity_edges;
            public int[] feature_indices;
        }

        [Serializable]
        public sealed class BatchReplacementDocument
        {
            public string source_spatial_contract_sha256;
            public string[] legacy_group_ids_to_disable;
            public string[] legacy_renderer_ids_to_disable;
            public BatchReplacement[] batches;
            public BatchStatistics statistics;
        }

        [Serializable]
        public sealed class BatchReplacement
        {
            public string integration_action;
            public string legacy_group_id_to_disable;
            public string[] legacy_renderer_ids_to_disable;
            public string new_group_id;
            public string[] new_renderer_ids;
            public int[] represented_feature_indices;
            public float[] stable_bounds_min_max_xyz_meters;
            public TierCount[] represented_tier_counts;
            public TierCount[] tier_counts;
            public LodPolicy[] lods;
            public string zone;
        }

        [Serializable]
        public sealed class TierCount
        {
            public int count;
            public string tier;
        }

        [Serializable]
        public sealed class LodPolicy
        {
            public string level;
            public string mesh_mode;
            public float screen_relative_height;
            public long triangles;
            public int vertices;
        }

        [Serializable]
        public sealed class BatchStatistics
        {
            public int replacement_group_count;
            public int replacement_renderer_count;
            public int hero_replacement_source_count_omitted;
            public int legacy_batch_count;
            public int legacy_renderer_count_to_disable;
            public int orthophoto_only_batch_count;
            public int ordinary_far_geometry_count;
            public int fixed_mid_group_count;
            public int near_reduction_lod_group_count;
            public long replacement_serialized_triangles;
            public long replacement_worst_case_triangles;
        }
    }
}
