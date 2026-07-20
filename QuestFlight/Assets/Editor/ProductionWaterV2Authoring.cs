using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    public static class ProductionWaterV2Authoring
    {
        public const string ManifestPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_WaterFeatureManifestV2.json";
        public const string SurfaceMeshPath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_OtherWaterFeatures_Surface_V2.asset";
        public const string BankMeshPath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_OtherWaterFeatures_ShoreBank_V2.asset";
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string WaterMaterialPath =
            "Assets/Production/Environment/Materials/Production_BoulderReservoir_Opaque.mat";
        private const string BankMaterialPath =
            "Assets/Production/Environment/Materials/Production_WaterBank.mat";
        private const float SurfaceClearanceMeters = 0.13f;

        [MenuItem("QuestFlight/Product/Integrate Bounded Water V2")]
        public static void RunInteractive() => Integrate();

        public static void RunBatch()
        {
            try
            {
                Integrate();
                Debug.Log("[QuestFlight][ProductionWaterV2] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Integrate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TextAsset manifestText = LoadRequired<TextAsset>(ManifestPath);
            WaterDocument document = JsonUtility.FromJson<WaterDocument>(manifestText.text);
            if (document == null || document.represented_features == null || document.represented_features.Length < 2)
                throw new InvalidOperationException("The V2 water manifest is missing represented features.");
            if (document.source_feature_count != document.represented_feature_count + document.excluded_feature_count)
                throw new InvalidOperationException("The V2 water manifest does not account for every source feature.");

            GameObject contents = PrefabUtility.LoadPrefabContents(EnvironmentPath);
            try
            {
                ProductionEnvironmentRoot root = contents.GetComponent<ProductionEnvironmentRoot>();
                if (root == null || root.essentialWater == null || root.authoritativeTerrainSurface == null ||
                    root.productionPlacementManifest == null)
                    throw new InvalidOperationException("The authored environment lacks the V2 water integration dependencies.");

                BuildMeshes(document, root.authoritativeTerrainSurface, out Mesh surface, out Mesh bank);
                SaveMesh(surface, SurfaceMeshPath);
                SaveMesh(bank, BankMeshPath);
                AssetDatabase.SaveAssets();

                Mesh savedSurface = LoadRequired<Mesh>(SurfaceMeshPath);
                Mesh savedBank = LoadRequired<Mesh>(BankMeshPath);
                Material waterMaterial = LoadRequired<Material>(WaterMaterialPath);
                Material bankMaterial = LoadRequired<Material>(BankMaterialPath);
                MeshFilter surfaceFilter = ReplaceChild(
                    root.essentialWater, "KBDU_OtherWaterFeatures_Surface_V2", savedSurface, waterMaterial);
                MeshFilter bankFilter = ReplaceChild(
                    root.essentialWater, "KBDU_OtherWaterFeatures_ShoreBank_V2", savedBank, bankMaterial);

                root.waterFeatureManifestV2 = manifestText;
                root.otherWaterFeaturesSurfaceV2 = surfaceFilter;
                root.otherWaterFeaturesShoreBankV2 = bankFilter;
                root.retainedWaterBodyCount = document.represented_feature_count;
                root.discardedMinorWaterFeatureCount = document.excluded_feature_count;
                root.minimumWaterTerrainSeparationMeters = Mathf.Min(
                    Mathf.Max(root.minimumWaterTerrainSeparationMeters, ProductionEnvironmentRoot.MinimumAcceptedWaterTerrainSeparationMeters),
                    SurfaceClearanceMeters);
                AppendPlacementEntries(document, root.authoritativeTerrainSurface, root.productionPlacementManifest, manifestText);
                ProductionEnvironmentBudget budget = root.CalculateBudget();
                root.bakedRendererCount = budget.rendererCount;
                root.bakedTriangleCount = budget.triangleCount;
                root.bakedMaterialCount = budget.materialCount;
                root.bakeUtc = "2026-07-17T10:00:00Z";
                root.bakeToolVersion = "environment_reconstruction_v2";
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(root.productionPlacementManifest);
                PrefabUtility.SaveAsPrefabAsset(contents, EnvironmentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void BuildMeshes(
            WaterDocument document,
            ProductionTerrainSurfaceAsset terrain,
            out Mesh surface,
            out Mesh bank)
        {
            List<Vector3> surfaceVertices = new List<Vector3>();
            List<Vector2> surfaceUvs = new List<Vector2>();
            List<int> surfaceTriangles = new List<int>();
            List<Vector3> bankVertices = new List<Vector3>();
            List<Vector2> bankUvs = new List<Vector2>();
            List<int> bankTriangles = new List<int>();

            foreach (WaterFeature feature in document.represented_features)
            {
                if (string.Equals(feature.name, "Boulder Reservoir", StringComparison.OrdinalIgnoreCase))
                    continue; // The same manifest source is already represented by the higher-quality reservoir mesh.
                Vector2[] points = DecodePoints(feature);
                if (string.Equals(feature.geometry_type, "polygon", StringComparison.Ordinal))
                    AddPolygon(points, terrain, surfaceVertices, surfaceUvs, surfaceTriangles, bankVertices, bankUvs, bankTriangles);
                else if (string.Equals(feature.geometry_type, "polyline", StringComparison.Ordinal))
                    AddPolyline(points, Mathf.Max(3f, feature.interpreted_width_meters), terrain,
                        surfaceVertices, surfaceUvs, surfaceTriangles, bankVertices, bankUvs, bankTriangles);
                else
                    throw new InvalidOperationException("Unsupported represented water geometry: " + feature.geometry_type);
            }

            surface = BuildMesh("KBDU_OtherWaterFeatures_Surface_V2", surfaceVertices, surfaceUvs, surfaceTriangles, true);
            bank = BuildMesh("KBDU_OtherWaterFeatures_ShoreBank_V2", bankVertices, bankUvs, bankTriangles, false);
        }

        private static void AddPolygon(
            Vector2[] points,
            ProductionTerrainSurfaceAsset terrain,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            List<Vector3> bankVertices,
            List<Vector2> bankUvs,
            List<int> bankTriangles)
        {
            int start = vertices.Count;
            float[] heights = points.Select(point => SampleHeight(terrain, point) + SurfaceClearanceMeters).ToArray();
            for (int index = 0; index < points.Length; index++)
            {
                vertices.Add(new Vector3(points[index].x, heights[index], points[index].y));
                uvs.Add(points[index] / 64f);
            }
            foreach (int index in Triangulate(points)) triangles.Add(start + index);
            for (int index = 0; index < points.Length; index++)
            {
                int next = (index + 1) % points.Length;
                AddBankQuad(points[index], heights[index], points[next], heights[next], terrain,
                    bankVertices, bankUvs, bankTriangles);
            }
        }

        private static void AddPolyline(
            Vector2[] points,
            float width,
            ProductionTerrainSurfaceAsset terrain,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            List<Vector3> bankVertices,
            List<Vector2> bankUvs,
            List<int> bankTriangles)
        {
            float halfWidth = width * 0.5f;
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector2 direction = (points[index + 1] - points[index]).normalized;
                if (direction.sqrMagnitude < 0.5f) continue;
                Vector2 side = new Vector2(-direction.y, direction.x) * halfWidth;
                Vector2 a = points[index] - side;
                Vector2 b = points[index] + side;
                Vector2 c = points[index + 1] + side;
                Vector2 d = points[index + 1] - side;
                Vector2[] quad = { a, b, c, d };
                int start = vertices.Count;
                float[] heights = quad.Select(point => SampleHeight(terrain, point) + SurfaceClearanceMeters).ToArray();
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices.Add(new Vector3(quad[corner].x, heights[corner], quad[corner].y));
                    uvs.Add(quad[corner] / 64f);
                }
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                AddBankQuad(a, heights[0], d, heights[3], terrain, bankVertices, bankUvs, bankTriangles);
                AddBankQuad(c, heights[2], b, heights[1], terrain, bankVertices, bankUvs, bankTriangles);
            }
        }

        private static void AddBankQuad(
            Vector2 a,
            float aTop,
            Vector2 b,
            float bTop,
            ProductionTerrainSurfaceAsset terrain,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles)
        {
            int start = vertices.Count;
            float aGround = SampleHeight(terrain, a) + 0.01f;
            float bGround = SampleHeight(terrain, b) + 0.01f;
            vertices.Add(new Vector3(a.x, aGround, a.y));
            vertices.Add(new Vector3(b.x, bGround, b.y));
            vertices.Add(new Vector3(b.x, bTop, b.y));
            vertices.Add(new Vector3(a.x, aTop, a.y));
            uvs.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private static int[] Triangulate(Vector2[] points)
        {
            List<int> remaining = Enumerable.Range(0, points.Length).ToList();
            List<int> result = new List<int>((points.Length - 2) * 3);
            bool ccw = SignedArea(points) > 0f;
            int guard = points.Length * points.Length;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int cursor = 0; cursor < remaining.Count; cursor++)
                {
                    int previous = remaining[(cursor - 1 + remaining.Count) % remaining.Count];
                    int current = remaining[cursor];
                    int next = remaining[(cursor + 1) % remaining.Count];
                    float cross = Cross(points[current] - points[previous], points[next] - points[current]);
                    if (ccw ? cross <= 0.0001f : cross >= -0.0001f) continue;
                    if (remaining.Any(index => index != previous && index != current && index != next &&
                                               PointInTriangle(points[index], points[previous], points[current], points[next])))
                        continue;
                    if (ccw) result.AddRange(new[] { previous, current, next });
                    else result.AddRange(new[] { previous, next, current });
                    remaining.RemoveAt(cursor);
                    clipped = true;
                    break;
                }
                if (!clipped) throw new InvalidOperationException("Water polygon could not be triangulated without self-overlap.");
            }
            if (remaining.Count != 3) throw new InvalidOperationException("Water polygon triangulation did not terminate.");
            if (ccw) result.AddRange(new[] { remaining[0], remaining[1], remaining[2] });
            else result.AddRange(new[] { remaining[0], remaining[2], remaining[1] });
            return result.ToArray();
        }

        private static float SignedArea(IReadOnlyList<Vector2> points)
        {
            float area = 0f;
            for (int index = 0; index < points.Count; index++)
                area += Cross(points[index], points[(index + 1) % points.Count]);
            return area * 0.5f;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool negative = d1 < -0.0001f || d2 < -0.0001f || d3 < -0.0001f;
            bool positive = d1 > 0.0001f || d2 > 0.0001f || d3 > 0.0001f;
            return !(negative && positive);
        }

        private static float SampleHeight(ProductionTerrainSurfaceAsset terrain, Vector2 point)
        {
            if (!terrain.TrySample(point.x, point.y, out ProductionTerrainSample sample))
                throw new InvalidOperationException($"Water point lies outside authoritative terrain: {point}");
            return sample.heightMeters;
        }

        private static Vector2[] DecodePoints(WaterFeature feature)
        {
            float[] flat = feature.points_enu_flat_meters ?? Array.Empty<float>();
            if (flat.Length < 4 || (flat.Length & 1) != 0)
                throw new InvalidOperationException("Water feature has invalid flat ENU points: " + feature.stable_id);
            Vector2[] points = new Vector2[flat.Length / 2];
            for (int index = 0; index < points.Length; index++)
                points[index] = new Vector2(flat[index * 2], flat[index * 2 + 1]);
            return points;
        }

        private static void AppendPlacementEntries(
            WaterDocument document,
            ProductionTerrainSurfaceAsset terrain,
            ProductionPlacementManifest manifest,
            TextAsset source)
        {
            List<ProductionPlacementManifest.Entry> entries = manifest.entries
                .Where(entry => entry.category != ProductionPlacementCategory.Water).ToList();
            string guid = AssetDatabase.AssetPathToGUID(ManifestPath);
            foreach (WaterFeature feature in document.represented_features)
            {
                Vector2[] points = DecodePoints(feature);
                Vector2 center = points.Aggregate(Vector2.zero, (sum, point) => sum + point) / points.Length;
                if (!terrain.TrySample(center.x, center.y, out ProductionTerrainSample sample))
                    throw new InvalidOperationException("Water center lies outside authoritative terrain: " + feature.stable_id);
                Vector2[] footprint = string.Equals(feature.geometry_type, "polygon", StringComparison.Ordinal)
                    ? points
                    : BoundingFootprint(points, Mathf.Max(3f, feature.interpreted_width_meters) * 0.5f);
                entries.Add(new ProductionPlacementManifest.Entry
                {
                    stableId = feature.stable_id,
                    sourceFeatureId = feature.source_feature_id,
                    category = ProductionPlacementCategory.Water,
                    localEnuMeters = center,
                    scale = Vector3.one,
                    terrainHeightMeters = sample.heightMeters,
                    terrainNormal = sample.normal,
                    contactPointOffsetMeters = SurfaceClearanceMeters,
                    footprintEnuMeters = footprint,
                    foundationMode = ProductionFoundationMode.WaterBedMask,
                    lodClass = string.Equals(feature.zone, "near_0_2km", StringComparison.Ordinal)
                        ? ProductionLodClass.NearInstanced : ProductionLodClass.MidHlod,
                    hlodCellId = feature.zone,
                    sourceLicenseClass = feature.source_license_class,
                    sourceAssetGuid = guid
                });
            }
            manifest.entries = entries.OrderBy(entry => entry.stableId, StringComparer.Ordinal).ToArray();
        }

        private static Vector2[] BoundingFootprint(Vector2[] points, float padding)
        {
            float minX = points.Min(point => point.x) - padding;
            float maxX = points.Max(point => point.x) + padding;
            float minY = points.Min(point => point.y) - padding;
            float maxY = points.Max(point => point.y) + padding;
            return new[] { new Vector2(minX, minY), new Vector2(maxX, minY), new Vector2(maxX, maxY), new Vector2(minX, maxY) };
        }

        private static Mesh BuildMesh(string name, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, bool upwardNormals)
        {
            if (vertices.Count == 0 || triangles.Count == 0)
                throw new InvalidOperationException(name + " is empty.");
            Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            if (upwardNormals) mesh.SetNormals(Enumerable.Repeat(Vector3.up, vertices.Count).ToList());
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static MeshFilter ReplaceChild(Transform parent, string name, Mesh mesh, Material material)
        {
            Transform existing = parent.Find(name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return filter;
        }

        private static void SaveMesh(Mesh source, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(source, path);
            else
            {
                EditorUtility.CopySerialized(source, existing);
                UnityEngine.Object.DestroyImmediate(source);
                EditorUtility.SetDirty(existing);
            }
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return asset;
        }

        [Serializable]
        private sealed class WaterDocument
        {
            public int source_feature_count;
            public int represented_feature_count;
            public int excluded_feature_count;
            public WaterFeature[] represented_features;
        }

        [Serializable]
        private sealed class WaterFeature
        {
            public string stable_id;
            public string source_feature_id;
            public string geometry_type;
            public string zone;
            public float[] points_enu_flat_meters;
            public float interpreted_width_meters;
            public string name;
            public string source_license_class;
        }
    }
}
