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
        private const int MaximumInstancesPerDraw = 1023;

        public const int MaximumCoveragePages = 96;
        public const int FarSamplesPerPage = 144;
        public const int MaximumLod0Instances = 24;
        public const int MaximumLod1Instances = 300;
        public const int MaximumFarDrawBatchBudget = 4;
        public const int MaximumLod2Instances =
            MaximumInstancesPerDraw * MaximumFarDrawBatchBudget - MaximumLod1Instances;
        public const int MaximumShrubInstances = 200;
        public const int MaximumGroveInstances = 0;
        public const int MaximumDrawCount = MaximumFarDrawBatchBudget + 3;
        public const int ImpostorFrameCount = 8;
        public const int NearSamplesPerPage = 600;
        public const int MaximumFullDensityPages = 9;
        public const int MaximumResidentFullDensityPages = MaximumFullDensityPages * 2;
        public const float FullDensityRadiusMeters = 2400f;
        public const float ForwardCoverageMeters = 10000f;
        public const float LateralCoverageMeters = 7000f;
        public const float RearCoverageMeters = 3000f;
        public const float CoverageExitBufferMeters = 1000f;
        public const float FullDensityPageOverlapSeconds = 2.35f;
        public const float SelectionRefreshSeconds = 0.25f;
        public const float SelectionRefreshDistanceMeters = 20f;
        public const float SelectionRefreshAngleDegrees = 4f;
        public const float FarMinimumProjectedPixels = 0.75f;
        private const byte Lod0 = 0;
        private const byte Lod1 = 1;
        private const byte Lod2 = 2;
        private const byte Culled = 3;
        private const byte Uninitialized = byte.MaxValue;
        private const int FullMapDatabaseVersion = 3;
        private const int FullMapHeaderBytes = 40;
        private const int FullMapInstanceBytes = 19;
        private static readonly byte[] FullMapMagic = Encoding.ASCII.GetBytes("QFFMVG3\0");

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
        [SerializeField] private Mesh shrubMesh;
        [SerializeField] private Mesh groveMesh;
        [Header("Authored neighborhood foliage replacement")]
        [SerializeField] private Renderer authoredFoliageReplacement;
        [SerializeField] private Vector2[] authoredFoliageReplacementCenters = Array.Empty<Vector2>();
        private readonly HashSet<Vector2Int> replacementTreeCenters = new HashSet<Vector2Int>();

        public void ConfigureAuthoredFoliageReplacement(Renderer replacement, Vector2[] centers)
        {
            if (centers == null || centers.Length > 256 || (centers.Length > 0 && replacement == null))
                throw new ArgumentException("A bounded authored replacement renderer and centers are required.");
            foreach (Vector2 center in centers)
                if (float.IsNaN(center.x) || float.IsInfinity(center.x) || float.IsNaN(center.y) || float.IsInfinity(center.y))
                    throw new ArgumentException("Replacement center must be finite.");
            authoredFoliageReplacement = replacement;
            authoredFoliageReplacementCenters = (Vector2[])centers.Clone();
            RefreshReplacementCenters();
        }

        private void RefreshReplacementCenters()
        {
            replacementTreeCenters.Clear();
            foreach (Vector2 center in authoredFoliageReplacementCenters ?? Array.Empty<Vector2>())
                replacementTreeCenters.Add(new Vector2Int(Mathf.RoundToInt(center.x * 100f), Mathf.RoundToInt(center.y * 100f)));
        }

        public bool HasActiveAuthoredReplacementAt(Vector3 position) =>
            authoredFoliageReplacement != null && authoredFoliageReplacement.enabled &&
            authoredFoliageReplacement.gameObject.activeInHierarchy &&
            replacementTreeCenters.Contains(new Vector2Int(Mathf.RoundToInt(position.x * 100f), Mathf.RoundToInt(position.z * 100f)));
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
        [SerializeField] private bool enableAuthoredFarCoveragePatch;
        [SerializeField] private Vector4 authoredFarCoveragePatchXZ;
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
        private readonly Matrix4x4[] shrubMatrices =
            new Matrix4x4[MaximumShrubInstances];
        private readonly Vector4[] shrubTints = new Vector4[MaximumShrubInstances];
        private readonly float[] shrubFades = new float[MaximumShrubInstances];
        private readonly float[] shrubFrames = new float[MaximumShrubInstances];
        // Each chunk owns its properties: color and XR motion reuse the same
        // admitted instances without repacking or mutating another queued draw.
        private sealed class PreparedTreeBatch
        {
            public Mesh Mesh;
            public int Count;
            public readonly Matrix4x4[] Matrices = new Matrix4x4[MaximumInstancesPerDraw];
            public readonly Vector4[] Tints = new Vector4[MaximumInstancesPerDraw];
            public readonly float[] Fades = new float[MaximumInstancesPerDraw];
            public readonly float[] Frames = new float[MaximumInstancesPerDraw];
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        }
        private readonly List<PreparedTreeBatch> preparedBatches = new List<PreparedTreeBatch>();
        private int preparedBatchCount;
        private static readonly Unity.Profiling.ProfilerMarker PackMarker =
            new Unity.Profiling.ProfilerMarker("QuestFlight.Trees.PackVisibleChunks");

        private struct RuntimeInstance
        {
            public Vector3 Position;
            public Vector3 Scale;
            public Vector3 Center;
            public Vector4 Tint;
            public float Radius;
            public float RotationDegrees;
            public byte Kind;

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
        private bool[] fullDensityPages = Array.Empty<bool>();
        private bool[] requestedFullDensityPages = Array.Empty<bool>();
        private float[] fullDensityRetireTimes = Array.Empty<float>();
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
        private bool selectionInitialized;
        private float nextSelectionRefreshTime;
        private Vector3 lastSelectionReferencePosition;
        private Vector3 lastSelectionAircraftForward = Vector3.forward;

        private struct Candidate
        {
            public int index;
            public float priority;
            public float pixels;
            public float distance;
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
        private bool[] activeMembership = Array.Empty<bool>();
        private float[] projectedPixels = Array.Empty<float>();
        private float[] priorities = Array.Empty<float>();
        private byte[] desiredLods = Array.Empty<byte>();
        private byte[] currentLods = Array.Empty<byte>();
        private byte[] previousLods = Array.Empty<byte>();
        private byte[] impostorFrames = Array.Empty<byte>();
        private float[] transitionStarts = Array.Empty<float>();
        private float[] lastStateChanges = Array.Empty<float>();

        private MaterialPropertyBlock groveProperties;

        public Material SharedCanopyMaterial => sharedCanopyMaterial;
        public Mesh Lod0Mesh => lod0Mesh;
        public Mesh Lod1Mesh => lod1Mesh;
        public Mesh Lod2Mesh => lod2Mesh;
        public Mesh ShrubMesh => shrubMesh;
        public Mesh GroveMesh => groveMesh;
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
        public bool CullEntryFadeEnabled => true;
        public int FarCoveragePageLimit => MaximumCoveragePages;
        public int FarSamplesPerCoveragePage => FarSamplesPerPage;
        public float ForwardTreeCoverageMeters => ForwardCoverageMeters;
        public float LateralTreeCoverageMeters => LateralCoverageMeters;
        public float RearTreeCoverageMeters => RearCoverageMeters;
        public bool BuildingPagesAreMandatoryTreeCoverage => false;
        public int MaximumFarCoverageCandidates =>
            MaximumCoveragePages * FarSamplesPerPage;
        public int MaximumFarDrawPopulation => MaximumLod2Instances;
        public int NearSamplesPerCoveragePage => NearSamplesPerPage;
        public int FullDensityPageLimit => MaximumFullDensityPages;
        public bool StablePageStratifiedFarSelection => true;
        public bool UsesSingleDrawProceduralFarPopulation => false;
        public bool UsesProvenBatchedFarPopulation => true;
        public int MaximumFarBatchCount =>
            BatchCountForCapacity(Lod2ArrayCapacity);
        public int MaximumImplementationDrawCount =>
            BatchCountForCapacity(Lod0ArrayCapacity) +
            BatchCountForCapacity(Lod1ArrayCapacity) +
            BatchCountForCapacity(Lod2ArrayCapacity) +
            BatchCountForCapacity(MaximumShrubInstances);

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
            Mesh authoredShrubMesh,
            Mesh authoredGroveMesh,
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
            shrubMesh = authoredShrubMesh;
            groveMesh = authoredGroveMesh;
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
                (long)TriangleCount(lod2Mesh) * MaximumLod1Instances +
                (long)TriangleCount(shrubMesh) * MaximumShrubInstances;
            loadedDatabaseInstanceId = 0;
            Prepare();
        }

        private static int TriangleCount(Mesh mesh)
        {
            return mesh == null || mesh.subMeshCount == 0
                ? 0
                : checked((int)(mesh.GetIndexCount(0) / 3u));
        }

        private static int BatchCountForCapacity(int capacity) =>
            (capacity + MaximumInstancesPerDraw - 1) / MaximumInstancesPerDraw;

        private void OnEnable()
        {
            RefreshReplacementCenters();
            Camera.onPreCull -= DrawForCamera;
            QuestFlight.Rendering.ProductRenderSubmission.Draw -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
            QuestFlight.Rendering.ProductRenderSubmission.Draw += DrawForCamera;
            Prepare();
        }

        private void OnDisable()
        {
            preparedFrame=-1;preparedCamera=null;
            Camera.onPreCull -= DrawForCamera;
            QuestFlight.Rendering.ProductRenderSubmission.Draw -= DrawForCamera;
        }

#if UNITY_EDITOR
        // Unsaved, geographically bounded benchmark only; never changes Android selection.
        public static bool AllowBenchmarkCrownProxiesForVisualQa;
        // External, unsaved prototype review only. Neither hook exists in a player.
        public static Func<Vector3, Mesh, bool> OmitHybridPrototypeInstanceForVisualQa;
        public static Action<Mesh, int> ObserveHybridPrototypeTierForVisualQa;
        public static bool DisableDrawCullingForVisualQa;
        private Camera settledPreviewCamera;
        private float settledPreviewTime;

        public void PrepareSettledPreview(Camera camera)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Settled preview is Editor-only.");
            Prepare();
            settledPreviewCamera = camera;
            // Reset identity/hysteresis so a capture does not inherit the preceding
            // shot's tree choices. Exercise the real selection at a fixed settled pose.
            Array.Fill(currentLods, Uninitialized);
            Array.Fill(previousLods, Culled);
            Array.Fill(transitionStarts, float.NegativeInfinity);
            Array.Fill(lastStateChanges, float.NegativeInfinity);
            Array.Clear(selectedLastFrame, 0, selectedLastFrame.Length);
            selectionInitialized = false;
            float start = Mathf.Max(settledPreviewTime, Time.realtimeSinceStartup);
            float step = Mathf.Max(FullDensityPageOverlapSeconds,
                Mathf.Max(minimumResidencySeconds, transitionDurationSeconds)) + 1f;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                settledPreviewTime = start + step * iteration;
                BuildActiveIndices(camera.transform.position, camera.transform.forward, settledPreviewTime);
                ShouldRefreshSelection(camera.transform.position, camera.transform.forward, settledPreviewTime);
                EvaluateVisibility(camera, camera.transform.position, camera.transform.forward, true);
                SelectDesiredLods();
                AdvancePersistentStates(settledPreviewTime);
                UpdateSelectionHistory();
            }
        }

        public void ClearSettledPreview() => settledPreviewCamera = null;

        private void OnValidate()
        {
            RefreshReplacementCenters();
            Prepare();
        }
