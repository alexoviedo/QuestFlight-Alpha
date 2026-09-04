using System;
using System.Collections.Generic;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Deterministic, offline-only repair for the two measured terrain tier joins.
    /// It preserves every source triangle outside the declared transition annuli.
    /// </summary>
    public static class TerrainDefectRepairV1Authoring
    {
        public const float NearRenderedHalfExtent = 2048f;
        public const float NearQueryHalfExtent = 2000f;
        public const float MiddleTransitionOuterHalfExtent = 2100f;
        public const float MiddleHalfExtent = 6000f;
        public const float FarTransitionOuterHalfExtent = 6200f;

        public const string EnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string NearMeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Terrain/Native/KBDU_ProductAeqd_BareEarth_Fixed.asset";
        public const string MiddleMeshPath =
            "Assets/Production/Environment/Generated/Terrain_Mid12km_USGS_Mesh.asset";
        public const string SurfacePath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";

        [MenuItem("QuestFlight/Product/Repair Terrain Defects V1")]
        public static void RunInteractive() => Author();

        public static void RunBatch()
        {
            try
            {
                Author();
                Debug.Log("[QuestFlight][TerrainDefectRepairV1] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Author()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Mesh near = LoadRequired<Mesh>(NearMeshPath);
            Mesh middleSource = LoadRequired<Mesh>(MiddleMeshPath);
            Mesh repairedMiddle = BuildTransitionMesh(
                near,
                NearRenderedHalfExtent,
                middleSource,
                MiddleTransitionOuterHalfExtent,
                "Terrain_Mid12km_USGS_Mesh");
            WriteMeshAsset(MiddleMeshPath, repairedMiddle);
            AssetDatabase.SaveAssets();

            Mesh repairedFar = StandaloneEnvironmentArtAuthoring.AuthorFarTerrainMesh();
            Mesh middle = LoadRequired<Mesh>(MiddleMeshPath);
            ProductionTerrainSurfaceAsset surface = LoadRequired<ProductionTerrainSurfaceAsset>(SurfacePath);
            surface.transitionPatches = new[]
            {
                BuildPatch(
                    "near_rendered_outer_band_v1",
                    ProductionTerrainZone.Near,
                    9,
                    NearQueryHalfExtent,
                    NearRenderedHalfExtent,
                    near,
                    (a, b, c) => Mathf.Max(SquareRadius(a), Mathf.Max(SquareRadius(b), SquareRadius(c))) >=
                                 NearQueryHalfExtent - 0.01f),
                BuildPatch(
                    "near_middle_transition_v1",
                    ProductionTerrainZone.Mid,
                    19,
                    NearRenderedHalfExtent,
                    MiddleTransitionOuterHalfExtent,
                    middle,
                    (a, b, c) => Mathf.Min(SquareRadius(a), Mathf.Min(SquareRadius(b), SquareRadius(c))) <
                                 MiddleTransitionOuterHalfExtent - 0.01f),
                BuildPatch(
                    "middle_rendered_outer_edge_v1",
                    ProductionTerrainZone.Mid,
                    19,
                    MiddleHalfExtent - 0.01f,
                    MiddleHalfExtent + 0.001f,
                    middle,
                    (a, b, c) => Mathf.Max(SquareRadius(a), Mathf.Max(SquareRadius(b), SquareRadius(c))) >=
                                 MiddleHalfExtent - 0.01f),
                BuildPatch(
                    "middle_far_transition_v1",
                    ProductionTerrainZone.Far,
                    29,
                    MiddleHalfExtent,
                    FarTransitionOuterHalfExtent,
                    repairedFar,
                    (a, b, c) => Mathf.Min(SquareRadius(a), Mathf.Min(SquareRadius(b), SquareRadius(c))) <
                                 FarTransitionOuterHalfExtent - 0.01f)
            };
            EditorUtility.SetDirty(surface);
            UpdateEnvironmentContract(repairedFar);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        internal static Mesh BuildTransitionMesh(
            Mesh innerSource,
            float innerBoundary,
            Mesh outerSource,
            float outerBoundary,
            string meshName)
        {
            if (innerSource == null || outerSource == null)
                throw new InvalidOperationException("Terrain transition sources are missing.");
            Vector3[] innerVertices = innerSource.vertices;
            Vector3[] innerNormals = ValidNormals(innerSource);
            Vector3[] outerVertices = outerSource.vertices;
            Vector3[] outerNormals = ValidNormals(outerSource);
            Vector2[] outerUv = outerSource.uv;
            Vector4[] outerTangents = outerSource.tangents;
            Color32[] outerColors = outerSource.colors32;
            int[] sourceTriangles = outerSource.triangles;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector4> tangents = new List<Vector4>();
            List<Color32> colors = new List<Color32>();
            List<int> triangles = new List<int>();
            Dictionary<int, int> retained = new Dictionary<int, int>();

            for (int offset = 0; offset < sourceTriangles.Length; offset += 3)
            {
                int ia = sourceTriangles[offset];
                int ib = sourceTriangles[offset + 1];
                int ic = sourceTriangles[offset + 2];
                // The legacy square masks leave one diagonal corner triangle on two corners.
                // A centroid test removes those inward-facing corner flaps while retaining the
                // first complete row outside the declared transition boundary.
                float centroidRadius = SquareRadius(
                    (outerVertices[ia] + outerVertices[ib] + outerVertices[ic]) / 3f);
                if (centroidRadius < outerBoundary + 0.01f) continue;
                triangles.Add(CopyVertex(ia));
                triangles.Add(CopyVertex(ib));
                triangles.Add(CopyVertex(ic));
            }

            int CopyVertex(int sourceIndex)
            {
                if (retained.TryGetValue(sourceIndex, out int existing)) return existing;
                int result = vertices.Count;
                retained.Add(sourceIndex, result);
                vertices.Add(outerVertices[sourceIndex]);
                normals.Add(outerNormals[sourceIndex]);
                uvs.Add(outerUv.Length == outerVertices.Length ? outerUv[sourceIndex] : Vector2.zero);
                tangents.Add(outerTangents.Length == outerVertices.Length
                    ? outerTangents[sourceIndex]
                    : new Vector4(1f, 0f, 0f, 1f));
                colors.Add(outerColors.Length == outerVertices.Length
                    ? outerColors[sourceIndex]
                    : new Color32(255, 255, 255, 255));
                return result;
            }

            List<LoopPoint> outerLoop = retained
                .Where(pair => Mathf.Abs(SquareRadius(outerVertices[pair.Key]) - outerBoundary) <= 0.01f)
                .Select(pair => new LoopPoint(
                    pair.Value,
                    outerVertices[pair.Key],
                    PerimeterParameter(outerVertices[pair.Key], outerBoundary)))
                .GroupBy(item => QuantizedPosition(item.Position))
                .Select(group => group.First())
                .OrderBy(item => item.Parameter)
                .ToList();
            List<LoopPoint> innerLoop = Enumerable.Range(0, innerVertices.Length)
                .Where(index => Mathf.Abs(SquareRadius(innerVertices[index]) - innerBoundary) <= 0.01f)
                .Select(index => new LoopPoint(
                    index,
                    innerVertices[index],
                    PerimeterParameter(innerVertices[index], innerBoundary)))
                .GroupBy(item => QuantizedPosition(item.Position))
                .Select(group => group.First())
                .OrderBy(item => item.Parameter)
                .ToList();
            ValidateLoop(innerLoop, innerBoundary, "inner");
            ValidateLoop(outerLoop, outerBoundary, "outer");

            for (int index = 0; index < innerLoop.Count; index++)
            {
                LoopPoint point = innerLoop[index];
                int outputIndex = vertices.Count;
                vertices.Add(point.Position);
                normals.Add(innerNormals[point.Index]);
                uvs.Add(Vector2.zero);
                tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                colors.Add(new Color32(255, 255, 255, 255));
                innerLoop[index] = new LoopPoint(outputIndex, point.Position, point.Parameter);
            }
            TriangulateAnnulus(vertices, triangles, innerLoop, outerLoop);

            Mesh result = new Mesh
            {
                name = meshName,
                indexFormat = IndexFormat.UInt32
            };
            result.SetVertices(vertices);
            result.SetNormals(normals);
            if (outerUv.Length == outerVertices.Length) result.SetUVs(0, uvs);
            if (outerTangents.Length == outerVertices.Length) result.SetTangents(tangents);
            if (outerColors.Length == outerVertices.Length) result.SetColors(colors);
            result.SetTriangles(triangles, 0, true);
            result.RecalculateBounds();
            return result;
        }

        private static void TriangulateAnnulus(
            IReadOnlyList<Vector3> vertices,
            ICollection<int> triangles,
            IReadOnlyList<LoopPoint> inner,
            IReadOnlyList<LoopPoint> outer)
        {
            int innerIndex = 0;
            int outerIndex = 0;
            while (innerIndex < inner.Count || outerIndex < outer.Count)
            {
                int currentInner = inner[innerIndex % inner.Count].Index;
                int currentOuter = outer[outerIndex % outer.Count].Index;
                float nextInner = innerIndex + 1 < inner.Count
                    ? inner[innerIndex + 1].Parameter
                    : 1f;
                float nextOuter = outerIndex + 1 < outer.Count
                    ? outer[outerIndex + 1].Parameter
                    : 1f;
                const float epsilon = 0.0000001f;
                if (nextInner < nextOuter - epsilon)
                {
                    int next = inner[(innerIndex + 1) % inner.Count].Index;
                    AddUpward(vertices, triangles, currentInner, next, currentOuter);
                    innerIndex++;
                }
                else if (nextOuter < nextInner - epsilon)
                {
                    int next = outer[(outerIndex + 1) % outer.Count].Index;
                    AddUpward(vertices, triangles, currentInner, next, currentOuter);
                    outerIndex++;
                }
                else
                {
                    int nextInnerIndex = inner[(innerIndex + 1) % inner.Count].Index;
                    int nextOuterIndex = outer[(outerIndex + 1) % outer.Count].Index;
                    AddUpward(vertices, triangles, currentInner, nextInnerIndex, nextOuterIndex);
                    AddUpward(vertices, triangles, currentInner, nextOuterIndex, currentOuter);
                    innerIndex++;
                    outerIndex++;
                }
            }
        }

        private static void AddUpward(
            IReadOnlyList<Vector3> vertices,
            ICollection<int> triangles,
            int a,
            int b,
            int c)
        {
            Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (cross.sqrMagnitude <= 0.000001f)
                throw new InvalidOperationException("Terrain transition produced a degenerate triangle.");
            if (cross.y < 0f) (b, c) = (c, b);
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static ProductionTerrainSurfaceAsset.TrianglePatch BuildPatch(
            string stableId,
            ProductionTerrainZone zone,
            int priority,
            float inner,
            float outer,
            Mesh source,
            Func<Vector3, Vector3, Vector3, bool> includeTriangle)
        {
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = ValidNormals(source);
            int[] sourceTriangles = source.triangles;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<int> triangles = new List<int>();
            Dictionary<int, int> remap = new Dictionary<int, int>();
            for (int triangle = 0; triangle * 3 + 2 < sourceTriangles.Length; triangle++)
            {
                int triangleOffset = triangle * 3;
                if (!includeTriangle(
                        sourceVertices[sourceTriangles[triangleOffset]],
                        sourceVertices[sourceTriangles[triangleOffset + 1]],
                        sourceVertices[sourceTriangles[triangleOffset + 2]])) continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceIndex = sourceTriangles[triangle * 3 + corner];
                    if (!remap.TryGetValue(sourceIndex, out int outputIndex))
                    {
                        outputIndex = vertices.Count;
                        remap.Add(sourceIndex, outputIndex);
                        vertices.Add(sourceVertices[sourceIndex]);
                        normals.Add(sourceNormals[sourceIndex]);
                    }
                    triangles.Add(outputIndex);
                }
            }
            if (triangles.Count == 0)
                throw new InvalidOperationException("Terrain query patch is empty: " + stableId);
            return new ProductionTerrainSurfaceAsset.TrianglePatch
            {
                stableId = stableId,
                zone = zone,
                priority = priority,
                innerHalfExtentMeters = inner,
                outerHalfExtentMeters = outer,
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                triangles = triangles.ToArray()
            };
        }

        private static Vector3[] ValidNormals(Mesh mesh)
        {
            Vector3[] normals = mesh.normals;
            if (normals.Length == mesh.vertexCount) return normals;
            Mesh copy = UnityEngine.Object.Instantiate(mesh);
            copy.RecalculateNormals();
            normals = copy.normals;
            UnityEngine.Object.DestroyImmediate(copy);
            return normals;
        }

        private static float SquareRadius(Vector3 value) =>
            Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.z));

        private static float PerimeterParameter(Vector3 point, float boundary)
        {
            float distance;
            if (Mathf.Abs(point.x + boundary) <= 0.01f)
                distance = point.z + boundary;
            else if (Mathf.Abs(point.z - boundary) <= 0.01f)
                distance = 2f * boundary + point.x + boundary;
            else if (Mathf.Abs(point.x - boundary) <= 0.01f)
                distance = 4f * boundary + boundary - point.z;
            else if (Mathf.Abs(point.z + boundary) <= 0.01f)
                distance = 6f * boundary + boundary - point.x;
            else
                throw new InvalidOperationException("Terrain loop vertex is not on its declared square boundary.");
            return distance / (8f * boundary);
        }

        private static string QuantizedPosition(Vector3 point) =>
            Mathf.RoundToInt(point.x * 1000f) + ":" +
            Mathf.RoundToInt(point.y * 1000f) + ":" +
            Mathf.RoundToInt(point.z * 1000f);

        private static void ValidateLoop(IReadOnlyCollection<LoopPoint> loop, float boundary, string label)
        {
            if (loop.Count < 8)
                throw new InvalidOperationException($"Terrain {label} loop at {boundary} m is incomplete.");
            foreach (int cornerX in new[] { -1, 1 })
            foreach (int cornerZ in new[] { -1, 1 })
                if (!loop.Any(item =>
                        Mathf.Abs(item.Position.x - cornerX * boundary) <= 0.01f &&
                        Mathf.Abs(item.Position.z - cornerZ * boundary) <= 0.01f))
                    throw new InvalidOperationException(
                        $"Terrain {label} loop at {boundary} m is missing corner {cornerX},{cornerZ}.");
        }

        private static void UpdateEnvironmentContract(Mesh far)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(EnvironmentPath);
            try
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                contract.farTerrainSourceVertexCount = far.vertexCount;
                contract.farTerrainSourceIndexCount = far.triangles.Length;
                string hash = TopologyHash(far.vertexCount, far.triangles);
                contract.farTerrainSourceTopologyHash = hash;
                contract.farTerrainBakedTopologyHash = hash;
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakeToolVersion = "terrain_defect_repair_v1";
                contract.bakeUtc = "2026-09-03T00:00:00.0000000Z";
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string TopologyHash(int vertexCount, IReadOnlyList<int> indices)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)vertexCount) * 16777619u;
                for (int index = 0; index < indices.Count; index++)
                    hash = (hash ^ (uint)indices[index]) * 16777619u;
                return hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static void WriteMeshAsset(string path, Mesh generated)
        {
            Mesh existing = LoadRequired<Mesh>(path);
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return value;
        }

        private readonly struct LoopPoint
        {
            public readonly int Index;
            public readonly Vector3 Position;
            public readonly float Parameter;

            public LoopPoint(int index, Vector3 position, float parameter)
            {
                Index = index;
                Position = position;
                Parameter = parameter;
            }
        }
    }
}
