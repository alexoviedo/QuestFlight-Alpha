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
    /// Owns a fixed-memory, aircraft-centered 3x3 aerial page window. Source imagery is
    /// compiled into Android AssetBundles; no network access or hierarchy repair occurs at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AircraftReferenceFrameRig))]
    public sealed class AircraftCenteredAerialClipmapController : MonoBehaviour
    {
        public const string ContractVersion = "full_map_aerial_clipmap_v1_1_projected_roofs";
        private const int GridSize = 3;
        private const int SliceCount = GridSize * GridSize;
        public const int MaximumActiveAerialTextureBytes = 24 * 1024 * 1024;

        [Header("Authored references")]
        public AircraftReferenceFrameRig trackedRig;
        public Material terrainMaterial;
        [Tooltip("The one full-map building material that shares terrain imagery for projected roofs.")]
        public Material buildingRoofMaterial;
        [Tooltip("Retained only as the deterministic startup fallback. Its explicit mip requests are released after the complete clipmap becomes active.")]
        public GeospatialMipController legacyFallbackMipController;

        [Header("Compiled page contract")]
        public string bundleDirectory = "QuestFlightAerialClipmap";
        public float mapMinimumX = -12000f;
        public float mapMinimumZ = -12000f;
        public float mapMaximumX = 12000f;
        public float mapMaximumZ = 12000f;
        public TierConfiguration high = new TierConfiguration
        {
            name = "high", columns = 36, rows = 36, pagePixels = 2048,
            gutterPixels = 16, tileInnerMeters = 672f, gridMinimumX = -12096f,
            gridMinimumZ = -12096f, bundleRegionCells = 2, blendWidthMeters = 90f
        };
        public TierConfiguration medium = new TierConfiguration
        {
            name = "medium", columns = 24, rows = 24, pagePixels = 1024,
            gutterPixels = 8, tileInnerMeters = 1008f, gridMinimumX = -12096f,
            gridMinimumZ = -12096f, bundleRegionCells = 2, blendWidthMeters = 240f
        };

        [Header("Deterministic preload")]
        [Min(0f)] public float predictionSeconds = 3f;
        [Min(0.05f)] public float movementPollSeconds = 0.25f;
        public bool writeDevelopmentTrace = true;
        public string authoredContractVersion = ContractVersion;

        private TierRuntime highRuntime;
        private TierRuntime mediumRuntime;
        private Vector3 previousPosition;
        private bool hasPreviousPosition;
        private float previousSampleTime;
        private bool refreshRunning;
        private Texture2D globalTexture;
        private bool globalReady;
        public bool HasGlobalCoverage => globalReady;
        private bool clipmapActivated;
        private Vector2Int highFailureMinimum = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int mediumFailureMinimum = new Vector2Int(int.MinValue, int.MinValue);
        private float highRetryAfter;
        private float mediumRetryAfter;
        private string tracePath;

        [Serializable]
        public sealed class TierConfiguration
        {
            public string name;
            public int columns;
            public int rows;
            public int pagePixels;
            public int gutterPixels;
            public float tileInnerMeters;
            public float gridMinimumX;
            public float gridMinimumZ;
            public int bundleRegionCells;
            public float blendWidthMeters;
        }

        private sealed class TierRuntime
        {
            public TierConfiguration config;
            public Texture2DArray active;
            public Texture2DArray staging;
            public Vector2Int activeMinimum = new Vector2Int(int.MinValue, int.MinValue);
            public bool ready;
        }

        private void Awake()
        {
            if (trackedRig == null) trackedRig = GetComponent<AircraftReferenceFrameRig>();
            highRuntime = new TierRuntime { config = high };
            mediumRuntime = new TierRuntime { config = medium };
            tracePath = Path.Combine(Application.persistentDataPath, "questflight_aerial_clipmap_trace.jsonl");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Each development launch owns one unambiguous evidence stream. Appending across
            // APKs/runs would make a previous successful activation indistinguishable from the
            // exact candidate currently installed on Quest.
            if (writeDevelopmentTrace)
            {
                try { if (File.Exists(tracePath)) File.Delete(tracePath); }
                catch (Exception error)
                {
                    Debug.LogWarning("[QuestFlight][AerialClipmap] Trace reset failed: " + error.Message, this);
                }
            }
#endif
            SetConsumerFloat("_ClipmapEnabled", 0f);
            Trace("startup", $"arrays={SystemInfo.supports2DArrayTextures}; copy={SystemInfo.copyTextureSupport}; " +
                $"device={SystemInfo.graphicsDeviceName}; api={SystemInfo.graphicsDeviceType}");
            if (!TryValidateContract(out string report))
            {
                Debug.LogError("[QuestFlight][AerialClipmap] " + report, this);
                enabled = false;
            }
        }

        private IEnumerator Start()
        {
            while (enabled && !globalReady)
            {
                yield return LoadGlobalPage();
                if (!globalReady) yield return new WaitForSecondsRealtime(5f);
            }
            yield return RefreshWindow(force: true);
            while (enabled)
            {
                yield return new WaitForSecondsRealtime(movementPollSeconds);
                yield return RefreshWindow(force: false);
            }
        }

        private IEnumerator RefreshWindow(bool force)
        {
            if (refreshRunning || trackedRig == null) yield break;
            if (trackedRig.TrackedCamera == null) yield break;
            refreshRunning = true;
            Vector3 current = transform.position;
            float now = Time.realtimeSinceStartup;
            float elapsed = Mathf.Max(0.001f, now - previousSampleTime);
            Vector3 velocity = hasPreviousPosition
                ? (current - previousPosition) / elapsed
                : Vector3.zero;
            previousPosition = current;
            previousSampleTime = now;
            hasPreviousPosition = true;
            // Recovery/teleport operations can produce one enormous velocity sample. Cap
            // look-ahead to one high-tier cell so a discontinuous aircraft relocation can
            // never stream the opposite map edge before the next stable poll.
            Vector3 prediction = Vector3.ClampMagnitude(
                velocity * predictionSeconds,
                Mathf.Max(1f, high.tileInnerMeters));
            Vector3 predicted = current + prediction;
            try
            {
                yield return RefreshTier(mediumRuntime, predicted, force);
                yield return RefreshTier(highRuntime, predicted, force);
                bool ready = globalReady && highRuntime.ready && mediumRuntime.ready;
                if (ready && !clipmapActivated)
                {
                    // Commit the first complete pair atomically. Once active, transient read
                    // failures retain the previous complete arrays and must never expose the
                    // startup fallback again.
                    clipmapActivated = true;
                    // Both materials become visible to the render thread in the same
                    // frame; roofs can never expose a different imagery generation.
                    SetConsumerFloat("_ClipmapEnabled", 1f);
                    if (legacyFallbackMipController != null && legacyFallbackMipController.enabled)
                    {
                        legacyFallbackMipController.enabled = false;
                        Trace("legacy_fallback_released", "fixed-pyramid explicit mip requests cleared");
                    }
                }
            }
            finally
            {
                refreshRunning = false;
            }
        }

        private IEnumerator LoadGlobalPage()
        {
            string path = Path.Combine(Application.streamingAssetsPath, bundleDirectory, "qf_aerial_global");
            using (UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(path))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Trace("global_bundle_error", request.error);
                    yield break;
                }
                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
                try
                {
                    AssetBundleRequest assetRequest = bundle.LoadAssetAsync<Texture2D>("KBDU_2020_global_000_000");
                    yield return assetRequest;
                    globalTexture = assetRequest.asset as Texture2D;
                    if (globalTexture == null)
                    {
                        Trace("global_asset_error", "KBDU_2020_global_000_000");
                        yield break;
                    }
                    SetConsumerTexture("_ClipGlobalAlbedo", globalTexture);
                    globalReady = true;
                    Trace("global_ready", $"format={globalTexture.graphicsFormat}; mips={globalTexture.mipmapCount}");
                }
                finally
                {
                    bundle.Unload(globalTexture == null);
                }
            }
        }

        private IEnumerator RefreshTier(TierRuntime runtime, Vector3 predicted, bool force)
        {
            TierConfiguration tier = runtime.config;
            Vector2Int minimum = DesiredGridMinimum(tier, predicted.x, predicted.z);
            if (!force && runtime.ready && minimum == runtime.activeMinimum) yield break;
            Vector2Int failedMinimum = tier.name == "high" ? highFailureMinimum : mediumFailureMinimum;
            float retryAfter = tier.name == "high" ? highRetryAfter : mediumRetryAfter;
            if (!force && minimum == failedMinimum && Time.realtimeSinceStartup < retryAfter) yield break;

            float started = Time.realtimeSinceStartup;
            Dictionary<string, AssetBundle> bundles = new Dictionary<string, AssetBundle>();
            bool failed = false;
            int copied = 0;
            try
            {
                for (int localRow = 0; localRow < GridSize && !failed; localRow++)
                {
                    for (int localColumn = 0; localColumn < GridSize && !failed; localColumn++)
                    {
                        int column = minimum.x + localColumn;
                        int row = minimum.y + localRow;
                        int slice = localRow * GridSize + localColumn;
                        if (runtime.ready && CopyOverlappingPage(runtime, minimum, column, row, slice))
                        {
                            copied++;
                            continue;
                        }
                        string bundleName = BundleName(tier, column, row);
                        if (!bundles.TryGetValue(bundleName, out AssetBundle bundle))
                        {
                            string path = Path.Combine(Application.streamingAssetsPath, bundleDirectory, bundleName);
                            using (UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(path))
                            {
                                yield return request.SendWebRequest();
                                if (request.result != UnityWebRequest.Result.Success)
                                {
                                    Trace("bundle_error", $"tier={tier.name}; page={column},{row}; error={request.error}");
                                    failed = true;
                                    break;
                                }
                                bundle = DownloadHandlerAssetBundle.GetContent(request);
                                if (bundle == null)
                                {
                                    Trace("bundle_error", $"tier={tier.name}; page={column},{row}; error=null bundle");
                                    failed = true;
                                    break;
                                }
                                bundles.Add(bundleName, bundle);
                            }
                        }

                        string assetName = AssetName(tier, column, row);
                        AssetBundleRequest assetRequest = bundle.LoadAssetAsync<Texture2D>(assetName);
                        yield return assetRequest;
                        Texture2D page = assetRequest.asset as Texture2D;
                        if (page == null)
                        {
                            Trace("asset_error", $"tier={tier.name}; asset={assetName}");
                            failed = true;
                            break;
                        }
                        EnsureArrays(runtime, page);
                        int mipCount = Mathf.Min(page.mipmapCount, runtime.staging.mipmapCount);
                        for (int mip = 0; mip < mipCount; mip++)
                            Graphics.CopyTexture(page, 0, mip, runtime.staging, slice, mip);
                        copied++;
                    }
                }
                if (!failed && copied == SliceCount)
                {
                    // CopyTexture commands are queued on the render thread. Commit the grid
                    // only after a frame boundary has made every slice visible to the shader;
                    // bundle lifetime is independent because texture objects remain referenced
                    // until the explicit Unload(true) below.
                    yield return new WaitForEndOfFrame();
                }
            }
            finally
            {
                foreach (AssetBundle bundle in bundles.Values) bundle.Unload(true);
            }
            if (!failed && copied == SliceCount)
            {
                Texture2DArray swap = runtime.active;
                runtime.active = runtime.staging;
                runtime.staging = swap;
                runtime.activeMinimum = minimum;
                runtime.ready = true;
                BindTier(runtime);
                Trace("grid_ready", $"tier={tier.name}; min={minimum.x},{minimum.y}; " +
                    $"milliseconds={(Time.realtimeSinceStartup - started) * 1000f:0.0}");
                if (tier.name == "high")
                {
                    highFailureMinimum = new Vector2Int(int.MinValue, int.MinValue);
                    highRetryAfter = 0f;
                }
                else
                {
                    mediumFailureMinimum = new Vector2Int(int.MinValue, int.MinValue);
                    mediumRetryAfter = 0f;
                }
            }
            else if (failed)
            {
                // A streaming read can fail transiently during storage pressure. Keep the
                // previous complete array live, then retry this exact grid after a bounded
                // delay instead of permanently suppressing it until the aircraft moves.
                if (tier.name == "high")
                {
                    highFailureMinimum = minimum;
                    highRetryAfter = Time.realtimeSinceStartup + 5f;
                }
                else
                {
                    mediumFailureMinimum = minimum;
                    mediumRetryAfter = Time.realtimeSinceStartup + 5f;
                }
            }
        }

        private static bool CopyOverlappingPage(
            TierRuntime runtime,
            Vector2Int newMinimum,
            int column,
            int row,
            int destinationSlice)
        {
            int oldLocalColumn = column - runtime.activeMinimum.x;
            int oldLocalRow = row - runtime.activeMinimum.y;
            if (oldLocalColumn < 0 || oldLocalColumn >= GridSize ||
                oldLocalRow < 0 || oldLocalRow >= GridSize)
                return false;
            int sourceSlice = oldLocalRow * GridSize + oldLocalColumn;
            for (int mip = 0; mip < runtime.active.mipmapCount; mip++)
                Graphics.CopyTexture(runtime.active, sourceSlice, mip, runtime.staging, destinationSlice, mip);
            return true;
        }

        private static Vector2Int DesiredGridMinimum(TierConfiguration tier, float x, float z)
        {
            int centerColumn = Mathf.FloorToInt((x - tier.gridMinimumX) / tier.tileInnerMeters);
            int centerRow = Mathf.FloorToInt((z - tier.gridMinimumZ) / tier.tileInnerMeters);
            return new Vector2Int(
                Mathf.Clamp(centerColumn - 1, 0, tier.columns - GridSize),
                Mathf.Clamp(centerRow - 1, 0, tier.rows - GridSize));
        }

        private static string AssetName(TierConfiguration tier, int column, int row) =>
            $"KBDU_2020_{tier.name}_{column:000}_{row:000}";

        private static string BundleName(TierConfiguration tier, int column, int row) =>
            $"qf_aerial_{tier.name}_r{column / tier.bundleRegionCells:00}_{row / tier.bundleRegionCells:00}";

        private static void EnsureArrays(TierRuntime runtime, Texture2D page)
        {
            if (runtime.active != null && runtime.active.graphicsFormat == page.graphicsFormat) return;
            runtime.active = CreateArray(runtime.config.name + "_A", page);
            runtime.staging = CreateArray(runtime.config.name + "_B", page);
        }

        private static Texture2DArray CreateArray(string label, Texture2D page)
        {
            Texture2DArray array = new Texture2DArray(
                page.width, page.height, SliceCount, page.graphicsFormat,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain)
            {
                name = "KBDU_AerialClipmap_" + label,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            return array;
        }

        private void BindTier(TierRuntime runtime)
        {
            TierConfiguration tier = runtime.config;
            float minimumX = tier.gridMinimumX + runtime.activeMinimum.x * tier.tileInnerMeters;
            float minimumZ = tier.gridMinimumZ + runtime.activeMinimum.y * tier.tileInnerMeters;
            float scale = (tier.pagePixels - 2f * tier.gutterPixels) / tier.pagePixels;
            float offset = tier.gutterPixels / (float)tier.pagePixels;
            string prefix = tier.name == "high" ? "_ClipHigh" : "_ClipMedium";
            SetConsumerTexture(prefix + "Albedo", runtime.active);
            SetConsumerVector(prefix + "GridMinSize",
                new Vector4(minimumX, minimumZ, tier.tileInnerMeters, 1f / tier.tileInnerMeters));
            SetConsumerVector(prefix + "UvScaleOffsetBlend",
                new Vector4(scale, scale, offset, tier.blendWidthMeters));
        }

        private void SetConsumerTexture(string propertyName, Texture value)
        {
            terrainMaterial?.SetTexture(propertyName, value);
            buildingRoofMaterial?.SetTexture(propertyName, value);
        }

        private void SetConsumerVector(string propertyName, Vector4 value)
        {
            terrainMaterial?.SetVector(propertyName, value);
            buildingRoofMaterial?.SetVector(propertyName, value);
        }

        private void SetConsumerFloat(string propertyName, float value)
        {
            terrainMaterial?.SetFloat(propertyName, value);
            buildingRoofMaterial?.SetFloat(propertyName, value);
        }

        private void OnDisable()
        {
            refreshRunning = false;
            SetConsumerFloat("_ClipmapEnabled", 0f);
            clipmapActivated = false;
            DestroyArray(highRuntime);
            DestroyArray(mediumRuntime);
            if (globalTexture != null) Destroy(globalTexture);
        }

        private static void DestroyArray(TierRuntime runtime)
        {
            if (runtime == null) return;
            if (runtime.active != null) Destroy(runtime.active);
            if (runtime.staging != null) Destroy(runtime.staging);
        }

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            if (!SystemInfo.supports2DArrayTextures) failures.Add("Texture2DArray unsupported");
            if (trackedRig == null || trackedRig.gameObject != gameObject)
                failures.Add("controller is not authored on the aircraft reference-frame root");
            if (terrainMaterial == null || terrainMaterial.shader == null ||
                terrainMaterial.shader.name != "QuestFlight/KBDU Terrain Pyramid")
                failures.Add("terrain material authority is missing or wrong");
            if (buildingRoofMaterial == null || buildingRoofMaterial.shader == null ||
                buildingRoofMaterial.shader.name != "QuestFlight/Full Map Building Clipmap")
                failures.Add("projected-roof building material is missing or wrong");
            if (legacyFallbackMipController == null ||
                legacyFallbackMipController.gameObject != gameObject)
                failures.Add("serialized fixed-pyramid startup fallback is missing or on another root");
            ValidateTier(high, failures);
            ValidateTier(medium, failures);
            if (!string.Equals(authoredContractVersion, ContractVersion, StringComparison.Ordinal))
                failures.Add("contract version is stale");
            report = failures.Count == 0 ? "PASS aircraft-centered 3x3 page contract" : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static void ValidateTier(TierConfiguration tier, List<string> failures)
        {
            if (tier == null || string.IsNullOrWhiteSpace(tier.name) || tier.columns < GridSize ||
                tier.rows < GridSize || tier.pagePixels <= 0 || tier.gutterPixels < 0 ||
                tier.tileInnerMeters <= 0f || tier.bundleRegionCells <= 0)
                failures.Add("invalid clipmap tier configuration");
        }

        private void Trace(string eventName, string details)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!writeDevelopmentTrace) return;
            string line = $"{{\"time\":{Time.realtimeSinceStartup:F3},\"event\":\"{eventName}\",\"details\":\"{details.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}\n";
            try { File.AppendAllText(tracePath, line); }
            catch (Exception error) { Debug.LogWarning("[QuestFlight][AerialClipmap] Trace failed: " + error.Message, this); }
#endif
        }
    }
}
