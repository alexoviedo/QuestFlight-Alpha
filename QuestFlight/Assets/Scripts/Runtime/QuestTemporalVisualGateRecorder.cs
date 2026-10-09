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
        public bool spaceWarpRequested, spaceWarpEnabled, spaceWarpSubmissionObserved;
        public uint spaceWarpHistoryGeneration;
        public bool frameTimingAvailable;
        public float cpuFrameMs;
        public float gpuFrameMs;
        public long gcAllocatedBytes;
        public long managedAllocationCount;
        public long runtimeDrawCalls;
        public long runtimeTriangles;
        public long groveSelectionCount, groveUploadedBytes;
        public int groveClusterInstances,groveReplacedCells,groveSourceTriangles,groveSubmittedCells;
        public double groveGatherTotalMs, groveFillTotalMs, groveBufferUploadTotalMs, groveOwnershipUploadTotalMs;
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
#if UNITY_EDITOR
        public static string EditorDiagnosticPath;
#endif
        public static RenderBudgetSnapshot Capture(Camera camera)
        {
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(renderer => renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
                                   renderer.gameObject.activeInHierarchy)
                .ToArray();
            AerialVegetationBatchRenderer[] vegetationBatches = UnityEngine.Object
                .FindObjectsByType<AerialVegetationBatchRenderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(batch => batch != null && batch.enabled && batch.gameObject.activeInHierarchy &&
                                batch.SharedMaterial != null && batch.NonEmptyDrawCount > 0)
                .ToArray();
            AirportCloseTreeLodRenderer[] closeTreeLods = UnityEngine.Object
                .FindObjectsByType<AirportCloseTreeLodRenderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(batch => batch != null && batch.enabled &&
                                batch.gameObject.activeInHierarchy &&
                                batch.SharedCanopyMaterial != null &&
                                batch.AuthoredMaximumDrawCount > 0)
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
                    IsVisibleForAudit(renderer, camera, frustum) &&
                    IsSelectedLodRenderer(renderer, rendererLodIndices, selectedLodIndices))
                .ToArray();
#if UNITY_EDITOR
            var diagnosticLines = string.IsNullOrEmpty(EditorDiagnosticPath) ? null : new List<string>
            {
                "renderer\tdraw_slots\tmaterials\tmaterial_assets\tkind\tmaterial_slot\tsubmesh\ttopology\ttriangles\tmesh_asset\tcount_semantics"
            };
