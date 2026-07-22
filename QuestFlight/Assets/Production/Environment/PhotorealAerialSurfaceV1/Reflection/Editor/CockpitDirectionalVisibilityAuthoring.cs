#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor
{
    /// <summary>
    /// Bakes low-frequency four-direction cabin visibility into MeshRenderer additional
    /// vertex streams. Runtime shading interpolates these bytes in the existing cockpit
    /// pass: no shadow map, texture lookup, renderer, material, camera, or draw is added.
    /// </summary>
    public static class CockpitDirectionalVisibilityAuthoring
    {
        public const string AssetFolder =
            PhotorealAerialReflectionAuthoring.Root + "/Cockpit/DirectionalVisibility";
        public const string ManifestPath = AssetFolder + "/C172_CockpitDirectionalVisibility.json";
        public const int DirectionCount = 4;
        public const int SoftSampleCount = 3;
        public const float RayDistanceMeters = 8f;

        private const int BakeLayer = 29;
        private const float SunElevationDegrees = 58.6f;
        private const float SoftYawDegrees = 8f;
        private const float OriginNormalOffsetMeters = 0.004f;
        private const float OriginLightOffsetMeters = 0.002f;

        private sealed class StreamRecord
        {
            public string RendererPath;
            public string SourceMeshPath;
            public string StreamPath;
            public int VertexCount;
            public float MeanVisibility;
        }

        public static void AuthorAndIntegrate()
        {
            EnsureFolder(AssetFolder);
            Dictionary<string, StreamRecord> records = BakeStreams();
            IntegrateStreams(records);
            AssetDatabase.SaveAssets();
            WriteManifest(records.Values.OrderBy(record => record.RendererPath).ToArray());
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateIntegratedStreams();
            Debug.Log(
                $"[QuestFlight][CockpitDirectionalVisibility] Authored {records.Count} " +
                "geometry-derived four-direction streams with zero runtime draws or shadow maps.");
        }

        public static void ValidateIntegratedStreams()
        {
            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(
                PhotorealAerialReflectionAuthoring.AircraftPrefabPath);
            if (aircraft == null) throw new InvalidOperationException("Aircraft prefab is missing.");
            AircraftReferenceFrameRig rig = aircraft.GetComponent<AircraftReferenceFrameRig>();
            Transform cockpit = FindDescendant(rig.AircraftVisualRoot,
                PhotorealAerialReflectionAuthoring.CockpitRootName);
            MeshRenderer[] eligible = EligibleRenderers(cockpit).ToArray();
            if (eligible.Length < 6)
                throw new InvalidOperationException("Too few opaque cockpit renderers accepted visibility streams.");
            foreach (MeshRenderer renderer in eligible)
            {
                Mesh source = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Mesh stream = renderer.additionalVertexStreams;
                if (source == null || stream == null || stream.vertexCount != source.vertexCount ||
                    !stream.HasVertexAttribute(VertexAttribute.Color) ||
                    stream.GetVertexAttributeDimension(VertexAttribute.Color) != 4 ||
                    stream.GetVertexAttributeFormat(VertexAttribute.Color) != VertexAttributeFormat.UNorm8)
                    throw new InvalidOperationException(
                        "Cockpit directional-visibility stream is incomplete: " + renderer.name);
            }
            if (!File.Exists(ManifestPath) ||
                !File.ReadAllText(ManifestPath).Contains(
                    "geometry_raycast_four_cardinal_soft_visibility", StringComparison.Ordinal))
                throw new InvalidOperationException("Cockpit directional-visibility manifest is missing or stale.");
        }

        private static Dictionary<string, StreamRecord> BakeStreams()
        {
            GameObject aircraftAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                PhotorealAerialReflectionAuthoring.AircraftPrefabPath);
            if (aircraftAsset == null) throw new InvalidOperationException("Aircraft prefab is missing.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("A loaded editor scene is required for cockpit visibility baking.");
            GameObject instance = null;
            try
            {
                instance = PrefabUtility.InstantiatePrefab(aircraftAsset, scene) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException("Aircraft prefab could not be instantiated for visibility baking.");
                AircraftReferenceFrameRig rig = instance.GetComponent<AircraftReferenceFrameRig>();
                Transform visual = rig?.AircraftVisualRoot;
                Transform cockpit = visual != null
                    ? FindDescendant(visual, PhotorealAerialReflectionAuthoring.CockpitRootName)
                    : null;
                if (visual == null || cockpit == null)
                    throw new InvalidOperationException("Aircraft visual/cockpit root is missing for visibility baking.");

                MeshRenderer[] renderers = EligibleRenderers(cockpit).ToArray();
                foreach (MeshRenderer renderer in renderers)
                {
                    renderer.gameObject.layer = BakeLayer;
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    MeshCollider collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    collider.convex = false;
                    collider.enabled = true;
                }

                Physics.SyncTransforms();
                Vector3[][] directions = BuildSoftDirections();
                var result = new Dictionary<string, StreamRecord>(StringComparer.Ordinal);
                foreach (MeshRenderer renderer in renderers)
                {
                    Mesh source = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Vector3[] vertices = source.vertices;
                    Vector3[] normals = source.normals;
                    if (vertices.Length == 0 || normals.Length != vertices.Length)
                        throw new InvalidOperationException(
                            "Cockpit source mesh lacks readable positions/normals: " + renderer.name);
                    Color32[] visibility = new Color32[vertices.Length];
                    double total = 0d;
                    for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        Vector3 worldPosition = renderer.transform.TransformPoint(vertices[vertexIndex]);
                        Vector3 worldNormal = renderer.transform.TransformDirection(normals[vertexIndex]).normalized;
                        byte[] channels = new byte[DirectionCount];
                        for (int directionIndex = 0; directionIndex < DirectionCount; directionIndex++)
                        {
                            int visibleSamples = 0;
                            for (int sampleIndex = 0; sampleIndex < SoftSampleCount; sampleIndex++)
                            {
                                Vector3 worldDirection = visual.TransformDirection(
                                    directions[directionIndex][sampleIndex]).normalized;
                                Vector3 origin = worldPosition +
                                    worldNormal * OriginNormalOffsetMeters +
                                    worldDirection * OriginLightOffsetMeters;
                                if (!Physics.Raycast(origin, worldDirection, RayDistanceMeters,
                                        1 << BakeLayer, QueryTriggerInteraction.Ignore))
                                    visibleSamples++;
                            }
                            float value = visibleSamples / (float)SoftSampleCount;
                            channels[directionIndex] = (byte)Mathf.RoundToInt(value * 255f);
                            total += value;
                        }
                        visibility[vertexIndex] = new Color32(
                            channels[0], channels[1], channels[2], channels[3]);
                    }

                    string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, visual);
                    string streamPath = AssetFolder + "/" + Sanitize(renderer.name) + "_" +
                        StableId(rendererPath) + "_Visibility.asset";
                    Mesh stream = AssetDatabase.LoadAssetAtPath<Mesh>(streamPath);
                    if (stream == null)
                    {
                        stream = new Mesh();
                        AssetDatabase.CreateAsset(stream, streamPath);
                    }
                    else
                    {
                        stream.Clear(false);
                    }
                    stream.name = renderer.name + " Directional Visibility";
                    stream.SetVertexBufferParams(vertices.Length,
                        new VertexAttributeDescriptor(
                            VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
                    stream.SetVertexBufferData(visibility, 0, 0, visibility.Length, 0,
                        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                    stream.bounds = source.bounds;
                    EditorUtility.SetDirty(stream);
                    result.Add(rendererPath, new StreamRecord
                    {
                        RendererPath = rendererPath,
                        SourceMeshPath = AssetDatabase.GetAssetPath(source).Replace('\\', '/'),
                        StreamPath = streamPath,
                        VertexCount = vertices.Length,
                        MeanVisibility = (float)(total / (vertices.Length * DirectionCount))
                    });
                }
                return result;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void IntegrateStreams(IReadOnlyDictionary<string, StreamRecord> records)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                PhotorealAerialReflectionAuthoring.AircraftPrefabPath);
            try
            {
                AircraftReferenceFrameRig rig = root.GetComponent<AircraftReferenceFrameRig>();
                Transform visual = rig.AircraftVisualRoot;
                Transform cockpit = FindDescendant(visual,
                    PhotorealAerialReflectionAuthoring.CockpitRootName);
                foreach (MeshRenderer renderer in cockpit.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string path = AnimationUtility.CalculateTransformPath(renderer.transform, visual);
                    renderer.additionalVertexStreams = records.TryGetValue(path, out StreamRecord record)
                        ? AssetDatabase.LoadAssetAtPath<Mesh>(record.StreamPath)
                        : null;
                    EditorUtility.SetDirty(renderer);
                }
                PrefabUtility.SaveAsPrefabAsset(root,
                    PhotorealAerialReflectionAuthoring.AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static IEnumerable<MeshRenderer> EligibleRenderers(Transform cockpit)
        {
            return cockpit.GetComponentsInChildren<MeshRenderer>(true).Where(renderer =>
            {
                Mesh source = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (source == null || source.vertexCount == 0) return false;
                if (renderer.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return !renderer.sharedMaterials.Any(material => material != null && material.shader != null &&
                    (material.shader.name.IndexOf("Window Reflection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     material.shader.name.IndexOf("Instrument Glass", StringComparison.OrdinalIgnoreCase) >= 0));
            });
        }

        private static Vector3[][] BuildSoftDirections()
        {
            float elevation = SunElevationDegrees * Mathf.Deg2Rad;
            Vector3[] cardinals =
            {
                new Vector3(0f, Mathf.Sin(elevation), Mathf.Cos(elevation)),
                new Vector3(Mathf.Cos(elevation), Mathf.Sin(elevation), 0f),
                new Vector3(0f, Mathf.Sin(elevation), -Mathf.Cos(elevation)),
                new Vector3(-Mathf.Cos(elevation), Mathf.Sin(elevation), 0f)
            };
            Vector3[][] result = new Vector3[DirectionCount][];
            for (int index = 0; index < DirectionCount; index++)
            {
                result[index] = new Vector3[SoftSampleCount];
                result[index][0] = cardinals[index].normalized;
                result[index][1] = Quaternion.AngleAxis(-SoftYawDegrees, Vector3.up) * cardinals[index];
                result[index][2] = Quaternion.AngleAxis(SoftYawDegrees, Vector3.up) * cardinals[index];
            }
            return result;
        }

        private static void WriteManifest(IReadOnlyList<StreamRecord> records)
        {
            var json = new StringBuilder(32768);
            json.AppendLine("{");
            json.AppendLine("  \"schema\": \"questflight.cockpit_directional_visibility.v1\",");
            json.AppendLine("  \"method\": \"geometry_raycast_four_cardinal_soft_visibility\",");
            json.AppendLine("  \"runtime_shadow_map\": false,");
            json.AppendLine("  \"runtime_draw_calls_added\": 0,");
            json.AppendLine("  \"runtime_texture_lookups_added\": 0,");
            json.Append("  \"sun_elevation_degrees\": ").Append(
                SunElevationDegrees.ToString("0.0", CultureInfo.InvariantCulture)).AppendLine(",");
            json.Append("  \"soft_samples_per_direction\": ").Append(SoftSampleCount).AppendLine(",");
            json.AppendLine("  \"channel_order\": [\"+Z\", \"+X\", \"-Z\", \"-X\"],");
            json.AppendLine("  \"streams\": [");
            for (int index = 0; index < records.Count; index++)
            {
                StreamRecord record = records[index];
                json.AppendLine("    {");
                AppendJson(json, "renderer_path", record.RendererPath, true, 6);
                AppendJson(json, "source_mesh", record.SourceMeshPath, true, 6);
                AppendJson(json, "stream_asset", record.StreamPath, true, 6);
                AppendJson(json, "stream_sha256", Sha256(record.StreamPath), true, 6);
                json.Append("      \"vertex_count\": ").Append(record.VertexCount).AppendLine(",");
                json.Append("      \"mean_visibility\": ").Append(
                    record.MeanVisibility.ToString("0.######", CultureInfo.InvariantCulture)).AppendLine();
                json.Append("    }").AppendLine(index + 1 < records.Count ? "," : string.Empty);
            }
            json.AppendLine("  ]");
            json.AppendLine("}");
            File.WriteAllText(ManifestPath, json.ToString().Replace("\r\n", "\n"),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static string StableId(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
                return string.Concat(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value))
                    .Take(4).Select(item => item.ToString("x2")));
        }

        private static string Sha256(string assetPath)
        {
            using (FileStream stream = File.OpenRead(assetPath))
            using (SHA256 algorithm = SHA256.Create())
                return string.Concat(algorithm.ComputeHash(stream).Select(item => item.ToString("x2")));
        }

        private static string Sanitize(string value) => new string(value.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_').ToArray());

        private static void AppendJson(
            StringBuilder json, string key, string value, bool comma, int indentation)
        {
            json.Append(' ', indentation).Append('\"').Append(key).Append("\": \"")
                .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                .Append('\"').AppendLine(comma ? "," : string.Empty);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
