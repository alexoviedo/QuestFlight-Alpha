using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight;
using QuestFlightLab.Runtime;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Renders the authored ProductionVerticalSlice scene without invoking any
    /// legacy runtime world/cockpit construction. Flight-phase images are
    /// deterministic presentation poses and are not physics evidence.
    /// </summary>
    public static class ProductionVisualQaBatchRunner
    {
        private const int DefaultWidth = 1280;
        private const int DefaultHeight = 720;
        private const int RequiredShotCount = 56;
        private const int DrawCallTarget = 120;
        private const long VisibleTriangleTarget = 600000L;
        private const int SceneMaterialTarget = 25;
        private const string ProductionScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private static readonly Vector3 RepresentativeCalibrationOffset = new Vector3(0.015f, 0.020f, -0.025f);

        [MenuItem("Quest Flight Lab/Production Vertical Slice V2/Run Production Visual QA")]
        public static void RunProductionVisualQa()
        {
            string outputDir = System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR");
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.GetFullPath(Path.Combine(
                    "..",
                    "T2-QuestFlight-product-extraction-artifacts",
                    $"product_visual_qa_{DateTime.UtcNow:yyyyMMdd_HHmmss}"));
            }

            int width = ReadInt("QFL_PRODUCTION_VISUAL_QA_WIDTH", DefaultWidth);
            int height = ReadInt("QFL_PRODUCTION_VISUAL_QA_HEIGHT", DefaultHeight);
            Directory.CreateDirectory(outputDir);
            Directory.CreateDirectory(Path.Combine(outputDir, "screenshots"));

            ProductionVisualQaReport report = new ProductionVisualQaReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scenePath = ProductionScenePath,
                outputDirectory = outputDir,
                renderWidth = width,
                renderHeight = height,
                humanHeadsetAcceptance = "NOT_RUN_NO_USER_AVAILABLE",
                flightPhaseImageSemantics = "Deterministic authored presentation poses; physics credibility is evaluated by the independent scenario gate."
            };

            try
            {
                VisualContext context = LoadContext(width, height, report);
                RenderRequiredShots(context, report, outputDir, width, height);
                report.mountainSweep = RunMountainSweep(context);
                report.contactSheetPath = BuildContactSheet(report, outputDir);
                report.airportCoreBeforeContactSheetPath = BuildContactSheet(
                    report.shots.Where(shot => shot.id.StartsWith("v4_before_", StringComparison.Ordinal)).ToList(),
                    outputDir,
                    "airport_core_before_contact_sheet.png");
                report.airportCoreAfterContactSheetPath = BuildContactSheet(
                    report.shots.Where(shot => shot.id.StartsWith("v4_after_", StringComparison.Ordinal)).ToList(),
                    outputDir,
                    "airport_core_after_contact_sheet.png");
                FinalizeReport(context, report);
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
                report.passed = false;
                report.classification = "FAIL_MACHINE_QA";
            }

            WriteReportFiles(report, outputDir);
            Debug.Log(
                $"[QuestFlightLab][ProductionVisualQA] shots={report.shots.Count}/{RequiredShotCount} " +
                $"classification={report.classification} output={outputDir}");
            if (!report.passed)
                Debug.LogWarning("[QuestFlightLab][ProductionVisualQA] Machine QA did not pass; see visual_qa_summary.md.");
        }

        [MenuItem("Quest Flight Lab/Production Vertical Slice V2/Trace Visual Artifacts")]
        public static void TraceProductionVisualArtifacts()
        {
            string outputDir = System.Environment.GetEnvironmentVariable("QFL_PRODUCTION_ARTIFACT_TRACE_DIR");
            if (string.IsNullOrWhiteSpace(outputDir))
                outputDir = Path.GetFullPath(Path.Combine("..", "T2-QuestFlight-product-extraction-artifacts", $"product_artifact_trace_{DateTime.UtcNow:yyyyMMdd_HHmmss}"));
            Directory.CreateDirectory(outputDir);

            ProductionVisualQaReport setupReport = new ProductionVisualQaReport();
            VisualContext context = LoadContext(320, 180, setupReport);
            ProductionArtifactTraceReport trace = new ProductionArtifactTraceReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scenePath = ProductionScenePath,
                waterSurfacePath = HierarchyPath(context.environment.boulderReservoirSurface.transform),
                waterBankPath = HierarchyPath(context.environment.boulderReservoirShoreBank.transform)
            };

            Transform aircraft = context.rig.transform;
            Vector3 pilotEye = context.rig.PilotSeatAnchor.position;
            trace.traces.Add(TracePose(
                context, outputDir, "cockpit_prop_obstruction", pilotEye, aircraft.rotation, 72f,
                ViewProfile.Cockpit, context.rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true),
                new Rect(0.43f, 0.28f, 0.14f, 0.70f)));

            Bounds hangarBounds = CombinedNamedRendererBounds(
                context.environment.airportContext, AirportCoreBenchmarkRoot.GameObjectName);
            Vector3 hangarTarget = hangarBounds.center;
            float airportExtent = Mathf.Max(hangarBounds.extents.x, hangarBounds.extents.z);
            Vector3 hangarCamera = hangarTarget
                                   - context.runwayForward * (airportExtent * 0.85f + 70f)
                                   + context.runwayRight * (airportExtent * 0.65f + 60f)
                                   + Vector3.up * (airportExtent * 0.38f + 60f);
            trace.traces.Add(TracePose(
                context, outputDir, "apron_vertical_slab", hangarCamera, LookAt(hangarCamera, hangarTarget), 52f,
                ViewProfile.External, context.environment.GetComponentsInChildren<Renderer>(true),
                new Rect(0.25f, 0.0f, 0.50f, 0.90f)));

            Vector3 aircraftTarget = aircraft.position + Vector3.up * 1.1f;
            Vector3 externalCamera = aircraftTarget - aircraft.forward * 12f + aircraft.right * 8f + Vector3.up * 4.5f;
            Renderer[] beigeCandidates = context.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            trace.traces.Add(TracePose(
                context, outputDir, "external_beige_polygon", externalCamera, LookAt(externalCamera, aircraftTarget), 52f,
                ViewProfile.External, beigeCandidates, new Rect(0f, 0f, 1f, 0.55f)));

            trace.largeOrTallRenderers = beigeCandidates
                .Where(renderer => renderer != null)
                .Select(renderer => new RendererInventoryEntry(renderer))
                .Where(entry => entry.boundsSize.y > 20f || entry.boundsSize.magnitude > 4000f)
                .OrderByDescending(entry => entry.boundsSize.y)
                .ThenByDescending(entry => entry.boundsSize.magnitude)
                .ToList();

            string path = Path.Combine(outputDir, "production_visual_artifact_trace.json");
            File.WriteAllText(path, JsonUtility.ToJson(trace, true));
            File.WriteAllText(Path.Combine(outputDir, "production_visual_artifact_trace.md"), BuildArtifactTraceMarkdown(trace));
            Debug.Log($"[QuestFlightLab][ProductionVisualQA] Artifact trace written: {path}");
        }

        private static ArtifactPoseTrace TracePose(
            VisualContext context,
            string outputDir,
            string id,
            Vector3 cameraPosition,
            Quaternion cameraRotation,
            float fov,
            ViewProfile profile,
            IEnumerable<Renderer> candidateRenderers,
            Rect normalizedRoi)
        {
            if (profile == ViewProfile.Cockpit) context.visibility?.SetCockpitView();
            else context.visibility?.SetExternalView();
            context.camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            context.camera.fieldOfView = fov;
            Texture2D baselineTexture = Render(context.camera, 320, 180, out _);
            Color32[] baseline = baselineTexture.GetPixels32();
            File.WriteAllBytes(Path.Combine(outputDir, id + "_reference.png"), baselineTexture.EncodeToPNG());
            Object.DestroyImmediate(baselineTexture);

            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(context.camera);
            List<RendererContribution> contributions = new List<RendererContribution>();
            foreach (Renderer renderer in candidateRenderers
                         .Where(candidate => candidate != null && candidate.enabled && candidate.gameObject.activeInHierarchy)
                         .Where(candidate => GeometryUtility.TestPlanesAABB(frustum, candidate.bounds))
                         .Distinct())
            {
                renderer.enabled = false;
                Texture2D without = Render(context.camera, 320, 180, out _);
                renderer.enabled = true;
                float difference = MeanAbsoluteDifference(baseline, without.GetPixels32(), 320, 180, normalizedRoi);
                Object.DestroyImmediate(without);
                contributions.Add(new RendererContribution(renderer, difference));
            }

            return new ArtifactPoseTrace
            {
                id = id,
                cameraPosition = cameraPosition,
                cameraEuler = cameraRotation.eulerAngles,
                normalizedRoi = normalizedRoi,
                candidateCount = contributions.Count,
                topContributors = contributions
                    .OrderByDescending(item => item.meanAbsoluteRgbDifference)
                    .Take(15)
                    .ToList()
            };
        }

        private static float MeanAbsoluteDifference(
            Color32[] baseline,
            Color32[] candidate,
            int width,
            int height,
            Rect roi)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(roi.xMin * width), 0, width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(roi.xMax * width), minX + 1, width);
            int minY = Mathf.Clamp(Mathf.FloorToInt(roi.yMin * height), 0, height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(roi.yMax * height), minY + 1, height);
            double total = 0d;
            int count = 0;
            for (int y = minY; y < maxY; y++)
            for (int x = minX; x < maxX; x++)
            {
                int index = y * width + x;
                total += Math.Abs(baseline[index].r - candidate[index].r);
                total += Math.Abs(baseline[index].g - candidate[index].g);
                total += Math.Abs(baseline[index].b - candidate[index].b);
                count += 3;
            }
            return count > 0 ? (float)(total / count) : 0f;
        }

        private static string BuildArtifactTraceMarkdown(ProductionArtifactTraceReport trace)
        {
            StringBuilder builder = new StringBuilder("# Production Visual Artifact Renderer Trace\n\n");
            builder.AppendLine($"- Water surface: `{trace.waterSurfacePath}`");
            builder.AppendLine($"- Water shore bank: `{trace.waterBankPath}`");
            foreach (ArtifactPoseTrace pose in trace.traces)
            {
                builder.AppendLine();
                builder.AppendLine("## " + pose.id);
                builder.AppendLine();
                builder.AppendLine("| Renderer | Material | Bounds | ROI RGB delta |");
                builder.AppendLine("| --- | --- | --- | ---: |");
                foreach (RendererContribution item in pose.topContributors)
                    builder.AppendLine($"| `{item.hierarchyPath}` | `{item.materialName}` | `{item.boundsSize}` | {item.meanAbsoluteRgbDifference:0.000} |");
            }
            return builder.ToString();
        }

        private static VisualContext LoadContext(int width, int height, ProductionVisualQaReport report)
        {
            if (!File.Exists(ProductionScenePath))
                throw new FileNotFoundException("Production scene is missing.", ProductionScenePath);

            Scene scene = EditorSceneManager.OpenScene(ProductionScenePath, OpenSceneMode.Single);
            ProductionVerticalSliceRoot marker = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ProductionVerticalSliceRoot>(true))
                .SingleOrDefault();
            if (marker == null) throw new InvalidOperationException("ProductionVerticalSliceRoot marker is missing.");
            ProductionEnvironmentRoot environment = marker.EnvironmentRoot != null
                ? marker.EnvironmentRoot.GetComponentInChildren<ProductionEnvironmentRoot>(true)
                : null;
            if (environment == null) throw new InvalidOperationException("Authored ProductionEnvironmentRoot is missing.");
            AirportCoreBenchmarkRoot[] benchmarkRoots = environment.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true);
            if (benchmarkRoots.Length != 1)
                throw new InvalidOperationException(
                    $"Expected one authored AirportCoreBenchmarkRoot, found {benchmarkRoots.Length}.");
            AirportCoreBenchmarkRoot benchmark = benchmarkRoots[0];
            if (!benchmark.TryValidateContract(out string benchmarkReport))
                throw new InvalidOperationException("Airport-core V4 contract failed: " + benchmarkReport);
            AircraftRunwayBlobShadow groundShadow = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AircraftRunwayBlobShadow>(true))
                .SingleOrDefault();
            if (groundShadow == null)
                throw new InvalidOperationException("Authored bounded aircraft runway shadow is missing.");
            AircraftReferenceFrameRig rig = marker.AircraftRig;
            if (rig == null) throw new InvalidOperationException("Authored aircraft reference-frame rig is missing.");
            if (marker.PilotSeatProfile == null) throw new InvalidOperationException("Authored pilot seat profile is missing.");
            ProductionAircraftLightingController cockpitLighting =
                rig.GetComponentInChildren<ProductionAircraftLightingController>(true);
            if (cockpitLighting == null)
                throw new InvalidOperationException("Authored production aircraft lighting controller is missing.");
            ProductionAircraftLightingReport lightingReport = cockpitLighting.ApplyAuthoredLightingNow();
            if (lightingReport == null || lightingReport.remainingRealtimeShadowCasterCount != 0 ||
                lightingReport.remainingRealtimeShadowReceiverCount != 0)
                throw new InvalidOperationException("Stable no-realtime-shadow cockpit lighting policy did not apply.");

            foreach (Camera existing in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)))
                existing.enabled = false;
            foreach (AudioListener listener in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AudioListener>(true)))
                listener.enabled = false;

            GameObject cameraObject = new GameObject("Production Visual QA Camera") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 24000f;
            camera.fieldOfView = 72f;
            camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.68f, 0.82f);
            camera.aspect = width / (float)height;

            report.architectureVersion = marker.AuthoredArchitectureVersion;
            report.productionEnvironmentContractPassed = environment.TryValidateContract(out report.productionEnvironmentContract);
            report.authoredHierarchyPassed = rig.ValidateHierarchy();
            report.singleEnabledXrOrigin = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<XROrigin>(true))
                .Count(origin => origin.isActiveAndEnabled) == 1;
            report.legacyComponentsFound = FindLegacyComponents(scene);
            report.legacyBootstrapAndUiSuppressed = report.legacyComponentsFound.Count == 0;
            report.defaultEyeLocal = marker.PilotSeatProfile != null
                ? marker.PilotSeatProfile.nominalEyeLocalPosition
                : rig.PilotSeatAnchor.localPosition;
            report.eyeToPanelDistanceMeters = marker.PilotSeatProfile != null
                ? marker.PilotSeatProfile.EyeToPanelForwardDistanceMeters()
                : -1f;

            Vector3 runway08 = environment.transform.TransformPoint(environment.runway08EndLocal);
            Vector3 runway26 = environment.transform.TransformPoint(environment.runway26EndLocal);
            Vector3 runwayForward = Vector3.ProjectOnPlane(runway26 - runway08, Vector3.up).normalized;
            if (runwayForward.sqrMagnitude < 0.5f) throw new InvalidOperationException("FAA runway endpoints are degenerate.");

            return new VisualContext
            {
                scene = scene,
                marker = marker,
                environment = environment,
                benchmark = benchmark,
                groundShadow = groundShadow,
                rig = rig,
                visibility = marker.VisibilityProfiles,
                animator = marker.ControlAnimator,
                camera = camera,
                originalAircraftPosition = rig.transform.position,
                originalAircraftRotation = rig.transform.rotation,
                runway08 = runway08,
                runway26 = runway26,
                runwayForward = runwayForward,
                runwayRight = Vector3.Cross(Vector3.up, runwayForward).normalized,
                runwayMidpoint = Vector3.Lerp(runway08, runway26, 0.5f)
            };
        }

        private static List<string> FindLegacyComponents(Scene scene)
        {
            HashSet<string> forbidden = new HashSet<string>(StringComparer.Ordinal)
            {
                "QuestFirstViewRuntimeRepair", "FirstViewPlaytestDiagnostics", "PlaytestHud",
                "CockpitInstrumentPanel", "TrainingModeController", "SceneryModeController",
                "MeshSceneryProvider", "SplatSceneryProvider", "QuestSplatRuntimeGateController"
            };
            List<string> found = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null || !forbidden.Contains(component.GetType().Name)) continue;
                    found.Add(component.GetType().Name + " @ " + HierarchyPath(component.transform));
                }
            }
            return found.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToList();
        }

        private static void RenderRequiredShots(
            VisualContext context,
            ProductionVisualQaReport report,
            string outputDir,
            int width,
            int height)
        {
            Transform aircraft = context.rig.transform;
            Quaternion runwayRotation = Quaternion.LookRotation(context.runwayForward, Vector3.up);
            Vector3 pilotEye = context.rig.PilotSeatAnchor.position;
            Quaternion pilotForward = aircraft.rotation;
            Bounds runwayBounds = context.environment.runwayPavement.GetComponent<Renderer>().bounds;
            Bounds waterBounds = context.environment.boulderReservoirSurface.GetComponent<Renderer>().bounds;
            Bounds hangarBounds = CombinedNamedRendererBounds(
                context.environment.airportContext,
                AirportCoreBenchmarkRoot.GameObjectName);

            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "01_default_cockpit", "Default cockpit", "cockpit", pilotEye, pilotForward, 72f, ViewProfile.Cockpit));
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "02_calibrated_cockpit", "Representative additive calibrated cockpit", "cockpit",
                pilotEye + aircraft.TransformVector(RepresentativeCalibrationOffset),
                pilotForward * Quaternion.Euler(0f, 1.5f, 0f), 72f, ViewProfile.Cockpit));
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "03_instrument_yoke", "Instrument and yoke readability", "cockpit",
                pilotEye + aircraft.TransformVector(new Vector3(0f, -0.025f, 0.015f)),
                pilotForward * Quaternion.Euler(9f, 0f, 0f), 56f, ViewProfile.Cockpit));
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "04_runway_from_cockpit", "Runway from default cockpit", "cockpit",
                pilotEye, pilotForward, 64f, ViewProfile.Cockpit));

            Vector3 runwayCloseCamera = runwayBounds.center - context.runwayForward * 80f + context.runwayRight * 8f + Vector3.up * 2.1f;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "05_runway_surface_markings", "Runway surface and controlled-depth markings", "runway",
                runwayCloseCamera, LookAt(runwayCloseCamera, runwayBounds.center + context.runwayForward * 120f), 45f, ViewProfile.Environment));

            Vector3 hangarTarget = hangarBounds.center;
            float airportExtent = Mathf.Max(hangarBounds.extents.x, hangarBounds.extents.z);
            Vector3 hangarCamera = hangarTarget
                                   - context.runwayForward * (airportExtent * 0.85f + 70f)
                                   + context.runwayRight * (airportExtent * 0.65f + 60f)
                                   + Vector3.up * (airportExtent * 0.38f + 60f);
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "06_apron_hangars", "Apron and hangars", "scenery",
                hangarCamera, LookAt(hangarCamera, hangarTarget), 52f, ViewProfile.Environment));

            Vector3 waterTarget = waterBounds.center;
            Vector3 waterCamera = waterTarget + new Vector3(460f, 320f, -520f);
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "07_boulder_reservoir", "Boulder Reservoir and shoreline", "water",
                waterCamera, LookAt(waterCamera, waterTarget), 50f, ViewProfile.Environment));

            Vector3 nearCamera = context.runwayMidpoint - context.runwayForward * 430f - context.runwayRight * 250f + Vector3.up * 280f;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "08_near_terrain", "Near production terrain", "scenery",
                nearCamera, LookAt(nearCamera, context.runwayMidpoint + context.runwayForward * 300f), 55f, ViewProfile.Environment));

            Vector3 patternCamera = context.runwayMidpoint - context.runwayRight * 900f + Vector3.up * 520f;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "09_pattern_altitude_macro", "Pattern-altitude unique macro terrain", "scenery",
                patternCamera, LookAt(patternCamera, context.runwayMidpoint + context.runwayRight * 500f), 58f, ViewProfile.Environment));

            Vector3 horizonCamera = context.runwayMidpoint + Vector3.up * 620f;
            Vector3 westHorizon = horizonCamera + Vector3.left * 12000f + Vector3.up * 300f;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "10_stable_mountain_horizon", "Immutable Front Range horizon", "mountain",
                horizonCamera, LookAt(horizonCamera, westHorizon), 52f, ViewProfile.Environment));

            Vector3 aircraftTarget = aircraft.position + Vector3.up * 1.1f;
            Vector3 externalCamera = aircraftTarget - aircraft.forward * 12f + aircraft.right * 8f + Vector3.up * 4.5f;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "11_external_aircraft_static", "External aircraft static", "aircraft",
                externalCamera, LookAt(externalCamera, aircraftTarget), 52f, ViewProfile.External));

            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "12_external_control_sweep", "External aircraft control sweep", "aircraft",
                aircraftTarget - aircraft.forward * 9f - aircraft.right * 10f + Vector3.up * 4f,
                LookAt(aircraftTarget - aircraft.forward * 9f - aircraft.right * 10f + Vector3.up * 4f, aircraftTarget),
                50f, ViewProfile.External,
                new AircraftControlState { aileron = 1f, elevator = 0.65f, flaps = 1f, throttle = 1f, mixture = 1f }));

            SetAircraftPose(context, Vector3.Lerp(context.runway08, context.runway26, 0.38f) + Vector3.up * 1.25f, runwayRotation);
            CaptureChase(context, report, outputDir, width, height,
                "13_takeoff_roll", "Takeoff roll", "runway", 54f,
                new AircraftControlState { elevator = 0.15f, throttle = 1f, mixture = 1f });

            SetAircraftPose(
                context,
                context.runway26 + context.runwayForward * 620f + Vector3.up * 125f,
                runwayRotation * Quaternion.Euler(-8f, 0f, 0f));
            CaptureChase(context, report, outputDir, width, height,
                "14_climb", "Initial climb", "aircraft", 54f,
                new AircraftControlState { elevator = 0.20f, throttle = 1f, mixture = 1f });

            Vector3 turnForward = Quaternion.AngleAxis(-28f, Vector3.up) * context.runwayForward;
            Quaternion turnRotation = Quaternion.AngleAxis(15f, turnForward) * Quaternion.LookRotation(turnForward, Vector3.up);
            SetAircraftPose(context, context.runway26 + context.runwayForward * 1250f - context.runwayRight * 260f + Vector3.up * 280f, turnRotation);
            CaptureChase(context, report, outputDir, width, height,
                "15_shallow_turn", "Shallow coordinated turn presentation", "aircraft", 54f,
                new AircraftControlState { aileron = -0.22f, elevator = 0.08f, rudder = -0.08f, throttle = 0.85f, mixture = 1f });

            Vector3 approachAircraft = context.runway08 - context.runwayForward * 720f + Vector3.up * 105f;
            SetAircraftPose(context, approachAircraft, runwayRotation * Quaternion.Euler(4f, 0f, 0f));
            Vector3 approachCamera = approachAircraft - context.runwayForward * 35f + context.runwayRight * 14f + Vector3.up * 10f;
            Vector3 approachTarget = approachAircraft + context.runwayForward * 160f - Vector3.up * 18f;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "16_approach", "Final approach toward authoritative runway", "runway",
                approachCamera, LookAt(approachCamera, approachTarget), 55f, ViewProfile.External,
                new AircraftControlState { elevator = 0.05f, throttle = 0.35f, mixture = 1f, flaps = 0.65f }));

            // Environment V1 views. These are deterministic presentation poses and
            // remain separate from flight-physics evidence.
            SetAircraftPose(context, context.originalAircraftPosition, context.originalAircraftRotation);
            Vector3 neutralPilotEye = context.rig.PilotSeatAnchor.position;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "17_sky_horizon_forward", "Photographic sky and forward horizon", "sky",
                neutralPilotEye, context.originalAircraftRotation * Quaternion.Euler(-8f, 0f, 0f),
                72f, ViewProfile.Cockpit));
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "18_sky_horizon_rearward", "Photographic sky and rearward horizon", "sky",
                neutralPilotEye + Vector3.up * 2f,
                context.originalAircraftRotation * Quaternion.Euler(-3f, 180f, 0f),
                72f, ViewProfile.Environment));

            Vector3 nearMidBoundary = context.runwayMidpoint + Vector3.right * 1825f;
            Vector3 nearMidCamera = nearMidBoundary + new Vector3(310f, 430f, -420f);
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "19_near_mid_terrain_seam", "Near/mid terrain overlap", "terrain_seam",
                nearMidCamera, LookAt(nearMidCamera, nearMidBoundary), 50f, ViewProfile.Environment));

            Vector3 midFarBoundary = context.runwayMidpoint + Vector3.right * 5500f;
            Vector3 midFarCamera = midFarBoundary + new Vector3(650f, 920f, -980f);
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "20_mid_far_terrain_seam", "Mid/far terrain overlap", "terrain_seam",
                midFarCamera, LookAt(midFarCamera, midFarBoundary), 48f, ViewProfile.Environment));

            Vector3 reservoirAircraft = waterTarget + new Vector3(0f, 430f, -920f);
            Vector3 reservoirHorizontal = Vector3.ProjectOnPlane(waterTarget - reservoirAircraft, Vector3.up).normalized;
            SetAircraftPose(context, reservoirAircraft, Quaternion.LookRotation(reservoirHorizontal, Vector3.up));
            Vector3 reservoirPilotEye = context.rig.PilotSeatAnchor.position;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "21_reservoir_cockpit", "Boulder Reservoir from the cockpit", "water",
                reservoirPilotEye, LookAt(reservoirPilotEye, waterTarget), 64f, ViewProfile.Cockpit));

            Vector3 reservoirAerial = waterTarget + new Vector3(-720f, 880f, -760f);
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "22_reservoir_aerial", "Boulder Reservoir aerial water read", "water",
                reservoirAerial, LookAt(reservoirAerial, waterTarget), 48f, ViewProfile.Environment));

            Vector3 rampCamera = hangarTarget
                                 - context.runwayForward * (airportExtent * 0.45f + 45f)
                                 + context.runwayRight * (airportExtent * 0.28f + 30f)
                                 + Vector3.up * (airportExtent * 0.14f + 22f);
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "23_airport_buildings_ramp", "Airport buildings and ramp scale cues", "airport",
                rampCamera, LookAt(rampCamera, hangarTarget), 48f, ViewProfile.Environment));

            Bounds parkedAircraftBounds = ClosestNamedRendererBounds(
                context.environment.transform,
                hangarTarget,
                "parked_aircraft_airport");
            // The three parked aircraft are the foreground scale reference. The
            // view looks south across the apron toward the explicitly authored
            // airport-perimeter cottonwoods, so both scenery features are large
            // enough for a full-resolution visual review instead of incidental
            // background specks.
            Vector3 scaleTarget = parkedAircraftBounds.center + new Vector3(-6f, 1.2f, -62f);
            Vector3 scaleCamera = parkedAircraftBounds.center + new Vector3(42f, 15f, 62f);
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                "24_vegetation_aircraft_scale", "Airport vegetation and parked-aircraft scale reference", "vegetation",
                scaleCamera, LookAt(scaleCamera, scaleTarget), 42f, ViewProfile.Environment));

            SetAircraftPose(context, context.originalAircraftPosition, context.originalAircraftRotation);
            context.animator?.ClearControlStateForTest();
            context.visibility?.SetCockpitView();
            RenderAirportCoreBenchmarkAbShots(context, report, outputDir, width, height);
        }

        private static void RenderAirportCoreBenchmarkAbShots(
            VisualContext context,
            ProductionVisualQaReport report,
            string outputDir,
            int width,
            int height)
        {
            AirportCoreBenchmarkRoot benchmark = context.benchmark;
            Bounds terminal = CombinedRendererBounds(benchmark.executiveTerminal.transform);
            Bounds hangar = CombinedRendererBounds(benchmark.type2HangarRow.transform);
            Bounds vegetation = CombinedRendererBounds(benchmark.vegetationHeroCluster);
            Bounds core = CombinedRendererBounds(benchmark.benchmarkContent);
            Bounds runway = context.environment.runwayPavement.GetComponent<Renderer>().bounds;
            Bounds reservoir = context.environment.boulderReservoirSurface.GetComponent<Renderer>().bounds;
            Bounds minorWater = MinorWaterBounds(benchmark, context.environment);
            string minorWaterTitle = context.environment.waterPresentationMode ==
                                     ProductionWaterPresentationMode.OrthophotoOnly
                ? "Minor water orthophoto-only hold"
                : "Minor-water terrain-mask effect";
            Vector3 north = Vector3.forward;
            Vector3 east = Vector3.right;

            List<BenchmarkShot> shots = new List<BenchmarkShot>
            {
                Benchmark("01_terminal_front", "Executive terminal front", "airport_core_terminal",
                    terminal.center + east * 58f + north * 8f + Vector3.up * 7f,
                    terminal.center + Vector3.up * 4.5f, 45f),
                Benchmark("02_terminal_apron_oblique", "Executive terminal apron oblique", "airport_core_terminal",
                    terminal.center + north * 52f + east * 46f + Vector3.up * 18f,
                    terminal.center + Vector3.up * 4f, 48f),
                Benchmark("03_terminal_aerial", "Executive terminal aerial", "airport_core_terminal",
                    terminal.center + north * 32f - east * 28f + Vector3.up * 78f,
                    terminal.center, 44f),
                Benchmark("04_type2_hangar_front", "Type 2 T-hangar row front", "airport_core_hangar",
                    hangar.center + east * 48f + north * 4f + Vector3.up * 7f,
                    hangar.center + Vector3.up * 3.5f, 47f),
                Benchmark("05_type2_hangar_aerial", "Type 2 T-hangar row aerial", "airport_core_hangar",
                    hangar.center + east * 54f - north * 38f + Vector3.up * 92f,
                    hangar.center, 43f),
                Benchmark("06_hangar_material_close", "Type 2 hangar material close-up", "airport_core_material",
                    hangar.center + east * 17f - north * 10f + Vector3.up * 3.2f,
                    hangar.center + Vector3.up * 3.2f, 39f),
                Benchmark("07_cleanplate_integration", "Terminal and clean-plate integration", "airport_core_cleanplate",
                    Vector3.Lerp(terminal.center, hangar.center, 0.42f) + east * 38f + north * 28f + Vector3.up * 72f,
                    Vector3.Lerp(terminal.center, hangar.center, 0.42f), 46f),
                Benchmark("08_baked_shadow_alignment", "Airport-core baked-shadow alignment", "airport_core_lighting",
                    terminal.center - east * 36f + north * 28f + Vector3.up * 9f,
                    terminal.center + Vector3.up * 2.5f, 44f),
                Benchmark("09_runway_surface_close", "Continuous runway hero material", "airport_core_runway",
                    runway.center - context.runwayForward * 115f + context.runwayRight * 9f + Vector3.up * 2.2f,
                    runway.center + context.runwayForward * 150f, 42f),
                Benchmark("10_runway_apron_overview", "Runway and airport-core apron overview", "airport_core_runway",
                    terminal.center + east * 145f + north * 135f + Vector3.up * 115f,
                    Vector3.Lerp(terminal.center, runway.center, 0.42f), 50f),
                BenchmarkCockpit("11_reservoir_cockpit", "Reservoir benchmark cockpit view", "airport_core_water",
                    reservoir.center + new Vector3(0f, 430f, -920f), reservoir.center, 64f),
                Benchmark("12_reservoir_aerial", "Reservoir benchmark aerial view", "airport_core_water",
                    reservoir.center + new Vector3(-720f, 880f, -760f), reservoir.center, 48f),
                Benchmark("13_minor_water_terrain", minorWaterTitle, "airport_core_water",
                    minorWater.center + new Vector3(36f, 28f, -42f), minorWater.center, 44f),
                Benchmark("14_vegetation_close", "Vegetation hero cluster close view", "airport_core_vegetation",
                    vegetation.center + east * 24f - north * 28f + Vector3.up * 5f,
                    vegetation.center + Vector3.up * 3f, 42f),
                Benchmark("15_vegetation_aerial", "Vegetation hero cluster aerial view", "airport_core_vegetation",
                    vegetation.center - east * 36f + north * 30f + Vector3.up * 70f,
                    vegetation.center, 44f),
                Benchmark("16_complete_overview", "Complete airport-core benchmark", "airport_core_overview",
                    core.center + east * 155f - north * 145f + Vector3.up * 155f,
                    core.center, 50f)
            };

            AirportCoreVariantState state = AirportCoreVariantState.Capture(benchmark);
            try
            {
                state.ApplyV2Baseline();
                foreach (BenchmarkShot shot in shots)
                    CaptureBenchmarkShot(context, report, outputDir, width, height, shot, "v4_before_", "V2 before — ");

                state.RestoreV4();
                foreach (BenchmarkShot shot in shots)
                    CaptureBenchmarkShot(context, report, outputDir, width, height, shot, "v4_after_", "V4 after — ");
            }
            finally
            {
                state.RestoreV4();
            }
        }

        private static BenchmarkShot Benchmark(
            string id,
            string title,
            string category,
            Vector3 camera,
            Vector3 target,
            float fov)
        {
            return new BenchmarkShot(id, title, category, camera, target, fov, false);
        }

        private static BenchmarkShot BenchmarkCockpit(
            string id,
            string title,
            string category,
            Vector3 aircraftPosition,
            Vector3 target,
            float fov)
        {
            return new BenchmarkShot(id, title, category, aircraftPosition, target, fov, true);
        }

        private static void CaptureBenchmarkShot(
            VisualContext context,
            ProductionVisualQaReport report,
            string outputDir,
            int width,
            int height,
            BenchmarkShot shot,
            string prefix,
            string titlePrefix)
        {
            if (!shot.cockpit)
            {
                Capture(context, report, outputDir, width, height, shot.Definition(prefix, titlePrefix));
                return;
            }

            Vector3 horizontal = Vector3.ProjectOnPlane(shot.target - shot.cameraOrAircraftPosition, Vector3.up).normalized;
            try
            {
                SetAircraftPose(context, shot.cameraOrAircraftPosition, Quaternion.LookRotation(horizontal, Vector3.up));
                Vector3 pilotEye = context.rig.PilotSeatAnchor.position;
                Capture(context, report, outputDir, width, height, new ShotDefinition(
                    prefix + shot.id,
                    titlePrefix + shot.title,
                    shot.category,
                    pilotEye,
                    LookAt(pilotEye, shot.target),
                    shot.fov,
                    ViewProfile.Cockpit));
            }
            finally
            {
                SetAircraftPose(context, context.originalAircraftPosition, context.originalAircraftRotation);
            }
        }

        private static void CaptureChase(
            VisualContext context,
            ProductionVisualQaReport report,
            string outputDir,
            int width,
            int height,
            string id,
            string title,
            string tag,
            float fov,
            AircraftControlState controls)
        {
            Transform aircraft = context.rig.transform;
            Vector3 target = aircraft.position + Vector3.up * 1f;
            Vector3 cameraPosition = target - aircraft.forward * 15f + aircraft.right * 7f + Vector3.up * 5f;
            Capture(context, report, outputDir, width, height, new ShotDefinition(
                id, title, tag, cameraPosition, LookAt(cameraPosition, target), fov, ViewProfile.External, controls));
        }

        private static void Capture(
            VisualContext context,
            ProductionVisualQaReport report,
            string outputDir,
            int width,
            int height,
            ShotDefinition shot)
        {
            Renderer[] environmentHiddenRenderers = Array.Empty<Renderer>();
            bool[] environmentHiddenStates = Array.Empty<bool>();
            string path = Path.Combine(outputDir, "screenshots", shot.id + ".png");
            Texture2D texture = null;
            try
            {
                if (shot.profile == ViewProfile.Cockpit) context.visibility?.SetCockpitView();
                else context.visibility?.SetExternalView();
                context.groundShadow?.UpdateShadowPoseNow();
                bool shadowRequired = shot.id == "11_external_aircraft_static" ||
                                      shot.id == "12_external_control_sweep" ||
                                      shot.id == "13_takeoff_roll";
                bool shadowReady = !shadowRequired ||
                                   (context.groundShadow?.BlobRenderer != null &&
                                    context.groundShadow.BlobRenderer.enabled);
                if (shot.profile == ViewProfile.Environment)
                {
                    IEnumerable<Renderer> renderers =
                        context.rig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true);
                    if (context.groundShadow?.BlobRenderer != null)
                        renderers = renderers.Concat(new[] { context.groundShadow.BlobRenderer });
                    environmentHiddenRenderers = renderers.Distinct().ToArray();
                    environmentHiddenStates = environmentHiddenRenderers.Select(renderer => renderer.enabled).ToArray();
                    foreach (Renderer renderer in environmentHiddenRenderers) renderer.enabled = false;
                }
                if (shot.controls != null)
                {
                    context.animator?.CaptureAuthoredRestPosesForValidation();
                    context.animator?.SetControlStateForTest(shot.controls);
                }
                else
                {
                    context.animator?.ClearControlStateForTest();
                }

                context.camera.transform.SetPositionAndRotation(shot.cameraPosition, shot.cameraRotation);
                context.camera.fieldOfView = shot.fov;
                texture = Render(context.camera, width, height, out EditorFrameRenderStats editorStats);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                ImageStats image = Analyze(texture);
                RenderBudgetSnapshot budget = QuestRenderBudgetAudit.Capture(context.camera);
                ProductionVisualQaShot result = new ProductionVisualQaShot
                {
                    id = shot.id,
                    title = shot.title,
                    category = shot.category,
                    screenshotPath = path,
                    cameraPosition = shot.cameraPosition,
                    cameraEuler = shot.cameraRotation.eulerAngles,
                    fov = shot.fov,
                    width = image.width,
                    height = image.height,
                    colorVariance = image.colorVariance,
                    nonBackgroundRatio = image.nonBackgroundRatio,
                    outsideViewRatio = image.outsideViewRatio,
                    editorRenderStats = editorStats,
                    estimatedDrawCalls = budget.estimatedInstancedDrawCalls,
                    estimatedVisibleTriangles = budget.estimatedFrustumTriangles,
                    sceneMaterialCount = budget.uniqueMaterialCount,
                    imageNotBlank = image.colorVariance > 18f && image.nonBackgroundRatio > 0.035f
                };
                if (!result.imageNotBlank) result.warnings.Add("Screenshot looks blank or solid.");
                if (shot.category == "cockpit" && image.outsideViewRatio < context.marker.PilotSeatProfile.minimumOutsideViewRatio)
                    result.warnings.Add($"Outside-view ratio {image.outsideViewRatio:0.000} is below {context.marker.PilotSeatProfile.minimumOutsideViewRatio:0.000}.");
                if (budget.estimatedInstancedDrawCalls > DrawCallTarget)
                    result.warnings.Add($"Estimated draw calls {budget.estimatedInstancedDrawCalls} exceed {DrawCallTarget}.");
                if (budget.estimatedFrustumTriangles > VisibleTriangleTarget)
                    result.warnings.Add($"Estimated visible triangles {budget.estimatedFrustumTriangles:N0} exceed {VisibleTriangleTarget:N0}.");
                if (!shadowReady)
                    result.warnings.Add("The bounded runway contact shadow was required but not active for this ground view.");
                result.passed = result.imageNotBlank &&
                                (shot.category != "cockpit" || image.outsideViewRatio >= context.marker.PilotSeatProfile.minimumOutsideViewRatio) &&
                                budget.estimatedInstancedDrawCalls <= DrawCallTarget &&
                                budget.estimatedFrustumTriangles <= VisibleTriangleTarget &&
                                shadowReady;
                report.shots.Add(result);
            }
            catch (Exception exception)
            {
                report.shots.Add(new ProductionVisualQaShot
                {
                    id = shot.id,
                    title = shot.title,
                    category = shot.category,
                    screenshotPath = path,
                    passed = false,
                    error = exception.Message
                });
            }
            finally
            {
                for (int index = 0; index < environmentHiddenRenderers.Length; index++)
                    environmentHiddenRenderers[index].enabled = environmentHiddenStates[index];
                if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        private static ProductionMountainSweepResult RunMountainSweep(VisualContext context)
        {
            MeshFilter far = context.environment.farTerrainMesh;
            Renderer renderer = far != null ? far.GetComponent<Renderer>() : null;
            ProductionMountainSweepResult result = new ProductionMountainSweepResult
            {
                rendererPath = renderer != null ? HierarchyPath(renderer.transform) : string.Empty,
                meshName = far != null && far.sharedMesh != null ? far.sharedMesh.name : string.Empty,
                meshInstanceId = far != null && far.sharedMesh != null ? far.sharedMesh.GetInstanceID() : 0,
                vertexCount = far != null && far.sharedMesh != null ? far.sharedMesh.vertexCount : 0,
                matrixHash = renderer != null ? renderer.localToWorldMatrix.GetHashCode() : 0,
                lodGroupCount = context.environment.immutableFarTerrain != null
                    ? context.environment.immutableFarTerrain.GetComponentsInChildren<LODGroup>(true).Length
                    : -1,
                distanceCullerCount = context.environment.immutableFarTerrain != null
                    ? context.environment.immutableFarTerrain.GetComponentsInChildren<MonoBehaviour>(true).Count(component =>
                        component != null && component.GetType().Name == "RealKbduBatchDistanceCuller")
                    : -1
            };

            Vector3 originalPosition = context.camera.transform.position;
            Quaternion originalRotation = context.camera.transform.rotation;
            RenderTexture previous = context.camera.targetTexture;
            RenderTexture probe = new RenderTexture(256, 144, 16, RenderTextureFormat.ARGB32);
            try
            {
                context.camera.transform.position = context.runwayMidpoint + Vector3.up * 620f;
                context.camera.targetTexture = probe;
                for (int yaw = -180; yaw <= 180; yaw += 15)
                {
                    context.camera.transform.rotation = context.originalAircraftRotation * Quaternion.Euler(0f, yaw, 0f);
                    context.camera.Render();
                    ProductionMountainSweepSample sample = new ProductionMountainSweepSample
                    {
                        yawDegrees = yaw,
                        meshInstanceId = far != null && far.sharedMesh != null ? far.sharedMesh.GetInstanceID() : 0,
                        vertexCount = far != null && far.sharedMesh != null ? far.sharedMesh.vertexCount : 0,
                        matrixHash = renderer != null ? renderer.localToWorldMatrix.GetHashCode() : 0,
                        rendererEnabled = renderer != null && renderer.enabled,
                        activeInHierarchy = renderer != null && renderer.gameObject.activeInHierarchy
                    };
                    result.samples.Add(sample);
                }
            }
            finally
            {
                context.camera.targetTexture = previous;
                context.camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                probe.Release();
                Object.DestroyImmediate(probe);
            }

            result.identityImmutable = result.meshInstanceId != 0 && result.samples.All(sample =>
                sample.meshInstanceId == result.meshInstanceId &&
                sample.vertexCount == result.vertexCount &&
                sample.matrixHash == result.matrixHash &&
                sample.rendererEnabled && sample.activeInHierarchy);
            result.passed = result.identityImmutable && result.lodGroupCount == 0 && result.distanceCullerCount == 0;
            result.limitation = "Machine identity/transform sweep only; perceived stereo silhouette stability requires a human wearing the Quest.";
            return result;
        }

        private static void FinalizeReport(VisualContext context, ProductionVisualQaReport report)
        {
            report.requiredShotCount = RequiredShotCount;
            report.maxEstimatedDrawCalls = report.shots.Count > 0 ? report.shots.Max(shot => shot.estimatedDrawCalls) : 0;
            report.maxEstimatedVisibleTriangles = report.shots.Count > 0 ? report.shots.Max(shot => shot.estimatedVisibleTriangles) : 0L;
            report.sceneMaterialCount = report.shots.Count > 0 ? report.shots.Max(shot => shot.sceneMaterialCount) : 0;
            report.runwayToTerrainMaximumGapMeters = context.environment.measuredRunwayToTerrainMaximumGapMeters;
            report.markingToRunwayMaximumGapMeters = context.environment.measuredMarkingToRunwayMaximumGapMeters;
            report.runwayCollisionSurfaceDisagreementMeters = context.environment.measuredCollisionSurfaceDisagreementMeters;
            report.waterTerrainMinimumSeparationMeters = context.environment.minimumWaterTerrainSeparationMeters;
            report.passed = report.errors.Count == 0 &&
                            report.shots.Count == RequiredShotCount &&
                            report.shots.All(shot => shot.passed) &&
                            report.productionEnvironmentContractPassed &&
                            report.authoredHierarchyPassed &&
                            report.singleEnabledXrOrigin &&
                            report.legacyBootstrapAndUiSuppressed &&
                            report.mountainSweep != null && report.mountainSweep.passed &&
                            report.maxEstimatedDrawCalls <= DrawCallTarget &&
                            report.maxEstimatedVisibleTriangles <= VisibleTriangleTarget &&
                            report.sceneMaterialCount <= SceneMaterialTarget;
            if (report.sceneMaterialCount > SceneMaterialTarget)
                report.errors.Add($"Scene material count {report.sceneMaterialCount} exceeds planning target {SceneMaterialTarget}.");
            report.classification = report.passed
                ? "PASS_MACHINE_VISUAL_QA_HUMAN_HEADSET_REQUIRED"
                : "FAIL_MACHINE_QA";
        }

        private static Texture2D Render(Camera camera, int width, int height, out EditorFrameRenderStats stats)
        {
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            stats = new EditorFrameRenderStats();
            try
            {
                camera.targetTexture = target;
                camera.Render();
                stats.available = UnityStats.drawCalls > 0 || UnityStats.triangles > 0;
                stats.drawCalls = UnityStats.drawCalls;
                stats.batches = UnityStats.batches;
                stats.setPassCalls = UnityStats.setPassCalls;
                stats.triangles = UnityStats.triangles;
                stats.vertices = UnityStats.vertices;
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static ImageStats Analyze(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            Color32 background = pixels.Length > 0 ? pixels[pixels.Length - 1] : new Color32(0, 0, 0, 255);
            double red = 0d, green = 0d, blue = 0d;
            int nonBackground = 0, outside = 0;
            foreach (Color32 pixel in pixels)
            {
                red += pixel.r;
                green += pixel.g;
                blue += pixel.b;
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 36)
                    nonBackground++;
                bool sky = pixel.b > 120 && pixel.g > 105 && pixel.b > pixel.r + 16;
                bool terrain = pixel.g > 60 && pixel.r > 35 && pixel.b < 190;
                if (sky || terrain) outside++;
            }
            int count = Math.Max(1, pixels.Length);
            red /= count;
            green /= count;
            blue /= count;
            double variance = 0d;
            foreach (Color32 pixel in pixels)
            {
                variance += Math.Pow(pixel.r - red, 2d);
                variance += Math.Pow(pixel.g - green, 2d);
                variance += Math.Pow(pixel.b - blue, 2d);
            }
            return new ImageStats
            {
                width = texture.width,
                height = texture.height,
                colorVariance = (float)(variance / (count * 3d)),
                nonBackgroundRatio = nonBackground / (float)count,
                outsideViewRatio = outside / (float)count
            };
        }

        private static string BuildContactSheet(ProductionVisualQaReport report, string outputDir)
        {
            return BuildContactSheet(report.shots, outputDir, "visual_qa_contact_sheet.png");
        }

        private static string BuildContactSheet(
            IReadOnlyList<ProductionVisualQaShot> shots,
            string outputDir,
            string fileName)
        {
            const int columns = 4;
            const int cellWidth = 320;
            const int cellHeight = 180;
            int rows = Mathf.Max(1, Mathf.CeilToInt(shots.Count / (float)columns));
            Texture2D sheet = new Texture2D(columns * cellWidth, rows * cellHeight, TextureFormat.RGBA32, false);
            Color32[] background = Enumerable.Repeat(new Color32(16, 18, 22, 255), sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(background);
            try
            {
                for (int index = 0; index < shots.Count; index++)
                {
                    string screenshotPath = shots[index].screenshotPath;
                    if (!File.Exists(screenshotPath)) continue;
                    Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        source.LoadImage(File.ReadAllBytes(screenshotPath));
                        int column = index % columns;
                        int row = rows - 1 - index / columns;
                        BlitScaled(source, sheet, column * cellWidth, row * cellHeight, cellWidth, cellHeight);
                    }
                    finally
                    {
                        Object.DestroyImmediate(source);
                    }
                }
                sheet.Apply();
                string path = Path.Combine(outputDir, fileName);
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                return path;
            }
            finally
            {
                Object.DestroyImmediate(sheet);
            }
        }

        private static void BlitScaled(Texture2D source, Texture2D target, int offsetX, int offsetY, int width, int height)
        {
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = source.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height);
            target.SetPixels(offsetX, offsetY, width, height, pixels);
        }

        private static void WriteReportFiles(ProductionVisualQaReport report, string outputDir)
        {
            File.WriteAllText(Path.Combine(outputDir, "visual_qa_report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(outputDir, "visual_qa_summary.md"), BuildMarkdown(report));
            File.WriteAllText(Path.Combine(outputDir, "visual_qa_shots.csv"), BuildCsv(report));
            if (report.mountainSweep != null)
                File.WriteAllText(Path.Combine(outputDir, "production_mountain_camera_sweep.json"), JsonUtility.ToJson(report.mountainSweep, true));
        }

        private static string BuildMarkdown(ProductionVisualQaReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Production Vertical Slice Visual QA");
            builder.AppendLine();
            builder.AppendLine($"- Classification: `{report.classification}`");
            builder.AppendLine($"- Scene: `{report.scenePath}`; architecture `{report.architectureVersion}`");
            builder.AppendLine($"- Captures: {report.shots.Count}/{report.requiredShotCount}; contact sheet `{report.contactSheetPath}`");
            builder.AppendLine($"- Airport-core A/B sheets: `{report.airportCoreBeforeContactSheetPath}` / `{report.airportCoreAfterContactSheetPath}`");
            builder.AppendLine($"- Authored hierarchy/environment contract: {report.authoredHierarchyPassed}/{report.productionEnvironmentContractPassed}");
            builder.AppendLine($"- One enabled XR Origin: {report.singleEnabledXrOrigin}");
            builder.AppendLine($"- Legacy bootstrap/UI suppressed: {report.legacyBootstrapAndUiSuppressed}");
            builder.AppendLine($"- Default eye / eye-to-panel: {report.defaultEyeLocal} / {report.eyeToPanelDistanceMeters:0.000} m");
            builder.AppendLine($"- Max estimated draw calls/visible triangles/materials: {report.maxEstimatedDrawCalls}/{report.maxEstimatedVisibleTriangles:N0}/{report.sceneMaterialCount}");
            builder.AppendLine($"- Runway gaps terrain/marking/collider: {report.runwayToTerrainMaximumGapMeters:0.0000}/{report.markingToRunwayMaximumGapMeters:0.0000}/{report.runwayCollisionSurfaceDisagreementMeters:0.000000} m");
            builder.AppendLine($"- Mountain sweep: {report.mountainSweep?.passed}; {report.mountainSweep?.samples.Count ?? 0} rendered headings");
            builder.AppendLine($"- Human headset acceptance: `{report.humanHeadsetAcceptance}`");
            builder.AppendLine();
            builder.AppendLine("| View | Pass | Draw estimate | Visible tris | Outside | Notes |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: | --- |");
            foreach (ProductionVisualQaShot shot in report.shots)
            {
                string notes = shot.warnings.Count > 0 ? string.Join("; ", shot.warnings) : "ok";
                builder.AppendLine($"| `{shot.id}` | {shot.passed} | {shot.estimatedDrawCalls} | {shot.estimatedVisibleTriangles:N0} | {shot.outsideViewRatio:0.000} | {notes} |");
            }
            builder.AppendLine();
            builder.AppendLine("## Evidence boundaries");
            builder.AppendLine();
            builder.AppendLine("- Flight-phase images are visual presentation poses, not flight-dynamics evidence.");
            builder.AppendLine("- Static screenshots and machine identity checks do not establish stereo headset quality.");
            builder.AppendLine("- No user was available, so human headset acceptance is explicitly not run.");
            if (report.errors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("## Errors");
                builder.AppendLine();
                foreach (string error in report.errors) builder.AppendLine("- " + error.Replace('\n', ' '));
            }
            return builder.ToString();
        }

        private static string BuildCsv(ProductionVisualQaReport report)
        {
            StringBuilder builder = new StringBuilder("id,title,category,passed,path,draw_calls,visible_triangles,materials,outside_ratio,warnings\n");
            foreach (ProductionVisualQaShot shot in report.shots)
            {
                builder.Append(Csv(shot.id)).Append(',').Append(Csv(shot.title)).Append(',').Append(Csv(shot.category)).Append(',')
                    .Append(shot.passed).Append(',').Append(Csv(shot.screenshotPath)).Append(',')
                    .Append(shot.estimatedDrawCalls).Append(',').Append(shot.estimatedVisibleTriangles).Append(',')
                    .Append(shot.sceneMaterialCount).Append(',')
                    .Append(shot.outsideViewRatio.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(string.Join("; ", shot.warnings))).AppendLine();
            }
            return builder.ToString();
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        private static int ReadInt(string key, int fallback) => int.TryParse(System.Environment.GetEnvironmentVariable(key), out int value) ? value : fallback;
        private static Quaternion LookAt(Vector3 from, Vector3 to) => Quaternion.LookRotation((to - from).normalized, Vector3.up);
        private static void SetAircraftPose(VisualContext context, Vector3 position, Quaternion rotation) => context.rig.transform.SetPositionAndRotation(position, rotation);

        private static Bounds ClosestNamedRendererBounds(Transform root, Vector3 reference, params string[] tokens)
        {
            Renderer renderer = root != null
                ? root.GetComponentsInChildren<Renderer>(true)
                    .Where(candidate => tokens.Any(token =>
                        HierarchyPath(candidate.transform).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(candidate => (candidate.bounds.center - reference).sqrMagnitude)
                    .FirstOrDefault()
                : null;
            return renderer != null ? renderer.bounds : new Bounds(reference + Vector3.right * 180f, new Vector3(80f, 20f, 80f));
        }

        private static Bounds CombinedNamedRendererBounds(Transform root, params string[] tokens)
        {
            Renderer[] renderers = root != null
                ? root.GetComponentsInChildren<Renderer>(true)
                    .Where(candidate => tokens.Any(token =>
                        HierarchyPath(candidate.transform).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToArray()
                : Array.Empty<Renderer>();
            if (renderers.Length == 0)
                throw new InvalidOperationException("Required authored renderer hierarchy is missing: " + string.Join(",", tokens));
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static string HierarchyPath(Transform transform)
        {
            Stack<string> names = new Stack<string>();
            for (Transform current = transform; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names.ToArray());
        }

        private enum ViewProfile { Cockpit, External, Environment }

        private sealed class VisualContext
        {
            public Scene scene;
            public ProductionVerticalSliceRoot marker;
            public ProductionEnvironmentRoot environment;
            public AirportCoreBenchmarkRoot benchmark;
            public AircraftRunwayBlobShadow groundShadow;
            public AircraftReferenceFrameRig rig;
            public AircraftVisibilityProfileController visibility;
            public ProductionAircraftControlAnimator animator;
            public Camera camera;
            public Vector3 originalAircraftPosition;
            public Quaternion originalAircraftRotation;
            public Vector3 runway08;
            public Vector3 runway26;
            public Vector3 runwayForward;
            public Vector3 runwayRight;
            public Vector3 runwayMidpoint;
        }

        private readonly struct BenchmarkShot
        {
            public readonly string id, title, category;
            public readonly Vector3 cameraOrAircraftPosition, target;
            public readonly float fov;
            public readonly bool cockpit;

            public BenchmarkShot(
                string id,
                string title,
                string category,
                Vector3 cameraOrAircraftPosition,
                Vector3 target,
                float fov,
                bool cockpit)
            {
                this.id = id;
                this.title = title;
                this.category = category;
                this.cameraOrAircraftPosition = cameraOrAircraftPosition;
                this.target = target;
                this.fov = fov;
                this.cockpit = cockpit;
            }

            public ShotDefinition Definition(string prefix, string titlePrefix)
            {
                return new ShotDefinition(prefix + id, titlePrefix + title, category,
                    cameraOrAircraftPosition, LookAt(cameraOrAircraftPosition, target), fov, ViewProfile.Environment);
            }
        }

        private sealed class AirportCoreVariantState
        {
            private readonly AirportCoreBenchmarkRoot root;
            private readonly Material v4RunwayMaterial;
            private readonly Material v4ReservoirMaterial;
            private readonly Material terrainMaterial;
            private readonly bool waterEffectsDeferred;
            private readonly bool hasMinorWaterStrength;
            private readonly float v4MinorWaterStrength;
            private readonly Light globalSun;
            private readonly Quaternion v4SunRotation;
            private readonly Color v4SunColor;
            private readonly float v4SunIntensity;
            private readonly Mesh[] v2ContextMeshes;
            private readonly Mesh[] v4ContextMeshes;

            private AirportCoreVariantState(AirportCoreBenchmarkRoot root)
            {
                this.root = root;
                v4RunwayMaterial = root.authoritativeRunway.sharedMaterial;
                v4ReservoirMaterial = root.boulderReservoir.sharedMaterial;
                waterEffectsDeferred = root.manifest != null &&
                                       root.manifest.waterEffectsDeferredToOrthophoto;
                terrainMaterial = root.minorWaterTerrainEffect.sharedMaterial;
                if (terrainMaterial == null)
                    throw new InvalidOperationException("Airport-core terrain A/B material is missing.");
                hasMinorWaterStrength = terrainMaterial.HasProperty("_AirportCoreMinorWaterStrength");
                v4MinorWaterStrength = hasMinorWaterStrength
                    ? terrainMaterial.GetFloat("_AirportCoreMinorWaterStrength")
                    : 0f;
                Light[] sceneSuns = Resources.FindObjectsOfTypeAll<Light>()
                    .Where(item => item != null && item.gameObject.scene.IsValid() &&
                                   item.type == LightType.Directional && item != root.benchmarkSun)
                    .ToArray();
                if (sceneSuns.Length != 1)
                    throw new InvalidOperationException(
                        $"Expected one global directional sun for V4 A/B, found {sceneSuns.Length}.");
                globalSun = sceneSuns[0];
                v4SunRotation = globalSun.transform.rotation;
                v4SunColor = globalSun.color;
                v4SunIntensity = globalSun.intensity;
                v2ContextMeshes = LoadContextMeshes(root.v2ContextOriginalMeshPaths, "V2 original");
                v4ContextMeshes = LoadContextMeshes(root.v4ContextFilteredMeshPaths, "V4 filtered");
            }

            public static AirportCoreVariantState Capture(AirportCoreBenchmarkRoot root)
            {
                return new AirportCoreVariantState(root);
            }

            public void ApplyV2Baseline()
            {
                root.benchmarkContent.gameObject.SetActive(false);
                ApplyContextMeshes(v2ContextMeshes);
                root.authoritativeRunway.sharedMaterial = root.v2RunwayMaterial;
                if (!waterEffectsDeferred)
                    root.boulderReservoir.sharedMaterial = root.v2ReservoirMaterial;
                if (hasMinorWaterStrength)
                    terrainMaterial.SetFloat("_AirportCoreMinorWaterStrength", 0f);
                globalSun.transform.rotation = root.v2SunRotation;
                globalSun.color = root.v2SunColor;
                globalSun.intensity = root.v2SunIntensity;
            }

            public void RestoreV4()
            {
                root.benchmarkContent.gameObject.SetActive(true);
                ApplyContextMeshes(v4ContextMeshes);
                root.authoritativeRunway.sharedMaterial = v4RunwayMaterial;
                if (!waterEffectsDeferred)
                    root.boulderReservoir.sharedMaterial = v4ReservoirMaterial;
                if (hasMinorWaterStrength)
                    terrainMaterial.SetFloat("_AirportCoreMinorWaterStrength", v4MinorWaterStrength);
                globalSun.transform.rotation = v4SunRotation;
                globalSun.color = v4SunColor;
                globalSun.intensity = v4SunIntensity;
            }

            private void ApplyContextMeshes(Mesh[] meshes)
            {
                LOD[] lods = root.filteredV2Context.GetLODs();
                if (lods.Length != 3 || meshes == null || meshes.Length != 3)
                    throw new InvalidOperationException("Airport-core context A/B requires three LOD meshes.");
                for (int index = 0; index < 3; index++)
                {
                    Renderer renderer = lods[index].renderers.SingleOrDefault();
                    MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
                    if (filter == null || meshes[index] == null)
                        throw new InvalidOperationException("Airport-core context A/B LOD binding is incomplete.");
                    filter.sharedMesh = meshes[index];
                }
            }

            private static Mesh[] LoadContextMeshes(string[] paths, string label)
            {
                if (paths == null || paths.Length != 3)
                    throw new InvalidOperationException(label + " context paths are incomplete.");
                Mesh[] meshes = paths.Select(AssetDatabase.LoadAssetAtPath<Mesh>).ToArray();
                if (meshes.Any(item => item == null))
                    throw new InvalidOperationException(label + " context mesh asset is missing.");
                return meshes;
            }
        }

        private static Bounds CombinedRendererBounds(Transform root)
        {
            Renderer[] renderers = root != null
                ? root.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            if (renderers.Length == 0)
                throw new InvalidOperationException("Required benchmark renderer hierarchy is empty: " + root?.name);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static Bounds MinorWaterBounds(
            AirportCoreBenchmarkRoot root,
            ProductionEnvironmentRoot environment)
        {
            Material material = root.minorWaterTerrainEffect != null
                ? root.minorWaterTerrainEffect.sharedMaterial
                : null;
            bool orthophotoOnly = environment != null && environment.waterPresentationMode ==
                                  ProductionWaterPresentationMode.OrthophotoOnly;
            if (!orthophotoOnly)
            {
                if (material == null || !material.HasProperty("_AirportCoreMinorWaterNearWorldMinInvSize"))
                    throw new InvalidOperationException("Minor-water world-space binding is missing.");
                Vector4 binding = material.GetVector("_AirportCoreMinorWaterNearWorldMinInvSize");
                if (binding.z <= 0f || binding.w <= 0f)
                    throw new InvalidOperationException(
                        "Minor-water world-space binding has an invalid inverse size.");
            }
            else if (material == null)
            {
                throw new InvalidOperationException(
                    "Orthophoto-only minor-water QA requires the authoritative terrain renderer.");
            }
            // Focus the dedicated ground-level shot on the existing OSM-derived drainage
            // feature southwest of the airport. In orthophoto-only mode this deliberately
            // proves the photographed feature remains coherent without a synthetic overlay.
            Vector3 size = new Vector3(110f, 2f, 70f);
            Vector3 center = new Vector3(-555f,
                root.minorWaterTerrainEffect.bounds.center.y + 0.2f,
                -490f);
            return new Bounds(center, size);
        }

        private readonly struct ShotDefinition
        {
            public readonly string id, title, category;
            public readonly Vector3 cameraPosition;
            public readonly Quaternion cameraRotation;
            public readonly float fov;
            public readonly ViewProfile profile;
            public readonly AircraftControlState controls;

            public ShotDefinition(string id, string title, string category, Vector3 cameraPosition, Quaternion cameraRotation,
                float fov, ViewProfile profile, AircraftControlState controls = null)
            {
                this.id = id;
                this.title = title;
                this.category = category;
                this.cameraPosition = cameraPosition;
                this.cameraRotation = cameraRotation;
                this.fov = fov;
                this.profile = profile;
                this.controls = controls;
            }
        }

        private struct ImageStats
        {
            public int width, height;
            public float colorVariance, nonBackgroundRatio, outsideViewRatio;
        }

        [Serializable]
        public sealed class ProductionVisualQaReport
        {
            public int schemaVersion = 1;
            public string generatedUtc, unityVersion, scenePath, architectureVersion, outputDirectory, contactSheetPath;
            public string airportCoreBeforeContactSheetPath, airportCoreAfterContactSheetPath;
            public int renderWidth, renderHeight, requiredShotCount;
            public bool productionEnvironmentContractPassed, authoredHierarchyPassed, singleEnabledXrOrigin, legacyBootstrapAndUiSuppressed;
            public string productionEnvironmentContract;
            public List<string> legacyComponentsFound = new List<string>();
            public Vector3 defaultEyeLocal;
            public float eyeToPanelDistanceMeters;
            public int maxEstimatedDrawCalls, sceneMaterialCount;
            public long maxEstimatedVisibleTriangles;
            public float runwayToTerrainMaximumGapMeters, markingToRunwayMaximumGapMeters, runwayCollisionSurfaceDisagreementMeters;
            public float waterTerrainMinimumSeparationMeters;
            public ProductionMountainSweepResult mountainSweep;
            public string humanHeadsetAcceptance, flightPhaseImageSemantics, classification;
            public bool passed;
            public List<ProductionVisualQaShot> shots = new List<ProductionVisualQaShot>();
            public List<string> errors = new List<string>();
        }

        [Serializable]
        public sealed class ProductionVisualQaShot
        {
            public string id, title, category, screenshotPath, error;
            public Vector3 cameraPosition, cameraEuler;
            public float fov;
            public int width, height;
            public float colorVariance, nonBackgroundRatio, outsideViewRatio;
            public EditorFrameRenderStats editorRenderStats;
            public int estimatedDrawCalls, sceneMaterialCount;
            public long estimatedVisibleTriangles;
            public bool imageNotBlank, passed;
            public List<string> warnings = new List<string>();
        }

        [Serializable]
        public sealed class EditorFrameRenderStats
        {
            public bool available;
            public int batches, drawCalls, setPassCalls, triangles, vertices;
        }

        [Serializable]
        public sealed class ProductionMountainSweepResult
        {
            public string rendererPath, meshName, limitation;
            public int meshInstanceId, vertexCount, matrixHash, lodGroupCount, distanceCullerCount;
            public bool identityImmutable, passed;
            public List<ProductionMountainSweepSample> samples = new List<ProductionMountainSweepSample>();
        }

        [Serializable]
        public struct ProductionMountainSweepSample
        {
            public int yawDegrees, meshInstanceId, vertexCount, matrixHash;
            public bool rendererEnabled, activeInHierarchy;
        }

        [Serializable]
        public sealed class ProductionArtifactTraceReport
        {
            public string generatedUtc, scenePath, waterSurfacePath, waterBankPath;
            public List<ArtifactPoseTrace> traces = new List<ArtifactPoseTrace>();
            public List<RendererInventoryEntry> largeOrTallRenderers = new List<RendererInventoryEntry>();
        }

        [Serializable]
        public sealed class ArtifactPoseTrace
        {
            public string id;
            public Vector3 cameraPosition, cameraEuler;
            public Rect normalizedRoi;
            public int candidateCount;
            public List<RendererContribution> topContributors = new List<RendererContribution>();
        }

        [Serializable]
        public sealed class RendererContribution
        {
            public string hierarchyPath, rendererName, materialName, shaderName;
            public Vector3 boundsCenter, boundsSize;
            public float meanAbsoluteRgbDifference;

            public RendererContribution(Renderer renderer, float difference)
            {
                hierarchyPath = HierarchyPath(renderer.transform);
                rendererName = renderer.name;
                materialName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : string.Empty;
                shaderName = renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                    ? renderer.sharedMaterial.shader.name
                    : string.Empty;
                boundsCenter = renderer.bounds.center;
                boundsSize = renderer.bounds.size;
                meanAbsoluteRgbDifference = difference;
            }
        }

        [Serializable]
        public sealed class RendererInventoryEntry
        {
            public string hierarchyPath, materialName;
            public Vector3 boundsCenter, boundsSize;

            public RendererInventoryEntry(Renderer renderer)
            {
                hierarchyPath = HierarchyPath(renderer.transform);
                materialName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : string.Empty;
                boundsCenter = renderer.bounds.center;
                boundsSize = renderer.bounds.size;
            }
        }
    }
}
