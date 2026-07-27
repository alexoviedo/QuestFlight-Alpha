using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Allocation-free, road-constrained traffic presentation. Drivers use a fixed-step,
    /// deterministic, IDM-inspired longitudinal controller so following distance responds
    /// to speed and leaders instead of oscillating around a fixed spacing lattice.
    /// Rendering remains limited to five opaque instanced draws.  Surface classes
    /// are vertex encoded and paint is supplied per instance, so fleet variety
    /// does not create extra materials or GameObjects.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class KbduTrafficRuntime : MonoBehaviour
    {
        public const int ArchetypeCount = 5;
        public const int MaximumDrawCalls = 5;
        public const int MaximumNearInstances = 128;
        public const int PaintVariantCount = 8;
        public const int MaximumVisibleTriangles = 130000;
        public const float MaximumDistanceMeters = 2500f;
        public const float SimulationStepSeconds = 0.1f;
        private const float MinimumNumericalGapMeters = 0.05f;
        private const float MaximumFrameCatchupSeconds = 0.5f;

        [Serializable]
        private sealed class Manifest
        {
            public string schema;
            public string status;
            public ParkedRecord[] parked;
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
            new Matrix4x4[MaximumNearInstances]
        };
        private readonly Vector4[][] nearPaintTints =
        {
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances],
            new Vector4[MaximumNearInstances]
        };
        private readonly int[] nearCounts = new int[ArchetypeCount];
        private readonly Matrix4x4[] selectedMatrices =
            new Matrix4x4[MaximumNearInstances];
        private readonly int[] selectedArchetypes = new int[MaximumNearInstances];
        private readonly int[] selectedPaintIndices = new int[MaximumNearInstances];
        private readonly float[] selectedScores = new float[MaximumNearInstances];
        private readonly MaterialPropertyBlock[] nearPropertyBlocks =
            new MaterialPropertyBlock[ArchetypeCount];
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
        private bool submissionLogged;

        public TextAsset TrafficManifest => trafficManifest;
        public Material SharedOpaqueMaterial => sharedOpaqueMaterial;
        public Mesh[] NearArchetypeMeshes => nearArchetypeMeshes;
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
            Mesh[] nearMeshes)
        {
            trafficManifest = manifest;
            sharedOpaqueMaterial = material;
            nearArchetypeMeshes = nearMeshes ?? Array.Empty<Mesh>();
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
                nearArchetypeMeshes.Length != ArchetypeCount)
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
                !string.Equals(parsedManifest.status, "STREAMED_MAJOR_ROAD_NETWORK", StringComparison.Ordinal) ||
                parsedManifest.parked == null ||
                parsedManifest.parked.Length != 40 ||
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
                parsedManifest.runtime_contract.minimum_runtime_bumper_gap_meters < 1.5f ||
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
            return largestNear * MaximumNearInstances;
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
            PrepareVisibleInstances(camera);

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
            if (!submissionLogged && Application.isPlaying)
            {
                submissionLogged = true;
                int nearTotal = 0;
                for (int index = 0; index < nearCounts.Length; index++)
                {
                    nearTotal += nearCounts[index];
                }
                Debug.Log(
                    $"[QuestFlight][Traffic] Submitted moving traffic for camera '{camera.name}': " +
                    $"visible={nearTotal}, stereo={camera.stereoEnabled}; " +
                    $"parked records={authoredParkedCount}.",
                    this);
            }
        }

        private void PrepareVisibleInstances(Camera camera)
        {
            Array.Clear(nearCounts, 0, nearCounts.Length);
            selectedCount = 0;

            ParkedRecord[] parked = parsedManifest.parked;
            for (int index = 0; index < parked.Length; index++)
            {
                ParkedRecord vehicle = parked[index];
                Vector3 position = new Vector3(vehicle.x, vehicle.y, vehicle.z);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    position,
                    Quaternion.Euler(0f, vehicle.yaw_degrees, 0f),
                    Vector3.one);
                ConsiderVisible(
                    camera,
                    position,
                    vehicle.archetype,
                    vehicle.paint_index,
                    matrix);
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
                    matrix);
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
                nearCounts[archetype] = count + 1;
            }
        }

        private void ConsiderVisible(
            Camera camera,
            Vector3 position,
            int archetype,
            int paintIndex,
            Matrix4x4 matrix)
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
            if (distanceSquared > MaximumDistanceMeters * MaximumDistanceMeters)
            {
                return;
            }
            float distance = Mathf.Sqrt(Mathf.Max(0.001f, distanceSquared));
            float facing = Vector3.Dot(camera.transform.forward, toVehicle) / distance;
            // The cutoff is well behind the edge of a typical Quest field of
            // view, so cars are selected before either eye can see them.
            if (distance > 250f && facing < -0.35f)
            {
                return;
            }

            float viewPenalty = Mathf.Lerp(
                1.35f,
                1f,
                Mathf.Clamp01((facing + 0.35f) / 1.35f));
            float score = distanceSquared * viewPenalty;
            int insertion;
            if (selectedCount < MaximumNearInstances)
            {
                insertion = selectedCount;
                selectedCount++;
            }
            else
            {
                insertion = selectedCount - 1;
                if (score >= selectedScores[insertion])
                {
                    return;
                }
            }

            while (insertion > 0 && score < selectedScores[insertion - 1])
            {
                selectedScores[insertion] = selectedScores[insertion - 1];
                selectedMatrices[insertion] = selectedMatrices[insertion - 1];
                selectedArchetypes[insertion] = selectedArchetypes[insertion - 1];
                selectedPaintIndices[insertion] =
                    selectedPaintIndices[insertion - 1];
                insertion--;
            }
            selectedScores[insertion] = score;
            selectedMatrices[insertion] = matrix;
            selectedArchetypes[insertion] = archetype;
            selectedPaintIndices[insertion] = paintIndex;
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
