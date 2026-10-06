using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    /// <summary>Offline authoring for the explicitly serialized static reservoir-watercraft feature.</summary>
    public static class ReservoirWatercraftAuthoring
    {
        private const string FeatureRoot = "Assets/Production/Environment/ReservoirWatercraftV1";
        private const string GeneratedRoot = FeatureRoot + "/Generated";
        private const string ManifestPath = FeatureRoot + "/ReservoirWatercraftPlacementsV1.json";
        private const string AlignmentReportPath = FeatureRoot + "/ReservoirWatercraftSurfaceAlignmentV1.json";
        private const string TemporaryRoot = "Assets/__ReservoirWatercraftTemp";
        private const string EnvironmentPrefabPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string VisibleTerrainObjectName = "Terrain_Mid12km_USGS";
        private const string SharedMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_ParkedAircraft.mat";
        private const float MaximumSurfaceTiltDegrees = 6f;
        private const float MaximumPlaneResidualMeters = 0.035f;

        private static readonly string[] GroupIds = { "open_water", "shore_storage", "dry_storage" };

        [MenuItem("QuestFlight/Reservoir Watercraft V1/Build Serialized Feature")]
        public static void BuildSerializedFeature()
        {
            PlacementDocument document = ReadManifest();
            Material material = LoadRequired<Material>(SharedMaterialPath);
            if (!string.Equals(document.vertical_surface_authority, "ACTIVE_RENDERED_TERRAIN_MESH",
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Reservoir watercraft must use the active rendered terrain mesh as vertical authority.");
            VisibleSurfaceSampler surface = VisibleSurfaceSampler.Create(EnvironmentPrefabPath);
            Dictionary<string, PlacementSolution> solutions = document.records
                .OrderBy(record => record.stable_id, StringComparer.Ordinal)
                .ToDictionary(record => record.stable_id,
                    record => BuildPlacementSolution(record, surface), StringComparer.Ordinal);
            Directory.CreateDirectory(GeneratedRoot);

            foreach (string groupId in GroupIds)
            {
                PlacementRecord[] records = document.records
                    .Where(record => string.Equals(record.spatial_group, groupId, StringComparison.Ordinal))
                    .OrderBy(record => record.stable_id, StringComparer.Ordinal)
                    .ToArray();
                SaveMesh(BuildCombinedMesh(records, solutions, false), MeshPath(groupId, 0));
                SaveMesh(BuildCombinedMesh(records, solutions, true), MeshPath(groupId, 1));
            }

            WriteAlignmentReport(document, surface, solutions);
            AuthorPrefab(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[QuestFlight][ReservoirWatercraftV1] SUCCESS records={document.records.Length}");
        }

        // A terrain edit outside the reservoir can invalidate the dependency hash
        // without moving the surface beneath any craft. Revalidate all nine footprint
        // samples before refreshing that record; never silently accept displaced boats.
        public static void RevalidateExistingSurfaceAlignment()
        {
            var report = JsonUtility.FromJson<AlignmentReport>(File.ReadAllText(AlignmentReportPath));
            var placements = ReadManifest();
            var surface = VisibleSurfaceSampler.Create(EnvironmentPrefabPath);
            foreach (var record in placements.records)
            {
                var old = report.records.Single(r => r.stable_id == record.stable_id);
                var center = new Vector2(record.product_x_meters, record.product_z_meters);
                Vector3 f = Quaternion.Euler(0, record.yaw_degrees, 0) * Vector3.forward * record.footprint_length_meters * 0.5f;
                Vector3 r = Quaternion.Euler(0, record.yaw_degrees, 0) * Vector3.right * record.footprint_width_meters * 0.5f;
                var forward = new Vector2(f.x, f.z);
                var right = new Vector2(r.x, r.z);
                var plane = FitVisibleSurfacePlane(record, surface, new[] { center,
                    center + forward, center - forward, center + right, center - right,
                    center + forward + right, center + forward - right,
                    center - forward + right, center - forward - right });
                float delta = plane.HeightAt(center.x, center.y) - old.surface_center_y_meters;
                Debug.Log($"[ReservoirWatercraft] {record.stable_id} rendered height delta={delta:F6}m");
                if (Mathf.Abs(delta) > 0.001f || Vector3.Distance(plane.normal, old.surface_normal) > 0.00001f ||
                    plane.maximumResidualMeters > MaximumPlaneResidualMeters)
                    throw new InvalidOperationException(record.stable_id + " needs surface realignment.");
            }
            report.surface_mesh_dependency_hash = surface.DependencyHash;
            report.placement_manifest_sha256 = Sha256(File.ReadAllBytes(ManifestPath));
            File.WriteAllText(AlignmentReportPath, JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(AlignmentReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        public static void BuildBatch()
        {
            try
            {
                BuildSerializedFeature();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        private static Mesh BuildCombinedMesh(
            IReadOnlyList<PlacementRecord> records,
            IReadOnlyDictionary<string, PlacementSolution> solutions,
            bool proxy)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Color32> colors = new List<Color32>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            foreach (PlacementRecord record in records)
            {
                if (proxy)
                {
                    AddPlanformProxy(record, solutions[record.stable_id],
                        vertices, normals, colors, uvs, triangles);
                    continue;
                }
                AddDerivative(record, solutions[record.stable_id],
                    vertices, normals, colors, uvs, triangles);
            }

            Mesh mesh = new Mesh
            {
                name = "ReservoirWatercraft_" + records.First().spatial_group + (proxy ? "_LOD1" : "_LOD0"),
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddDerivative(
            PlacementRecord record,
            PlacementSolution solution,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color32> colors,
            ICollection<Vector2> uvs,
            ICollection<int> triangles)
        {
            Mesh source = LoadDerivativeMesh(record);
            if (source.subMeshCount != 1)
                throw new InvalidOperationException(record.derivative + " must have one submesh.");
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Color32[] sourceColors = source.colors32;
            if (sourceNormals.Length != sourceVertices.Length)
                throw new InvalidOperationException(record.derivative + " is missing complete normals.");

            Matrix4x4 transform = solution.modelTransform;
            Matrix4x4 normalTransform = transform.inverse.transpose;
            int start = vertices.Count;
            for (int index = 0; index < sourceVertices.Length; index++)
            {
                vertices.Add(transform.MultiplyPoint3x4(sourceVertices[index]));
                normals.Add(normalTransform.MultiplyVector(sourceNormals[index]).normalized);
                colors.Add(PaletteColor(record.palette,
                    sourceColors.Length == sourceVertices.Length ? sourceColors[index] : new Color32(255, 0, 0, 255)));
                uvs.Add(Vector2.zero);
            }
            foreach (int index in source.triangles) triangles.Add(start + index);
        }

        private static void AddPlanformProxy(
            PlacementRecord record,
            PlacementSolution solution,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color32> colors,
            ICollection<Vector2> uvs,
            ICollection<int> triangles)
        {
            float halfWidth = record.footprint_width_meters * 0.5f;
            float halfLength = record.footprint_length_meters * 0.5f;
            Matrix4x4 transform = Matrix4x4.TRS(
                solution.anchorWorld, solution.rotation, Vector3.one);
            Vector3[] outline =
            {
                new Vector3(0f, 0f, halfLength),
                new Vector3(-halfWidth, 0f, halfLength * 0.45f),
                new Vector3(-halfWidth * 0.92f, 0f, -halfLength),
                new Vector3(halfWidth * 0.92f, 0f, -halfLength),
                new Vector3(halfWidth, 0f, halfLength * 0.45f)
            };
            int start = vertices.Count;
            Color32 color = PalettePrimary(record.palette);
            foreach (Vector3 point in outline)
            {
                vertices.Add(transform.MultiplyPoint3x4(point));
                normals.Add(solution.normal);
                colors.Add(color);
                uvs.Add(Vector2.zero);
            }
            int[] local = { 0, 2, 1, 0, 3, 2, 0, 4, 3 };
            foreach (int index in local) triangles.Add(start + index);
        }

        private static PlacementSolution BuildPlacementSolution(
            PlacementRecord record,
            VisibleSurfaceSampler surface)
        {
            Mesh source = LoadDerivativeMesh(record);
            Bounds bounds = source.bounds;
            Vector3 scale = new Vector3(
                record.footprint_width_meters / Mathf.Max(bounds.size.x, 0.01f),
                1f,
                record.footprint_length_meters / Mathf.Max(bounds.size.z, 0.01f));
            scale.y = (scale.x + scale.z) * 0.5f;

            Vector3 yawForward = Quaternion.Euler(0f, record.yaw_degrees, 0f) * Vector3.forward;
            Vector3 yawRight = Quaternion.Euler(0f, record.yaw_degrees, 0f) * Vector3.right;
            Vector2 center = new Vector2(record.product_x_meters, record.product_z_meters);
            float halfLength = record.footprint_length_meters * 0.5f;
            float halfWidth = record.footprint_width_meters * 0.5f;
            Vector2 forward = new Vector2(yawForward.x, yawForward.z);
            Vector2 right = new Vector2(yawRight.x, yawRight.z);
            Vector2[] footprintSamples =
            {
                center,
                center + forward * halfLength,
                center - forward * halfLength,
                center + right * halfWidth,
                center - right * halfWidth,
                center + forward * halfLength + right * halfWidth,
                center + forward * halfLength - right * halfWidth,
                center - forward * halfLength + right * halfWidth,
                center - forward * halfLength - right * halfWidth
            };
            ProductionTerrainPlane plane = FitVisibleSurfacePlane(record, surface, footprintSamples);
            float tilt = Vector3.Angle(plane.normal, Vector3.up);
            if (tilt > MaximumSurfaceTiltDegrees)
                throw new InvalidOperationException(
                    $"{record.stable_id} requires implausible {tilt:F2}-degree surface tilt.");
            if (plane.maximumResidualMeters > MaximumPlaneResidualMeters)
                throw new InvalidOperationException(
                    $"{record.stable_id} spans a nonplanar visible surface " +
                    $"({plane.maximumResidualMeters:F3}/{MaximumPlaneResidualMeters:F3} m).");

            Vector3 projectedForward = Vector3.ProjectOnPlane(yawForward, plane.normal).normalized;
            if (projectedForward.sqrMagnitude < 0.99f)
                throw new InvalidOperationException(record.stable_id + " has an invalid surface-aligned heading.");
            Quaternion rotation = Quaternion.LookRotation(projectedForward, plane.normal);
            Vector3 anchorWorld = new Vector3(center.x, plane.HeightAt(center.x, center.y), center.y) +
                                  plane.normal * record.surface_clearance_meters;
            Vector3 scaledAnchor = Vector3.Scale(
                new Vector3(0f, record.anchor_local_y_meters, 0f), scale);
            Vector3 origin = anchorWorld - rotation * scaledAnchor;
            Matrix4x4 modelTransform = Matrix4x4.TRS(origin, rotation, scale);
            float anchorError = Vector3.Distance(
                modelTransform.MultiplyPoint3x4(new Vector3(0f, record.anchor_local_y_meters, 0f)),
                anchorWorld);
            return new PlacementSolution(record, plane, tilt, scale, rotation, origin,
                anchorWorld, modelTransform, anchorError);
        }

        private static ProductionTerrainPlane FitVisibleSurfacePlane(
            PlacementRecord record,
            VisibleSurfaceSampler surface,
            IReadOnlyList<Vector2> points)
        {
            double sx = 0d, sz = 0d, sy = 0d;
            double sxx = 0d, szz = 0d, sxz = 0d, sxy = 0d, szy = 0d;
            List<Vector3> samples = new List<Vector3>(points.Count);
            foreach (Vector2 point in points)
            {
                if (!surface.TrySample(point, out float height, out _))
                    throw new InvalidOperationException(
                        $"Visible terrain sampling failed for {record.stable_id} at ({point.x:F2}, {point.y:F2}).");
                samples.Add(new Vector3(point.x, height, point.y));
                double x = point.x;
                double z = point.y;
                double y = height;
                sx += x;
                sz += z;
                sy += y;
                sxx += x * x;
                szz += z * z;
                sxz += x * z;
                sxy += x * y;
                szy += z * y;
            }
            if (!Solve3x3(
                    sxx, sxz, sx,
                    sxz, szz, sz,
                    sx, sz, samples.Count,
                    sxy, szy, sy,
                    out double a, out double b, out double c))
                throw new InvalidOperationException("Visible terrain plane fit failed for " + record.stable_id);
            Vector3 center = new Vector3((float)(sx / samples.Count), 0f, (float)(sz / samples.Count));
            center.y = (float)(a * center.x + b * center.z + c);
            Vector3 normal = new Vector3((float)-a, 1f, (float)-b).normalized;
            float residual = samples.Max(sample => Mathf.Abs(
                sample.y - (float)(a * sample.x + b * sample.z + c)));
            return new ProductionTerrainPlane(center, normal, residual);
        }

        private static bool Solve3x3(
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            double m20, double m21, double m22,
            double r0, double r1, double r2,
            out double x, out double y, out double z)
        {
            double determinant =
                m00 * (m11 * m22 - m12 * m21) -
                m01 * (m10 * m22 - m12 * m20) +
                m02 * (m10 * m21 - m11 * m20);
            if (Math.Abs(determinant) < 1e-9)
            {
                x = y = z = 0d;
                return false;
            }
            x = (r0 * (m11 * m22 - m12 * m21) - m01 * (r1 * m22 - m12 * r2) +
                 m02 * (r1 * m21 - m11 * r2)) / determinant;
            y = (m00 * (r1 * m22 - m12 * r2) - r0 * (m10 * m22 - m12 * m20) +
                 m02 * (m10 * r2 - r1 * m20)) / determinant;
            z = (m00 * (m11 * r2 - r1 * m21) - m01 * (m10 * r2 - r1 * m20) +
                 r0 * (m10 * m21 - m11 * m20)) / determinant;
            return true;
        }

        private static Mesh LoadDerivativeMesh(PlacementRecord record)
        {
            GameObject model = LoadRequired<GameObject>(TemporaryRoot + "/" + record.derivative + ".fbx");
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null).ToArray();
            if (filters.Length != 1)
                throw new InvalidOperationException(record.derivative + " must import as exactly one mesh.");
            Mesh source = filters[0].sharedMesh;
            if (source.subMeshCount != 1)
                throw new InvalidOperationException(record.derivative + " must have one submesh.");
            return source;
        }

        private static Color32 PaletteColor(string palette, Color32 marker)
        {
            bool red = marker.r > 127;
            bool green = marker.g > 127;
            bool blue = marker.b > 127;
            if (red && !green && !blue) return PalettePrimary(palette);
            if (!red && green && !blue) return new Color32(24, 29, 33, 255);
            if (!red && !green && blue) return new Color32(92, 98, 101, 255);
            if (red && green && !blue)
            {
                if (palette.StartsWith("sailer_", StringComparison.Ordinal))
                    return palette.EndsWith("navy", StringComparison.Ordinal) || palette.EndsWith("blue", StringComparison.Ordinal)
                        ? new Color32(27, 45, 70, 255) : new Color32(64, 68, 70, 255);
                return new Color32(220, 219, 210, 255);
            }
            if (red && !green && blue)
            {
                if (palette.Contains("red") || palette.Contains("orange")) return new Color32(145, 53, 35, 255);
                if (palette.Contains("blue") || palette.Contains("navy")) return new Color32(35, 62, 91, 255);
                return new Color32(68, 73, 75, 255);
            }
            if (!red && green && blue) return new Color32(112, 116, 116, 255);
            return PalettePrimary(palette);
        }

        private static Color32 PalettePrimary(string palette)
        {
            if (palette.Contains("muted_teal")) return new Color32(36, 112, 118, 255);
            if (palette.Contains("deep_blue")) return new Color32(45, 73, 103, 255);
            if (palette.Contains("muted_orange")) return new Color32(171, 78, 35, 255);
            if (palette.Contains("lightgray")) return new Color32(194, 197, 193, 255);
            return new Color32(218, 217, 207, 255);
        }

        private static void AuthorPrefab(Material material)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                Transform previous = root.transform.Find("ReservoirWatercraftRoot");
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
                GameObject feature = new GameObject("ReservoirWatercraftRoot");
                feature.transform.SetParent(root.transform, false);
                SetStatic(feature);
                foreach (string groupId in GroupIds)
                {
                    GameObject group = new GameObject(GroupObjectName(groupId));
                    group.transform.SetParent(feature.transform, false);
                    SetStatic(group);
                    Renderer lod0 = AddRenderer(group.transform, "LOD0", MeshPath(groupId, 0), material);
                    Renderer lod1 = AddRenderer(group.transform, "LOD1", MeshPath(groupId, 1), material);
                    LODGroup lodGroup = group.AddComponent<LODGroup>();
                    lodGroup.fadeMode = LODFadeMode.None;
                    lodGroup.animateCrossFading = false;
                    lodGroup.SetLODs(new[]
                    {
                        new LOD(0.018f, new[] { lod0 }),
                        new LOD(0.0015f, new[] { lod1 })
                    });
                    lodGroup.RecalculateBounds();
                }
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null) throw new InvalidOperationException("ProductionEnvironmentRoot marker missing.");
                contract.reservoirWatercraftRoot = feature.transform;
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Renderer AddRenderer(Transform parent, string name, string meshPath, Material material)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            SetStatic(child);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = LoadRequired<Mesh>(meshPath);
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            return renderer;
        }

        private static void SetStatic(GameObject gameObject)
        {
            GameObjectUtility.SetStaticEditorFlags(gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static string GroupObjectName(string groupId)
        {
            return groupId == "open_water" ? "MooringAndOpenWater" :
                groupId == "shore_storage" ? "ShorelinePaddlecraftStorage" : "DryStorageWatercraft";
        }

        private static string MeshPath(string groupId, int lod)
        {
            return GeneratedRoot + "/ReservoirWatercraft_" + groupId + "_LOD" + lod + ".asset";
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
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
        }

        private static void WriteAlignmentReport(
            PlacementDocument document,
            VisibleSurfaceSampler surface,
            IReadOnlyDictionary<string, PlacementSolution> solutions)
        {
            AlignmentReport report = new AlignmentReport
            {
                schema = "questflight.reservoir-watercraft-surface-alignment-v1",
                classification = "PASS_ACTIVE_RENDERED_SURFACE_ALIGNMENT",
                vertical_surface_authority = document.vertical_surface_authority,
                surface_mesh_asset_path = surface.AssetPath,
                surface_mesh_dependency_hash = surface.DependencyHash,
                placement_manifest_sha256 = Sha256(File.ReadAllBytes(
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..", ManifestPath)))),
                maximum_surface_tilt_degrees = MaximumSurfaceTiltDegrees,
                maximum_plane_residual_meters = MaximumPlaneResidualMeters,
                records = document.records.OrderBy(record => record.stable_id, StringComparer.Ordinal)
                    .Select(record =>
                    {
                        PlacementSolution solution = solutions[record.stable_id];
                        return new AlignmentRecord
                        {
                            stable_id = record.stable_id,
                            placement_mode = record.placement_mode,
                            anchor_mode = record.anchor_mode,
                            surface_center_y_meters = solution.plane.HeightAt(
                                record.product_x_meters, record.product_z_meters),
                            surface_normal = solution.normal,
                            surface_tilt_degrees = solution.surfaceTiltDegrees,
                            maximum_plane_residual_meters = solution.plane.maximumResidualMeters,
                            anchor_local_y_meters = record.anchor_local_y_meters,
                            surface_clearance_meters = record.surface_clearance_meters,
                            scaled_anchor_y_meters = record.anchor_local_y_meters * solution.scale.y,
                            model_origin = solution.origin,
                            model_rotation = solution.rotation,
                            lod0_anchor_error_meters = solution.lod0AnchorErrorMeters,
                            lod1_anchor_error_meters = 0f
                        };
                    }).ToArray()
            };
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", AlignmentReportPath));
            File.WriteAllText(absolute, JsonUtility.ToJson(report, true) + System.Environment.NewLine,
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(AlignmentReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return string.Concat(hash.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }

        private static PlacementDocument ReadManifest()
        {
            TextAsset asset = LoadRequired<TextAsset>(ManifestPath);
            PlacementDocument document = JsonUtility.FromJson<PlacementDocument>(asset.text);
            if (document == null || document.records == null || document.records.Length == 0)
                throw new InvalidOperationException("Reservoir watercraft placement manifest is empty.");
            return document;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException("Required asset is missing.", path);
            return asset;
        }

        [Serializable]
        private sealed class PlacementDocument
        {
            public string vertical_surface_authority;
            public PlacementRecord[] records;
        }

        [Serializable]
        private sealed class PlacementRecord
        {
            public string stable_id;
            public float product_x_meters;
            public float product_z_meters;
            public float yaw_degrees;
            public float footprint_length_meters;
            public float footprint_width_meters;
            public string derivative;
            public string placement_mode;
            public string spatial_group;
            public string palette;
            public string vertical_authority;
            public string anchor_mode;
            public float anchor_local_y_meters;
            public float surface_clearance_meters;
        }

        private sealed class PlacementSolution
        {
            public readonly PlacementRecord record;
            public readonly ProductionTerrainPlane plane;
            public readonly float surfaceTiltDegrees;
            public readonly Vector3 scale;
            public readonly Quaternion rotation;
            public readonly Vector3 origin;
            public readonly Vector3 anchorWorld;
            public readonly Vector3 normal;
            public readonly Matrix4x4 modelTransform;
            public readonly float lod0AnchorErrorMeters;

            public PlacementSolution(
                PlacementRecord placementRecord,
                ProductionTerrainPlane fittedPlane,
                float tiltDegrees,
                Vector3 modelScale,
                Quaternion modelRotation,
                Vector3 modelOrigin,
                Vector3 semanticAnchorWorld,
                Matrix4x4 transform,
                float anchorErrorMeters)
            {
                record = placementRecord;
                plane = fittedPlane;
                surfaceTiltDegrees = tiltDegrees;
                scale = modelScale;
                rotation = modelRotation;
                origin = modelOrigin;
                anchorWorld = semanticAnchorWorld;
                normal = fittedPlane.normal;
                modelTransform = transform;
                lod0AnchorErrorMeters = anchorErrorMeters;
            }
        }

        private sealed class VisibleSurfaceSampler
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles;

            public string AssetPath { get; }
            public string DependencyHash { get; }

            private VisibleSurfaceSampler(
                Vector3[] surfaceVertices,
                int[] surfaceTriangles,
                string assetPath,
                string dependencyHash)
            {
                vertices = surfaceVertices;
                triangles = surfaceTriangles;
                AssetPath = assetPath;
                DependencyHash = dependencyHash;
            }

            public static VisibleSurfaceSampler Create(string environmentPrefabPath)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(environmentPrefabPath);
                try
                {
                    Transform visible = root.GetComponentsInChildren<Transform>(true).SingleOrDefault(item =>
                        string.Equals(item.name, VisibleTerrainObjectName, StringComparison.Ordinal));
                    MeshFilter filter = visible != null ? visible.GetComponent<MeshFilter>() : null;
                    MeshRenderer renderer = visible != null ? visible.GetComponent<MeshRenderer>() : null;
                    if (filter == null || filter.sharedMesh == null || renderer == null ||
                        !renderer.enabled || !visible.gameObject.activeInHierarchy)
                        throw new InvalidOperationException(
                            "The active rendered reservoir terrain mesh is unavailable: " + VisibleTerrainObjectName);
                    Vector3[] source = filter.sharedMesh.vertices;
                    Vector3[] transformed = new Vector3[source.Length];
                    for (int index = 0; index < source.Length; index++)
                    {
                        Vector3 world = filter.transform.TransformPoint(source[index]);
                        transformed[index] = root.transform.InverseTransformPoint(world);
                    }
                    string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                    return new VisibleSurfaceSampler(transformed, filter.sharedMesh.triangles, path,
                        AssetDatabase.GetAssetDependencyHash(path).ToString());
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            public bool TrySample(Vector2 point, out float height, out Vector3 normal)
            {
                bool found = false;
                height = float.NegativeInfinity;
                normal = Vector3.up;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]];
                    Vector3 b = vertices[triangles[index + 1]];
                    Vector3 c = vertices[triangles[index + 2]];
                    if (!TryBarycentric(point, new Vector2(a.x, a.z), new Vector2(b.x, b.z),
                            new Vector2(c.x, c.z), out Vector3 weights))
                        continue;
                    float candidate = a.y * weights.x + b.y * weights.y + c.y * weights.z;
                    if (found && candidate <= height) continue;
                    height = candidate;
                    normal = Vector3.Cross(b - a, c - a).normalized;
                    if (normal.y < 0f) normal = -normal;
                    found = true;
                }
                return found;
            }

            private static bool TryBarycentric(
                Vector2 point,
                Vector2 a,
                Vector2 b,
                Vector2 c,
                out Vector3 weights)
            {
                float denominator = (b.y - c.y) * (a.x - c.x) +
                                    (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(denominator) <= 0.000001f)
                {
                    weights = default;
                    return false;
                }
                float wa = ((b.y - c.y) * (point.x - c.x) +
                            (c.x - b.x) * (point.y - c.y)) / denominator;
                float wb = ((c.y - a.y) * (point.x - c.x) +
                            (a.x - c.x) * (point.y - c.y)) / denominator;
                float wc = 1f - wa - wb;
                const float epsilon = -0.00001f;
                weights = new Vector3(wa, wb, wc);
                return wa >= epsilon && wb >= epsilon && wc >= epsilon;
            }
        }

        [Serializable]
        private sealed class AlignmentReport
        {
            public string schema;
            public string classification;
            public string vertical_surface_authority;
            public string surface_mesh_asset_path;
            public string surface_mesh_dependency_hash;
            public string placement_manifest_sha256;
            public float maximum_surface_tilt_degrees;
            public float maximum_plane_residual_meters;
            public AlignmentRecord[] records;
        }

        [Serializable]
        private sealed class AlignmentRecord
        {
            public string stable_id;
            public string placement_mode;
            public string anchor_mode;
            public float surface_center_y_meters;
            public Vector3 surface_normal;
            public float surface_tilt_degrees;
            public float maximum_plane_residual_meters;
            public float anchor_local_y_meters;
            public float surface_clearance_meters;
            public float scaled_anchor_y_meters;
            public Vector3 model_origin;
            public Quaternion model_rotation;
            public float lod0_anchor_error_meters;
            public float lod1_anchor_error_meters;
        }
    }
}
