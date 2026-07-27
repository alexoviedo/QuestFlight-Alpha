using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Allocation-free, road-constrained traffic presentation. The hierarchy, meshes,
    /// material, and source manifest are authored in the product prefab. Runtime work
    /// is limited to advancing precompiled route distances and issuing four or fewer
    /// opaque instanced draws.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class KbduTrafficRuntime : MonoBehaviour
    {
        public const int ArchetypeCount = 3;
        public const int MaximumDrawCalls = 4;
        public const int MaximumNearInstances = 24;
        public const int MaximumMidInstances = 48;
        public const int MaximumVisibleTriangles = 8000;
        public const float NearDistanceMeters = 550f;
        public const float MaximumDistanceMeters = 1500f;

        [Serializable]
        private sealed class Manifest
        {
            public string schema;
            public string status;
            public ParkedRecord[] parked;
            public RouteRecord[] routes;
            public DynamicRecord[] moving;
        }

        [Serializable]
        private sealed class ParkedRecord
        {
            public string id;
            public int archetype;
            public float x;
            public float y;
            public float z;
            public float yaw_degrees;
        }

        [Serializable]
        private sealed class RouteRecord
        {
            public string id;
            public string road_name;
            public string highway_class;
            public float speed_mps;
            public float length_meters;
            public RouteSample[] samples;
        }

        [Serializable]
        private sealed class RouteSample
        {
            public float x;
            public float y;
            public float z;
            public float distance;
        }

        [Serializable]
        private sealed class DynamicRecord
        {
            public string id;
            public int route_index;
            public int archetype;
            public float initial_distance_meters;
            public float speed_scale;
        }

        [SerializeField] private TextAsset trafficManifest;
        [SerializeField] private Material sharedOpaqueMaterial;
        [SerializeField] private Mesh[] nearArchetypeMeshes = Array.Empty<Mesh>();
        [SerializeField] private Mesh midDistanceMesh;
        [SerializeField] private bool deterministicMotion = true;
        [SerializeField] private bool realtimeVehicleShadows;
        [SerializeField] private bool perVehicleGameObjects;
        [SerializeField] private int authoredParkedCount;
        [SerializeField] private int authoredDynamicCount;
        [SerializeField] private int authoredRouteCount;
        [SerializeField] private int authoredMaximumDrawCount;
        [SerializeField] private int authoredMaximumVisibleTriangles;

        private readonly Matrix4x4[][] nearMatrices =
        {
            new Matrix4x4[MaximumNearInstances],
            new Matrix4x4[MaximumNearInstances],
            new Matrix4x4[MaximumNearInstances]
        };
        private readonly Matrix4x4[] midMatrices = new Matrix4x4[MaximumMidInstances];
        private readonly int[] nearCounts = new int[ArchetypeCount];
        private Manifest parsedManifest;
        private int midCount;

        public TextAsset TrafficManifest => trafficManifest;
        public Material SharedOpaqueMaterial => sharedOpaqueMaterial;
        public Mesh[] NearArchetypeMeshes => nearArchetypeMeshes;
        public Mesh MidDistanceMesh => midDistanceMesh;
        public int AuthoredParkedCount => authoredParkedCount;
        public int AuthoredDynamicCount => authoredDynamicCount;
        public int AuthoredRouteCount => authoredRouteCount;
        public int AuthoredMaximumDrawCount => authoredMaximumDrawCount;
        public int AuthoredMaximumVisibleTriangles => authoredMaximumVisibleTriangles;
        public bool DeterministicMotion => deterministicMotion;
        public bool RealtimeVehicleShadows => realtimeVehicleShadows;
        public bool PerVehicleGameObjects => perVehicleGameObjects;

        public void Configure(TextAsset manifest, Material material, Mesh[] nearMeshes, Mesh midMesh)
        {
            trafficManifest = manifest;
            sharedOpaqueMaterial = material;
            nearArchetypeMeshes = nearMeshes ?? Array.Empty<Mesh>();
            midDistanceMesh = midMesh;
            deterministicMotion = true;
            realtimeVehicleShadows = false;
            perVehicleGameObjects = false;
            ParseManifest();
            authoredParkedCount = parsedManifest?.parked?.Length ?? 0;
            authoredDynamicCount = parsedManifest?.moving?.Length ?? 0;
            authoredRouteCount = parsedManifest?.routes?.Length ?? 0;
            authoredMaximumDrawCount = MaximumDrawCalls;
            authoredMaximumVisibleTriangles = ComputeMaximumVisibleTriangles();
        }

        public bool TryValidateContract(out string report)
        {
            if (trafficManifest == null ||
                sharedOpaqueMaterial == null ||
                sharedOpaqueMaterial.shader == null ||
                nearArchetypeMeshes == null ||
                nearArchetypeMeshes.Length != ArchetypeCount ||
                midDistanceMesh == null)
            {
                report = "missing authored traffic asset";
                return false;
            }
            for (int index = 0; index < nearArchetypeMeshes.Length; index++)
            {
                if (nearArchetypeMeshes[index] == null || nearArchetypeMeshes[index].subMeshCount != 1)
                {
                    report = "near vehicle archetype is missing or not single-submesh";
                    return false;
                }
            }
            if (!sharedOpaqueMaterial.enableInstancing ||
                realtimeVehicleShadows ||
                perVehicleGameObjects ||
                !deterministicMotion)
            {
                report = "traffic runtime policy mismatch";
                return false;
            }
            ParseManifest();
            if (parsedManifest == null ||
                !string.Equals(parsedManifest.schema, "questflight.kbdu-traffic-v1", StringComparison.Ordinal) ||
                !string.Equals(parsedManifest.status, "BOUNDED_AUTHORED_BENCHMARK", StringComparison.Ordinal) ||
                parsedManifest.parked == null ||
                parsedManifest.parked.Length != 30 ||
                parsedManifest.routes == null ||
                parsedManifest.routes.Length != 12 ||
                parsedManifest.moving == null ||
                parsedManifest.moving.Length != 12)
            {
                report = "compiled traffic manifest mismatch";
                return false;
            }
            int triangleCount = ComputeMaximumVisibleTriangles();
            if (authoredMaximumDrawCount != MaximumDrawCalls ||
                triangleCount > MaximumVisibleTriangles ||
                authoredMaximumVisibleTriangles != triangleCount)
            {
                report = $"traffic render budget mismatch: draws={authoredMaximumDrawCount}, triangles={triangleCount}";
                return false;
            }
            report =
                $"parked={parsedManifest.parked.Length}, moving={parsedManifest.moving.Length}, " +
                $"routes={parsedManifest.routes.Length}, draws<={MaximumDrawCalls}, triangles<={triangleCount}";
            return true;
        }

        private void Awake()
        {
            if (!TryValidateContract(out string report))
            {
                Debug.LogError("[QuestFlight][Traffic] Authored traffic contract failed: " + report, this);
            }
        }

        private void OnEnable()
        {
            ParseManifest();
            Camera.onPreCull -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
        }

        private void ParseManifest()
        {
            if (parsedManifest == null && trafficManifest != null)
            {
                parsedManifest = JsonUtility.FromJson<Manifest>(trafficManifest.text);
            }
        }

        private int ComputeMaximumVisibleTriangles()
        {
            int largestNear = 0;
            if (nearArchetypeMeshes != null)
            {
                for (int index = 0; index < nearArchetypeMeshes.Length; index++)
                {
                    Mesh mesh = nearArchetypeMeshes[index];
                    if (mesh != null && mesh.subMeshCount > 0)
                    {
                        largestNear = Mathf.Max(largestNear, (int)(mesh.GetIndexCount(0) / 3u));
                    }
                }
            }
            int mid = midDistanceMesh == null || midDistanceMesh.subMeshCount == 0
                ? 0
                : (int)(midDistanceMesh.GetIndexCount(0) / 3u);
            return largestNear * MaximumNearInstances + mid * MaximumMidInstances;
        }

        private void DrawForCamera(Camera camera)
        {
            if (camera == null ||
                !enabled ||
                !gameObject.activeInHierarchy ||
                !gameObject.scene.IsValid() ||
                parsedManifest == null ||
                sharedOpaqueMaterial == null ||
                (camera.cullingMask & (1 << gameObject.layer)) == 0)
            {
                return;
            }
            float presentationTime = Application.isPlaying
                ? (float)Time.unscaledTimeAsDouble
                : 0f;
            PrepareVisibleInstances(camera, presentationTime);

            int layer = gameObject.layer;
            for (int archetype = 0; archetype < ArchetypeCount; archetype++)
            {
                int count = nearCounts[archetype];
                if (count <= 0)
                {
                    continue;
                }
                Graphics.DrawMeshInstanced(
                    nearArchetypeMeshes[archetype],
                    0,
                    sharedOpaqueMaterial,
                    nearMatrices[archetype],
                    count,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    layer,
                    camera,
                    LightProbeUsage.Off,
                    null);
            }
            if (midCount > 0)
            {
                Graphics.DrawMeshInstanced(
                    midDistanceMesh,
                    0,
                    sharedOpaqueMaterial,
                    midMatrices,
                    midCount,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    layer,
                    camera,
                    LightProbeUsage.Off,
                    null);
            }
        }

        private void PrepareVisibleInstances(Camera camera, float timeSeconds)
        {
            Array.Clear(nearCounts, 0, nearCounts.Length);
            midCount = 0;
            int nearTotal = 0;

            ParkedRecord[] parked = parsedManifest.parked;
            for (int index = 0; index < parked.Length; index++)
            {
                ParkedRecord vehicle = parked[index];
                Vector3 position = new Vector3(vehicle.x, vehicle.y, vehicle.z);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    position,
                    Quaternion.Euler(0f, vehicle.yaw_degrees, 0f),
                    Vector3.one);
                SubmitVisible(camera, position, vehicle.archetype, matrix, ref nearTotal);
            }

            DynamicRecord[] dynamicVehicles = parsedManifest.moving;
            RouteRecord[] routes = parsedManifest.routes;
            for (int index = 0; index < dynamicVehicles.Length; index++)
            {
                DynamicRecord vehicle = dynamicVehicles[index];
                RouteRecord route = routes[vehicle.route_index];
                float distance = Mathf.Repeat(
                    vehicle.initial_distance_meters +
                    timeSeconds * route.speed_mps * vehicle.speed_scale,
                    route.length_meters);
                EvaluateRoute(route, distance, out Vector3 position, out Quaternion rotation);
                Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
                SubmitVisible(camera, position, vehicle.archetype, matrix, ref nearTotal);
            }
        }

        private void SubmitVisible(
            Camera camera,
            Vector3 position,
            int archetype,
            Matrix4x4 matrix,
            ref int nearTotal)
        {
            Vector3 toVehicle = position - camera.transform.position;
            float distanceSquared = toVehicle.sqrMagnitude;
            if (distanceSquared > MaximumDistanceMeters * MaximumDistanceMeters)
            {
                return;
            }
            if (distanceSquared > 150f * 150f &&
                Vector3.Dot(camera.transform.forward, toVehicle) < -0.25f * Mathf.Sqrt(distanceSquared))
            {
                return;
            }
            if (distanceSquared <= NearDistanceMeters * NearDistanceMeters &&
                nearTotal < MaximumNearInstances &&
                archetype >= 0 &&
                archetype < ArchetypeCount &&
                nearCounts[archetype] < nearMatrices[archetype].Length)
            {
                nearMatrices[archetype][nearCounts[archetype]++] = matrix;
                nearTotal++;
                return;
            }
            if (midCount < MaximumMidInstances)
            {
                midMatrices[midCount++] = matrix;
            }
        }

        private static void EvaluateRoute(
            RouteRecord route,
            float distance,
            out Vector3 position,
            out Quaternion rotation)
        {
            RouteSample[] samples = route.samples;
            int low = 0;
            int high = samples.Length - 1;
            while (low + 1 < high)
            {
                int middle = (low + high) >> 1;
                if (samples[middle].distance <= distance)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            RouteSample start = samples[low];
            RouteSample end = samples[Mathf.Min(low + 1, samples.Length - 1)];
            float denominator = Mathf.Max(0.001f, end.distance - start.distance);
            float t = Mathf.Clamp01((distance - start.distance) / denominator);
            Vector3 a = new Vector3(start.x, start.y, start.z);
            Vector3 b = new Vector3(end.x, end.y, end.z);
            position = Vector3.LerpUnclamped(a, b, t);
            Vector3 forward = b - a;
            rotation = forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
        }
    }
}
