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
    /// 4x4 coverage pool and one exact-detail renderer. Coverage never changes with
    /// LOD: every represented roof remains present until its far page leaves the
    /// aircraft-centered window. The detail renderer replaces only the coverage page
    /// containing the aircraft, so a page transition changes fidelity without making
    /// an entire neighborhood appear or disappear.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AircraftCenteredBuildingClipmapController : MonoBehaviour
    {
        public const string ContractVersion = "full-map-building-clipmap-v1.1";
        public const int GridSize = 4;
        public const int SlotCount = GridSize * GridSize;

        [Header("Explicitly authored references")]
        public AircraftReferenceFrameRig trackedRig;
        public MeshFilter[] pageFilters = Array.Empty<MeshFilter>();
        public MeshRenderer[] pageRenderers = Array.Empty<MeshRenderer>();
        public MeshFilter detailFilter;
        public MeshRenderer detailRenderer;
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
        [Min(0f)] public float predictionSeconds = 2.5f;
        [Min(0.02f)] public float movementPollSeconds = 0.3f;
        public int authoredMaximumSelectedTriangles = 116000;
        public string authoredContractVersion = ContractVersion;

        [Header("Runtime evidence (read only)")]
        [SerializeField] private int activeCoveragePages;
        [SerializeField] private int lastRetainedPages;
        [SerializeField] private int lastEnteringPages;
        [SerializeField] private int residentRegions;

        private readonly PageState[] states = new PageState[SlotCount];
        private readonly Dictionary<PageAssetKey, Mesh> meshes = new Dictionary<PageAssetKey, Mesh>();
        private readonly Dictionary<string, RegionState> regions = new Dictionary<string, RegionState>();
        private readonly Dictionary<string, uint> bundleCrcs = new Dictionary<string, uint>(StringComparer.Ordinal);
        private Vector3 previousPosition;
        private float previousSampleTime;
        private bool hasPreviousPosition;
        private bool refreshRunning;
        private bool indexReady;

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

        private readonly struct PageAssetKey : IEquatable<PageAssetKey>
        {
            public readonly PageKey page;
            public readonly bool detail;
            public PageAssetKey(PageKey page, bool detail) { this.page = page; this.detail = detail; }
            public bool Equals(PageAssetKey other) => page.Equals(other.page) && detail == other.detail;
            public override bool Equals(object obj) => obj is PageAssetKey other && Equals(other);
            public override int GetHashCode() => unchecked((page.GetHashCode() * 397) ^ (detail ? 1 : 0));
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
            detailRenderer.sharedMaterial = sharedBuildingMaterial;
            detailRenderer.enabled = false;
        }

        private void DisableRenderers()
        {
            if (pageRenderers != null)
                foreach (MeshRenderer renderer in pageRenderers)
                    if (renderer != null) renderer.enabled = false;
            if (detailRenderer != null) detailRenderer.enabled = false;
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

                Vector2Int minimum = DesiredMinimum(current);
                List<PageKey> active = WindowPages(minimum);
                PageKey detailPage = ContainingPage(current);
                List<PageAssetKey> required = active.Select(page => new PageAssetKey(page, false)).ToList();
                required.Add(new PageAssetKey(detailPage, true));

                bool loaded = true;
                foreach (PageAssetKey key in required)
                {
                    bool oneLoaded = false;
                    yield return EnsureMesh(key, result => oneLoaded = result != null);
                    if (!oneLoaded) { loaded = false; break; }
                }
                if (!loaded)
                {
                    Debug.LogWarning("[QuestFlight][BuildingClipmap] Retaining the complete prior window after a page-load failure.", this);
                    yield break;
                }

                CommitWindow(active, detailPage, force);

                Vector3 prediction = Vector3.ClampMagnitude(velocity * predictionSeconds, pageSizeMeters);
                Vector3 predicted = current + prediction;
                HashSet<PageAssetKey> residency = new HashSet<PageAssetKey>(required);
                foreach (PageKey page in WindowPages(DesiredMinimum(predicted)))
                    residency.Add(new PageAssetKey(page, false));
                residency.Add(new PageAssetKey(ContainingPage(predicted), true));
                foreach (PageAssetKey key in residency)
                    if (!meshes.ContainsKey(key))
                        yield return EnsureMesh(key, _ => { });
                yield return ReleaseUnusedRegions(residency);
            }
            finally { refreshRunning = false; }
        }

        private void CommitWindow(IReadOnlyList<PageKey> active, PageKey detailPage, bool force)
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
                    pageRenderers[existing].enabled = !page.Equals(detailPage) && states[existing].mesh.vertexCount > 0;
                    continue;
                }
                int slot = available.Dequeue();
                Mesh mesh = meshes[new PageAssetKey(page, false)];
                states[slot].page = page;
                states[slot].mesh = mesh;
                states[slot].occupied = true;
                pageFilters[slot].sharedMesh = mesh;
                pageRenderers[slot].enabled = !page.Equals(detailPage) && mesh.vertexCount > 0;
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

            Mesh detail = meshes[new PageAssetKey(detailPage, true)];
            detailFilter.sharedMesh = detail;
            detailRenderer.enabled = detail.vertexCount > 0;
            activeCoveragePages = pageRenderers.Count(renderer => renderer.enabled);
            if (!force && (lastEnteringPages == GridSize || lastEnteringPages == GridSize * 2 - 1))
                Debug.Log($"[QuestFlight][BuildingClipmap] Atomic page shift: retained={lastRetainedPages}; entering={lastEnteringPages}; detail={detailPage}.", this);
        }

        private IEnumerator EnsureMesh(PageAssetKey key, Action<Mesh> completed)
        {
            if (meshes.TryGetValue(key, out Mesh cached) && cached != null)
            {
                completed(cached);
                yield break;
            }
            string regionName = RegionName(key.page);
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

            string assetName = AssetName(key);
            AssetBundleRequest assetRequest = region.bundle.LoadAssetAsync<Mesh>(assetName);
            yield return assetRequest;
            Mesh loaded = assetRequest.asset as Mesh;
            if (loaded == null)
            {
                Debug.LogWarning("[QuestFlight][BuildingClipmap] Mesh missing from bundle: " + assetName, this);
                completed(null);
                yield break;
            }
            meshes[key] = loaded;
            completed(loaded);
        }

        private IEnumerator ReleaseUnusedRegions(HashSet<PageAssetKey> residency)
        {
            HashSet<string> needed = new HashSet<string>(residency.Select(key => RegionName(key.page)), StringComparer.Ordinal);
            List<string> stale = regions.Keys.Where(name => !needed.Contains(name)).ToList();
            foreach (string name in stale)
            {
                RegionState region = regions[name];
                regions.Remove(name);
                foreach (PageAssetKey key in meshes.Keys.Where(key => RegionName(key.page) == name).ToList()) meshes.Remove(key);
                if (region.bundle != null) yield return region.bundle.UnloadAsync(true);
            }
            residentRegions = regions.Count;
        }

        private Vector2Int DesiredMinimum(Vector3 position)
        {
            PageKey page = ContainingPage(position);
            return new Vector2Int(
                Mathf.Clamp(page.column - GridSize / 2, 0, columns - GridSize),
                Mathf.Clamp(page.row - GridSize / 2, 0, rows - GridSize));
        }

        private PageKey ContainingPage(Vector3 position) => new PageKey(
            Mathf.Clamp(Mathf.FloorToInt((position.x - mapMinimumX) / pageSizeMeters), 0, columns - 1),
            Mathf.Clamp(Mathf.FloorToInt((position.z - mapMinimumZ) / pageSizeMeters), 0, rows - 1));

        private static List<PageKey> WindowPages(Vector2Int minimum)
        {
            List<PageKey> result = new List<PageKey>(SlotCount);
            for (int row = 0; row < GridSize; row++)
            for (int column = 0; column < GridSize; column++)
                result.Add(new PageKey(minimum.x + column, minimum.y + row));
            return result;
        }

        private string RegionName(PageKey page) =>
            $"qf_buildings_r{page.row / bundleRegionCells:D2}_c{page.column / bundleRegionCells:D2}";

        private static string AssetName(PageAssetKey key) =>
            $"KBDU_buildings_{(key.detail ? "detail" : "coverage")}_{key.page.column:D3}_{key.page.row:D3}";

        private string StreamingUrl(string leaf) =>
            Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + bundleDirectory + "/" + leaf;

        private void OnDestroy()
        {
            DisableRenderers();
            foreach (MeshFilter filter in pageFilters ?? Array.Empty<MeshFilter>())
                if (filter != null) filter.sharedMesh = null;
            if (detailFilter != null) detailFilter.sharedMesh = null;
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
                failures.Add("the fixed 4x4 coverage renderer pool is incomplete");
            else
                for (int i = 0; i < SlotCount; i++)
                    if (pageFilters[i] == null || pageRenderers[i] == null || pageFilters[i].gameObject != pageRenderers[i].gameObject ||
                        pageFilters[i].transform.parent != transform)
                        failures.Add("coverage slot " + i + " is not explicitly authored directly under the clipmap root");
            if (detailFilter == null || detailRenderer == null || detailFilter.gameObject != detailRenderer.gameObject || detailFilter.transform.parent != transform)
                failures.Add("the exact-detail renderer is not explicitly authored directly under the clipmap root");
            if (columns < GridSize || rows < GridSize || pageSizeMeters <= 0f || bundleRegionCells <= 0) failures.Add("page grid contract is invalid");
            if (predictionSeconds < 0f || movementPollSeconds <= 0f) failures.Add("streaming timing contract is invalid");
            if (authoredMaximumSelectedTriangles <= 0) failures.Add("authored maximum selected triangle count is missing");
            report = failures.Count == 0 ? "PASS stable 16-page coverage plus one exact aircraft-page detail renderer" : string.Join("; ", failures);
            return failures.Count == 0;
        }
    }
}