#endif
            HashSet<Material> materials = new HashSet<Material>();
            long triangles = 0;
            int drawCalls = 0;
            int transparentSlots = 0;
            foreach (Renderer renderer in visible)
            {
                Material[] sharedMaterials = renderer.sharedMaterials;
                foreach (Material material in sharedMaterials)
                    if (material != null) materials.Add(material);

                TextMesh textMesh = renderer.GetComponent<TextMesh>();
                if (textMesh != null)
                {
                    long glyphTriangles = 2L * (textMesh.text ?? string.Empty)
                        .Count(character => !char.IsWhiteSpace(character));
#if UNITY_EDITOR
                    int textMaterialSlot = -1;
#endif
                    foreach (Material material in sharedMaterials)
                    {
#if UNITY_EDITOR
                        textMaterialSlot++;
#endif
                        if (material == null) continue;
                        drawCalls++;
                        triangles += glyphTriangles;
                        if (IsTransparent(material)) transparentSlots++;
#if UNITY_EDITOR
                        AddEditorDiagnostic(diagnosticLines, renderer, "legacy_text", 1, glyphTriangles,
                            material, textMaterialSlot, -1, "GeneratedGlyphs", null,
                            "Conservative two triangles per non-whitespace glyph per material slot");
#endif
                    }
                    continue;
                }

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
                    if (IsTransparent(material)) transparentSlots++;
                    int subMesh = Mathf.Min(materialSlot, mesh.subMeshCount - 1);
                    drawCalls++;
#if UNITY_EDITOR
                    long trianglesBeforeSlot = triangles;
#endif
                    if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                        triangles += (long)mesh.GetIndexCount(subMesh) / 3L;
#if UNITY_EDITOR
                    AddEditorDiagnostic(diagnosticLines, renderer, "renderer_submesh", 1,
                        triangles - trianglesBeforeSlot, material, materialSlot, subMesh,
                        mesh.GetTopology(subMesh).ToString(), mesh,
                        "Selected renderer material slot; surplus slots use final submesh; submitted indices before clipping");
#endif
                }
            }
            int visibleVegetationBatchDraws = 0;
            foreach (AerialVegetationBatchRenderer batch in vegetationBatches)
            {
                if (camera != null && (camera.cullingMask & (1 << batch.gameObject.layer)) == 0)
                    continue;

                int batchDraws = batch.NonEmptyDrawCount;
                Material material = batch.SharedMaterial;
                visibleVegetationBatchDraws += batchDraws;
                drawCalls += batchDraws;
                triangles += batch.FullyInstancedTriangleCount;
                materials.Add(material);
                if (IsTransparent(material)) transparentSlots += batchDraws;
#if UNITY_EDITOR
                AddEditorDiagnostic(diagnosticLines, batch, "aerial_vegetation", batchDraws,
                    batch.FullyInstancedTriangleCount, material, -1, -1, "InstancedTriangles", null,
                    "Conservative full authored instance count; existing audit does not cull individual batches");
#endif
            }
            int visibleCloseTreeDraws = 0;
            foreach (AirportCloseTreeLodRenderer closeTree in closeTreeLods)
            {
                if (camera != null &&
                    (camera.cullingMask & (1 << closeTree.gameObject.layer)) == 0)
                    continue;
                int batchDraws = closeTree.AuthoredMaximumDrawCount;
                Material material = closeTree.SharedCanopyMaterial;
                visibleCloseTreeDraws += batchDraws;
                drawCalls += batchDraws;
                triangles += closeTree.AuthoredMaximumSelectedTriangleCount;
                materials.Add(material);
                if (IsTransparent(material)) transparentSlots += batchDraws;
#if UNITY_EDITOR
                AddEditorDiagnostic(diagnosticLines, closeTree, "close_tree_authored_maximum", batchDraws,
                    closeTree.AuthoredMaximumSelectedTriangleCount, material, -1, -1,
                    "InstancedTriangles", null, "Conservative authored maxima, not current prepared draw count");
#endif
            }
            int branchDraws=0;
            foreach(var grove in UnityEngine.Object.FindObjectsByType<MapBranchGroveRenderer>(FindObjectsSortMode.None))
            {
                if(!grove.isActiveAndEnabled||grove.LastDraws==0||camera!=null&&(camera.cullingMask&(1<<grove.gameObject.layer))==0)continue;
                branchDraws+=grove.LastDraws;drawCalls+=grove.LastDraws;triangles+=grove.SelectedTriangles;materials.Add(grove.branchMaterial);
                if(grove.ClusterInstances>0&&grove.distantClusterMaterial!=null)materials.Add(grove.distantClusterMaterial);
#if UNITY_EDITOR
                AddEditorDiagnostic(diagnosticLines, grove, "branch_grove_submission", grove.LastDraws,
                    grove.SelectedTriangles, grove.branchMaterial, -1, -1, "InstancedTriangles", null,
                    "Selected submitted branch tiers and cluster proxies before fragment clipping");
#endif
            }
            if (camera != null && camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
            {
                Material skybox = RenderSettings.skybox;
                materials.Add(skybox);
                drawCalls++;
                if (IsTransparent(skybox)) transparentSlots++;
#if UNITY_EDITOR
                AddEditorDiagnostic(diagnosticLines, camera, "skybox", 1, 0, skybox, -1, -1,
                    "Skybox", null, "Existing audit counts one skybox draw and zero triangles");
#endif
            }
#if UNITY_EDITOR
            if (diagnosticLines != null) File.WriteAllLines(EditorDiagnosticPath, diagnosticLines);
#endif
            return new RenderBudgetSnapshot
            {
                activeRendererCount = renderers.Length +
                    vegetationBatches.Sum(batch => batch.NonEmptyDrawCount) +
                    closeTreeLods.Sum(batch => batch.AuthoredMaximumDrawCount) + branchDraws,
                estimatedVisibleRendererCount = visible.Length +
                    visibleVegetationBatchDraws + visibleCloseTreeDraws + branchDraws,
                estimatedInstancedDrawCalls = drawCalls,
                estimatedFrustumTriangles = triangles,
                uniqueMaterialCount = materials.Count,
                transparentMaterialSlots = transparentSlots,
                drawCountSemantics = "Camera-layer/frustum and selected-LOD non-null MeshRenderer/SkinnedMeshRenderer material-submesh slots; active layer-visible legacy TextMesh material slots are counted conservatively without requiring a MeshFilter; plus conservative authored aerial-vegetation and bounded airport-close tree LOD draws, before Unity batching.",
                triangleCountSemantics = "Triangle topology indices for the same effective renderer slots; two triangles per non-whitespace legacy TextMesh glyph and material slot; plus conservative authored maxima for instanced aerial vegetation and airport-close tree LODs."
            };
        }

