using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Runtime
{
    [Serializable]
    public struct QuestTemporalFrameSample
    {
        public int sequence;
        public int unityFrameCount;
        public float elapsedSeconds;
        public float frameMs;
        public bool frameTimingAvailable;
        public float cpuFrameMs;
        public float gpuFrameMs;
        public long gcAllocatedBytes;
        public long runtimeDrawCalls;
        public long runtimeTriangles;
    }

    [Serializable]
    public struct RenderBudgetSnapshot
    {
        public int activeRendererCount;
        public int estimatedInstancedDrawCalls;
        public long estimatedFrustumTriangles;
        public int uniqueMaterialCount;
    }

    public static class QuestRenderBudgetAudit
    {
        public static RenderBudgetSnapshot Capture(Camera camera)
        {
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            Plane[] frustum = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
            Renderer[] visible = frustum == null
                ? renderers
                : renderers.Where(renderer => GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)).ToArray();
            HashSet<Material> materials = new HashSet<Material>();
            long triangles = 0;
            foreach (Renderer renderer in visible)
            {
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) materials.Add(material);
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter?.sharedMesh == null) continue;
                for (int subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                    triangles += (long)filter.sharedMesh.GetIndexCount(subMesh) / 3L;
            }
            return new RenderBudgetSnapshot
            {
                activeRendererCount = renderers.Length,
                estimatedInstancedDrawCalls = visible.Length,
                estimatedFrustumTriangles = triangles,
                uniqueMaterialCount = materials.Count
            };
        }
    }

    [Serializable]
    public sealed class QuestTemporalVisualGateReport
    {
        public int schemaVersion = 3;
        public string generatedUtc;
        public string unityVersion;
        public string platform;
        public string deviceModel;
        public string graphicsDeviceType;
        public string graphicsDeviceName;
        public string sceneryMode = ProductionVerticalSliceRoot.ArchitectureVersion;
        public bool productionVerticalSliceActive;
        public string productionArchitectureVersion;
        public bool authoredProductionHierarchyValid;
        public bool productionEnvironmentContractValid;
        public bool legacyRuntimeRepairAbsent;
        public string authoritativePhysicsBackend;
        public bool readinessPrerequisitesMet;
        public string readinessStatus;
        public float readinessWaitSeconds;
        public float warmupSeconds;
        public float requestedMeasurementSeconds;
        public float measuredSeconds;
        public int sampleCount;
        public float averageFrameMs;
        public float minFrameMs;
        public float maxFrameMs;
        public float p95FrameMs;
        public float p99FrameMs;
        public float estimatedFps;
        public int framesOver72HzBudget;
        public float framesOver72HzBudgetRatio;
        public string deliveredIntervalSemantics;
        public float averageDeliveredIntervalMs;
        public float p95DeliveredIntervalMs;
        public string workloadTimingSemantics;
        public int workloadTimingSamples;
        public float averageWorkloadMs;
        public float p95WorkloadMs;
        public int workloadSamplesOver72HzBudget;
        public float workloadSamplesOver72HzBudgetRatio;
        public bool frameTimingManagerAvailable;
        public int frameTimingManagerSamples;
        public int gpuTimingSamples;
        public float averageCpuFrameMs;
        public float p95CpuFrameMs;
        public float averageGpuFrameMs;
        public float p95GpuFrameMs;
        public bool allocationRecorderAvailable;
        public long averageGcAllocatedBytesPerFrame;
        public long maxGcAllocatedBytesPerFrame;
        public RenderBudgetSnapshot renderBudget;
        public int activeRendererCount;
        public int sceneMaterialCount;
        public int performanceDrawCallGate = QuestTemporalVisualGateRecorder.ProductionDrawCallGate;
        public long performanceVisibleTriangleGate = QuestTemporalVisualGateRecorder.ProductionVisibleTriangleGate;
        public bool hardPerformanceGatePassed;
        public string hardPerformanceGateSemantics;
        public bool deliveredCadencePerformanceGatePassed;
        public bool workloadPerformanceGatePassed;
        public bool mountainTemporalGatePassed;
        public string mountainTemporalClassification;
        public string mountainTemporalEvidencePath;
        public string evidencePath;
        public List<QuestTemporalFrameSample> frameSamples = new List<QuestTemporalFrameSample>();
        public List<string> limitations = new List<string>();
    }

    /// <summary>
    /// Product-only temporal observer. It is serialized on ProductionWorldRoot and activates only
    /// for the host acceptance intent; it never constructs hierarchy or writes tracked transforms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestTemporalVisualGateRecorder : MonoBehaviour
    {
        public const string EvidenceDirectoryName = "temporal_visual_gate";
        public const float DefaultReadinessTimeoutSeconds = 90f;
        public const float DefaultWarmupSeconds = 90f;
        public const float DefaultMeasurementSeconds = 60f;
        public const float Quest72HzFrameBudgetMs = 1000f / 72f;
        public const float QuestAverageFrameGateMs = 13.2f;
        public const float QuestOverBudgetGateRatio = 0.05f;
        public const int ProductionDrawCallGate = 180;
        public const long ProductionVisibleTriangleGate = 700000L;
        public const int ProductionSceneMaterialGate = 40;
        public const string DeliveredIntervalSemantics =
            "Time.unscaledDeltaTime is delivered-frame cadence and is not app execution headroom.";
        public const string WorkloadTimingSemantics =
            "Per-frame max of available Unity FrameTimingManager CPU/GPU duration; host VrApi remains authoritative on Quest.";

        private const string LogPrefix = "[QuestFlight][TemporalVisualGate]";

        [SerializeField] private float readinessTimeoutSeconds = DefaultReadinessTimeoutSeconds;
        [SerializeField] private float warmupSeconds = DefaultWarmupSeconds;
        [SerializeField] private float measurementSeconds = DefaultMeasurementSeconds;
        [SerializeField] private ProductionVerticalSliceRoot productionRoot;
        [SerializeField] private MountainTemporalStabilityProbe mountainProbe;
        public QuestTemporalVisualGateReport LastReport { get; private set; }
        public ProductionVerticalSliceRoot AuthoredProductionRoot => productionRoot;
        public MountainTemporalStabilityProbe AuthoredMountainProbe => mountainProbe;
        public bool HasAuthoredReferences => productionRoot != null && mountainProbe != null;

        public void ConfigureAuthoredReferences(
            ProductionVerticalSliceRoot authoredProductionRoot,
            MountainTemporalStabilityProbe authoredMountainProbe)
        {
            productionRoot = authoredProductionRoot;
            mountainProbe = authoredMountainProbe;
        }

        private IEnumerator Start()
        {
            if (Application.isEditor || !AcceptanceCaptureRequested()) yield break;
            yield return Capture();
        }

        public static bool EvaluateSceneReadiness(out string status)
        {
            ProductionVerticalSliceRoot production = FindFirstObjectByType<ProductionVerticalSliceRoot>(FindObjectsInactive.Include);
            if (production == null)
            {
                status = "ProductionVerticalSliceRoot is missing.";
                return false;
            }
            if (production.AircraftRig == null || !production.AircraftRig.ValidateHierarchy())
            {
                status = "Authored aircraft/XR hierarchy is invalid.";
                return false;
            }
            ProductionEnvironmentRoot environment = production.EnvironmentRoot != null
                ? production.EnvironmentRoot.GetComponentInChildren<ProductionEnvironmentRoot>(true)
                : null;
            string environmentStatus = "ProductionEnvironmentRoot is missing.";
            if (environment == null || !environment.TryValidateContract(out environmentStatus))
            {
                status = "Production environment contract failed: " + environmentStatus;
                return false;
            }
            FlightDynamicsCoordinator[] coordinators = FindObjectsByType<FlightDynamicsCoordinator>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (coordinators.Length != 1 || coordinators[0].requestedBackend != FlightDynamicsBackendKind.UnityPrototype)
            {
                status = "Exactly one Unity prototype coordinator is required.";
                return false;
            }
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            string[] forbidden =
            {
                "QuestFirstViewRuntimeRepair", "FirstViewPlaytestDiagnostics", "SceneryModeController",
                "MeshSceneryProvider", "SplatSceneryProvider", "TrainingModeController", "PlaytestHud"
            };
            string found = forbidden.FirstOrDefault(name => behaviours.Any(component =>
                component != null && component.GetType().Name == name));
            if (found != null)
            {
                status = "Excluded runtime component is present: " + found;
                return false;
            }
            status = "PASS: authored production hierarchy, environment, tracked XR, and sole Unity authority are ready.";
            return true;
        }

        private IEnumerator Capture()
        {
            float readinessStarted = Time.realtimeSinceStartup;
            string readiness = string.Empty;
            while (!EvaluateSceneReadiness(out readiness) &&
                   Time.realtimeSinceStartup - readinessStarted < readinessTimeoutSeconds)
                yield return null;
            bool ready = EvaluateSceneReadiness(out readiness);
            Debug.Log($"{LogPrefix} Readiness {(ready ? "PASS" : "FAIL")}: {readiness}");
            if (!ready) yield break;

            float warmupStarted = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmupStarted < warmupSeconds) yield return null;

            QuestTemporalVisualGateReport report = CreateReport(readiness, Time.realtimeSinceStartup - readinessStarted);
            LastReport = report;
            if (mountainProbe != null)
            {
                mountainProbe.autoCaptureInPlayer = false;
                mountainProbe.automaticCaptureSeconds = measurementSeconds;
                mountainProbe.BeginCapture(Camera.main);
            }
            else
            {
                report.limitations.Add("Authored MountainTemporalStabilityProbe is missing.");
            }
            Debug.Log($"{LogPrefix} Measurement started");
            ProfilerRecorder allocations = default;
            bool allocationAvailable = false;
            try
            {
                allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
                allocationAvailable = allocations.Valid;
            }
            catch (Exception exception)
            {
                report.limitations.Add("Allocation recorder unavailable: " + exception.Message);
            }

            float measurementStarted = Time.realtimeSinceStartup;
            int sequence = 0;
            while (Time.realtimeSinceStartup - measurementStarted < measurementSeconds)
            {
                yield return new WaitForEndOfFrame();
                FrameTimingManager.CaptureFrameTimings();
                FrameTiming[] timing = new FrameTiming[1];
                uint count = FrameTimingManager.GetLatestTimings(1, timing);
                float cpu = count > 0 ? (float)timing[0].cpuFrameTime : 0f;
                float gpu = count > 0 ? (float)timing[0].gpuFrameTime : 0f;
                report.frameSamples.Add(new QuestTemporalFrameSample
                {
                    sequence = sequence++,
                    unityFrameCount = Time.frameCount,
                    elapsedSeconds = Time.realtimeSinceStartup - measurementStarted,
                    frameMs = Time.unscaledDeltaTime * 1000f,
                    frameTimingAvailable = count > 0,
                    cpuFrameMs = cpu,
                    gpuFrameMs = gpu,
                    gcAllocatedBytes = allocationAvailable ? allocations.LastValue : 0
                });
            }
            if (allocationAvailable) allocations.Dispose();
            if (mountainProbe != null)
            {
                MountainTemporalStabilityReport mountainReport = mountainProbe.IsCapturing
                    ? mountainProbe.EndCapture(true)
                    : mountainProbe.LastReport;
                report.mountainTemporalGatePassed = mountainReport != null && mountainReport.passed;
                report.mountainTemporalClassification = mountainReport?.classification ?? "MISSING_REPORT";
                report.mountainTemporalEvidencePath = mountainReport?.evidencePath ?? string.Empty;
            }
            FinalizeReport(report, Time.realtimeSinceStartup - measurementStarted, allocationAvailable);
            WriteEvidence(report);
        }

        private QuestTemporalVisualGateReport CreateReport(string readiness, float readinessSeconds)
        {
            ProductionVerticalSliceRoot production = productionRoot;
            return new QuestTemporalVisualGateReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                deviceModel = SystemInfo.deviceModel,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                productionVerticalSliceActive = production != null,
                productionArchitectureVersion = production != null ? production.AuthoredArchitectureVersion : string.Empty,
                authoredProductionHierarchyValid = production?.AircraftRig != null && production.AircraftRig.ValidateHierarchy(),
                productionEnvironmentContractValid = true,
                legacyRuntimeRepairAbsent = true,
                authoritativePhysicsBackend = FlightDynamicsBackendKind.UnityPrototype.ToString(),
                readinessPrerequisitesMet = true,
                readinessStatus = readiness,
                readinessWaitSeconds = readinessSeconds,
                warmupSeconds = warmupSeconds,
                requestedMeasurementSeconds = measurementSeconds,
                deliveredIntervalSemantics = DeliveredIntervalSemantics,
                workloadTimingSemantics = WorkloadTimingSemantics,
                hardPerformanceGateSemantics = "Machine workload metrics plus app-PID VrApi; human headset quality remains a separate gate."
            };
        }

        private static void FinalizeReport(QuestTemporalVisualGateReport report, float measuredSeconds, bool allocationAvailable)
        {
            report.measuredSeconds = measuredSeconds;
            report.sampleCount = report.frameSamples.Count;
            float[] delivered = report.frameSamples.Select(sample => sample.frameMs).OrderBy(value => value).ToArray();
            float[] cpu = report.frameSamples.Where(sample => sample.cpuFrameMs > 0f).Select(sample => sample.cpuFrameMs).OrderBy(value => value).ToArray();
            float[] gpu = report.frameSamples.Where(sample => sample.gpuFrameMs > 0f).Select(sample => sample.gpuFrameMs).OrderBy(value => value).ToArray();
            float[] workload = report.frameSamples.Select(sample => Mathf.Max(sample.cpuFrameMs, sample.gpuFrameMs))
                .Where(value => value > 0f).OrderBy(value => value).ToArray();
            report.averageFrameMs = Average(delivered);
            report.averageDeliveredIntervalMs = report.averageFrameMs;
            report.minFrameMs = delivered.Length > 0 ? delivered[0] : 0f;
            report.maxFrameMs = delivered.Length > 0 ? delivered[delivered.Length - 1] : 0f;
            report.p95FrameMs = Percentile(delivered, 0.95f);
            report.p99FrameMs = Percentile(delivered, 0.99f);
            report.p95DeliveredIntervalMs = report.p95FrameMs;
            report.estimatedFps = report.averageFrameMs > 0f ? 1000f / report.averageFrameMs : 0f;
            report.framesOver72HzBudget = delivered.Count(value => value > Quest72HzFrameBudgetMs);
            report.framesOver72HzBudgetRatio = delivered.Length > 0
                ? report.framesOver72HzBudget / (float)delivered.Length : 1f;
            report.workloadTimingSamples = workload.Length;
            report.averageWorkloadMs = Average(workload);
            report.p95WorkloadMs = Percentile(workload, 0.95f);
            report.workloadSamplesOver72HzBudget = workload.Count(value => value > Quest72HzFrameBudgetMs);
            report.workloadSamplesOver72HzBudgetRatio = workload.Length > 0
                ? report.workloadSamplesOver72HzBudget / (float)workload.Length : 1f;
            report.frameTimingManagerSamples = report.frameSamples.Count(sample => sample.frameTimingAvailable);
            report.frameTimingManagerAvailable = report.frameTimingManagerSamples > 0;
            report.gpuTimingSamples = gpu.Length;
            report.averageCpuFrameMs = Average(cpu);
            report.p95CpuFrameMs = Percentile(cpu, 0.95f);
            report.averageGpuFrameMs = Average(gpu);
            report.p95GpuFrameMs = Percentile(gpu, 0.95f);
            report.allocationRecorderAvailable = allocationAvailable;
            report.averageGcAllocatedBytesPerFrame = report.frameSamples.Count > 0
                ? (long)report.frameSamples.Average(sample => (double)sample.gcAllocatedBytes) : 0L;
            report.maxGcAllocatedBytesPerFrame = report.frameSamples.Count > 0
                ? report.frameSamples.Max(sample => sample.gcAllocatedBytes) : 0L;
            Camera camera = Camera.main;
            report.renderBudget = QuestRenderBudgetAudit.Capture(camera);
            report.activeRendererCount = report.renderBudget.activeRendererCount;
            report.sceneMaterialCount = report.renderBudget.uniqueMaterialCount;
            report.deliveredCadencePerformanceGatePassed = report.p95DeliveredIntervalMs <= Quest72HzFrameBudgetMs &&
                                                          report.framesOver72HzBudgetRatio <= QuestOverBudgetGateRatio;
            report.workloadPerformanceGatePassed = workload.Length > 0 &&
                                                    report.p95WorkloadMs <= Quest72HzFrameBudgetMs &&
                                                    report.workloadSamplesOver72HzBudgetRatio <= QuestOverBudgetGateRatio;
            report.hardPerformanceGatePassed = report.workloadPerformanceGatePassed &&
                                               report.renderBudget.estimatedInstancedDrawCalls <= ProductionDrawCallGate &&
                                               report.renderBudget.estimatedFrustumTriangles <= ProductionVisibleTriangleGate &&
                                               report.renderBudget.uniqueMaterialCount <= ProductionSceneMaterialGate;
            report.limitations.Add("No worn-headset human observation was available during machine capture.");
        }

        private static void WriteEvidence(QuestTemporalVisualGateReport report)
        {
            string directory = Path.Combine(Application.persistentDataPath, "QuestFlight", EvidenceDirectoryName);
            Directory.CreateDirectory(directory);
            string stem = "quest_temporal_visual_gate_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, stem + ".json");
            report.evidencePath = path;
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log($"{LogPrefix} Evidence written: {path}");
        }

        private static bool AcceptanceCaptureRequested()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject intent = activity?.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<string>("getStringExtra", "qfl_scenery_mode") ==
                    ProductionVerticalSliceRoot.ArchitectureVersion;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(LogPrefix + " Could not read acceptance intent: " + exception.Message);
            }
#endif
            return false;
        }

        private static float Average(IReadOnlyList<float> values) =>
            values.Count > 0 ? values.Sum() / values.Count : 0f;

        private static float Percentile(IReadOnlyList<float> sorted, float percentile)
        {
            if (sorted.Count == 0) return 0f;
            float position = Mathf.Clamp01(percentile) * (sorted.Count - 1);
            int lower = Mathf.FloorToInt(position);
            int upper = Mathf.CeilToInt(position);
            return Mathf.Lerp(sorted[lower], sorted[upper], position - lower);
        }
    }
}
