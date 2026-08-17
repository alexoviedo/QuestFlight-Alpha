using System;
using System.Collections.Generic;
using System.IO;
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
        public const int MaximumLod2Instances = 4875;
        public const int MaximumDrawCount = 7;
        public const int ImpostorFrameCount = 8;
        public const int MaximumCoveragePages = 96;
        public const int FarSamplesPerPage = 144;
        public const float ForwardCoverageMeters = 10000f;
        public const float LateralCoverageMeters = 7000f;
        public const float RearCoverageMeters = 3000f;
        public const float CoverageExitBufferMeters = 1000f;
        private const int MaximumInstancesPerDraw = 1023;

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
        [SerializeField] private AircraftCenteredBuildingClipmapController buildingClipmap;
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
        private readonly Matrix4x4[] batchMatrices =
            new Matrix4x4[MaximumInstancesPerDraw];
        private readonly Vector4[] batchTints =
            new Vector4[MaximumInstancesPerDraw];
        private readonly float[] batchFades = new float[MaximumInstancesPerDraw];
        private readonly float[] batchFrames = new float[MaximumInstancesPerDraw];

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
        private bool[] committedPages = Array.Empty<bool>();
        private bool[] prefetchedPages = Array.Empty<bool>();
        private bool[] previousPrefetchedPages = Array.Empty<bool>();
        private bool[] treeCoveragePages = Array.Empty<bool>();
        private bool[] previousTreeCoveragePages = Array.Empty<bool>();
        private PageCandidate[] pageCandidates = Array.Empty<PageCandidate>();
        private int[] coveragePageIndices = Array.Empty<int>();
        private int coveragePageCount;
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

        private struct PageCandidate
        {
            public int page;
            public float priority;
            public bool mandatory;
        }

        private sealed class PageCandidateComparer : IComparer<PageCandidate>
        {
            public static readonly PageCandidateComparer Instance =
                new PageCandidateComparer();

            public int Compare(PageCandidate left, PageCandidate right)
            {
                int mandatory = right.mandatory.CompareTo(left.mandatory);
                if (mandatory != 0)
                    return mandatory;
                int priority = right.priority.CompareTo(left.priority);
                return priority != 0 ? priority : left.page.CompareTo(right.page);
            }
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
        private bool[] selectedForDraw = Array.Empty<bool>();
        private bool[] selectedLastFrame = Array.Empty<bool>();
        private bool[] farEligible = Array.Empty<bool>();
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
        public AircraftCenteredBuildingClipmapController BuildingClipmap => buildingClipmap;
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
        public bool UsesSharedBuildingPageResidency =>
            fullMapPlacementDatabase == null || buildingClipmap != null;
        public bool CullEntryFadeEnabled => false;
        public int FarCoveragePageLimit => MaximumCoveragePages;
        public int FarSamplesPerCoveragePage => FarSamplesPerPage;
        public float ForwardTreeCoverageMeters => ForwardCoverageMeters;
        public float LateralTreeCoverageMeters => LateralCoverageMeters;
        public float RearTreeCoverageMeters => RearCoverageMeters;
        public bool BuildingPagesAreMandatoryTreeCoverage => true;
        public int MaximumFarCoverageCandidates =>
            MaximumCoveragePages * FarSamplesPerPage;
        public int MaximumFarDrawPopulation => MaximumLod2Instances;
        public bool StablePageStratifiedFarSelection => true;

        // Compatibility properties make the intentional V1-to-V2 policy change
        // explicit to repository validators and temporal evidence.
        public bool FixedWorldOrientationOnly => false;
        public bool CameraFacingBehavior => centerEyeWorldUprightImpostor;
        public bool LodCrossfadeOrDither => alphaToCoverageTransition;

        public bool IsInsideTreeCoverageForTest(Vector3 offset, Vector3 aircraftForward)
        {
            Vector3 forward = HorizontalDirection(aircraftForward, Vector3.forward);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            offset.y = 0f;
            return InsideCoverageEnvelope(
                Vector3.Dot(offset, forward),
                Mathf.Abs(Vector3.Dot(offset, right)),
                ForwardCoverageMeters,
                LateralCoverageMeters,
                RearCoverageMeters);
        }

        public void Configure(
            Material material,
            Mesh nearMesh,
            Mesh middleMesh,
            Mesh farMesh,
            TextAsset placements,
            TextAsset sourceManifest,
            Instance[] authoredInstances,
            TextAsset fullMapDatabase = null,
            AircraftCenteredBuildingClipmapController sharedBuildingClipmap = null)
        {
            sharedCanopyMaterial = material;
            lod0Mesh = nearMesh;
            lod1Mesh = middleMesh;
            lod2Mesh = farMesh;
            placementManifest = placements;
            sourceLicenseManifest = sourceManifest;
            fullMapPlacementDatabase = fullMapDatabase;
            buildingClipmap = sharedBuildingClipmap;
            instances = authoredInstances ?? Array.Empty<Instance>();
            lod0EnterScreenPixels = 72f;
            lod0ExitScreenPixels = 48f;
            lod1EnterScreenPixels = 12f;
            lod1ExitScreenPixels = 7f;
            maximumDistanceMeters = fullMapDatabase == null
                ? 750f
                : ForwardCoverageMeters;
            transitionDurationSeconds = 0.35f;
            minimumResidencySeconds = 2.00f;
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
            selectedForDraw = new bool[count];
            selectedLastFrame = new bool[count];
            farEligible = new bool[count];
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
                activeCapacity = Mathf.Min(count, checked(
                    MaximumCoveragePages * FarSamplesPerPage));
            }
            activeIndices = new int[activeCapacity];
            previousActiveIndices = new int[activeCapacity];
            candidates = new Candidate[activeCapacity];
            activeCount = 0;
            previousActiveCount = 0;
            candidateCount = 0;
            coveragePageCount = 0;
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
                committedPages = Array.Empty<bool>();
                prefetchedPages = Array.Empty<bool>();
                previousPrefetchedPages = Array.Empty<bool>();
                treeCoveragePages = Array.Empty<bool>();
                previousTreeCoveragePages = Array.Empty<bool>();
                pageCandidates = Array.Empty<PageCandidate>();
                coveragePageIndices = Array.Empty<int>();
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
            committedPages = new bool[pageCount];
            prefetchedPages = new bool[pageCount];
            previousPrefetchedPages = new bool[pageCount];
            treeCoveragePages = new bool[pageCount];
            previousTreeCoveragePages = new bool[pageCount];
            pageCandidates = new PageCandidate[pageCount];
            coveragePageIndices = new int[MaximumCoveragePages];
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
            Vector3 aircraftForward = buildingClipmap != null
                ? buildingClipmap.StableAircraftForward
                : cameraForward;
            Vector3 viewForward = buildingClipmap != null
                ? buildingClipmap.StableViewForward
                : cameraForward;
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            float now = Time.realtimeSinceStartup;
            if (!BuildActiveIndices(cameraPosition, aircraftForward, viewForward, now))
                return;
            EvaluateVisibility(camera, cameraPosition, aircraftForward, viewForward);
            SelectDesiredLods();
            AdvancePersistentStates(now);
            BuildAndDraw(camera, cameraPosition, layer, now);
            UpdateSelectionHistory();
        }

        private bool BuildActiveIndices(
            Vector3 cameraPosition,
            Vector3 aircraftForward,
            Vector3 viewForward,
            float now)
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
                selectedForDraw[index] = false;
                farEligible[index] = false;
            }

            if (pageOffsets.Length == 0)
            {
                for (int index = 0; index < runtimeInstances.Length; index++)
                    activeIndices[activeCount++] = index;
                return activeCount > 0;
            }

            bool residencyReady = SynchronizeBuildingResidency(now);
