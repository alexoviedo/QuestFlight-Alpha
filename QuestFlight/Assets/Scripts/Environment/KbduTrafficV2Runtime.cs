using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Authored Traffic V2 full-map moving-vehicle runtime. The serialized
    /// TrafficRoot owns this component; it creates no hierarchy and never writes
    /// the OpenXR camera. A compact frozen corridor manifest supplies the complete
    /// +/-12 km source universe, while a tile index bounds per-camera work.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KbduTrafficRuntime))]
    public sealed class KbduTrafficV2Runtime : MonoBehaviour
    {
        public const string ExpectedSchema =
            "questflight.traffic-v2.full-map-corridor-schedule.v1";
        public const string ExpectedStatus = "FULL_MAP_HEADSET_CANDIDATE";
        // Filled after the deterministic full-map compile; a placeholder fails closed.
        public const string ExpectedManifestSha256 =
            "e45e9fb4284780a643fc54f20a7da1ff25754174376bb6cb24cef9e3c4205209";
        public const string ExpectedProvenanceSha256 =
            "6023d38c8f1b5903777e34e2d065ba46b19cffa628694d0c0a6197258ebd6af1";
        public const string ExpectedSourceFreezeSha256 =
            "6f643c3d44d115c64b9b2c9b2bbf84cb331979fdc7598ed11b0d2e34377db097";
        public const string ExpectedAcceptedSourceSha =
            "fda11a25eb526824d82a47330f2be80b1410bb64";
        public const int DrawMeshInstancedLimit = 1023;
        public const int MaximumInstancesPerGroup = 2048;
        public const int MaximumDrawCalls = 18;
        public const float NearDistanceMeters = 1000f;
        public const float FarDistanceMeters = 4500f;
        public const float RoadSurfaceOffsetMeters = 0.04f;
        private const int ExpectedTileAxisCount = 24;
        private const float ExpectedTileSizeMeters = 1000f;
        private const float BoundsMinimumMeters = -12000f;
        private const int MaximumCorridorPoints = 256;

        [Serializable]
        private sealed class Manifest
        {
            public string accepted_source_sha;
            public float[] bounds_xz_meters;
            public CanonicalUniverse canonical_universe;
            public string clock_contract;
            public Corridor[] corridors;
            public string[] limitations;
            public Metrics metrics;
            public string schema;
            public string source_freeze_sha256;
            public string source_gate_status;
            public string status;
            public float tile_size_meters;
            public Tile[] tiles;
        }

        [Serializable]
        private sealed class CanonicalUniverse
        {
            public int bytes;
            public string path;
            public string sha256;
        }

        [Serializable]
        private sealed class Metrics
        {
            public int canonical_input_record_count;
            public int corridor_count;
            public int direct_count_corridor_count;
            public float estimated_full_map_active_vehicle_count;
            public int explicit_or_unresolved_record_count;
            public int source_count_observation_count;
            public int tile_count;
            public float total_corridor_kilometers;
        }

        [Serializable]
        private sealed class Corridor
        {
            public string canonical_segment_id;
            public string corridor_id;
            public string direction_mode;
            public float headway_seconds;
            public int lane_count;
            public float length_meters;
            public int part_index;
            public float phase_seconds;
            public float[] points_xz_meters;
            public string road_class;
            public string source_id;
            public string source_record_id;
            public int source_record_part;
            public float speed_mps;
            public int[] tile_bounds;

            [NonSerialized] public float[] cumulativeMeters;
        }

        [Serializable]
        private sealed class Tile
        {
            public int[] corridor_indices;
            public int tile_x;
            public int tile_z;
        }

        [SerializeField] private TextAsset trajectoryManifest;
        [SerializeField] private TextAsset sourceAndLicenseRecord;
        [SerializeField] private KbduTrafficRuntime acceptedVehicleAssets;
        [SerializeField] private ProductionTerrainSurfaceAsset terrainSurface;
        [SerializeField] private bool deterministicGlobalClock = true;
        [SerializeField] private bool perVehicleGameObjects;
        [SerializeField] private bool realtimeVehicleShadows;
        [SerializeField] private int authoredTrajectoryCount;
        [SerializeField] private int authoredMaximumDrawCount;

        private readonly Matrix4x4[][] nearMatrices = CreateMatrixGroups();
        private readonly Vector4[][] nearBodyTints = CreateVectorGroups();
        private readonly Vector4[][] nearTrailerTints = CreateVectorGroups();
        private readonly int[] nearCounts = new int[KbduTrafficRuntime.ArchetypeCount];
        private readonly Matrix4x4[][] farMatrices = CreateProxyMatrixGroups();
        private readonly Vector4[][] farBodyTints = CreateProxyVectorGroups();
        private readonly Vector4[][] farTrailerTints = CreateProxyVectorGroups();
        private readonly int[] farCounts = new int[KbduTrafficRuntime.ProxyArchetypeCount];
        private readonly MaterialPropertyBlock[] nearPropertyBlocks =
            new MaterialPropertyBlock[KbduTrafficRuntime.ArchetypeCount];
        private readonly MaterialPropertyBlock[] farPropertyBlocks =
            new MaterialPropertyBlock[KbduTrafficRuntime.ProxyArchetypeCount];
        private readonly Matrix4x4[] chunkMatrices = new Matrix4x4[DrawMeshInstancedLimit];
        private readonly Vector4[] chunkBodyTints = new Vector4[DrawMeshInstancedLimit];
        private readonly Vector4[] chunkTrailerTints = new Vector4[DrawMeshInstancedLimit];
        private readonly Dictionary<int, Tile> tileByKey = new Dictionary<int, Tile>();

        private static readonly Vector4[] PaintPalette =
        {
            new Vector4(0.78f, 0.79f, 0.76f, 1f),
            new Vector4(0.025f, 0.027f, 0.030f, 1f),
            new Vector4(0.26f, 0.28f, 0.30f, 1f),
            new Vector4(0.54f, 0.56f, 0.57f, 1f),
            new Vector4(0.055f, 0.15f, 0.31f, 1f),
            new Vector4(0.38f, 0.045f, 0.035f, 1f),
            new Vector4(0.055f, 0.20f, 0.11f, 1f),
            new Vector4(0.18f, 0.10f, 0.055f, 1f)
        };

        private static readonly Vector4[] TrailerPalette =
        {
            new Vector4(0.78f, 0.79f, 0.77f, 1f),
            new Vector4(0.58f, 0.60f, 0.61f, 1f),
            new Vector4(0.70f, 0.71f, 0.70f, 1f),
            new Vector4(0.48f, 0.50f, 0.51f, 1f),
            new Vector4(0.76f, 0.77f, 0.75f, 1f),
            new Vector4(0.62f, 0.64f, 0.64f, 1f),
            new Vector4(0.72f, 0.73f, 0.71f, 1f),
            new Vector4(0.52f, 0.53f, 0.52f, 1f)
        };

        private Manifest parsedManifest;
        private int[] corridorVisitGeneration;
        private int visitGeneration;
        private float scheduleClockSeconds;
        private bool contractValid;
        private int lastPreparedFrame = -1;
        private Camera lastPreparedCamera;
        private bool submissionLogged;
        private int droppedInstanceCount;

        public TextAsset TrajectoryManifest => trajectoryManifest;
        public TextAsset SourceAndLicenseRecord => sourceAndLicenseRecord;
        public KbduTrafficRuntime AcceptedVehicleAssets => acceptedVehicleAssets;
        public ProductionTerrainSurfaceAsset TerrainSurface => terrainSurface;
        public bool DeterministicGlobalClock => deterministicGlobalClock;
        public bool PerVehicleGameObjects => perVehicleGameObjects;
        public bool RealtimeVehicleShadows => realtimeVehicleShadows;
        public int AuthoredTrajectoryCount => authoredTrajectoryCount;
        public int AuthoredCorridorCount => authoredTrajectoryCount;
        public int AuthoredMaximumDrawCount => authoredMaximumDrawCount;
        public float ScheduleClockSeconds => scheduleClockSeconds;
        public int DroppedInstanceCount => droppedInstanceCount;
        public int PreparedNearInstanceCount => SumCounts(nearCounts);
        public int PreparedFarInstanceCount => SumCounts(farCounts);

        public void Configure(
            TextAsset manifest,
            TextAsset provenance,
            KbduTrafficRuntime vehicleAssets,
            ProductionTerrainSurfaceAsset authoredTerrain)
        {
            trajectoryManifest = manifest;
            sourceAndLicenseRecord = provenance;
            acceptedVehicleAssets = vehicleAssets;
            terrainSurface = authoredTerrain;
            deterministicGlobalClock = true;
            perVehicleGameObjects = false;
            realtimeVehicleShadows = false;
            parsedManifest = null;
            ParseManifest();
            authoredTrajectoryCount = parsedManifest?.corridors?.Length ?? 0;
            authoredMaximumDrawCount = MaximumDrawCalls;
            contractValid = TryValidateContract(out _);
        }

        public bool TryValidateContract(out string report)
        {
            contractValid = false;
            report = null;
            if (trajectoryManifest == null || sourceAndLicenseRecord == null ||
                acceptedVehicleAssets == null || terrainSurface == null)
            {
                report = "missing authored Traffic V2 binding";
                return false;
            }
            if (acceptedVehicleAssets.MovingTrafficEnabled ||
                acceptedVehicleAssets.SharedOpaqueMaterial == null ||
                !acceptedVehicleAssets.SharedOpaqueMaterial.enableInstancing ||
                acceptedVehicleAssets.NearArchetypeMeshes == null ||
                acceptedVehicleAssets.NearArchetypeMeshes.Length != KbduTrafficRuntime.ArchetypeCount ||
                acceptedVehicleAssets.FarProxyMeshes == null ||
                acceptedVehicleAssets.FarProxyMeshes.Length != KbduTrafficRuntime.ProxyArchetypeCount ||
                perVehicleGameObjects || realtimeVehicleShadows || !deterministicGlobalClock)
            {
                report = "Traffic V1/V2 presentation ownership or instancing policy mismatch";
                return false;
            }
            if (!string.Equals(ComputeSha256(trajectoryManifest.bytes),
                    ExpectedManifestSha256, StringComparison.Ordinal))
            {
                report = "Traffic V2 full-map manifest hash mismatch";
                return false;
            }
            if (!string.Equals(ComputeSha256(sourceAndLicenseRecord.bytes),
                    ExpectedProvenanceSha256, StringComparison.Ordinal))
            {
                report = "Traffic V2 source-and-license record hash mismatch";
                return false;
            }
            ParseManifest();
            if (parsedManifest == null ||
                !string.Equals(parsedManifest.schema, ExpectedSchema, StringComparison.Ordinal) ||
                !string.Equals(parsedManifest.status, ExpectedStatus, StringComparison.Ordinal) ||
                !string.Equals(parsedManifest.accepted_source_sha,
                    ExpectedAcceptedSourceSha, StringComparison.Ordinal) ||
                !string.Equals(parsedManifest.source_freeze_sha256,
                    ExpectedSourceFreezeSha256, StringComparison.Ordinal) ||
                !string.Equals(parsedManifest.clock_contract,
                    "CONTINUOUS_DETERMINISTIC_DEPARTURE_STREAMS", StringComparison.Ordinal) ||
                (!string.Equals(parsedManifest.source_gate_status,
                     "PENDING_LEAD_D_INSPECTION", StringComparison.Ordinal) &&
                 !string.Equals(parsedManifest.source_gate_status,
                     "PASS_PHASE3_SOURCE_GATE", StringComparison.Ordinal) &&
                  !string.Equals(parsedManifest.source_gate_status,
                     "PENDING_CANONICAL_SOURCE_GATE", StringComparison.Ordinal) &&
                  !string.Equals(parsedManifest.source_gate_status,
                     "PENDING_PHASE3_SOURCE_GATE", StringComparison.Ordinal)) ||
                parsedManifest.corridors == null || parsedManifest.corridors.Length == 0 ||
                parsedManifest.tiles == null || parsedManifest.tiles.Length == 0 ||
                parsedManifest.metrics == null ||
                parsedManifest.metrics.corridor_count != parsedManifest.corridors.Length ||
                authoredTrajectoryCount != parsedManifest.corridors.Length ||
                authoredMaximumDrawCount != MaximumDrawCalls ||
                !AllCorridorsAreValid(out report))
            {
                if (string.IsNullOrEmpty(report))
                {
                    report = "Traffic V2 full-map corridor contract mismatch";
                }
                return false;
            }
            contractValid = true;
            report =
                $"corridors={parsedManifest.corridors.Length}, " +
                $"coverage={parsedManifest.metrics.total_corridor_kilometers:F1}km, " +
                $"draws<={MaximumDrawCalls}, groupInstances<={MaximumInstancesPerGroup}";
            return true;
        }

        public bool TryEvaluateCorridor(
            int corridorIndex,
            int direction,
            float scheduleSeconds,
            out Vector3 position,
            out Quaternion rotation)
        {
            ParseManifest();
            if (parsedManifest?.corridors == null || corridorIndex < 0 ||
                corridorIndex >= parsedManifest.corridors.Length || direction < 0 || direction > 1)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }
            Corridor corridor = parsedManifest.corridors[corridorIndex];
            if (!DirectionIsEnabled(corridor, direction))
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }
            double age = PositiveModulo(scheduleSeconds - corridor.phase_seconds,
                corridor.headway_seconds);
            if (age * corridor.speed_mps >= corridor.length_meters)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }
            return TryEvaluateDistance(corridor, direction,
                (float)(age * corridor.speed_mps), 0, out position, out rotation);
        }

        // Retained for the existing diagnostic runner. The product manifest now
        // indexes corridors rather than the rejected Gate-1 trip list.
        public bool TryEvaluateTrajectory(
            int corridorIndex,
            float scheduleSeconds,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (TryEvaluateCorridor(
                    corridorIndex, 0, scheduleSeconds, out position, out rotation))
            {
                return true;
            }
            return TryEvaluateCorridor(
                corridorIndex, 1, scheduleSeconds, out position, out rotation);
        }

        private void Awake()
        {
            if (!Application.isPlaying && trajectoryManifest == null)
            {
                return;
            }
            if (!TryValidateContract(out string report))
            {
                Debug.LogError("[QuestFlight][TrafficV2] Authored contract failed: " + report, this);
                return;
            }
            Debug.Log("[QuestFlight][TrafficV2] Authored contract ready: " + report, this);
        }

        private void OnEnable()
        {
            scheduleClockSeconds = 0f;
            contractValid = TryValidateContract(out _);
            Camera.onPreCull -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
            lastPreparedFrame = -1;
            lastPreparedCamera = null;
        }

        private void Update()
        {
            if (Application.isPlaying && contractValid)
            {
                scheduleClockSeconds += Mathf.Max(0f, Time.unscaledDeltaTime);
            }
        }

        private void ParseManifest()
        {
            if (parsedManifest != null || trajectoryManifest == null)
            {
                return;
            }
            parsedManifest = JsonUtility.FromJson<Manifest>(trajectoryManifest.text);
            tileByKey.Clear();
            if (parsedManifest?.corridors == null || parsedManifest.tiles == null)
            {
                return;
            }
            corridorVisitGeneration = new int[parsedManifest.corridors.Length];
            for (int index = 0; index < parsedManifest.corridors.Length; index++)
            {
                Corridor corridor = parsedManifest.corridors[index];
                if (corridor == null)
                {
                    continue;
                }
                int pointCount = corridor?.points_xz_meters?.Length / 2 ?? 0;
                corridor.cumulativeMeters = new float[pointCount];
                for (int point = 1; point < pointCount; point++)
                {
                    float deltaX = corridor.points_xz_meters[point * 2] -
                        corridor.points_xz_meters[(point - 1) * 2];
                    float deltaZ = corridor.points_xz_meters[point * 2 + 1] -
                        corridor.points_xz_meters[(point - 1) * 2 + 1];
                    corridor.cumulativeMeters[point] =
                        corridor.cumulativeMeters[point - 1] +
                        Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
                }
            }
            foreach (Tile tile in parsedManifest.tiles)
            {
                if (tile != null)
                {
                    tileByKey[TileKey(tile.tile_x, tile.tile_z)] = tile;
                }
            }
        }

        private bool AllCorridorsAreValid(out string report)
        {
            if (parsedManifest.bounds_xz_meters == null ||
                parsedManifest.bounds_xz_meters.Length != 4 ||
                Mathf.Abs(parsedManifest.bounds_xz_meters[0] + 12000f) > 0.01f ||
                Mathf.Abs(parsedManifest.bounds_xz_meters[1] + 12000f) > 0.01f ||
                Mathf.Abs(parsedManifest.bounds_xz_meters[2] - 12000f) > 0.01f ||
                Mathf.Abs(parsedManifest.bounds_xz_meters[3] - 12000f) > 0.01f ||
                Mathf.Abs(parsedManifest.tile_size_meters - ExpectedTileSizeMeters) > 0.01f ||
                parsedManifest.canonical_universe == null ||
                parsedManifest.canonical_universe.bytes <= 0 ||
                string.IsNullOrWhiteSpace(parsedManifest.canonical_universe.sha256) ||
                (!string.Equals(parsedManifest.canonical_universe.path,
                     "intermediates/canonical_road_universe.geojsonl", StringComparison.Ordinal) &&
                 !string.Equals(parsedManifest.canonical_universe.path,
                      "candidate_input/osm_bounded_roads.normalized.geojsonl",
                      StringComparison.Ordinal) &&
                  !string.Equals(parsedManifest.canonical_universe.path,
                      "candidate_input/canonical_road_universe.geojsonl",
                      StringComparison.Ordinal)))
            {
                report = "full-map bounds or tile contract mismatch";
                return false;
            }
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < parsedManifest.corridors.Length; index++)
            {
                Corridor corridor = parsedManifest.corridors[index];
                int pointCount = corridor?.points_xz_meters?.Length / 2 ?? 0;
                if (corridor == null || string.IsNullOrWhiteSpace(corridor.corridor_id) ||
                    !ids.Add(corridor.corridor_id) || corridor.lane_count <= 0 ||
                    corridor.length_meters < 12f || corridor.speed_mps <= 0f ||
                    corridor.headway_seconds <= 0f || corridor.phase_seconds < 0f ||
                    corridor.phase_seconds >= corridor.headway_seconds ||
                    pointCount < 2 || pointCount > MaximumCorridorPoints ||
                    corridor.points_xz_meters.Length != pointCount * 2 ||
                    corridor.cumulativeMeters == null ||
                    Mathf.Abs(corridor.cumulativeMeters[pointCount - 1] -
                        corridor.length_meters) > Mathf.Max(0.5f, corridor.length_meters * 0.002f) ||
                    !DirectionModeIsValid(corridor.direction_mode))
                {
                    report = $"corridor {index} is invalid";
                    return false;
                }
                for (int coordinate = 0; coordinate < corridor.points_xz_meters.Length; coordinate++)
                {
                    float value = corridor.points_xz_meters[coordinate];
                    if (!IsFinite(value) || Mathf.Abs(value) > 12000.01f)
                    {
                        report = $"corridor {index} coordinate is invalid";
                        return false;
                    }
                }
            }
            bool[] referenced = new bool[parsedManifest.corridors.Length];
            foreach (Tile tile in parsedManifest.tiles)
            {
                if (tile == null || tile.tile_x < 0 || tile.tile_x >= ExpectedTileAxisCount ||
                    tile.tile_z < 0 || tile.tile_z >= ExpectedTileAxisCount ||
                    tile.corridor_indices == null)
                {
                    report = "tile index is invalid";
                    return false;
                }
                foreach (int corridorIndex in tile.corridor_indices)
                {
                    if (corridorIndex < 0 || corridorIndex >= referenced.Length)
                    {
                        report = "tile references an invalid corridor";
                        return false;
                    }
                    referenced[corridorIndex] = true;
                }
            }
            for (int index = 0; index < referenced.Length; index++)
            {
                if (!referenced[index])
                {
                    report = $"corridor {index} is absent from the spatial index";
                    return false;
                }
            }
            report = null;
            return true;
        }

        private void DrawForCamera(Camera camera)
        {
            if (camera == null || !enabled || !contractValid ||
                !gameObject.activeInHierarchy || !gameObject.scene.IsValid() ||
                (camera.cullingMask & (1 << gameObject.layer)) == 0)
            {
                return;
            }
            if (lastPreparedFrame != Time.frameCount || lastPreparedCamera != camera)
            {
                PrepareVisibleInstances(camera, scheduleClockSeconds);
                lastPreparedFrame = Time.frameCount;
                lastPreparedCamera = camera;
            }
            Material material = acceptedVehicleAssets.SharedOpaqueMaterial;
            Mesh[] nearMeshes = acceptedVehicleAssets.NearArchetypeMeshes;
            for (int archetype = 0; archetype < nearCounts.Length; archetype++)
            {
                SubmitChunks(nearMeshes[archetype], material, nearMatrices[archetype],
                    nearBodyTints[archetype], nearTrailerTints[archetype], nearCounts[archetype],
                    nearPropertyBlocks, archetype, camera);
            }
            Mesh[] farMeshes = acceptedVehicleAssets.FarProxyMeshes;
            for (int archetype = 0; archetype < farCounts.Length; archetype++)
            {
                SubmitChunks(farMeshes[archetype], material, farMatrices[archetype],
                    farBodyTints[archetype], farTrailerTints[archetype], farCounts[archetype],
                    farPropertyBlocks, archetype, camera);
            }
            if (!submissionLogged && Application.isPlaying)
            {
                submissionLogged = true;
                Debug.Log(
                    $"[QuestFlight][TrafficV2] Submitted full-map corridor traffic at " +
                    $"t={scheduleClockSeconds:F1}s for '{camera.name}', stereo={camera.stereoEnabled}.",
                    this);
            }
        }

        private void PrepareVisibleInstances(Camera camera, double scheduleSeconds)
        {
            Array.Clear(nearCounts, 0, nearCounts.Length);
            Array.Clear(farCounts, 0, farCounts.Length);
            droppedInstanceCount = 0;
            visitGeneration++;
            if (visitGeneration == int.MaxValue)
            {
                Array.Clear(corridorVisitGeneration, 0, corridorVisitGeneration.Length);
                visitGeneration = 1;
            }
            Vector3 cameraPosition = camera.transform.position;
            int centerX = TileCoordinate(cameraPosition.x);
            int centerZ = TileCoordinate(cameraPosition.z);
            int tileRadius = Mathf.CeilToInt(FarDistanceMeters / ExpectedTileSizeMeters) + 1;
            for (int tileX = Mathf.Max(0, centerX - tileRadius);
                tileX <= Mathf.Min(ExpectedTileAxisCount - 1, centerX + tileRadius); tileX++)
            {
                for (int tileZ = Mathf.Max(0, centerZ - tileRadius);
                    tileZ <= Mathf.Min(ExpectedTileAxisCount - 1, centerZ + tileRadius); tileZ++)
                {
                    if (!tileByKey.TryGetValue(TileKey(tileX, tileZ), out Tile tile))
                    {
                        continue;
                    }
                    foreach (int corridorIndex in tile.corridor_indices)
                    {
                        if (corridorVisitGeneration[corridorIndex] == visitGeneration)
                        {
                            continue;
                        }
                        corridorVisitGeneration[corridorIndex] = visitGeneration;
                        PrepareCorridor(cameraPosition, parsedManifest.corridors[corridorIndex],
                            corridorIndex, scheduleSeconds);
                    }
                }
            }
        }

        private void PrepareCorridor(
            Vector3 cameraPosition,
            Corridor corridor,
            int corridorIndex,
            double scheduleSeconds)
        {
            for (int direction = 0; direction < 2; direction++)
            {
                if (!DirectionIsEnabled(corridor, direction))
                {
                    continue;
                }
                double age = PositiveModulo(scheduleSeconds - corridor.phase_seconds,
                    corridor.headway_seconds);
                int streamIndex = 0;
                for (double vehicleAge = age;
                    vehicleAge * corridor.speed_mps < corridor.length_meters;
                    vehicleAge += corridor.headway_seconds, streamIndex++)
                {
                    int laneSlots = Mathf.Max(1, corridor.lane_count /
                        (string.Equals(corridor.direction_mode, "BIDIRECTIONAL",
                            StringComparison.Ordinal) ? 2 : 1));
                    int laneIndex = PositiveModulo(
                        StableHash(corridor.corridor_id) + streamIndex, laneSlots);
                    if (!TryEvaluateDistance(corridor, direction,
                        (float)(vehicleAge * corridor.speed_mps), laneIndex,
                        out Vector3 position, out Quaternion rotation))
                    {
                        continue;
                    }
                    float distance = Vector3.Distance(cameraPosition, position);
                    if (distance > FarDistanceMeters)
                    {
                        continue;
                    }
                    long departure = (long)Math.Floor(
                        (scheduleSeconds - corridor.phase_seconds) / corridor.headway_seconds) -
                        streamIndex;
                    int archetype = SelectArchetype(corridor, departure, direction);
                    int paint = PositiveModulo(
                        StableHash(corridor.corridor_id) + (int)(departure & 0x7fffffff) * 31 +
                        direction * 131, PaintPalette.Length);
                    AppendInstance(position, rotation, distance, archetype, paint,
                        corridorIndex, direction);
                }
            }
        }

        private bool TryEvaluateDistance(
            Corridor corridor,
            int direction,
            float travelDistance,
            int laneIndex,
            out Vector3 position,
            out Quaternion rotation)
        {
            float distance = direction == 0
                ? travelDistance
                : corridor.length_meters - travelDistance;
            distance = Mathf.Clamp(distance, 0f, corridor.length_meters);
            float[] cumulative = corridor.cumulativeMeters;
            int low = 0;
            int high = cumulative.Length - 1;
            while (low + 1 < high)
            {
                int middle = (low + high) >> 1;
                if (cumulative[middle] <= distance)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            float segmentLength = Mathf.Max(0.001f, cumulative[high] - cumulative[low]);
            float ratio = Mathf.Clamp01((distance - cumulative[low]) / segmentLength);
            float startX = corridor.points_xz_meters[low * 2];
            float startZ = corridor.points_xz_meters[low * 2 + 1];
            float endX = corridor.points_xz_meters[high * 2];
            float endZ = corridor.points_xz_meters[high * 2 + 1];
            Vector3 flatForward = new Vector3(endX - startX, 0f, endZ - startZ).normalized;
            if (direction == 1)
            {
                flatForward = -flatForward;
            }
            Vector3 right = new Vector3(flatForward.z, 0f, -flatForward.x);
            float lateral = 1.75f + laneIndex * 3.5f;
            float x = Mathf.LerpUnclamped(startX, endX, ratio) + right.x * lateral;
            float z = Mathf.LerpUnclamped(startZ, endZ, ratio) + right.z * lateral;
            if (!terrainSurface.TrySample(x, z, out ProductionTerrainSample sample))
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }
            Vector3 normal = sample.normal.sqrMagnitude > 0.5f
                ? sample.normal.normalized
                : Vector3.up;
            Vector3 surfaceForward = Vector3.ProjectOnPlane(flatForward, normal).normalized;
            if (surfaceForward.sqrMagnitude < 0.5f)
            {
                surfaceForward = flatForward;
            }
            position = new Vector3(x, sample.heightMeters + RoadSurfaceOffsetMeters, z);
            rotation = Quaternion.LookRotation(surfaceForward, normal);
            return true;
        }

        private void AppendInstance(
            Vector3 position,
            Quaternion rotation,
            float distance,
            int archetype,
            int paintIndex,
            int corridorIndex,
            int direction)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
            if (distance <= NearDistanceMeters)
            {
                int count = nearCounts[archetype];
                if (count >= MaximumInstancesPerGroup)
                {
                    droppedInstanceCount++;
                    return;
                }
                nearMatrices[archetype][count] = matrix;
                nearBodyTints[archetype][count] = PaintPalette[paintIndex];
                nearTrailerTints[archetype][count] = TrailerPalette[paintIndex];
                nearCounts[archetype] = count + 1;
            }
            else
            {
                int proxy = archetype == KbduTrafficRuntime.ArchetypeCount - 1
                    ? 2
                    : PositiveModulo(corridorIndex + direction, 2);
                int count = farCounts[proxy];
                if (count >= MaximumInstancesPerGroup)
                {
                    droppedInstanceCount++;
                    return;
                }
                farMatrices[proxy][count] = matrix;
                farBodyTints[proxy][count] = PaintPalette[paintIndex];
                farTrailerTints[proxy][count] = TrailerPalette[paintIndex];
                farCounts[proxy] = count + 1;
            }
        }

        private void SubmitChunks(
            Mesh mesh,
            Material material,
            Matrix4x4[] matrices,
            Vector4[] bodyTints,
            Vector4[] trailerTints,
            int count,
            MaterialPropertyBlock[] propertyBlocks,
            int group,
            Camera camera)
        {
            for (int offset = 0; offset < count; offset += DrawMeshInstancedLimit)
            {
                int chunkCount = Mathf.Min(DrawMeshInstancedLimit, count - offset);
                Array.Copy(matrices, offset, chunkMatrices, 0, chunkCount);
                Array.Copy(bodyTints, offset, chunkBodyTints, 0, chunkCount);
                Array.Copy(trailerTints, offset, chunkTrailerTints, 0, chunkCount);
                MaterialPropertyBlock propertyBlock = propertyBlocks[group] ??
                    (propertyBlocks[group] = new MaterialPropertyBlock());
                propertyBlock.Clear();
                propertyBlock.SetVectorArray("_BodyTint", chunkBodyTints);
                propertyBlock.SetVectorArray("_TrailerTint", chunkTrailerTints);
                Graphics.DrawMeshInstanced(mesh, 0, material, chunkMatrices, chunkCount,
                    propertyBlock, ShadowCastingMode.Off, false, gameObject.layer, camera,
                    LightProbeUsage.Off, null);
            }
        }

        private static bool DirectionModeIsValid(string value)
        {
            return string.Equals(value, "FORWARD", StringComparison.Ordinal) ||
                string.Equals(value, "REVERSE", StringComparison.Ordinal) ||
                string.Equals(value, "BIDIRECTIONAL", StringComparison.Ordinal);
        }

        private static bool DirectionIsEnabled(Corridor corridor, int direction)
        {
            if (string.Equals(corridor.direction_mode, "BIDIRECTIONAL", StringComparison.Ordinal))
            {
                return true;
            }
            return direction == 0
                ? string.Equals(corridor.direction_mode, "FORWARD", StringComparison.Ordinal)
                : string.Equals(corridor.direction_mode, "REVERSE", StringComparison.Ordinal);
        }

        private static int SelectArchetype(Corridor corridor, long departure, int direction)
        {
            int value = PositiveModulo(
                StableHash(corridor.corridor_id) + (int)(departure & 0x7fffffff) * 17 +
                direction * 71, 100);
            int heavyPercent = corridor.road_class == "motorway" || corridor.road_class == "trunk"
                ? 12
                : corridor.road_class == "primary" ? 6 : 2;
            if (value < heavyPercent)
            {
                return 5;
            }
            switch (value % 4)
            {
                case 0: return 0;
                case 1: return 2;
                case 2: return 3;
                default: return 0;
            }
        }

        private static int TileCoordinate(float value)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt((value - BoundsMinimumMeters) / ExpectedTileSizeMeters),
                0, ExpectedTileAxisCount - 1);
        }

        private static int TileKey(int tileX, int tileZ)
        {
            return tileX * ExpectedTileAxisCount + tileZ;
        }

        private static double PositiveModulo(double value, double divisor)
        {
            double result = value % divisor;
            return result < 0.0 ? result + divisor : result;
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
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

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder text = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    text.Append(digest[index].ToString("x2"));
                }
                return text.ToString();
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int SumCounts(int[] values)
        {
            int sum = 0;
            for (int index = 0; index < values.Length; index++)
            {
                sum += values[index];
            }
            return sum;
        }

        private static Matrix4x4[][] CreateMatrixGroups()
        {
            Matrix4x4[][] result = new Matrix4x4[KbduTrafficRuntime.ArchetypeCount][];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new Matrix4x4[MaximumInstancesPerGroup];
            }
            return result;
        }

        private static Vector4[][] CreateVectorGroups()
        {
            Vector4[][] result = new Vector4[KbduTrafficRuntime.ArchetypeCount][];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new Vector4[MaximumInstancesPerGroup];
            }
            return result;
        }

        private static Matrix4x4[][] CreateProxyMatrixGroups()
        {
            Matrix4x4[][] result = new Matrix4x4[KbduTrafficRuntime.ProxyArchetypeCount][];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new Matrix4x4[MaximumInstancesPerGroup];
            }
            return result;
        }

        private static Vector4[][] CreateProxyVectorGroups()
        {
            Vector4[][] result = new Vector4[KbduTrafficRuntime.ProxyArchetypeCount][];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new Vector4[MaximumInstancesPerGroup];
            }
            return result;
        }
    }
}
