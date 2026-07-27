using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// Deterministically authors the three project-owned traffic archetypes, their
    /// shared proxy, one TrafficRoot prefab, and the serialized product integration.
    /// </summary>
    public static class KbduTrafficAuthoring
    {
        public const string Root = "Assets/Production/Environment/TrafficV1";
        public const string ManifestPath = Root + "/Data/KBDU_TrafficManifestV1.json";
        public const string PrefabPath = Root + "/KBDU_TrafficRoot.prefab";
        public const string ExistingMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_ParkedAircraft.mat";
        public const string ProductEnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";

        private static readonly Color32 Glass = new Color32(42, 62, 72, 255);
        private static readonly Color32 Tire = new Color32(24, 25, 25, 255);
        private static readonly Color32 Trim = new Color32(66, 69, 70, 255);
        private static readonly Color32 Light = new Color32(225, 215, 166, 255);

        [MenuItem("QuestFlight/Traffic V1/Build And Integrate")]
        public static void BuildAndIntegrate()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Models");

            TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifest == null)
            {
                throw new InvalidOperationException("Compiled traffic manifest is missing: " + ManifestPath);
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ExistingMaterialPath);
            if (material == null || material.shader == null || !material.enableInstancing)
            {
                throw new InvalidOperationException("Shared opaque instanced context material is missing.");
            }

            Mesh sedan = WriteMesh(
                Root + "/Models/KBDU_TrafficSedan_Blue.asset",
                BuildSedan("KBDU_TrafficSedan_Blue", new Color32(74, 105, 137, 255)));
            Mesh suv = WriteMesh(
                Root + "/Models/KBDU_TrafficSuv_Red.asset",
                BuildSuv("KBDU_TrafficSuv_Red", new Color32(132, 65, 57, 255)));
            Mesh pickup = WriteMesh(
                Root + "/Models/KBDU_TrafficPickup_White.asset",
                BuildPickup("KBDU_TrafficPickup_White", new Color32(202, 202, 195, 255)));
            Mesh proxy = WriteMesh(
                Root + "/Models/KBDU_TrafficMidProxy.asset",
                BuildProxy("KBDU_TrafficMidProxy"));

            Mesh[] nearMeshes = { sedan, suv, pickup };
            GameObject trafficRoot = new GameObject("TrafficRoot");
            try
            {
                KbduTrafficRuntime runtime = trafficRoot.AddComponent<KbduTrafficRuntime>();
                runtime.Configure(manifest, material, nearMeshes, proxy);
                if (!runtime.TryValidateContract(out string report))
                {
                    throw new InvalidOperationException("Authored traffic runtime failed: " + report);
                }
                PrefabUtility.SaveAsPrefabAsset(trafficRoot, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(trafficRoot);
            }

            GameObject productRoot = PrefabUtility.LoadPrefabContents(ProductEnvironmentPath);
            try
            {
                Transform existing = productRoot.transform.Find("TrafficRoot");
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }
                GameObject trafficPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(trafficPrefab, productRoot.transform);
                instance.name = "TrafficRoot";
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                ProductionEnvironmentRoot environment = productRoot.GetComponent<ProductionEnvironmentRoot>();
                if (environment == null)
                {
                    throw new InvalidOperationException("Production environment marker is missing.");
                }
                environment.trafficRoot = instance.transform;
                EditorUtility.SetDirty(environment);
                PrefabUtility.SaveAsPrefabAsset(productRoot, ProductEnvironmentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(productRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"Authored and integrated {PrefabPath}: parked=30, moving=12, routes=12, " +
                "draws<=4, materials=0 net (shared context material), transparent=0, shadows=0.");
        }

        private static Mesh BuildSedan(string name, Color32 body)
        {
            MeshBuilder builder = new MeshBuilder(name);
            builder.AddBox(new Vector3(0f, 0.52f, 0f), new Vector3(1.78f, 0.50f, 4.25f), body);
            builder.AddTaperedBox(
                new Vector3(0f, 1.03f, -0.10f),
                new Vector2(1.58f, 2.40f),
                new Vector2(1.28f, 1.60f),
                0.72f,
                Glass);
            builder.AddBox(new Vector3(0f, 1.43f, -0.10f), new Vector3(1.30f, 0.10f, 1.60f), body);
            builder.AddWheelSet(0.92f, 0.36f, 1.38f);
            builder.AddLights(0.91f, 0.62f, 2.14f);
            return builder.ToMesh();
        }

        private static Mesh BuildSuv(string name, Color32 body)
        {
            MeshBuilder builder = new MeshBuilder(name);
            builder.AddBox(new Vector3(0f, 0.63f, 0f), new Vector3(1.86f, 0.70f, 4.45f), body);
            builder.AddTaperedBox(
                new Vector3(0f, 1.25f, -0.18f),
                new Vector2(1.70f, 2.70f),
                new Vector2(1.52f, 2.28f),
                0.88f,
                Glass);
            builder.AddBox(new Vector3(0f, 1.72f, -0.18f), new Vector3(1.54f, 0.11f, 2.28f), body);
            builder.AddWheelSet(0.96f, 0.40f, 1.45f);
            builder.AddLights(0.95f, 0.73f, 2.24f);
            return builder.ToMesh();
        }

        private static Mesh BuildPickup(string name, Color32 body)
        {
            MeshBuilder builder = new MeshBuilder(name);
            builder.AddBox(new Vector3(0f, 0.62f, 0.20f), new Vector3(1.92f, 0.68f, 4.75f), body);
            builder.AddTaperedBox(
                new Vector3(0f, 1.23f, -0.55f),
                new Vector2(1.72f, 1.72f),
                new Vector2(1.52f, 1.42f),
                0.86f,
                Glass);
            builder.AddBox(new Vector3(0f, 1.70f, -0.55f), new Vector3(1.54f, 0.10f, 1.43f), body);
            builder.AddBox(new Vector3(0f, 0.91f, 1.38f), new Vector3(1.70f, 0.16f, 1.25f), Trim);
            builder.AddWheelSet(0.98f, 0.41f, 1.55f);
            builder.AddLights(0.98f, 0.72f, 2.39f);
            return builder.ToMesh();
        }

        private static Mesh BuildProxy(string name)
        {
            MeshBuilder builder = new MeshBuilder(name);
            builder.AddBox(
                new Vector3(0f, 0.56f, 0f),
                new Vector3(1.82f, 0.62f, 4.35f),
                new Color32(112, 118, 121, 255));
            builder.AddTaperedBox(
                new Vector3(0f, 1.08f, -0.12f),
                new Vector2(1.60f, 2.30f),
                new Vector2(1.34f, 1.72f),
                0.70f,
                Glass);
            return builder.ToMesh();
        }

        private static Mesh WriteMesh(string path, Mesh generated)
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            Mesh mesh = existing as Mesh;
            if (mesh == null)
            {
                UnityEngine.Object.DestroyImmediate(generated);
                throw new InvalidOperationException("Traffic mesh path is not a Mesh: " + path);
            }
            EditorUtility.CopySerialized(generated, mesh);
            mesh.name = generated.name;
            EditorUtility.SetDirty(mesh);
            UnityEngine.Object.DestroyImmediate(generated);
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(leaf))
            {
                throw new InvalidOperationException("Invalid asset folder: " + path);
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private sealed class MeshBuilder
        {
            private readonly string name;
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Color32> colors = new List<Color32>();
            private readonly List<int> indices = new List<int>();

            public MeshBuilder(string meshName)
            {
                name = meshName;
            }

            public void AddBox(Vector3 center, Vector3 size, Color32 color)
            {
                Vector3 half = size * 0.5f;
                Vector3[] corners =
                {
                    center + new Vector3(-half.x, -half.y, -half.z),
                    center + new Vector3(half.x, -half.y, -half.z),
                    center + new Vector3(half.x, half.y, -half.z),
                    center + new Vector3(-half.x, half.y, -half.z),
                    center + new Vector3(-half.x, -half.y, half.z),
                    center + new Vector3(half.x, -half.y, half.z),
                    center + new Vector3(half.x, half.y, half.z),
                    center + new Vector3(-half.x, half.y, half.z)
                };
                AddQuad(corners[0], corners[3], corners[2], corners[1], Vector3.back, color);
                AddQuad(corners[5], corners[6], corners[7], corners[4], Vector3.forward, color);
                AddQuad(corners[4], corners[7], corners[3], corners[0], Vector3.left, color);
                AddQuad(corners[1], corners[2], corners[6], corners[5], Vector3.right, color);
                AddQuad(corners[3], corners[7], corners[6], corners[2], Vector3.up, color);
                AddQuad(corners[4], corners[0], corners[1], corners[5], Vector3.down, color);
            }

            public void AddTaperedBox(
                Vector3 center,
                Vector2 lowerSize,
                Vector2 upperSize,
                float height,
                Color32 color)
            {
                float y0 = center.y - height * 0.5f;
                float y1 = center.y + height * 0.5f;
                Vector3[] lower =
                {
                    new Vector3(-lowerSize.x * 0.5f, y0, -lowerSize.y * 0.5f) + new Vector3(center.x, 0f, center.z),
                    new Vector3(lowerSize.x * 0.5f, y0, -lowerSize.y * 0.5f) + new Vector3(center.x, 0f, center.z),
                    new Vector3(lowerSize.x * 0.5f, y0, lowerSize.y * 0.5f) + new Vector3(center.x, 0f, center.z),
                    new Vector3(-lowerSize.x * 0.5f, y0, lowerSize.y * 0.5f) + new Vector3(center.x, 0f, center.z)
                };
                Vector3[] upper =
                {
                    new Vector3(-upperSize.x * 0.5f, y1, -upperSize.y * 0.5f) + new Vector3(center.x, 0f, center.z),
                    new Vector3(upperSize.x * 0.5f, y1, -upperSize.y * 0.5f) + new Vector3(center.x, 0f, center.z),
                    new Vector3(upperSize.x * 0.5f, y1, upperSize.y * 0.5f) + new Vector3(center.x, 0f, center.z),
                    new Vector3(-upperSize.x * 0.5f, y1, upperSize.y * 0.5f) + new Vector3(center.x, 0f, center.z)
                };
                AddQuad(lower[0], upper[0], upper[1], lower[1], Vector3.back, color);
                AddQuad(lower[1], upper[1], upper[2], lower[2], Vector3.right, color);
                AddQuad(lower[2], upper[2], upper[3], lower[3], Vector3.forward, color);
                AddQuad(lower[3], upper[3], upper[0], lower[0], Vector3.left, color);
                AddQuad(upper[0], upper[3], upper[2], upper[1], Vector3.up, color);
            }

            public void AddWheelSet(float halfWidth, float radius, float halfLength)
            {
                AddWheel(new Vector3(-halfWidth, radius, -halfLength), radius);
                AddWheel(new Vector3(halfWidth, radius, -halfLength), radius);
                AddWheel(new Vector3(-halfWidth, radius, halfLength), radius);
                AddWheel(new Vector3(halfWidth, radius, halfLength), radius);
            }

            private void AddWheel(Vector3 center, float radius)
            {
                const int segments = 6;
                float halfThickness = 0.11f;
                for (int index = 0; index < segments; index++)
                {
                    float a0 = index / (float)segments * Mathf.PI * 2f;
                    float a1 = (index + 1) / (float)segments * Mathf.PI * 2f;
                    Vector3 p0 = center + new Vector3(-halfThickness, Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius);
                    Vector3 p1 = center + new Vector3(halfThickness, Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius);
                    Vector3 p2 = center + new Vector3(halfThickness, Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                    Vector3 p3 = center + new Vector3(-halfThickness, Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                    Vector3 normal = new Vector3(0f, Mathf.Cos((a0 + a1) * 0.5f), Mathf.Sin((a0 + a1) * 0.5f));
                    AddQuad(p0, p1, p2, p3, normal, Tire);
                }
            }

            public void AddLights(float halfWidth, float height, float z)
            {
                AddBox(new Vector3(-halfWidth * 0.58f, height, z), new Vector3(0.35f, 0.18f, 0.04f), Light);
                AddBox(new Vector3(halfWidth * 0.58f, height, z), new Vector3(0.35f, 0.18f, 0.04f), Light);
            }

            private void AddQuad(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 d,
                Vector3 normal,
                Color32 color)
            {
                int offset = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                for (int index = 0; index < 4; index++)
                {
                    normals.Add(normal.normalized);
                    colors.Add(color);
                }
                indices.Add(offset);
                indices.Add(offset + 1);
                indices.Add(offset + 2);
                indices.Add(offset);
                indices.Add(offset + 2);
                indices.Add(offset + 3);
            }

            public Mesh ToMesh()
            {
                Mesh mesh = new Mesh
                {
                    name = name,
                    indexFormat = vertices.Count > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                return mesh;
            }
        }
    }
}
