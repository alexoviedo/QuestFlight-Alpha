using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Draws the explicitly authored, spatially compiled full-map tree set
    /// without per-tree GameObjects. Near and middle trees share source-informed volumetric
    /// crown lobes around a real trunk/branch scaffold, while the far tier is
    /// one world-upright multiview impostor selected from the XR center-eye
    /// pose.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AirportCloseTreeLodRenderer : MonoBehaviour
    {
        public const int MaximumLod0Instances = 24;
        public const int MaximumLod1Instances = 240;
        public const int MaximumLod2Instances = 780;
        public const int MaximumDrawCount = 3;
        public const int ImpostorFrameCount = 8;

        private const byte Lod0 = 0;
        private const byte Lod1 = 1;
        private const byte Lod2 = 2;
        private const byte Culled = 3;
        private const byte Uninitialized = byte.MaxValue;
        private const int FullMapDatabaseVersion = 2;
        private const int FullMapHeaderBytes = 40;
        private const int FullMapInstanceBytes = 19;
        private static readonly byte[] FullMapMagic = Encoding.ASCII.GetBytes("QFFMVG2\0");

        private const int Lod0ArrayCapacity = MaximumLod0Instances * 2;
        private const int Lod1ArrayCapacity =
            MaximumLod1Instances + MaximumLod0Instances + MaximumLod1Instances;
        private const int Lod2ArrayCapacity =
            MaximumLod2Instances + MaximumLod1Instances;

        [Serializable]
        public struct Instance
        {
            [SerializeField] private Matrix4x4 matrix;
            [SerializeField] private Vector4 tint;
            [SerializeField] private Vector3 center;
            [SerializeField] private Vector3 position;
            [SerializeField] private Vector3 scale;
            [SerializeField] private float radius;
            [SerializeField] private float rotationDegrees;

            public Matrix4x4 Matrix => matrix;
            public Vector4 Tint => tint;
            public Vector3 Center => center;
            public Vector3 Position => position;
            public Vector3 Scale => scale;
            public float Radius => radius;
            public float RotationDegrees => rotationDegrees;

            public Instance(
                Matrix4x4 authoredMatrix,
                Vector4 authoredTint,
                Vector3 authoredCenter,
                Vector3 authoredPosition,
                Vector3 authoredScale,
                float authoredRadius,
                float authoredRotationDegrees)
            {
                matrix = authoredMatrix;
                tint = authoredTint;
                center = authoredCenter;
                position = authoredPosition;
                scale = authoredScale;
                radius = authoredRadius;
                rotationDegrees = authoredRotationDegrees;
            }
        }

        private static readonly int InstanceTintId = Shader.PropertyToID("_InstanceTint");
        private static readonly int InstanceFadeId = Shader.PropertyToID("_InstanceFade");
        private static readonly int ImpostorFrameId = Shader.PropertyToID("_ImpostorFrame");

        [SerializeField] private Material sharedCanopyMaterial;
        [SerializeField] private Mesh lod0Mesh;
        [SerializeField] private Mesh lod1Mesh;
        [SerializeField] private Mesh lod2Mesh;
        [SerializeField] private TextAsset placementManifest;
        [SerializeField] private TextAsset sourceLicenseManifest;
        [SerializeField] private TextAsset fullMapPlacementDatabase;
        [SerializeField] private Instance[] instances = Array.Empty<Instance>();
        [SerializeField] private float lod0EnterScreenPixels = 72f;
        [SerializeField] private float lod0ExitScreenPixels = 48f;
        [SerializeField] private float lod1EnterScreenPixels = 12f;
        [SerializeField] private float lod1ExitScreenPixels = 7f;
        [SerializeField] private float maximumDistanceMeters = 1600f;
        [SerializeField] private float transitionDurationSeconds = 0.60f;
        [SerializeField] private float minimumResidencySeconds = 1.00f;
        [SerializeField] private float impostorAngularHysteresisDegrees = 6f;
        [SerializeField] private int authoredMaximumDrawCount;
        [SerializeField] private long authoredMaximumSelectedTriangleCount;
        [SerializeField] private bool projectedScreenSizeSelection = true;
        [SerializeField] private bool persistentHysteresis = true;
        [SerializeField] private bool centerEyeWorldUprightImpostor = true;
        [SerializeField] private bool perEyeFacingBehavior;
        [SerializeField] private bool alphaToCoverageTransition = true;

        private readonly Matrix4x4[] lod0Matrices =
            new Matrix4x4[Lod0ArrayCapacity];
        private readonly Matrix4x4[] lod1Matrices =
            new Matrix4x4[Lod1ArrayCapacity];
        private readonly Matrix4x4[] lod2Matrices =
            new Matrix4x4[Lod2ArrayCapacity];
        private readonly Vector4[] lod0Tints = new Vector4[Lod0ArrayCapacity];
        private readonly Vector4[] lod1Tints = new Vector4[Lod1ArrayCapacity];
        private readonly Vector4[] lod2Tints = new Vector4[Lod2ArrayCapacity];
        private readonly float[] lod0Fades = new float[Lod0ArrayCapacity];
        private readonly float[] lod1Fades = new float[Lod1ArrayCapacity];
        private readonly float[] lod2Fades = new float[Lod2ArrayCapacity];
        private readonly float[] lod0Frames = new float[Lod0ArrayCapacity];
        private readonly float[] lod1Frames = new float[Lod1ArrayCapacity];
        private readonly float[] lod2Frames = new float[Lod2ArrayCapacity];
        private readonly Plane[] frustumPlanes = new Plane[6];

        private struct RuntimeInstance
        {
            public Vector3 Position;
            public Vector3 Scale;
            public Vector3 Center;
            public Vector4 Tint;
            public float Radius;
            public float RotationDegrees;

            public Matrix4x4 Matrix => Matrix4x4.TRS(
                Position,
                Quaternion.Euler(0f, RotationDegrees, 0f),
                Scale);
        }

        private RuntimeInstance[] runtimeInstances = Array.Empty<RuntimeInstance>();
        private int[] pageOffsets = Array.Empty<int>();
        private int[] pageCounts = Array.Empty<int>();
        private float databaseMinimumX;
        private float databaseMinimumZ;
        private float databasePageSize;
        private int databaseColumns;
        private int databaseRows;
        private int[] activeIndices = Array.Empty<int>();
        private int[] previousActiveIndices = Array.Empty<int>();
        private int activeCount;
        private int previousActiveCount;
        private Candidate[] candidates = Array.Empty<Candidate>();
        private int candidateCount;
        private int loadedDatabaseInstanceId;

        private struct Candidate
        {
            public int index;
            public float priority;
            public float pixels;
        }

        private sealed class CandidateComparer : IComparer<Candidate>
        {
            public static readonly CandidateComparer Instance = new CandidateComparer();

            public int Compare(Candidate left, Candidate right)
            {
                int priority = right.priority.CompareTo(left.priority);
                return priority != 0 ? priority : left.index.CompareTo(right.index);
            }
        }

        private bool[] visible = Array.Empty<bool>();
        private bool[] selectedLod0 = Array.Empty<bool>();
        private bool[] selectedLod1 = Array.Empty<bool>();
        private float[] projectedPixels = Array.Empty<float>();
        private float[] priorities = Array.Empty<float>();
        private byte[] desiredLods = Array.Empty<byte>();
        private byte[] currentLods = Array.Empty<byte>();
        private byte[] previousLods = Array.Empty<byte>();
        private byte[] impostorFrames = Array.Empty<byte>();
        private float[] transitionStarts = Array.Empty<float>();
        private float[] lastStateChanges = Array.Empty<float>();

        private MaterialPropertyBlock lod0Properties;
        private MaterialPropertyBlock lod1Properties;
        private MaterialPropertyBlock lod2Properties;

        public Material SharedCanopyMaterial => sharedCanopyMaterial;
        public Mesh Lod0Mesh => lod0Mesh;
        public Mesh Lod1Mesh => lod1Mesh;
        public Mesh Lod2Mesh => lod2Mesh;
        public TextAsset PlacementManifest => placementManifest;
        public TextAsset SourceLicenseManifest => sourceLicenseManifest;
        public TextAsset FullMapPlacementDatabase => fullMapPlacementDatabase;
        public Instance[] Instances => instances;
        public int RuntimeInstanceCount => runtimeInstances == null ? 0 : runtimeInstances.Length;
        public float Lod0EnterScreenPixels => lod0EnterScreenPixels;
        public float Lod0ExitScreenPixels => lod0ExitScreenPixels;
        public float Lod1EnterScreenPixels => lod1EnterScreenPixels;
        public float Lod1ExitScreenPixels => lod1ExitScreenPixels;
        public float MaximumDistanceMeters => maximumDistanceMeters;
        public float TransitionDurationSeconds => transitionDurationSeconds;
        public float MinimumResidencySeconds => minimumResidencySeconds;
        public int AuthoredMaximumDrawCount => authoredMaximumDrawCount;
        public long AuthoredMaximumSelectedTriangleCount =>
            authoredMaximumSelectedTriangleCount;
        public bool ProjectedScreenSizeSelection => projectedScreenSizeSelection;
        public bool PersistentHysteresis => persistentHysteresis;
        public bool CenterEyeWorldUprightImpostor => centerEyeWorldUprightImpostor;
        public bool PerEyeFacingBehavior => perEyeFacingBehavior;
        public bool AlphaToCoverageTransition => alphaToCoverageTransition;

        // Compatibility properties make the intentional V1-to-V2 policy change
        // explicit to repository validators and temporal evidence.
        public bool FixedWorldOrientationOnly => false;
        public bool CameraFacingBehavior => centerEyeWorldUprightImpostor;
        public bool LodCrossfadeOrDither => alphaToCoverageTransition;

        public void Configure(
            Material material,
            Mesh nearMesh,
            Mesh middleMesh,
            Mesh farMesh,
            TextAsset placements,
            TextAsset sourceManifest,
            Instance[] authoredInstances,
            TextAsset fullMapDatabase = null)
        {
            sharedCanopyMaterial = material;
            lod0Mesh = nearMesh;
            lod1Mesh = middleMesh;
            lod2Mesh = farMesh;
            placementManifest = placements;
            sourceLicenseManifest = sourceManifest;
            fullMapPlacementDatabase = fullMapDatabase;
            instances = authoredInstances ?? Array.Empty<Instance>();
            lod0EnterScreenPixels = 72f;
            lod0ExitScreenPixels = 48f;
            lod1EnterScreenPixels = 12f;
            lod1ExitScreenPixels = 7f;
            maximumDistanceMeters = fullMapDatabase == null ? 750f : 1600f;
            transitionDurationSeconds = 0.60f;
            minimumResidencySeconds = 1.00f;
            impostorAngularHysteresisDegrees = 6f;
            projectedScreenSizeSelection = true;
            persistentHysteresis = true;
            centerEyeWorldUprightImpostor = true;
            perEyeFacingBehavior = false;
            alphaToCoverageTransition = true;
            authoredMaximumDrawCount = MaximumDrawCount;
            authoredMaximumSelectedTriangleCount =
                (long)TriangleCount(lod0Mesh) * MaximumLod0Instances +
                (long)TriangleCount(lod1Mesh) * MaximumLod1Instances +
                (long)TriangleCount(lod2Mesh) * MaximumLod2Instances +
                (long)TriangleCount(lod1Mesh) * MaximumLod0Instances +
                (long)TriangleCount(lod2Mesh) * MaximumLod1Instances;
            loadedDatabaseInstanceId = 0;
            Prepare();
        }

        private static int TriangleCount(Mesh mesh)
        {
            return mesh == null || mesh.subMeshCount == 0
                ? 0
                : checked((int)(mesh.GetIndexCount(0) / 3u));
        }

        private void OnEnable()
        {
            Camera.onPreCull -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
            Prepare();
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Prepare();
        }
#endif

        private void Prepare()
        {
            lod0Properties ??= new MaterialPropertyBlock();
            lod1Properties ??= new MaterialPropertyBlock();
            lod2Properties ??= new MaterialPropertyBlock();
            EnsureRuntimeInstances();
            int count = runtimeInstances == null ? 0 : runtimeInstances.Length;
            if (currentLods.Length == count)
                return;

            visible = new bool[count];
            selectedLod0 = new bool[count];
            selectedLod1 = new bool[count];
            projectedPixels = new float[count];
            priorities = new float[count];
            desiredLods = new byte[count];
            currentLods = new byte[count];
            previousLods = new byte[count];
            impostorFrames = new byte[count];
            transitionStarts = new float[count];
            lastStateChanges = new float[count];
            int activeCapacity = count;
            if (pageOffsets.Length > 0)
            {
                int pageRadius = Mathf.CeilToInt(maximumDistanceMeters / databasePageSize);
                int nearbyPageCount = (pageRadius * 2 + 1) * (pageRadius * 2 + 1);
                int maximumPageCount = pageCounts.Max();
                activeCapacity = Mathf.Min(count, checked(nearbyPageCount * maximumPageCount));
            }
            activeIndices = new int[activeCapacity];
            previousActiveIndices = new int[activeCapacity];
            candidates = new Candidate[activeCapacity];
            activeCount = 0;
            previousActiveCount = 0;
            candidateCount = 0;
            for (int index = 0; index < count; index++)
            {
                desiredLods[index] = Culled;
                currentLods[index] = Uninitialized;
                previousLods[index] = Culled;
                impostorFrames[index] = 0;
                transitionStarts[index] = float.NegativeInfinity;
                lastStateChanges[index] = float.NegativeInfinity;
            }
        }

        private void EnsureRuntimeInstances()
        {
            if (fullMapPlacementDatabase == null)
            {
                Instance[] authored = instances ?? Array.Empty<Instance>();
                runtimeInstances = new RuntimeInstance[authored.Length];
                for (int index = 0; index < authored.Length; index++)
                {
                    runtimeInstances[index] = new RuntimeInstance
                    {
                        Position = authored[index].Position,
                        Scale = authored[index].Scale,
                        Center = authored[index].Center,
                        Tint = authored[index].Tint,
                        Radius = authored[index].Radius,
                        RotationDegrees = authored[index].RotationDegrees,
                    };
                }
                pageOffsets = Array.Empty<int>();
                pageCounts = Array.Empty<int>();
                loadedDatabaseInstanceId = 0;
                return;
            }
            int databaseId = fullMapPlacementDatabase.GetInstanceID();
            if (loadedDatabaseInstanceId == databaseId && runtimeInstances.Length > 0)
                return;
            if (lod0Mesh == null)
                return;

            byte[] bytes = fullMapPlacementDatabase.bytes;
            using MemoryStream stream = new MemoryStream(bytes, false);
            using BinaryReader reader = new BinaryReader(stream);
            byte[] magic = reader.ReadBytes(8);
            if (magic.Length != FullMapMagic.Length)
                throw new InvalidDataException("Full-map vegetation database header is truncated.");
            for (int index = 0; index < FullMapMagic.Length; index++)
                if (magic[index] != FullMapMagic[index])
                    throw new InvalidDataException("Full-map vegetation database magic mismatch.");
            int version = reader.ReadInt32();
            if (version != FullMapDatabaseVersion)
                throw new InvalidDataException($"Unsupported full-map vegetation database version {version}.");
            databaseMinimumX = reader.ReadSingle();
            databaseMinimumZ = reader.ReadSingle();
            databasePageSize = reader.ReadSingle();
            float quantization = reader.ReadSingle();
            databaseColumns = reader.ReadInt32();
            databaseRows = reader.ReadInt32();
            int instanceCount = reader.ReadInt32();
            if (databasePageSize <= 0f || Mathf.Abs(quantization - 0.1f) > 0.0001f ||
                databaseColumns <= 0 || databaseRows <= 0 || instanceCount <= 0)
                throw new InvalidDataException("Full-map vegetation database contract is invalid.");
            int pageCount = checked(databaseColumns * databaseRows);
            long expectedBytes = FullMapHeaderBytes + (long)pageCount * 8L +
                (long)instanceCount * FullMapInstanceBytes;
            if (bytes.LongLength != expectedBytes)
                throw new InvalidDataException(
                    $"Full-map vegetation database length mismatch ({bytes.LongLength} != {expectedBytes}).");
            pageOffsets = new int[pageCount];
            pageCounts = new int[pageCount];
            for (int page = 0; page < pageCount; page++)
            {
                pageOffsets[page] = reader.ReadInt32();
                pageCounts[page] = reader.ReadInt32();
                if (pageOffsets[page] < 0 || pageCounts[page] < 0 ||
                    pageOffsets[page] + pageCounts[page] > instanceCount)
                    throw new InvalidDataException($"Full-map vegetation page {page} is invalid.");
            }

            Bounds bounds = lod0Mesh.bounds;
            if (bounds.size.x <= 0.01f || bounds.size.y <= 0.01f || bounds.size.z <= 0.01f)
                throw new InvalidDataException("Full-map vegetation LOD0 bounds are invalid.");
            runtimeInstances = new RuntimeInstance[instanceCount];
            for (int page = 0; page < pageCount; page++)
            {
                int column = page % databaseColumns;
                int row = page / databaseColumns;
                float pageX = databaseMinimumX + column * databasePageSize;
                float pageZ = databaseMinimumZ + row * databasePageSize;
                for (int localIndex = 0; localIndex < pageCounts[page]; localIndex++)
                {
                    int destination = pageOffsets[page] + localIndex;
                    float x = pageX + reader.ReadUInt16() * quantization;
                    float z = pageZ + reader.ReadUInt16() * quantization;
                    float y = reader.ReadInt32() * 0.01f;
                    float height = reader.ReadUInt16() * 0.01f;
                    float crown = reader.ReadUInt16() * 0.01f;
                    float rotationDegrees = reader.ReadUInt16() * (360f / 65535f);
                    float red = reader.ReadByte() / 255f;
                    float green = reader.ReadByte() / 255f;
                    float blue = reader.ReadByte() / 255f;
                    reader.ReadByte(); // Stable seed is retained in the derivative manifest.
                    byte kind = reader.ReadByte();
                    if (height < 0.75f || height > 30f || crown < 1.5f || crown > 20f || kind > 2)
                        throw new InvalidDataException($"Full-map vegetation instance {destination} is implausible.");
                    Vector3 position = new Vector3(x, y, z);
                    Vector3 scale = new Vector3(
                        crown / bounds.size.x,
                        height / bounds.size.y,
                        crown / bounds.size.z);
                    Vector3 center = position + Vector3.up * height * 0.5f;
                    float radius = Mathf.Sqrt(
                        height * height * 0.25f + crown * crown * 0.5f);
                    runtimeInstances[destination] = new RuntimeInstance
                    {
                        Position = position,
                        Scale = scale,
                        Center = center,
                        Tint = new Vector4(red, green, blue, 1f),
                        Radius = radius,
                        RotationDegrees = rotationDegrees,
                    };
                }
            }
            loadedDatabaseInstanceId = databaseId;
        }

        private void DrawForCamera(Camera camera)
        {
            if (camera == null || !enabled || !gameObject.activeInHierarchy ||
                !gameObject.scene.IsValid() || sharedCanopyMaterial == null ||
                lod0Mesh == null || lod1Mesh == null || lod2Mesh == null ||
                runtimeInstances == null || runtimeInstances.Length == 0)
                return;
            if (camera.cameraType == CameraType.Preview ||
                camera.cameraType == CameraType.Reflection)
                return;
            int layer = gameObject.layer;
            if ((camera.cullingMask & (1 << layer)) == 0)
                return;

            Prepare();
            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            BuildActiveIndices(cameraPosition);
            EvaluateVisibility(camera, cameraPosition, cameraForward);
            SelectDesiredLods();
            float now = Time.realtimeSinceStartup;
            AdvancePersistentStates(now);
            BuildAndDraw(camera, cameraPosition, layer, now);
        }

        private void BuildActiveIndices(Vector3 cameraPosition)
        {
            int[] swap = previousActiveIndices;
            previousActiveIndices = activeIndices;
            activeIndices = swap;
            previousActiveCount = activeCount;
            activeCount = 0;
            for (int position = 0; position < previousActiveCount; position++)
            {
                int index = previousActiveIndices[position];
                visible[index] = false;
                desiredLods[index] = Culled;
            }

            if (pageOffsets.Length == 0)
            {
                for (int index = 0; index < runtimeInstances.Length; index++)
                    activeIndices[activeCount++] = index;
                return;
            }

            int centerColumn = Mathf.FloorToInt(
                (cameraPosition.x - databaseMinimumX) / databasePageSize);
            int centerRow = Mathf.FloorToInt(
                (cameraPosition.z - databaseMinimumZ) / databasePageSize);
            int radius = Mathf.CeilToInt(maximumDistanceMeters / databasePageSize);
            float maximumSquared = maximumDistanceMeters * maximumDistanceMeters;
            for (int row = Mathf.Max(0, centerRow - radius);
                 row <= Mathf.Min(databaseRows - 1, centerRow + radius);
                 row++)
            {
                float minimumZ = databaseMinimumZ + row * databasePageSize;
                float maximumZ = minimumZ + databasePageSize;
                float closestZ = Mathf.Clamp(cameraPosition.z, minimumZ, maximumZ);
                for (int column = Mathf.Max(0, centerColumn - radius);
                     column <= Mathf.Min(databaseColumns - 1, centerColumn + radius);
                     column++)
                {
                    float minimumX = databaseMinimumX + column * databasePageSize;
                    float maximumX = minimumX + databasePageSize;
                    float closestX = Mathf.Clamp(cameraPosition.x, minimumX, maximumX);
                    float dx = cameraPosition.x - closestX;
                    float dz = cameraPosition.z - closestZ;
                    if (dx * dx + dz * dz > maximumSquared)
                        continue;
                    Bounds pageBounds = new Bounds(
                        new Vector3(
                            (minimumX + maximumX) * 0.5f,
                            500f,
                            (minimumZ + maximumZ) * 0.5f),
                        new Vector3(databasePageSize, 2000f, databasePageSize));
                    if (!GeometryUtility.TestPlanesAABB(frustumPlanes, pageBounds))
                        continue;
                    int page = row * databaseColumns + column;
                    int start = pageOffsets[page];
                    int end = start + pageCounts[page];
                    for (int index = start; index < end; index++)
                    {
                        if (activeCount >= activeIndices.Length)
                            throw new InvalidOperationException(
                                "Full-map vegetation active-page capacity was exceeded.");
                        activeIndices[activeCount++] = index;
                    }
                }
            }
        }

        private void EvaluateVisibility(
            Camera camera,
            Vector3 cameraPosition,
            Vector3 cameraForward)
        {
            float maximumDistanceSquared = maximumDistanceMeters * maximumDistanceMeters;
            float focalPixels = camera.pixelHeight /
                (2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            candidateCount = 0;
            for (int active = 0; active < activeCount; active++)
            {
                int index = activeIndices[active];
                RuntimeInstance instance = runtimeInstances[index];
                Vector3 offset = instance.Center - cameraPosition;
                float distanceSquared = offset.sqrMagnitude;
                bool inRange = distanceSquared <= maximumDistanceSquared;
                bool inFront = Vector3.Dot(cameraForward, offset) >= -instance.Radius;
                bool inFrustum = false;
                if (inRange && inFront)
                {
                    Bounds bounds = new Bounds(
                        instance.Center,
                        Vector3.one * (instance.Radius * 2f));
                    inFrustum = GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                }
                visible[index] = inRange && inFront && inFrustum;
                float distance = Mathf.Sqrt(Mathf.Max(0.01f, distanceSquared));
                float pixels = instance.Radius * 2f * focalPixels / distance;
                projectedPixels[index] = pixels;
                float forwardWeight = Mathf.Clamp01(
                    Vector3.Dot(cameraForward, offset / distance));
                priorities[index] = pixels + forwardWeight * 32f - distance * 0.001f +
                    (runtimeInstances.Length - index) * 0.0000001f;
                selectedLod0[index] = false;
                selectedLod1[index] = false;
                desiredLods[index] = Culled;
                if (visible[index])
                {
                    candidates[candidateCount++] = new Candidate
                    {
                        index = index,
                        priority = priorities[index],
                        pixels = pixels,
                    };
                }
            }
            Array.Sort(candidates, 0, candidateCount, CandidateComparer.Instance);
        }

        private void SelectDesiredLods()
        {
            int lod0Count = 0;
            int lod1Count = 0;
            int lod2Count = 0;
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                Candidate candidate = candidates[candidateIndex];
                int index = candidate.index;
                float lod0Threshold = currentLods[index] == Lod0
                    ? lod0ExitScreenPixels
                    : lod0EnterScreenPixels;
                float lod1Threshold = currentLods[index] == Lod1
                    ? lod1ExitScreenPixels
                    : lod1EnterScreenPixels;
                if (candidate.pixels >= lod0Threshold && lod0Count < MaximumLod0Instances)
                {
                    selectedLod0[index] = true;
                    desiredLods[index] = Lod0;
                    lod0Count++;
                }
                else if (candidate.pixels >= lod1Threshold && lod1Count < MaximumLod1Instances)
                {
                    selectedLod1[index] = true;
                    desiredLods[index] = Lod1;
                    lod1Count++;
                }
                else if (lod2Count < MaximumLod2Instances)
                {
                    desiredLods[index] = Lod2;
                    lod2Count++;
                }
            }
        }

        private void AdvancePersistentStates(float now)
        {
            for (int active = 0; active < activeCount; active++)
            {
                int index = activeIndices[active];
                byte desired = desiredLods[index];
                if (currentLods[index] == Uninitialized)
                {
                    currentLods[index] = desired;
                    previousLods[index] = Culled;
                    transitionStarts[index] = float.NegativeInfinity;
                    lastStateChanges[index] = now;
                    continue;
                }
                if (currentLods[index] == desired)
                    continue;
                bool transitionComplete =
                    now - transitionStarts[index] >= transitionDurationSeconds;
                bool residencyComplete =
                    now - lastStateChanges[index] >= minimumResidencySeconds;
                if (!transitionComplete || !residencyComplete)
                    continue;
                previousLods[index] = currentLods[index];
                currentLods[index] = desired;
                transitionStarts[index] = now;
                lastStateChanges[index] = now;
            }
        }

        private void BuildAndDraw(
            Camera camera,
            Vector3 cameraPosition,
            int layer,
            float now)
        {
            int lod0Count = 0;
            int lod1Count = 0;
            int lod2Count = 0;

            for (int active = 0; active < activeCount; active++)
            {
                int index = activeIndices[active];
                if (!visible[index])
                    continue;
                float elapsed = now - transitionStarts[index];
                bool transitioning = elapsed >= 0f &&
                    elapsed < transitionDurationSeconds &&
                    previousLods[index] != currentLods[index];
                if (!transitioning)
                {
                    AddInstance(
                        index,
                        currentLods[index],
                        1f,
                        cameraPosition,
                        ref lod0Count,
                        ref lod1Count,
                        ref lod2Count);
                    continue;
                }
                float progress = Mathf.SmoothStep(
                    0f, 1f, elapsed / transitionDurationSeconds);
                AddInstance(
                    index,
                    previousLods[index],
                    1f - progress,
                    cameraPosition,
                    ref lod0Count,
                    ref lod1Count,
                    ref lod2Count);
                AddInstance(
                    index,
                    currentLods[index],
                    progress,
                    cameraPosition,
                    ref lod0Count,
                    ref lod1Count,
                    ref lod2Count);
            }

            DrawTier(
                lod0Mesh,
                lod0Matrices,
                lod0Tints,
                lod0Fades,
                lod0Frames,
                lod0Count,
                lod0Properties,
                camera,
                layer);
            DrawTier(
                lod1Mesh,
                lod1Matrices,
                lod1Tints,
                lod1Fades,
                lod1Frames,
                lod1Count,
                lod1Properties,
                camera,
                layer);
            DrawTier(
                lod2Mesh,
                lod2Matrices,
                lod2Tints,
                lod2Fades,
                lod2Frames,
                lod2Count,
                lod2Properties,
                camera,
                layer);
        }

        private void AddInstance(
            int index,
            byte lod,
            float fade,
            Vector3 cameraPosition,
            ref int lod0Count,
            ref int lod1Count,
            ref int lod2Count)
        {
            if (lod == Culled || fade <= 0.001f)
                return;
            RuntimeInstance instance = runtimeInstances[index];
            float distance = Vector3.Distance(cameraPosition, instance.Center);
            float handoffStart = Mathf.Max(0f, maximumDistanceMeters - 180f);
            float handoff = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(handoffStart, maximumDistanceMeters, distance));
            fade *= handoff;
            if (fade <= 0.001f)
                return;
            if (lod == Lod0 && lod0Count < lod0Matrices.Length)
            {
                lod0Matrices[lod0Count] = instance.Matrix;
                lod0Tints[lod0Count] = instance.Tint;
                lod0Fades[lod0Count] = fade;
                lod0Frames[lod0Count] = 0f;
                lod0Count++;
                return;
            }
            if (lod == Lod1 && lod1Count < lod1Matrices.Length)
            {
                lod1Matrices[lod1Count] = instance.Matrix;
                lod1Tints[lod1Count] = instance.Tint;
                lod1Fades[lod1Count] = fade;
                lod1Frames[lod1Count] = 0f;
                lod1Count++;
                return;
            }
            if (lod != Lod2 || lod2Count >= lod2Matrices.Length)
                return;
            Vector3 facing = cameraPosition - instance.Position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.001f)
                facing = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            lod2Matrices[lod2Count] = Matrix4x4.TRS(
                instance.Position,
                rotation,
                instance.Scale);
            lod2Tints[lod2Count] = instance.Tint;
            lod2Fades[lod2Count] = fade;
            lod2Frames[lod2Count] = SelectImpostorFrame(index, facing);
            lod2Count++;
        }

        private float SelectImpostorFrame(int index, Vector3 facing)
        {
            float viewAngle = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg -
                runtimeInstances[index].RotationDegrees;
            byte current = impostorFrames[index];
            float center = current * (360f / ImpostorFrameCount);
            float halfCell = 180f / ImpostorFrameCount;
            if (Mathf.Abs(Mathf.DeltaAngle(center, viewAngle)) >
                halfCell + impostorAngularHysteresisDegrees)
            {
                int frame = Mathf.RoundToInt(
                    Mathf.Repeat(viewAngle, 360f) / (360f / ImpostorFrameCount));
                current = (byte)(frame % ImpostorFrameCount);
                impostorFrames[index] = current;
            }
            return current;
        }

        private void DrawTier(
            Mesh mesh,
            Matrix4x4[] matrices,
            Vector4[] tints,
            float[] fades,
            float[] frames,
            int count,
            MaterialPropertyBlock properties,
            Camera camera,
            int layer)
        {
            if (count <= 0)
                return;
            properties.SetVectorArray(InstanceTintId, tints);
            properties.SetFloatArray(InstanceFadeId, fades);
            properties.SetFloatArray(ImpostorFrameId, frames);
            Graphics.DrawMeshInstanced(
                mesh,
                0,
                sharedCanopyMaterial,
                matrices,
                count,
                properties,
                ShadowCastingMode.Off,
                false,
                layer,
                camera,
                LightProbeUsage.Off,
                null);
        }
    }
}
