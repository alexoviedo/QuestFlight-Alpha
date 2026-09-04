using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestFlightLab.Editor.Tests
{
    public static class GradeSeparatedRoadDiagnosticRunner
    {
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";

        [Serializable] private sealed class Manifest { public Corridor[] corridors; }
        [Serializable] private sealed class Corridor
        {
            public string corridor_id;
            public string road_class;
            public float length_meters;
            public float phase_seconds;
            public float speed_mps;
            public float headway_seconds;
            public int lane_count;
            public string direction_mode;
            public float[] points_xz_meters;
        }
        [Serializable] private sealed class Report
        {
            public string schema = "questflight.grade-separated-road-diagnostic.v1";
            public string generatedUtc;
            public List<SpanReport> spans = new List<SpanReport>();
            public List<VehicleReport> vehicles = new List<VehicleReport>();
            public List<RouteSample> routeSamples = new List<RouteSample>();
        }
        [Serializable] private sealed class SpanReport
        {
            public string id;
            public Vector2 center;
            public List<RendererReport> renderers = new List<RendererReport>();
            public List<RouteReport> routes = new List<RouteReport>();
            public List<ProfilePoint> terrainProfile = new List<ProfilePoint>();
        }
        [Serializable] private sealed class RendererReport
        {
            public string hierarchyPath;
            public string meshPath;
            public string materialPath;
            public int meshVertexCount;
            public int meshTriangleCount;
            public int affectedTriangleCount;
            public Bounds worldBounds;
            public float affectedMinimumY = float.PositiveInfinity;
            public float affectedMaximumY = float.NegativeInfinity;
        }
        [Serializable] private sealed class RouteReport
        {
            public int corridorIndex;
            public string corridorId;
            public string roadClass;
            public string ownership;
            public float centerlineDistanceMeters;
            public float tangentDegreesFromNorth;
            public float lengthMeters;
        }
        [Serializable] private sealed class ProfilePoint
        {
            public float alongMeters;
            public float x;
            public float z;
            public float terrainY;
            public Vector3 terrainNormal;
        }
        [Serializable] private sealed class VehicleReport
        {
            public int archetype;
            public string meshName;
            public string meshPath;
            public int vertexCount;
            public int triangleCount;
            public Bounds localBounds;
            public float minimumLocalY;
            public float maximumLocalY;
        }
        [Serializable] private sealed class RouteSample
        {
            public int corridorIndex;
            public string corridorId;
            public int direction;
            public float routeDistanceMeters;
            public float phaseSeconds;
            public float speedMetersPerSecond;
            public float headwaySeconds;
            public int archetype;
            public Vector2 xzMeters;
            public float terrainY;
            public float finalRootY;
            public Quaternion rotation;
            public int activeInstanceCountAtClockZero;
            public bool evaluationSucceeded;
            public bool terrainSampleSucceeded;
        }

        private readonly struct Span
        {
            public readonly string id;
            public readonly Vector2 center;
            public readonly float halfWidth;
            public readonly float startZ;
            public readonly float endZ;

            public Span(string id, Vector2 center, float halfWidth, float startZ, float endZ)
            {
                this.id = id;
                this.center = center;
                this.halfWidth = halfWidth;
                this.startZ = startZ;
                this.endZ = endZ;
            }

            public bool Contains(Vector3 point) =>
                Mathf.Abs(point.x - center.x) <= halfWidth &&
                point.z >= startZ && point.z <= endZ;
        }

        private static readonly Span[] Spans =
        {
            new Span("47TH_OVER_DIAGONAL", new Vector2(-1545.9f, 323.4f), 22f, 200f, 445f),
            new Span("FOOTHILLS_OVER_DIAGONAL", new Vector2(-1598.3f, -122f), 25f, -210f, -35f)
        };

        public static void Run()
        {
            string output = System.Environment.GetEnvironmentVariable("QFL_GRADE_SEPARATED_DIAGNOSTIC_DIR");
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("QFL_GRADE_SEPARATED_DIAGNOSTIC_DIR is required.");
            Directory.CreateDirectory(output);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            KbduTrafficV2Runtime runtime = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
            ProductionTerrainSurfaceAsset terrain = runtime.TerrainSurface;
            Manifest manifest = JsonUtility.FromJson<Manifest>(runtime.TrajectoryManifest.text);
            Report report = new Report { generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };

            foreach (Span span in Spans)
            {
                SpanReport spanReport = new SpanReport { id = span.id, center = span.center };
                AddRendererOwnership(scene, span, spanReport);
                AddRoutes(manifest, span, spanReport);
                AddTerrainProfile(terrain, span, spanReport);
                report.spans.Add(spanReport);
            }

            Mesh[] meshes = runtime.AcceptedVehicleAssets.NearArchetypeMeshes;
            for (int index = 0; index < meshes.Length; index++)
            {
                Mesh mesh = meshes[index];
                Vector3[] vertices = mesh.vertices;
                report.vehicles.Add(new VehicleReport
                {
                    archetype = index,
                    meshName = mesh.name,
                    meshPath = AssetDatabase.GetAssetPath(mesh),
                    vertexCount = mesh.vertexCount,
                    triangleCount = mesh.triangles.Length / 3,
                    localBounds = mesh.bounds,
                    minimumLocalY = vertices.Min(value => value.y),
                    maximumLocalY = vertices.Max(value => value.y)
                });
            }

            AddDeterministicRouteSamples(runtime, terrain, manifest, report);

            File.WriteAllText(Path.Combine(output, "grade_separated_road_diagnostic.json"),
                JsonUtility.ToJson(report, true));
            Debug.Log("[QuestFlight][GradeSeparatedRoadDiagnostic] " + output);
            EditorApplication.Exit(0);
        }

        private static void AddDeterministicRouteSamples(
            KbduTrafficV2Runtime runtime,
            ProductionTerrainSurfaceAsset terrain,
            Manifest manifest,
            Report report)
        {
            for (int corridorIndex = 0; corridorIndex < manifest.corridors.Length; corridorIndex++)
            {
                Corridor corridor = manifest.corridors[corridorIndex];
                for (int direction = 0; direction < 2; direction++)
                {
                    if (!DirectionEnabled(corridor.direction_mode, direction)) continue;
                    float distance = Mathf.Min(
                        corridor.length_meters * 0.5f,
                        corridor.speed_mps * corridor.headway_seconds * 0.5f);
                    float schedule = corridor.phase_seconds + distance / corridor.speed_mps;
                    bool evaluated = runtime.TryEvaluateCorridor(
                            corridorIndex, direction, schedule,
                            out Vector3 position, out Quaternion rotation);
                    ProductionTerrainSample terrainSample = default;
                    bool sampled = evaluated && terrain.TrySample(
                        position.x, position.z, out terrainSample);
                    long departure = (long)Math.Floor(
                        (schedule - corridor.phase_seconds) / corridor.headway_seconds);
                    report.routeSamples.Add(new RouteSample
                    {
                        corridorIndex = corridorIndex,
                        corridorId = corridor.corridor_id,
                        direction = direction,
                        routeDistanceMeters = distance,
                        phaseSeconds = corridor.phase_seconds,
                        speedMetersPerSecond = corridor.speed_mps,
                        headwaySeconds = corridor.headway_seconds,
                        archetype = SelectArchetype(corridor, departure, direction),
                        xzMeters = new Vector2(position.x, position.z),
                        terrainY = terrainSample.heightMeters,
                        finalRootY = position.y,
                        rotation = rotation,
                        activeInstanceCountAtClockZero = ActiveCountAtClockZero(corridor),
                        evaluationSucceeded = evaluated,
                        terrainSampleSucceeded = sampled
                    });
                }
            }
        }

        private static bool DirectionEnabled(string mode, int direction) =>
            string.Equals(mode, "BIDIRECTIONAL", StringComparison.Ordinal) ||
            (direction == 0 && string.Equals(mode, "FORWARD", StringComparison.Ordinal)) ||
            (direction == 1 && string.Equals(mode, "REVERSE", StringComparison.Ordinal));

        private static int ActiveCountAtClockZero(Corridor corridor)
        {
            double age = PositiveModulo(-corridor.phase_seconds, corridor.headway_seconds);
            double traversal = corridor.length_meters / corridor.speed_mps;
            if (age >= traversal) return 0;
            return 1 + (int)Math.Floor((traversal - age - 0.000001) / corridor.headway_seconds);
        }

        private static int SelectArchetype(Corridor corridor, long departure, int direction)
        {
            int value = PositiveModulo(
                StableHash(corridor.corridor_id) + (int)(departure & 0x7fffffff) * 17 + direction * 71,
                100);
            int heavyPercent = corridor.road_class == "motorway" || corridor.road_class == "trunk"
                ? 12
                : corridor.road_class == "primary" ? 6 : 2;
            if (value < heavyPercent) return 5;
            switch (value % 4)
            {
                case 0: return 0;
                case 1: return 2;
                case 2: return 3;
                default: return 0;
            }
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7fffffff);
            }
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static double PositiveModulo(double value, double divisor)
        {
            double result = value % divisor;
            return result < 0.0 ? result + divisor : result;
        }

        private static void AddRendererOwnership(Scene scene, Span span, SpanReport report)
        {
            foreach (MeshRenderer renderer in scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !renderer.bounds.Intersects(new Bounds(
                        new Vector3(span.center.x, renderer.bounds.center.y, (span.startZ + span.endZ) * 0.5f),
                        new Vector3(span.halfWidth * 2f, 10000f, span.endZ - span.startZ))))
                    continue;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                int affected = 0;
                float minimumY = float.PositiveInfinity;
                float maximumY = float.NegativeInfinity;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[triangles[index]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[triangles[index + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[triangles[index + 2]]);
                    Vector3 center = (a + b + c) / 3f;
                    if (!span.Contains(center) && !span.Contains(a) && !span.Contains(b) && !span.Contains(c))
                        continue;
                    affected++;
                    minimumY = Mathf.Min(minimumY, a.y, b.y, c.y);
                    maximumY = Mathf.Max(maximumY, a.y, b.y, c.y);
                }
                if (affected == 0) continue;
                report.renderers.Add(new RendererReport
                {
                    hierarchyPath = HierarchyPath(renderer.transform),
                    meshPath = AssetDatabase.GetAssetPath(mesh),
                    materialPath = renderer.sharedMaterial != null ? AssetDatabase.GetAssetPath(renderer.sharedMaterial) : null,
                    meshVertexCount = mesh.vertexCount,
                    meshTriangleCount = triangles.Length / 3,
                    affectedTriangleCount = affected,
                    worldBounds = renderer.bounds,
                    affectedMinimumY = minimumY,
                    affectedMaximumY = maximumY
                });
            }
        }

        private static void AddRoutes(Manifest manifest, Span span, SpanReport report)
        {
            for (int corridorIndex = 0; corridorIndex < manifest.corridors.Length; corridorIndex++)
            {
                Corridor corridor = manifest.corridors[corridorIndex];
                float[] points = corridor.points_xz_meters;
                float best = float.PositiveInfinity;
                float angle = 0f;
                for (int index = 0; index + 3 < points.Length; index += 2)
                {
                    Vector2 a = new Vector2(points[index], points[index + 1]);
                    Vector2 b = new Vector2(points[index + 2], points[index + 3]);
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude > 0.0001f
                        ? Mathf.Clamp01(Vector2.Dot(span.center - a, ab) / ab.sqrMagnitude)
                        : 0f;
                    float distance = Vector2.Distance(span.center, a + ab * t);
                    if (distance >= best) continue;
                    best = distance;
                    angle = Mathf.Repeat(Mathf.Atan2(ab.x, ab.y) * Mathf.Rad2Deg, 180f);
                }
                if (best > 30f) continue;
                float fromNorth = Mathf.Min(angle, 180f - angle);
                report.routes.Add(new RouteReport
                {
                    corridorIndex = corridorIndex,
                    corridorId = corridor.corridor_id,
                    roadClass = corridor.road_class,
                    ownership = fromNorth <= 15f ? "UPPER" : fromNorth >= 25f ? "LOWER" : "UNCERTAIN",
                    centerlineDistanceMeters = best,
                    tangentDegreesFromNorth = angle,
                    lengthMeters = corridor.length_meters
                });
            }
        }

        private static void AddTerrainProfile(ProductionTerrainSurfaceAsset terrain, Span span, SpanReport report)
        {
            for (float z = span.startZ; z <= span.endZ + 0.01f; z += 5f)
            {
                if (!terrain.TrySample(span.center.x, z, out ProductionTerrainSample sample)) continue;
                report.terrainProfile.Add(new ProfilePoint
                {
                    alongMeters = z - span.startZ,
                    x = span.center.x,
                    z = z,
                    terrainY = sample.heightMeters,
                    terrainNormal = sample.normal
                });
            }
        }

        private static string HierarchyPath(Transform transform)
        {
            string result = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                result = transform.name + "/" + result;
            }
            return result;
        }
    }
}
