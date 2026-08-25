using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Environment;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Editor.Tests
{
    /// <summary>
    /// Read-only evidence capture for the accepted Traffic V1 baseline. The runner opens
    /// the serialized production scene, but never writes or reconfigures its Main Camera,
    /// aircraft, XR hierarchy, or product objects. All camera movement occurs on one
    /// transient HideAndDontSave diagnostic camera.
    /// </summary>
    public static class TrafficV2BaselineBatchRunner
    {
        private const string OutputVariable = "QFL_TRAFFIC_V2_BASELINE_DIR";
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const string RunnerSourcePath = "Assets/Editor/Tests/TrafficV2/TrafficV2BaselineBatchRunner.cs";
        private const string AcceptedSourceSha = "fda11a25eb526824d82a47330f2be80b1410bb64";
        private const string FrozenSpecSha256 = "8e1b3f7442652128b306a0706d3eac02b59750158839ca0443351312fcbf09c7";
        private const int Width = 1280;
        private const int Height = 720;
        private const int ResolvablePixels = 2;
        private const int ExpectedViewCount = 15;
        private const int SimulationWarmupIterations = 100;
        private const int SimulationMeasuredIterations = 1000;
        private const int PreparationWarmupIterations = 100;
        private const int PreparationMeasuredIterations = 225;
        private const int DrawMeshInstancedLimit = 1023;

        // Positions are KBDU local ENU metres (+X east, +Z north). Cockpit-relative
        // definitions freeze offsets and rotations from the serialized Main Camera pose.
        // The Airport Boulevard, Diagonal Highway, reservoir, and AGL views retain the
        // accepted ProductionVisualQa traffic target/camera constants where applicable.
        private static readonly FrozenView[] Views =
        {
            FrozenView.Cockpit("cockpit_runway_takeoff", Vector3.zero, new Vector3(0f, 0f, 0f), 72f),
            FrozenView.Cockpit("left_airport_glider_road", Vector3.zero, new Vector3(0f, -90f, 0f), 72f),
            FrozenView.Absolute("airport_boulevard", new Vector3(-30f, 120f, -650f), new Vector3(32.5f, 342.5f, 0f), 52f),
            FrozenView.Absolute("diagonal_highway_approach", new Vector3(-312f, 8.2f, 1578f), new Vector3(16.2f, 326.8f, 0f), 40f),
            FrozenView.Absolute("highway_flyover", new Vector3(-899f, 240f, 1058f), new Vector3(62f, 45f, 0f), 55f),
            FrozenView.Absolute("reservoir", new Vector3(400f, 58f, 3220f), new Vector3(20f, 320.2f, 0f), 50f),
            FrozenView.Absolute("downtown", new Vector3(-3400f, 550f, -2300f), new Vector3(44.3f, 225f, 0f), 55f),
            FrozenView.Absolute("residential", new Vector3(-2000f, 350f, -1000f), new Vector3(26.3f, 225f, 0f), 55f),
            FrozenView.Absolute("industrial", new Vector3(-1250f, 300f, -850f), new Vector3(22.9f, 230.7f, 0f), 55f),
            FrozenView.Absolute("map_periphery", new Vector3(10500f, 900f, 10000f), new Vector3(3.6f, 226.4f, 0f), 58f),
            FrozenView.Absolute("agl_300m_oblique", new Vector3(-450f, 300f, -340f), new Vector3(9f, 3f, 0f), 58f),
            FrozenView.Absolute("agl_800m_aerial", new Vector3(-450f, 800f, -340f), new Vector3(40.4f, 0f, 0f), 58f),
            FrozenView.Cockpit("head_pan_90deg", Vector3.zero, new Vector3(0f, 90f, 0f), 72f),
            FrozenView.Cockpit("rearward_glance", Vector3.zero, new Vector3(0f, 180f, 0f), 72f),
            FrozenView.Absolute("aircraft_turn_180deg", new Vector3(-450f, 300f, -340f), new Vector3(9f, 183f, 0f), 58f)
        };

        public static void RunBatch()
        {
            string outputDirectory = System.Environment.GetEnvironmentVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new InvalidOperationException(OutputVariable + " is required.");
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string screenshotsDirectory = Path.Combine(outputDirectory, "screenshots");
            Directory.CreateDirectory(screenshotsDirectory);

            if (Views.Length != ExpectedViewCount || Views.Select(view => view.id).Distinct().Count() != ExpectedViewCount)
                throw new InvalidOperationException("The frozen Traffic V2 baseline view inventory is invalid.");

            BaselineReport report = new BaselineReport
            {
                schema = "questflight.traffic-v2.accepted-baseline.v1",
                status = "CAPTURED_WITH_EXPLICIT_LIMITATIONS",
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                acceptedSourceSha = AcceptedSourceSha,
                frozenSpecSha256 = FrozenSpecSha256,
                unityVersion = Application.unityVersion,
                scenePath = ScenePath,
                renderWidth = Width,
                renderHeight = Height,
                resolvablePixelThreshold = ResolvablePixels,
                evidenceGenerator = DescribeEvidenceGenerator(),
                evidenceLimitations = EvidenceLimitations(),
                recordedBaselineDefects = RecordedDefects()
            };

            GameObject diagnosticObject = null;
            string sceneDiskPath = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScenePath));
            report.sceneSha256Before = Sha256(sceneDiskPath);
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Camera[] mainCameras = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                    .Where(camera => camera.name == "Main Camera" && camera.CompareTag("MainCamera"))
                    .ToArray();
                if (mainCameras.Length != 1)
                    throw new InvalidOperationException("Expected exactly one serialized tagged Main Camera; found " + mainCameras.Length + ".");
                KbduTrafficRuntime[] runtimes = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<KbduTrafficRuntime>(true))
                    .ToArray();
                if (runtimes.Length != 1)
                    throw new InvalidOperationException("Expected exactly one serialized KbduTrafficRuntime; found " + runtimes.Length + ".");

                Camera mainCamera = mainCameras[0];
                KbduTrafficRuntime runtime = runtimes[0];
                report.mainCameraHierarchyPath = HierarchyPath(mainCamera.transform);
                report.trafficRuntimeHierarchyPath = HierarchyPath(runtime.transform);
                report.mainCameraWasNeverWritten = true;
                report.productSceneWasNeverSaved = true;

                diagnosticObject = new GameObject("Traffic V2 Baseline Diagnostic Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                Camera diagnostic = diagnosticObject.AddComponent<Camera>();
                diagnostic.CopyFrom(mainCamera);
                diagnostic.enabled = false;
                diagnostic.stereoTargetEye = StereoTargetEyeMask.None;
                diagnostic.targetTexture = null;
                diagnostic.aspect = Width / (float)Height;

                Manifest manifest = JsonUtility.FromJson<Manifest>(runtime.TrafficManifest.text);
                ValidateManifest(manifest);
                report.manifest = DescribeManifest(runtime, manifest);
                ReflectionAccess reflection = new ReflectionAccess(runtime);
                reflection.ParseManifest();

                foreach (FrozenView frozen in Views)
                {
                    ViewResult view = CaptureView(
                        frozen,
                        mainCamera,
                        diagnostic,
                        runtime,
                        manifest,
                        reflection,
                        screenshotsDirectory);
                    report.views.Add(view);
                }
                report.renderBudgetSummary = SummarizeRenderBudgets(report.views);
                report.fleetByCameraCsvPath = WriteFleetByCameraCsv(
                    report.views,
                    outputDirectory);

                try
                {
                    report.editorPlanningMetrics = MeasureEditorPlanningMetrics(
                        runtime,
                        mainCamera,
                        diagnostic,
                        reflection);
                }
                catch (Exception exception)
                {
                    report.errors.Add("Editor planning benchmark failed: " + exception);
                    report.status = "BASELINE_CAPTURE_INCOMPLETE";
                }

                report.contactSheetPath = BuildContactSheet(report.views, outputDirectory);
                report.capturedViewCount = report.views.Count(view => string.IsNullOrEmpty(view.captureError));
                report.reflectionSucceededViewCount = report.views.Count(view => string.IsNullOrEmpty(view.reflectionError));
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
                report.status = "BASELINE_CAPTURE_INCOMPLETE";
            }
            finally
            {
                if (diagnosticObject != null)
                    Object.DestroyImmediate(diagnosticObject);
                report.sceneSha256After = File.Exists(sceneDiskPath) ? Sha256(sceneDiskPath) : "MISSING";
                report.sceneFileUnchanged = string.Equals(report.sceneSha256Before, report.sceneSha256After, StringComparison.Ordinal);
            }

            string jsonPath = Path.Combine(outputDirectory, "traffic_v2_baseline.json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            string reviewPath = Path.Combine(outputDirectory, "traffic_v2_baseline_review.md");
            File.WriteAllText(reviewPath, BuildReview(report), new UTF8Encoding(false));
            WriteHashManifest(outputDirectory, jsonPath, reviewPath, report);

            Debug.Log("[QuestFlightLab][TrafficV2Baseline] views=" + report.capturedViewCount + "/" + ExpectedViewCount +
                      " output=" + outputDirectory + " status=" + report.status);
            if (report.errors.Count > 0 || report.capturedViewCount != ExpectedViewCount || !report.sceneFileUnchanged)
                throw new InvalidOperationException("Traffic V2 accepted-baseline capture was incomplete; inspect " + jsonPath + ".");
        }

        private static ViewResult CaptureView(
            FrozenView frozen,
            Camera mainCamera,
            Camera diagnostic,
            KbduTrafficRuntime runtime,
            Manifest manifest,
            ReflectionAccess reflection,
            string screenshotsDirectory)
        {
            Vector3 position = frozen.cockpitRelative
                ? mainCamera.transform.TransformPoint(frozen.position)
                : frozen.position;
            Quaternion rotation = frozen.cockpitRelative
                ? mainCamera.transform.rotation * Quaternion.Euler(frozen.euler)
                : Quaternion.Euler(frozen.euler);
            diagnostic.transform.SetPositionAndRotation(position, rotation);
            diagnostic.fieldOfView = frozen.fov;
            diagnostic.aspect = Width / (float)Height;

            ViewResult result = new ViewResult
            {
                id = frozen.id,
                poseAuthority = frozen.cockpitRelative ? "FROZEN_MAIN_CAMERA_RELATIVE" : "FROZEN_KBDU_ENU_ABSOLUTE",
                frozenPositionOrOffset = frozen.position,
                frozenEuler = frozen.euler,
                resolvedPosition = position,
                resolvedEuler = rotation.eulerAngles,
                fieldOfView = frozen.fov,
                pngPath = "screenshots/" + frozen.id + ".png"
            };

            try
            {
                result.selection = reflection.PrepareAndRead(diagnostic, manifest);
            }
            catch (Exception exception)
            {
                result.reflectionError = exception.GetBaseException().Message;
            }

            try
            {
                result.renderBudget = CaptureRenderBudget(
                    diagnostic,
                    runtime,
                    result.selection);
            }
            catch (Exception exception)
            {
                result.renderBudgetError = exception.GetBaseException().Message;
            }

            try
            {
                CountIndependentCandidates(diagnostic, runtime, manifest, result);
            }
            catch (Exception exception)
            {
                result.projectionError = exception.GetBaseException().Message;
            }

            string pngPath = Path.Combine(screenshotsDirectory, frozen.id + ".png");
            try
            {
                Texture2D image = Render(diagnostic);
                File.WriteAllBytes(pngPath, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                result.pngBytes = new FileInfo(pngPath).Length;
                result.pngSha256 = Sha256(pngPath);
            }
            catch (Exception exception)
            {
                result.captureError = exception.GetBaseException().Message;
            }
            return result;
        }

        private static ViewRenderBudget CaptureRenderBudget(
            Camera diagnostic,
            KbduTrafficRuntime runtime,
            SelectionSnapshot selection)
        {
            if (selection == null)
                throw new InvalidOperationException("V1 selection is unavailable for procedural render accounting.");
            QuestFlightLab.Runtime.RenderBudgetSnapshot staticAudit =
                QuestFlightLab.Runtime.QuestRenderBudgetAudit.Capture(diagnostic);
            Mesh[] nearMeshes = runtime.NearArchetypeMeshes;
            Mesh[] farMeshes = runtime.FarProxyMeshes;
            TrafficProceduralBudget traffic = new TrafficProceduralBudget
            {
                accountingSemantics = "Selected V1 instance count multiplied by the bound single-submesh mesh triangle count; one attempted accepted-V1 draw per non-empty batch and one shared opaque material.",
                nearBatchCounts = selection.nearBatchCounts.ToArray(),
                farBatchCounts = selection.farBatchCounts.ToArray(),
                nearTrianglesPerInstance = TriangleCounts(nearMeshes),
                farTrianglesPerInstance = TriangleCounts(farMeshes)
            };
            traffic.nearBatchTriangles = BatchTriangles(
                traffic.nearBatchCounts,
                traffic.nearTrianglesPerInstance);
            traffic.farBatchTriangles = BatchTriangles(
                traffic.farBatchCounts,
                traffic.farTrianglesPerInstance);
            traffic.selectedDrawCalls = traffic.nearBatchCounts.Count(count => count > 0) +
                                        traffic.farBatchCounts.Count(count => count > 0);
            traffic.safeChunkedDrawCalls = traffic.nearBatchCounts.Sum(ChunkedDrawCount) +
                                           traffic.farBatchCounts.Sum(ChunkedDrawCount);
            traffic.selectedTriangles = traffic.nearBatchTriangles.Sum() + traffic.farBatchTriangles.Sum();
            traffic.selectedMaterialCount = traffic.selectedDrawCalls > 0 ? 1 : 0;
            traffic.maximumBatchCount = Mathf.Max(Maximum(traffic.nearBatchCounts), Maximum(traffic.farBatchCounts));
            traffic.anyBatchOver1023 = traffic.maximumBatchCount > DrawMeshInstancedLimit;

            return new ViewRenderBudget
            {
                staticAudit = staticAudit,
                traffic = traffic,
                aggregateEstimatedDrawCalls = staticAudit.estimatedInstancedDrawCalls + traffic.selectedDrawCalls,
                aggregateEstimatedTriangles = staticAudit.estimatedFrustumTriangles + traffic.selectedTriangles,
                aggregateUniqueMaterialCount = staticAudit.uniqueMaterialCount + traffic.selectedMaterialCount,
                aggregateTransparentMaterialSlots = staticAudit.transparentMaterialSlots,
                aggregateSemantics = "QuestRenderBudgetAudit static estimate plus separately accounted accepted V1 procedural traffic omitted by that audit. Traffic uses a distinct shared opaque material."
            };
        }

        private static int[] TriangleCounts(Mesh[] meshes)
        {
            int[] triangles = new int[meshes.Length];
            for (int index = 0; index < meshes.Length; index++)
            {
                Mesh mesh = meshes[index];
                if (mesh == null || mesh.subMeshCount != 1 || mesh.GetTopology(0) != UnityEngine.MeshTopology.Triangles)
                    throw new InvalidOperationException("Traffic batch mesh " + index + " is missing or not a single triangle submesh.");
                triangles[index] = checked((int)(mesh.GetIndexCount(0) / 3u));
            }
            return triangles;
        }

        private static long[] BatchTriangles(int[] counts, int[] trianglesPerInstance)
        {
            if (counts.Length != trianglesPerInstance.Length)
                throw new InvalidOperationException("Traffic batch count/mesh inventory mismatch.");
            long[] triangles = new long[counts.Length];
            for (int index = 0; index < counts.Length; index++)
                triangles[index] = (long)counts[index] * trianglesPerInstance[index];
            return triangles;
        }

        private static int ChunkedDrawCount(int count)
        {
            return count <= 0 ? 0 : (count + DrawMeshInstancedLimit - 1) / DrawMeshInstancedLimit;
        }

        private static RenderBudgetSummary SummarizeRenderBudgets(List<ViewResult> views)
        {
            ViewRenderBudget[] budgets = views
                .Where(view => view.renderBudget != null)
                .Select(view => view.renderBudget)
                .ToArray();
            RenderBudgetSummary summary = new RenderBudgetSummary
            {
                viewCountWithBudget = budgets.Length,
                viewCountRequired = ExpectedViewCount,
                conservativeCeilingSemantics = "Independent maxima across the 15 static benchmark audits plus accepted V1 authored procedural ceilings (9 draws, 129,312 triangles, one distinct opaque material)."
            };
            if (budgets.Length == 0) return summary;
            summary.maximumStaticDrawCalls = budgets.Max(budget => budget.staticAudit.estimatedInstancedDrawCalls);
            summary.maximumStaticTriangles = budgets.Max(budget => budget.staticAudit.estimatedFrustumTriangles);
            summary.maximumStaticMaterials = budgets.Max(budget => budget.staticAudit.uniqueMaterialCount);
            summary.maximumSelectedTrafficDrawCalls = budgets.Max(budget => budget.traffic.selectedDrawCalls);
            summary.maximumSelectedTrafficTriangles = budgets.Max(budget => budget.traffic.selectedTriangles);
            summary.maximumSelectedTrafficMaterials = budgets.Max(budget => budget.traffic.selectedMaterialCount);
            summary.maximumObservedAggregateDrawCalls = budgets.Max(budget => budget.aggregateEstimatedDrawCalls);
            summary.maximumObservedAggregateTriangles = budgets.Max(budget => budget.aggregateEstimatedTriangles);
            summary.maximumObservedAggregateMaterials = budgets.Max(budget => budget.aggregateUniqueMaterialCount);
            summary.conservativeAuthoredAggregateDrawCeiling = summary.maximumStaticDrawCalls + 9;
            summary.conservativeAuthoredAggregateTriangleCeiling = summary.maximumStaticTriangles + 129312L;
            summary.conservativeAuthoredAggregateMaterialCeiling = summary.maximumStaticMaterials + 1;
            summary.maximumFarBatchCount = budgets.Max(budget => Maximum(budget.traffic.farBatchCounts));
            summary.anyAcceptedV1BatchOver1023 = budgets.Any(budget => budget.traffic.anyBatchOver1023);
            return summary;
        }

        private static Texture2D Render(Camera camera)
        {
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static EditorPlanningMetrics MeasureEditorPlanningMetrics(
            KbduTrafficRuntime runtime,
            Camera mainCamera,
            Camera diagnostic,
            ReflectionAccess reflection)
        {
            EditorPlanningMetrics metrics = new EditorPlanningMetrics
            {
                evidenceClass = "EDITOR_MONO_NON_QUEST_PLANNING_EVIDENCE",
                qualification = "Planning evidence only; not Quest 3 CPU, allocation, frame-time, thermal, or 72 Hz acceptance.",
                questPerformanceEvidence = false,
                allocationCounter = "System.GC.GetAllocatedBytesForCurrentThread",
                managedThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId,
                simulationStepSeconds = KbduTrafficRuntime.SimulationStepSeconds,
                graphicsDrawMeshInstancedLimit = DrawMeshInstancedLimit,
                stopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                editorMachine = CaptureEditorMachineIdentity()
            };

            reflection.InitializeSimulation();
            for (int index = 0; index < SimulationWarmupIterations; index++)
                reflection.StepSimulation(KbduTrafficRuntime.SimulationStepSeconds);

            long[] stepTicks = new long[SimulationMeasuredIterations];
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < SimulationMeasuredIterations; index++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                reflection.StepSimulation(KbduTrafficRuntime.SimulationStepSeconds);
                stepTicks[index] = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            }
            long stepAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            metrics.simulationStep = SummarizeTiming(
                stepTicks,
                SimulationWarmupIterations,
                stepAllocatedBytes,
                "Current-thread allocation counter surrounds only the 1,000 StepSimulation delegate calls.");

            int[] liveFarBatchCounts = reflection.ReadFarBatchCountsReference();
            int maximumFarBatch = 0;
            int[] perViewWarmups = new int[Views.Length];
            for (int index = 0; index < PreparationWarmupIterations; index++)
            {
                int viewIndex = index % Views.Length;
                perViewWarmups[viewIndex]++;
                ApplyFrozenPose(Views[viewIndex], mainCamera, diagnostic);
                reflection.PrepareVisibleInstances(diagnostic);
                maximumFarBatch = Mathf.Max(maximumFarBatch, Maximum(liveFarBatchCounts));
            }

            if (PreparationMeasuredIterations % Views.Length != 0)
                throw new InvalidOperationException("Measured preparation iterations must divide evenly across frozen views.");
            int samplesPerView = PreparationMeasuredIterations / Views.Length;
            long[][] perViewTicks = new long[Views.Length][];
            int[] perViewOffsets = new int[Views.Length];
            for (int index = 0; index < perViewTicks.Length; index++)
                perViewTicks[index] = new long[samplesPerView];
            long[] preparationTicks = new long[PreparationMeasuredIterations];
            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < PreparationMeasuredIterations; index++)
            {
                int viewIndex = index % Views.Length;
                ApplyFrozenPose(Views[viewIndex], mainCamera, diagnostic);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                reflection.PrepareVisibleInstances(diagnostic);
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                preparationTicks[index] = elapsed;
                perViewTicks[viewIndex][perViewOffsets[viewIndex]++] = elapsed;
                maximumFarBatch = Mathf.Max(maximumFarBatch, Maximum(liveFarBatchCounts));
            }
            long preparationAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            metrics.visibilityPreparation = SummarizeTiming(
                preparationTicks,
                PreparationWarmupIterations,
                preparationAllocatedBytes,
                "Current-thread allocation counter surrounds all 225 preparation delegate calls and diagnostic pose assignments; per-view allocation is not separately measured.");
            for (int index = 0; index < Views.Length; index++)
            {
                metrics.visibilityPreparationByView.Add(new VisibilityPoseTiming
                {
                    viewId = Views[index].id,
                    timing = SummarizeTiming(
                        perViewTicks[index],
                        perViewWarmups[index],
                        -1,
                        "NOT_SEPARATELY_MEASURED; use the aggregate visibilityPreparation allocation counter.")
                });
            }
            VisibilityPoseTiming worst = metrics.visibilityPreparationByView
                .OrderByDescending(item => item.timing.p95Microseconds)
                .ThenBy(item => item.viewId, StringComparer.Ordinal)
                .First();
            metrics.worstVisibilityPreparationViewId = worst.viewId;
            metrics.worstVisibilityPreparationCriterion = "Highest per-view p95 microseconds across 15 measured samples per frozen view.";
            metrics.preparationPoseCount = Views.Length;
            metrics.maxFarBatchCountObserved = maximumFarBatch;
            metrics.farBatchLimitExceeded = maximumFarBatch > DrawMeshInstancedLimit;
            metrics.runtimeMemory = CaptureRuntimeMemory(runtime);
            return metrics;
        }

        private static void ApplyFrozenPose(FrozenView frozen, Camera mainCamera, Camera diagnostic)
        {
            Vector3 position = frozen.cockpitRelative
                ? mainCamera.transform.TransformPoint(frozen.position)
                : frozen.position;
            Quaternion rotation = frozen.cockpitRelative
                ? mainCamera.transform.rotation * Quaternion.Euler(frozen.euler)
                : Quaternion.Euler(frozen.euler);
            diagnostic.transform.SetPositionAndRotation(position, rotation);
            diagnostic.fieldOfView = frozen.fov;
            diagnostic.aspect = Width / (float)Height;
        }

        private static TimingDistribution SummarizeTiming(
            long[] ticks,
            int warmupIterations,
            long allocatedBytes,
            string allocationScope)
        {
            long[] sorted = ticks.ToArray();
            Array.Sort(sorted);
            double ticksToMicroseconds = 1000000.0 / System.Diagnostics.Stopwatch.Frequency;
            double sum = 0.0;
            for (int index = 0; index < ticks.Length; index++) sum += ticks[index];
            return new TimingDistribution
            {
                warmupIterations = warmupIterations,
                measuredIterations = ticks.Length,
                meanMicroseconds = sum / ticks.Length * ticksToMicroseconds,
                p50Microseconds = Percentile(sorted, 0.50) * ticksToMicroseconds,
                p95Microseconds = Percentile(sorted, 0.95) * ticksToMicroseconds,
                maxMicroseconds = sorted[sorted.Length - 1] * ticksToMicroseconds,
                editorMonoThreadAllocatedBytes = allocatedBytes,
                editorMonoThreadAllocatedBytesPerIteration = allocatedBytes >= 0
                    ? allocatedBytes / (double)ticks.Length
                    : -1.0,
                allocationScope = allocationScope,
                timerApi = "System.Diagnostics.Stopwatch.GetTimestamp",
                stopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                rawTickEncoding = "signed_int64_little_endian_stopwatch_ticks_in_measurement_order",
                rawTickSha256 = Sha256Ticks(ticks)
            };
        }

        private static string Sha256Ticks(long[] ticks)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = new byte[sizeof(long)];
                for (int index = 0; index < ticks.Length; index++)
                {
                    ulong value = unchecked((ulong)ticks[index]);
                    for (int offset = 0; offset < bytes.Length; offset++)
                        bytes[offset] = (byte)(value >> (offset * 8));
                    hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static EditorMachineIdentity CaptureEditorMachineIdentity()
        {
            return new EditorMachineIdentity
            {
                qualification = "Editor host identity for reproducibility only; none of these fields establish Quest hardware performance.",
                operatingSystem = SystemInfo.operatingSystem,
                operatingSystemFamily = SystemInfo.operatingSystemFamily.ToString(),
                deviceModel = SystemInfo.deviceModel,
                processorType = SystemInfo.processorType,
                logicalProcessorCount = SystemInfo.processorCount,
                processorFrequencyMHz = SystemInfo.processorFrequency,
                systemMemoryMiB = SystemInfo.systemMemorySize,
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceVendor = SystemInfo.graphicsDeviceVendor,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
                graphicsMemoryMiB = SystemInfo.graphicsMemorySize,
                graphicsMultiThreaded = SystemInfo.graphicsMultiThreaded,
                batchMode = Application.isBatchMode,
                processBitness = IntPtr.Size * 8
            };
        }

        private static long Percentile(long[] sorted, double percentile)
        {
            int index = Mathf.Clamp(
                (int)Math.Ceiling(percentile * sorted.Length) - 1,
                0,
                sorted.Length - 1);
            return sorted[index];
        }

        private static int Maximum(int[] values)
        {
            int maximum = 0;
            for (int index = 0; index < values.Length; index++)
                maximum = Mathf.Max(maximum, values[index]);
            return maximum;
        }

        private static RuntimeMemorySnapshot CaptureRuntimeMemory(KbduTrafficRuntime runtime)
        {
            RuntimeMemorySnapshot snapshot = new RuntimeMemorySnapshot
            {
                measurementApi = "UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong",
                diskByteSemantics = "Imported asset file bytes; duplicate asset paths are counted once in aggregateUniqueDiskBytes.",
                runtimeByteSemantics = "Editor runtime object memory; duplicate Unity instance IDs are counted once in aggregateUniqueRuntimeBytes. This is not Android resident memory."
            };
            HashSet<int> runtimeObjects = new HashSet<int>();
            HashSet<string> diskAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddMemoryObject(snapshot, runtimeObjects, diskAssets, "trafficManifest", runtime.TrafficManifest);
            AddMemoryObject(snapshot, runtimeObjects, diskAssets, "sharedOpaqueMaterial", runtime.SharedOpaqueMaterial);
            Mesh[] near = runtime.NearArchetypeMeshes;
            for (int index = 0; index < near.Length; index++)
                AddMemoryObject(snapshot, runtimeObjects, diskAssets, "nearMesh[" + index + "]", near[index]);
            Mesh[] far = runtime.FarProxyMeshes;
            for (int index = 0; index < far.Length; index++)
                AddMemoryObject(snapshot, runtimeObjects, diskAssets, "proxyMesh[" + index + "]", far[index]);

            Material material = runtime.SharedOpaqueMaterial;
            if (material != null)
            {
                string[] textureProperties = material.GetTexturePropertyNames();
                Array.Sort(textureProperties, StringComparer.Ordinal);
                foreach (string property in textureProperties)
                {
                    Texture texture = material.GetTexture(property);
                    if (texture != null)
                        AddMemoryObject(snapshot, runtimeObjects, diskAssets, "materialTexture[" + property + "]", texture);
                }
            }
            snapshot.uniqueRuntimeObjectCount = runtimeObjects.Count;
            snapshot.uniqueDiskAssetCount = diskAssets.Count;
            return snapshot;
        }

        private static void AddMemoryObject(
            RuntimeMemorySnapshot snapshot,
            HashSet<int> runtimeObjects,
            HashSet<string> diskAssets,
            string label,
            Object value)
        {
            if (value == null) return;
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(value).Replace('\\', '/');
            long diskBytes = 0;
            if (!string.IsNullOrEmpty(assetPath))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string diskPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
                if (File.Exists(diskPath)) diskBytes = new FileInfo(diskPath).Length;
            }
            long runtimeBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(value);
            snapshot.objects.Add(new RuntimeMemoryRecord
            {
                label = label,
                unityType = value.GetType().FullName,
                instanceId = value.GetInstanceID(),
                assetPath = assetPath,
                runtimeBytes = runtimeBytes,
                diskBytes = diskBytes
            });
            if (runtimeObjects.Add(value.GetInstanceID())) snapshot.aggregateUniqueRuntimeBytes += runtimeBytes;
            if (!string.IsNullOrEmpty(assetPath) && diskAssets.Add(assetPath)) snapshot.aggregateUniqueDiskBytes += diskBytes;
        }

        private static void CountIndependentCandidates(
            Camera camera,
            KbduTrafficRuntime runtime,
            Manifest manifest,
            ViewResult result)
        {
            result.independentParkedByArchetype = new int[KbduTrafficRuntime.ArchetypeCount];
            result.independentMovingByArchetype = new int[KbduTrafficRuntime.ArchetypeCount];
            Mesh[] meshes = runtime.NearArchetypeMeshes;
            for (int index = 0; index < manifest.parked.Length; index++)
            {
                ParkedRecord vehicle = manifest.parked[index];
                if (!ValidArchetype(vehicle.archetype, meshes))
                    continue;
                Matrix4x4 matrix = Matrix4x4.TRS(
                    new Vector3(vehicle.x, vehicle.y, vehicle.z),
                    Quaternion.Euler(0f, vehicle.yaw_degrees, 0f),
                    Vector3.one);
                if (ProjectsAtLeast2x2(camera, meshes[vehicle.archetype].bounds, matrix))
                    result.independentParkedByArchetype[vehicle.archetype]++;
            }
            for (int index = 0; index < manifest.moving.Length; index++)
            {
                MovingRecord vehicle = manifest.moving[index];
                if (!ValidArchetype(vehicle.archetype, meshes) || vehicle.route_index < 0 || vehicle.route_index >= manifest.routes.Length)
                    continue;
                EvaluateRoute(manifest.routes[vehicle.route_index], vehicle.initial_distance_meters, out Vector3 position, out Quaternion rotation);
                Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
                if (ProjectsAtLeast2x2(camera, meshes[vehicle.archetype].bounds, matrix))
                    result.independentMovingByArchetype[vehicle.archetype]++;
            }
            result.independentParkedResolvable = result.independentParkedByArchetype.Sum();
            result.independentMovingResolvable = result.independentMovingByArchetype.Sum();
        }

        private static bool ValidArchetype(int archetype, Mesh[] meshes)
        {
            return archetype >= 0 && archetype < KbduTrafficRuntime.ArchetypeCount &&
                   meshes != null && archetype < meshes.Length && meshes[archetype] != null;
        }

        private static bool ProjectsAtLeast2x2(Camera camera, Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            int pointsInFront = 0;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 local = center + Vector3.Scale(extents, new Vector3(x, y, z));
                Vector3 screen = camera.WorldToScreenPoint(matrix.MultiplyPoint3x4(local));
                if (screen.z <= camera.nearClipPlane)
                    continue;
                pointsInFront++;
                minX = Mathf.Min(minX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxX = Mathf.Max(maxX, screen.x);
                maxY = Mathf.Max(maxY, screen.y);
            }
            if (pointsInFront == 0 || maxX < 0f || maxY < 0f || minX > Width || minY > Height)
                return false;
            float clippedWidth = Mathf.Min(Width, maxX) - Mathf.Max(0f, minX);
            float clippedHeight = Mathf.Min(Height, maxY) - Mathf.Max(0f, minY);
            return clippedWidth >= ResolvablePixels && clippedHeight >= ResolvablePixels;
        }

        private static void EvaluateRoute(RouteRecord route, float distance, out Vector3 position, out Quaternion rotation)
        {
            RouteSample[] samples = route.samples;
            float routeLength = route.length_meters > 0f ? route.length_meters : samples[samples.Length - 1].distance;
            distance = Mathf.Repeat(distance, Mathf.Max(routeLength, 0.001f));
            int low = 0;
            int high = samples.Length - 1;
            while (low + 1 < high)
            {
                int middle = (low + high) >> 1;
                if (samples[middle].distance <= distance) low = middle;
                else high = middle;
            }
            RouteSample start = samples[low];
            RouteSample end = samples[Mathf.Min(low + 1, samples.Length - 1)];
            float t = Mathf.Clamp01((distance - start.distance) / Mathf.Max(0.001f, end.distance - start.distance));
            Vector3 a = new Vector3(start.x, start.y, start.z);
            Vector3 b = new Vector3(end.x, end.y, end.z);
            position = Vector3.LerpUnclamped(a, b, t);
            Vector3 forward = b - a;
            rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
        }

        private static ManifestSummary DescribeManifest(KbduTrafficRuntime runtime, Manifest manifest)
        {
            TextAsset asset = runtime.TrafficManifest;
            string path = UnityEditor.AssetDatabase.GetAssetPath(asset);
            string diskPath = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
            return new ManifestSummary
            {
                assetPath = path,
                sha256 = Sha256(diskPath),
                bytes = new FileInfo(diskPath).Length,
                schema = manifest.schema,
                status = manifest.status,
                parkedCount = manifest.parked.Length,
                movingCount = manifest.moving.Length,
                routeCount = manifest.routes.Length,
                nearEnterMeters = KbduTrafficRuntime.NearEnterDistanceMeters,
                nearExitMeters = KbduTrafficRuntime.NearExitDistanceMeters,
                farEnterMeters = KbduTrafficRuntime.FarEnterDistanceMeters,
                farExitMeters = KbduTrafficRuntime.FarExitDistanceMeters,
                maximumNearInstances = KbduTrafficRuntime.MaximumNearInstances,
                maximumFarInstances = KbduTrafficRuntime.MaximumFarInstances,
                v1ProceduralMaximumDraws = 9,
                v1ProceduralMaximumTriangles = 129312,
                v1ProceduralMaterialCount = 1,
                runtimeTriangleCeiling = KbduTrafficRuntime.MaximumVisibleTriangles
            };
        }

        private static void ValidateManifest(Manifest manifest)
        {
            if (manifest == null || manifest.parked == null || manifest.moving == null || manifest.routes == null)
                throw new InvalidOperationException("Accepted Traffic V1 manifest could not be parsed.");
            if (manifest.routes.Any(route => route == null || route.samples == null || route.samples.Length < 2))
                throw new InvalidOperationException("Accepted Traffic V1 route sample data is incomplete.");
        }

        private static string BuildContactSheet(List<ViewResult> views, string outputDirectory)
        {
            const int columns = 5;
            const int rows = 3;
            const int cellWidth = 320;
            const int cellHeight = 180;
            Texture2D sheet = new Texture2D(columns * cellWidth, rows * cellHeight, TextureFormat.RGB24, false, false);
            Color32[] background = Enumerable.Repeat(new Color32(20, 20, 20, 255), sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(background);
            for (int index = 0; index < views.Count; index++)
            {
                ViewResult view = views[index];
                string path = Path.Combine(outputDirectory, view.pngPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) continue;
                Texture2D source = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
                source.LoadImage(File.ReadAllBytes(path), false);
                Color[] pixels = new Color[cellWidth * cellHeight];
                for (int y = 0; y < cellHeight; y++)
                for (int x = 0; x < cellWidth; x++)
                    pixels[y * cellWidth + x] = source.GetPixelBilinear((x + 0.5f) / cellWidth, (y + 0.5f) / cellHeight);
                int column = index % columns;
                int rowFromTop = index / columns;
                int destinationY = (rows - 1 - rowFromTop) * cellHeight;
                sheet.SetPixels(column * cellWidth, destinationY, cellWidth, cellHeight, pixels);
                Object.DestroyImmediate(source);
            }
            sheet.Apply(false, false);
            string pathOut = Path.Combine(outputDirectory, "traffic_v2_baseline_contact_sheet.png");
            File.WriteAllBytes(pathOut, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            return Path.GetFileName(pathOut);
        }

        private static string WriteFleetByCameraCsv(
            List<ViewResult> views,
            string outputDirectory)
        {
            StringBuilder csv = new StringBuilder();
            csv.Append("view_id,pose_authority,render_width,render_height,resolvable_pixel_threshold");
            string[] names = { "sedan_a0", "hatchback_a1", "crossover_a2", "pickup_a3", "cargo_van_a4", "semi_a5" };
            foreach (string name in names) csv.Append(",parked_").Append(name);
            csv.Append(",parked_total");
            foreach (string name in names) csv.Append(",moving_").Append(name);
            csv.AppendLine(",moving_total");
            foreach (ViewResult view in views)
            {
                if (view.independentParkedByArchetype == null ||
                    view.independentParkedByArchetype.Length != KbduTrafficRuntime.ArchetypeCount ||
                    view.independentMovingByArchetype == null ||
                    view.independentMovingByArchetype.Length != KbduTrafficRuntime.ArchetypeCount)
                    throw new InvalidOperationException("Independent fleet inventory is incomplete for " + view.id + ".");
                csv.Append(view.id).Append(',')
                    .Append(view.poseAuthority).Append(',')
                    .Append(Width).Append(',')
                    .Append(Height).Append(',')
                    .Append(ResolvablePixels);
                foreach (int count in view.independentParkedByArchetype) csv.Append(',').Append(count);
                csv.Append(',').Append(view.independentParkedResolvable);
                foreach (int count in view.independentMovingByArchetype) csv.Append(',').Append(count);
                csv.Append(',').Append(view.independentMovingResolvable).AppendLine();
            }
            string path = Path.Combine(outputDirectory, "traffic_v2_baseline_fleet_by_camera.csv");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            return Path.GetFileName(path);
        }

        private static string BuildReview(BaselineReport report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Traffic System V2 accepted-baseline review");
            text.AppendLine();
            text.AppendLine("Status: **" + report.status + "**. This is comparison evidence for accepted Traffic V1, not Traffic V2 acceptance, Quest performance evidence, or a headset result.");
            text.AppendLine();
            text.AppendLine("- Accepted source SHA: `" + report.acceptedSourceSha + "`");
            text.AppendLine("- Frozen spec SHA-256: `" + report.frozenSpecSha256 + "`");
            if (report.evidenceGenerator != null)
                text.AppendLine("- Evidence generator: `" + report.evidenceGenerator.sourceAssetPath + "` / `" +
                                report.evidenceGenerator.sourceSha256 + "`");
            text.AppendLine("- Scene file unchanged: `" + report.sceneFileUnchanged + "`");
            text.AppendLine("- Captures: `" + report.capturedViewCount + "/" + ExpectedViewCount + "`");
            text.AppendLine("- Fleet-by-camera CSV: `" + report.fleetByCameraCsvPath + "`");
            if (report.manifest != null)
            {
                text.AppendLine("- Manifest: `" + report.manifest.sha256 + "`, " + report.manifest.bytes + " bytes; " +
                                report.manifest.parkedCount + " parked, " + report.manifest.movingCount + " moving, " + report.manifest.routeCount + " routes");
                text.AppendLine("- Accepted V1 procedural maximum: 9 draws, 129,312 triangles, 1 material");
                text.AppendLine("- Accepted V1 LOD: near " + report.manifest.nearEnterMeters + "/" + report.manifest.nearExitMeters +
                                " m; far " + report.manifest.farEnterMeters + "/" + report.manifest.farExitMeters + " m (enter/exit)");
            }
            text.AppendLine();
            if (report.editorPlanningMetrics != null)
            {
                EditorPlanningMetrics metrics = report.editorPlanningMetrics;
                text.AppendLine("## Editor-only CPU and memory planning evidence");
                text.AppendLine();
                text.AppendLine("**" + metrics.qualification + "**");
                text.AppendLine();
                text.AppendLine("- Simulation step (" + metrics.simulationStepSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                                " s): " + TimingSummary(metrics.simulationStep));
                text.AppendLine("- Visibility preparation across " + metrics.preparationPoseCount + " frozen poses: " +
                                TimingSummary(metrics.visibilityPreparation));
                text.AppendLine("- Maximum observed far proxy batch: " + metrics.maxFarBatchCountObserved +
                                " / " + metrics.graphicsDrawMeshInstancedLimit + "; limit exceeded: `" + metrics.farBatchLimitExceeded + "`");
                text.AppendLine("- Stopwatch frequency: " + metrics.stopwatchFrequency + " ticks/second; worst preparation p95 view: `" +
                                metrics.worstVisibilityPreparationViewId + "`");
                if (metrics.editorMachine != null)
                    text.AppendLine("- Editor host: " + metrics.editorMachine.processorType + ", " +
                                    metrics.editorMachine.logicalProcessorCount + " logical processors, " +
                                    metrics.editorMachine.systemMemoryMiB + " MiB RAM, GPU `" +
                                    metrics.editorMachine.graphicsDeviceName + "`, OS `" +
                                    metrics.editorMachine.operatingSystem + "`");
                if (metrics.runtimeMemory != null)
                {
                    text.AppendLine("- Unique Editor runtime object bytes: " + metrics.runtimeMemory.aggregateUniqueRuntimeBytes);
                    text.AppendLine("- Unique imported asset disk bytes: " + metrics.runtimeMemory.aggregateUniqueDiskBytes);
                }
                text.AppendLine();
            }
            if (report.renderBudgetSummary != null)
            {
                RenderBudgetSummary budget = report.renderBudgetSummary;
                text.AppendLine("## Static plus accepted-V1 procedural render accounting");
                text.AppendLine();
                text.AppendLine("- Budgeted views: " + budget.viewCountWithBudget + "/" + budget.viewCountRequired);
                text.AppendLine("- Max static audit: " + budget.maximumStaticDrawCalls + " draws, " +
                                budget.maximumStaticTriangles + " triangles, " + budget.maximumStaticMaterials + " materials");
                text.AppendLine("- Max observed aggregate static+selected traffic: " + budget.maximumObservedAggregateDrawCalls +
                                " draws, " + budget.maximumObservedAggregateTriangles + " triangles, " +
                                budget.maximumObservedAggregateMaterials + " materials");
                text.AppendLine("- Conservative authored aggregate ceiling: " + budget.conservativeAuthoredAggregateDrawCeiling +
                                " draws, " + budget.conservativeAuthoredAggregateTriangleCeiling + " triangles, " +
                                budget.conservativeAuthoredAggregateMaterialCeiling + " materials");
                text.AppendLine("- Max accepted-V1 far batch: " + budget.maximumFarBatchCount +
                                "; any batch over 1,023: `" + budget.anyAcceptedV1BatchOver1023 + "`");
                text.AppendLine();
            }
            text.AppendLine("## Frozen view inventory");
            text.AppendLine();
            text.AppendLine("| View | Capture | V1 selected near/far | Independently resolvable parked/moving |");
            text.AppendLine("|---|---:|---:|---:|");
            foreach (ViewResult view in report.views)
            {
                int near = view.selection == null ? 0 : view.selection.nearCount;
                int far = view.selection == null ? 0 : view.selection.farCount;
                text.AppendLine("| `" + view.id + "` | " + (string.IsNullOrEmpty(view.captureError) ? "yes" : "no") + " | " + near + "/" + far +
                                " | " + view.independentParkedResolvable + "/" + view.independentMovingResolvable + " |");
            }
            text.AppendLine();
            text.AppendLine("## Recorded accepted-baseline defects");
            text.AppendLine();
            foreach (string defect in report.recordedBaselineDefects) text.AppendLine("- " + defect);
            text.AppendLine();
            text.AppendLine("## Evidence limitations");
            text.AppendLine();
            foreach (string limitation in report.evidenceLimitations) text.AppendLine("- " + limitation);
            if (report.errors.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("## Capture errors");
                text.AppendLine();
                foreach (string error in report.errors) text.AppendLine("- `" + error.Replace("\r", " ").Replace("\n", " ") + "`");
            }
            return text.ToString();
        }

        private static string TimingSummary(TimingDistribution timing)
        {
            if (timing == null) return "not captured";
            return "warmup=" + timing.warmupIterations + ", measured=" + timing.measuredIterations +
                   ", mean=" + timing.meanMicroseconds.ToString("0.###", CultureInfo.InvariantCulture) + " us" +
                   ", p50=" + timing.p50Microseconds.ToString("0.###", CultureInfo.InvariantCulture) + " us" +
                   ", p95=" + timing.p95Microseconds.ToString("0.###", CultureInfo.InvariantCulture) + " us" +
                   ", max=" + timing.maxMicroseconds.ToString("0.###", CultureInfo.InvariantCulture) + " us" +
                   ", Editor Mono current-thread allocated=" + timing.editorMonoThreadAllocatedBytes + " bytes (" +
                   timing.editorMonoThreadAllocatedBytesPerIteration.ToString("0.###", CultureInfo.InvariantCulture) + " bytes/iteration)";
        }

        private static void WriteHashManifest(string outputDirectory, string jsonPath, string reviewPath, BaselineReport report)
        {
            List<string> files = new List<string> { jsonPath, reviewPath };
            string contact = Path.Combine(outputDirectory, report.contactSheetPath ?? string.Empty);
            if (File.Exists(contact)) files.Add(contact);
            files.AddRange(report.views.Select(view => Path.Combine(outputDirectory, view.pngPath.Replace('/', Path.DirectorySeparatorChar))).Where(File.Exists));
            string fleetCsv = Path.Combine(outputDirectory, report.fleetByCameraCsvPath ?? string.Empty);
            if (File.Exists(fleetCsv)) files.Add(fleetCsv);
            HashManifest manifest = new HashManifest
            {
                schema = "questflight.evidence-file-hashes.v1",
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                note = "This manifest intentionally excludes its own file.",
                files = files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).Select(path => new HashedFile
                {
                    path = path.Substring(outputDirectory.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                    bytes = new FileInfo(path).Length,
                    sha256 = Sha256(path)
                }).ToList()
            };
            File.WriteAllText(Path.Combine(outputDirectory, "traffic_v2_baseline_hashes.json"), JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
        }

        private static List<string> EvidenceLimitations()
        {
            return new List<string>
            {
                "Editor diagnostic-camera captures are monoscopic machine evidence; they are not inspected stereo Meta XR Simulator captures.",
                "This runner does not enter Play Mode and does not measure runtime CPU time, managed allocations, frame timing, thermals, or sustained 72 Hz behavior.",
                "No APK is built or installed, and no physical Quest 3 headset acceptance is inferred.",
                "The moving projection uses each accepted V1 vehicle's manifest initial distance, not a timed Play Mode simulation state.",
                "The 2x2-pixel inventory is a geometric candidate count; it does not prove visible pixels after depth occlusion, transparency, fog, or final stereo projection.",
                "Out-of-coverage Traffic V1 views truthfully remain sparse or empty; absence is baseline evidence, not a Traffic V2 pass.",
                "Invoking private V1 preparation through reflection records current implementation behavior but is not a public product API contract."
                ,"Editor Mono timing, allocation, and Profiler runtime-memory measurements are planning evidence only and may not be presented as standalone Quest 3 performance."
            };
        }

        private static List<string> RecordedDefects()
        {
            return new List<string>
            {
                "KbduTrafficRuntime stores up to 1,800 far instances per proxy batch and submits each batch with one Graphics.DrawMeshInstanced call; Unity's 1,023-instance call limit is not chunked or fail-closed.",
                "Accepted V1 traffic is not included in the existing static ProductionEnvironmentRoot, QuestPerformanceBudget, or QuestRenderBudget audits, so their published scene totals understate aggregate traffic cost.",
                "Traffic V1 membership and LOD are selected from one camera transform and therefore do not constitute max-of-both-eyes stereo evidence.",
                "Traffic V1 covers an airport-centered subset rather than the frozen full 24 km by 24 km Traffic V2 evaluation square.",
                "This accepted baseline provides looping per-route motion, not the required directed, non-looping, 900-second Traffic V2 trajectory schedule."
            };
        }

        private static string HierarchyPath(Transform transform)
        {
            List<string> parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                transform = transform.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static EvidenceGenerator DescribeEvidenceGenerator()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string diskPath = Path.GetFullPath(Path.Combine(projectRoot, RunnerSourcePath));
            if (!File.Exists(diskPath))
                throw new FileNotFoundException("Baseline evidence-generator source is missing.", diskPath);
            return new EvidenceGenerator
            {
                method = "QuestFlightLab.Editor.Tests.TrafficV2BaselineBatchRunner.RunBatch",
                sourceAssetPath = RunnerSourcePath,
                sourceSha256 = Sha256(diskPath),
                sourceBytes = new FileInfo(diskPath).Length
            };
        }

        private sealed class ReflectionAccess
        {
            private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            private readonly KbduTrafficRuntime runtime;
            private readonly MethodInfo parse;
            private readonly Action initializeSimulation;
            private readonly Action<float> stepSimulation;
            private readonly Action<Camera> prepareVisibleInstances;
            private readonly FieldInfo selectedCount;
            private readonly FieldInfo selectedFarCount;
            private readonly FieldInfo selectedIds;
            private readonly FieldInfo selectedFarIds;
            private readonly FieldInfo selectedArchetypes;
            private readonly FieldInfo selectedFarArchetypes;
            private readonly FieldInfo selectedMatrices;
            private readonly FieldInfo selectedFarMatrices;
            private readonly FieldInfo nearCounts;
            private readonly FieldInfo farCounts;

            public ReflectionAccess(KbduTrafficRuntime runtime)
            {
                this.runtime = runtime;
                Type type = typeof(KbduTrafficRuntime);
                parse = RequiredMethod(type, "ParseManifest");
                MethodInfo initialize = RequiredMethod(type, "InitializeSimulation");
                MethodInfo step = RequiredMethod(type, "StepSimulation", typeof(float));
                MethodInfo prepare = RequiredMethod(type, "PrepareVisibleInstances", typeof(Camera));
                initializeSimulation = (Action)Delegate.CreateDelegate(typeof(Action), runtime, initialize, true);
                stepSimulation = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), runtime, step, true);
                prepareVisibleInstances = (Action<Camera>)Delegate.CreateDelegate(typeof(Action<Camera>), runtime, prepare, true);
                selectedCount = RequiredField(type, "selectedCount");
                selectedFarCount = RequiredField(type, "selectedFarCount");
                selectedIds = RequiredField(type, "selectedVehicleIds");
                selectedFarIds = RequiredField(type, "selectedFarVehicleIds");
                selectedArchetypes = RequiredField(type, "selectedArchetypes");
                selectedFarArchetypes = RequiredField(type, "selectedFarArchetypes");
                selectedMatrices = RequiredField(type, "selectedMatrices");
                selectedFarMatrices = RequiredField(type, "selectedFarMatrices");
                nearCounts = RequiredField(type, "nearCounts");
                farCounts = RequiredField(type, "farCounts");
            }

            public void ParseManifest() { parse.Invoke(runtime, null); }
            public void InitializeSimulation() { initializeSimulation(); }
            public void StepSimulation(float deltaTime) { stepSimulation(deltaTime); }
            public void PrepareVisibleInstances(Camera camera) { prepareVisibleInstances(camera); }
            public int[] ReadFarBatchCountsReference() { return (int[])farCounts.GetValue(runtime); }

            public SelectionSnapshot PrepareAndRead(Camera camera, Manifest manifest)
            {
                prepareVisibleInstances(camera);
                int near = (int)selectedCount.GetValue(runtime);
                int far = (int)selectedFarCount.GetValue(runtime);
                int[] nearIds = (int[])selectedIds.GetValue(runtime);
                int[] farIds = (int[])selectedFarIds.GetValue(runtime);
                int[] nearTypes = (int[])selectedArchetypes.GetValue(runtime);
                int[] farTypes = (int[])selectedFarArchetypes.GetValue(runtime);
                Matrix4x4[] nearMatrix = (Matrix4x4[])selectedMatrices.GetValue(runtime);
                Matrix4x4[] farMatrix = (Matrix4x4[])selectedFarMatrices.GetValue(runtime);
                SelectionSnapshot snapshot = new SelectionSnapshot
                {
                    nearCount = near,
                    farCount = far,
                    nearBatchCounts = ((int[])nearCounts.GetValue(runtime)).ToArray(),
                    farBatchCounts = ((int[])farCounts.GetValue(runtime)).ToArray()
                };
                for (int index = 0; index < near; index++) snapshot.instances.Add(Selected(nearIds[index], nearTypes[index], "near", nearMatrix[index], manifest));
                for (int index = 0; index < far; index++) snapshot.instances.Add(Selected(farIds[index], farTypes[index], "far", farMatrix[index], manifest));
                return snapshot;
            }

            private static SelectedInstance Selected(int numericId, int archetype, string tier, Matrix4x4 matrix, Manifest manifest)
            {
                bool parked = numericId >= 0 && numericId < manifest.parked.Length;
                int movingIndex = numericId - manifest.parked.Length;
                string stableId = parked
                    ? manifest.parked[numericId].id
                    : movingIndex >= 0 && movingIndex < manifest.moving.Length ? manifest.moving[movingIndex].id : "INVALID";
                return new SelectedInstance
                {
                    numericId = numericId,
                    stableId = stableId,
                    category = parked ? "parked" : "moving",
                    archetype = archetype,
                    lodTier = tier,
                    position = matrix.GetColumn(3),
                    matrix = MatrixValues(matrix)
                };
            }

            private static float[] MatrixValues(Matrix4x4 matrix)
            {
                float[] values = new float[16];
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    values[row * 4 + column] = matrix[row, column];
                return values;
            }

            private static MethodInfo RequiredMethod(Type type, string name, params Type[] arguments)
            {
                MethodInfo method = type.GetMethod(name, Flags, null, arguments, null);
                if (method == null) throw new MissingMethodException(type.FullName, name);
                return method;
            }

            private static FieldInfo RequiredField(Type type, string name)
            {
                FieldInfo field = type.GetField(name, Flags);
                if (field == null) throw new MissingFieldException(type.FullName, name);
                return field;
            }
        }

        private sealed class FrozenView
        {
            public string id;
            public bool cockpitRelative;
            public Vector3 position;
            public Vector3 euler;
            public float fov;
            public static FrozenView Absolute(string id, Vector3 position, Vector3 euler, float fov) => new FrozenView { id = id, position = position, euler = euler, fov = fov };
            public static FrozenView Cockpit(string id, Vector3 offset, Vector3 euler, float fov) => new FrozenView { id = id, cockpitRelative = true, position = offset, euler = euler, fov = fov };
        }

        [Serializable] private sealed class Manifest { public string schema; public string status; public ParkedRecord[] parked; public RouteRecord[] routes; public MovingRecord[] moving; }
        [Serializable] private sealed class ParkedRecord { public string id; public int archetype; public float x; public float y; public float z; public float yaw_degrees; }
        [Serializable] private sealed class MovingRecord { public string id; public int route_index; public int archetype; public float initial_distance_meters; }
        [Serializable] private sealed class RouteRecord { public float length_meters; public RouteSample[] samples; }
        [Serializable] private sealed class RouteSample { public float x; public float y; public float z; public float distance; }

        [Serializable]
        private sealed class BaselineReport
        {
            public string schema, status, generatedUtc, acceptedSourceSha, frozenSpecSha256, unityVersion, scenePath;
            public int renderWidth, renderHeight, resolvablePixelThreshold, capturedViewCount, reflectionSucceededViewCount;
            public string mainCameraHierarchyPath, trafficRuntimeHierarchyPath, sceneSha256Before, sceneSha256After, contactSheetPath;
            public string fleetByCameraCsvPath;
            public bool mainCameraWasNeverWritten, productSceneWasNeverSaved, sceneFileUnchanged;
            public EvidenceGenerator evidenceGenerator;
            public ManifestSummary manifest;
            public EditorPlanningMetrics editorPlanningMetrics;
            public RenderBudgetSummary renderBudgetSummary;
            public List<ViewResult> views = new List<ViewResult>();
            public List<string> evidenceLimitations = new List<string>();
            public List<string> recordedBaselineDefects = new List<string>();
            public List<string> errors = new List<string>();
        }

        [Serializable]
        private sealed class ManifestSummary
        {
            public string assetPath, sha256, schema, status;
            public long bytes;
            public int parkedCount, movingCount, routeCount, maximumNearInstances, maximumFarInstances;
            public int v1ProceduralMaximumDraws, v1ProceduralMaximumTriangles, v1ProceduralMaterialCount, runtimeTriangleCeiling;
            public float nearEnterMeters, nearExitMeters, farEnterMeters, farExitMeters;
        }

        [Serializable]
        private sealed class EditorPlanningMetrics
        {
            public string evidenceClass, qualification, allocationCounter;
            public string worstVisibilityPreparationViewId, worstVisibilityPreparationCriterion;
            public bool questPerformanceEvidence, farBatchLimitExceeded;
            public int managedThreadId, preparationPoseCount, maxFarBatchCountObserved, graphicsDrawMeshInstancedLimit;
            public long stopwatchFrequency;
            public float simulationStepSeconds;
            public TimingDistribution simulationStep, visibilityPreparation;
            public List<VisibilityPoseTiming> visibilityPreparationByView = new List<VisibilityPoseTiming>();
            public EditorMachineIdentity editorMachine;
            public RuntimeMemorySnapshot runtimeMemory;
        }

        [Serializable]
        private sealed class TimingDistribution
        {
            public int warmupIterations, measuredIterations;
            public double meanMicroseconds, p50Microseconds, p95Microseconds, maxMicroseconds;
            public long editorMonoThreadAllocatedBytes;
            public long stopwatchFrequency;
            public double editorMonoThreadAllocatedBytesPerIteration;
            public string allocationScope, timerApi, rawTickEncoding, rawTickSha256;
        }

        [Serializable]
        private sealed class VisibilityPoseTiming
        {
            public string viewId;
            public TimingDistribution timing;
        }

        [Serializable]
        private sealed class EditorMachineIdentity
        {
            public string qualification, operatingSystem, operatingSystemFamily, deviceModel, processorType;
            public string graphicsDeviceName, graphicsDeviceVendor, graphicsDeviceType, graphicsDeviceVersion;
            public int logicalProcessorCount, processorFrequencyMHz, systemMemoryMiB, graphicsMemoryMiB, processBitness;
            public bool graphicsMultiThreaded, batchMode;
        }

        [Serializable]
        private sealed class EvidenceGenerator
        {
            public string method, sourceAssetPath, sourceSha256;
            public long sourceBytes;
        }

        [Serializable]
        private sealed class RuntimeMemorySnapshot
        {
            public string measurementApi, diskByteSemantics, runtimeByteSemantics;
            public int uniqueRuntimeObjectCount, uniqueDiskAssetCount;
            public long aggregateUniqueRuntimeBytes, aggregateUniqueDiskBytes;
            public List<RuntimeMemoryRecord> objects = new List<RuntimeMemoryRecord>();
        }

        [Serializable]
        private sealed class RuntimeMemoryRecord
        {
            public string label, unityType, assetPath;
            public int instanceId;
            public long runtimeBytes, diskBytes;
        }

        [Serializable]
        private sealed class RenderBudgetSummary
        {
            public string conservativeCeilingSemantics;
            public int viewCountWithBudget, viewCountRequired;
            public int maximumStaticDrawCalls, maximumStaticMaterials;
            public long maximumStaticTriangles;
            public int maximumSelectedTrafficDrawCalls, maximumSelectedTrafficMaterials;
            public long maximumSelectedTrafficTriangles;
            public int maximumObservedAggregateDrawCalls, maximumObservedAggregateMaterials;
            public long maximumObservedAggregateTriangles;
            public int conservativeAuthoredAggregateDrawCeiling, conservativeAuthoredAggregateMaterialCeiling;
            public long conservativeAuthoredAggregateTriangleCeiling;
            public int maximumFarBatchCount;
            public bool anyAcceptedV1BatchOver1023;
        }

        [Serializable]
        private sealed class ViewRenderBudget
        {
            public QuestFlightLab.Runtime.RenderBudgetSnapshot staticAudit;
            public TrafficProceduralBudget traffic;
            public int aggregateEstimatedDrawCalls, aggregateUniqueMaterialCount, aggregateTransparentMaterialSlots;
            public long aggregateEstimatedTriangles;
            public string aggregateSemantics;
        }

        [Serializable]
        private sealed class TrafficProceduralBudget
        {
            public string accountingSemantics;
            public int[] nearBatchCounts, farBatchCounts, nearTrianglesPerInstance, farTrianglesPerInstance;
            public long[] nearBatchTriangles, farBatchTriangles;
            public int selectedDrawCalls, safeChunkedDrawCalls, selectedMaterialCount, maximumBatchCount;
            public long selectedTriangles;
            public bool anyBatchOver1023;
        }

        [Serializable]
        private sealed class ViewResult
        {
            public string id, poseAuthority, pngPath, pngSha256, captureError, reflectionError, projectionError, renderBudgetError;
            public Vector3 frozenPositionOrOffset, frozenEuler, resolvedPosition, resolvedEuler;
            public float fieldOfView;
            public long pngBytes;
            public int independentParkedResolvable, independentMovingResolvable;
            public int[] independentParkedByArchetype, independentMovingByArchetype;
            public SelectionSnapshot selection;
            public ViewRenderBudget renderBudget;
        }

        [Serializable]
        private sealed class SelectionSnapshot
        {
            public int nearCount, farCount;
            public int[] nearBatchCounts, farBatchCounts;
            public List<SelectedInstance> instances = new List<SelectedInstance>();
        }

        [Serializable]
        private sealed class SelectedInstance
        {
            public int numericId, archetype;
            public string stableId, category, lodTier;
            public Vector3 position;
            public float[] matrix;
        }

        [Serializable] private sealed class HashManifest { public string schema, generatedUtc, note; public List<HashedFile> files = new List<HashedFile>(); }
        [Serializable] private sealed class HashedFile { public string path, sha256; public long bytes; }
    }
}
