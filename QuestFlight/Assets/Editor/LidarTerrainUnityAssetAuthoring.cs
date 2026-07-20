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
    /// Offline conversion of the frozen Unity-local lidar OBJ into a native Mesh.
    /// Unity's generic OBJ importer changes handedness; parsing the documented
    /// Unity-local vertices directly prevents an east/west mirror in the product.
    /// </summary>
    public static class LidarTerrainUnityAssetAuthoring
    {
        public const string SourceObjPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_NearTerrain_Fixed.obj";
        public const string SourceManifestPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json";
        public const string OutputRoot =
            "Assets/Production/Environment/AerialSceneryV1/TerrainIntegration";
        public const string OutputMeshPath = OutputRoot + "/KBDU_Lidar_NearTerrain_UnityLocal_V1.asset";
        public const string OutputReportPath = OutputRoot + "/KBDU_Lidar_NearTerrain_UnityLocal_V1.json";
        public const string FrozenSourceObjSha256 =
            "4689de66d4b47bdfc1a3ea6e8c33e906d4308bbb77317f23e3dc5185b5cefce8";
        public const string FrozenManifestSha256 =
            "1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5";
        public const int ExpectedVertexCount = 46426;
        public const int ExpectedTriangleCount = 92000;
        public const string DeterministicGeneratedUtc = "2026-07-19T04:15:00Z";

        [MenuItem("QuestFlight/Product/Compile Unity-Local Lidar Terrain")]
        public static void RunInteractive() => CompileForAerialSceneryV1Integration();

        public static void RunBatch()
        {
            try
            {
                Mesh mesh = CompileForAerialSceneryV1Integration();
                Debug.Log($"[QuestFlight][LidarUnityLocal] SUCCESS vertices={mesh.vertexCount:N0} " +
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
            VerifyFrozenInput(SourceObjPath, FrozenSourceObjSha256);
            VerifyFrozenInput(SourceManifestPath, FrozenManifestSha256);
            EnsureFolder(OutputRoot);

            ParseObj(out List<Vector3> vertices, out List<int> triangles);
            Mesh compiled = new Mesh
            {
                name = "KBDU_Lidar_NearTerrain_UnityLocal_V1",
                indexFormat = IndexFormat.UInt16
            };
            compiled.SetVertices(vertices);
            compiled.SetTriangles(triangles, 0, true);
            compiled.RecalculateNormals();
            compiled.RecalculateBounds();

            Mesh output = AssetDatabase.LoadAssetAtPath<Mesh>(OutputMeshPath);
            if (output == null)
            {
                output = compiled;
                AssetDatabase.CreateAsset(output, OutputMeshPath);
            }
            else
            {
                EditorUtility.CopySerialized(compiled, output);
                UnityEngine.Object.DestroyImmediate(compiled);
            }
            output.name = "KBDU_Lidar_NearTerrain_UnityLocal_V1";
            EditorUtility.SetDirty(output);
            AssetDatabase.SaveAssets();

            ConversionReport report = new ConversionReport
            {
                schema = "questflight.aerial-scenery-v1.unity-local-lidar-mesh.v1",
                generatedUtc = DeterministicGeneratedUtc,
                label = "KBDU-inspired visual terrain / not for navigation",
                sourceObjAssetPath = SourceObjPath,
                sourceObjSha256 = FrozenSourceObjSha256,
                sourceManifestSha256 = FrozenManifestSha256,
                outputMeshAssetPath = OutputMeshPath,
                outputMeshAssetSha256 = Sha256(OutputMeshPath),
                conversion = "Direct parse of documented Unity-local +X east/+Y up/+Z north OBJ vertices and one-based triangle indices; generic OBJ handedness conversion bypassed.",
                vertexCount = output.vertexCount,
                triangleCount = TriangleCount(output),
                boundsMinimum = output.bounds.min,
                boundsMaximum = output.bounds.max,
                sourceCoordinatesPreserved = true,
                genericObjImporterBypassed = true,
                runtimeConversionRequired = false,
                realWorldAccuracyEstablished = false,
                limitations = new[]
                {
                    "This fixes asset-coordinate handedness only; it does not establish survey or navigation accuracy.",
                    "The frozen source OBJ and 3DEP provenance manifest remain unchanged.",
                    "The conversion is editor-only and the product serializes the native fixed mesh."
                }
            };
            File.WriteAllText(ToAbsolutePath(OutputReportPath), JsonUtility.ToJson(report, true) + Environment.NewLine);
            AssetDatabase.ImportAsset(OutputReportPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            ValidateCompiledAsset(output, report);
            return output;
        }

        public static void ValidateCompiledAsset(Mesh mesh, ConversionReport report)
        {
            if (mesh == null || report == null) throw new InvalidOperationException("Unity-local lidar derivative is missing.");
            if (!string.Equals(report.schema, "questflight.aerial-scenery-v1.unity-local-lidar-mesh.v1",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Unity-local lidar report schema changed.");
            if (mesh.vertexCount != ExpectedVertexCount || TriangleCount(mesh) != ExpectedTriangleCount ||
                report.vertexCount != ExpectedVertexCount || report.triangleCount != ExpectedTriangleCount)
                throw new InvalidOperationException("Unity-local lidar topology changed.");
            Bounds bounds = mesh.bounds;
            if (Mathf.Abs(bounds.min.x + 2048f) > 0.001f || Mathf.Abs(bounds.max.x - 2048f) > 0.001f ||
                Mathf.Abs(bounds.min.z + 2048f) > 0.001f || Mathf.Abs(bounds.max.z - 2048f) > 0.001f)
                throw new InvalidOperationException("Unity-local lidar horizontal bounds changed.");
            if (!report.sourceCoordinatesPreserved || !report.genericObjImporterBypassed ||
                report.runtimeConversionRequired || report.realWorldAccuracyEstablished)
                throw new InvalidOperationException("Unity-local lidar authority contract changed.");
        }

        private static void ParseObj(out List<Vector3> vertices, out List<int> triangles)
        {
            vertices = new List<Vector3>(ExpectedVertexCount);
            triangles = new List<int>(ExpectedTriangleCount * 3);
            foreach (string raw in File.ReadLines(ToAbsolutePath(SourceObjPath)))
            {
                if (raw.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] values = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length != 4) throw new InvalidOperationException("Unsupported lidar OBJ vertex record.");
                    vertices.Add(new Vector3(
                        float.Parse(values[1], CultureInfo.InvariantCulture),
                        float.Parse(values[2], CultureInfo.InvariantCulture),
                        float.Parse(values[3], CultureInfo.InvariantCulture)));
                }
                else if (raw.StartsWith("f ", StringComparison.Ordinal))
                {
                    string[] values = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length != 4) throw new InvalidOperationException("Lidar OBJ contains a non-triangle face.");
                    triangles.Add(ParseObjIndex(values[1]));
                    triangles.Add(ParseObjIndex(values[2]));
                    triangles.Add(ParseObjIndex(values[3]));
                }
            }
            if (vertices.Count != ExpectedVertexCount || triangles.Count / 3 != ExpectedTriangleCount)
                throw new InvalidOperationException(
                    $"Frozen lidar OBJ topology changed: {vertices.Count:N0}/{triangles.Count / 3:N0}.");
        }

        private static int ParseObjIndex(string value)
        {
            string token = value.Split('/')[0];
            int oneBased = int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (oneBased <= 0) throw new InvalidOperationException("Relative/negative lidar OBJ indices are unsupported.");
            return oneBased - 1;
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

        [Serializable]
        public sealed class ConversionReport
        {
            public string schema;
            public string generatedUtc;
            public string label;
            public string sourceObjAssetPath;
            public string sourceObjSha256;
            public string sourceManifestSha256;
            public string outputMeshAssetPath;
            public string outputMeshAssetSha256;
            public string conversion;
            public int vertexCount;
            public int triangleCount;
            public Vector3 boundsMinimum;
            public Vector3 boundsMaximum;
            public bool sourceCoordinatesPreserved;
            public bool genericObjImporterBypassed;
            public bool runtimeConversionRequired;
            public bool realWorldAccuracyEstablished;
            public string[] limitations;
        }
    }
}
