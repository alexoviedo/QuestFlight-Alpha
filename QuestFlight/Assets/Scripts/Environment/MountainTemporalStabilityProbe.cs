using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    [Serializable]
    public sealed class MountainRendererBaseline
    {
        public string objectName;
        public string parentHierarchy;
        public int rendererInstanceId;
        public bool activeSelf;
        public bool activeInHierarchy;
        public bool rendererEnabled;
        public bool forceRenderingOff;
        public long localToWorldMatrixHash;
        public int meshInstanceId;
        public string meshName;
        public int vertexCount;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public int materialInstanceId;
        public string materialName;
        public string shaderName;
        public Vector3 localScale;
        public Vector3 lossyScale;
        public bool lodGroupPresent;
        public bool lodGroupEnabled;
        public int lodFadeMode;
        public int rendererLodIndex;
        public bool distanceCullerPresent;
        public bool cameraFacingOrImpostor;
        public bool ditherKeywordOrShader;
        public bool staticGameObject;
    }

    [Serializable]
    public struct MountainRendererFrameSample
    {
        public int frameSequence;
        public int unityFrameCount;
        public float elapsedSeconds;
        public string objectName;
        public string parentHierarchy;
        public bool activeSelf;
        public bool activeInHierarchy;
        public bool rendererEnabled;
        public bool forceRenderingOff;
        public long localToWorldMatrixHash;
        public int meshInstanceId;
        public int vertexCount;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public int materialInstanceId;
        public Vector3 localScale;
        public Vector3 lossyScale;
        public bool lodGroupPresent;
        public bool lodGroupEnabled;
        public int lodFadeMode;
        public int activeLodIndex;
        public float cameraDistanceMeters;
        public bool insideCameraFarClip;
        public bool rendererReportedVisible;
    }

    [Serializable]
    public sealed class MountainTemporalStabilityReport
    {
        public int schemaVersion = 1;
        public string generatedUtc;
        public string captureStartedUtc;
        public string captureEndedUtc;
        public string unityVersion;
        public string platform;
        public string graphicsDevice;
        public string runtimeEnvironmentRoot;
        public string authoritativeMountainRoot;
        public bool realDataRootActive;
        public bool proceduralFallbackActive;
        public int expectedRendererCount;
        public int sampledFrameCount;
        public int rendererSampleCount;
        public float capturedActiveSeconds;
        public int activeLegacyMountainRendererCount;
        public bool immutableTransforms;
        public bool immutableMeshes;
        public bool immutableRendererSet;
        public bool noTerrainLodOrDither;
        public bool noCameraFacingOrDistanceScaling;
        public bool oneMountainSource;
        public bool passed;
        public string classification;
        public string evidencePath;
        public List<MountainRendererBaseline> rendererBaselines = new List<MountainRendererBaseline>();
        public List<MountainRendererFrameSample> frameSamples = new List<MountainRendererFrameSample>();
        public List<string> violations = new List<string>();
        public List<string> limitations = new List<string>();
    }

    /// <summary>
    /// Records the actual far-terrain renderer state during a seated head sweep. The probe never
    /// changes a transform, renderer, mesh, material, LOD, or camera; it only witnesses them.
    /// Detailed sampling starts after a 90-second active warmup in Android players and runs for
    /// 60 active seconds, matching the Quest temporal gate.
    /// </summary>
    public sealed class MountainTemporalStabilityProbe : MonoBehaviour
    {
        public const string EvidenceDirectoryName = "mountain_temporal_stability";
        public const float DefaultWarmupSeconds = 90f;
        public const float DefaultCaptureSeconds = 60f;

        public bool autoCaptureInPlayer = true;
        public float automaticWarmupSeconds = DefaultWarmupSeconds;
        public float automaticCaptureSeconds = DefaultCaptureSeconds;

        public bool IsCapturing => _capturing;
        public MountainTemporalStabilityReport LastReport { get; private set; }
        public string LatestEvidencePath { get; private set; } = string.Empty;

        private sealed class RendererBinding
        {
            public Renderer Renderer;
            public MeshFilter MeshFilter;
            public Transform InitialParent;
            public LODGroup LodGroup;
            public LOD[] Lods;
            public MountainRendererBaseline Baseline;
        }

        private readonly List<RendererBinding> _bindings = new List<RendererBinding>();
        private readonly HashSet<string> _violations = new HashSet<string>(StringComparer.Ordinal);
        private Transform _authoritativeRoot;
        private Transform _environmentRoot;
        private Camera _camera;
        private bool _initialized;
        private bool _capturing;
        private bool _automaticCaptureFinished;
        private float _activeWarmupSeconds;
        private float _captureActiveSeconds;
        private int _sampledFrames;
        private string _captureStartedUtc;

        private void Awake()
        {
            Initialize(transform, transform.parent);
        }

        private void Update()
        {
            if (!_initialized) Initialize(transform, transform.parent);

            if (!_capturing && !_automaticCaptureFinished && autoCaptureInPlayer && !Application.isEditor)
            {
                _camera = ResolveCamera(_camera);
                if (_camera != null && _authoritativeRoot != null && _authoritativeRoot.gameObject.activeInHierarchy)
                {
                    _activeWarmupSeconds += Time.unscaledDeltaTime;
                    if (_activeWarmupSeconds >= Mathf.Max(0f, automaticWarmupSeconds))
                    {
                        BeginCapture(_camera);
                    }
                }
            }

            if (!_capturing) return;
            _captureActiveSeconds += Time.unscaledDeltaTime;
            CaptureFrameNow();
            if (_captureActiveSeconds >= Mathf.Max(0.1f, automaticCaptureSeconds))
            {
                EndCapture(true);
                _automaticCaptureFinished = true;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _capturing && _sampledFrames > 0)
            {
                EndCapture(true);
                _automaticCaptureFinished = true;
            }
        }

        public void Initialize(Transform authoritativeRoot, Transform environmentRoot)
        {
            _authoritativeRoot = authoritativeRoot != null ? authoritativeRoot : transform;
            _environmentRoot = environmentRoot != null ? environmentRoot : _authoritativeRoot.parent;
            _bindings.Clear();
            _violations.Clear();

            Renderer[] renderers = _authoritativeRoot
                .GetComponentsInChildren<Renderer>(true)
                .Where(IsAuthoritativeTerrainRenderer)
                .OrderBy(renderer => HierarchyPath(renderer.transform), StringComparer.Ordinal)
                .ToArray();

            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                LODGroup lodGroup = FindLodGroup(renderer.transform, _authoritativeRoot);
                LOD[] lods = lodGroup != null ? lodGroup.GetLODs() : null;
                MountainRendererBaseline baseline = CreateBaseline(renderer, filter, lodGroup, lods);
                _bindings.Add(new RendererBinding
                {
                    Renderer = renderer,
                    MeshFilter = filter,
                    InitialParent = renderer.transform.parent,
                    LodGroup = lodGroup,
                    Lods = lods,
                    Baseline = baseline
                });
                ValidateBaseline(baseline);
            }

            if (_bindings.Count < 2)
            {
                AddViolation($"Expected at least mid/far authoritative terrain renderers; found {_bindings.Count}.");
            }
            ScanRendererSetAndLegacySources();
            _initialized = true;
        }

        public void BeginCapture(Camera camera = null)
        {
            if (!_initialized) Initialize(transform, transform.parent);
            _camera = ResolveCamera(camera);
            _captureStartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            _captureActiveSeconds = 0f;
            _sampledFrames = 0;
            _capturing = true;

            LastReport = CreateReport();
            int expectedSamples = Mathf.CeilToInt(Mathf.Max(1f, automaticCaptureSeconds) * 90f * Mathf.Max(1, _bindings.Count));
            LastReport.frameSamples = new List<MountainRendererFrameSample>(expectedSamples);
        }

        public void CaptureFrameNow()
        {
            if (!_capturing) BeginCapture(_camera);
            _camera = ResolveCamera(_camera);
            _sampledFrames++;

            if (_sampledFrames == 1 || _sampledFrames % 30 == 0)
            {
                ScanRendererSetAndLegacySources();
            }

            foreach (RendererBinding binding in _bindings)
            {
                RecordRendererFrame(binding);
            }
        }

        public MountainTemporalStabilityReport EndCapture(bool writeEvidence)
        {
            if (!_initialized) Initialize(transform, transform.parent);
            if (LastReport == null) LastReport = CreateReport();
            ScanRendererSetAndLegacySources();
            _capturing = false;

            LastReport.generatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            LastReport.captureStartedUtc = _captureStartedUtc;
            LastReport.captureEndedUtc = LastReport.generatedUtc;
            LastReport.sampledFrameCount = _sampledFrames;
            LastReport.rendererSampleCount = LastReport.frameSamples.Count;
            LastReport.capturedActiveSeconds = _captureActiveSeconds;
            LastReport.activeLegacyMountainRendererCount = CountActiveLegacyMountainRenderers();
            LastReport.violations = _violations.OrderBy(value => value, StringComparer.Ordinal).ToList();
            PopulateClassification(LastReport);

            if (writeEvidence)
            {
                WriteEvidence(LastReport);
            }
            return LastReport;
        }

        public MountainTemporalStabilityReport CaptureSingleFrameForTest(Camera camera = null)
        {
            BeginCapture(camera);
            CaptureFrameNow();
            return EndCapture(false);
        }

        public static bool IsLegacyMountainName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return false;
            string normalized = objectName.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            return normalized.StartsWith("frontrange", StringComparison.Ordinal) ||
                   normalized.StartsWith("foothillridge", StringComparison.Ordinal) ||
                   normalized.Contains("mountainridge") ||
                   normalized.Contains("ridgeimpostor") ||
                   normalized.Contains("mountainimpostor") ||
                   normalized.Contains("proceduralmountain") ||
                   normalized.Contains("snowcap") ||
                   normalized.Contains("hazecube") ||
                   normalized.Contains("hazeband");
        }

        private MountainTemporalStabilityReport CreateReport()
        {
            bool productionActive = _authoritativeRoot != null &&
                                    (_authoritativeRoot.GetComponent<ProductionEnvironmentRoot>() != null ||
                                     _authoritativeRoot.GetComponentInParent<ProductionEnvironmentRoot>() != null);
            bool realActive = _authoritativeRoot != null && _authoritativeRoot.gameObject.activeInHierarchy && productionActive;
            MountainTemporalStabilityReport report = new MountainTemporalStabilityReport
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                runtimeEnvironmentRoot = _environmentRoot != null ? HierarchyPath(_environmentRoot) : string.Empty,
                authoritativeMountainRoot = _authoritativeRoot != null ? HierarchyPath(_authoritativeRoot) : string.Empty,
                realDataRootActive = realActive,
                proceduralFallbackActive = FindActiveProceduralFallback() != null,
                expectedRendererCount = _bindings.Count,
                rendererBaselines = _bindings.Select(binding => binding.Baseline).ToList()
            };
            report.limitations.Add("Transform/mesh/renderer immutability is machine-verified; perceived silhouette stability still requires the recorded stereo headset sweep and human witness.");
            report.limitations.Add("Renderer.isVisible is diagnostic only and may change normally with the camera frustum; it is not treated as an enabled-renderer mutation.");
            if (productionActive)
                report.limitations.Add("The authored production USGS near/mid/far mesh set is treated as the real-data authority even though it intentionally does not use the legacy RealKBDU runtime root name.");
            return report;
        }

        private void RecordRendererFrame(RendererBinding binding)
        {
            Renderer renderer = binding.Renderer;
            if (renderer == null)
            {
                AddViolation($"Authoritative terrain renderer was destroyed: {binding.Baseline.parentHierarchy}.");
                return;
            }

            Mesh mesh = binding.MeshFilter != null ? binding.MeshFilter.sharedMesh : null;
            Material material = renderer.sharedMaterial;
            long matrixHash = HashMatrix(renderer.localToWorldMatrix);
            Bounds bounds = renderer.bounds;
            string hierarchy = renderer.transform.parent == binding.InitialParent
                ? binding.Baseline.parentHierarchy
                : HierarchyPath(renderer.transform);
            int meshId = mesh != null ? mesh.GetInstanceID() : 0;
            int vertexCount = mesh != null ? mesh.vertexCount : 0;
            int materialId = material != null ? material.GetInstanceID() : 0;
            LODGroup currentLod = FindLodGroup(renderer.transform, _authoritativeRoot);

            if (matrixHash != binding.Baseline.localToWorldMatrixHash)
                AddViolation($"Transform/matrix changed for {hierarchy}.");
            if (renderer.transform.parent != binding.InitialParent)
                AddViolation($"Parent hierarchy changed for {binding.Baseline.parentHierarchy} -> {hierarchy}.");
            if (meshId != binding.Baseline.meshInstanceId || vertexCount != binding.Baseline.vertexCount)
                AddViolation($"Mesh identity/vertex count changed for {hierarchy}: {binding.Baseline.meshInstanceId}/{binding.Baseline.vertexCount} -> {meshId}/{vertexCount}.");
            if (!Approximately(bounds.center, binding.Baseline.boundsCenter) || !Approximately(bounds.size, binding.Baseline.boundsSize))
                AddViolation($"World bounds changed for {hierarchy}.");
            if (materialId != binding.Baseline.materialInstanceId)
                AddViolation($"Material identity changed for {hierarchy}: {binding.Baseline.materialInstanceId} -> {materialId}.");
            if (renderer.enabled != binding.Baseline.rendererEnabled ||
                renderer.gameObject.activeSelf != binding.Baseline.activeSelf ||
                renderer.gameObject.activeInHierarchy != binding.Baseline.activeInHierarchy ||
                renderer.forceRenderingOff != binding.Baseline.forceRenderingOff)
                AddViolation($"Enabled/active renderer state changed for {hierarchy}.");
            if (!Approximately(renderer.transform.localScale, binding.Baseline.localScale) ||
                !Approximately(renderer.transform.lossyScale, binding.Baseline.lossyScale))
                AddViolation($"Scale changed for {hierarchy}.");
            if (currentLod != binding.LodGroup)
                AddViolation($"LODGroup binding changed for {hierarchy}.");

            float cameraDistance = 0f;
            bool insideFarClip = true;
            bool reportedVisible = renderer.isVisible;
            if (_camera != null)
            {
                Vector3 closest = bounds.ClosestPoint(_camera.transform.position);
                cameraDistance = Vector3.Distance(_camera.transform.position, closest);
                insideFarClip = cameraDistance <= _camera.farClipPlane;
            }

            LastReport.frameSamples.Add(new MountainRendererFrameSample
            {
                frameSequence = _sampledFrames,
                unityFrameCount = Time.frameCount,
                elapsedSeconds = _captureActiveSeconds,
                objectName = renderer.name,
                parentHierarchy = hierarchy,
                activeSelf = renderer.gameObject.activeSelf,
                activeInHierarchy = renderer.gameObject.activeInHierarchy,
                rendererEnabled = renderer.enabled,
                forceRenderingOff = renderer.forceRenderingOff,
                localToWorldMatrixHash = matrixHash,
                meshInstanceId = meshId,
                vertexCount = vertexCount,
                boundsCenter = bounds.center,
                boundsSize = bounds.size,
                materialInstanceId = materialId,
                localScale = renderer.transform.localScale,
                lossyScale = renderer.transform.lossyScale,
                lodGroupPresent = currentLod != null,
                lodGroupEnabled = currentLod != null && currentLod.enabled,
                lodFadeMode = currentLod != null ? (int)currentLod.fadeMode : -1,
                activeLodIndex = EstimateActiveLodIndex(binding, _camera, cameraDistance),
                cameraDistanceMeters = cameraDistance,
                insideCameraFarClip = insideFarClip,
                rendererReportedVisible = reportedVisible
            });
        }

        private void ScanRendererSetAndLegacySources()
        {
            if (_authoritativeRoot == null) return;
            Renderer[] current = _authoritativeRoot.GetComponentsInChildren<Renderer>(true)
                .Where(IsAuthoritativeTerrainRenderer)
                .ToArray();
            HashSet<int> expected = new HashSet<int>(_bindings.Where(binding => binding.Renderer != null).Select(binding => binding.Renderer.GetInstanceID()));
            HashSet<int> actual = new HashSet<int>(current.Select(renderer => renderer.GetInstanceID()));
            if (!expected.SetEquals(actual))
            {
                AddViolation($"Authoritative terrain renderer set changed: expected={expected.Count} actual={actual.Count}.");
            }

            int activeLegacy = CountActiveLegacyMountainRenderers();
            if (activeLegacy > 0)
            {
                AddViolation($"Detected {activeLegacy} active legacy ridge/snow/haze/impostor renderer(s) while real terrain is authoritative.");
            }
            if (FindActiveProceduralFallback() != null)
            {
                AddViolation("Procedural fallback root is active at the same time as the real-data mountain root.");
            }
        }

        private void ValidateBaseline(MountainRendererBaseline baseline)
        {
            if (baseline.meshInstanceId == 0 || baseline.vertexCount <= 0)
                AddViolation($"Authoritative terrain has no fixed mesh: {baseline.parentHierarchy}.");
            if (!baseline.activeInHierarchy || !baseline.rendererEnabled || baseline.forceRenderingOff)
                AddViolation($"Authoritative terrain is not continuously enabled: {baseline.parentHierarchy}.");
            if (baseline.lodGroupPresent)
                AddViolation($"Terrain LODGroup is forbidden for temporal stability: {baseline.parentHierarchy}.");
            if (baseline.distanceCullerPresent)
                AddViolation($"Terrain distance culler is forbidden for temporal stability: {baseline.parentHierarchy}.");
            if (baseline.cameraFacingOrImpostor)
                AddViolation($"Camera-facing/billboard/impostor terrain is forbidden: {baseline.parentHierarchy}.");
            if (baseline.ditherKeywordOrShader)
                AddViolation($"Dither/crossfade material path is forbidden on terrain: {baseline.parentHierarchy}.");
            if (!baseline.staticGameObject)
                AddViolation($"Authoritative terrain object is not marked static: {baseline.parentHierarchy}.");
        }

        private MountainRendererBaseline CreateBaseline(Renderer renderer, MeshFilter filter, LODGroup lodGroup, LOD[] lods)
        {
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Material material = renderer.sharedMaterial;
            Bounds bounds = renderer.bounds;
            return new MountainRendererBaseline
            {
                objectName = renderer.name,
                parentHierarchy = HierarchyPath(renderer.transform),
                rendererInstanceId = renderer.GetInstanceID(),
                activeSelf = renderer.gameObject.activeSelf,
                activeInHierarchy = renderer.gameObject.activeInHierarchy,
                rendererEnabled = renderer.enabled,
                forceRenderingOff = renderer.forceRenderingOff,
                localToWorldMatrixHash = HashMatrix(renderer.localToWorldMatrix),
                meshInstanceId = mesh != null ? mesh.GetInstanceID() : 0,
                meshName = mesh != null ? mesh.name : string.Empty,
                vertexCount = mesh != null ? mesh.vertexCount : 0,
                boundsCenter = bounds.center,
                boundsSize = bounds.size,
                materialInstanceId = material != null ? material.GetInstanceID() : 0,
                materialName = material != null ? material.name : string.Empty,
                shaderName = material != null && material.shader != null ? material.shader.name : string.Empty,
                localScale = renderer.transform.localScale,
                lossyScale = renderer.transform.lossyScale,
                lodGroupPresent = lodGroup != null,
                lodGroupEnabled = lodGroup != null && lodGroup.enabled,
                lodFadeMode = lodGroup != null ? (int)lodGroup.fadeMode : -1,
                rendererLodIndex = FindRendererLodIndex(lods, renderer),
                distanceCullerPresent = renderer.GetComponents<MonoBehaviour>().Any(component =>
                    component != null && component.GetType().Name == "RealKbduBatchDistanceCuller"),
                cameraFacingOrImpostor = IsCameraFacingOrImpostor(renderer),
                ditherKeywordOrShader = UsesDither(material),
                staticGameObject = renderer.gameObject.isStatic
            };
        }

        private static bool IsAuthoritativeTerrainRenderer(Renderer renderer)
        {
            if (renderer == null) return false;
            return renderer.name.StartsWith("RealTerrain_", StringComparison.Ordinal) ||
                   renderer.name.StartsWith("Terrain_AirportPatch_USGS", StringComparison.Ordinal) ||
                   renderer.name.StartsWith("Terrain_Near4km_USGS", StringComparison.Ordinal) ||
                   renderer.name.StartsWith("Terrain_Mid12km_USGS", StringComparison.Ordinal) ||
                   renderer.name.StartsWith("Terrain_Far24km_Immutable_USGS", StringComparison.Ordinal);
        }

        private int CountActiveLegacyMountainRenderers()
        {
            Transform sceneRoot = _environmentRoot != null ? _environmentRoot.root : _authoritativeRoot != null ? _authoritativeRoot.root : null;
            if (sceneRoot == null) return 0;
            int count = 0;
            foreach (Renderer renderer in sceneRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || (_authoritativeRoot != null && renderer.transform.IsChildOf(_authoritativeRoot))) continue;
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (IsLegacyMountainName(renderer.name)) count++;
            }
            return count;
        }

        private Transform FindActiveProceduralFallback()
        {
            Transform sceneRoot = _environmentRoot != null ? _environmentRoot.root : _authoritativeRoot != null ? _authoritativeRoot.root : null;
            if (sceneRoot == null) return null;
            foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.gameObject.activeInHierarchy &&
                    string.Equals(candidate.name, "KBDU_Inspired_Expanded_World_NotForNavigation", StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        private static LODGroup FindLodGroup(Transform candidate, Transform stopAt)
        {
            Transform current = candidate;
            while (current != null)
            {
                LODGroup group = current.GetComponent<LODGroup>();
                if (group != null) return group;
                if (current == stopAt) break;
                current = current.parent;
            }
            return null;
        }

        private static int FindRendererLodIndex(LOD[] lods, Renderer renderer)
        {
            if (lods == null) return -1;
            for (int index = 0; index < lods.Length; index++)
            {
                Renderer[] members = lods[index].renderers;
                if (members != null && Array.IndexOf(members, renderer) >= 0) return index;
            }
            return -1;
        }

        private static int EstimateActiveLodIndex(RendererBinding binding, Camera camera, float cameraDistance)
        {
            if (binding.LodGroup == null || binding.Lods == null || camera == null || !binding.LodGroup.enabled) return -1;
            float scale = Mathf.Max(
                Mathf.Abs(binding.LodGroup.transform.lossyScale.x),
                Mathf.Abs(binding.LodGroup.transform.lossyScale.y),
                Mathf.Abs(binding.LodGroup.transform.lossyScale.z));
            float relativeHeight;
            if (camera.orthographic)
                relativeHeight = binding.LodGroup.size * scale / Mathf.Max(0.001f, camera.orthographicSize * 2f);
            else
                relativeHeight = binding.LodGroup.size * scale /
                                 Mathf.Max(0.001f, cameraDistance * 2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            relativeHeight *= QualitySettings.lodBias;
            for (int index = 0; index < binding.Lods.Length; index++)
            {
                if (relativeHeight >= binding.Lods[index].screenRelativeTransitionHeight) return index;
            }
            return -1;
        }

        private static Camera ResolveCamera(Camera candidate)
        {
            if (candidate != null && candidate.isActiveAndEnabled) return candidate;
            return Camera.main;
        }

        private static bool IsCameraFacingOrImpostor(Renderer renderer)
        {
            if (renderer is BillboardRenderer) return true;
            string normalized = renderer.name.ToLowerInvariant();
            if (normalized.Contains("billboard") || normalized.Contains("impostor")) return true;
            foreach (MonoBehaviour behaviour in renderer.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null) continue;
                string typeName = behaviour.GetType().Name.ToLowerInvariant();
                if (typeName.Contains("billboard") || typeName.Contains("lookatcamera") || typeName.Contains("camerafacing")) return true;
            }
            return false;
        }

        private static bool UsesDither(Material material)
        {
            if (material == null) return false;
            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            return material.IsKeywordEnabled("LOD_FADE_CROSSFADE") ||
                   material.name.IndexOf("dither", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   shaderName.IndexOf("dither", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static long HashMatrix(Matrix4x4 matrix)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                for (int index = 0; index < 16; index++)
                {
                    hash ^= matrix[index].GetHashCode();
                    hash *= 1099511628211L;
                }
                return hash;
            }
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude <= 0.000001f;
        }

        private static string HierarchyPath(Transform candidate)
        {
            if (candidate == null) return string.Empty;
            Stack<string> names = new Stack<string>();
            Transform current = candidate;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private void AddViolation(string violation)
        {
            if (!string.IsNullOrWhiteSpace(violation)) _violations.Add(violation);
        }

        private static void PopulateClassification(MountainTemporalStabilityReport report)
        {
            report.immutableTransforms = !report.violations.Any(value =>
                value.IndexOf("Transform", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("bounds changed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Scale changed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Parent hierarchy", StringComparison.OrdinalIgnoreCase) >= 0);
            report.immutableMeshes = !report.violations.Any(value =>
                value.IndexOf("Mesh", StringComparison.OrdinalIgnoreCase) >= 0);
            report.immutableRendererSet = !report.violations.Any(value =>
                value.IndexOf("renderer set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("renderer state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("destroyed", StringComparison.OrdinalIgnoreCase) >= 0);
            report.noTerrainLodOrDither = !report.violations.Any(value =>
                value.IndexOf("LOD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Dither", StringComparison.OrdinalIgnoreCase) >= 0);
            report.noCameraFacingOrDistanceScaling = !report.violations.Any(value =>
                value.IndexOf("camera-facing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("distance culler", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Scale changed", StringComparison.OrdinalIgnoreCase) >= 0);
            report.oneMountainSource = report.activeLegacyMountainRendererCount == 0 && !report.proceduralFallbackActive &&
                                       !report.violations.Any(value => value.IndexOf("legacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                      value.IndexOf("fallback root", StringComparison.OrdinalIgnoreCase) >= 0);
            report.passed = report.realDataRootActive && report.immutableTransforms && report.immutableMeshes &&
                            report.immutableRendererSet && report.noTerrainLodOrDither &&
                            report.noCameraFacingOrDistanceScaling && report.oneMountainSource &&
                            report.sampledFrameCount > 0;
            report.classification = report.passed ? "PASS_MACHINE_INVARIANTS_HUMAN_WITNESS_REQUIRED" : "FAIL";
        }

        private void WriteEvidence(MountainTemporalStabilityReport report)
        {
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "QuestFlight", EvidenceDirectoryName);
                Directory.CreateDirectory(directory);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string stem = "mountain_temporal_stability_" + stamp;
                string jsonPath = Path.Combine(directory, stem + ".json");
                report.evidencePath = jsonPath;
                File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(directory, stem + ".md"), BuildMarkdown(report));
                LatestEvidencePath = jsonPath;
                Debug.Log($"[QuestFlightLab][MountainTemporalStability] {report.classification} frames={report.sampledFrameCount} samples={report.rendererSampleCount} evidence={jsonPath}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[QuestFlightLab][MountainTemporalStability] Evidence write failed: {exception.Message}");
            }
        }

        private static string BuildMarkdown(MountainTemporalStabilityReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Mountain Temporal Stability Probe");
            builder.AppendLine();
            builder.AppendLine($"- Classification: `{report.classification}`");
            builder.AppendLine($"- Runtime root: `{report.runtimeEnvironmentRoot}`");
            builder.AppendLine($"- Authoritative root: `{report.authoritativeMountainRoot}`");
            builder.AppendLine($"- Frames/render samples: {report.sampledFrameCount}/{report.rendererSampleCount}");
            builder.AppendLine($"- Active capture: {report.capturedActiveSeconds.ToString("0.00", CultureInfo.InvariantCulture)} seconds");
            builder.AppendLine($"- Immutable transforms/meshes/renderer set: {report.immutableTransforms}/{report.immutableMeshes}/{report.immutableRendererSet}");
            builder.AppendLine($"- No LOD/dither/camera-facing/distance scaling: {report.noTerrainLodOrDither}/{report.noCameraFacingOrDistanceScaling}");
            builder.AppendLine($"- One mountain source: {report.oneMountainSource}; active legacy renderers={report.activeLegacyMountainRendererCount}");
            builder.AppendLine();
            builder.AppendLine("## Violations");
            builder.AppendLine();
            if (report.violations.Count == 0) builder.AppendLine("- None.");
            foreach (string violation in report.violations) builder.AppendLine("- " + violation);
            builder.AppendLine();
            builder.AppendLine("## Limitations");
            builder.AppendLine();
            foreach (string limitation in report.limitations) builder.AppendLine("- " + limitation);
            return builder.ToString();
        }
    }
}
