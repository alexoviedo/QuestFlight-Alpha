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
        public const string ExpectedGradeSeparatedRoadAuthoritySha256 =
            "e06ee50d3de7f404489ceb4c2e48dc988e68a549ec8bc1a44726eee7d8da0d84";
        public const int DrawMeshInstancedLimit = 1023;
        public const int MaximumInstancesPerGroup = 2048;
        public const int MaximumDrawCalls = 18;
        public const float NearDistanceMeters = 1000f;
        public const float FarDistanceMeters = 4500f;
        public const float EndpointFadeSeconds = 2f;
        public const float FarFadeMeters = 250f;
        public const float LodHysteresisMeters = 100f;
        public const float LegacyUniversalRoadSurfaceOffsetMeters = 0.04f;
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
            [NonSerialized] public int stableHash;
            [NonSerialized] public Stream[] streams;
            [NonSerialized] public Bounds conservativeBounds;
        }

        private sealed class Stream
        {
            public int hash, direction, root;
            public double phase, ageOffset, lifetime;
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
        [SerializeField] private TextAsset gradeSeparatedRoadAuthority;
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
        private readonly Plane[] leftCullPlanes = new Plane[6];
        private readonly Plane[] rightCullPlanes = new Plane[6];
        private bool cullStereo, cullBoundsValid;
#if UNITY_EDITOR
        public bool DisableFrustumCullingForVisualQa { get; set; }
#endif
        private readonly Dictionary<int, Tile> tileByKey = new Dictionary<int, Tile>();
        private readonly Dictionary<long, int> lodBandStates = new Dictionary<long, int>();
        private readonly List<long> expiredLodStates = new List<long>();
        private Camera lodStateCamera;

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
        private KbduGradeSeparatedRoadAuthorityV1 roadAuthority;
#if UNITY_EDITOR
        private bool gradeSeparatedRoadDiagnosticBypass;
        private bool trafficContinuityDiagnosticBypass;
        public void ConfigureContinuityBaselineForVisualQa(bool enabled)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Editor diagnostic only");
            trafficContinuityDiagnosticBypass = enabled;
            parsedManifest = null;
            lodBandStates.Clear();
            contractValid = TryValidateContract(out _);
        }
#endif

        public TextAsset TrajectoryManifest => trajectoryManifest;
        public TextAsset SourceAndLicenseRecord => sourceAndLicenseRecord;
        public KbduTrafficRuntime AcceptedVehicleAssets => acceptedVehicleAssets;
        public ProductionTerrainSurfaceAsset TerrainSurface => terrainSurface;
        public TextAsset GradeSeparatedRoadAuthority => gradeSeparatedRoadAuthority;
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
        public int PreparedDrawCallCount
        {
            get
            {
                int draws = 0;
                foreach (int count in nearCounts) draws += (count + DrawMeshInstancedLimit - 1) / DrawMeshInstancedLimit;
                foreach (int count in farCounts) draws += (count + DrawMeshInstancedLimit - 1) / DrawMeshInstancedLimit;
                return draws;
            }
        }

#if UNITY_EDITOR
        public void SetGradeSeparatedRoadDiagnosticBypass(bool bypass)
        {
            gradeSeparatedRoadDiagnosticBypass = bypass;
            lastPreparedFrame = -1;
            lastPreparedCamera = null;
        }

        public void SetDiagnosticScheduleClock(float seconds)
        {
            scheduleClockSeconds = seconds;
            lastPreparedFrame = -1;
            lastPreparedCamera = null;
        }
