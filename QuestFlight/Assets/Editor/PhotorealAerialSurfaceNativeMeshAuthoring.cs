using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Editor-only conversion of the documented Unity-local semantic-surface OBJ
    /// batches into native Mesh assets. Unity's generic OBJ importer changes
    /// handedness, so the shipping prefab must bind these direct-parsed assets.
    /// </summary>
    public static class PhotorealAerialSurfaceNativeMeshAuthoring
    {
        public const string SurfaceRoot =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1";
        public const string BuildingSourceRoot = SurfaceRoot + "/Buildings/Generated";
        public const string BuildingNativeRoot = SurfaceRoot + "/Buildings/Native";
        public const string CanopySourceRoot = SurfaceRoot + "/Canopy/Generated";
        public const string CanopyNativeRoot = SurfaceRoot + "/Canopy/Native";
        public const string TerrainSourceRoot = SurfaceRoot + "/Terrain";
        public const string TerrainNativeRoot = TerrainSourceRoot + "/Native";
        public const string TerrainSourcePath =
            TerrainSourceRoot + "/KBDU_ProductAeqd_BareEarth_Fixed.obj";
        public const string TerrainManifestPath =
            TerrainSourceRoot + "/KBDU_ProductAeqd_BareEarth_Manifest.json";
        public const string TerrainErrorReportPath =
            TerrainSourceRoot + "/KBDU_ProductAeqd_BareEarth_ErrorReport.json";
        public const string TerrainNativePath =
            TerrainNativeRoot + "/KBDU_ProductAeqd_BareEarth_Fixed.asset";
        public const string ReportPath = SurfaceRoot + "/KBDU_AerialSurface_UnityLocalMeshManifestV1.json";
        public const string DeterministicGeneratedUtc = "2026-07-20T18:30:00Z";
        public const int ExpectedBuildingBatchCount = 16;
        public const int ExpectedCanopyBatchCount = 0;
        public const int ExpectedTerrainBatchCount = 1;
        public const int MaximumTerrainTriangleCount = 92000;

        private const float CoordinateToleranceMeters = 0.0005f;
        private const float NormalQuantization = 1_000_000f;

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Compile Unity-Local Native Meshes")]
        public static void RunInteractive()
        {
            CompilationReport report = CompileAll();
            Debug.Log($"[QuestFlight][PhotorealAerialSurfaceNativeMesh] PASS " +
                      $"buildings={report.buildingBatches.Length}, canopy={report.canopyBatches.Length}, " +
                      $"terrain={report.terrainBatches.Length}");
        }

        public static void RunBatch()
        {
            try
            {
                CompilationReport report = CompileAll();
                Debug.Log($"[QuestFlight][PhotorealAerialSurfaceNativeMesh] SUCCESS " +
                          $"buildings={report.buildingBatches.Length}, canopy={report.canopyBatches.Length}, " +
                          $"terrain={report.terrainBatches.Length}");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static CompilationReport CompileAll()
        {
            EnsureFolder(BuildingNativeRoot);
            EnsureFolder(CanopyNativeRoot);
            EnsureFolder(TerrainNativeRoot);

            BatchRecord[] buildings = CompileFolder(
                BuildingSourceRoot,
                BuildingNativeRoot,
                "ProductionBuildingSurfaceLayer",
                ExpectedBuildingBatchCount,
                NativeNormalPolicy.HonorObjSmoothing,
                0);
            BatchRecord[] canopy = CompileFolder(
                CanopySourceRoot,
                CanopyNativeRoot,
                "ProductionCanopySurfaceLayer",
                ExpectedCanopyBatchCount,
                NativeNormalPolicy.HonorObjSmoothing,
                0);
            BatchRecord[] terrain = CompileFolder(
                TerrainSourceRoot,
                TerrainNativeRoot,
                "ProductAeqdBareEarthTerrain",
                ExpectedTerrainBatchCount,
                NativeNormalPolicy.SmoothSharedSourceVertices,
                MaximumTerrainTriangleCount);

            AssetDatabase.SaveAssets();
            foreach (BatchRecord record in buildings.Concat(canopy).Concat(terrain))
                record.outputMeshAssetSha256 = Sha256(record.outputMeshAssetPath);

            CompilationReport report = new CompilationReport
            {
                schema = "questflight.photoreal-aerial-surface-v1.unity-local-native-meshes.v1",
                generatedUtc = DeterministicGeneratedUtc,
                coordinateFrame = "KBDU local tangent ENU; Unity +X=east, +Y=up, +Z=north",
                conversion =
                    "Direct editor-only parse of documented Unity-local OBJ vertices and one-based triangle " +
                    "indices; Unity generic OBJ handedness conversion is bypassed.",
                sourceCoordinatesPreserved = true,
                genericObjImporterBypassed = true,
                runtimeConversionRequired = false,
                buildingBatches = buildings,
                canopyBatches = canopy,
                terrainBatches = terrain,
                terrainSourceManifestAssetPath = TerrainManifestPath,
                terrainSourceManifestSha256 = Sha256(TerrainManifestPath),
                terrainErrorReportAssetPath = TerrainErrorReportPath,
                terrainErrorReportSha256 = Sha256(TerrainErrorReportPath),
                limitations = new[]
                {
                    "This conversion preserves authored asset coordinates; it does not establish survey accuracy.",
                    "Source OBJ files remain offline authoring inputs and are not product-prefab dependencies.",
                    "Static topology and asset size are not physical Quest performance evidence."
                }
            };

            File.WriteAllText(ToAbsolutePath(ReportPath),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            ValidateReport(report);
            return report;
        }

        private static BatchRecord[] CompileFolder(
            string sourceRoot,
            string outputRoot,
            string semanticRoot,
            int expectedCount,
            NativeNormalPolicy normalPolicy,
            int maximumTriangleCount)
        {
            string sourceDirectory = ToAbsolutePath(sourceRoot);
            if (!Directory.Exists(sourceDirectory) && expectedCount != 0)
                throw new InvalidOperationException("Semantic-surface source folder is missing: " + sourceRoot);

            string[] sourcePaths = Directory.Exists(sourceDirectory)
                ? Directory.GetFiles(sourceDirectory, "*.obj", SearchOption.TopDirectoryOnly)
                    .Select(path => sourceRoot + "/" + Path.GetFileName(path))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();
            if (sourcePaths.Length != expectedCount)
                throw new InvalidOperationException(
                    $"{semanticRoot} must have exactly {expectedCount} source batches, found {sourcePaths.Length}.");

            HashSet<string> expectedOutputs = new HashSet<string>(StringComparer.Ordinal);
            List<BatchRecord> records = new List<BatchRecord>(sourcePaths.Length);
            foreach (string sourcePath in sourcePaths)
            {
                string outputPath = outputRoot + "/" + Path.GetFileNameWithoutExtension(sourcePath) + ".asset";
                expectedOutputs.Add(outputPath);
                records.Add(CompileBatch(sourcePath, outputPath, semanticRoot,
                    normalPolicy, maximumTriangleCount));
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { outputRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                    !expectedOutputs.Contains(path))
                    AssetDatabase.DeleteAsset(path);
            }
            return records.ToArray();
        }

        private static BatchRecord CompileBatch(
            string sourcePath,
            string outputPath,
            string semanticRoot,
            NativeNormalPolicy normalPolicy,
            int maximumTriangleCount)
        {
            ParsedObj parsed = ParseObj(sourcePath);
            if (maximumTriangleCount > 0 && parsed.triangles.Count > maximumTriangleCount)
                throw new InvalidOperationException(
                    $"{semanticRoot} exceeds its {maximumTriangleCount:N0}-triangle ceiling: " +
                    $"{parsed.triangles.Count:N0}.");
            BuildNativeArrays(parsed, normalPolicy,
                out List<Vector3> nativeVertices,
                out List<Vector3> nativeNormals,
                out List<Color32> nativeColors,
                out List<int> nativeIndices);

            Mesh compiled = new Mesh
            {
                name = Path.GetFileNameWithoutExtension(sourcePath),
                indexFormat = nativeVertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            compiled.SetVertices(nativeVertices);
            compiled.SetNormals(nativeNormals);
            if (parsed.hasExplicitColors) compiled.SetColors(nativeColors);
            compiled.SetTriangles(nativeIndices, 0, true);
            compiled.RecalculateBounds();

            Bounds sourceBounds = BoundsOf(parsed.vertices);
            Bounds nativeBounds = compiled.bounds;
            RequireBoundsEqual(sourcePath, sourceBounds, nativeBounds);
            // Match the previous ModelImporter read/write-off contract so the
            // direct-parsed correction does not retain a duplicate CPU mesh at runtime.
            compiled.UploadMeshData(true);

            Mesh output = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
            if (output == null)
            {
                output = compiled;
                AssetDatabase.CreateAsset(output, outputPath);
            }
            else
            {
                EditorUtility.CopySerialized(compiled, output);
                UnityEngine.Object.DestroyImmediate(compiled);
            }
            output.name = Path.GetFileNameWithoutExtension(sourcePath);
            EditorUtility.SetDirty(output);

            return new BatchRecord
            {
                semanticRoot = semanticRoot,
                sourceObjAssetPath = sourcePath,
                sourceObjSha256 = Sha256(sourcePath),
                outputMeshAssetPath = outputPath,
                sourceVertexCount = parsed.vertices.Count,
                nativeVertexCount = nativeVertices.Count,
                triangleCount = parsed.triangles.Count,
                hardNormalSplit = normalPolicy == NativeNormalPolicy.HonorObjSmoothing &&
                                  parsed.triangles.Any(face => face.smoothingGroup == 0),
                sourceIndexTopologyPreserved =
                    normalPolicy == NativeNormalPolicy.SmoothSharedSourceVertices,
                normalPolicy = normalPolicy == NativeNormalPolicy.SmoothSharedSourceVertices
                    ? "area_weighted_shared_source_vertices"
                    : "obj_smoothing_groups_with_hard_face_splits",
                indexFormat = output.indexFormat == IndexFormat.UInt16 ? "UInt16" : "UInt32",
                sourceBoundsMinimum = sourceBounds.min,
                sourceBoundsMaximum = sourceBounds.max,
                sourceBoundsCentroid = sourceBounds.center,
                nativeBoundsMinimum = nativeBounds.min,
                nativeBoundsMaximum = nativeBounds.max,
                nativeBoundsCentroid = nativeBounds.center,
                sourceCoordinatesPreserved = true,
                genericObjImporterBypassed = true,
                semanticVertexColors = parsed.hasExplicitColors
            };
        }

        private static ParsedObj ParseObj(string assetPath)
        {
            ParsedObj parsed = new ParsedObj();
            int smoothingGroup = 0;
            Dictionary<string, int> smoothingGroups = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string raw in File.ReadLines(ToAbsolutePath(assetPath)))
            {
                string line = raw.Trim();
                if (line.StartsWith("v ", StringComparison.Ordinal) ||
                    line.StartsWith("v\t", StringComparison.Ordinal))
                {
                    string[] values = Split(line);
                    if (values.Length != 4 && values.Length != 7 && values.Length != 8)
                        throw new InvalidOperationException("Unsupported OBJ vertex record: " + assetPath);
                    parsed.vertices.Add(new Vector3(
                        float.Parse(values[1], CultureInfo.InvariantCulture),
                        float.Parse(values[2], CultureInfo.InvariantCulture),
                        float.Parse(values[3], CultureInfo.InvariantCulture)));
                    if (values.Length > 4)
                    {
                        parsed.hasExplicitColors = true;
                        parsed.colors.Add(new Color(
                            float.Parse(values[4], CultureInfo.InvariantCulture),
                            float.Parse(values[5], CultureInfo.InvariantCulture),
                            float.Parse(values[6], CultureInfo.InvariantCulture),
                            values.Length == 8
                                ? float.Parse(values[7], CultureInfo.InvariantCulture)
                                : 1f));
                    }
                    else
                    {
                        if (parsed.hasExplicitColors)
                            throw new InvalidOperationException(
                                "OBJ cannot mix semantic-colored and uncolored vertices: " + assetPath);
                        parsed.colors.Add(Color.white);
                    }
                }
                else if (line.StartsWith("s ", StringComparison.Ordinal) ||
                         line.StartsWith("s\t", StringComparison.Ordinal))
                {
                    string[] values = Split(line);
                    if (values.Length != 2)
                        throw new InvalidOperationException("Unsupported OBJ smoothing record: " + assetPath);
                    string token = values[1];
                    if (string.Equals(token, "off", StringComparison.OrdinalIgnoreCase) || token == "0")
                        smoothingGroup = 0;
                    else
                    {
                        if (!smoothingGroups.TryGetValue(token, out smoothingGroup))
                        {
                            smoothingGroup = smoothingGroups.Count + 1;
                            smoothingGroups.Add(token, smoothingGroup);
                        }
                    }
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal) ||
                         line.StartsWith("f\t", StringComparison.Ordinal))
                {
                    string[] values = Split(line);
                    if (values.Length != 4)
                        throw new InvalidOperationException(
                            "Semantic-surface OBJ contains a non-triangle face: " + assetPath);
                    parsed.triangles.Add(new ParsedTriangle(
                        ParseObjIndex(values[1], parsed.vertices.Count, assetPath),
                        ParseObjIndex(values[2], parsed.vertices.Count, assetPath),
                        ParseObjIndex(values[3], parsed.vertices.Count, assetPath),
                        smoothingGroup));
                }
            }

            if (parsed.vertices.Count == 0 || parsed.triangles.Count == 0)
                throw new InvalidOperationException("Semantic-surface OBJ is empty: " + assetPath);
            return parsed;
        }

        private static void BuildNativeArrays(
            ParsedObj parsed,
            NativeNormalPolicy normalPolicy,
            out List<Vector3> vertices,
            out List<Vector3> normals,
            out List<Color32> colors,
            out List<int> indices)
        {
            if (normalPolicy == NativeNormalPolicy.SmoothSharedSourceVertices)
            {
                BuildSharedVertexArrays(parsed, out vertices, out normals, out colors, out indices);
                return;
            }

            vertices = new List<Vector3>(parsed.vertices.Count * 2);
            colors = new List<Color32>(parsed.vertices.Count * 2);
            List<Vector3> normalSums = new List<Vector3>(parsed.vertices.Count * 2);
            indices = new List<int>(parsed.triangles.Count * 3);
            Dictionary<VertexKey, int> nativeByKey = new Dictionary<VertexKey, int>();

            foreach (ParsedTriangle triangle in parsed.triangles)
            {
                Vector3 a = parsed.vertices[triangle.a];
                Vector3 b = parsed.vertices[triangle.b];
                Vector3 c = parsed.vertices[triangle.c];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (!Finite(cross) || cross.sqrMagnitude <= 0.0000000001f)
                    throw new InvalidOperationException("Semantic-surface OBJ contains a degenerate triangle.");
                Vector3 faceNormal = cross.normalized;
                int normalX = triangle.smoothingGroup == 0
                    ? Mathf.RoundToInt(faceNormal.x * NormalQuantization)
                    : 0;
                int normalY = triangle.smoothingGroup == 0
                    ? Mathf.RoundToInt(faceNormal.y * NormalQuantization)
                    : 0;
                int normalZ = triangle.smoothingGroup == 0
                    ? Mathf.RoundToInt(faceNormal.z * NormalQuantization)
                    : 0;

                foreach (int sourceIndex in new[] { triangle.a, triangle.b, triangle.c })
                {
                    VertexKey key = new VertexKey(
                        sourceIndex, triangle.smoothingGroup, normalX, normalY, normalZ);
                    if (!nativeByKey.TryGetValue(key, out int nativeIndex))
                    {
                        nativeIndex = vertices.Count;
                        nativeByKey.Add(key, nativeIndex);
                        vertices.Add(parsed.vertices[sourceIndex]);
                        colors.Add(parsed.colors[sourceIndex]);
                        normalSums.Add(Vector3.zero);
                    }
                    normalSums[nativeIndex] += triangle.smoothingGroup == 0 ? faceNormal : cross;
                    indices.Add(nativeIndex);
                }
            }

            normals = normalSums.Select(value => value.normalized).ToList();
            if (vertices.Count != normals.Count || vertices.Count != colors.Count ||
                indices.Count != parsed.triangles.Count * 3)
                throw new InvalidOperationException("Native semantic-surface topology is inconsistent.");
        }

        private static void BuildSharedVertexArrays(
            ParsedObj parsed,
            out List<Vector3> vertices,
            out List<Vector3> normals,
            out List<Color32> colors,
            out List<int> indices)
        {
            vertices = new List<Vector3>(parsed.vertices);
            colors = parsed.colors.Select(value => (Color32)value).ToList();
            List<Vector3> normalSums = Enumerable.Repeat(Vector3.zero, vertices.Count).ToList();
            indices = new List<int>(parsed.triangles.Count * 3);
            foreach (ParsedTriangle triangle in parsed.triangles)
            {
                Vector3 a = parsed.vertices[triangle.a];
                Vector3 b = parsed.vertices[triangle.b];
                Vector3 c = parsed.vertices[triangle.c];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (!Finite(cross) || cross.sqrMagnitude <= 0.0000000001f)
                    throw new InvalidOperationException("Native terrain OBJ contains a degenerate triangle.");
                normalSums[triangle.a] += cross;
                normalSums[triangle.b] += cross;
                normalSums[triangle.c] += cross;
                indices.Add(triangle.a);
                indices.Add(triangle.b);
                indices.Add(triangle.c);
            }

            if (normalSums.Any(value => !Finite(value) || value.sqrMagnitude <= 0.0000000001f))
                throw new InvalidOperationException(
                    "Native terrain OBJ contains an unreferenced vertex or cancelling vertex normal.");
            normals = normalSums.Select(value => value.normalized).ToList();
            if (vertices.Count != parsed.vertices.Count || colors.Count != parsed.colors.Count ||
                indices.Count != parsed.triangles.Count * 3)
                throw new InvalidOperationException("Native terrain source-index topology is inconsistent.");
        }

        private static void ValidateReport(CompilationReport report)
        {
            if (report == null ||
                !string.Equals(report.schema,
                    "questflight.photoreal-aerial-surface-v1.unity-local-native-meshes.v1",
                    StringComparison.Ordinal) ||
                !report.sourceCoordinatesPreserved ||
                !report.genericObjImporterBypassed ||
                report.runtimeConversionRequired ||
                report.buildingBatches == null ||
                report.buildingBatches.Length != ExpectedBuildingBatchCount ||
                report.canopyBatches == null ||
                report.canopyBatches.Length != ExpectedCanopyBatchCount ||
                report.terrainBatches == null ||
                report.terrainBatches.Length != ExpectedTerrainBatchCount ||
                !string.Equals(report.terrainSourceManifestAssetPath, TerrainManifestPath,
                    StringComparison.Ordinal) ||
                !string.Equals(report.terrainErrorReportAssetPath, TerrainErrorReportPath,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Unity-local native semantic-surface report is invalid.");

            foreach (BatchRecord record in report.buildingBatches.Concat(report.canopyBatches)
                         .Concat(report.terrainBatches))
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(record.outputMeshAssetPath);
                if (mesh == null || mesh.GetIndexCount(0) / 3u != record.triangleCount ||
                    !record.sourceCoordinatesPreserved || !record.genericObjImporterBypassed)
                    throw new InvalidOperationException("Native semantic-surface batch is invalid: " +
                                                        record.outputMeshAssetPath);
                RequireBoundsEqual(record.outputMeshAssetPath,
                    new Bounds(record.sourceBoundsCentroid,
                        record.sourceBoundsMaximum - record.sourceBoundsMinimum),
                    mesh.bounds);
                bool isBuilding = string.Equals(record.semanticRoot,
                    "ProductionBuildingSurfaceLayer", StringComparison.Ordinal);
                if (record.semanticVertexColors != isBuilding ||
                    mesh.HasVertexAttribute(VertexAttribute.Color) != isBuilding)
                    throw new InvalidOperationException(
                        "Semantic facade color stream contract failed: " + record.outputMeshAssetPath);
            }
            BatchRecord terrain = report.terrainBatches.Single();
            if (!string.Equals(terrain.sourceObjAssetPath, TerrainSourcePath,
                    StringComparison.Ordinal) ||
                !string.Equals(terrain.outputMeshAssetPath, TerrainNativePath,
                    StringComparison.Ordinal) ||
                terrain.triangleCount > MaximumTerrainTriangleCount ||
                terrain.sourceVertexCount != terrain.nativeVertexCount ||
                !terrain.sourceIndexTopologyPreserved || terrain.hardNormalSplit)
                throw new InvalidOperationException("Product-AEQD native terrain topology is invalid.");
        }

        private static void RequireBoundsEqual(string label, Bounds source, Bounds native)
        {
            if ((source.min - native.min).sqrMagnitude > CoordinateToleranceMeters * CoordinateToleranceMeters ||
                (source.max - native.max).sqrMagnitude > CoordinateToleranceMeters * CoordinateToleranceMeters ||
                (source.center - native.center).sqrMagnitude > CoordinateToleranceMeters * CoordinateToleranceMeters)
                throw new InvalidOperationException(
                    $"Unity-local bounds changed for {label}: source={source}, native={native}.");
        }

        private static Bounds BoundsOf(IReadOnlyList<Vector3> vertices)
        {
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            for (int index = 1; index < vertices.Count; index++) bounds.Encapsulate(vertices[index]);
            return bounds;
        }

        private static int ParseObjIndex(string value, int vertexCount, string path)
        {
            string token = value.Split('/')[0];
            int oneBased = int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (oneBased <= 0 || oneBased > vertexCount)
                throw new InvalidOperationException("OBJ index is invalid in " + path + ": " + value);
            return oneBased - 1;
        }

        private static string[] Split(string value) => value.Split(
            new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

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

        private static string Sha256(string assetPath)
        {
            using SHA256 algorithm = SHA256.Create();
            using FileStream stream = File.OpenRead(ToAbsolutePath(assetPath));
            return string.Concat(algorithm.ComputeHash(stream).Select(value =>
                value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", assetPath.Replace('/', Path.DirectorySeparatorChar)));

        private sealed class ParsedObj
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<ParsedTriangle> triangles = new List<ParsedTriangle>();
            public bool hasExplicitColors;
        }

        private enum NativeNormalPolicy
        {
            HonorObjSmoothing,
            SmoothSharedSourceVertices
        }

        private readonly struct ParsedTriangle
        {
            public readonly int a;
            public readonly int b;
            public readonly int c;
            public readonly int smoothingGroup;

            public ParsedTriangle(int a, int b, int c, int smoothingGroup)
            {
                this.a = a;
                this.b = b;
                this.c = c;
                this.smoothingGroup = smoothingGroup;
            }
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            private readonly int sourceIndex;
            private readonly int smoothingGroup;
            private readonly int normalX;
            private readonly int normalY;
            private readonly int normalZ;

            public VertexKey(int sourceIndex, int smoothingGroup, int normalX, int normalY, int normalZ)
            {
                this.sourceIndex = sourceIndex;
                this.smoothingGroup = smoothingGroup;
                this.normalX = normalX;
                this.normalY = normalY;
                this.normalZ = normalZ;
            }

            public bool Equals(VertexKey other) =>
                sourceIndex == other.sourceIndex && smoothingGroup == other.smoothingGroup &&
                normalX == other.normalX && normalY == other.normalY && normalZ == other.normalZ;

            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = sourceIndex;
                    hash = hash * 397 ^ smoothingGroup;
                    hash = hash * 397 ^ normalX;
                    hash = hash * 397 ^ normalY;
                    hash = hash * 397 ^ normalZ;
                    return hash;
                }
            }
        }

        [Serializable]
        public sealed class CompilationReport
        {
            public string schema;
            public string generatedUtc;
            public string coordinateFrame;
            public string conversion;
            public bool sourceCoordinatesPreserved;
            public bool genericObjImporterBypassed;
            public bool runtimeConversionRequired;
            public BatchRecord[] buildingBatches;
            public BatchRecord[] canopyBatches;
            public BatchRecord[] terrainBatches;
            public string terrainSourceManifestAssetPath;
            public string terrainSourceManifestSha256;
            public string terrainErrorReportAssetPath;
            public string terrainErrorReportSha256;
            public string[] limitations;
        }

        [Serializable]
        public sealed class BatchRecord
        {
            public string semanticRoot;
            public string sourceObjAssetPath;
            public string sourceObjSha256;
            public string outputMeshAssetPath;
            public string outputMeshAssetSha256;
            public int sourceVertexCount;
            public int nativeVertexCount;
            public int triangleCount;
            public bool hardNormalSplit;
            public bool sourceIndexTopologyPreserved;
            public string normalPolicy;
            public string indexFormat;
            public Vector3 sourceBoundsMinimum;
            public Vector3 sourceBoundsMaximum;
            public Vector3 sourceBoundsCentroid;
            public Vector3 nativeBoundsMinimum;
            public Vector3 nativeBoundsMaximum;
            public Vector3 nativeBoundsCentroid;
            public bool sourceCoordinatesPreserved;
            public bool genericObjImporterBypassed;
            public bool semanticVertexColors;
        }
    }
}