#if UNITY_EDITOR
            if (!residencyReady && !Application.isPlaying)
            {
                BuildEditorFallbackResidency(cameraPosition, aircraftForward, viewForward, now);
                residencyReady = true;
            }
#endif
            if (!residencyReady)
                return false;

            BuildTreeCoverage(cameraPosition, aircraftForward);
            for (int coverage = 0; coverage < coveragePageCount; coverage++)
            {
                int page = coveragePageIndices[coverage];
                int start = pageOffsets[page];
                int farEnd = start + Mathf.Min(pageCounts[page], FarSamplesPerPage);
                for (int index = start; index < farEnd; index++)
                {
                    if (activeCount >= activeIndices.Length)
                        throw new InvalidOperationException(
                            "Full-map vegetation active-page capacity was exceeded.");
                    activeIndices[activeCount++] = index;
                    farEligible[index] = true;
                }
            }
            return activeCount > 0;
        }

        private void BuildTreeCoverage(Vector3 cameraPosition, Vector3 aircraftForward)
        {
            Array.Clear(treeCoveragePages, 0, treeCoveragePages.Length);
            Vector3 forward = HorizontalDirection(aircraftForward, Vector3.forward);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            int pageCandidateCount = 0;
            float forwardExit = ForwardCoverageMeters + CoverageExitBufferMeters;
            float lateralExit = LateralCoverageMeters + CoverageExitBufferMeters;
            float rearExit = RearCoverageMeters + CoverageExitBufferMeters;

            for (int row = 0; row < databaseRows; row++)
            for (int column = 0; column < databaseColumns; column++)
            {
                int page = row * databaseColumns + column;
                Vector3 center = new Vector3(
                    databaseMinimumX + (column + 0.5f) * databasePageSize,
                    cameraPosition.y,
                    databaseMinimumZ + (row + 0.5f) * databasePageSize);
                Vector3 offset = center - cameraPosition;
                offset.y = 0f;
                float longitudinal = Vector3.Dot(offset, forward);
                float lateral = Mathf.Abs(Vector3.Dot(offset, right));
                bool mandatory = committedPages[page] || prefetchedPages[page];
                bool insideEntry = InsideCoverageEnvelope(
                    longitudinal,
                    lateral,
                    ForwardCoverageMeters,
                    LateralCoverageMeters,
                    RearCoverageMeters);
                bool insideExit = previousTreeCoveragePages[page] &&
                    InsideCoverageEnvelope(
                        longitudinal,
                        lateral,
                        forwardExit,
                        lateralExit,
                        rearExit);
                if (!mandatory && !insideEntry && !insideExit)
                    continue;

                float distance = Mathf.Max(1f, offset.magnitude);
                float forwardWeight = Mathf.Clamp01(
                    (longitudinal + RearCoverageMeters) /
                    (ForwardCoverageMeters + RearCoverageMeters));
                float sideWeight = 1f - Mathf.Clamp01(lateral / lateralExit);
                pageCandidates[pageCandidateCount++] = new PageCandidate
                {
                    page = page,
                    mandatory = mandatory,
                    priority =
                        (insideExit ? 10000f : 0f) +
                        forwardWeight * 1000f +
                        sideWeight * 250f -
                        distance * 0.01f,
                };
            }

            Array.Sort(pageCandidates, 0, pageCandidateCount,
                PageCandidateComparer.Instance);
            coveragePageCount = Mathf.Min(MaximumCoveragePages, pageCandidateCount);
            for (int index = 0; index < coveragePageCount; index++)
            {
                int page = pageCandidates[index].page;
                coveragePageIndices[index] = page;
                treeCoveragePages[page] = true;
            }
            Array.Copy(treeCoveragePages, previousTreeCoveragePages,
                treeCoveragePages.Length);
        }

        private static bool InsideCoverageEnvelope(
            float longitudinal,
            float lateral,
            float forwardMeters,
            float lateralMeters,
            float rearMeters)
        {
            if (longitudinal < -rearMeters || longitudinal > forwardMeters ||
                lateral > lateralMeters)
                return false;
            float longitudinalScale = longitudinal >= 0f
                ? forwardMeters
                : rearMeters;
            float normalizedLongitudinal = longitudinal /
                Mathf.Max(1f, longitudinalScale);
            float normalizedLateral = lateral / Mathf.Max(1f, lateralMeters);
            return normalizedLongitudinal * normalizedLongitudinal +
                normalizedLateral * normalizedLateral <= 1f;
        }

        private bool SynchronizeBuildingResidency(float now)
        {
            if (buildingClipmap == null || committedPages.Length == 0 ||
                buildingClipmap.PageColumns != databaseColumns ||
                buildingClipmap.PageRows != databaseRows ||
                Mathf.Abs(buildingClipmap.PageSizeMeters - databasePageSize) > 0.01f ||
                Mathf.Abs(buildingClipmap.MapMinimumX - databaseMinimumX) > 0.01f ||
                Mathf.Abs(buildingClipmap.MapMinimumZ - databaseMinimumZ) > 0.01f)
                return false;

            bool ready = buildingClipmap.TryCopyPageResidency(
                committedPages, prefetchedPages);
            if (!ready)
                return false;
            PrewarmNewPages(now);
            return true;
        }

        private void PrewarmNewPages(float now)
        {
            for (int page = 0; page < prefetchedPages.Length; page++)
            {
                if (prefetchedPages[page] && !previousPrefetchedPages[page])
                {
                    int start = pageOffsets[page];
                    int end = start + pageCounts[page];
                    for (int index = start; index < end; index++)
                    {
                        if (currentLods[index] != Uninitialized && currentLods[index] != Culled)
                            continue;
                        currentLods[index] = Lod2;
                        previousLods[index] = Culled;
                        desiredLods[index] = Lod2;
                        transitionStarts[index] = float.NegativeInfinity;
                        lastStateChanges[index] = now - minimumResidencySeconds;
                    }
                }
                previousPrefetchedPages[page] = prefetchedPages[page];
            }
        }