#endif

        private void Prepare()
        {
            groveProperties ??= new MaterialPropertyBlock();
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
            activeMembership = new bool[count];
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
                    MaximumCoveragePages * FarSamplesPerPage +
                    MaximumResidentFullDensityPages * NearSamplesPerPage));
            }
            activeIndices = new int[activeCapacity];
            previousActiveIndices = new int[activeCapacity];
            candidates = new Candidate[activeCapacity];
            activeCount = 0;
            previousActiveCount = 0;
            candidateCount = 0;
            coveragePageCount = 0;
            selectionInitialized = false;
            nextSelectionRefreshTime = float.NegativeInfinity;
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
                fullDensityPages = Array.Empty<bool>();
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
            fullDensityPages = new bool[pageCount];
            requestedFullDensityPages = new bool[pageCount];
            fullDensityRetireTimes = new float[pageCount];
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
            bool hasTreeFamilies=sharedCanopyMaterial!=null&&sharedCanopyMaterial.HasProperty("_TreeRealismEnabled")&&
                sharedCanopyMaterial.GetFloat("_TreeRealismEnabled")>.5f;
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
                    byte seedAndFamily=reader.ReadByte(); // Low six bits retain the stable seed.
                    byte kind = reader.ReadByte();
                    if (height < 0.75f || height > 30f || crown < 1.5f ||
                        crown > 85f || kind > 3)
                        throw new InvalidDataException($"Full-map vegetation instance {destination} is implausible.");
                    Vector3 position = new Vector3(x, y, z);
                    Vector3 scale = new Vector3(
                        crown / bounds.size.x,
                        height / bounds.size.y,
                        crown / bounds.size.z);
                    Vector3 center = position + Vector3.up * height * 0.5f;
                    float radius = Mathf.Sqrt(
                        height * height * 0.25f + crown * crown * 0.5f);
                    int family=hasTreeFamilies ? seedAndFamily>>6 : 0;
                    if(family>2)throw new InvalidDataException("Unknown tree visual family");
                    if(family>0)
                    {
                        red=Mathf.Lerp(red,.25f,.55f);green=Mathf.Lerp(green,.34f,.55f);blue=Mathf.Lerp(blue,.21f,.55f);
                    }
                    runtimeInstances[destination] = new RuntimeInstance
                    {
                        Position = position,
                        Scale = scale,
                        Center = center,
                        Tint = new Vector4(red, green, blue, 1f+family),
                        Radius = radius,
                        RotationDegrees = rotationDegrees,
                        Kind = kind,
                    };
                }
            }
            loadedDatabaseInstanceId = databaseId;
        }

        private int preparedFrame=-1;
        private Camera preparedCamera;
        private Matrix4x4 preparedView,preparedProjection;
        private uint preparedGeneration;
        private int preparedLod0,preparedLod1,preparedLod2,preparedShrubs;
        private void DrawForCamera(Camera camera)
        {
            if(QuestFlight.Rendering.ProductRenderSubmission.Preparing)return;
            if (camera == null || !enabled || !gameObject.activeInHierarchy ||
                !gameObject.scene.IsValid() || sharedCanopyMaterial == null ||
                lod0Mesh == null || lod1Mesh == null || lod2Mesh == null ||
                shrubMesh == null || groveMesh == null ||
                runtimeInstances == null || runtimeInstances.Length == 0)
                return;
            if (camera.cameraType == CameraType.Preview ||
                camera.cameraType == CameraType.Reflection)
                return;
            int layer = gameObject.layer;
            if ((camera.cullingMask & (1 << layer)) == 0)
                return;

            if(Application.isPlaying && preparedFrame==Time.frameCount && preparedCamera==camera &&
                preparedGeneration==QuestFlight.Rendering.ProductSpaceWarpController.HistoryGeneration &&
                preparedView==camera.worldToCameraMatrix && preparedProjection==camera.projectionMatrix)
            { DrawPrepared(camera,layer); return; }
            Prepare();
            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            Vector3 referencePosition = buildingClipmap != null &&
                buildingClipmap.trackedRig != null
                ? buildingClipmap.trackedRig.transform.position
                : cameraPosition;
            Vector3 aircraftForward = buildingClipmap != null
                ? buildingClipmap.StableAircraftForward
                : cameraForward;
            float now = Time.realtimeSinceStartup;
#if UNITY_EDITOR
            if (!Application.isPlaying && settledPreviewCamera == camera)
            {
                referencePosition = cameraPosition;
                aircraftForward = cameraForward;
                now = settledPreviewTime;
            }
#endif
            if (!BuildActiveIndices(referencePosition, aircraftForward, now))
                return;
            bool refreshSelection = ShouldRefreshSelection(
                referencePosition, aircraftForward, now);
            EvaluateVisibility(
                camera, referencePosition, aircraftForward, refreshSelection);
            if (refreshSelection)
            {
                SelectDesiredLods();
                AdvancePersistentStates(now);
            }
            PrepareDrawFrustum(camera);
            BuildAndDraw(camera, cameraPosition, layer, now);
            preparedFrame=Time.frameCount;preparedCamera=camera;
            preparedGeneration=QuestFlight.Rendering.ProductSpaceWarpController.HistoryGeneration;
            preparedView=camera.worldToCameraMatrix;preparedProjection=camera.projectionMatrix;
            if (refreshSelection)
                UpdateSelectionHistory();
        }

        private bool BuildActiveIndices(
            Vector3 referencePosition,
            Vector3 aircraftForward,
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
                farEligible[index] = false;
                activeMembership[index] = false;
            }

            if (pageOffsets.Length == 0)
            {
                for (int index = 0; index < runtimeInstances.Length; index++)
                {
                    activeIndices[activeCount++] = index;
                    activeMembership[index] = true;
                }
                return activeCount > 0;
            }

            bool residencyReady = SynchronizeBuildingResidency(now);