#endif

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

        public void ConfigureGradeSeparatedRoadAuthority(TextAsset authority)
        {
            gradeSeparatedRoadAuthority = authority;
            roadAuthority = null;
            contractValid = TryValidateContract(out _);
        }

        public bool TryValidateContract(out string report)
        {
            contractValid = false;
            report = null;
            if (trajectoryManifest == null || sourceAndLicenseRecord == null ||
                acceptedVehicleAssets == null || terrainSurface == null ||
                gradeSeparatedRoadAuthority == null)
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
            // Git and the Windows authoring tool use different line endings. Pin the
            // LF serialization, retaining all content checks without platform-dependent bytes.
            if (!string.Equals(ComputeSha256(Encoding.UTF8.GetBytes(
                    gradeSeparatedRoadAuthority.text.Replace("\r\n", "\n"))),
                    ExpectedGradeSeparatedRoadAuthoritySha256, StringComparison.Ordinal))
            {
                report = "grade-separated road authority hash mismatch";
                return false;
            }
            try
            {
                roadAuthority = roadAuthority ??
                    new KbduGradeSeparatedRoadAuthorityV1(gradeSeparatedRoadAuthority);
                ParseManifest();
            }
            catch (Exception exception)
            {
                report = "grade-separated road authority invalid: " + exception.Message;
                return false;
            }
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
            double age = PositiveModulo(scheduleSeconds - corridor.streams[direction].phase,
                corridor.headway_seconds);
            if (age * corridor.speed_mps >= corridor.length_meters)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }
            long departure = (long)Math.Floor(
                (scheduleSeconds - corridor.streams[direction].phase) / corridor.headway_seconds);
            int archetype = SelectArchetype(corridor, departure, direction);
            return TryEvaluateDistance(corridor, direction,
                (float)(age * corridor.speed_mps), SelectLane(corridor, departure, direction),
                archetype, out position, out rotation);
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
                corridor.stableHash = StableHash(corridor.corridor_id);
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
            BuildContinuousStreams();
            BuildConservativeCorridorBounds();
            foreach (Tile tile in parsedManifest.tiles)
            {
                if (tile != null)
                {
                    tileByKey[TileKey(tile.tile_x, tile.tile_z)] = tile;
                }
            }
        }

        private void BuildConservativeCorridorBounds()
        {
            float minimumY = float.PositiveInfinity, maximumY = float.NegativeInfinity;
            if (terrainSurface == null) return;
            foreach (var layer in terrainSurface.layers)
                if (layer != null && layer.IsValid)
                    foreach (float y in layer.heightMeters)
                    { minimumY = Mathf.Min(minimumY, y); maximumY = Mathf.Max(maximumY, y); }
            foreach (var patch in terrainSurface.transitionPatches)
                if (patch != null && patch.IsValid)
                    foreach (Vector3 vertex in patch.vertices)
                    { minimumY = Mathf.Min(minimumY, vertex.y); maximumY = Mathf.Max(maximumY, vertex.y); }
            if (roadAuthority != null)
                foreach (var profile in roadAuthority.Data.profiles)
                    for (int i = 1; i < profile.samples_xyz_meters.Length; i += 3)
                    { float y = profile.samples_xyz_meters[i]; minimumY = Mathf.Min(minimumY, y); maximumY = Mathf.Max(maximumY, y); }
            cullBoundsValid = !float.IsInfinity(minimumY) && !float.IsInfinity(maximumY) &&
                !float.IsNaN(minimumY) && !float.IsNaN(maximumY);
            if (!cullBoundsValid) return;
            foreach (var corridor in parsedManifest.corridors)
            {
                if (corridor == null || corridor.points_xz_meters.Length < 2) continue;
                float[] p = corridor.points_xz_meters;
                Bounds bounds = new Bounds(new Vector3(p[0], minimumY, p[1]), Vector3.zero);
                for (int i = 0; i < p.Length; i += 2)
                {
                    bounds.Encapsulate(new Vector3(p[i], minimumY, p[i + 1]));
                    bounds.Encapsulate(new Vector3(p[i], maximumY, p[i + 1]));
                }
                // Cover lane offsets, the entire longest vehicle and numerical/head-pose margin.
                // Height spans all authored terrain and bridge data, never a guessed ground plane.
                bounds.Expand(128f + corridor.lane_count * 3.5f);
                corridor.conservativeBounds = bounds;
            }
        }

        private bool CorridorCanReachEitherEye(Corridor corridor)
        {
#if UNITY_EDITOR
            if (DisableFrustumCullingForVisualQa) return true;
#endif
            return !cullBoundsValid || GeometryUtility.TestPlanesAABB(leftCullPlanes, corridor.conservativeBounds) ||
                cullStereo && GeometryUtility.TestPlanesAABB(rightCullPlanes, corridor.conservativeBounds);
        }

        // Continue a stream only at an exact, unambiguous two-road endpoint with
        // matching authored flow/lane/speed contracts. Junctions and ambiguous
        // source overlaps retain their independent schedules; no invented turn.
        private void BuildContinuousStreams()
        {
            Corridor[] corridors = parsedManifest.corridors;
            if (roadAuthority == null && gradeSeparatedRoadAuthority != null)
                roadAuthority = new KbduGradeSeparatedRoadAuthorityV1(gradeSeparatedRoadAuthority);
            var ports = new Dictionary<Vector2, List<int>>();
            for (int i = 0; i < corridors.Length; i++)
            {
                Corridor c = corridors[i];
                if (c == null || c.points_xz_meters == null || c.points_xz_meters.Length < 4 ||
                    c.points_xz_meters.Length % 2 != 0) return;
                c.streams = new Stream[2];
                for (int end = 0; end < 2; end++)
                {
                    int p = end == 0 ? 0 : c.points_xz_meters.Length - 2;
                    var point = new Vector2(c.points_xz_meters[p], c.points_xz_meters[p + 1]);
                    if (!ports.TryGetValue(point, out List<int> list)) ports[point] = list = new List<int>(2);
                    list.Add(i * 2 + end);
                }
            }
            int[] next = new int[corridors.Length * 2], previous = new int[corridors.Length * 2];
            Array.Fill(next, -1);
            Array.Fill(previous, -1);
            foreach (List<int> list in ports.Values)
            {
#if UNITY_EDITOR
                if (trafficContinuityDiagnosticBypass) break;
#endif
                if (list.Count != 2 || list[0] / 2 == list[1] / 2) continue;
                for (int side = 0; side < 2; side++)
                {
                    int fromPort = list[side], toPort = list[1 - side];
                    int fromDirection = 1 - fromPort % 2, toDirection = toPort % 2;
                    Corridor from = corridors[fromPort / 2], to = corridors[toPort / 2];
                    // Existing deck authority is route-specific; a 2D endpoint
                    // alone cannot prove the adjoining route uses the same deck.
                    if (roadAuthority == null || roadAuthority.HasRouteProfile(from.corridor_id) ||
                        roadAuthority.HasRouteProfile(to.corridor_id)) continue;
                    if (!DirectionIsEnabled(from, fromDirection) || !DirectionIsEnabled(to, toDirection) ||
                        from.headway_seconds != to.headway_seconds || from.speed_mps != to.speed_mps ||
                        from.lane_count != to.lane_count || from.direction_mode != to.direction_mode ||
                        from.road_class != to.road_class) continue;
                    // Only a nearly straight continuation can preserve the lane
                    // center at the shared endpoint without inventing a turn arc.
                    if (Vector2.Dot(EndpointHeading(from, fromDirection, true),
                            EndpointHeading(to, toDirection, false)) < 0.999f) continue;
                    int fromId = fromPort / 2 * 2 + fromDirection, toId = toPort / 2 * 2 + toDirection;
                    next[fromId] = toId;
                    previous[toId] = fromId;
                }
            }
            for (int id = 0; id < next.Length; id++)
            {
                if (previous[id] >= 0 || !DirectionIsEnabled(corridors[id / 2], id % 2)) continue;
                var chain = new List<int>();
                double lifetime = 0;
                for (int node = id; node >= 0; node = next[node])
                {
                    chain.Add(node);
                    lifetime += corridors[node / 2].length_meters / (double)corridors[node / 2].speed_mps;
                }
                double offset = 0;
                Corridor root = corridors[id / 2];
                foreach (int node in chain)
                {
                    Corridor c = corridors[node / 2];
                    c.streams[node % 2] = new Stream { hash = root.stableHash, direction = id % 2, root = id / 2,
                        phase = root.phase_seconds + offset, ageOffset = offset, lifetime = lifetime };
                    offset += c.length_meters / (double)c.speed_mps;
                }
            }
            // Closed cycles have no start: retain their authored schedules until
            // an explicit loop-flow contract exists instead of inventing one.
            for (int index = 0; index < corridors.Length; index++)
            {
                Corridor c = corridors[index];
                for (int direction = 0; direction < 2; direction++)
                    if (c.streams[direction] == null)
                        c.streams[direction] = new Stream { hash = c.stableHash, direction = direction, root = index,
                            phase = c.phase_seconds, lifetime = c.length_meters / (double)c.speed_mps };
            }
        }

        private static Vector2 EndpointHeading(Corridor corridor, int direction, bool exit)
        {
            float[] p = corridor.points_xz_meters;
            bool atEnd = (direction == 0) == exit;
            int a = atEnd ? p.Length - 4 : 0;
            Vector2 heading = new Vector2(p[a + 2] - p[a], p[a + 3] - p[a + 1]).normalized;
            return direction == 0 ? heading : -heading;
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
            if (lodStateCamera != camera)
            {
                lodBandStates.Clear();
                lodStateCamera = camera;
            }
            visitGeneration++;
            if (visitGeneration == int.MaxValue)
            {
                Array.Clear(corridorVisitGeneration, 0, corridorVisitGeneration.Length);
                visitGeneration = 1;
                lodBandStates.Clear();
            }
            if ((visitGeneration & 127) == 0)
            {
                expiredLodStates.Clear();
                foreach (var state in lodBandStates)
                    if (visitGeneration - Math.Abs(state.Value) > 2) expiredLodStates.Add(state.Key);
                foreach (long key in expiredLodStates) lodBandStates.Remove(key);
            }
            cullStereo = camera.stereoEnabled;
            if (cullStereo)
            {
                GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) *
                    camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left), leftCullPlanes);
                GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right) *
                    camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right), rightCullPlanes);
            }
            else GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix, leftCullPlanes);
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
                        Corridor corridor = parsedManifest.corridors[corridorIndex];
                        if (CorridorCanReachEitherEye(corridor))
                            PrepareCorridor(cameraPosition, corridor, corridorIndex, scheduleSeconds);
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
                Stream stream = corridor.streams[direction];
                double age = PositiveModulo(scheduleSeconds - stream.phase,
                    corridor.headway_seconds);
                int streamIndex = 0;
                for (double vehicleAge = age;
                    vehicleAge * corridor.speed_mps < corridor.length_meters;
                    vehicleAge += corridor.headway_seconds, streamIndex++)
                {
                    long departure = (long)Math.Floor(
                        (scheduleSeconds - stream.phase) / corridor.headway_seconds) -
                        streamIndex;
                    int laneIndex = SelectLane(corridor, departure, direction);
#if UNITY_EDITOR
                    if (trafficContinuityDiagnosticBypass)
                        laneIndex = PositiveModulo(corridor.stableHash + streamIndex,
                            Mathf.Max(1, corridor.lane_count / (corridor.direction_mode == "BIDIRECTIONAL" ? 2 : 1)));
#endif
                    int archetype = SelectArchetype(corridor, departure, direction);
                    if (!TryEvaluateDistance(corridor, direction,
                        (float)(vehicleAge * corridor.speed_mps), laneIndex, archetype,
                        out Vector3 position, out Quaternion rotation, cameraPosition))
                    {
                        continue;
                    }
                    float distance = Vector3.Distance(cameraPosition, position);
                    if (distance > FarDistanceMeters)
                    {
                        continue;
                    }
                    int paint = PositiveModulo(
                        stream.hash + (int)(departure & 0x7fffffff) * 31 +
                        stream.direction * 131, PaintPalette.Length);
                    AppendInstance(position, rotation, distance, archetype, paint,
                        corridorIndex, direction, EvaluateVisibilityEnvelope(vehicleAge + stream.ageOffset,
                            stream.lifetime, distance), RetainNearLod(stream.root, stream.direction, departure, distance));
                }
            }
        }

        private bool TryEvaluateDistance(
            Corridor corridor,
            int direction,
            float travelDistance,
            int laneIndex,
            int archetype,
            out Vector3 position,
            out Quaternion rotation,
            Vector3? visibilityCenter = null)
        {
            if (roadAuthority == null)
            {
                if (gradeSeparatedRoadAuthority == null)
                {
                    position = default;
                    rotation = Quaternion.identity;
                    return false;
                }
                roadAuthority = new KbduGradeSeparatedRoadAuthorityV1(
                    gradeSeparatedRoadAuthority);
            }
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
            // Reject only points provably outside the 3D draw radius before the
            // expensive three terrain/road samples. This cannot reject a visible point.
            if (visibilityCenter.HasValue)
            {
                Vector3 center = visibilityCenter.Value;
                float dx = x - center.x, dz = z - center.z;
                if (dx * dx + dz * dz > FarDistanceMeters * FarDistanceMeters)
                {
                    position = default;
                    rotation = Quaternion.identity;
                    return false;
                }
            }
            float contactOffset = roadAuthority.ContactOffsetMeters(archetype);
#if UNITY_EDITOR
            bool useGradeSeparatedRoad = !gradeSeparatedRoadDiagnosticBypass;
            if (gradeSeparatedRoadDiagnosticBypass)
            {
                contactOffset = LegacyUniversalRoadSurfaceOffsetMeters;
                if (!terrainSurface.TrySample(x, z, out ProductionTerrainSample baselineSample))
                {
                    position = default;
                    rotation = Quaternion.identity;
                    return false;
                }
                Vector3 baselineNormal = baselineSample.normal.sqrMagnitude > 0.5f
                    ? baselineSample.normal.normalized
                    : Vector3.up;
                Vector3 baselineForward = Vector3.ProjectOnPlane(flatForward, baselineNormal).normalized;
                if (baselineForward.sqrMagnitude < 0.5f) baselineForward = flatForward;
                position = new Vector3(x, baselineSample.heightMeters, z) + baselineNormal * contactOffset;
                rotation = Quaternion.LookRotation(baselineForward, baselineNormal);
                return true;
            }
#else
            const bool useGradeSeparatedRoad = true;
#endif
            if (!TrySampleRoadSurface(
                    corridor.corridor_id, x, z, flatForward, useGradeSeparatedRoad,
                    out float centerHeight, out Vector3 centerNormal))
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }
            float halfLength = roadAuthority.ContactHalfLengthMeters(archetype);
            float frontX = x + flatForward.x * halfLength;
            float frontZ = z + flatForward.z * halfLength;
            float rearX = x - flatForward.x * halfLength;
            float rearZ = z - flatForward.z * halfLength;
            if (!TrySampleRoadSurface(
                    corridor.corridor_id, frontX, frontZ, flatForward, useGradeSeparatedRoad,
                    out float frontHeight, out _) ||
                !TrySampleRoadSurface(
                    corridor.corridor_id, rearX, rearZ, flatForward, useGradeSeparatedRoad,
                    out float rearHeight, out _))
            {
                frontHeight = centerHeight;
                rearHeight = centerHeight;
            }
            float chordHeight = (frontHeight + rearHeight) * 0.5f;
            float rootHeight = chordHeight + Mathf.Max(0f, centerHeight - chordHeight) + contactOffset;
            Vector3 supportedForward = new Vector3(
                flatForward.x * halfLength * 2f,
                frontHeight - rearHeight,
                flatForward.z * halfLength * 2f).normalized;
            Vector3 supportedUp = Vector3.ProjectOnPlane(centerNormal, supportedForward).normalized;
            if (supportedUp.sqrMagnitude < 0.5f)
            {
                supportedUp = Vector3.ProjectOnPlane(Vector3.up, supportedForward).normalized;
            }
            position = new Vector3(x, rootHeight, z);
            rotation = Quaternion.LookRotation(supportedForward, supportedUp);
            return true;
        }

        private bool TrySampleRoadSurface(
            string routeId,
            float x,
            float z,
            Vector3 horizontalForward,
            bool useGradeSeparatedRoad,
            out float height,
            out Vector3 normal)
        {
            bool hasTerrain = terrainSurface.TrySample(x, z, out ProductionTerrainSample terrainSample);
            if (useGradeSeparatedRoad && roadAuthority.TryEvaluate(
                    routeId, x, z, horizontalForward,
                    out KbduGradeSeparatedRoadAuthorityV1.SurfaceSample roadSample) &&
                (!hasTerrain || roadSample.position.y >= terrainSample.heightMeters))
            {
                height = roadSample.position.y;
                normal = roadSample.normal.sqrMagnitude > 0.5f ? roadSample.normal.normalized : Vector3.up;
                return true;
            }
            // A straight authored approach can pass under a terrain crest. The
            // visible terrain occludes it: never support a vehicle below that surface.
            if (hasTerrain)
            {
                height = terrainSample.heightMeters;
                normal = terrainSample.normal.sqrMagnitude > 0.5f ? terrainSample.normal.normalized : Vector3.up;
                return true;
            }
            height = 0f;
            normal = Vector3.up;
            return false;
        }

        private void AppendInstance(
            Vector3 position,
            Quaternion rotation,
            float distance,
            int archetype,
            int paintIndex,
            int corridorIndex,
            int direction,
            float visibility,
            bool near)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
            Vector4 bodyTint = PaintPalette[paintIndex];
            bodyTint.w = visibility;
