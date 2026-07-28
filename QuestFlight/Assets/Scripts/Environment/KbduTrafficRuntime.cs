using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Allocation-free, road-constrained traffic presentation. Drivers use a fixed-step,
    /// deterministic, IDM-inspired longitudinal controller so following distance responds
    /// to speed and leaders instead of oscillating around a fixed spacing lattice.
    /// Rendering uses six detailed near archetypes and three bounded far batches:
    /// two passenger batches sharing one silhouette mesh plus a semi batch. Surface
    /// classes are vertex encoded and paint is supplied per instance, so whole-map
    /// parked density does not create material clones or GameObjects.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class KbduTrafficRuntime : MonoBehaviour
    {
        public const int ArchetypeCount = 6;
        public const int ProxyArchetypeCount = 3;
        public const int MaximumDrawCalls = 9;
        public const int MaximumNearInstances = 64;
        public const int MaximumFarInstances = 1200;
        public const int PaintVariantCount = 8;
        public const int MaximumVisibleTriangles = 130000;
        public const float NearEnterDistanceMeters = 800f;
        public const float NearExitDistanceMeters = 1000f;
        public const float FarEnterDistanceMeters = 4200f;
        public const float FarExitDistanceMeters = 4500f;
        public const float ParkedCellSizeMeters = 500f;
        public const float SimulationStepSeconds = 0.1f;
        private const float MinimumNumericalGapMeters = 0.05f;
        private const float MaximumFrameCatchupSeconds = 0.5f;

        [Serializable]
        private sealed class Manifest
        {
            public string schema;
            public string status;
            public ParkedRecord[] parked;
            public ParkedCell[] parked_cells;
            public RouteRecord[] routes;
            public DynamicRecord[] moving;
            public RuntimeContract runtime_contract;
        }

        [Serializable]
        private sealed class RuntimeContract
        {
            public string car_following_model;
            public float simulation_step_seconds;
            public float minimum_initial_bumper_gap_meters;
            public float minimum_runtime_bumper_gap_meters;
            public float far_enter_distance_meters;
            public float far_exit_distance_meters;
            public float parked_cell_size_meters;
        }

        [Serializable]
        private sealed class ParkedRecord
        {
            public string id;
            public string lot;
            public int archetype;
            public int paint_index;
            public float x;
            public float y;
            public float z;
            public float yaw_degrees;
        }

        [Serializable]
        private sealed class ParkedCell
        {
            public int cell_x;
            public int cell_z;
            public int start;
            public int count;
            public float min_x;
            public float min_z;
            public float max_x;
            public float max_z;
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
            public int paint_index;
            public float initial_distance_meters;
            public float desired_speed_scale;
            public float time_headway_seconds;
            public float minimum_gap_meters;
            public float maximum_acceleration_mps2;
            public float comfortable_deceleration_mps2;
            public float emergency_deceleration_mps2;
            public float acceleration_response_seconds;
            public float vehicle_length_meters;
        }

        [SerializeField] private TextAsset trafficManifest;
        [SerializeField] private Material sharedOpaqueMaterial;
        [SerializeField] private Mesh[] nearArchetypeMeshes = Array.Empty<Mesh>();
        [SerializeField] private Mesh[] farProxyMeshes = Array.Empty<Mesh>();
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
            new Matrix4x4[MaximumNearInstances],
            new Matrix4x4[MaximumNearInstances],
            new Matrix4x4[MaximumNearInstances],
            new Matrix4x4[MaximumNearInstances]
        };
        private readonly Vector4[][] nearPaintTints =
        {
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances]
        };
        private readonly Vector4[][] nearTrailerTints =
        {
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances]
        };
        private readonly int[] nearCounts = new int[ArchetypeCount];
        private readonly Matrix4x4[][] farMatrices =
        {
            new Matrix4x4[MaximumFarInstances],
            new Matrix4x4[MaximumFarInstances],
            new Matrix4x4[MaximumFarInstances]
        };
        private readonly Vector4[][] farPaintTints =
        {
            new Vector4[MaximumFarInstances],
            new Vector4[MaximumFarInstances],
            new Vector4[MaximumFarInstances]
        };
        private readonly Vector4[][] farTrailerTints =
        {
            new Vector4[MaximumFarInstances],
            new Vector4[MaximumFarInstances],
            new Vector4[MaximumFarInstances]
        };
        private readonly int[] farCounts = new int[ProxyArchetypeCount];
        private readonly Matrix4x4[] selectedMatrices =
            new Matrix4x4[MaximumNearInstances];
        private readonly int[] selectedArchetypes = new int[MaximumNearInstances];
        private readonly int[] selectedPaintIndices = new int[MaximumNearInstances];
        private readonly int[] selectedVehicleIds = new int[MaximumNearInstances];
        private readonly float[] selectedScores = new float[MaximumNearInstances];
        private readonly Matrix4x4[] selectedFarMatrices =
            new Matrix4x4[MaximumFarInstances];
        private readonly int[] selectedFarArchetypes =
            new int[MaximumFarInstances];
        private readonly int[] selectedFarPaintIndices =
            new int[MaximumFarInstances];
        private readonly int[] selectedFarVehicleIds =
            new int[MaximumFarInstances];
        private readonly float[] selectedFarScores =
            new float[MaximumFarInstances];
        private readonly MaterialPropertyBlock[] nearPropertyBlocks =
            new MaterialPropertyBlock[ArchetypeCount];
        private readonly MaterialPropertyBlock[] farPropertyBlocks =
            new MaterialPropertyBlock[ProxyArchetypeCount];
        private static readonly Vector4[] PaintPalette =
        {
            new Vector4(0.78f, 0.79f, 0.76f, 1f), // white
            new Vector4(0.025f, 0.027f, 0.030f, 1f), // black
            new Vector4(0.26f, 0.28f, 0.30f, 1f), // gray
            new Vector4(0.54f, 0.56f, 0.57f, 1f), // silver
            new Vector4(0.055f, 0.15f, 0.31f, 1f), // blue
            new Vector4(0.38f, 0.045f, 0.035f, 1f), // red
            new Vector4(0.055f, 0.20f, 0.11f, 1f), // green
            new Vector4(0.18f, 0.10f, 0.055f, 1f) // brown
        };
        private static readonly Vector4[] TrailerPalette =
        {
            new Vector4(0.78f, 0.79f, 0.77f, 1f), // fleet white
            new Vector4(0.58f, 0.60f, 0.61f, 1f), // aluminum
            new Vector4(0.70f, 0.71f, 0.70f, 1f), // warm white
            new Vector4(0.48f, 0.50f, 0.51f, 1f), // light gray
            new Vector4(0.76f, 0.77f, 0.75f, 1f), // fleet white
            new Vector4(0.62f, 0.64f, 0.64f, 1f), // galvanized silver
            new Vector4(0.72f, 0.73f, 0.71f, 1f), // warm white
            new Vector4(0.52f, 0.53f, 0.52f, 1f) // weathered gray
        };
        private Manifest parsedManifest;
        private float[] previousDistances = Array.Empty<float>();
        private float[] currentDistances = Array.Empty<float>();
        private float[] previousSpeeds = Array.Empty<float>();
        private float[] currentSpeeds = Array.Empty<float>();
        private float[] currentAccelerations = Array.Empty<float>();
        private float[] proposedSpeeds = Array.Empty<float>();
        private float[] proposedAdvances = Array.Empty<float>();
        private int[] routeVehicleStarts = Array.Empty<int>();
        private int[] routeVehicleCounts = Array.Empty<int>();
        private int[] routeVehicleIndices = Array.Empty<int>();
        private float simulationAccumulator;
        private bool simulationInitialized;
        private int selectedCount;
        private int selectedFarCount;
        private bool submissionLogged;
        private byte[] previousLodTier = Array.Empty<byte>();
        private int lastPreparedFrame = -1;
        private Camera lastPreparedCamera;

        public TextAsset TrafficManifest => trafficManifest;
        public Material SharedOpaqueMaterial => sharedOpaqueMaterial;
        public Mesh[] NearArchetypeMeshes => nearArchetypeMeshes;
        public Mesh[] FarProxyMeshes => farProxyMeshes;
        public int AuthoredParkedCount => authoredParkedCount;
        public int AuthoredDynamicCount => authoredDynamicCount;
        public int AuthoredRouteCount => authoredRouteCount;
        public int AuthoredMaximumDrawCount => authoredMaximumDrawCount;
        public int AuthoredMaximumVisibleTriangles => authoredMaximumVisibleTriangles;
        public bool DeterministicMotion => deterministicMotion;
        public bool RealtimeVehicleShadows => realtimeVehicleShadows;
        public bool PerVehicleGameObjects => perVehicleGameObjects;

        public void Configure(
            TextAsset manifest,
            Material material,
            Mesh[] nearMeshes,
            Mesh[] proxyMeshes)
        {
            trafficManifest = manifest;
            sharedOpaqueMaterial = material;
            nearArchetypeMeshes = nearMeshes ?? Array.Empty<Mesh>();
            farProxyMeshes = proxyMeshes ?? Array.Empty<Mesh>();
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
                farProxyMeshes == null ||
                farProxyMeshes.Length != ProxyArchetypeCount)
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
            for (int index = 0; index < farProxyMeshes.Length; index++)
            {
                if (farProxyMeshes[index] == null ||
                    farProxyMeshes[index].subMeshCount != 1)
                {
                    report = "far vehicle proxy is missing or not single-submesh";
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
                !string.Equals(parsedManifest.status, "STREAMED_MAJOR_ROAD_NETWORK", StringComparison.Ordinal) ||
                parsedManifest.parked == null ||
                parsedManifest.parked.Length < 500 ||
                parsedManifest.parked.Length > 50000 ||
                parsedManifest.parked_cells == null ||
                parsedManifest.parked_cells.Length < 1 ||
                parsedManifest.routes == null ||
                parsedManifest.routes.Length < 300 ||
                parsedManifest.moving == null ||
                parsedManifest.moving.Length < 600 ||
                parsedManifest.runtime_contract == null ||
                !string.Equals(
                    parsedManifest.runtime_contract.car_following_model,
                    "IDM_INSPIRED_DETERMINISTIC",
                    StringComparison.Ordinal) ||
                Mathf.Abs(
                    parsedManifest.runtime_contract.simulation_step_seconds -
                    SimulationStepSeconds) > 0.0001f ||
                Mathf.Abs(
                    parsedManifest.runtime_contract.far_enter_distance_meters -
                    FarEnterDistanceMeters) > 0.001f ||
                Mathf.Abs(
                    parsedManifest.runtime_contract.far_exit_distance_meters -
                    FarExitDistanceMeters) > 0.001f ||
                Mathf.Abs(
                    parsedManifest.runtime_contract.parked_cell_size_meters -
                    ParkedCellSizeMeters) > 0.001f ||
                parsedManifest.runtime_contract.minimum_runtime_bumper_gap_meters < 1.5f ||
                !AllParkedCellsAreValid() ||
                !AllFleetIndicesAreValid())
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
                return;
            }
            Debug.Log("[QuestFlight][Traffic] Authored traffic contract ready: " + report, this);
        }

        private void OnEnable()
        {
            ParseManifest();
            if (Application.isPlaying)
            {
                InitializeSimulation();
            }
            Camera.onPreCull -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
            simulationInitialized = false;
            simulationAccumulator = 0f;
            lastPreparedFrame = -1;
            lastPreparedCamera = null;
        }

        private void Update()
        {
            if (!Application.isPlaying || parsedManifest == null)
            {
                return;
            }
            if (!simulationInitialized)
            {
                InitializeSimulation();
            }
            simulationAccumulator += Mathf.Min(
                Time.unscaledDeltaTime,
                MaximumFrameCatchupSeconds);
            while (simulationAccumulator >= SimulationStepSeconds)
            {
                StepSimulation(SimulationStepSeconds);
                simulationAccumulator -= SimulationStepSeconds;
            }
        }

        private void ParseManifest()
        {
            if (parsedManifest == null && trafficManifest != null)
            {
                parsedManifest = JsonUtility.FromJson<Manifest>(trafficManifest.text);
                int parkedCount = parsedManifest?.parked?.Length ?? 0;
                int movingCount = parsedManifest?.moving?.Length ?? 0;
                previousLodTier = new byte[parkedCount + movingCount];
            }
        }

        private void InitializeSimulation()
        {
            if (parsedManifest?.moving == null || parsedManifest.routes == null)
            {
                return;
            }

            int vehicleCount = parsedManifest.moving.Length;
            int routeCount = parsedManifest.routes.Length;
            previousDistances = new float[vehicleCount];
            currentDistances = new float[vehicleCount];
            previousSpeeds = new float[vehicleCount];
            currentSpeeds = new float[vehicleCount];
            currentAccelerations = new float[vehicleCount];
            proposedSpeeds = new float[vehicleCount];
            proposedAdvances = new float[vehicleCount];
            routeVehicleStarts = new int[routeCount];
            routeVehicleCounts = new int[routeCount];
            routeVehicleIndices = new int[vehicleCount];

            for (int index = 0; index < vehicleCount; index++)
            {
                DynamicRecord vehicle = parsedManifest.moving[index];
                if (vehicle.route_index < 0 || vehicle.route_index >= routeCount)
                {
                    continue;
                }
                RouteRecord route = parsedManifest.routes[vehicle.route_index];
                float distance = Mathf.Repeat(
                    vehicle.initial_distance_meters,
                    route.length_meters);
                float speed = Mathf.Max(
                    0f,
                    route.speed_mps * vehicle.desired_speed_scale);
                previousDistances[index] = distance;
                currentDistances[index] = distance;
                previousSpeeds[index] = speed;
                currentSpeeds[index] = speed;
                routeVehicleCounts[vehicle.route_index]++;
            }

            int runningStart = 0;
            for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
            {
                routeVehicleStarts[routeIndex] = runningStart;
                runningStart += routeVehicleCounts[routeIndex];
            }
            int[] fillOffsets = new int[routeCount];
            for (int index = 0; index < vehicleCount; index++)
            {
                int routeIndex = parsedManifest.moving[index].route_index;
                int destination =
                    routeVehicleStarts[routeIndex] + fillOffsets[routeIndex]++;
                routeVehicleIndices[destination] = index;
            }

            // The cyclic order remains valid as vehicles wrap because the safety
            // projection below prevents passing.
            for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
            {
                int start = routeVehicleStarts[routeIndex];
                int count = routeVehicleCounts[routeIndex];
                for (int offset = 1; offset < count; offset++)
                {
                    int value = routeVehicleIndices[start + offset];
                    float valueDistance = currentDistances[value];
                    int insertion = offset - 1;
                    while (
                        insertion >= 0 &&
                        currentDistances[routeVehicleIndices[start + insertion]] >
                        valueDistance)
                    {
                        routeVehicleIndices[start + insertion + 1] =
                            routeVehicleIndices[start + insertion];
                        insertion--;
                    }
                    routeVehicleIndices[start + insertion + 1] = value;
                }
            }

            simulationAccumulator = 0f;
            simulationInitialized = true;
        }

        private void StepSimulation(float deltaTime)
        {
            DynamicRecord[] vehicles = parsedManifest.moving;
            RouteRecord[] routes = parsedManifest.routes;

            for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
            {
                int start = routeVehicleStarts[routeIndex];
                int count = routeVehicleCounts[routeIndex];
                for (int offset = 0; offset < count; offset++)
                {
                    int vehicleIndex = routeVehicleIndices[start + offset];
                    DynamicRecord vehicle = vehicles[vehicleIndex];
                    float speed = currentSpeeds[vehicleIndex];
                    float desiredSpeed =
                        routes[routeIndex].speed_mps *
                        vehicle.desired_speed_scale;
                    float targetAcceleration;
                    if (count <= 1)
                    {
                        targetAcceleration = CalculateFreeAcceleration(
                            speed,
                            desiredSpeed,
                            vehicle.maximum_acceleration_mps2);
                    }
                    else
                    {
                        int leaderIndex = routeVehicleIndices[
                            start + ((offset + 1) % count)];
                        DynamicRecord leader = vehicles[leaderIndex];
                        float centerDistance = Mathf.Repeat(
                            currentDistances[leaderIndex] -
                            currentDistances[vehicleIndex],
                            routes[routeIndex].length_meters);
                        float bumperGap = Mathf.Max(
                            MinimumNumericalGapMeters,
                            centerDistance -
                            0.5f * (
                                vehicle.vehicle_length_meters +
                                leader.vehicle_length_meters));
                        targetAcceleration = CalculateCarFollowingAcceleration(
                            speed,
                            currentSpeeds[leaderIndex],
                            desiredSpeed,
                            bumperGap,
                            vehicle.time_headway_seconds,
                            vehicle.minimum_gap_meters,
                            vehicle.maximum_acceleration_mps2,
                            vehicle.comfortable_deceleration_mps2,
                            vehicle.emergency_deceleration_mps2);
                    }

                    float responseSeconds = Mathf.Max(
                        deltaTime,
                        vehicle.acceleration_response_seconds);
                    float accelerationChangeRate =
                        (vehicle.maximum_acceleration_mps2 +
                        vehicle.emergency_deceleration_mps2) /
                        responseSeconds;
                    float acceleration = Mathf.MoveTowards(
                        currentAccelerations[vehicleIndex],
                        targetAcceleration,
                        accelerationChangeRate * deltaTime);
                    proposedSpeeds[vehicleIndex] = Mathf.Max(
                        0f,
                        speed + acceleration * deltaTime);
                    proposedAdvances[vehicleIndex] =
                        proposedSpeeds[vehicleIndex] * deltaTime;
                }

                // Project proposed movement onto the collision-free cyclic lane.
                // Repeating at most the route population propagates a leader's
                // braking through a complete platoon without rigidbodies.
                for (int pass = 0; count > 1 && pass < count; pass++)
                {
                    bool changed = false;
                    for (int offset = 0; offset < count; offset++)
                    {
                        int vehicleIndex = routeVehicleIndices[start + offset];
                        int leaderIndex = routeVehicleIndices[
                            start + ((offset + 1) % count)];
                        DynamicRecord vehicle = vehicles[vehicleIndex];
                        DynamicRecord leader = vehicles[leaderIndex];
                        float centerDistance = Mathf.Repeat(
                            currentDistances[leaderIndex] -
                            currentDistances[vehicleIndex],
                            routes[routeIndex].length_meters);
                        float bumperGap = centerDistance -
                            0.5f * (
                                vehicle.vehicle_length_meters +
                                leader.vehicle_length_meters);
                        float maximumAdvance = Mathf.Max(
                            0f,
                            bumperGap -
                            vehicle.minimum_gap_meters +
                            proposedAdvances[leaderIndex]);
                        if (proposedAdvances[vehicleIndex] >
                            maximumAdvance + 0.0001f)
                        {
                            proposedAdvances[vehicleIndex] = maximumAdvance;
                            proposedSpeeds[vehicleIndex] =
                                maximumAdvance / deltaTime;
                            changed = true;
                        }
                    }
                    if (!changed)
                    {
                        break;
                    }
                }
            }

            for (int index = 0; index < vehicles.Length; index++)
            {
                previousDistances[index] = currentDistances[index];
                previousSpeeds[index] = currentSpeeds[index];
                RouteRecord route = routes[vehicles[index].route_index];
                currentDistances[index] = Mathf.Repeat(
                    currentDistances[index] + proposedAdvances[index],
                    route.length_meters);
                currentSpeeds[index] = proposedSpeeds[index];
                currentAccelerations[index] = Mathf.Clamp(
                    (currentSpeeds[index] - previousSpeeds[index]) / deltaTime,
                    -vehicles[index].emergency_deceleration_mps2,
                    vehicles[index].maximum_acceleration_mps2);
            }
        }

        public static float CalculateDesiredGap(
            float speed,
            float closingSpeed,
            float timeHeadwaySeconds,
            float minimumGapMeters,
            float maximumAccelerationMetersPerSecondSquared,
            float comfortableDecelerationMetersPerSecondSquared)
        {
            float denominator = 2f * Mathf.Sqrt(
                Mathf.Max(
                    0.001f,
                    maximumAccelerationMetersPerSecondSquared *
                    comfortableDecelerationMetersPerSecondSquared));
            float dynamicGap =
                Mathf.Max(0f, speed) * Mathf.Max(0f, timeHeadwaySeconds) +
                Mathf.Max(0f, speed) * closingSpeed / denominator;
            return Mathf.Max(0f, minimumGapMeters) +
                Mathf.Max(0f, dynamicGap);
        }

        public static float CalculateCarFollowingAcceleration(
            float speed,
            float leaderSpeed,
            float desiredSpeed,
            float bumperGap,
            float timeHeadwaySeconds,
            float minimumGapMeters,
            float maximumAccelerationMetersPerSecondSquared,
            float comfortableDecelerationMetersPerSecondSquared,
            float emergencyDecelerationMetersPerSecondSquared)
        {
            float safeDesiredSpeed = Mathf.Max(0.1f, desiredSpeed);
            float normalizedSpeed = Mathf.Max(0f, speed) / safeDesiredSpeed;
            float freeRoadTerm =
                normalizedSpeed * normalizedSpeed *
                normalizedSpeed * normalizedSpeed;
            float desiredGap = CalculateDesiredGap(
                speed,
                speed - leaderSpeed,
                timeHeadwaySeconds,
                minimumGapMeters,
                maximumAccelerationMetersPerSecondSquared,
                comfortableDecelerationMetersPerSecondSquared);
            float interaction = desiredGap /
                Mathf.Max(MinimumNumericalGapMeters, bumperGap);
            float acceleration =
                maximumAccelerationMetersPerSecondSquared *
                (1f - freeRoadTerm - interaction * interaction);
            return Mathf.Clamp(
                acceleration,
                -Mathf.Max(0f, emergencyDecelerationMetersPerSecondSquared),
                Mathf.Max(0f, maximumAccelerationMetersPerSecondSquared));
        }

        public static float CalculateSelectionScore(
            float distanceSquared,
            bool retainedFromPreviousFrame)
        {
            return Mathf.Max(0f, distanceSquared) *
                (retainedFromPreviousFrame ? 0.72f : 1f);
        }

        public static byte SelectLodTier(
            float distanceMeters,
            byte previousTier)
        {
            if (previousTier == 1)
            {
                if (distanceMeters <= NearExitDistanceMeters)
                {
                    return 1;
                }
                return distanceMeters <= FarExitDistanceMeters
                    ? (byte)2
                    : (byte)0;
            }
            if (previousTier == 2)
            {
                if (distanceMeters < NearEnterDistanceMeters)
                {
                    return 1;
                }
                return distanceMeters <= FarExitDistanceMeters
                    ? (byte)2
                    : (byte)0;
            }
            if (distanceMeters <= NearEnterDistanceMeters)
            {
                return 1;
            }
            return distanceMeters <= FarEnterDistanceMeters
                ? (byte)2
                : (byte)0;
        }

        private static float CalculateFreeAcceleration(
            float speed,
            float desiredSpeed,
            float maximumAccelerationMetersPerSecondSquared)
        {
            float normalized = Mathf.Max(0f, speed) /
                Mathf.Max(0.1f, desiredSpeed);
            float normalizedSquared = normalized * normalized;
            return Mathf.Clamp(
                maximumAccelerationMetersPerSecondSquared *
                (1f - normalizedSquared * normalizedSquared),
                -maximumAccelerationMetersPerSecondSquared,
                maximumAccelerationMetersPerSecondSquared);
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
            int largestFar = 0;
            if (farProxyMeshes != null)
            {
                for (int index = 0; index < farProxyMeshes.Length; index++)
                {
                    Mesh mesh = farProxyMeshes[index];
                    if (mesh != null && mesh.subMeshCount > 0)
                    {
                        largestFar = Mathf.Max(
                            largestFar,
                            (int)(mesh.GetIndexCount(0) / 3u));
                    }
                }
            }
            return largestNear * MaximumNearInstances +
                largestFar * MaximumFarInstances;
        }

        private bool AllFleetIndicesAreValid()
        {
            if (parsedManifest?.parked == null || parsedManifest.moving == null)
            {
                return false;
            }
            for (int index = 0; index < parsedManifest.parked.Length; index++)
            {
                ParkedRecord vehicle = parsedManifest.parked[index];
                if (vehicle.archetype < 0 ||
                    vehicle.archetype >= ArchetypeCount ||
                    vehicle.paint_index < 0 ||
                    vehicle.paint_index >= PaintVariantCount)
                {
                    return false;
                }
            }
            for (int index = 0; index < parsedManifest.moving.Length; index++)
            {
                DynamicRecord vehicle = parsedManifest.moving[index];
                if (vehicle.archetype < 0 ||
                    vehicle.archetype >= ArchetypeCount ||
                    vehicle.paint_index < 0 ||
                    vehicle.paint_index >= PaintVariantCount)
                {
                    return false;
                }
            }
            return true;
        }

        private bool AllParkedCellsAreValid()
        {
            if (parsedManifest?.parked == null ||
                parsedManifest.parked_cells == null)
            {
                return false;
            }
            int nextStart = 0;
            for (int index = 0;
                index < parsedManifest.parked_cells.Length;
                index++)
            {
                ParkedCell cell = parsedManifest.parked_cells[index];
                if (cell.start != nextStart ||
                    cell.count <= 0 ||
                    cell.start + cell.count > parsedManifest.parked.Length ||
                    cell.max_x <= cell.min_x ||
                    cell.max_z <= cell.min_z ||
                    Mathf.Abs(
                        cell.min_x -
                        cell.cell_x * ParkedCellSizeMeters) > 0.01f ||
                    Mathf.Abs(
                        cell.min_z -
                        cell.cell_z * ParkedCellSizeMeters) > 0.01f)
                {
                    return false;
                }
                nextStart += cell.count;
            }
            return nextStart == parsedManifest.parked.Length;
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
            if (lastPreparedFrame != Time.frameCount || lastPreparedCamera != camera)
            {
                PrepareVisibleInstances(camera);
                lastPreparedFrame = Time.frameCount;
                lastPreparedCamera = camera;
            }

            int layer = gameObject.layer;
            for (int archetype = 0; archetype < ArchetypeCount; archetype++)
            {
                int count = nearCounts[archetype];
                if (count <= 0)
                {
                    continue;
                }
                MaterialPropertyBlock propertyBlock =
                    nearPropertyBlocks[archetype] ??
                    (nearPropertyBlocks[archetype] = new MaterialPropertyBlock());
                propertyBlock.Clear();
                propertyBlock.SetVectorArray(
                    "_BodyTint",
                    nearPaintTints[archetype]);
                propertyBlock.SetVectorArray(
                    "_TrailerTint",
                    nearTrailerTints[archetype]);
                Graphics.DrawMeshInstanced(
                    nearArchetypeMeshes[archetype],
                    0,
                    sharedOpaqueMaterial,
                    nearMatrices[archetype],
                    count,
                    propertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    layer,
                    camera,
                    LightProbeUsage.Off,
                    null);
            }
            for (int archetype = 0;
                archetype < ProxyArchetypeCount;
                archetype++)
            {
                int count = farCounts[archetype];
                if (count <= 0)
                {
                    continue;
                }
                MaterialPropertyBlock propertyBlock =
                    farPropertyBlocks[archetype] ??
                    (farPropertyBlocks[archetype] =
                        new MaterialPropertyBlock());
                propertyBlock.Clear();
                propertyBlock.SetVectorArray(
                    "_BodyTint",
                    farPaintTints[archetype]);
                propertyBlock.SetVectorArray(
                    "_TrailerTint",
                    farTrailerTints[archetype]);
                Graphics.DrawMeshInstanced(
                    farProxyMeshes[archetype],
                    0,
                    sharedOpaqueMaterial,
                    farMatrices[archetype],
                    count,
                    propertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    layer,
                    camera,
                    LightProbeUsage.Off,
                    null);
            }
            if (!submissionLogged && Application.isPlaying)
            {
                submissionLogged = true;
                int nearTotal = 0;
                for (int index = 0; index < nearCounts.Length; index++)
                {
                    nearTotal += nearCounts[index];
                }
                int farTotal = 0;
                for (int index = 0; index < farCounts.Length; index++)
                {
                    farTotal += farCounts[index];
                }
                Debug.Log(
                    $"[QuestFlight][Traffic] Submitted moving traffic for camera '{camera.name}': " +
                    $"near={nearTotal}, far={farTotal}, " +
                    $"visible={nearTotal + farTotal}, stereo={camera.stereoEnabled}; " +
                    $"parked records={authoredParkedCount}.",
                    this);
            }
        }

        private void PrepareVisibleInstances(Camera camera)
        {
            Array.Clear(nearCounts, 0, nearCounts.Length);
            Array.Clear(farCounts, 0, farCounts.Length);
            selectedCount = 0;
            selectedFarCount = 0;

            ParkedRecord[] parked = parsedManifest.parked;
            ParkedCell[] parkedCells = parsedManifest.parked_cells;
            Vector3 cameraPosition = camera.transform.position;
            float farCellDistanceSquared =
                FarExitDistanceMeters * FarExitDistanceMeters;
            for (int cellIndex = 0;
                cellIndex < parkedCells.Length;
                cellIndex++)
            {
                ParkedCell cell = parkedCells[cellIndex];
                float closestX = Mathf.Clamp(
                    cameraPosition.x,
                    cell.min_x,
                    cell.max_x);
                float closestZ = Mathf.Clamp(
                    cameraPosition.z,
                    cell.min_z,
                    cell.max_z);
                float deltaX = cameraPosition.x - closestX;
                float deltaZ = cameraPosition.z - closestZ;
                if (deltaX * deltaX + deltaZ * deltaZ >
                    farCellDistanceSquared)
                {
                    continue;
                }
                int end = cell.start + cell.count;
                for (int index = cell.start; index < end; index++)
                {
                    ParkedRecord vehicle = parked[index];
                    Vector3 position =
                        new Vector3(vehicle.x, vehicle.y, vehicle.z);
                    Matrix4x4 matrix = Matrix4x4.TRS(
                        position,
                        Quaternion.Euler(0f, vehicle.yaw_degrees, 0f),
                        Vector3.one);
                    ConsiderVisible(
                        camera,
                        position,
                        vehicle.archetype,
                        vehicle.paint_index,
                        matrix,
                        index);
                }
            }

            DynamicRecord[] dynamicVehicles = parsedManifest.moving;
            RouteRecord[] routes = parsedManifest.routes;
            float interpolation = simulationInitialized
                ? Mathf.Clamp01(
                    simulationAccumulator / SimulationStepSeconds)
                : 0f;
            for (int index = 0; index < dynamicVehicles.Length; index++)
            {
                DynamicRecord vehicle = dynamicVehicles[index];
                RouteRecord route = routes[vehicle.route_index];
                float distance = vehicle.initial_distance_meters;
                if (simulationInitialized)
                {
                    float forwardDelta = Mathf.Repeat(
                        currentDistances[index] - previousDistances[index],
                        route.length_meters);
                    distance = Mathf.Repeat(
                        previousDistances[index] +
                        forwardDelta * interpolation,
                        route.length_meters);
                }
                EvaluateRoute(route, distance, out Vector3 position, out Quaternion rotation);
                Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
                ConsiderVisible(
                    camera,
                    position,
                    vehicle.archetype,
                    vehicle.paint_index,
                    matrix,
                    parked.Length + index);
            }

            for (int index = 0; index < selectedCount; index++)
            {
                int archetype = selectedArchetypes[index];
                int count = nearCounts[archetype];
                if (count >= nearMatrices[archetype].Length)
                {
                    continue;
                }
                nearMatrices[archetype][count] = selectedMatrices[index];
                nearPaintTints[archetype][count] =
                    PaintPalette[selectedPaintIndices[index]];
                nearTrailerTints[archetype][count] =
                    TrailerPalette[selectedPaintIndices[index]];
                nearCounts[archetype] = count + 1;
            }

            for (int index = 0; index < selectedFarCount; index++)
            {
                int archetype = selectedFarArchetypes[index];
                int count = farCounts[archetype];
                if (count >= farMatrices[archetype].Length)
                {
                    continue;
                }
                farMatrices[archetype][count] = selectedFarMatrices[index];
                farPaintTints[archetype][count] =
                    PaintPalette[selectedFarPaintIndices[index]];
                farTrailerTints[archetype][count] =
                    TrailerPalette[selectedFarPaintIndices[index]];
                farCounts[archetype] = count + 1;
            }

            Array.Clear(previousLodTier, 0, previousLodTier.Length);
            for (int index = 0; index < selectedCount; index++)
            {
                int vehicleId = selectedVehicleIds[index];
                if (vehicleId >= 0 && vehicleId < previousLodTier.Length)
                {
                    previousLodTier[vehicleId] = 1;
                }
            }
            for (int index = 0; index < selectedFarCount; index++)
            {
                int vehicleId = selectedFarVehicleIds[index];
                if (vehicleId >= 0 && vehicleId < previousLodTier.Length)
                {
                    previousLodTier[vehicleId] = 2;
                }
            }
        }

        private void ConsiderVisible(
            Camera camera,
            Vector3 position,
            int archetype,
            int paintIndex,
            Matrix4x4 matrix,
            int vehicleId)
        {
            if (archetype < 0 ||
                archetype >= ArchetypeCount ||
                paintIndex < 0 ||
                paintIndex >= PaintVariantCount)
            {
                return;
            }
            Vector3 toVehicle = position - camera.transform.position;
            float distanceSquared = toVehicle.sqrMagnitude;
            float distance = Mathf.Sqrt(distanceSquared);
            byte previousTier =
                vehicleId >= 0 && vehicleId < previousLodTier.Length
                    ? previousLodTier[vehicleId]
                    : (byte)0;
            byte desiredTier = SelectLodTier(distance, previousTier);
            if (desiredTier == 0)
            {
                return;
            }

            // Vehicles that lose a detailed slot fall back to a cheap proxy
            // instead of disappearing. World-distance selection means head
            // turns cannot churn either eye's membership.
            if (desiredTier == 1 &&
                TryInsertNear(
                    distanceSquared,
                    previousTier == 1,
                    archetype,
                    paintIndex,
                    matrix,
                    vehicleId))
            {
                return;
            }
            TryInsertFar(
                distanceSquared,
                previousTier == 2,
                archetype == ArchetypeCount - 1
                    ? 2
                    : Mathf.Abs(vehicleId) % 2,
                paintIndex,
                matrix,
                vehicleId);
        }

        private bool TryInsertNear(
            float distanceSquared,
            bool retained,
            int archetype,
            int paintIndex,
            Matrix4x4 matrix,
            int vehicleId)
        {
            float score = CalculateSelectionScore(distanceSquared, retained);
            if (selectedCount < MaximumNearInstances)
            {
                int insertion = selectedCount++;
                SetNearSelection(
                    insertion,
                    score,
                    archetype,
                    paintIndex,
                    matrix,
                    vehicleId);
                SiftNearUp(insertion);
                return true;
            }
            if (score >= selectedScores[0])
            {
                return false;
            }
            SetNearSelection(
                0,
                score,
                archetype,
                paintIndex,
                matrix,
                vehicleId);
            SiftNearDown(0);
            return true;
        }

        private bool TryInsertFar(
            float distanceSquared,
            bool retained,
            int archetype,
            int paintIndex,
            Matrix4x4 matrix,
            int vehicleId)
        {
            float score = CalculateSelectionScore(distanceSquared, retained);
            if (selectedFarCount < MaximumFarInstances)
            {
                int insertion = selectedFarCount++;
                SetFarSelection(
                    insertion,
                    score,
                    archetype,
                    paintIndex,
                    matrix,
                    vehicleId);
                SiftFarUp(insertion);
                return true;
            }
            if (score >= selectedFarScores[0])
            {
                return false;
            }
            SetFarSelection(
                0,
                score,
                archetype,
                paintIndex,
                matrix,
                vehicleId);
            SiftFarDown(0);
            return true;
        }

        private void SetNearSelection(
            int index,
            float score,
            int archetype,
            int paintIndex,
            Matrix4x4 matrix,
            int vehicleId)
        {
            selectedScores[index] = score;
            selectedMatrices[index] = matrix;
            selectedArchetypes[index] = archetype;
            selectedPaintIndices[index] = paintIndex;
            selectedVehicleIds[index] = vehicleId;
        }

        private void SiftNearUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (selectedScores[parent] >= selectedScores[index])
                {
                    return;
                }
                SwapNear(parent, index);
                index = parent;
            }
        }

        private void SiftNearDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= selectedCount)
                {
                    return;
                }
                int right = left + 1;
                int largest =
                    right < selectedCount &&
                    selectedScores[right] > selectedScores[left]
                        ? right
                        : left;
                if (selectedScores[index] >= selectedScores[largest])
                {
                    return;
                }
                SwapNear(index, largest);
                index = largest;
            }
        }

        private void SwapNear(int first, int second)
        {
            (selectedScores[first], selectedScores[second]) =
                (selectedScores[second], selectedScores[first]);
            (selectedMatrices[first], selectedMatrices[second]) =
                (selectedMatrices[second], selectedMatrices[first]);
            (selectedArchetypes[first], selectedArchetypes[second]) =
                (selectedArchetypes[second], selectedArchetypes[first]);
            (selectedPaintIndices[first], selectedPaintIndices[second]) =
                (selectedPaintIndices[second], selectedPaintIndices[first]);
            (selectedVehicleIds[first], selectedVehicleIds[second]) =
                (selectedVehicleIds[second], selectedVehicleIds[first]);
        }

        private void SetFarSelection(
            int index,
            float score,
            int archetype,
            int paintIndex,
            Matrix4x4 matrix,
            int vehicleId)
        {
            selectedFarScores[index] = score;
            selectedFarMatrices[index] = matrix;
            selectedFarArchetypes[index] = archetype;
            selectedFarPaintIndices[index] = paintIndex;
            selectedFarVehicleIds[index] = vehicleId;
        }

        private void SiftFarUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (selectedFarScores[parent] >= selectedFarScores[index])
                {
                    return;
                }
                SwapFar(parent, index);
                index = parent;
            }
        }

        private void SiftFarDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= selectedFarCount)
                {
                    return;
                }
                int right = left + 1;
                int largest =
                    right < selectedFarCount &&
                    selectedFarScores[right] > selectedFarScores[left]
                        ? right
                        : left;
                if (selectedFarScores[index] >= selectedFarScores[largest])
                {
                    return;
                }
                SwapFar(index, largest);
                index = largest;
            }
        }

        private void SwapFar(int first, int second)
        {
            (selectedFarScores[first], selectedFarScores[second]) =
                (selectedFarScores[second], selectedFarScores[first]);
            (selectedFarMatrices[first], selectedFarMatrices[second]) =
                (selectedFarMatrices[second], selectedFarMatrices[first]);
            (selectedFarArchetypes[first], selectedFarArchetypes[second]) =
                (selectedFarArchetypes[second], selectedFarArchetypes[first]);
            (selectedFarPaintIndices[first], selectedFarPaintIndices[second]) =
                (selectedFarPaintIndices[second], selectedFarPaintIndices[first]);
            (selectedFarVehicleIds[first], selectedFarVehicleIds[second]) =
                (selectedFarVehicleIds[second], selectedFarVehicleIds[first]);
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