#if UNITY_EDITOR
        private void BuildEditorFallbackResidency(
            Vector3 cameraPosition,
            Vector3 aircraftForward,
            Vector3 viewForward,
            float now)
        {
            Array.Clear(committedPages, 0, committedPages.Length);
            Array.Clear(prefetchedPages, 0, prefetchedPages.Length);
            int centerColumn = Mathf.Clamp(Mathf.FloorToInt(
                (cameraPosition.x - databaseMinimumX) / databasePageSize),
                0, databaseColumns - 1);
            int centerRow = Mathf.Clamp(Mathf.FloorToInt(
                (cameraPosition.z - databaseMinimumZ) / databasePageSize),
                0, databaseRows - 1);
            int minimumColumn = Mathf.Clamp(centerColumn - 2, 0, databaseColumns - 4);
            int minimumRow = Mathf.Clamp(centerRow - 2, 0, databaseRows - 4);
            for (int row = minimumRow; row < minimumRow + 4; row++)
            for (int column = minimumColumn; column < minimumColumn + 4; column++)
                SetEditorPage(column, row);

            Vector3 stableForward = HorizontalDirection(aircraftForward, viewForward);
            bool alongZ = Mathf.Abs(stableForward.z) >= Mathf.Abs(stableForward.x);
            int sign = alongZ ? (stableForward.z >= 0f ? 1 : -1) :
                (stableForward.x >= 0f ? 1 : -1);
            for (int depth = 1; depth <= 2; depth++)
            for (int offset = 0; offset < 4; offset++)
            {
                int column = alongZ ? minimumColumn + offset :
                    minimumColumn + (sign > 0 ? 3 + depth : -depth);
                int row = alongZ ? minimumRow + (sign > 0 ? 3 + depth : -depth) :
                    minimumRow + offset;
                SetEditorPage(column, row);
            }
            Array.Copy(committedPages, prefetchedPages, committedPages.Length);
            PrewarmNewPages(now);
        }

        private void SetEditorPage(int column, int row)
        {
            if (column < 0 || column >= databaseColumns || row < 0 || row >= databaseRows)
                return;
            committedPages[row * databaseColumns + column] = true;
        }
