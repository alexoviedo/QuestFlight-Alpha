using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// Converts the compact, reviewed fence authority into four terrain-aligned
    /// flight-distance meshes. No production geometry is created at runtime,
    /// and no LOD switch can produce fence popping.
    /// </summary>
    public static class AirportPerimeterFenceAuthoring
    {
        public const string Root = "Assets/Production/Environment/AirportFenceV1";
        public const string ManifestPath = Root + "/Data/KBDU_AirportFenceSourceV1.json";
        public const string GeneratedFolder = Root + "/Generated";
        public const string PrefabPath = Root + "/KBDU_AirportPerimeterFence.prefab";
        public const string ProductEnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string SharedMaterialPath =
            "Assets/Production/Environment/AirportCoreV4/Materials/AirportCoreHangarKitAtlas.mat";

        [MenuItem("QuestFlight/Airport Fence V1/Build And Integrate")]
        public static void BuildAndIntegrate()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Data");
            EnsureFolder(GeneratedFolder);

            TextAsset source = LoadRequired<TextAsset>(ManifestPath);
            FenceDocument document = JsonUtility.FromJson<FenceDocument>(source.text);
            if (document == null || document.sections == null ||
                document.sections.Length != AirportPerimeterFenceRoot.ExpectedSectionCount)
                throw new InvalidOperationException("Fence source must contain four sections.");

            GameObject product = PrefabUtility.LoadPrefabContents(ProductEnvironmentPath);
            try
            {
                ProductionEnvironmentRoot environment =
                    product.GetComponent<ProductionEnvironmentRoot>();
                if (environment == null || environment.authoritativeTerrainSurface == null ||
                    environment.airportContext == null)
                    throw new InvalidOperationException(
                        "Production environment lacks airport/terrain authority.");

                Material material = LoadRequired<Material>(SharedMaterialPath);
                GameObject fenceRoot = new GameObject(AirportPerimeterFenceRoot.GameObjectName);
                try
                {
                    AirportPerimeterFenceRoot marker =
                        fenceRoot.AddComponent<AirportPerimeterFenceRoot>();
                    marker.sourceManifest = source;
                    marker.sharedMaterial = material;
                    marker.authoredPerimeterLengthMeters = CalculateSourceLength(document);

                    List<MeshRenderer> sectionRenderers = new List<MeshRenderer>();
                    int serializedTriangles = 0;
                    int selectedTriangles = 0;
                    for (int sectionIndex = 0;
                         sectionIndex < document.sections.Length;
                         sectionIndex++)
                    {
                        FenceSection section = document.sections[sectionIndex];
                        GameObject sectionObject =
                            new GameObject($"FenceSection_{sectionIndex:00}_{section.stable_id}");
                        sectionObject.transform.SetParent(fenceRoot.transform, false);
                        Mesh mesh = BuildSectionMesh(
                            section,
                            environment.authoritativeTerrainSurface);
                        string meshPath =
                            $"{GeneratedFolder}/{section.stable_id}.asset";
                        mesh = SaveMesh(mesh, meshPath);
                        int triangleCount = checked((int)mesh.GetIndexCount(0) / 3);
                        serializedTriangles += triangleCount;
                        selectedTriangles += triangleCount;

                        MeshFilter filter = sectionObject.AddComponent<MeshFilter>();
                        filter.sharedMesh = mesh;
                        MeshRenderer renderer = sectionObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.motionVectorGenerationMode =
                            MotionVectorGenerationMode.ForceNoMotion;
                        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        renderer.scaleInLightmap = 0f;
                        GameObjectUtility.SetStaticEditorFlags(
                            sectionObject,
                            StaticEditorFlags.BatchingStatic |
                            StaticEditorFlags.OccludeeStatic);
                        sectionRenderers.Add(renderer);
                    }

                    marker.sections = sectionRenderers.ToArray();
                    marker.authoredMaximumSelectedTriangles = selectedTriangles;
                    marker.authoredSerializedTriangles = serializedTriangles;
                    marker.authoredRendererCount =
                        fenceRoot.GetComponentsInChildren<Renderer>(true).Length;
                    if (!marker.TryValidateContract(out string report))
                        throw new InvalidOperationException("Fence authoring failed: " + report);
                    PrefabUtility.SaveAsPrefabAsset(fenceRoot, PrefabPath);
                    DeleteObsoleteLodAssets(document);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(fenceRoot);
                }

                Integrate(product, environment);
                AssetDatabase.SaveAssets();
                Debug.Log("[QuestFlight][AirportFenceV1] SERIALIZED_INTEGRATION_SUCCESS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static void Integrate(
            GameObject product,
            ProductionEnvironmentRoot environment)
        {
            AirportPerimeterFenceRoot prior = product
                .GetComponentsInChildren<AirportPerimeterFenceRoot>(true)
                .SingleOrDefault();
            if (prior != null)
                UnityEngine.Object.DestroyImmediate(prior.gameObject);

            GameObject prefab = LoadRequired<GameObject>(PrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                environment.airportContext);
            instance.name = AirportPerimeterFenceRoot.GameObjectName;
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            environment.airportPerimeterFence = instance.transform;

            ProductionEnvironmentBudget budget = environment.CalculateBudget();
            environment.bakedRendererCount = budget.rendererCount;
            environment.bakedTriangleCount = budget.triangleCount;
            environment.bakedMaterialCount = budget.materialCount;
            EditorUtility.SetDirty(environment);
            if (!environment.TryValidateContract(out string report))
                throw new InvalidOperationException(
                    "Fence integration leaves the product invalid: " + report);
            PrefabUtility.SaveAsPrefabAsset(product, ProductEnvironmentPath);
        }

        private static Mesh BuildSectionMesh(
            FenceSection section,
            ProductionTerrainSurfaceAsset terrain)
        {
            List<Vector3> samples = SampleSourceVertices(section, terrain);
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            float height = section.height_meters;
            float cumulativeMeters = 0f;
            float style = section.representation.Contains("wood") ? 1f : 0f;
            for (int index = 0; index < samples.Count - 1; index++)
            {
                Vector3 first = samples[index];
                Vector3 second = samples[index + 1];
                float segmentLength = Vector2.Distance(
                    new Vector2(first.x, first.z),
                    new Vector2(second.x, second.z));
                AddFencePanel(
                    vertices,
                    triangles,
                    uvs,
                    colors,
                    first + Vector3.up * 0.025f,
                    second + Vector3.up * 0.025f,
                    height,
                    cumulativeMeters,
                    cumulativeMeters + segmentLength,
                    style);
                cumulativeMeters += segmentLength;
            }

            Mesh mesh = new Mesh
            {
                name = section.stable_id,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static List<Vector3> SampleSourceVertices(
            FenceSection section,
            ProductionTerrainSurfaceAsset terrain)
        {
            if (section.points == null || section.points.Length < 2)
                throw new InvalidOperationException(section.stable_id + " has no points.");
            return section.points
                .Select(point => GroundPoint(
                    terrain,
                    new Vector2(point.east_meters, point.north_meters)))
                .ToList();
        }

        private static Vector3 GroundPoint(
            ProductionTerrainSurfaceAsset terrain,
            Vector2 point)
        {
            if (!terrain.TrySample(point.x, point.y, out ProductionTerrainSample sample))
                throw new InvalidOperationException(
                    $"Fence terrain sample failed at ({point.x:F1}, {point.y:F1}).");
            return new Vector3(point.x, sample.heightMeters + 0.025f, point.y);
        }

        private static void AddFencePanel(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            Vector3 firstGround,
            Vector3 secondGround,
            float height,
            float firstDistance,
            float secondDistance,
            float style)
        {
            int start = vertices.Count;
            Vector3 v0 = firstGround;
            Vector3 v1 = firstGround + Vector3.up * height;
            Vector3 v2 = secondGround + Vector3.up * height;
            Vector3 v3 = secondGround;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);
            vertices.Add(v0);
            vertices.Add(v3);
            vertices.Add(v2);
            vertices.Add(v1);
            uvs.Add(new Vector2(firstDistance, 0f));
            uvs.Add(new Vector2(firstDistance, 1f));
            uvs.Add(new Vector2(secondDistance, 1f));
            uvs.Add(new Vector2(secondDistance, 0f));
            uvs.Add(new Vector2(firstDistance, 0f));
            uvs.Add(new Vector2(secondDistance, 0f));
            uvs.Add(new Vector2(secondDistance, 1f));
            uvs.Add(new Vector2(firstDistance, 1f));
            Color fenceVertexContract = new Color(height * 0.5f, style, 0f, 0f);
            for (int index = 0; index < 8; index++) colors.Add(fenceVertexContract);
            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            triangles.Add(start + 4);
            triangles.Add(start + 5);
            triangles.Add(start + 6);
            triangles.Add(start + 4);
            triangles.Add(start + 6);
            triangles.Add(start + 7);
        }

        private static float CalculateSourceLength(FenceDocument document)
        {
            float length = 0f;
            foreach (FenceSection section in document.sections)
            {
                for (int index = 0; index < section.points.Length - 1; index++)
                {
                    FencePoint first = section.points[index];
                    FencePoint second = section.points[index + 1];
                    length += Vector2.Distance(
                        new Vector2(first.east_meters, first.north_meters),
                        new Vector2(second.east_meters, second.north_meters));
                }
            }
            return length;
        }

        private static Mesh SaveMesh(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            existing.name = generated.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static void DeleteObsoleteLodAssets(FenceDocument document)
        {
            foreach (FenceSection section in document.sections)
            {
                for (int lodIndex = 0; lodIndex < 3; lodIndex++)
                {
                    AssetDatabase.DeleteAsset(
                        $"{GeneratedFolder}/{section.stable_id}_LOD{lodIndex}.asset");
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null)
                throw new InvalidOperationException("Required fence asset is missing: " + path);
            return value;
        }

        [Serializable]
        private sealed class FenceDocument
        {
            public FenceSection[] sections;
        }

        [Serializable]
        private sealed class FenceSection
        {
            public string stable_id;
            public string representation;
            public float height_meters;
            public int[] gate_segment_indices;
            public FencePoint[] points;
        }

        [Serializable]
        private sealed class FencePoint
        {
            public float east_meters;
            public float north_meters;
        }
    }
}
