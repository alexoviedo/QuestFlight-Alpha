using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline-only compiler that makes the preserved airport operational patch
    /// meet the coordinate-correct product-AEQD bare-earth terrain without
    /// changing either source asset.
    /// The product continues to serialize a fixed mesh; no runtime repair occurs.
    /// </summary>
    public static class AirportPatchLidarConformanceAuthoring
    {
        public const string SourcePatchPath =
            "Assets/Production/Environment/AirportCoreV4/Generated/KBDU_AirportTerrain_ErrorBounded_V4_2.asset";
        public const string SourceLidarPath =
            PhotorealAerialSurfaceNativeMeshAuthoring.TerrainNativePath;
        public const string ProductTerrainManifestPath =
            PhotorealAerialSurfaceNativeMeshAuthoring.TerrainManifestPath;
        public const string FrozenLidarManifestPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json";
        public const string FrozenRunwayInputPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_RunwayPlaneInputs.json";
        public const string OutputRoot =
            "Assets/Production/Environment/AerialSceneryV1/TerrainIntegration";
        public const string OutputMeshPath = OutputRoot + "/KBDU_AirportPatch_LidarConformed_V1.asset";
        public const string OutputReportPath = OutputRoot + "/KBDU_AirportPatch_LidarConformance_V1.json";
        public const string FrozenLidarManifestSha256 =
            "1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5";
        public const string FrozenRunwayInputSha256 =
            "e0ac3b6e8d076c46e00da041ede94ed7c91e0cd67ffb58df7390742db1570dee";
        public const string ProductTerrainManifestSha256 =
            "969ffd1a3682c5136b9db6f4ad977470fcbb149610d1d032a752a4a1b21c7130";
        public const float BoundarySampleSpacingMeters = 1f;
        public const float MaximumBoundaryMismatchMeters = 0.03f;
        public const float MaximumOriginalBoundaryAdjustmentMeters = 2f;
        public const int MaximumOutputTriangles = 20000;
        public const string DeterministicGeneratedUtc = "2026-07-19T04:00:00Z";

        [MenuItem("QuestFlight/Product/Compile Airport Patch / Lidar Conformance")]
        public static void RunInteractive() => CompileForAerialSceneryV1Integration();

        public static void RunBatch()
        {
            try
            {
                Mesh mesh = CompileForAerialSceneryV1Integration();
                Debug.Log($"[QuestFlight][AirportPatchLidar] SUCCESS mesh={mesh.name} " +
                          $"triangles={TriangleCount(mesh):N0}");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static Mesh CompileForAerialSceneryV1Integration()
        {
            VerifyFrozenInput(FrozenLidarManifestPath, FrozenLidarManifestSha256);
            VerifyFrozenInput(FrozenRunwayInputPath, FrozenRunwayInputSha256);
            VerifyFrozenInput(ProductTerrainManifestPath, ProductTerrainManifestSha256);
            Mesh sourcePatch = LoadRequiredMesh(SourcePatchPath);
            Mesh lidar = LoadRequiredMesh(SourceLidarPath);
            EnsureFolder(OutputRoot);

            CompileResult result = Compile(sourcePatch, lidar);
            Mesh output = AssetDatabase.LoadAssetAtPath<Mesh>(OutputMeshPath);
            if (output == null)
            {
                output = result.mesh;
                AssetDatabase.CreateAsset(output, OutputMeshPath);
            }
            else
            {
                EditorUtility.CopySerialized(result.mesh, output);
                UnityEngine.Object.DestroyImmediate(result.mesh);
            }
            output.name = "KBDU_AirportPatch_LidarConformed_V1";
            EditorUtility.SetDirty(output);
            AssetDatabase.SaveAssets();

            result.report.outputMeshAssetPath = OutputMeshPath;
            result.report.outputMeshAssetSha256 = Sha256(OutputMeshPath);
            string reportJson = JsonUtility.ToJson(result.report, true) + Environment.NewLine;
            File.WriteAllText(ToAbsolutePath(OutputReportPath), reportJson);
            AssetDatabase.ImportAsset(OutputReportPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            ValidateCompiledAsset(output, result.report);
            return output;
        }

        public static void ValidateCompiledAsset(Mesh mesh, ConformanceReport report)
        {
            if (mesh == null) throw new InvalidOperationException("Conformed airport patch mesh is missing.");
            if (report == null) throw new InvalidOperationException("Conformance report is missing.");
            if (!string.Equals(report.schema, "questflight.aerial-scenery-v1.terrain-conformance.v1",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Conformance report schema changed.");
            if (report.outputTriangleCount <= 0 || report.outputTriangleCount > MaximumOutputTriangles ||
                TriangleCount(mesh) != report.outputTriangleCount)
                throw new InvalidOperationException("Conformed terrain triangle contract failed.");
            if (report.boundaryProbeCount < 4000 || !float.IsFinite(report.maximumBoundaryMismatchMeters) ||
                !float.IsFinite(report.meanBoundaryMismatchMeters) || !float.IsFinite(report.p95BoundaryMismatchMeters) ||
                report.maximumBoundaryMismatchMeters < 0 || report.meanBoundaryMismatchMeters < 0 || report.p95BoundaryMismatchMeters < 0 ||
                report.maximumBoundaryMismatchMeters > MaximumBoundaryMismatchMeters)
                throw new InvalidOperationException(
                    $"Airport/lidar boundary mismatch is {report.maximumBoundaryMismatchMeters:0.000000} m.");
            if (report.interiorOriginalVertexChanges != 0 || report.runwayProtectedVertexChanges != 0)
                throw new InvalidOperationException("Conformance changed protected interior vertices.");
            if (report.maximumOriginalBoundaryAdjustmentMeters > MaximumOriginalBoundaryAdjustmentMeters)
                throw new InvalidOperationException(
                    $"Airport boundary adjustment exceeds {MaximumOriginalBoundaryAdjustmentMeters:0.00} m " +
                    $"({report.maximumOriginalBoundaryAdjustmentMeters:0.000000} m).");
            ValidateConformanceReceiptScope(report);
            if (AssetDatabase.GetAssetPath(mesh) == OutputMeshPath && report.outputMeshAssetSha256 != Sha256(OutputMeshPath))
                throw new InvalidOperationException("Conformance output hash differs from the actual serialized airport mesh.");
            if (report.terrainFloat32Repair != null)
            {
                var current = QuestFlightLab.Editor.TerrainFloat32IntegrationRepairAuthoring.ValidateCurrentRoadTerrainIdentity(LoadRequiredMesh(SourceLidarPath));
                if (JsonUtility.ToJson(current.terrainFloat32Repair) != JsonUtility.ToJson(report.terrainFloat32Repair) ||
                    report.outputMeshAssetSha256 != Sha256(OutputMeshPath) || AssetDatabase.GetAssetPath(mesh) != OutputMeshPath)
                    throw new InvalidOperationException("Conformance repair does not match the actual source/query/airport receipt identities.");
                Vector3[] vertices = mesh.vertices;
                for (int index = 0; index < report.terrainFloat32Repair.changedAirportBoundaryVertices.Length; index++)
                    if (vertices[report.terrainFloat32Repair.changedAirportBoundaryVertices[index]].y != report.terrainFloat32Repair.correctedAirportBoundaryY[index])
                        throw new InvalidOperationException("Conformance repair recorded boundary heights differ from the serialized airport mesh.");
            }
            if (!string.Equals(report.lidarManifestSha256, FrozenLidarManifestSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(report.runwayInputSha256, FrozenRunwayInputSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(report.productTerrainManifestSha256, ProductTerrainManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Frozen lidar/runway provenance changed.");
        }

        public static ConformanceReport ParseConformanceReport(string json)
        {
            var report = JsonUtility.FromJson<ConformanceReport>(json);
            if (report == null) throw new InvalidOperationException("Conformance JSON is missing.");
            int token = QuestFlightLab.Editor.TerrainFloat32IntegrationRepairAuthoring.TopLevelPropertyValueOffset(json, "terrainFloat32Repair");
            if (token < 0) report.terrainFloat32Repair = null;
            else if (token >= json.Length || json[token] != '{' || report.terrainFloat32Repair == null)
                throw new InvalidOperationException("Current conformance repair must be an explicit top-level receipt object.");
            else QuestFlightLab.Editor.TerrainFloat32IntegrationRepairAuthoring.RequireSerializedRepairReceiptFields(json.Substring(token));
            return report;
        }

        public static void ValidateConformanceReceiptScope(ConformanceReport report)
        {
            if (report == null) throw new InvalidOperationException("Conformance report is missing.");
            if (report.terrainFloat32Repair == null)
            {
                if (!report.sourceLidarMeshUnchanged || !report.authoritativeTerrainQueryUnchanged || report.sampledSupportAndNativeHeightFootprintPreserved ||
                    report.outputMeshAssetSha256 != QuestFlightLab.Editor.TerrainFloat32IntegrationRepairAuthoring.BeforeAirportSha256)
                    throw new InvalidOperationException("Conformance source/query changes lack the explicit current bounded repair receipt.");
                return;
            }
            var repair = report.terrainFloat32Repair;
            QuestFlightLab.Editor.TerrainFloat32IntegrationRepairAuthoring.ValidateRepairReceiptScope(repair);
            if (report.sourceLidarMeshUnchanged || report.authoritativeTerrainQueryUnchanged || !report.sampledSupportAndNativeHeightFootprintPreserved ||
                report.sourceLidarAssetSha256 != repair.afterNearSha256 || report.outputMeshAssetSha256 != repair.afterAirportSha256 ||
                report.boundaryProbeCount != repair.boundaryProbeCount || report.maximumBoundaryMismatchMeters != repair.maximumBoundaryMismatchMeters ||
                report.meanBoundaryMismatchMeters != repair.meanBoundaryMismatchMeters || report.p95BoundaryMismatchMeters != repair.p95BoundaryMismatchMeters)
                throw new InvalidOperationException("Conformance current flags/hashes/metrics contradict the explicit bounded repair receipt.");
        }

        public sealed class ExistingBoundaryResult
        {
            public Mesh mesh;
            public int[] changedVertexIndices;
            public int[] affectedNormalIndices;
            public int[] roadSampledBoundaryVertexIndices;
            public int probeCount;
            public float maximumBoundaryMismatchMeters;
            public float meanBoundaryMismatchMeters, p95BoundaryMismatchMeters;
            public float maximumHeightAdjustmentMeters;
        }

        /// <summary>
        /// Reconcile only the already-authored boundary that now intersects
        /// accepted road-added near triangles. No subdivision or reconstruction.
        /// </summary>
        public static ExistingBoundaryResult ReconcileExistingRoadBoundary(Mesh existing, Mesh lidar)
        {
            if (existing == null || lidar == null || existing.subMeshCount != 1 || lidar.subMeshCount != 1)
                throw new InvalidOperationException("Existing airport/near fixed meshes are required.");
            RequireBounds(existing.bounds);
            Vector3[] original = existing.vertices;
            Vector3[] vertices = (Vector3[])original.Clone();
            int[] nearTriangles = lidar.triangles;
            SurfaceSampler sampler = new SurfaceSampler(lidar.vertices, nearTriangles, 32f);
            var changed = new List<int>();
            var roadSampled = new List<int>();
            float maximumAdjustment = 0f;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 vertex = vertices[index];
                if (!IsBoundaryPoint(vertex, existing.bounds)) continue;
                float height = sampler.HeightAt(vertex.x, vertex.z, out int face);
                // The documented accepted local road repair preserves the first
                // 46,426 immutable source vertices; only its added faces changed.
                int offset = face * 3;
                if (nearTriangles[offset] < 46426 && nearTriangles[offset + 1] < 46426 && nearTriangles[offset + 2] < 46426)
                    continue;
                roadSampled.Add(index);
                if (height == vertex.y) continue;
                maximumAdjustment = Mathf.Max(maximumAdjustment, Mathf.Abs(height - vertex.y));
                vertex.y = height; vertices[index] = vertex; changed.Add(index);
            }
            Mesh output = UnityEngine.Object.Instantiate(existing);
            output.name = existing.name;
            Bounds preservedBounds = existing.bounds;
            Vector3[] preservedNormals = existing.normals;
            output.vertices = vertices;
            output.RecalculateNormals();
            Vector3[] recalculatedNormals = output.normals;
            var changedSet = new HashSet<int>(changed);
            var adjacent = new HashSet<int>();
            int[] triangles = existing.triangles;
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                int a = triangles[offset], b = triangles[offset + 1], c = triangles[offset + 2];
                if (!changedSet.Contains(a) && !changedSet.Contains(b) && !changedSet.Contains(c)) continue;
                adjacent.Add(a); adjacent.Add(b); adjacent.Add(c);
            }
            foreach (int index in adjacent) preservedNormals[index] = recalculatedNormals[index];
            output.normals = preservedNormals;
            output.bounds = preservedBounds;
            try
            {
                BoundaryMetrics metrics = MeasureBoundary(new SurfaceSampler(output.vertices, output.triangles, 16f), sampler, preservedBounds);
                if (metrics.maximum > MaximumBoundaryMismatchMeters)
                    throw new InvalidOperationException($"Existing boundary reconciliation failed: {metrics.maximum} m.");
                return new ExistingBoundaryResult { mesh = output, changedVertexIndices = changed.ToArray(),
                    roadSampledBoundaryVertexIndices = roadSampled.ToArray(),
                    affectedNormalIndices = adjacent.OrderBy(value => value).ToArray(), probeCount = metrics.count,
                    maximumBoundaryMismatchMeters = metrics.maximum, maximumHeightAdjustmentMeters = maximumAdjustment,
                    meanBoundaryMismatchMeters = metrics.mean, p95BoundaryMismatchMeters = metrics.p95 };
            }
            catch { UnityEngine.Object.DestroyImmediate(output); throw; }
        }

        private static CompileResult Compile(Mesh sourcePatch, Mesh lidar)
        {
            if (sourcePatch.subMeshCount != 1 || lidar.subMeshCount != 1)
                throw new InvalidOperationException("Terrain conformance requires one-submesh fixed meshes.");
            Vector3[] sourceVertices = sourcePatch.vertices;
            int[] sourceTriangles = sourcePatch.triangles;
            Vector2[] sourceUv = sourcePatch.uv;
            bool hasUv = sourceUv != null && sourceUv.Length == sourceVertices.Length;
            Bounds bounds = sourcePatch.bounds;
            RequireBounds(bounds);

            SurfaceSampler lidarSampler = new SurfaceSampler(lidar.vertices, lidar.triangles, 32f);
            List<Vector3> vertices = new List<Vector3>(sourceVertices.Length + 4096);
            List<Vector2> uvs = new List<Vector2>(sourceVertices.Length + 4096);
            int boundaryOriginalVertices = 0;
            float maximumOriginalBoundaryAdjustment = 0f;
            Vector3 maximumAdjustmentPosition = Vector3.zero;
            float maximumAdjustmentSourceY = 0f;
            float maximumAdjustmentLidarY = 0f;
            for (int index = 0; index < sourceVertices.Length; index++)
            {
                Vector3 value = sourceVertices[index];
                if (IsBoundaryPoint(value, bounds))
                {
                    float conformed = lidarSampler.HeightAt(value.x, value.z);
                    float adjustment = Mathf.Abs(conformed - value.y);
                    if (adjustment > maximumOriginalBoundaryAdjustment)
                    {
                        maximumOriginalBoundaryAdjustment = adjustment;
                        maximumAdjustmentPosition = new Vector3(value.x, 0f, value.z);
                        maximumAdjustmentSourceY = value.y;
                        maximumAdjustmentLidarY = conformed;
                    }
                    value.y = conformed;
                    boundaryOriginalVertices++;
                }
                vertices.Add(value);
                uvs.Add(hasUv ? sourceUv[index] : Vector2.zero);
            }

            List<int> triangles = new List<int>(sourceTriangles.Length + 12000);
            int subdividedBoundaryEdges = 0;
            int insertedBoundaryVertices = 0;
            for (int offset = 0; offset < sourceTriangles.Length; offset += 3)
            {
                int[] corners =
                {
                    sourceTriangles[offset], sourceTriangles[offset + 1], sourceTriangles[offset + 2]
                };
                List<int> polygon = new List<int>(18);
                for (int edge = 0; edge < 3; edge++)
                {
                    int fromIndex = corners[edge];
                    int toIndex = corners[(edge + 1) % 3];
                    polygon.Add(fromIndex);
                    Vector3 from = sourceVertices[fromIndex];
                    Vector3 to = sourceVertices[toIndex];
                    if (!IsOuterBoundaryEdge(from, to, bounds)) continue;
                    float length = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
                    int segments = Mathf.Max(1, Mathf.CeilToInt(length / BoundarySampleSpacingMeters));
                    if (segments <= 1) continue;
                    subdividedBoundaryEdges++;
                    for (int segment = 1; segment < segments; segment++)
                    {
                        float t = segment / (float)segments;
                        Vector3 inserted = Vector3.Lerp(from, to, t);
                        inserted.y = lidarSampler.HeightAt(inserted.x, inserted.z);
                        int insertedIndex = vertices.Count;
                        vertices.Add(inserted);
                        uvs.Add(hasUv ? Vector2.Lerp(sourceUv[fromIndex], sourceUv[toIndex], t) : Vector2.zero);
                        polygon.Add(insertedIndex);
                        insertedBoundaryVertices++;
                    }
                }
                for (int index = 1; index < polygon.Count - 1; index++)
                {
                    triangles.Add(polygon[0]);
                    triangles.Add(polygon[index]);
                    triangles.Add(polygon[index + 1]);
                }
            }

            Mesh output = new Mesh
            {
                name = "KBDU_AirportPatch_LidarConformed_V1",
                indexFormat = sourcePatch.indexFormat
            };
            output.SetVertices(vertices);
            output.SetUVs(0, uvs);
            output.SetTriangles(triangles, 0, true);
            output.RecalculateNormals();
            if (hasUv) output.RecalculateTangents();
            output.RecalculateBounds();

            SurfaceSampler outputSampler = new SurfaceSampler(output.vertices, output.triangles, 16f);
            BoundaryMetrics boundary = MeasureBoundary(outputSampler, lidarSampler, bounds);
            int outputTriangles = TriangleCount(output);
            if (outputTriangles > MaximumOutputTriangles)
                throw new InvalidOperationException(
                    $"Conformed patch exceeds {MaximumOutputTriangles:N0} triangles ({outputTriangles:N0}).");

            ConformanceReport report = new ConformanceReport
            {
                schema = "questflight.aerial-scenery-v1.terrain-conformance.v1",
                generatedUtc = DeterministicGeneratedUtc,
                label = "KBDU-inspired visual terrain / not for navigation",
                sourcePatchAssetPath = SourcePatchPath,
                sourcePatchAssetSha256 = Sha256(SourcePatchPath),
                sourceLidarAssetPath = SourceLidarPath,
                sourceLidarAssetSha256 = Sha256(SourceLidarPath),
                lidarManifestSha256 = FrozenLidarManifestSha256,
                runwayInputSha256 = FrozenRunwayInputSha256,
                productTerrainManifestAssetPath = ProductTerrainManifestPath,
                productTerrainManifestSha256 = ProductTerrainManifestSha256,
                method = "Subdivide only outer airport-patch boundary edges to <=1 m, sample the coordinate-correct product-AEQD bare-earth surface, and fan-triangulate the affected visual boundary triangles.",
                boundarySampleSpacingMeters = BoundarySampleSpacingMeters,
                sourceVertexCount = sourceVertices.Length,
                outputVertexCount = vertices.Count,
                sourceTriangleCount = sourceTriangles.Length / 3,
                outputTriangleCount = outputTriangles,
                originalBoundaryVertexCount = boundaryOriginalVertices,
                subdividedBoundaryEdgeCount = subdividedBoundaryEdges,
                insertedBoundaryVertexCount = insertedBoundaryVertices,
                boundaryProbeCount = boundary.count,
                meanBoundaryMismatchMeters = boundary.mean,
                p95BoundaryMismatchMeters = boundary.p95,
                maximumBoundaryMismatchMeters = boundary.maximum,
                maximumOriginalBoundaryAdjustmentMeters = maximumOriginalBoundaryAdjustment,
                maximumOriginalBoundaryAdjustmentPosition = maximumAdjustmentPosition,
                maximumOriginalBoundaryAdjustmentSourceY = maximumAdjustmentSourceY,
                maximumOriginalBoundaryAdjustmentLidarY = maximumAdjustmentLidarY,
                interiorOriginalVertexChanges = 0,
                runwayProtectedVertexChanges = 0,
                sourceLidarMeshUnchanged = true,
                authoritativeTerrainQueryUnchanged = true,
                realWorldAccuracyEstablished = false,
                limitations = new[]
                {
                    "Boundary agreement is between two authored visual meshes, not surveyed real-world accuracy.",
                    "The product-AEQD near terrain samples the pinned public-domain DTM; lidar DSM/nDSM never displaces terrain.",
                    "The airport patch remains the operational-core visual override; physics and the authoritative V2 terrain query asset are unchanged.",
                    "The frozen lidar source OBJ, runway geometry/collider, water, and terrain outside the airport patch are not modified."
                }
            };
            return new CompileResult(output, report);
        }

        private static BoundaryMetrics MeasureBoundary(
            SurfaceSampler patch,
            SurfaceSampler lidar,
            Bounds bounds)
        {
            List<float> errors = new List<float>(5000);
            SampleHorizontal(bounds.min.z, bounds.min.x, bounds.max.x, patch, lidar, errors);
            SampleHorizontal(bounds.max.z, bounds.min.x, bounds.max.x, patch, lidar, errors);
            SampleVertical(bounds.min.x, bounds.min.z, bounds.max.z, patch, lidar, errors);
            SampleVertical(bounds.max.x, bounds.min.z, bounds.max.z, patch, lidar, errors);
            errors.Sort();
            double sum = errors.Sum(value => (double)value);
            int p95Index = Mathf.Clamp(Mathf.CeilToInt(errors.Count * 0.95f) - 1, 0, errors.Count - 1);
            return new BoundaryMetrics(
                errors.Count,
                errors.Count > 0 ? (float)(sum / errors.Count) : 0f,
                errors.Count > 0 ? errors[p95Index] : 0f,
                errors.Count > 0 ? errors[errors.Count - 1] : 0f);
        }

        private static void SampleHorizontal(
            float z,
            float minimum,
            float maximum,
            SurfaceSampler patch,
            SurfaceSampler lidar,
            ICollection<float> errors)
        {
            for (float x = minimum; x <= maximum + 0.001f; x += BoundarySampleSpacingMeters)
                errors.Add(Mathf.Abs(patch.HeightAt(x, z) - lidar.HeightAt(x, z)));
        }

        private static void SampleVertical(
            float x,
            float minimum,
            float maximum,
            SurfaceSampler patch,
            SurfaceSampler lidar,
            ICollection<float> errors)
        {
            for (float z = minimum; z <= maximum + 0.001f; z += BoundarySampleSpacingMeters)
                errors.Add(Mathf.Abs(patch.HeightAt(x, z) - lidar.HeightAt(x, z)));
        }

        private static bool IsBoundaryPoint(Vector3 value, Bounds bounds)
        {
            const float epsilon = 0.002f;
            return Mathf.Abs(value.x - bounds.min.x) <= epsilon ||
                   Mathf.Abs(value.x - bounds.max.x) <= epsilon ||
                   Mathf.Abs(value.z - bounds.min.z) <= epsilon ||
                   Mathf.Abs(value.z - bounds.max.z) <= epsilon;
        }

        private static bool IsOuterBoundaryEdge(Vector3 a, Vector3 b, Bounds bounds)
        {
            const float epsilon = 0.002f;
            return Mathf.Abs(a.x - bounds.min.x) <= epsilon && Mathf.Abs(b.x - bounds.min.x) <= epsilon ||
                   Mathf.Abs(a.x - bounds.max.x) <= epsilon && Mathf.Abs(b.x - bounds.max.x) <= epsilon ||
                   Mathf.Abs(a.z - bounds.min.z) <= epsilon && Mathf.Abs(b.z - bounds.min.z) <= epsilon ||
                   Mathf.Abs(a.z - bounds.max.z) <= epsilon && Mathf.Abs(b.z - bounds.max.z) <= epsilon;
        }

        private static void RequireBounds(Bounds bounds)
        {
            if (Mathf.Abs(bounds.min.x + 800f) > 0.001f || Mathf.Abs(bounds.max.x - 800f) > 0.001f ||
                Mathf.Abs(bounds.min.z + 300f) > 0.001f || Mathf.Abs(bounds.max.z - 300f) > 0.001f)
                throw new InvalidOperationException("Airport patch horizontal bounds changed.");
        }

        private static Mesh LoadRequiredMesh(string path)
        {
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            if (meshes.Length != 1)
                throw new InvalidOperationException($"Expected one mesh at {path}, found {meshes.Length}.");
            return meshes[0];
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private static void VerifyFrozenInput(string path, string expected)
        {
            string actual = Sha256(path);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Frozen input changed: {path} expected {expected}, got {actual}.");
        }

        private static string Sha256(string assetPath)
        {
            using SHA256 algorithm = SHA256.Create();
            using FileStream stream = File.OpenRead(ToAbsolutePath(assetPath));
            return string.Concat(algorithm.ComputeHash(stream).Select(value => value.ToString("x2",
                CultureInfo.InvariantCulture)));
        }

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", assetPath.Replace('/', Path.DirectorySeparatorChar)));

        private static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                count += (int)(mesh.GetIndexCount(subMesh) / 3u);
            return count;
        }

        private readonly struct CompileResult
        {
            public readonly Mesh mesh;
            public readonly ConformanceReport report;

            public CompileResult(Mesh compiledMesh, ConformanceReport compiledReport)
            {
                mesh = compiledMesh;
                report = compiledReport;
            }
        }

        private readonly struct BoundaryMetrics
        {
            public readonly int count;
            public readonly float mean;
            public readonly float p95;
            public readonly float maximum;

            public BoundaryMetrics(int sampleCount, float meanError, float p95Error, float maximumError)
            {
                count = sampleCount;
                mean = meanError;
                p95 = p95Error;
                maximum = maximumError;
            }
        }

        [Serializable]
        public sealed class ConformanceReport
        {
            public string schema;
            public string generatedUtc;
            public string label;
            public string sourcePatchAssetPath;
            public string sourcePatchAssetSha256;
            public string sourceLidarAssetPath;
            public string sourceLidarAssetSha256;
            public string outputMeshAssetPath;
            public string outputMeshAssetSha256;
            public string lidarManifestSha256;
            public string runwayInputSha256;
            public string productTerrainManifestAssetPath;
            public string productTerrainManifestSha256;
            public string method;
            public float boundarySampleSpacingMeters;
            public int sourceVertexCount;
            public int outputVertexCount;
            public int sourceTriangleCount;
            public int outputTriangleCount;
            public int originalBoundaryVertexCount;
            public int subdividedBoundaryEdgeCount;
            public int insertedBoundaryVertexCount;
            public int boundaryProbeCount;
            public float meanBoundaryMismatchMeters;
            public float p95BoundaryMismatchMeters;
            public float maximumBoundaryMismatchMeters;
            public float maximumOriginalBoundaryAdjustmentMeters;
            public Vector3 maximumOriginalBoundaryAdjustmentPosition;
            public float maximumOriginalBoundaryAdjustmentSourceY;
            public float maximumOriginalBoundaryAdjustmentLidarY;
            public int interiorOriginalVertexChanges;
            public int runwayProtectedVertexChanges;
            public bool sourceLidarMeshUnchanged;
            public bool authoritativeTerrainQueryUnchanged;
            public bool sampledSupportAndNativeHeightFootprintPreserved;
            public QuestFlightLab.Editor.TerrainFloat32IntegrationRepairAuthoring.RepairReceipt terrainFloat32Repair;
            public bool realWorldAccuracyEstablished;
            public string[] limitations;
        }

        private sealed class SurfaceSampler
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles;
            private readonly float cellSize;
            private readonly float minimumX;
            private readonly float minimumZ;
            private readonly int width;
            private readonly int height;
            private readonly Dictionary<int, List<int>> bins = new Dictionary<int, List<int>>();

            public SurfaceSampler(Vector3[] sourceVertices, int[] sourceTriangles, float binSize)
            {
                vertices = sourceVertices;
                triangles = sourceTriangles;
                cellSize = binSize;
                minimumX = vertices.Min(value => value.x);
                minimumZ = vertices.Min(value => value.z);
                float maximumX = vertices.Max(value => value.x);
                float maximumZ = vertices.Max(value => value.z);
                width = Mathf.Max(1, Mathf.CeilToInt((maximumX - minimumX) / cellSize) + 1);
                height = Mathf.Max(1, Mathf.CeilToInt((maximumZ - minimumZ) / cellSize) + 1);
                for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
                {
                    Vector3 a = vertices[triangles[triangle * 3]];
                    Vector3 b = vertices[triangles[triangle * 3 + 1]];
                    Vector3 c = vertices[triangles[triangle * 3 + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (Mathf.Abs(normal.y) <= 0.15f) continue;
                    int minX = CellX(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
                    int maxX = CellX(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                    int minZ = CellZ(Mathf.Min(a.z, Mathf.Min(b.z, c.z)));
                    int maxZ = CellZ(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                    for (int z = minZ; z <= maxZ; z++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        int key = z * width + x;
                        if (!bins.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>();
                            bins.Add(key, list);
                        }
                        list.Add(triangle);
                    }
                }
            }

            public float HeightAt(float x, float z) => HeightAt(x, z, out _);

            public float HeightAt(float x, float z, out int sampledTriangle)
            {
                sampledTriangle = -1;
                int key = CellZ(z) * width + CellX(x);
                if (!bins.TryGetValue(key, out List<int> candidates))
                    throw new InvalidOperationException($"Surface has no bin at ({x:0.000}, {z:0.000}).");
                foreach (int triangle in candidates)
                {
                    Vector3 a = vertices[triangles[triangle * 3]];
                    Vector3 b = vertices[triangles[triangle * 3 + 1]];
                    Vector3 c = vertices[triangles[triangle * 3 + 2]];
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) <= 0.000001f) continue;
                    float wa = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                    float wb = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                    float wc = 1f - wa - wb;
                    const float epsilon = 0.00015f;
                    if (wa < -epsilon || wb < -epsilon || wc < -epsilon) continue;
                    sampledTriangle = triangle;
                    return wa * a.y + wb * b.y + wc * c.y;
                }
                throw new InvalidOperationException($"Surface has no triangle at ({x:0.000}, {z:0.000}).");
            }

            private int CellX(float value) => Mathf.Clamp(
                Mathf.FloorToInt((value - minimumX) / cellSize), 0, width - 1);

            private int CellZ(float value) => Mathf.Clamp(
                Mathf.FloorToInt((value - minimumZ) / cellSize), 0, height - 1);
        }
    }
}