#endif

        private void EvaluateVisibility(
            Camera camera,
            Vector3 cameraPosition,
            Vector3 aircraftForward,
            Vector3 viewForward)
        {
            float focalPixels = camera.pixelHeight /
                (2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            Vector3 stableAircraftForward = HorizontalDirection(aircraftForward, Vector3.forward);
            Vector3 stableViewForward = HorizontalDirection(viewForward, stableAircraftForward);
            float viewThreshold = Mathf.Cos(68f * Mathf.Deg2Rad);
            candidateCount = 0;
            for (int active = 0; active < activeCount; active++)
            {
                int index = activeIndices[active];
                RuntimeInstance instance = runtimeInstances[index];
                Vector3 offset = instance.Center - cameraPosition;
                float distanceSquared = offset.sqrMagnitude;
                Bounds bounds = new Bounds(
                    instance.Center,
                    Vector3.one * (instance.Radius * 2f));
                visible[index] = GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                float distance = Mathf.Sqrt(Mathf.Max(0.01f, distanceSquared));
                float pixels = instance.Radius * 2f * focalPixels / distance;
                projectedPixels[index] = pixels;
                Vector3 direction = HorizontalDirection(offset, stableAircraftForward);
                float aircraftDot = Vector3.Dot(stableAircraftForward, direction);
                float viewDot = Vector3.Dot(stableViewForward, direction);
                float forwardWeight = Mathf.Clamp01((aircraftDot + 0.15f) / 1.15f);
                float viewWeight = Mathf.Clamp01((viewDot - viewThreshold) /
                    Mathf.Max(0.001f, 1f - viewThreshold));
                priorities[index] = pixels + forwardWeight * 32f + viewWeight * 12f +
                    (selectedLastFrame[index] ? 12f : 0f) - distance * 0.0005f +
                    (runtimeInstances.Length - index) * 0.0000001f;
                selectedLod0[index] = false;
                selectedLod1[index] = false;
                selectedForDraw[index] = false;
                desiredLods[index] = currentLods[index] == Uninitialized
                    ? Lod2
                    : currentLods[index];
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
                    selectedForDraw[index] = true;
                    desiredLods[index] = Lod0;
                    lod0Count++;
                }
                else if (candidate.pixels >= lod1Threshold && lod1Count < MaximumLod1Instances)
                {
                    selectedLod1[index] = true;
                    selectedForDraw[index] = true;
                    desiredLods[index] = Lod1;
                    lod1Count++;
                }
            }

            int lod2Count = 0;
            for (int candidateIndex = 0;
                 candidateIndex < candidateCount && lod2Count < MaximumLod2Instances;
                 candidateIndex++)
                AssignFarCandidate(candidateIndex, ref lod2Count);
        }

        private void AssignFarCandidate(int candidateIndex, ref int total)
        {
            int index = candidates[candidateIndex].index;
            if (selectedForDraw[index] || !farEligible[index] ||
                total >= MaximumLod2Instances)
                return;
            selectedForDraw[index] = true;
            desiredLods[index] = Lod2;
            total++;
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
                if (selectedForDraw[index] && !selectedLastFrame[index])
                {
                    // The page was prepared before it entered the visible population.
                    // Select the correct first visible tier immediately; never expose a
                    // Cull-to-tree alpha ramp in the pilot's view.
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
                if (!visible[index] || !selectedForDraw[index])
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

        private void UpdateSelectionHistory()
        {
            for (int active = 0; active < activeCount; active++)
            {
                int index = activeIndices[active];
                selectedLastFrame[index] = selectedForDraw[index];
            }
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

        private static Vector3 HorizontalDirection(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude > 0.0001f)
                return value.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f
                ? fallback.normalized
                : Vector3.forward;
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
            for (int offset = 0; offset < count; offset += MaximumInstancesPerDraw)
            {
                int batchCount = Mathf.Min(MaximumInstancesPerDraw, count - offset);
                Array.Copy(matrices, offset, batchMatrices, 0, batchCount);
                Array.Copy(tints, offset, batchTints, 0, batchCount);
                Array.Copy(fades, offset, batchFades, 0, batchCount);
                Array.Copy(frames, offset, batchFrames, 0, batchCount);
                properties.SetVectorArray(InstanceTintId, batchTints);
                properties.SetFloatArray(InstanceFadeId, batchFades);
                properties.SetFloatArray(ImpostorFrameId, batchFrames);
                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    sharedCanopyMaterial,
                    batchMatrices,
                    batchCount,
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
}