#if UNITY_EDITOR
            if (trafficContinuityDiagnosticBypass)
            {
                bodyTint.w = 1;
                near = distance <= NearDistanceMeters;
            }
#endif
            if (near)
            {
                int count = nearCounts[archetype];
                if (count >= MaximumInstancesPerGroup)
                {
                    droppedInstanceCount++;
                    return;
                }
                nearMatrices[archetype][count] = matrix;
                nearBodyTints[archetype][count] = bodyTint;
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
                farBodyTints[proxy][count] = bodyTint;
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

        private static int SelectLane(Corridor corridor, long departure, int direction)
        {
            int slots = Mathf.Max(1, corridor.lane_count /
                (corridor.direction_mode == "BIDIRECTIONAL" ? 2 : 1));
            // Departure identifies the vehicle. Its changing rank in the stream
            // must never change its lane when the next vehicle enters the road.
            Stream stream = corridor.streams[direction];
            return (int)PositiveModulo((double)stream.hash + departure + stream.direction, slots);
        }

        public static bool EvaluateNearLod(bool previouslyNear, float distance) =>
            distance <= NearDistanceMeters + (previouslyNear ? LodHysteresisMeters : -LodHysteresisMeters);

        private bool RetainNearLod(int corridor, int direction, long departure, float distance)
        {
            long key = ((long)corridor << 33) | ((long)direction << 32) | (uint)departure;
            if (distance < NearDistanceMeters - LodHysteresisMeters ||
                distance > NearDistanceMeters + LodHysteresisMeters)
            {
                lodBandStates.Remove(key);
                return distance < NearDistanceMeters;
            }
            bool near = lodBandStates.TryGetValue(key, out int state)
                ? EvaluateNearLod(state > 0, distance) : distance <= NearDistanceMeters;
            lodBandStates[key] = near ? visitGeneration : -visitGeneration;
            return near;
        }

        public static float EvaluateVisibilityEnvelope(double age, double lifetime, float distance)
        {
            if (age <= 0 || age >= lifetime || distance >= FarDistanceMeters) return 0f;
            float fadeSeconds = (float)Math.Min(EndpointFadeSeconds, lifetime * 0.25);
            float endWeight = Mathf.Clamp01((float)Math.Min(age, lifetime - age) / fadeSeconds);
            float rangeWeight = Mathf.Clamp01((FarDistanceMeters - distance) / FarFadeMeters);
            return Mathf.SmoothStep(0f, 1f, endWeight) * Mathf.SmoothStep(0f, 1f, rangeWeight);
        }

#if UNITY_EDITOR
        public bool TryEvaluateVehicleForVisualQa(int corridorIndex, int direction, long departure,
            double time, out Vector3 position, out Quaternion rotation, out int lane, out float visibility)
        {
            ParseManifest();
            Corridor corridor = parsedManifest.corridors[corridorIndex];
            Stream stream = corridor.streams[direction];
            double age = time - stream.phase - departure * corridor.headway_seconds;
            lane = SelectLane(corridor, departure, direction);
            visibility = EvaluateVisibilityEnvelope(age + stream.ageOffset, stream.lifetime, 0);
            position = default;
            rotation = Quaternion.identity;
            return DirectionIsEnabled(corridor, direction) && age >= 0 &&
                age * corridor.speed_mps < corridor.length_meters &&
                TryEvaluateDistance(corridor, direction, (float)(age * corridor.speed_mps), lane,
                    SelectArchetype(corridor, departure, direction), out position, out rotation);
        }
#endif

        private static int SelectArchetype(Corridor corridor, long departure, int direction)
        {
            int value = PositiveModulo(
                corridor.streams[direction].hash + (int)(departure & 0x7fffffff) * 17 +
                corridor.streams[direction].direction * 71, 100);
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