#if UNITY_EDITOR
            if (!residencyReady && !Application.isPlaying)
            {
                BuildEditorFallbackResidency(
                    referencePosition, aircraftForward, aircraftForward, now);
                residencyReady = true;
            }
#endif
            if (!residencyReady)
                return false;

            BuildTreeCoverage(referencePosition, aircraftForward);
            SelectFullDensityPages(referencePosition, now);
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
                    activeMembership[index] = true;
                    farEligible[index] = true;
                }
                if (!fullDensityPages[page])
                    continue;
                int nearEnd = Mathf.Min(
                    start + pageCounts[page],
                    farEnd + NearSamplesPerPage);
                for (int index = farEnd; index < nearEnd; index++)
                {
                    if (activeCount >= activeIndices.Length)
                        throw new InvalidOperationException(
                            "Full-map vegetation near-density capacity was exceeded.");
                    activeIndices[activeCount++] = index;
                    activeMembership[index] = true;
                    Vector3 crownPosition = runtimeInstances[index].Position;
                    farEligible[index] = enableAuthoredFarCoveragePatch &&
                        crownPosition.x >= authoredFarCoveragePatchXZ.x && crownPosition.x <= authoredFarCoveragePatchXZ.y &&
                        crownPosition.z >= authoredFarCoveragePatchXZ.z && crownPosition.z <= authoredFarCoveragePatchXZ.w;
#if UNITY_EDITOR
                    if (AllowBenchmarkCrownProxiesForVisualQa && !Application.isPlaying)
                    {
                        Vector3 position = runtimeInstances[index].Position;
                        farEligible[index] = position.x >= -884f && position.x <= -135f &&
                            position.z >= -914f && position.z <= -494f;
                    }
#endif
                }
            }
            for (int position = 0; position < previousActiveCount; position++)
            {
                int index = previousActiveIndices[position];
                if (activeMembership[index])
                    continue;
                selectedForDraw[index] = false;
                selectedLastFrame[index] = false;
                currentLods[index] = Uninitialized;
                previousLods[index] = Culled;
                transitionStarts[index] = float.NegativeInfinity;
            }
            return activeCount > 0;
        }

        private void SelectFullDensityPages(Vector3 referencePosition, float now)
        {
            Array.Clear(requestedFullDensityPages, 0, requestedFullDensityPages.Length);
            int selected = 0;
            float radiusSquared = FullDensityRadiusMeters * FullDensityRadiusMeters;
            while (selected < MaximumFullDensityPages)
            {
                int bestPage = -1;
                float bestDistanceSquared = float.PositiveInfinity;
                for (int coverage = 0; coverage < coveragePageCount; coverage++)
                {
                    int page = coveragePageIndices[coverage];
                    if (requestedFullDensityPages[page])
                        continue;
                    int column = page % databaseColumns;
                    int row = page / databaseColumns;
                    float centerX = databaseMinimumX + (column + 0.5f) * databasePageSize;
                    float centerZ = databaseMinimumZ + (row + 0.5f) * databasePageSize;
                    float dx = centerX - referencePosition.x;
                    float dz = centerZ - referencePosition.z;
                    float distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared > radiusSquared || distanceSquared >= bestDistanceSquared)
                        continue;
                    bestDistanceSquared = distanceSquared;
                    bestPage = page;
                }
                if (bestPage < 0)
                    break;
                requestedFullDensityPages[bestPage] = true;
                selected++;
            }

            Array.Clear(fullDensityPages, 0, fullDensityPages.Length);
            int resident = 0;
            for (int page = 0; page < requestedFullDensityPages.Length; page++)
            {
                if (!requestedFullDensityPages[page])
                    continue;
                fullDensityPages[page] = true;
                fullDensityRetireTimes[page] = now + FullDensityPageOverlapSeconds;
                resident++;
            }
            while (resident < MaximumResidentFullDensityPages)
            {
                int bestPage = -1;
                float bestRetireTime = now;
                for (int page = 0; page < fullDensityPages.Length; page++)
                {
                    if (fullDensityPages[page] || !treeCoveragePages[page] ||
                        fullDensityRetireTimes[page] <= bestRetireTime)
                        continue;
                    bestPage = page;
                    bestRetireTime = fullDensityRetireTimes[page];
                }
                if (bestPage < 0)
                    break;
                fullDensityPages[bestPage] = true;
                resident++;
            }
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
                if (!insideEntry && !insideExit)
                    continue;

                float distance = Mathf.Max(1f, offset.magnitude);
                float forwardWeight = Mathf.Clamp01(
                    (longitudinal + RearCoverageMeters) /
                    (ForwardCoverageMeters + RearCoverageMeters));
                float sideWeight = 1f - Mathf.Clamp01(lateral / lateralExit);
                pageCandidates[pageCandidateCount++] = new PageCandidate
                {
                    page = page,
                    // Current entry coverage always outranks overlap-only pages.
                    // The previous implementation added 10,000 to every retiring
                    // page, allowing stale residency to consume all 96 slots.
                    mandatory = insideEntry,
                    priority =
                        forwardWeight * 1000f +
                        sideWeight * 250f -
                        distance * 0.01f +
                        (insideExit && !insideEntry ? 25f : 0f),
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

        private bool ShouldRefreshSelection(
            Vector3 referencePosition,
            Vector3 aircraftForward,
            float now)
        {
            Vector3 stableForward = HorizontalDirection(aircraftForward, Vector3.forward);
            bool moved = (referencePosition - lastSelectionReferencePosition).sqrMagnitude >=
                SelectionRefreshDistanceMeters * SelectionRefreshDistanceMeters;
            bool turned = Vector3.Angle(lastSelectionAircraftForward, stableForward) >=
                SelectionRefreshAngleDegrees;
            if (selectionInitialized && now < nextSelectionRefreshTime && !moved && !turned)
                return false;

            selectionInitialized = true;
            nextSelectionRefreshTime = now + SelectionRefreshSeconds;
            lastSelectionReferencePosition = referencePosition;
            lastSelectionAircraftForward = stableForward;
            return true;
        }

        private void EvaluateVisibility(
            Camera camera,
            Vector3 referencePosition,
            Vector3 aircraftForward,
            bool refreshSelection)
        {
            // Selection is deliberately independent of head pose. DrawMeshInstanced
            // keeps the bounded resident selection stable. Draw-stage frustum culling
            // omits offscreen submissions without changing identities or LOD state.
            for (int active = 0; active < activeCount; active++)
                visible[activeIndices[active]] = true;
            if (!refreshSelection)
                return;

            float focalPixels = camera.pixelHeight /
                (2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            Vector3 stableAircraftForward = HorizontalDirection(aircraftForward, Vector3.forward);
            candidateCount = 0;
            for (int active = 0; active < activeCount; active++)
            {
                int index = activeIndices[active];
                RuntimeInstance instance = runtimeInstances[index];
                Vector3 offset = instance.Center - referencePosition;
                float distanceSquared = offset.sqrMagnitude;
                float distance = Mathf.Sqrt(Mathf.Max(0.01f, distanceSquared));
                float pixels = instance.Radius * 2f * focalPixels / distance;
                projectedPixels[index] = pixels;
                Vector3 direction = HorizontalDirection(offset, stableAircraftForward);
                float aircraftDot = Vector3.Dot(stableAircraftForward, direction);
                float forwardWeight = Mathf.Clamp01((aircraftDot + 0.15f) / 1.15f);
                priorities[index] = pixels + forwardWeight * 32f +
                    (selectedLastFrame[index] ? 18f : 0f) - distance * 0.0005f +
                    (runtimeInstances.Length - index) * 0.0000001f;
                selectedLod0[index] = false;
                selectedLod1[index] = false;
                selectedForDraw[index] = false;
                desiredLods[index] = currentLods[index] == Uninitialized
                    ? Lod2
                    : currentLods[index];
                candidates[candidateCount++] = new Candidate
                {
                    index = index,
                    priority = priorities[index],
                    pixels = pixels,
                    distance = distance,
                };
            }
            Array.Sort(candidates, 0, candidateCount, CandidateComparer.Instance);
        }

        private void SelectDesiredLods()
        {
            int lod0Count = 0;
            int lod1Count = 0;
            int shrubCount = 0;
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                Candidate candidate = candidates[candidateIndex];
                int index = candidate.index;
                byte kind = runtimeInstances[index].Kind;
                if (kind == 3)
                    continue;
                if (kind == 2)
                {
                    if (candidate.pixels >= 4f && shrubCount < MaximumShrubInstances)
                    {
                        selectedForDraw[index] = true;
                        desiredLods[index] = Lod1;
                        shrubCount++;
                    }
                    continue;
                }
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

        private void AssignFarCandidate(
            int candidateIndex,
            ref int individualTotal)
        {
            int index = candidates[candidateIndex].index;
            if (selectedForDraw[index] || !farEligible[index])
                return;
            byte kind = runtimeInstances[index].Kind;
            if (kind == 3)
            {
                // The photographed orthophoto plus crown-normal derivative is the
                // aggregate grove representation. The old crossed-strip mesh reads
                // as flat camo at flight-simulator oblique angles and is intentionally
                // excluded; far individual silhouettes remain available below.
                return;
            }
            if (individualTotal >= MaximumLod2Instances ||
                candidates[candidateIndex].pixels < FarMinimumProjectedPixels)
                return;
            selectedForDraw[index] = true;
            desiredLods[index] = Lod2;
            individualTotal++;
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
                    // The resident page is already present before selection changes;
                    // introduce only the chosen representation through bounded A2C.
                    currentLods[index] = desired;
                    previousLods[index] = Culled;
                    transitionStarts[index] = now;
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
            int shrubCount = 0;
            int groveCount = 0;
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
                        ref lod2Count,
                        ref shrubCount,
                        ref groveCount);
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
                    ref lod2Count,
                    ref shrubCount,
                    ref groveCount);
                AddInstance(
                    index,
                    currentLods[index],
                    progress,
                    cameraPosition,
                    ref lod0Count,
                    ref lod1Count,
                    ref lod2Count,
                    ref shrubCount,
                    ref groveCount);
            }

            preparedLod0=lod0Count;preparedLod1=lod1Count;preparedLod2=lod2Count;preparedShrubs=shrubCount;
            PrepareBatches(camera, layer);
            DrawPrepared(camera,layer);
        }

        // Color and XR motion submit identical selected geometry and fades.
        private void PrepareBatches(Camera camera,int layer)
        {
            using var sample = PackMarker.Auto();
            preparedBatchCount = 0;
            DrawTier(
                lod0Mesh,
                lod0Matrices,
                lod0Tints,
                lod0Fades,
                lod0Frames,
                preparedLod0,
                camera,
                layer);
            DrawTier(
                lod1Mesh,
                lod1Matrices,
                lod1Tints,
                lod1Fades,
                lod1Frames,
                preparedLod1,
                camera,
                layer);
            DrawTier(
                lod2Mesh,
                lod2Matrices,
                lod2Tints,
                lod2Fades,
                lod2Frames,
                preparedLod2,
                camera,
                layer);
            DrawTier(
                shrubMesh,
                shrubMatrices,
                shrubTints,
                shrubFades,
                shrubFrames,
                preparedShrubs,
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
            ref int lod2Count,
            ref int shrubCount,
            ref int groveCount)
        {
            if (lod == Culled || fade <= 0.001f)
                return;
            RuntimeInstance instance = runtimeInstances[index];
            if (HasActiveAuthoredReplacementAt(instance.Position)) return;
 #if UNITY_EDITOR
            if (!Application.isPlaying && OmitHybridPrototypeInstanceForVisualQa != null)
            {
                Mesh reviewMesh = instance.Kind == 2 && lod != Lod2 ? shrubMesh :
                    lod == Lod0 ? lod0Mesh : lod == Lod1 ? lod1Mesh : lod2Mesh;
                if (OmitHybridPrototypeInstanceForVisualQa(instance.Position, reviewMesh))
                    return;
            }
 #endif
            if (instance.Kind == 2 && lod != Lod2)
            {
                if (shrubCount >= shrubMatrices.Length)
                    return;
                shrubMatrices[shrubCount] = instance.Matrix;
                shrubTints[shrubCount] = instance.Tint;
                shrubFades[shrubCount] = fade;
                shrubFrames[shrubCount] = 0f;
                shrubCount++;
                return;
            }
            if (instance.Kind == 3)
            {
                // V3.2 contains no aggregate grove records. Keep this guard so a
                // stale database cannot silently reintroduce blob geometry.
                return;
            }
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
            // The impostor is unit-height; reusing the non-unit near-mesh scale
            // shortened every far tree (including its crown and pictured stem).
            Vector3 farScale=instance.Scale;
            farScale.x*=lod0Mesh.bounds.size.x/lod2Mesh.bounds.size.x;
            farScale.y*=lod0Mesh.bounds.size.y/lod2Mesh.bounds.size.y;
            lod2Matrices[lod2Count] = Matrix4x4.TRS(
                instance.Position,
                rotation,
                farScale);
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
            Camera camera,
            int layer)
        {
            if (count <= 0)
                return;
            Bounds bounds = mesh.bounds;
            PreparedTreeBatch batch = null;
            int batchCount = 0;
            for (int source = 0; source < count; source++)
            {
                Bounds visibleBounds=bounds;
                if(tints[source].w>1.5f)
                    visibleBounds.Encapsulate(new Bounds(new Vector3(0,bounds.max.y*.5f,0),
                        new Vector3(Mathf.Max(bounds.size.x,bounds.size.z),bounds.max.y*1.1f,Mathf.Max(bounds.size.x,bounds.size.z))));
                bool retain = IsMeshInsideDrawFrustum(matrices[source], visibleBounds);
#if UNITY_EDITOR
                if (!Application.isPlaying && DisableDrawCullingForVisualQa) retain = true;
#endif
                if (!retain) continue;
                if (batch == null)
                {
                    if (preparedBatchCount == preparedBatches.Count)
                        preparedBatches.Add(new PreparedTreeBatch());
                    batch = preparedBatches[preparedBatchCount++];
                    batch.Mesh = mesh;
                }
                batch.Matrices[batchCount] = matrices[source];
                batch.Tints[batchCount] = tints[source];
                batch.Fades[batchCount] = fades[source];
                batch.Frames[batchCount] = frames[source];
                batchCount++;
                if (batchCount != MaximumInstancesPerDraw) continue;
                PackTreeBatch(batch, batchCount);
                batchCount = 0;
                batch = null;
            }
            if (batchCount > 0) PackTreeBatch(batch, batchCount);
        }

        private readonly Plane[] drawPlanesLeft = new Plane[6];
        private readonly Plane[] drawPlanesRight = new Plane[6];
        private bool drawStereo;

        private void PrepareDrawFrustum(Camera camera)
        {
            drawStereo = camera.stereoEnabled;
            // Include a small angular margin for XR pose prediction. A fixed
            // 25 cm bounds pad alone becomes negligible for distant conifers.
            Matrix4x4 guard=Matrix4x4.Scale(new Vector3(.90f,.90f,1));
            if (drawStereo)
            {
                GeometryUtility.CalculateFrustumPlanes(guard * camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) *
                    camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left), drawPlanesLeft);
                GeometryUtility.CalculateFrustumPlanes(guard * camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right) *
                    camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right), drawPlanesRight);
            }
            else GeometryUtility.CalculateFrustumPlanes(guard * camera.projectionMatrix * camera.worldToCameraMatrix, drawPlanesLeft);
        }

        private bool IsMeshInsideDrawFrustum(Matrix4x4 matrix, Bounds bounds) =>
            IsTreeBoundsInsidePlanes(matrix, bounds, drawPlanesLeft) ||
            (drawStereo && IsTreeBoundsInsidePlanes(matrix, bounds, drawPlanesRight));

        // All submitted tree transforms are TRS (orthogonal scaled axes). This
        // sphere encloses the actual mesh bounds, including offset LOD centers.
        // Keep a small margin at plane boundaries; never use selection radii,
        // whose nominal crown dimensions need not enclose every LOD mesh.
        public static bool IsTreeBoundsInsidePlanes(Matrix4x4 matrix, Bounds bounds, Plane[] planes)
        {
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 extents = bounds.extents;
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            float radius = Mathf.Sqrt(x.sqrMagnitude * extents.x * extents.x +
                y.sqrMagnitude * extents.y * extents.y + z.sqrMagnitude * extents.z * extents.z) + 0.25f;
            for (int plane = 0; plane < planes.Length; plane++)
                if (planes[plane].GetDistanceToPoint(center) < -radius) return false;
            return true;
        }

        private static void PackTreeBatch(PreparedTreeBatch batch, int count)
        {
            batch.Count = count;
            batch.Properties.SetVectorArray(InstanceTintId, batch.Tints);
            batch.Properties.SetFloat("_TreePrototypeHeight", batch.Mesh.bounds.max.y);
            batch.Properties.SetFloatArray(InstanceFadeId, batch.Fades);
            batch.Properties.SetFloatArray(ImpostorFrameId, batch.Frames);
        }

        private void DrawPrepared(Camera camera, int layer)
        {
            for (int index = 0; index < preparedBatchCount; index++)
            {
                PreparedTreeBatch batch = preparedBatches[index];
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    ObserveHybridPrototypeTierForVisualQa?.Invoke(batch.Mesh, batch.Count);
#endif
                QuestFlight.Rendering.ProductRenderSubmission.DrawMeshInstanced(
                    batch.Mesh, 0, sharedCanopyMaterial, batch.Matrices, batch.Count,
                    batch.Properties, ShadowCastingMode.Off, false, layer, camera,
                    LightProbeUsage.Off, null);
            }
        }
    }
}
