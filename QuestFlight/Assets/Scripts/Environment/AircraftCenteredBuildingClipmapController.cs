using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using QuestFlightLab.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Swaps offline-compiled 1 km building mesh pages through one explicitly authored
    /// 4x4 renderer pool. It never creates product hierarchy or geometry at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AircraftCenteredBuildingClipmapController : MonoBehaviour
    {
        public const string ContractVersion = "full-map-building-clipmap-v1";
        public const int GridSize = 4;
        public const int SlotCount = GridSize * GridSize;

        [Header("Explicitly authored references")]
        public AircraftReferenceFrameRig trackedRig;
        public MeshFilter[] pageFilters = Array.Empty<MeshFilter>();
        public MeshRenderer[] pageRenderers = Array.Empty<MeshRenderer>();
        public Material sharedBuildingMaterial;

        [Header("Offline page contract")]
        public string bundleDirectory = "QuestFlightBuildingClipmap";
        public int columns = 24;
        public int rows = 24;
        public float pageSizeMeters = 1000f;
        public float mapMinimumX = -12000f;
        public float mapMinimumZ = -12000f;
        public int bundleRegionCells = 2;
        public float predictionSeconds = 2.5f;
        public float lod0MaximumDistance = 1150f;
        public float lod1MaximumDistance = 2600f;
        public float lodHysteresisMeters = 160f;
        public float movementPollSeconds = 0.3f;
        public int authoredMaximumSelectedTriangles = 45213;
        public string authoredContractVersion = ContractVersion;

        private readonly PageState[] states = new PageState[SlotCount];
        private Vector3 previousPosition;
        private float previousSampleTime;
        private bool hasPreviousPosition;
        private bool refreshRunning;

        private sealed class PageState
        {
            public int column = int.MinValue;
            public int row = int.MinValue;
            public int lod = -1;
            public Mesh mesh;
        }

        private void Awake()
        {
            for (int i = 0; i < states.Length; i++) states[i] = new PageState();

            // The environment prefab is also instantiated in isolation by product
            // tests and authoring tools. A prefab asset cannot serialize a reference
            // to the scene-owned AircraftReferenceFrameRig, so that valid isolated
            // state must remain inert without pretending that it is a product error.
            // The authored product scene supplies this reference explicitly and the
            // contract validator still fails it if that scene override is absent.
            if (trackedRig == null)
            {
                DisablePageRenderers();
                enabled = false;
                return;
            }

            if (!TryValidateContract(out string report))
            {
                Debug.LogError("[QuestFlight][BuildingClipmap] " + report, this);
                DisablePageRenderers();
                enabled = false;
                return;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                pageRenderers[i].sharedMaterial = sharedBuildingMaterial;
                pageRenderers[i].enabled = false;
            }
        }

        private void DisablePageRenderers()
        {
            if (pageRenderers == null) return;
            for (int i = 0; i < pageRenderers.Length; i++)
                if (pageRenderers[i] != null)
                    pageRenderers[i].enabled = false;
        }

        private IEnumerator Start()
        {
            yield return RefreshWindow(true);
            while (enabled)
            {
                yield return new WaitForSecondsRealtime(movementPollSeconds);
                yield return RefreshWindow(false);
            }
        }

        private IEnumerator RefreshWindow(bool force)
        {
            if (refreshRunning || trackedRig == null) yield break;
            refreshRunning = true;
            Vector3 current = trackedRig.transform.position;
            float now = Time.realtimeSinceStartup;
            float elapsed = Mathf.Max(0.001f, now - previousSampleTime);
            Vector3 velocity = hasPreviousPosition ? (current - previousPosition) / elapsed : Vector3.zero;
            previousPosition = current;
            previousSampleTime = now;
            hasPreviousPosition = true;
            Vector3 prediction = Vector3.ClampMagnitude(velocity * predictionSeconds, pageSizeMeters);
            Vector3 predicted = current + prediction;
            Vector2Int minimum = DesiredMinimum(predicted);
            try
            {
                for (int localRow = 0; localRow < GridSize; localRow++)
                for (int localColumn = 0; localColumn < GridSize; localColumn++)
                {
                    int slot = localRow * GridSize + localColumn;
                    int column = minimum.x + localColumn;
                    int row = minimum.y + localRow;
                    int lod = DesiredLod(states[slot], predicted, column, row);
                    if (!force && states[slot].column == column && states[slot].row == row && states[slot].lod == lod)
                        continue;
                    yield return LoadPage(slot, column, row, lod);
                }
            }
            finally
            {
                refreshRunning = false;
            }
        }

        private Vector2Int DesiredMinimum(Vector3 position)
        {
            int column = Mathf.FloorToInt((position.x - mapMinimumX) / pageSizeMeters);
            int row = Mathf.FloorToInt((position.z - mapMinimumZ) / pageSizeMeters);
            return new Vector2Int(
                Mathf.Clamp(column - 1, 0, columns - GridSize),
                Mathf.Clamp(row - 1, 0, rows - GridSize));
        }

        private int DesiredLod(PageState state, Vector3 position, int column, int row)
        {
            float centerX = mapMinimumX + (column + 0.5f) * pageSizeMeters;
            float centerZ = mapMinimumZ + (row + 0.5f) * pageSizeMeters;
            float distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(centerX, centerZ));
            if (state.column == column && state.row == row)
            {
                if (state.lod == 0 && distance <= lod0MaximumDistance + lodHysteresisMeters) return 0;
                if (state.lod == 1)
                {
                    if (distance < lod0MaximumDistance - lodHysteresisMeters) return 0;
                    if (distance <= lod1MaximumDistance + lodHysteresisMeters) return 1;
                }
                if (state.lod == 2 && distance >= lod1MaximumDistance - lodHysteresisMeters) return 2;
            }
            if (distance <= lod0MaximumDistance) return 0;
            return distance <= lod1MaximumDistance ? 1 : 2;
        }

        private IEnumerator LoadPage(int slot, int column, int row, int lod)
        {
            string bundleName = $"qf_buildings_r{row / bundleRegionCells:D2}_c{column / bundleRegionCells:D2}";
            string assetName = $"KBDU_buildings_lod{lod}_{column:D3}_{row:D3}";
            string path = Path.Combine(Application.streamingAssetsPath, bundleDirectory, bundleName);
            using UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(path);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[QuestFlight][BuildingClipmap] Retaining page after bundle failure: {bundleName}: {request.error}", this);
                yield break;
            }
            AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
            Mesh loaded = null;
            try
            {
                AssetBundleRequest assetRequest = bundle.LoadAssetAsync<Mesh>(assetName);
                yield return assetRequest;
                loaded = assetRequest.asset as Mesh;
            }
            finally
            {
                bundle.Unload(loaded == null);
            }
            if (loaded == null)
            {
                Debug.LogWarning("[QuestFlight][BuildingClipmap] Retaining page after missing asset: " + assetName, this);
                yield break;
            }

            PageState state = states[slot];
            Mesh previous = state.mesh;
            state.mesh = loaded;
            state.column = column;
            state.row = row;
            state.lod = lod;
            pageFilters[slot].sharedMesh = loaded;
            pageRenderers[slot].enabled = loaded.vertexCount > 0;
            if (previous != null && previous != loaded) Destroy(previous);
        }

        private void OnDestroy()
        {
            foreach (PageState state in states)
                if (state?.mesh != null) Destroy(state.mesh);
        }

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            if (!string.Equals(authoredContractVersion, ContractVersion, StringComparison.Ordinal))
                failures.Add("contract version mismatch");
            if (trackedRig == null) failures.Add("tracked aircraft reference is missing");
            if (sharedBuildingMaterial == null) failures.Add("shared material is missing");
            if (pageFilters == null || pageFilters.Length != SlotCount || pageRenderers == null || pageRenderers.Length != SlotCount)
                failures.Add("the fixed 4x4 renderer pool is incomplete");
            else
                for (int i = 0; i < SlotCount; i++)
                    if (pageFilters[i] == null || pageRenderers[i] == null || pageFilters[i].gameObject != pageRenderers[i].gameObject ||
                        pageFilters[i].transform.parent != transform)
                        failures.Add("slot " + i + " is not explicitly authored directly under the clipmap root");
            if (columns < GridSize || rows < GridSize || pageSizeMeters <= 0f || bundleRegionCells <= 0)
                failures.Add("page grid contract is invalid");
            if (lod0MaximumDistance <= 0f || lod1MaximumDistance <= lod0MaximumDistance || lodHysteresisMeters < 0f)
                failures.Add("LOD thresholds are invalid");
            if (authoredMaximumSelectedTriangles <= 0)
                failures.Add("authored maximum selected triangle count is missing");
            report = failures.Count == 0 ? "PASS fixed 16-slot aircraft-centered building clipmap" : string.Join("; ", failures);
            return failures.Count == 0;
        }
    }
}
