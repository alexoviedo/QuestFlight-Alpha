using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        public long managedAllocationCount;
        public long runtimeDrawCalls;
        public long runtimeTriangles;
    }

    [Serializable]
    public struct RenderBudgetSnapshot
    {
        public int activeRendererCount;
        public int estimatedVisibleRendererCount;
        public int estimatedInstancedDrawCalls;
        public long estimatedFrustumTriangles;
        public int uniqueMaterialCount;
        public int transparentMaterialSlots;
        public string drawCountSemantics;
        public string triangleCountSemantics;
    }

    public static class QuestRenderBudgetAudit
    {
        public static RenderBudgetSnapshot Capture(Camera camera)
        {
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(renderer => renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
                                   renderer.gameObject.activeInHierarchy)
                .ToArray();
            Plane[] frustum = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
            Dictionary<Renderer, int> rendererLodIndices = new Dictionary<Renderer, int>();
            Dictionary<LODGroup, int> selectedLodIndices = new Dictionary<LODGroup, int>();
            if (camera != null)
            {
                foreach (LODGroup group in UnityEngine.Object.FindObjectsByType<LODGroup>(
                             FindObjectsInactive.Include,
                             FindObjectsSortMode.None))
                {
                    if (group == null || !group.enabled || !group.gameObject.activeInHierarchy) continue;
                    LOD[] lods = group.GetLODs();
                    selectedLodIndices[group] = EstimateActiveLodIndex(group, lods, camera);
                    for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                    foreach (Renderer renderer in lods[lodIndex].renderers ?? Array.Empty<Renderer>())
                        if (renderer != null && !rendererLodIndices.ContainsKey(renderer))
                            rendererLodIndices.Add(renderer, lodIndex);
                }
            }
            Renderer[] visible = renderers.Where(renderer =>
                    (camera == null ||
                     ((camera.cullingMask & (1 << renderer.gameObject.layer)) != 0 &&
                      GeometryUtility.TestPlanesAABB(frustum, renderer.bounds))) &&
                    IsSelectedLodRenderer(renderer, rendererLodIndices, selectedLodIndices))
                .ToArray();
            HashSet<Material> materials = new HashSet<Material>();
            long triangles = 0;
            int drawCalls = 0;
            int transparentSlots = 0;
            foreach (Renderer renderer in visible)
            {
                Material[] sharedMaterials = renderer.sharedMaterials;
                foreach (Material material in sharedMaterials)
                    if (material != null) materials.Add(material);

                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer is MeshRenderer
                        ? renderer.GetComponent<MeshFilter>()?.sharedMesh
                        : null;
                if (mesh == null || mesh.subMeshCount == 0) continue;

                // Unity maps each material slot to the corresponding submesh and maps
                // surplus material slots to the final submesh. Count only non-null,
                // effectively rendered slots so a multi-submesh mesh is neither collapsed
                // to one draw nor counted once in full for every material.
                for (int materialSlot = 0; materialSlot < sharedMaterials.Length; materialSlot++)
                {
                    Material material = sharedMaterials[materialSlot];
                    if (material == null) continue;
                    if (material.renderQueue >= (int)RenderQueue.Transparent ||
                        string.Equals(
                            material.GetTag("RenderType", false, string.Empty),
                            "Transparent",
                            StringComparison.OrdinalIgnoreCase))
                        transparentSlots++;
                    int subMesh = Mathf.Min(materialSlot, mesh.subMeshCount - 1);
                    drawCalls++;
                    if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                        triangles += (long)mesh.GetIndexCount(subMesh) / 3L;
                }
            }
            if (camera != null && camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
            {
                Material skybox = RenderSettings.skybox;
                materials.Add(skybox);
                drawCalls++;
                if (skybox.renderQueue >= (int)RenderQueue.Transparent ||
                    string.Equals(
                        skybox.GetTag("RenderType", false, string.Empty),
                        "Transparent",
                        StringComparison.OrdinalIgnoreCase))
                    transparentSlots++;
            }
            return new RenderBudgetSnapshot
            {
                activeRendererCount = renderers.Length,
                estimatedVisibleRendererCount = visible.Length,
                estimatedInstancedDrawCalls = drawCalls,
                estimatedFrustumTriangles = triangles,
                uniqueMaterialCount = materials.Count,
                transparentMaterialSlots = transparentSlots,
                drawCountSemantics = "Camera-layer/frustum and selected-LOD non-null MeshRenderer/SkinnedMeshRenderer material-submesh slots before batching or instancing.",
                triangleCountSemantics = "Triangle topology indices for the same camera-layer/frustum and selected-LOD effective rendered material-submesh slots."
            };
        }

        private static bool IsSelectedLodRenderer(
            Renderer renderer,
            IReadOnlyDictionary<Renderer, int> rendererLodIndices,
            IReadOnlyDictionary<LODGroup, int> selectedLodIndices)
        {
            if (!rendererLodIndices.TryGetValue(renderer, out int rendererLodIndex)) return true;
            LODGroup group = renderer.GetComponentInParent<LODGroup>();
            return group == null || !selectedLodIndices.TryGetValue(group, out int selectedLodIndex) ||
                   selectedLodIndex == rendererLodIndex;
        }

        private static int EstimateActiveLodIndex(LODGroup group, LOD[] lods, Camera camera)
        {
            if (group == null || lods == null || lods.Length == 0 || camera == null) return -1;
            float scale = Mathf.Max(
                Mathf.Abs(group.transform.lossyScale.x),
                Mathf.Abs(group.transform.lossyScale.y),
                Mathf.Abs(group.transform.lossyScale.z));
            Vector3 referencePoint = group.transform.TransformPoint(group.localReferencePoint);
            float distance = Vector3.Distance(camera.transform.position, referencePoint);
            float relativeHeight = camera.orthographic
                ? group.size * scale / Mathf.Max(0.001f, camera.orthographicSize * 2f)
                : group.size * scale /
                  Mathf.Max(0.001f, distance * 2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            relativeHeight *= QualitySettings.lodBias;
            for (int index = 0; index < lods.Length; index++)
                if (relativeHeight >= lods[index].screenRelativeTransitionHeight) return index;
            return -1;
        }
    }

    [Serializable]
    public sealed class QuestTemporalVisualGateReport
    {
        public int schemaVersion = 5;
        public string generatedUtc;
        public string unityVersion;
        public string platform;
        public string deviceModel;
        public string graphicsDeviceType;
        public string graphicsDeviceName;
        public string sceneryMode = ProductionVerticalSliceRoot.ArchitectureVersion;
        public string diagnosticIsolationProfile = QuestTemporalVisualGateRecorder.ProductionIsolationProfile;
        public bool diagnosticIsolationActive;
        public int diagnosticDisabledRendererCount;
        public int diagnosticTextureMipmapLimit;
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
        public bool allocationMeasurementAvailable;
        public bool allocationMeasurementValidated;
        public string allocationMeasurementSource;
        public string allocationMeasurementScope;
        public int allocationMeasurementSamples;
        public bool allocationByteMeasurementAvailable;
        public bool managedAllocationCountMeasurementAvailable;
        public bool allocationCounterCalibrationPassed;
        public long allocationCounterCalibrationDelta;
        public long totalManagedAllocationCount;
        public long maxManagedAllocationCountPerFrame;
        public string allocationMeasurementInvalidReason;
        public long averageGcAllocatedBytesPerFrame;
        public long maxGcAllocatedBytesPerFrame;
        public RenderBudgetSnapshot renderBudget;
        public int activeRendererCount;
        public int sceneMaterialCount;
        public int transparentMaterialSlots;
        public int performanceDrawCallGate = QuestTemporalVisualGateRecorder.ProductionDrawCallGate;
        public long performanceVisibleTriangleGate = QuestTemporalVisualGateRecorder.ProductionVisibleTriangleGate;
        public int performanceSceneMaterialGate = QuestTemporalVisualGateRecorder.ProductionSceneMaterialGate;
        public int performanceTransparentMaterialSlotGate =
            QuestTemporalVisualGateRecorder.ProductionTransparentMaterialSlotGate;
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
        public const float QuestOverBudgetGateRatio = 0.05f;
        public const int ProductionDrawCallGate = 120;
        public const long ProductionVisibleTriangleGate = 500000L;
        public const int ProductionSceneMaterialGate = 30;
        public const int ProductionTransparentMaterialSlotGate = 2;
        public const string ProductionIsolationProfile = "production";
        public const string SkyOnlyIsolationProfile = "sky_only";
        public const string WaterOffIsolationProfile = "water_off";
        public const string BuildingsVegetationOffIsolationProfile = "buildings_vegetation_off";
        public const string ReducedTextureIsolationProfile = "reduced_texture_tier";
        public const string DeliveredIntervalSemantics =
            "Time.unscaledDeltaTime is delivered-frame cadence and is not app execution headroom.";
        public const string WorkloadTimingSemantics =
            "Per-frame max of available Unity FrameTimingManager CPU/GPU duration; host VrApi remains authoritative on Quest.";

        private const string LogPrefix = "[QuestFlight][TemporalVisualGate]";
        private const int Il2CppStatNewObjectCount = 0;
        private static byte[] allocationCounterCalibrationRoot;

        private sealed class DiagnosticIsolationState
        {
            public string profile = ProductionIsolationProfile;
            public int previousTextureMipmapLimit;
            public readonly List<Renderer> disabledRenderers = new List<Renderer>();
        }

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

            ProductionEnvironmentRoot diagnosticEnvironment = productionRoot != null && productionRoot.EnvironmentRoot != null
                ? productionRoot.EnvironmentRoot.GetComponentInChildren<ProductionEnvironmentRoot>(true)
                : null;
            DiagnosticIsolationState isolation = ApplyDiagnosticIsolation(ReadIsolationProfile(), diagnosticEnvironment);
            Debug.Log($"{LogPrefix} Isolation profile={isolation.profile}; disabledRenderers={isolation.disabledRenderers.Count}; " +
                      $"textureMipmapLimit={QualitySettings.globalTextureMipmapLimit}");

            // The temporal recorder owns mountain capture during this gate. Disable the probe's
            // independent 90 s auto-start before this gate's 90 s warmup so diagnostic profiles
            // cannot accidentally start a second observer at the measurement boundary. Capture
            // the full mountain invariant window during warmup, then leave a cooldown before the
            // allocation-sensitive product window. Both gates remain strict, but the observer's
            // own evidence serialization is not misclassified as product allocation.
            bool mountainDiagnosticApplicable = mountainProbe != null && !string.Equals(
                isolation.profile,
                SkyOnlyIsolationProfile,
                StringComparison.Ordinal);
            MountainTemporalStabilityReport completedMountainReport = null;
            if (mountainProbe != null)
            {
                mountainProbe.autoCaptureInPlayer = false;
                if (mountainProbe.IsCapturing)
                    mountainProbe.EndCapture(false);
                if (mountainDiagnosticApplicable)
                {
                    mountainProbe.automaticCaptureSeconds = measurementSeconds;
                    mountainProbe.BeginCapture(Camera.main);
                }
            }

            float warmupStarted = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmupStarted < warmupSeconds)
            {
                if (mountainDiagnosticApplicable && completedMountainReport == null &&
                    Time.realtimeSinceStartup - warmupStarted >= measurementSeconds)
                {
                    completedMountainReport = mountainProbe.IsCapturing
                        ? mountainProbe.EndCapture(true)
                        : mountainProbe.LastReport;
                }
                yield return null;
            }
            if (mountainDiagnosticApplicable && completedMountainReport == null)
            {
                completedMountainReport = mountainProbe.IsCapturing
                    ? mountainProbe.EndCapture(true)
                    : mountainProbe.LastReport;
            }

            QuestTemporalVisualGateReport report = CreateReport(readiness, Time.realtimeSinceStartup - readinessStarted);
            report.diagnosticIsolationProfile = isolation.profile;
            report.diagnosticIsolationActive = !string.Equals(
                isolation.profile,
                ProductionIsolationProfile,
                StringComparison.Ordinal);
            report.diagnosticDisabledRendererCount = isolation.disabledRenderers.Count;
            report.diagnosticTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit;
            // Allocate all observer-owned steady-state storage before the allocation recorder starts.
            // The gate must measure the product workload, not allocations introduced by its own probe.
            int expectedFrameSamples = Mathf.CeilToInt(Mathf.Max(1f, measurementSeconds) * 144f) + 16;
            report.frameSamples.Capacity = Mathf.Max(report.frameSamples.Capacity, expectedFrameSamples);
            WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();
            FrameTiming[] timingBuffer = new FrameTiming[1];
            LastReport = report;
            if (mountainProbe != null)
            {
                if (string.Equals(isolation.profile, SkyOnlyIsolationProfile, StringComparison.Ordinal))
                {
                    report.limitations.Add(
                        "Mountain temporal probing is not applicable to the sky-only diagnostic because all environment renderers are intentionally disabled.");
                }
                else if (completedMountainReport == null)
                {
                    report.limitations.Add(
                        "The warmup-owned MountainTemporalStabilityProbe did not produce a report before the allocation window.");
                }
            }
            else
            {
                report.limitations.Add("Authored MountainTemporalStabilityProbe is missing.");
            }
            ProfilerRecorder allocations = default;
            bool profilerAllocationAvailable = false;
            try
            {
                allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
                profilerAllocationAvailable = allocations.Valid;
                if (!profilerAllocationAvailable)
                {
                    report.limitations.Add(
                        "Unity's byte-valued GC Allocated In Frame counter is unavailable in this non-development Release Player.");
                    report.limitations.Add(
                        "System.GC.GetAllocatedBytesForCurrentThread is intentionally not used because Unity 6000.3.8f1 IL2CPP implements it as an unimplemented zero-return stub.");
                }
            }
            catch (Exception exception)
            {
                report.limitations.Add("Unity allocation recorder unavailable: " + exception.Message);
            }

            bool allocationCounterCalibrationPassed = false;
            long allocationCounterCalibrationDelta = 0L;
            string allocationMeasurementInvalidReason = string.Empty;
            bool il2CppAllocationCounterAvailable = false;
            ulong previousManagedObjectCount = 0UL;
            if (!profilerAllocationAvailable)
            {
                il2CppAllocationCounterAvailable = TryCalibrateIl2CppAllocationCounter(
                    out previousManagedObjectCount,
                    out allocationCounterCalibrationDelta,
                    out allocationMeasurementInvalidReason);
                allocationCounterCalibrationPassed = il2CppAllocationCounterAvailable;
                if (!il2CppAllocationCounterAvailable)
                    report.limitations.Add("IL2CPP managed-object allocation counter unavailable: " +
                                           allocationMeasurementInvalidReason);
            }

            bool allocationMeasurementAvailable = profilerAllocationAvailable || il2CppAllocationCounterAvailable;
            string allocationMeasurementSource = profilerAllocationAvailable
                ? "Unity ProfilerRecorder: GC Allocated In Frame"
                : il2CppAllocationCounterAvailable
                    ? "Unity IL2CPP public runtime stat: IL2CPP_STAT_NEW_OBJECT_COUNT"
                    : "unavailable";
            string allocationMeasurementScope = profilerAllocationAvailable
                ? "All managed allocations reported by Unity for each frame."
                : il2CppAllocationCounterAvailable
                    ? "Monotonic process-global managed-object creations across all IL2CPP threads; zero objects implies zero managed allocation bytes."
                    : "No exact managed-allocation measurement was available; the gate remains failed closed.";

            Debug.Log($"{LogPrefix} Measurement started");
            if (il2CppAllocationCounterAvailable)
            {
                try
                {
                    // Reset after all observer setup and the measurement log so only the exact
                    // steady-state window contributes to the first per-frame delta.
                    previousManagedObjectCount = ReadIl2CppManagedObjectCount();
                }
                catch (Exception exception)
                {
                    il2CppAllocationCounterAvailable = false;
                    allocationMeasurementInvalidReason = "Could not establish the measurement baseline: " + exception.Message;
                }
            }
            float measurementStarted = Time.realtimeSinceStartup;
            int sequence = 0;
            int allocationMeasurementSamples = 0;
            while (Time.realtimeSinceStartup - measurementStarted < measurementSeconds)
            {
                yield return waitForEndOfFrame;
                FrameTimingManager.CaptureFrameTimings();
                uint count = FrameTimingManager.GetLatestTimings(1, timingBuffer);
                float cpu = count > 0 ? (float)timingBuffer[0].cpuFrameTime : 0f;
                float gpu = count > 0 ? (float)timingBuffer[0].gpuFrameTime : 0f;
                long allocatedBytes = -1L;
                long managedAllocationCount = -1L;
                if (profilerAllocationAvailable)
                {
                    long profilerValue = allocations.LastValue;
                    if (allocations.Count > 0 && profilerValue >= 0L)
                    {
                        allocatedBytes = profilerValue;
                        allocationMeasurementSamples++;
                    }
                    else if (string.IsNullOrEmpty(allocationMeasurementInvalidReason))
                    {
                        allocationMeasurementInvalidReason =
                            "Unity ProfilerRecorder did not provide a nonnegative per-frame allocation sample.";
                    }
                }
                else if (il2CppAllocationCounterAvailable)
                {
                    try
                    {
                        ulong currentManagedObjectCount = ReadIl2CppManagedObjectCount();
                        if (TryComputeAllocationCounterDelta(
                                previousManagedObjectCount,
                                currentManagedObjectCount,
                                out managedAllocationCount))
                        {
                            previousManagedObjectCount = currentManagedObjectCount;
                            allocationMeasurementSamples++;
                        }
                        else
                        {
                            allocationMeasurementInvalidReason =
                                "IL2CPP managed-object allocation counter decreased or overflowed Int64.";
                            il2CppAllocationCounterAvailable = false;
                        }
                    }
                    catch (Exception exception)
                    {
                        allocationMeasurementInvalidReason = "IL2CPP allocation sample failed: " + exception.Message;
                        il2CppAllocationCounterAvailable = false;
                    }
                }
                report.frameSamples.Add(new QuestTemporalFrameSample
                {
                    sequence = sequence++,
                    unityFrameCount = Time.frameCount,
                    elapsedSeconds = Time.realtimeSinceStartup - measurementStarted,
                    frameMs = Time.unscaledDeltaTime * 1000f,
                    frameTimingAvailable = count > 0,
                    cpuFrameMs = cpu,
                    gpuFrameMs = gpu,
                    gcAllocatedBytes = allocatedBytes,
                    managedAllocationCount = managedAllocationCount
                });
            }
            if (!profilerAllocationAvailable && il2CppAllocationCounterAvailable && report.frameSamples.Count > 0)
            {
                try
                {
                    ulong finalManagedObjectCount = ReadIl2CppManagedObjectCount();
                    if (TryComputeAllocationCounterDelta(
                            previousManagedObjectCount,
                            finalManagedObjectCount,
                            out long tailManagedAllocationCount))
                    {
                        int lastIndex = report.frameSamples.Count - 1;
                        QuestTemporalFrameSample finalSample = report.frameSamples[lastIndex];
                        finalSample.managedAllocationCount += tailManagedAllocationCount;
                        report.frameSamples[lastIndex] = finalSample;
                    }
                    else
                    {
                        allocationMeasurementInvalidReason =
                            "IL2CPP managed-object allocation counter decreased or overflowed at the measurement boundary.";
                        il2CppAllocationCounterAvailable = false;
                    }
                }
                catch (Exception exception)
                {
                    allocationMeasurementInvalidReason = "IL2CPP final allocation sample failed: " + exception.Message;
                    il2CppAllocationCounterAvailable = false;
                }
            }
            if (profilerAllocationAvailable) allocations.Dispose();
            if (mountainProbe != null)
            {
                if (string.Equals(isolation.profile, SkyOnlyIsolationProfile, StringComparison.Ordinal))
                {
                    report.mountainTemporalGatePassed = false;
                    report.mountainTemporalClassification = "NOT_APPLICABLE_DIAGNOSTIC_SKY_ONLY";
                    report.mountainTemporalEvidencePath = string.Empty;
                }
                else
                {
                    MountainTemporalStabilityReport mountainReport = completedMountainReport;
                    report.mountainTemporalGatePassed = mountainReport != null && mountainReport.passed;
                    report.mountainTemporalClassification = mountainReport?.classification ?? "MISSING_REPORT";
                    report.mountainTemporalEvidencePath = mountainReport?.evidencePath ?? string.Empty;
                }
            }
            FinalizeReport(
                report,
                Time.realtimeSinceStartup - measurementStarted,
                profilerAllocationAvailable,
                allocationMeasurementAvailable,
                allocationMeasurementSource,
                allocationMeasurementScope,
                allocationMeasurementSamples,
                il2CppAllocationCounterAvailable,
                allocationCounterCalibrationPassed,
                allocationCounterCalibrationDelta,
                allocationMeasurementInvalidReason);
            WriteEvidence(report);
            RestoreDiagnosticIsolation(isolation);
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

        private static void FinalizeReport(
            QuestTemporalVisualGateReport report,
            float measuredSeconds,
            bool profilerAllocationAvailable,
            bool allocationMeasurementAvailable,
            string allocationMeasurementSource,
            string allocationMeasurementScope,
            int allocationMeasurementSamples,
            bool il2CppAllocationCounterAvailable,
            bool allocationCounterCalibrationPassed,
            long allocationCounterCalibrationDelta,
            string allocationMeasurementInvalidReason)
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
            report.allocationRecorderAvailable = profilerAllocationAvailable;
            report.allocationMeasurementAvailable = allocationMeasurementAvailable;
            report.allocationMeasurementSource = allocationMeasurementSource;
            report.allocationMeasurementScope = allocationMeasurementScope;
            report.allocationMeasurementSamples = allocationMeasurementSamples;
            report.allocationByteMeasurementAvailable = profilerAllocationAvailable;
            report.allocationCounterCalibrationPassed = allocationCounterCalibrationPassed;
            report.allocationCounterCalibrationDelta = allocationCounterCalibrationDelta;
            report.allocationMeasurementInvalidReason = allocationMeasurementInvalidReason ?? string.Empty;
            report.allocationMeasurementValidated = allocationMeasurementAvailable &&
                                                    allocationMeasurementSamples == report.frameSamples.Count &&
                                                    string.IsNullOrEmpty(report.allocationMeasurementInvalidReason) &&
                                                    (profilerAllocationAvailable ||
                                                     il2CppAllocationCounterAvailable && allocationCounterCalibrationPassed);
            report.managedAllocationCountMeasurementAvailable = !profilerAllocationAvailable &&
                                                                il2CppAllocationCounterAvailable &&
                                                                report.allocationMeasurementValidated;
            report.averageGcAllocatedBytesPerFrame = profilerAllocationAvailable && report.frameSamples.Count > 0
                ? (long)report.frameSamples.Average(sample => (double)sample.gcAllocatedBytes) : -1L;
            report.maxGcAllocatedBytesPerFrame = profilerAllocationAvailable && report.frameSamples.Count > 0
                ? report.frameSamples.Max(sample => sample.gcAllocatedBytes) : -1L;
            report.totalManagedAllocationCount = report.managedAllocationCountMeasurementAvailable
                ? report.frameSamples.Sum(sample => sample.managedAllocationCount) : -1L;
            report.maxManagedAllocationCountPerFrame = report.managedAllocationCountMeasurementAvailable &&
                                                       report.frameSamples.Count > 0
                ? report.frameSamples.Max(sample => sample.managedAllocationCount) : -1L;
            Camera camera = Camera.main;
            report.renderBudget = QuestRenderBudgetAudit.Capture(camera);
            report.activeRendererCount = report.renderBudget.activeRendererCount;
            report.sceneMaterialCount = report.renderBudget.uniqueMaterialCount;
            report.transparentMaterialSlots = report.renderBudget.transparentMaterialSlots;
            report.deliveredCadencePerformanceGatePassed = report.p95DeliveredIntervalMs <= Quest72HzFrameBudgetMs &&
                                                          report.framesOver72HzBudgetRatio <= QuestOverBudgetGateRatio;
            report.workloadPerformanceGatePassed = workload.Length > 0 &&
                                                    report.p95WorkloadMs <= Quest72HzFrameBudgetMs &&
                                                    report.workloadSamplesOver72HzBudgetRatio <= QuestOverBudgetGateRatio;
            report.hardPerformanceGatePassed = report.workloadPerformanceGatePassed &&
                                               report.renderBudget.estimatedInstancedDrawCalls <= ProductionDrawCallGate &&
                                               report.renderBudget.estimatedFrustumTriangles <= ProductionVisibleTriangleGate &&
                                               report.renderBudget.uniqueMaterialCount <= ProductionSceneMaterialGate &&
                                               report.renderBudget.transparentMaterialSlots <= ProductionTransparentMaterialSlotGate;
            report.limitations.Add("No worn-headset human observation was available during machine capture.");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "il2cpp_stats_get_value", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Il2CppStatsGetValue(int stat);
#endif

        private static ulong ReadIl2CppManagedObjectCount()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Il2CppStatsGetValue(Il2CppStatNewObjectCount);
#else
            throw new PlatformNotSupportedException("IL2CPP runtime allocation stats are available only in the Android player.");
#endif
        }

        private static bool TryCalibrateIl2CppAllocationCounter(
            out ulong currentCount,
            out long calibrationDelta,
            out string error)
        {
            currentCount = 0UL;
            calibrationDelta = 0L;
            error = string.Empty;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                ulong before = ReadIl2CppManagedObjectCount();
                allocationCounterCalibrationRoot = new byte[1];
                allocationCounterCalibrationRoot[0] = 0x51;
                ulong after = ReadIl2CppManagedObjectCount();
                currentCount = after;
                if (!TryComputeAllocationCounterDelta(before, after, out calibrationDelta) || calibrationDelta < 1L)
                {
                    error = "Calibration allocation did not increment IL2CPP_STAT_NEW_OBJECT_COUNT.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
#else
            error = "Not an Android IL2CPP player.";
            return false;
#endif
        }

        private static bool TryComputeAllocationCounterDelta(ulong previous, ulong current, out long delta)
        {
            delta = 0L;
            if (current < previous) return false;
            ulong unsignedDelta = current - previous;
            if (unsignedDelta > (ulong)long.MaxValue) return false;
            delta = (long)unsignedDelta;
            return true;
        }

        private static string ReadIsolationProfile()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject intent = activity?.Call<AndroidJavaObject>("getIntent");
                string profile = intent?.Call<string>("getStringExtra", "qfl_environment_isolation");
                return IsSupportedIsolationProfile(profile) ? profile : ProductionIsolationProfile;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(LogPrefix + " Could not read diagnostic isolation intent: " + exception.Message);
            }
#endif
            return ProductionIsolationProfile;
        }

        private static bool IsSupportedIsolationProfile(string profile) =>
            string.Equals(profile, ProductionIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, SkyOnlyIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, WaterOffIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, BuildingsVegetationOffIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, ReducedTextureIsolationProfile, StringComparison.Ordinal);

        private static DiagnosticIsolationState ApplyDiagnosticIsolation(
            string profile,
            ProductionEnvironmentRoot environment)
        {
            DiagnosticIsolationState state = new DiagnosticIsolationState
            {
                profile = IsSupportedIsolationProfile(profile) ? profile : ProductionIsolationProfile,
                previousTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit
            };
            if (string.Equals(state.profile, ReducedTextureIsolationProfile, StringComparison.Ordinal))
            {
                QualitySettings.globalTextureMipmapLimit = Math.Max(1, state.previousTextureMipmapLimit);
                return state;
            }
            if (string.Equals(state.profile, ProductionIsolationProfile, StringComparison.Ordinal)) return state;

            if (environment == null) return state;
            Renderer[] renderers = environment.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                bool disable = string.Equals(state.profile, SkyOnlyIsolationProfile, StringComparison.Ordinal) ||
                               string.Equals(state.profile, WaterOffIsolationProfile, StringComparison.Ordinal) &&
                               environment.essentialWater != null &&
                               (renderer.transform == environment.essentialWater ||
                                renderer.transform.IsChildOf(environment.essentialWater)) ||
                               string.Equals(state.profile, BuildingsVegetationOffIsolationProfile, StringComparison.Ordinal) &&
                               IsBuildingOrVegetationRenderer(renderer.transform);
                if (!disable) continue;
                renderer.enabled = false;
                state.disabledRenderers.Add(renderer);
            }
            return state;
        }

        private static void RestoreDiagnosticIsolation(DiagnosticIsolationState state)
        {
            if (state == null) return;
            foreach (Renderer renderer in state.disabledRenderers)
                if (renderer != null) renderer.enabled = true;
            QualitySettings.globalTextureMipmapLimit = state.previousTextureMipmapLimit;
        }

        private static bool IsBuildingOrVegetationRenderer(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string objectName = current.name;
                if (objectName.StartsWith("buildings_", StringComparison.Ordinal) ||
                    objectName.StartsWith("vegetation_", StringComparison.Ordinal) ||
                    string.Equals(objectName, "AirportArtProduction", StringComparison.Ordinal) ||
                    string.Equals(objectName, "SparseVegetationProduction", StringComparison.Ordinal))
                    return true;
            }
            return false;
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