#if UNITY_EDITOR
        private static void AddEditorDiagnostic(List<string> lines, Component source, string kind,
            int draws, long triangles, Material material, int materialSlot, int submesh,
            string topology, Mesh mesh, string semantics)
        {
            if (lines == null) return;
            string path = source.name;
            for (Transform parent = source.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            string[] columns =
            {
                path, draws.ToString(CultureInfo.InvariantCulture), material == null ? "" : material.name,
                material == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(material), kind,
                materialSlot.ToString(CultureInfo.InvariantCulture), submesh.ToString(CultureInfo.InvariantCulture),
                topology, triangles.ToString(CultureInfo.InvariantCulture),
                mesh == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(mesh), semantics
            };
            lines.Add(string.Join("\t", columns.Select(column =>
                column.Replace("\t", " ").Replace("\r", " ").Replace("\n", " "))));
        }
#endif

        private static bool IsVisibleForAudit(Renderer renderer, Camera camera, Plane[] frustum)
        {
            if (camera == null) return true;
            if ((camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) return false;

            if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() == null)
            {
                // Unity's legacy TextMesh owns generated glyph geometry internally and
                // legitimately has no MeshFilter. Querying MeshRenderer.bounds in that
                // configuration throws, so count an active layer-visible TextMesh
                // conservatively instead of allowing a diagnostic audit to fail.
                return renderer.GetComponent<TextMesh>() != null;
            }

            return GeometryUtility.TestPlanesAABB(frustum, renderer.bounds);
        }

        private static bool IsTransparent(Material material) =>
            material.renderQueue >= (int)RenderQueue.Transparent ||
            string.Equals(
                material.GetTag("RenderType", false, string.Empty),
                "Transparent",
                StringComparison.OrdinalIgnoreCase);

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
        public bool nativeAuthorityEnabled;
        public bool nativeAuthorityInitialized;
        public bool nativeAuthorityFinite;
        public string nativeAuthorityError;
        public string nativePayloadIdentity;
        public string measuredFlightMode;
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
        public string requestedQualityMode = "Unspecified";
        public bool native72ModeObserved;
        public int spaceWarpRequestedSamples, spaceWarpActiveSamples, spaceWarpSubmissionSamples;
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
        public const string CloudsOffIsolationProfile = "clouds_off";
        public const string CloudsFullSamplingIsolationProfile = "clouds_full_sampling";
        public const string CloudsVolumeIsolationProfile = "clouds_volume";
        public const string CloudsVolumeSkipIsolationProfile = "clouds_volume_skip";
        public const string CloudsAtlasIsolationProfile = "clouds_atlas";
        public const string TrafficOffIsolationProfile = "traffic_off";
        public const string VegetationOffIsolationProfile = "vegetation_off";
        public const string ConnectedGroveOffIsolationProfile = "connected_grove_off";
        public const string GroveSourceGeometryIsolationProfile = "grove_source_geometry";
        public const string BuildingsOffIsolationProfile = "buildings_off";
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
            public readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();
            public float previousWaterDisabled;
            public readonly Dictionary<Material,float> previousCloudSampling = new Dictionary<Material,float>();
            public readonly Dictionary<Material,Vector2> previousCloudVolume = new Dictionary<Material,Vector2>();
            public readonly Dictionary<MapBranchGroveRenderer,bool> previousGroveClusters = new Dictionary<MapBranchGroveRenderer,bool>();
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
            if (Application.isEditor) yield break;
            if (!AcceptanceCaptureRequested())
            {
                Debug.Log(LogPrefix + " Capture not requested by intent or a valid timed diagnostic request.");
                yield break;
            }
            Debug.Log(LogPrefix + " Capture requested; checking authored scene readiness.");
            yield return Capture();
        }

        public const string RequestFileName = "qfl_acceptance_request.json";
        public const string Native72QualityMode = "Native72";
        public const string SpaceWarp36QualityMode = "SpaceWarp36_72";
        [Serializable]
        private sealed class CaptureRequest
        {
            public string schema;
            public string expiresUtc;
            public string isolationProfile;
            public string requestedQualityMode;
            public bool startDemo;
            public bool groveObservation;
            public bool groveVisibilityDiagnostic;
            public int groveObservationStation;
        }

        public static bool TryValidateCaptureRequest(string json, DateTime utcNow, out string profile)
        {
            profile = ProductionIsolationProfile;
            try
            {
                CaptureRequest request = JsonUtility.FromJson<CaptureRequest>(json);
                if (request == null || request.schema != "questflight.acceptance-request.v1" ||
                    !DateTime.TryParse(request.expiresUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime expires) ||
                    expires <= utcNow || expires > utcNow.AddMinutes(15) ||
                    !IsSupportedIsolationProfile(request.isolationProfile) ||
                    !request.groveObservation && request.requestedQualityMode != Native72QualityMode && request.requestedQualityMode != SpaceWarp36QualityMode ||
                    request.groveObservation && (request.startDemo || request.groveObservationStation < 0 || request.groveObservationStation > 8) || request.groveVisibilityDiagnostic && !request.groveObservation) return false;
                profile = request.isolationProfile;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        public static bool CaptureRequestStartsDemo(string json, DateTime utcNow) =>
            TryValidateCaptureRequest(json, utcNow, out _) && JsonUtility.FromJson<CaptureRequest>(json).startDemo;

        public static bool TryGetCaptureQualityMode(string json, DateTime utcNow, out string mode)
        {
            mode = "Unspecified";
            if (!TryValidateCaptureRequest(json, utcNow, out _)) return false;
            CaptureRequest request = JsonUtility.FromJson<CaptureRequest>(json);
            if (request.groveObservation) return false;
            mode = request.requestedQualityMode;
            return true;
        }

        public static IDisposable BeginCaptureQualityScope(string json, DateTime utcNow, out string mode) =>
            TryGetCaptureQualityMode(json, utcNow, out mode)
                ? QuestFlight.Rendering.ProductSpaceWarpController.OverrideForAcceptanceCapture(mode == SpaceWarp36QualityMode)
                : null;

        private static bool TryReadCaptureRequest(out string profile) => TryReadCaptureRequest(out profile, out _);

        private static bool TryReadCaptureRequest(out string profile, out bool startDemo)
        {
            profile = ProductionIsolationProfile;
            startDemo = false;
            string path = Path.Combine(Application.persistentDataPath, RequestFileName);
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path);
                if (!TryValidateCaptureRequest(json, DateTime.UtcNow, out profile)) return false;
                startDemo = JsonUtility.FromJson<CaptureRequest>(json).startDemo;
                return true;
            }
            catch (IOException exception)
            {
                Debug.LogWarning(LogPrefix + " Cannot read diagnostic request: " + exception.Message);
                return false;
            }
            catch (UnauthorizedAccessException) { return false; }
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
            if (coordinators.Length != 1 ||
                coordinators[0].requestedBackend != FlightDynamicsBackendKind.JSBSimNativeDevelopment ||
                coordinators[0].ActiveBackendKind != FlightDynamicsBackendKind.JSBSimNativeDevelopment)
            {
                status = "Exactly one active native JSBSim coordinator is required.";
                return false;
            }
            if (!coordinators[0].TryGetAuthorityReadiness(out status)) return false;
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
            status = "PASS: authored production hierarchy, environment, tracked XR, and sole native JSBSim authority are ready.";
            return true;
        }

        private float observationPreviousTimeScale;
        private bool observationOwnsPause;
        private DiagnosticIsolationState observationIsolation;
        private Rigidbody observationBody;
        private RigidbodyInterpolation observationPreviousInterpolation;
        private IDisposable captureQualityScope;
        private string captureRequestedQualityMode = "Unspecified";
        private void RestoreCaptureQualityMode()
        {
            captureQualityScope?.Dispose();
            captureQualityScope = null;
        }
        private void OnDisable()
        {
            RestoreCaptureQualityMode();
            if(observationOwnsPause){Time.timeScale=observationPreviousTimeScale;observationOwnsPause=false;}
            if(observationIsolation!=null){RestoreDiagnosticIsolation(observationIsolation);observationIsolation=null;}
            if(observationBody!=null){observationBody.interpolation=observationPreviousInterpolation;observationBody=null;}
        }
        [Serializable] private sealed class GroveObservationEvidence
        {
            public string status="STATIC_APPEARANCE_ONLY_NOT_FLIGHT_PERFORMANCE",profile,backend;
            public int station;public double latitude,longitude,altitudeMslMeters;public bool nativeInitialized;
            public Vector3 simulationPosition,presentationPosition;
            public float presentationErrorMeters,nativePoseErrorMeters;
            public bool mapGroveActive,mapGroveReady,mapGroveCameraMatches;
            public int mapGroveResidentPages,mapGroveSelectedCells,mapGroveEligibleCells,mapGroveDraws,mapGroveLoadFailures;
            public int mapGroveNearCells,mapGroveMiddleCells,mapGroveFarCells,mapGroveTriangles;
            public float mapGroveFarthestMeters;public double mapGroveSelectionMs;
            public Vector3 trackedCameraPosition,trackedCameraForward;
        }
        private IEnumerator CaptureConnectedGroveObservation(CaptureRequest request)
        {
            var mode=FindFirstObjectByType<QuestFlight.Product.ProductFlightModeCoordinator>();
            var environment=productionRoot.EnvironmentRoot.GetComponentInChildren<ProductionEnvironmentRoot>(true);
            var authoredMap=environment.GetComponentInChildren<MapBranchGroveRenderer>(true);
            var pose=authoredMap!=null&&authoredMap.observationStarts!=null&&request.groveObservationStation<authoredMap.observationStarts.Length?authoredMap.observationStarts[request.groveObservationStation]:null;
            if(mode==null||pose==null||!mode.AuthoredFlightCoordinator.IsInitialized)
            {Debug.LogError("[ConnectedGroveObservation] Missing authored observation/native references");yield break;}
            mode.SelectFreeFlight();
            float started=Time.realtimeSinceStartup;
            while(mode.AuthoredModeMenuView.TransitionPending && Time.realtimeSinceStartup-started<15)yield return null;
            if(mode.State!=QuestFlight.Product.ProductFlightModeCoordinator.FlightModeState.FreeFlight||mode.AuthoredModeMenuView.TransitionPending)
            {Debug.LogError("[ConnectedGroveObservation] Free-flight presentation failed");yield break;}
            var initial=pose.Build(GeodeticReference.Kbdu);
            // A paused native teleport must not retain PhysX's older interpolated
            // transform. This diagnostic override is restored when the test ends.
            observationBody=mode.AuthoredFlightCoordinator.authoritativeRigidbody;
            observationPreviousInterpolation=observationBody.interpolation;
            observationBody.interpolation=RigidbodyInterpolation.None;
            if(!mode.AuthoredFlightCoordinator.TryResetToInitialConditions(initial))
            {Debug.LogError("[ConnectedGroveObservation] Native reset failed: "+mode.AuthoredFlightCoordinator.LastError);yield break;}
            Physics.SyncTransforms();
            // Pausing prevents the rig's next FixedUpdate from sampling the native
            // reset. Clear its interpolation history through the existing rig API.
            productionRoot.AircraftRig.ResetPresentationHistory();
            // Native JSBSim applies the aircraft pose. The tracked camera and seat
            // are never written. Pause only this explicitly requested observation;
            // unscaled streaming and OpenXR tracking continue normally.
            observationPreviousTimeScale=Time.timeScale;observationOwnsPause=true;Time.timeScale=0;
            observationIsolation=ApplyDiagnosticIsolation(request.isolationProfile,environment);
            if(request.groveVisibilityDiagnostic&&authoredMap!=null)authoredMap.BeginVisibilityDiagnostic();
            yield return new WaitForSecondsRealtime(12);
            var rig=productionRoot.AircraftRig;
            float presentationError=Vector3.Distance(rig.AircraftSimulationRoot.position,rig.AircraftVisualRoot.position);
            float nativeError=Vector3.Distance(rig.AircraftSimulationRoot.position,mode.AuthoredFlightCoordinator.CurrentState.positionUnityMeters);
            if(presentationError>0.1f||nativeError>0.1f)
            {Debug.LogError("[ConnectedGroveObservation] Presentation alignment failed: visual="+presentationError+" m native="+nativeError+" m simulation="+rig.AircraftSimulationRoot.position+" presentation="+rig.AircraftVisualRoot.position);yield break;}
            var evidence=new GroveObservationEvidence{profile=request.isolationProfile,station=request.groveObservationStation,
                backend=mode.AuthoredFlightCoordinator.ActiveBackendKind.ToString(),nativeInitialized=mode.AuthoredFlightCoordinator.IsInitialized,
                latitude=initial.latitudeDegrees,longitude=initial.longitudeDegrees,altitudeMslMeters=initial.altitudeMslMeters,
                simulationPosition=rig.AircraftSimulationRoot.position,presentationPosition=rig.AircraftVisualRoot.position,
                presentationErrorMeters=presentationError,nativePoseErrorMeters=nativeError};
            var mapGrove=environment.GetComponentInChildren<MapBranchGroveRenderer>(true);
            if(mapGrove!=null)
            {
                evidence.mapGroveActive=mapGrove.isActiveAndEnabled;evidence.mapGroveReady=mapGrove.Ready;
                evidence.mapGroveResidentPages=mapGrove.ResidentPages;evidence.mapGroveSelectedCells=mapGrove.SelectedCells;
                evidence.mapGroveEligibleCells=mapGrove.EligibleCells;evidence.mapGroveDraws=mapGrove.LastDraws;evidence.mapGroveLoadFailures=mapGrove.LoadFailures;
                evidence.mapGroveCameraMatches=mapGrove.viewingCamera==rig.TrackedCamera;
                evidence.mapGroveNearCells=mapGrove.NearCells;evidence.mapGroveMiddleCells=mapGrove.MiddleCells;evidence.mapGroveFarCells=mapGrove.FarCells;evidence.mapGroveTriangles=mapGrove.SelectedTriangles;
                evidence.mapGroveFarthestMeters=mapGrove.FarthestSelectedMeters;evidence.mapGroveSelectionMs=mapGrove.SelectionMilliseconds;
            }
            evidence.trackedCameraPosition=rig.TrackedCamera.transform.position;evidence.trackedCameraForward=rig.TrackedCamera.transform.forward;
            File.WriteAllText(Path.Combine(Application.persistentDataPath,"connected_grove_observation.json"),JsonUtility.ToJson(evidence,true));
            Debug.Log("[ConnectedGroveObservation] READY "+JsonUtility.ToJson(evidence));
            // No temporal/performance gate is run at a paused visual station.
        }
        private IEnumerator Capture()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, RequestFileName);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    captureQualityScope = BeginCaptureQualityScope(json, DateTime.UtcNow, out string mode);
                    if (captureQualityScope != null)
                    {
                        captureRequestedQualityMode = mode;
                        Debug.Log(LogPrefix + " Explicit quality mode=" + mode + "; user SpaceWarp preference retained.");
                    }
                }
                yield return CaptureInSelectedMode();
            }
            finally { RestoreCaptureQualityMode(); }
        }

        private IEnumerator CaptureInSelectedMode()
        {
            float readinessStarted = Time.realtimeSinceStartup;
            string readiness = string.Empty;
            while (!EvaluateSceneReadiness(out readiness) &&
                   Time.realtimeSinceStartup - readinessStarted < readinessTimeoutSeconds)
                yield return new WaitForSecondsRealtime(0.5f);
            bool ready = EvaluateSceneReadiness(out readiness);
            Debug.Log($"{LogPrefix} Readiness {(ready ? "PASS" : "FAIL")}: {readiness}");
            if (!ready)
            {
                QuestTemporalVisualGateReport failed = CreateReport(readiness, Time.realtimeSinceStartup - readinessStarted);
                failed.readinessPrerequisitesMet = false;
                WriteEvidence(failed);
                yield break;
            }
            FlightDynamicsCoordinator observedAuthority = FindFirstObjectByType<FlightDynamicsCoordinator>();
            string authorityFailure = string.Empty;

            string requestPath=Path.Combine(Application.persistentDataPath,RequestFileName);
            if(File.Exists(requestPath))
            {
                string requestJson=File.ReadAllText(requestPath);
                if(TryValidateCaptureRequest(requestJson,DateTime.UtcNow,out _) && JsonUtility.FromJson<CaptureRequest>(requestJson).groveObservation)
                {
                    yield return CaptureConnectedGroveObservation(JsonUtility.FromJson<CaptureRequest>(requestJson));
                    yield break;
                }
            }

            if (TryReadCaptureRequest(out _, out bool startDemo) && startDemo)
            {
                var mode = FindFirstObjectByType<QuestFlight.Product.ProductFlightModeCoordinator>();
                if (mode != null) mode.SelectDemo();
                if (mode == null || mode.State != QuestFlight.Product.ProductFlightModeCoordinator.FlightModeState.Demo)
                {
                    Debug.LogError(LogPrefix + " Explicit diagnostic demo request failed; refusing menu-only measurement.");
                    yield break;
                }
                Debug.Log(LogPrefix + " Explicit diagnostic request started the authored native-JSBSim demo route.");
                // The menu deliberately restores world renderers during its fade.
                // Establish the immutable-renderer baseline after that authored
                // transition, otherwise normal startup becomes a false violation.
                float presentationStarted = Time.realtimeSinceStartup;
                var menu = mode.AuthoredModeMenuView;
                while (menu != null && menu.TransitionPending &&
                    Time.realtimeSinceStartup - presentationStarted < 15f)
                    yield return null;
                if (menu == null || menu.TransitionPending)
                {
                    Debug.LogError(LogPrefix + " Explicit diagnostic demo request failed: presentation did not settle.");
                    yield break;
                }
            }

            ProductionEnvironmentRoot diagnosticEnvironment = productionRoot != null && productionRoot.EnvironmentRoot != null
                ? productionRoot.EnvironmentRoot.GetComponentInChildren<ProductionEnvironmentRoot>(true)
                : null;
            DiagnosticIsolationState isolation = ApplyDiagnosticIsolation(ReadIsolationProfile(), diagnosticEnvironment);
            Debug.Log($"{LogPrefix} Isolation profile={isolation.profile}; disabledRenderers={isolation.disabledRenderers.Count}; disabledBehaviours={isolation.disabledBehaviours.Count}; " +
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
                if (!ObserveAuthority(observedAuthority, ref authorityFailure)) break;
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
            if (!ObserveAuthority(observedAuthority, ref authorityFailure))
            {
                report.readinessPrerequisitesMet = false;
                report.readinessStatus = authorityFailure;
                report.nativeAuthorityError = authorityFailure;
                WriteEvidence(report);
                RestoreDiagnosticIsolation(isolation);
                yield break;
            }
            var measuredGrove = diagnosticEnvironment.GetComponentInChildren<MapBranchGroveRenderer>(true);
            report.diagnosticIsolationProfile = isolation.profile;
            report.diagnosticIsolationActive = !string.Equals(
                isolation.profile,
                ProductionIsolationProfile,
                StringComparison.Ordinal);
            report.diagnosticDisabledRendererCount = isolation.disabledRenderers.Count;
            report.diagnosticTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit;
            if (isolation.profile == ReducedTextureIsolationProfile)
                report.limitations.Add("Global mip limit diagnostic does not establish Texture2DArray residency or aerial clipmap cost.");
            if (isolation.profile == BuildingsOffIsolationProfile || isolation.profile == BuildingsVegetationOffIsolationProfile)
                report.limitations.Add("Building draw isolation retains streaming and page selection CPU work; it measures rendering removal, not the entire subsystem.");
            if (isolation.profile == CloudsOffIsolationProfile)
                report.limitations.Add("Cloud draw isolation preserves atmosphere response and skybox lighting.");
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
                if (!ObserveAuthority(observedAuthority, ref authorityFailure)) break;
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
                    spaceWarpRequested = QuestFlight.Rendering.ProductSpaceWarpController.Requested,
                    spaceWarpEnabled = QuestFlight.Rendering.ProductSpaceWarpController.Active,
                    spaceWarpSubmissionObserved = QuestFlight.Rendering.ProductSpaceWarpController.SubmissionObserved,
                    spaceWarpHistoryGeneration = QuestFlight.Rendering.ProductSpaceWarpController.HistoryGeneration,
                    sequence = sequence++,
                    unityFrameCount = Time.frameCount,
                    elapsedSeconds = Time.realtimeSinceStartup - measurementStarted,
                    frameMs = Time.unscaledDeltaTime * 1000f,
                    frameTimingAvailable = count > 0,
                    cpuFrameMs = cpu,
                    gpuFrameMs = gpu,
                    gcAllocatedBytes = allocatedBytes,
                    managedAllocationCount = managedAllocationCount,
                    groveSelectionCount = measuredGrove == null ? 0 : measuredGrove.SelectionCount,
                    groveClusterInstances = measuredGrove == null ? 0 : measuredGrove.ClusterInstances,
                    groveReplacedCells = measuredGrove == null ? 0 : measuredGrove.ReplacedBranchCells,
                    groveSourceTriangles = measuredGrove == null ? 0 : measuredGrove.SourceSelectedTriangles,
                    groveSubmittedCells = measuredGrove == null ? 0 : measuredGrove.SubmittedBranchCells,
                    groveUploadedBytes = measuredGrove == null ? 0 : measuredGrove.UploadedBytes,
                    groveGatherTotalMs = measuredGrove == null ? 0 : measuredGrove.GatherTotalMs,
                    groveFillTotalMs = measuredGrove == null ? 0 : measuredGrove.FillTotalMs,
                    groveBufferUploadTotalMs = measuredGrove == null ? 0 : measuredGrove.BufferUploadTotalMs,
                    groveOwnershipUploadTotalMs = measuredGrove == null ? 0 : measuredGrove.OwnershipUploadTotalMs
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
            if (!ObserveAuthority(observedAuthority, ref authorityFailure))
            {
                report.readinessPrerequisitesMet = false;
                report.readinessStatus = authorityFailure;
                report.nativeAuthorityError = authorityFailure;
                report.hardPerformanceGatePassed = false;
                report.workloadPerformanceGatePassed = false;
                report.deliveredCadencePerformanceGatePassed = false;
            }
            report.nativeAuthorityEnabled = observedAuthority != null && observedAuthority.isActiveAndEnabled;
            report.nativeAuthorityInitialized = observedAuthority != null && observedAuthority.IsInitialized;
            report.nativeAuthorityFinite = observedAuthority != null && observedAuthority.CurrentState.IsFinite;
            WriteEvidence(report);
            RestoreDiagnosticIsolation(isolation);
        }

        private QuestTemporalVisualGateReport CreateReport(string readiness, float readinessSeconds)
        {
            ProductionVerticalSliceRoot production = productionRoot;
            FlightDynamicsCoordinator authority = FindFirstObjectByType<FlightDynamicsCoordinator>(FindObjectsInactive.Include);
            bool ready = EvaluateSceneReadiness(out string currentReadiness);
            var mode = FindFirstObjectByType<QuestFlight.Product.ProductFlightModeCoordinator>(FindObjectsInactive.Include);
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
                productionEnvironmentContractValid = ready,
                legacyRuntimeRepairAbsent = ready,
                authoritativePhysicsBackend = authority != null && authority.IsInitialized ? authority.ActiveBackendKind.ToString() : string.Empty,
                readinessPrerequisitesMet = ready,
                readinessStatus = ready ? readiness : currentReadiness,
                nativeAuthorityEnabled = authority != null && authority.isActiveAndEnabled,
                nativeAuthorityInitialized = authority != null && authority.IsInitialized,
                nativeAuthorityFinite = authority != null && authority.IsInitialized && authority.CurrentState.IsFinite,
                nativeAuthorityError = authority != null ? authority.LastError : "Native JSBSim authority is missing.",
                nativePayloadIdentity = authority != null ? authority.NativePayloadIdentity : string.Empty,
                measuredFlightMode = mode != null ? mode.State.ToString() : "Unknown",
                requestedQualityMode = captureRequestedQualityMode,
                readinessWaitSeconds = readinessSeconds,
                warmupSeconds = warmupSeconds,
                requestedMeasurementSeconds = measurementSeconds,
                deliveredIntervalSemantics = DeliveredIntervalSemantics,
                workloadTimingSemantics = WorkloadTimingSemantics,
                hardPerformanceGateSemantics = "Machine workload metrics plus app-PID VrApi; human headset quality remains a separate gate."
            };
        }

        // A failure is latched for the entire warmup/measurement; a later recovery
        // cannot retroactively validate samples collected with an unusable authority.
        public static bool ObserveAuthority(FlightDynamicsCoordinator authority, ref string failure)
        {
            if (!string.IsNullOrEmpty(failure)) return false;
            if (authority == null) { failure = "Native JSBSim authority is missing."; return false; }
            return authority.TryGetAuthorityReadiness(out failure);
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
            report.spaceWarpRequestedSamples = report.frameSamples.Count(value => value.spaceWarpRequested);
            report.spaceWarpActiveSamples = report.frameSamples.Count(value => value.spaceWarpEnabled);
            report.spaceWarpSubmissionSamples = report.frameSamples.Count(value => value.spaceWarpSubmissionObserved);
            report.native72ModeObserved = report.frameSamples.Count > 0 && report.spaceWarpRequestedSamples == 0 && report.spaceWarpActiveSamples == 0 && report.spaceWarpSubmissionSamples == 0;
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
            report.hardPerformanceGatePassed = report.readinessPrerequisitesMet && report.workloadPerformanceGatePassed &&
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
            if (TryReadCaptureRequest(out string fileProfile)) return fileProfile;
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
            string.Equals(profile, CloudsOffIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, CloudsFullSamplingIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, CloudsVolumeIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, CloudsVolumeSkipIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, CloudsAtlasIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, TrafficOffIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, VegetationOffIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, ConnectedGroveOffIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, GroveSourceGeometryIsolationProfile, StringComparison.Ordinal) ||
            string.Equals(profile, BuildingsOffIsolationProfile, StringComparison.Ordinal) ||
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
            if (environment == null) return state;
            if(state.profile==GroveSourceGeometryIsolationProfile)
            {
                foreach(var grove in environment.GetComponentsInChildren<MapBranchGroveRenderer>(true))
                {state.previousGroveClusters.Add(grove,grove.distantClustersEnabled);grove.distantClustersEnabled=false;}
                return state;
            }
            foreach (var clouds in environment.GetComponentsInChildren<BoulderCloudField>(true))
            {
                var material = clouds.SharedCloudMaterial;
                if (material == null || !material.HasProperty("_CloudSamplingLod")) continue;
                if (state.profile == CloudsVolumeIsolationProfile || state.profile == CloudsVolumeSkipIsolationProfile || state.profile == CloudsAtlasIsolationProfile)
                {
                    if (!state.previousCloudVolume.ContainsKey(material))
                        state.previousCloudVolume.Add(material, new Vector2(material.GetFloat("_CloudVolumeSampling"), material.GetFloat("_CloudEmptySkipping")));
                    material.SetFloat("_CloudVolumeSampling", state.profile == CloudsAtlasIsolationProfile ? 0 : 1);
                    material.SetFloat("_CloudEmptySkipping", state.profile == CloudsVolumeSkipIsolationProfile ? 1 : 0);
                    Debug.Log($"{LogPrefix} Cloud volume={material.GetFloat("_CloudVolumeSampling")}; emptySkip={material.GetFloat("_CloudEmptySkipping")}");
                }
                if (state.profile == CloudsFullSamplingIsolationProfile && !state.previousCloudSampling.ContainsKey(material))
                {
                    state.previousCloudSampling.Add(material,material.GetFloat("_CloudSamplingLod"));
                    material.SetFloat("_CloudSamplingLod",0);
                }
                Debug.Log($"{LogPrefix} Cloud sampling profile={state.profile}; actualLod={material.GetFloat("_CloudSamplingLod")}");
            }
            if (state.profile == ProductionIsolationProfile || state.profile == CloudsFullSamplingIsolationProfile ||
                state.profile == CloudsVolumeIsolationProfile || state.profile == CloudsVolumeSkipIsolationProfile || state.profile == CloudsAtlasIsolationProfile) return state;
            bool sky = state.profile == SkyOnlyIsolationProfile;
            bool trees = sky || state.profile == VegetationOffIsolationProfile ||
                state.profile == BuildingsVegetationOffIsolationProfile;
            bool buildings = sky || state.profile == BuildingsOffIsolationProfile ||
                state.profile == BuildingsVegetationOffIsolationProfile;
            bool traffic = sky || state.profile == TrafficOffIsolationProfile;
            // Instanced draws have no Renderer to hide. Unsubscribe their camera callbacks.
            foreach (Behaviour behaviour in environment.GetComponentsInChildren<Behaviour>(true))
            {
                bool disable = trees && (behaviour is AirportCloseTreeLodRenderer || behaviour is AerialVegetationBatchRenderer || behaviour is AirportNearFieldGrassRenderer) ||
                    (trees || state.profile == ConnectedGroveOffIsolationProfile) && behaviour is MapBranchGroveRenderer ||
                    traffic && (behaviour is KbduTrafficRuntime || behaviour is KbduTrafficV2Runtime);
                if (!disable || !behaviour.enabled) continue;
                behaviour.enabled = false;
                state.disabledBehaviours.Add(behaviour);
            }
            var buildingPages = new HashSet<Renderer>();
            if (buildings)
                foreach (var controller in environment.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true))
                    foreach (var renderer in controller.pageRenderers)
                        if (renderer != null) buildingPages.Add(renderer);
            var cloudRenderers = new HashSet<Renderer>();
            if (state.profile == CloudsOffIsolationProfile)
                foreach (var clouds in environment.GetComponentsInChildren<BoulderCloudField>(true))
                    foreach (var renderer in clouds.SectorRenderers)
                        if (renderer != null) cloudRenderers.Add(renderer);
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.forceRenderingOff) continue;
                bool disable = sky || cloudRenderers.Contains(renderer) || buildingPages.Contains(renderer) ||
                    state.profile == WaterOffIsolationProfile && environment.essentialWater != null &&
                    (renderer.transform == environment.essentialWater || renderer.transform.IsChildOf(environment.essentialWater)) ||
                    (buildings || trees) && IsBuildingOrVegetationRenderer(renderer.transform, buildings, trees);
                if (!disable) continue;
                // Unlike enabled, streaming and LOD selection do not overwrite this flag.
                // Include presently disabled slots: they can receive a streamed mesh later.
                renderer.forceRenderingOff = true;
                state.disabledRenderers.Add(renderer);
            }
            state.previousWaterDisabled = Shader.GetGlobalFloat("_QflDiagnosticWaterDisabled");
            if (state.profile == WaterOffIsolationProfile)
                Shader.SetGlobalFloat("_QflDiagnosticWaterDisabled", 1f);
            return state;
        }

        private static void RestoreDiagnosticIsolation(DiagnosticIsolationState state)
        {
            if (state == null) return;
            foreach(var setting in state.previousGroveClusters)
                if(setting.Key!=null)setting.Key.distantClustersEnabled=setting.Value;
            foreach (var setting in state.previousCloudSampling)
                if (setting.Key != null) setting.Key.SetFloat("_CloudSamplingLod",setting.Value);
            foreach (var setting in state.previousCloudVolume)
                if (setting.Key != null) { setting.Key.SetFloat("_CloudVolumeSampling",setting.Value.x); setting.Key.SetFloat("_CloudEmptySkipping",setting.Value.y); }
            foreach (Renderer renderer in state.disabledRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            foreach (Behaviour behaviour in state.disabledBehaviours)
                if (behaviour != null) behaviour.enabled = true;
            if (state.profile == WaterOffIsolationProfile)
                Shader.SetGlobalFloat("_QflDiagnosticWaterDisabled", state.previousWaterDisabled);
            QualitySettings.globalTextureMipmapLimit = state.previousTextureMipmapLimit;
        }

        private static bool IsBuildingOrVegetationRenderer(Transform transform, bool buildings, bool trees)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string objectName = current.name;
                if (buildings && (objectName.StartsWith("buildings_", StringComparison.Ordinal) ||
                        string.Equals(objectName, AirportCoreBenchmarkRoot.GameObjectName, StringComparison.Ordinal)) ||
                    trees && (objectName.StartsWith("vegetation_", StringComparison.Ordinal) ||
                        string.Equals(objectName, "SparseVegetationProduction", StringComparison.Ordinal)))
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
            // Quest shell launches may replace Activity intent extras. A bounded,
            // explicitly requested host diagnostic also works through app-owned storage.
            if (TryReadCaptureRequest(out _)) return true;
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
