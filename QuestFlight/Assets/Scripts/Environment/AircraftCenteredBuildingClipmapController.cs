using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Streams offline-compiled 1 km building pages through one explicitly authored
    /// renderer pool. The accepted contiguous 4x4 window is always retained at full
    /// height. Eight additional, connected pages extend the aircraft-forward view and
    /// preload the next base-window pages before the aircraft reaches them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AircraftCenteredBuildingClipmapController : MonoBehaviour
    {
        public const string ContractVersion = "full-map-building-clipmap-v1.5";
        public const int GridSize = 4;
        public const int BaseSlotCount = GridSize * GridSize;
        public const int ForwardExtensionSlotCount = 8;
        public const int SlotCount = BaseSlotCount + ForwardExtensionSlotCount;
        public const int CandidateRadiusPages = 6;

        [Header("Explicitly authored references")]
        public AircraftReferenceFrameRig trackedRig;
        public MeshFilter[] pageFilters = Array.Empty<MeshFilter>();
        public MeshRenderer[] pageRenderers = Array.Empty<MeshRenderer>();
        public Material sharedBuildingMaterial;

        [Header("Offline page contract")]
        public string bundleDirectory = "QuestFlightBuildingClipmap";
        public string bundleIndexFile = "bundle_index.json";
        public int columns = 24;
        public int rows = 24;
        public float pageSizeMeters = 1000f;
        public float mapMinimumX = -12000f;
        public float mapMinimumZ = -12000f;
        public int bundleRegionCells = 2;
        public int[] pageTriangleCounts = Array.Empty<int>();
        public int[] pageBuildingCounts = Array.Empty<int>();
        public float[] pageRoofAreasSquareMeters = Array.Empty<float>();
        [Min(0f)] public float predictionSeconds = 2.5f;
        [Min(0.02f)] public float movementPollSeconds = 0.3f;
        public int authoredMaximumSelectedTriangles = 235000;
        public string authoredContractVersion = ContractVersion;

        [Header("Flight-simulator interest contract")]
        [Range(30f, 100f)] public float expandedViewHalfAngleDegrees = 68f;
        [Range(0.05f, 2f)] public float directionResponseSeconds = 0.65f;
        [Min(0f)] public float stickyPageScore = 0.18f;

        [Header("Runtime evidence (read only)")]
        [SerializeField] private int activeCoveragePages;
        [SerializeField] private int lastRetainedPages;
        [SerializeField] private int lastEnteringPages;
        [SerializeField] private int residentRegions;
        [SerializeField] private Vector3 stableAircraftForward = Vector3.forward;
        [SerializeField] private Vector3 stableViewForward = Vector3.forward;
        [SerializeField] private int protectedBasePages;
        [SerializeField] private int activeForwardPages;
        [SerializeField] private int pageResidencyRevision;

        private readonly PageState[] states = new PageState[SlotCount];
        private readonly Dictionary<PageKey, Mesh> meshes = new Dictionary<PageKey, Mesh>();
        private readonly Dictionary<string, RegionState> regions = new Dictionary<string, RegionState>();
        private readonly Dictionary<string, uint> bundleCrcs = new Dictionary<string, uint>(StringComparer.Ordinal);
        private Vector3 previousPosition;
        private float previousSampleTime;
        private bool hasPreviousPosition;
        private bool refreshRunning;
        private bool indexReady;

        private struct ScoredPage
        {
            public PageKey page;
            public float score;
            public int triangles;
            public float distance;
        }

        [Serializable] private sealed class BundleIndex
        {
            public string contract_version;
            public BundleRecord[] bundles;
        }

        [Serializable] private sealed class BundleRecord
        {
            public string name;
            public uint crc;
            public string hash;
        }

        private readonly struct PageKey : IEquatable<PageKey>
        {
            public readonly int column;
            public readonly int row;
            public PageKey(int column, int row) { this.column = column; this.row = row; }
            public bool Equals(PageKey other) => column == other.column && row == other.row;
            public override bool Equals(object obj) => obj is PageKey other && Equals(other);
            public override int GetHashCode() => unchecked((column * 397) ^ row);
            public override string ToString() => $"{column:D3},{row:D3}";
        }

        private sealed class PageState
        {
            public PageKey page = new PageKey(int.MinValue, int.MinValue);
            public Mesh mesh;
            public bool occupied;
        }

        private sealed class RegionState
        {
            public AssetBundle bundle;
        }

        public int PageColumns => columns;
        public int PageRows => rows;
        public float PageSizeMeters => pageSizeMeters;
        public float MapMinimumX => mapMinimumX;
        public float MapMinimumZ => mapMinimumZ;
        public Vector3 StableAircraftForward => stableAircraftForward;
        public Vector3 StableViewForward => stableViewForward;
        public int PageResidencyRevision => pageResidencyRevision;
        public bool HasCommittedCoverage => activeCoveragePages > 0;

        /// <summary>
        /// Copies the controller's already-committed and already-loaded/prefetched page
        /// residency into caller-owned buffers. This does not alter building selection
        /// and performs no managed allocation.
        /// </summary>
        public bool TryCopyPageResidency(bool[] committed, bool[] prefetched)
        {
            int pageCount = checked(columns * rows);
            if (committed == null || prefetched == null ||
                committed.Length != pageCount || prefetched.Length != pageCount)
                return false;

            Array.Clear(committed, 0, committed.Length);
            Array.Clear(prefetched, 0, prefetched.Length);
            for (int index = 0; index < states.Length; index++)
            {
                PageState state = states[index];
                // Editor capture cameras can render an authored scene before Awake has
                // initialized the fixed residency slots. Treat that as unavailable
                // coverage; callers already fall back to their deterministic editor
                // selection instead of allowing a render callback to throw.
                if (state == null || !state.occupied ||
                    !TryPageIndex(state.page, out int pageIndex))
                    continue;
                committed[pageIndex] = true;
                prefetched[pageIndex] = true;
            }
            foreach (PageKey page in meshes.Keys)
                if (TryPageIndex(page, out int pageIndex))
                    prefetched[pageIndex] = true;
            return activeCoveragePages > 0;
        }

        private bool TryPageIndex(PageKey page, out int index)
        {
            if (page.column < 0 || page.column >= columns ||
                page.row < 0 || page.row >= rows)
            {
                index = -1;
                return false;
            }
            index = page.row * columns + page.column;
            return true;
        }

        private void Awake()
        {
            for (int i = 0; i < states.Length; i++) states[i] = new PageState();
            if (trackedRig == null)
            {
                DisableRenderers();
                enabled = false;
                return;
            }
            if (!TryValidateContract(out string report))
            {
                Debug.LogError("[QuestFlight][BuildingClipmap] " + report, this);
                DisableRenderers();
                enabled = false;
                return;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                pageRenderers[i].sharedMaterial = sharedBuildingMaterial;
                pageRenderers[i].enabled = false;
            }
            stableAircraftForward = HorizontalForward(trackedRig.transform.forward, Vector3.forward);
            Camera camera = trackedRig.TrackedCamera;
            stableViewForward = camera != null
                ? HorizontalForward(camera.transform.forward, stableAircraftForward)
                : stableAircraftForward;
        }

        private void LateUpdate() => UpdateStableDirections();

        private void UpdateStableDirections()
        {
            if (trackedRig == null) return;
            float response = 1f - Mathf.Exp(-Mathf.Max(0f, Time.unscaledDeltaTime) /
                Mathf.Max(0.05f, directionResponseSeconds));
            Vector3 aircraft = HorizontalForward(trackedRig.transform.forward, stableAircraftForward);
            stableAircraftForward = Vector3.Slerp(stableAircraftForward, aircraft, response).normalized;
            Camera camera = trackedRig.TrackedCamera;
            if (camera == null) return;
            Vector3 view = HorizontalForward(camera.transform.forward, stableViewForward);
            stableViewForward = Vector3.Slerp(stableViewForward, view, response).normalized;
        }

        private void DisableRenderers()
        {
            if (pageRenderers != null)
                foreach (MeshRenderer renderer in pageRenderers)
                    if (renderer != null) renderer.enabled = false;
        }

        private IEnumerator Start()
        {
            yield return LoadBundleIndex();
            if (!indexReady)
            {
                DisableRenderers();
                enabled = false;
                yield break;
            }
            yield return RefreshWindow(true);
            while (enabled)
            {
                yield return new WaitForSecondsRealtime(movementPollSeconds);
                yield return RefreshWindow(false);
            }
        }

        private IEnumerator LoadBundleIndex()
        {
            string path = StreamingUrl(bundleIndexFile);
            using UnityWebRequest request = UnityWebRequest.Get(path);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[QuestFlight][BuildingClipmap] Bundle index unavailable: {request.error}", this);
                yield break;
            }
            BundleIndex index = JsonUtility.FromJson<BundleIndex>(request.downloadHandler.text);
            if (index == null || !string.Equals(index.contract_version, ContractVersion, StringComparison.Ordinal) ||
                index.bundles == null || index.bundles.Length == 0)
            {
                Debug.LogError("[QuestFlight][BuildingClipmap] Bundle index contract is invalid.", this);
                yield break;
            }
            bundleCrcs.Clear();
            foreach (BundleRecord record in index.bundles)
                if (record != null && !string.IsNullOrWhiteSpace(record.name))
                    bundleCrcs[record.name] = record.crc;
            indexReady = bundleCrcs.Count > 0;
        }

        private IEnumerator RefreshWindow(bool force)
        {
            if (refreshRunning || trackedRig == null) yield break;
            refreshRunning = true;
            try
            {
                Vector3 current = trackedRig.transform.position;
                float now = Time.realtimeSinceStartup;
                float elapsed = Mathf.Max(0.001f, now - previousSampleTime);
                Vector3 velocity = hasPreviousPosition ? (current - previousPosition) / elapsed : Vector3.zero;
                previousPosition = current;
                previousSampleTime = now;
                hasPreviousPosition = true;

                List<PageKey> target = SelectDesiredPages(current, stableAircraftForward,
                    stableViewForward, OccupiedPages());

                bool loaded = true;
                foreach (PageKey page in target)
                {
                    bool oneLoaded = false;
                    yield return EnsureMesh(page, result => oneLoaded = result != null);
                    if (!oneLoaded) { loaded = false; break; }
                }
                if (!loaded)
                {
                    Debug.LogWarning("[QuestFlight][BuildingClipmap] Retaining the complete prior window after a page-load failure.", this);
                    yield break;
                }

                CommitWindow(target, force);
                List<PageKey> active = target;

                HashSet<PageKey> protectedWindow = new HashSet<PageKey>(
                    WindowPages(DesiredMinimum(current)));
                protectedBasePages = active.Count(protectedWindow.Contains);
                activeForwardPages = active.Count - protectedBasePages;

                Vector3 prediction = Vector3.ClampMagnitude(velocity * predictionSeconds, pageSizeMeters);
                Vector3 predicted = current + prediction;
                HashSet<PageKey> residency = new HashSet<PageKey>(active);
                foreach (PageKey page in target) residency.Add(page);
                foreach (PageKey page in SelectDesiredPages(predicted, stableAircraftForward,
                             stableViewForward, residency))
                    residency.Add(page);
                foreach (PageKey page in residency)
                    if (!meshes.ContainsKey(page))
                        yield return EnsureMesh(page, _ => { });
                yield return ReleaseUnusedRegions(residency);
                pageResidencyRevision++;
            }
            finally { refreshRunning = false; }
        }

        private HashSet<PageKey> OccupiedPages() => new HashSet<PageKey>(states
            .Where(state => state.occupied)
            .Select(state => state.page));

        private readonly List<ScoredPage> scoredPageScratch = new List<ScoredPage>((CandidateRadiusPages * 2 + 1) * (CandidateRadiusPages * 2 + 1));

        private List<PageKey> SelectDesiredPages(
            Vector3 referencePosition,
            Vector3 aircraftForward,
            Vector3 viewForward,
            ICollection<PageKey> stickyPages)
        {
            PageKey containing = ContainingPage(referencePosition);
            Vector2Int baseMinimum = DesiredMinimum(referencePosition);
            List<PageKey> basePages = WindowPages(baseMinimum);
            HashSet<PageKey> selected = new HashSet<PageKey>(basePages);
            bool viewIsRear = Vector3.Dot(aircraftForward, viewForward) < -0.15f;
            float viewThreshold = Mathf.Cos(expandedViewHalfAngleDegrees * Mathf.Deg2Rad);
            List<ScoredPage> candidates = scoredPageScratch;
            candidates.Clear();

            // The first extension tier is the complete page fringe that will enter the
            // protected 4x4 window at the next boundary in the dominant flight direction.
            // This prevents a one-frame geometry hole during normal forward flight.
            AddForwardFringe(baseMinimum, aircraftForward, selected);

            // For diagonal flight, also retain the orthogonal entering fringe. This uses
            // the full extension budget and prevents alternating X/Z boundary gaps.
            float absoluteX = Mathf.Abs(aircraftForward.x);
            float absoluteZ = Mathf.Abs(aircraftForward.z);
            if (Mathf.Min(absoluteX, absoluteZ) >= Mathf.Max(absoluteX, absoluteZ) * 0.35f)
                AddForwardFringe(baseMinimum,
                    absoluteX >= absoluteZ
                        ? new Vector3(0f, 0f, Mathf.Sign(aircraftForward.z))
                        : new Vector3(Mathf.Sign(aircraftForward.x), 0f, 0f),
                    selected);

            for (int row = Mathf.Max(0, containing.row - CandidateRadiusPages);
                 row <= Mathf.Min(rows - 1, containing.row + CandidateRadiusPages); row++)
            for (int column = Mathf.Max(0, containing.column - CandidateRadiusPages);
                 column <= Mathf.Min(columns - 1, containing.column + CandidateRadiusPages); column++)
            {
                PageKey page = new PageKey(column, row);
                if (selected.Contains(page)) continue;
                Vector3 offset = PageCenter(page) - referencePosition;
                offset.y = 0f;
                float distance = Mathf.Max(1f, offset.magnitude);
                Vector3 direction = HorizontalForward(offset, aircraftForward);
                float aircraftDot = Vector3.Dot(aircraftForward, direction);
                float viewDot = Vector3.Dot(viewForward, direction);
                float corridor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.9f, aircraftDot));
                float view = !viewIsRear && viewDot > viewThreshold
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(viewThreshold, 1f, viewDot))
                    : 0f;
                float coverage = Mathf.Sqrt(Mathf.Max(1, PageBuildingCount(page))) * 0.35f +
                                 Mathf.Sqrt(Mathf.Max(1f, PageRoofArea(page))) * 0.035f;
                float distanceKilometers = Mathf.Max(0.25f, distance / 1000f);
                float score = coverage * (0.3f + 1.7f * Mathf.Max(corridor, view)) /
                              (distanceKilometers * distanceKilometers);
                if (stickyPages != null && stickyPages.Contains(page)) score *= 1f + stickyPageScore;
                candidates.Add(new ScoredPage
                {
                    page = page,
                    score = score,
                    triangles = PageTriangleCount(page),
                    distance = distance
                });
            }

            // Grow a connected extension. A disconnected high-score page would pop into
            // an isolated island, which is particularly visible from a flight-simulator view.
            while (selected.Count < SlotCount)
            {
                bool found = false;
                ScoredPage next = default;
                foreach (ScoredPage candidate in candidates)
                {
                    if (selected.Contains(candidate.page) || !IsEdgeAdjacent(candidate.page, selected)) continue;
                    if (!found || candidate.score > next.score ||
                        candidate.score == next.score && (candidate.distance < next.distance ||
                        candidate.distance == next.distance && (candidate.page.row < next.page.row ||
                        candidate.page.row == next.page.row && candidate.page.column < next.page.column)))
                    {
                        found = true;
                        next = candidate;
                    }
                }
                if (!found) break;
                selected.Add(next.page);
            }

            return selected.OrderBy(page => basePages.Contains(page) ? 0 : 1)
                .ThenBy(page => PageCenterDistanceSquared(page, referencePosition))
                .ToList();
        }

        private void AddForwardFringe(Vector2Int minimum, Vector3 forward, HashSet<PageKey> selected)
        {
            bool alongZ = Mathf.Abs(forward.z) >= Mathf.Abs(forward.x);
            int sign = alongZ ? (forward.z >= 0f ? 1 : -1) : (forward.x >= 0f ? 1 : -1);
            for (int offset = 0; offset < GridSize && selected.Count < SlotCount; offset++)
            {
                int column = alongZ ? minimum.x + offset : minimum.x + (sign > 0 ? GridSize : -1);
                int row = alongZ ? minimum.y + (sign > 0 ? GridSize : -1) : minimum.y + offset;
                if (column >= 0 && column < columns && row >= 0 && row < rows)
                    selected.Add(new PageKey(column, row));
            }
        }

        private static bool IsEdgeAdjacent(PageKey page, HashSet<PageKey> selected)
        {
            return selected.Contains(new PageKey(page.column - 1, page.row)) ||
                   selected.Contains(new PageKey(page.column + 1, page.row)) ||
                   selected.Contains(new PageKey(page.column, page.row - 1)) ||
                   selected.Contains(new PageKey(page.column, page.row + 1));
        }

        private Vector2Int DesiredMinimum(Vector3 position)
        {
            PageKey containing = ContainingPage(position);
            return new Vector2Int(
                Mathf.Clamp(containing.column - GridSize / 2, 0, columns - GridSize),
                Mathf.Clamp(containing.row - GridSize / 2, 0, rows - GridSize));
        }

        private List<PageKey> WindowPages(Vector2Int minimum)
        {
            List<PageKey> pages = new List<PageKey>(BaseSlotCount);
            for (int row = minimum.y; row < minimum.y + GridSize; row++)
            for (int column = minimum.x; column < minimum.x + GridSize; column++)
                pages.Add(new PageKey(column, row));
            return pages;
        }

        private Vector3 PageCenter(PageKey page) => new Vector3(
            mapMinimumX + (page.column + 0.5f) * pageSizeMeters,
            0f,
            mapMinimumZ + (page.row + 0.5f) * pageSizeMeters);

        private float PageCenterDistanceSquared(PageKey page, Vector3 position) =>
            (PageCenter(page) - position).sqrMagnitude;

        private int PageTriangleCount(PageKey page)
        {
            int index = page.row * columns + page.column;
            return pageTriangleCounts != null && index >= 0 && index < pageTriangleCounts.Length
                ? Mathf.Max(0, pageTriangleCounts[index])
                : 0;
        }

        private int PageBuildingCount(PageKey page)
        {
            int index = page.row * columns + page.column;
            return pageBuildingCounts != null && index >= 0 && index < pageBuildingCounts.Length
                ? Mathf.Max(0, pageBuildingCounts[index])
                : 0;
        }

        private float PageRoofArea(PageKey page)
        {
            int index = page.row * columns + page.column;
            return pageRoofAreasSquareMeters != null && index >= 0 && index < pageRoofAreasSquareMeters.Length
                ? Mathf.Max(0f, pageRoofAreasSquareMeters[index])
                : 0f;
        }

        private static Vector3 HorizontalForward(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude > 0.0001f) return value.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
        }

        public Vector2Int[] CalculateDesiredPagesForTest(
            Vector3 position,
            Vector3 aircraftForward,
            Vector3 viewForward)
        {
            return SelectDesiredPages(position,
                    HorizontalForward(aircraftForward, Vector3.forward),
                    HorizontalForward(viewForward, aircraftForward),
                    new HashSet<PageKey>())
                .Select(page => new Vector2Int(page.column, page.row)).ToArray();
        }

        public Vector2Int[] CalculateBasePagesForTest(Vector3 position) =>
            WindowPages(DesiredMinimum(position))
                .Select(page => new Vector2Int(page.column, page.row)).ToArray();

        private void CommitWindow(IReadOnlyList<PageKey> active, bool force)
        {
            HashSet<PageKey> desired = new HashSet<PageKey>(active);
            Dictionary<PageKey, int> retained = new Dictionary<PageKey, int>();
            Queue<int> available = new Queue<int>();
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].occupied && desired.Contains(states[i].page)) retained[states[i].page] = i;
                else available.Enqueue(i);
            }
            lastRetainedPages = retained.Count;
            lastEnteringPages = active.Count - retained.Count;

            foreach (PageKey page in active)
            {
                if (retained.TryGetValue(page, out int existing))
                {
                    pageRenderers[existing].enabled = states[existing].mesh.vertexCount > 0;
                    continue;
                }
                int slot = available.Dequeue();
                Mesh mesh = meshes[page];
                states[slot].page = page;
                states[slot].mesh = mesh;
                states[slot].occupied = true;
                pageFilters[slot].sharedMesh = mesh;
                pageRenderers[slot].enabled = mesh.vertexCount > 0;
            }

            HashSet<PageKey> committed = new HashSet<PageKey>(active);
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].occupied && committed.Contains(states[i].page)) continue;
                states[i].occupied = false;
                states[i].mesh = null;
                pageFilters[i].sharedMesh = null;
                pageRenderers[i].enabled = false;
            }

            activeCoveragePages = pageRenderers.Count(renderer => renderer.enabled);
            if (!force && lastEnteringPages > 0)
                Debug.Log($"[QuestFlight][BuildingClipmap] Full-height page shift: retained={lastRetainedPages}; entering={lastEnteringPages}.", this);
        }

        private IEnumerator EnsureMesh(PageKey page, Action<Mesh> completed)
        {
            if (meshes.TryGetValue(page, out Mesh cached) && cached != null)
            {
                completed(cached);
                yield break;
            }
            string regionName = RegionName(page);
            if (!regions.TryGetValue(regionName, out RegionState region) || region.bundle == null)
            {
                if (!bundleCrcs.TryGetValue(regionName, out uint crc))
                {
                    Debug.LogWarning("[QuestFlight][BuildingClipmap] Bundle missing from index: " + regionName, this);
                    completed(null);
                    yield break;
                }
                using UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(StreamingUrl(regionName), crc);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[QuestFlight][BuildingClipmap] Bundle load failed: {regionName}: {request.error}", this);
                    completed(null);
                    yield break;
                }
                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
                if (bundle == null)
                {
                    completed(null);
                    yield break;
                }
                region = new RegionState { bundle = bundle };
                regions[regionName] = region;
                residentRegions = regions.Count;
            }

            string assetName = AssetName(page);
            AssetBundleRequest assetRequest = region.bundle.LoadAssetAsync<Mesh>(assetName);
            yield return assetRequest;
            Mesh loaded = assetRequest.asset as Mesh;
            if (loaded == null)
            {
                Debug.LogWarning("[QuestFlight][BuildingClipmap] Mesh missing from bundle: " + assetName, this);
                completed(null);
                yield break;
            }
            meshes[page] = loaded;
            completed(loaded);
        }

        private IEnumerator ReleaseUnusedRegions(HashSet<PageKey> residency)
        {
            HashSet<string> needed = new HashSet<string>(residency.Select(RegionName), StringComparer.Ordinal);
            List<string> stale = regions.Keys.Where(name => !needed.Contains(name)).ToList();
            foreach (string name in stale)
            {
                RegionState region = regions[name];
                regions.Remove(name);
                foreach (PageKey page in meshes.Keys.Where(page => RegionName(page) == name).ToList()) meshes.Remove(page);
                if (region.bundle != null) yield return region.bundle.UnloadAsync(true);
            }
            residentRegions = regions.Count;
        }

        private PageKey ContainingPage(Vector3 position) => new PageKey(
            Mathf.Clamp(Mathf.FloorToInt((position.x - mapMinimumX) / pageSizeMeters), 0, columns - 1),
            Mathf.Clamp(Mathf.FloorToInt((position.z - mapMinimumZ) / pageSizeMeters), 0, rows - 1));

        private string RegionName(PageKey page) =>
            $"qf_buildings_r{page.row / bundleRegionCells:D2}_c{page.column / bundleRegionCells:D2}";

        private static string AssetName(PageKey page) =>
            $"KBDU_buildings_stable_{page.column:D3}_{page.row:D3}";

        private string StreamingUrl(string leaf) =>
            Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + bundleDirectory + "/" + leaf;

        private void OnDestroy()
        {
            DisableRenderers();
            foreach (MeshFilter filter in pageFilters ?? Array.Empty<MeshFilter>())
                if (filter != null) filter.sharedMesh = null;
            foreach (RegionState region in regions.Values)
                if (region.bundle != null) region.bundle.Unload(true);
            regions.Clear();
            meshes.Clear();
        }

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            if (!string.Equals(authoredContractVersion, ContractVersion, StringComparison.Ordinal)) failures.Add("contract version mismatch");
            if (trackedRig == null) failures.Add("tracked aircraft reference is missing");
            if (sharedBuildingMaterial == null) failures.Add("shared material is missing");
            if (pageFilters == null || pageFilters.Length != SlotCount || pageRenderers == null || pageRenderers.Length != SlotCount)
                failures.Add("the fixed 16-page base plus eight-page forward renderer pool is incomplete");
            else
                for (int i = 0; i < SlotCount; i++)
                    if (pageFilters[i] == null || pageRenderers[i] == null || pageFilters[i].gameObject != pageRenderers[i].gameObject ||
                        pageFilters[i].transform.parent != transform)
                        failures.Add("coverage slot " + i + " is not explicitly authored directly under the clipmap root");
            if (columns < GridSize || rows < GridSize || pageSizeMeters <= 0f || bundleRegionCells <= 0) failures.Add("page grid contract is invalid");
            if (pageTriangleCounts == null || pageTriangleCounts.Length != columns * rows ||
                pageBuildingCounts == null || pageBuildingCounts.Length != columns * rows ||
                pageRoofAreasSquareMeters == null || pageRoofAreasSquareMeters.Length != columns * rows)
                failures.Add("page coverage metadata is invalid");
            if (predictionSeconds < 0f || movementPollSeconds <= 0f) failures.Add("streaming timing contract is invalid");
            if (authoredMaximumSelectedTriangles <= 0) failures.Add("authored maximum selected triangle count is missing");
            report = failures.Count == 0
                ? "PASS protected 16-page full-height base plus eight connected forward pages"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }
    }
}
